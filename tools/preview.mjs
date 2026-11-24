#!/usr/bin/env node
/**
 * Three.js 版の連番キャプチャ（Unity 版 tools/preview.py に対応）。
 *
 * Playwright でヘッドレス Chrome を起動し、シナリオ名を受け取って
 * N フレームを interval 秒ごとに `f_000.png` 形式で保存する。
 * 実時間は待たない。ページ側の `window.__portCapture.step(1)` で
 * 固定 dt 進めてから `render()` し、canvas を PNG で読む。
 *
 * 使い方（リポジトリ直下で実行）:
 *   node tools/preview.mjs --list
 *   node tools/preview.mjs frontpin
 *   node tools/preview.mjs belly --frames 40 --interval 0.05 --audit
 *   node tools/preview.mjs --reel
 *
 * 出力先: Verify/preview/<scenario>-<timestamp>/
 *   f_000.png ... 生の連番
 *   sheet.png     コマ送り（--sheet-step で間引き）
 *   state.json    最終 state()（clock / scenario / frame / rig / feel）
 *
 * Chromium が入っていない場合はエラーにせず「要 chromium インストール」と
 * 報告して終了コード 0 で返す（lead が判断する）。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';
import sharp from 'sharp';
import { SCENARIOS, REEL_SCENARIOS, SCENARIO_NAMES, listScenarios, toQuery } from './scenarios.mjs';
import { report as auditReport } from './motion_audit.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PREVIEW_ROOT = path.join(ROOT, 'Verify', 'preview');
const SIZE = { width: 1280, height: 720 };

function stamp() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

function help() {
  console.log(`Three.js 版の連番キャプチャ（Unity 版 preview.py の移植）。

使い方:
  node tools/preview.mjs --list
  node tools/preview.mjs <scenario> [--frames N] [--interval S] [--audit] [--compare DIR]
  node tools/preview.mjs --reel

出力: Verify/preview/<scenario>-<timestamp>/f_000.png ほか

Chromium が無い場合は「要 chromium インストール」と表示して終了コード 0 で返す。`);
}

/** Chromium の有無を確認する。無ければメッセージを出して false。 */
function chromiumMissing(chromium) {
  try {
    const exe = chromium.executablePath();
    if (!exe || !fs.existsSync(exe)) {
      console.log('要 chromium インストール: npx playwright install chromium');
      return true;
    }
  } catch {
    console.log('要 chromium インストール: npx playwright install chromium');
    return true;
  }
  return false;
}

/**
 * Vite 開発サーバを起動して 1 ページだけ開く。
 * @param {(page: import('playwright').Page, url: string) => Promise<number>} fn
 */
async function withPage(fn) {
  const { chromium } = await import('playwright');
  if (chromiumMissing(chromium)) return 0;

  const server = await createServer({
    root: ROOT,
    configFile: path.join(ROOT, 'vite.config.ts'),
    // 撮影は 1 ページを読むだけなので watcher は不要。並行する他エージェントの
    // 一時ファイル (Vitest の .tmpdir 等) を Vite が watch して EBUSY になるのを防ぐ。
    server: { host: '127.0.0.1', port: 0, strictPort: false, watch: null },
    logLevel: 'error',
  });
  let browser;
  try {
    await server.listen();
    const addr = server.httpServer?.address();
    const port = typeof addr === 'object' && addr ? addr.port : 5174;
    browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({
      viewport: SIZE,
      deviceScaleFactor: 1,
    });
    const page = await context.newPage();
    page.on('pageerror', (e) => console.log(`  ! page error: ${e.message}`));
    return await fn(page, `http://127.0.0.1:${port}/`);
  } finally {
    if (browser) await browser.close();
    await server.close();
  }
}

/**
 * 1 シナリオを撮る。撮れた枚数を返す。
 * @param {import('playwright').Page} page
 * @param {string} url
 * @param {string} name
 * @param {number} frames
 * @param {number} interval
 * @param {string} outDir
 */
