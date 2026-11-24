#!/usr/bin/env node
/**
 * 撮った連番から「動きの不自然さ」を数字で拾う（Unity 版 tools/motion_audit.py の移植）。
 *
 * 連番 PNG をグレースケール 160x90 に BILINEAR リサイズし、直前フレームとの
 * 平均輝度差をフレーム番号付きで記録する。カメラの飛び・キャラのワープは
 * ここで大きな値になる。目視で全部のコマを見る前に、見るべき場所を絞る道具。
 *
 * 使い方（リポジトリ直下で実行）:
 *   node tools/motion_audit.mjs Verify/preview/frontpin-20261124-190000
 *   node tools/motion_audit.mjs <dir> --threshold 5        # 超えたら終了コード 1
 *   node tools/motion_audit.mjs --compare <before> <after> # 最大 ratio の比較
 *   node tools/motion_audit.mjs <dir> --strip 110 117 -o seq.png
 *   node tools/motion_audit.mjs --list                     # 直近のキャプチャ一覧
 *
 * 運用ルール（Unity 版の AGENTS.md / motion_audit.py から継承）:
 *   - カメラの飛び・ワープは平均の 3〜7 倍として出る。
 *   - 打撃ごとの 3〜4 倍はヒットストップ由来で正常。Number を見て中身を確かめる。
 *   - 大きな値＝不具合ではない。RequestCinematicShot(..., cut: true) の意図的な
 *     カットも同じ値になる（例: GroundMountAttack のマウント開始は 9.9 倍）。
 *     値が出たら --strip でそのフレームを開き、コードに cut: true が無いか確認する。
 *   - 直したら --compare で下がったことを数字で示す。下がっていなければ直っていない。
 *   - 既定のしきい値は 5 倍（--threshold 5）。
 *
 * 終了コード: 0 = 問題なし / 1 = しきい値超え・読み込み失敗・比較で悪化。
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const SIZE = { width: 160, height: 90 };
const PREVIEW_ROOT = path.resolve('Verify', 'preview');
/** Unity 版 frameseq.py の LEGACY_PREFIXES 相当。 */
const PREFIXES = ['f', 'pin', 'belly', 'face', 'knee', 'front'];

/** `f_012.png` → 12 */
function frameIndex(file) {
  const base = path.basename(file, path.extname(file));
  const parts = base.split('_');
  const n = Number.parseInt(parts.at(-1) ?? '', 10);
  return Number.isFinite(n) ? n : -1;
}

/** 連番 PNG を番号順に返す。接頭辞が違っても同じ番号順に並べる。 */
function frameFiles(dir) {
  if (!fs.existsSync(dir)) return [];
  const found = new Map();
  for (const name of fs.readdirSync(dir)) {
    if (!name.toLowerCase().endsWith('.png')) continue;
    const prefix = name.split('_')[0];
    if (!PREFIXES.includes(prefix)) continue;
    const idx = frameIndex(name);
    if (idx < 0 || found.has(idx)) continue;
    found.set(idx, path.join(dir, name));
  }
  return [...found.entries()].sort((a, b) => a[0] - b[0]).map(([, p]) => p);
}

/**
 * 各フレームを grayscale 160x90 (BILINEAR) にして RAW を返す。
 * @param {string[]} files
 */
async function loadGray(files) {
  const out = [];
  for (const file of files) {
    const { data, info } = await sharp(file)
      .greyscale()
      .resize(SIZE.width, SIZE.height, { kernel: sharp.kernel.bilinear })
      .raw()
      .toBuffer({ resolveWithObject: true });
    if (info.channels !== 1) throw new Error(`想定外のチャンネル数: ${info.channels} (${file})`);
    out.push({ index: frameIndex(file), file, data });
  }
  return out;
}

