/**
 * Unity 版 tools/preview.py の SCENARIOS 対応表（Three.js 側の撮影シナリオ定義）。
 *
 * キー・burst・frames・interval は Unity 版と同じ。Unity 固有の起動引数 (`args`) は
 * そのまま `args` に残し、Three.js 側では URL クエリへ変換して撮影ドライバへ渡す。
 * これにより「Unity と同じフレーム番号で突き合わせる」運用が变わらない。
 *
 * 使い方:
 *   import { SCENARIOS, REEL_SCENARIOS, toQuery, listScenarios } from './scenarios.mjs';
 *
 * 参考: MioPrototype/tools/preview.py の SCENARIOS（2026-11-24 時点、overhead を除く19場面）。
 */

/** @typedef {{ name: string, args: string[], burst: string, frames: number, interval: number }} Scenario */
/** @typedef {Record<string, Scenario>} ScenarioTable */

/** 撮りたい場面ごとの起動引数。フレーム数と間隔もここに持つ。 */
/** @type {ScenarioTable} */
export const SCENARIOS = {
  // 修正済みモーションを 1 本のビルドで順番に再生（デバッグメニューと同じ番号）。
  motionseq: {
    name: 'motionseq',
    args: ['-motionseq=1,2,3,4,5,6,7,8,9,10', '-motionseqinterval=3.5'],
    burst: '-motionseqburst',
    frames: 760,
    interval: 0.05,
  },
  // 手首ロック（後ろ手の拘束リング）。割る動きまで撮る。
  wristlock: {
    name: 'wristlock',
    args: ['-wristlocktest'],
    burst: '-wristlockburst',
    frames: 140,
    interval: 0.05,
  },
  // 挟み撃ちの突き飛ばし（ピンポン）。前後の2体で往復→腹パンでダウン。
  pinball: {
    name: 'pinball',
    args: ['-pinballtest'],
    burst: '-pinballburst',
    frames: 320,
    interval: 0.05,
  },
  // 手首ロック連動の引き寄せ膝（輪 → 正面の敵が引き込む → 正面拘束の膝蹴り）。
  wristpull: {
    name: 'wristpull',
    args: ['-wristlocktest', '-wristpulltest'],
    burst: '-wristlockburst',
    frames: 220,
    interval: 0.05,
  },
  // 同じく、引き寄せの瞬間に頭突きで押し返す（-wristpullcounter で自動入力）。
  wristpullcounter: {
    name: 'wristpullcounter',
    args: ['-wristlocktest', '-wristpulltest', '-wristpullcounter'],
    burst: '-wristlockburst',
    frames: 100,
    interval: 0.05,
  },
  // 壁押さえつけ（壁へ押し出す版）。膝〜二人がかり〜3 人がかりまで含む。
  wallpin: {
    name: 'wallpin',
    args: ['-grappletest', '-wallpintest'],
    burst: '-pinburst',
    frames: 200,
    interval: 0.05,
  },
  // レーンの正面拘束（-nowallpin で壁版を避ける）。膝蹴りが入る。
  frontpin: {
    name: 'frontpin',
    args: ['-grappletest', '-pintest', '-nowallpin'],
    burst: '-pinburst',
    frames: 140,
    interval: 0.05,
  },
  // ベアハッグ（正面抱え締め）。カメラの寄りを確認する。
  bearhug: {
    name: 'bearhug',
    args: ['-motionseq=5'],
    burst: '-motionseqburst',
    frames: 140,
    interval: 0.05,
  },
  // 壁 2 人がかり（Y 字）の腹パンを最後まで撮る。
  wallsplit: {
    name: 'wallsplit',
    args: ['-grappletest', '-wallpintest', '-splitcamlog'],
    burst: '-pinburst',
    frames: 360,
    interval: 0.05,
  },
  // 羽交い締めからの腹パン（5連撃→グロッキー→引き起こし→トドメ→崩れ落ち）。
  belly: {
    name: 'belly',
    args: ['-grappletest'],
    burst: '-heldpunchburst',
    frames: 195,
    interval: 0.05,
  },
  // 羽交い締めからの顔面パン（5連撃→グロッキー→引き起こし→トドメ→仰向けダウン）。
  face: {
    name: 'face',
    args: ['-grappletest', '-heldpunchface=1'],
    burst: '-heldpunchburst',
    frames: 195,
    interval: 0.05,
  },
  // 顔パンの締めで殴り飛ばした後のマウント（-nohaul で引き起こしを選ばせない）。
  facemount: {
    name: 'facemount',
    args: ['-grappletest', '-heldpunchface=1', '-nohaul', '-mountlog'],
    burst: '-heldpunchburst',
    frames: 400,
    interval: 0.05,
  },
  // 腹パン（体）を固定。左右交互のみぞおち打ち込みからトドメ崩れ落ちまでを見る。
  bellybody: {
    name: 'bellybody',
    args: ['-grappletest', '-heldpunchface=0'],
    burst: '-heldpunchburst',
    frames: 195,
    interval: 0.05,
  },
  // 足取り（背後の足首掴み）。掴まれて 1.6 秒でもがけなければ前へ転ぶ。
  ankle: {
    name: 'ankle',
    args: ['-grappletest', '-anklegrabtest'],
    burst: '-pinburst',
    frames: 90,
    interval: 0.05,
  },
  // ダウンした彼女を引きずり起こし、羽交い締めへつなぐ（-haultest で必ず選ぶ）。
  haul: {
    name: 'haul',
    args: ['-haultest'],
    burst: '-mountburst',
    frames: 100,
    interval: 0.05,
  },
  haulprone: {
    name: 'haulprone',
    args: ['-haultest', '-mountprone'],
    burst: '-mountburst',
    frames: 100,
    interval: 0.05,
  },
  // 地面マウント（跨ってのパンチ）。マウントのカメラ（GroundMountAttack.FrameMount）を見る。
  mount: {
    name: 'mount',
    args: ['-mountlog'],
    burst: '-mountburst',
    frames: 110,
    interval: 0.05,
  },
  mountprone: {
    name: 'mountprone',
    args: ['-mountprone', '-mountlog'],
    burst: '-mountburst',
    frames: 110,
    interval: 0.05,
  },
  // 放置プレイ（入力なし・仕込みなし）。0.5 秒ごとに 2 分撮る。拘束の連鎖と途切れを見る。
  idle: {
    name: 'idle',
    args: [],
    burst: '-idleburst',
    frames: 240,
    interval: 0.5,
  },
};