async function capture(page, url, name, frames, interval, outDir) {
  const spec = SCENARIOS[name];
  fs.mkdirSync(outDir, { recursive: true });

  const query = new URLSearchParams(toQuery(spec));
  query.set('capture', '1');                  // rAF ループを止め、step() を唯一の進行源にする
  query.set('interval', String(interval));
  const target = `${url}?${query.toString()}`;

  console.log(`[run] ${name}: ${frames} 枚 / ${interval} 秒間隔`);
  await page.goto(target, { waitUntil: 'load' });
  await page.waitForFunction(() => typeof window.__portCapture?.step === 'function', null, { timeout: 30000 });
  // 明示的にシナリオを選ぶ（既定の idle クエリ指定より優先）。
  await page.evaluate((n) => window.__portCapture.scenario(n), name);

  for (let i = 0; i < frames; i++) {
    await page.evaluate(() => {
      const c = window.__portCapture;
      c.step(1);
      c.render();
    });
    const file = path.join(outDir, `f_${String(i).padStart(3, '0')}.png`);
    await page.locator('#app').screenshot({ path: file, animations: 'disabled' });
    if ((i + 1) % 50 === 0) console.log(`  ${i + 1}/${frames}`);
  }

  const state = await page.evaluate(() => window.__portCapture.state());
  fs.writeFileSync(path.join(outDir, 'state.json'), JSON.stringify(state, null, 2));
  await makeSheet(outDir);
  console.log(`  ${frames} 枚 → ${outDir}`);
  return frames;
}

/** 0.25 秒ごとのコマ送りシート（Unity 版 sheet.png 相当）。 */
async function makeSheet(dir, step = 5, cols = 6, width = 320) {
  const files = fs.readdirSync(dir)
    .filter((f) => /^f_\d+\.png$/.test(f))
    .sort();
  if (!files.length) return;
  const picked = files.filter((_, i) => i % Math.max(1, step) === 0);
  const height = Math.round(width * 9 / 16);
  const labelH = 14;
  const rows = Math.ceil(picked.length / cols);
  const tiles = [];
  for (const [i, file] of picked.entries()) {
    const idx = Number(path.basename(file, '.png').split('_')[1]);
    const image = await sharp(path.join(dir, file))
      .resize(width, height, { fit: 'fill', kernel: sharp.kernel.lanczos3 })
      .toBuffer();
    const x = (i % cols) * width;
    const y = Math.floor(i / cols) * (height + labelH);
    const label = Buffer.from(
      `<svg width="${width}" height="${labelH}">` +
      `<rect width="${width}" height="${labelH}" fill="#0c0c0c"/>` +
      `<text x="3" y="11" fill="#ffee00" font-family="monospace" font-size="11">` +
      `#${idx} ${(idx * 0.05).toFixed(2)}s</text></svg>`,
    );
    tiles.push({ input: label, left: x, top: y });
    tiles.push({ input: image, left: x, top: y + labelH });
  }
  const out = path.join(dir, 'sheet.png');
  await sharp({
    create: {
      width: cols * width,
      height: rows * (height + labelH),
      channels: 3,
      background: { r: 12, g: 12, b: 12 },
    },
  }).composite(tiles).png().toFile(out);
  console.log(`  sheet: ${path.basename(out)}`);
}