/** 直前フレームとの平均絶対輝度差（0-255 スケール）とフレーム番号を返す。 */
export async function diffs(dir) {
  const files = frameFiles(dir);
  if (files.length < 3) return { values: [], indices: [], count: files.length };
  const imgs = await loadGray(files);
  const values = [];
  const indices = [];
  for (let i = 1; i < imgs.length; i++) {
    const a = imgs[i - 1].data;
    const b = imgs[i].data;
    let sum = 0;
    for (let p = 0; p < a.length; p++) sum += Math.abs(a[p] - b[p]);
    values.push(sum / a.length);
    indices.push(imgs[i - 1].index);
  }
  return { values, indices, count: imgs.length };
}

function stats(values) {
  const avg = values.length ? values.reduce((a, b) => a + b, 0) / values.length : 0;
  const peak = values.length ? Math.max(...values) : 0;
  return { avg, peak, ratio: avg > 0 ? peak / avg : 0 };
}

/** レポートを出力して ratio を返す。 */
export async function report(dir, top = 6, threshold = null) {
  const { values, indices, count } = await diffs(dir);
  if (!values.length) {
    console.log(`  ! フレームが足りない: ${dir}`);
    return 0;
  }
  const { avg, peak, ratio } = stats(values);
  console.log(path.basename(path.resolve(dir)));
  console.log(`  ${count} フレーム  平均差 ${avg.toFixed(2)}  最大 ${peak.toFixed(2)} (${ratio.toFixed(1)}倍)`);

  const order = values.map((_, i) => i).sort((a, b) => values[b] - values[a]).slice(0, top);
  for (const i of order.sort((a, b) => a - b)) {
    const r = avg > 0 ? values[i] / avg : 0;
    const mark = threshold !== null && r >= threshold ? '  <- 要確認' : '';
    console.log(
      `    #${String(indices[i]).padStart(3)}->#${String(indices[i] + 1).padStart(3)}` +
      `  差 ${values[i].toFixed(2).padStart(6)}  (${r.toFixed(1).padStart(4)}倍)${mark}`,
    );
  }
  if (threshold !== null && ratio >= threshold) {
    console.log(`  ! 最大 ${ratio.toFixed(1)} 倍がしきい値 ${threshold} 倍を超えている`);
  }
  return ratio;
}

/** 最大 ratio の比較。改善なら 0 / 悪化なら 1。 */
export async function compare(before, after) {
  const a = await diffs(before);
  const b = await diffs(after);
  if (!a.values.length || !b.values.length) {
    console.log('! 比較できない（フレーム不足）');
    return 1;
  }
  const sa = stats(a.values);
  const sb = stats(b.values);
  console.log(`${path.basename(path.resolve(before))}: 最大 ${sa.peak.toFixed(2)} (${sa.ratio.toFixed(1)}倍)`);
  console.log(`${path.basename(path.resolve(after))}: 最大 ${sb.peak.toFixed(2)} (${sb.ratio.toFixed(1)}倍)`);
  if (sb.ratio < sa.ratio) {
    console.log(`  改善: ${(sa.ratio - sb.ratio).toFixed(1)} 倍ぶん下がった`);
    return 0;
  }
  if (sb.ratio > sa.ratio) {
    console.log(`  悪化: ${(sb.ratio - sa.ratio).toFixed(1)} 倍ぶん上がった`);
    return 1;
  }
  console.log('  変化なし');
  return 0;
}