/** `--reel` でまとめて撮る3本（Unity 版と同じ）。 */
export const REEL_SCENARIOS = /** @type {const} */ (['bellybody', 'frontpin', 'haul']);

/** キー配列（定義順）。 */
export const SCENARIO_NAMES = Object.keys(SCENARIOS);

/**
 * Unity の起動引数を Three.js 側の URL クエリへ変換する。
 * `-foo=bar` → `foo=bar` / `-flag` → `flag=1`。撮影ドライバは
 * `new URLSearchParams(location.search)` で同じ情報を受け取れるようにする。
 * @param {Scenario} spec
 * @returns {string}
 */
export function toQuery(spec) {
  const params = new URLSearchParams();
  params.set('scenario', spec.name);
  for (const arg of spec.args) {
    const body = arg.replace(/^-/, '');
    const eq = body.indexOf('=');
    if (eq < 0) params.set(body, '1');
    else params.set(body.slice(0, eq), body.slice(eq + 1));
  }
  return params.toString();
}

/** 一覧を人間向けに整形する（preview.mjs --list で使う）。 */
export function listScenarios() {
  return SCENARIO_NAMES.map((name) => {
    const s = SCENARIOS[name];
    return `  ${name.padEnd(17)} ${String(s.frames).padStart(4)} 枚 / ${s.interval}s  ${s.args.join(' ')}`;
  });
}

export default SCENARIOS;