/** BEFORE / AFTER を同じフレーム番号で横に並べる（Unity 版 compare.png 相当）。 */
async function makeCompare(beforeDir, afterDir, out) {
  const list = (d) => fs.readdirSync(d).filter((f) => /^f_\d+\.png$/.test(f)).sort();
  const old = list(beforeDir);
  const newer = list(afterDir);
  if (!old.length || !newer.length) {
    console.log('  ! 比較できない（フレームが無い）');
    return;
  }
  const picks = 4;
  const n = Math.min(old.length, newer.length);
  const idxs = Array.from({ length: picks }, (_, i) => Math.min(n - 1, Math.floor(n * (i + 1) / (picks + 1))));
  const width = 480;
  const height = Math.round(width * 9 / 16);
  const labelH = 14;
  const tiles = [];
  for (const [col, idx] of idxs.entries()) {
    for (const [row, dir] of [beforeDir, afterDir].entries()) {
      const f = row === 0 ? old[idx] : newer[idx];
      const image = await sharp(path.join(dir, f))
        .resize(width, height, { fit: 'fill', kernel: sharp.kernel.lanczos3 })
        .toBuffer();
      const x = col * width;
      const y = row * (height + labelH);
      const color = row === 0 ? '#ff8282' : '#82ff82';
      const label = Buffer.from(
        `<svg width="${width}" height="${labelH}">` +
        `<rect width="${width}" height="${labelH}" fill="#0c0c0c"/>` +
        `<text x="3" y="11" fill="${color}" font-family="monospace" font-size="11">` +
        `${row === 0 ? 'BEFORE' : 'AFTER'} #${idx}</text></svg>`,
      );
      tiles.push({ input: label, left: x, top: y });
      tiles.push({ input: image, left: x, top: y + labelH });
    }
  }
  await sharp({
    create: {
      width: picks * width,
      height: 2 * (height + labelH),
      channels: 3,
      background: { r: 12, g: 12, b: 12 },
    },
  }).composite(tiles).png().toFile(out);
  console.log(`  compare: ${path.basename(out)}`);
}

/** @param {string[]} argv */
export async function main(argv = process.argv.slice(2)) {
  const args = {
    scenario: null,
    list: false,
    reel: false,
    frames: null,
    interval: null,
    compare: null,
    audit: false,
    sheetStep: 5,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--help' || a === '-h') { help(); return 0; }
    if (a === '--list') { args.list = true; continue; }
    if (a === '--reel') { args.reel = true; continue; }
    if (a === '--audit') { args.audit = true; continue; }
    if (a === '--frames') { args.frames = Number(argv[++i]); continue; }
    if (a === '--interval') { args.interval = Number(argv[++i]); continue; }
    if (a === '--compare') { args.compare = argv[++i]; continue; }
    if (a === '--sheet-step') { args.sheetStep = Number(argv[++i]); continue; }
    if (a.startsWith('-')) { console.log(`! 未知のオプション: ${a}`); return 1; }
    args.scenario = a;
  }

  if (args.list) {
    console.log('場面:');
    console.log(listScenarios().join('\n'));
    console.log('\n毎回の確認: node tools/preview.mjs --reel');
    return 0;
  }
  if (!args.reel && !args.scenario) { help(); return 1; }
  if (args.scenario && !SCENARIO_NAMES.includes(args.scenario)) {
    console.log(`! 未知の場面: ${args.scenario}（--list で一覧）`);
    return 1;
  }

  // Chromium 未インストールはエラー扱いにしない（lead が判断する）。
  const { chromium } = await import('playwright');
  if (chromiumMissing(chromium)) return 0;

  fs.mkdirSync(PREVIEW_ROOT, { recursive: true });
  const time = stamp();
  const names = args.reel ? [...REEL_SCENARIOS] : [args.scenario];

  return withPage(async (page, url) => {
    const dirs = {};
    for (const name of names) {
      const spec = SCENARIOS[name];
      const frames = args.frames ?? spec.frames;
      const interval = args.interval ?? spec.interval;
      const outDir = path.join(PREVIEW_ROOT, `${name}-${time}`);
      const got = await capture(page, url, name, frames, interval, outDir);
      if (got < 10) {
        console.log(`! ${name} のフレームが撮れていない`);
        return 1;
      }
      dirs[name] = outDir;
      if (args.compare) {
        await makeCompare(path.resolve(args.compare), outDir, path.join(outDir, 'compare.png'));
      }
      if (args.audit) {
        console.log('[audit]');
        await auditReport(outDir, 6, null);
      }
    }
    if (args.reel) {
      console.log('[reel]');
      for (const name of REEL_SCENARIOS) console.log(`  ${name}: ${dirs[name]}`);
    }
    console.log(`\n完了: ${PREVIEW_ROOT}`);
    return 0;
  });
}

const invoked = process.argv[1] ? path.resolve(process.argv[1]) : null;
const modulePath = fileURLToPath(import.meta.url);
if (invoked !== null && invoked === modulePath) {
  main().then((code) => { process.exitCode = code; }).catch((e) => {
    console.error(e);
    process.exitCode = 1;
  });
}

export default main;