/** START..END のフレームを4列シートにする。 */
export async function strip(dir, start, end, out) {
  const files = frameFiles(dir).filter((f) => {
    const i = frameIndex(f);
    return i >= start && i <= end;
  });
  if (!files.length) {
    console.log('  ! その区間にフレームが無い');
    return;
  }

  const cols = Math.min(4, files.length);
  const width = 480;
  const height = Math.round(width * 9 / 16);
  const labelH = 14;
  const rows = Math.ceil(files.length / cols);

  const tiles = [];
  for (const [i, file] of files.entries()) {
    const idx = frameIndex(file);
    const image = await sharp(file)
      .resize(width, height, { fit: 'fill', kernel: sharp.kernel.lanczos3 })
      .toBuffer();
    const x = (i % cols) * width;
    const y = Math.floor(i / cols) * (height + labelH);
    const label = Buffer.from(
      `<svg width="${width}" height="${labelH}">` +
      `<rect width="${width}" height="${labelH}" fill="#0a0a0a"/>` +
      `<text x="3" y="11" fill="#ffee00" font-family="monospace" font-size="11">#${idx}</text>` +
      `</svg>`,
    );
    tiles.push({ input: label, left: x, top: y });
    tiles.push({ input: image, left: x, top: y + labelH });
  }

  const target = path.isAbsolute(out) ? out : path.join(path.resolve(dir), out);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  await sharp({
    create: {
      width: cols * width,
      height: rows * (height + labelH),
      channels: 3,
      background: { r: 10, g: 10, b: 10 },
    },
  })
    .composite(tiles)
    .png()
    .toFile(target);
  console.log(`  strip: ${target}`);
}

/** 直近のキャプチャ一覧。 */
export function list() {
  if (!fs.existsSync(PREVIEW_ROOT)) {
    console.log('キャプチャがまだ無い（node tools/preview.mjs <場面> で撮る）');
    return;
  }
  const dirs = fs.readdirSync(PREVIEW_ROOT, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => path.join(PREVIEW_ROOT, e.name))
    .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  for (const d of dirs) {
    console.log(`  ${path.basename(d)}  ${frameFiles(d).length} 枚`);
  }
}

// --- CLI ---------------------------------------------------------------

function help() {
  console.log(`撮った連番から動きの不自然さを拾う（Unity 版 motion_audit.py の移植）。

使い方:
  node tools/motion_audit.mjs <dir> [--top N] [--threshold N]
  node tools/motion_audit.mjs --compare BEFORE AFTER
  node tools/motion_audit.mjs <dir> --strip START END [-o FILE]
  node tools/motion_audit.mjs --list

運用ルール:
  - 打撃ごとの 3〜4 倍はヒットストップ由来で正常。
  - cut: true の意図的なカットも大きな値になる。--strip で中身を確認する。
  - 直したら --compare で下がったことを数字で示す。
  - 既定のしきい値は 5 倍。超過時は終了コード 1。`);
}

/** @param {string[]} argv */
export async function main(argv = process.argv.slice(2)) {
  const args = {
    dirs: [],
    top: 6,
    threshold: null,
    compare: null,
    strip: null,
    out: 'strip.png',
    list: false,
  };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--help' || a === '-h') { help(); return 0; }
    if (a === '--list') { args.list = true; continue; }
    if (a === '--top') { args.top = Number(argv[++i]); continue; }
    if (a === '--threshold') { args.threshold = Number(argv[++i]); continue; }
    if (a === '--compare') { args.compare = [argv[++i], argv[++i]]; continue; }
    if (a === '--strip') { args.strip = [Number(argv[++i]), Number(argv[++i])]; continue; }
    if (a === '-o' || a === '--out') { args.out = argv[++i]; continue; }
    if (a.startsWith('-')) { console.log(`! 未知のオプション: ${a}`); return 1; }
    args.dirs.push(a);
  }

  if (args.list) { list(); return 0; }
  if (args.compare) return compare(args.compare[0], args.compare[1]);
  if (!args.dirs.length) { help(); return 1; }

  let failed = false;
  for (const dir of args.dirs) {
    const ratio = await report(dir, args.top, args.threshold);
    if (args.threshold !== null && ratio >= args.threshold) failed = true;
  }
  if (args.strip) await strip(args.dirs[0], args.strip[0], args.strip[1], args.out);
  return failed ? 1 : 0;
}

const invoked = process.argv[1] ? path.resolve(process.argv[1]) : null;
const modulePath = fileURLToPath(import.meta.url);
const isDirectRun = invoked !== null && invoked === modulePath;
if (isDirectRun) {
  main().then((code) => { process.exitCode = code; }).catch((e) => {
    console.error(e);
    process.exitCode = 1;
  });
}

export default main;
