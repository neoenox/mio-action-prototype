/**
 * 撮影ドライバ（Unity 版 preview.py の連番キャプチャに対応する Three.js 側の入口）。
 *
 * 実時間で待たない。Playwright から `step(n)` でシミュレーションを固定 dt 進め、
 * `render()` で描画してから canvas を PNG に保存する。これにより Unity 版と同じ
 * フレーム番号で画像差分を突き合わせられる。
 *
 * window.__portCapture = {
 *   step(n?: number): void    // n フレーム進める（既定 1）
 *   render(): void            // 現在の状態を canvas に描画
 *   state(): CaptureState     // clock / scenario / frame + rig/feel のログ領域
 *   scenario(name: string): void // 場面を選んでフレーム 0 に戻す
 * }
 *
 * rig / feel が書き込むログ領域は state().rig / state().feel として公開する。
 * 各モジュールはこのオブジェクトに追記するだけで、撮影結果と一緒に残せる。
 */
import * as THREE from 'three';
import { Clock } from '../core/clock';
import { Prng } from '../core/prng';
import { createEnemyRig, createPlayerRig } from '../rig/index.js';
import { CameraController } from '../feel/camera.js';
import { HeldBellyPunchCamera, PunchZone } from '../feel/held-belly-punch-camera.js';
import { createPunchAnchorProvider } from '../logic/hooks.js';
import { headBoneFromObject, measure, type EnemyFigureView } from '../feel/face-visibility.js';

/** 撮影対象の場面名（tools/scenarios.mjs の19キーと同じ）。 */
const SCENARIO_NAMES = [
  'motionseq', 'wristlock', 'pinball', 'wristpull', 'wristpullcounter',
  'wallpin', 'frontpin', 'bearhug', 'wallsplit', 'belly', 'face',
  'facemount', 'bellybody', 'ankle', 'haul', 'haulprone', 'mount',
  'mountprone', 'idle',
] as const;

type ScenarioName = (typeof SCENARIO_NAMES)[number];

interface LogArea {
  [key: string]: unknown;
  events: Array<Record<string, unknown>>;
}

export interface CaptureState {
  clock: { time: number; unscaledTime: number; scaledScale: number };
  scenario: ScenarioName;
  frame: number;
  dt: number;
  rig: LogArea;
  feel: LogArea;
  /** test-player が数値で顔の可視性を判定するための値。 */
  face: {
    visible: number; inFrame: number; unblocked: number;
    blocker: string | null; facingAngle: number; readable: boolean;
  } | null;
  debug: { rootRotationY: number };
  query: Record<string, string>;
}

interface CaptureDriver {
  step(n?: number): CaptureState;
  render(): void;
  state(): CaptureState;
  scenario(name: string): void;
}

declare global {
  interface Window {
    __portCapture?: CaptureDriver;
  }
}

const params = new URLSearchParams(window.location.search);
const canvas = document.getElementById('app') as HTMLCanvasElement;
const hud = document.getElementById('hud');

/**
 * preserveDrawingBuffer: true が必須。Playwright の screenshot は
 * 描画バッファをクリア後の canvas を読むため、false だと真っ黒になる。
 */
const renderer = new THREE.WebGLRenderer({
  canvas,
  antialias: true,
  preserveDrawingBuffer: true,
});
renderer.setPixelRatio(1);
renderer.setSize(canvas.clientWidth || 1280, canvas.clientHeight || 720, false);
renderer.setClearColor(0x0a0a0e, 1);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0a0a0e);
const camera = new THREE.PerspectiveCamera(50, 16 / 9, 0.1, 100);
camera.position.set(0, 1.6, 4.2);
camera.lookAt(0, 1.0, 0);

scene.add(new THREE.HemisphereLight(0xffffff, 0x404058, 2.2));
const key = new THREE.DirectionalLight(0xffffff, 2.4);
key.position.set(2, 4, 3);
scene.add(key);

const ground = new THREE.Mesh(
  new THREE.PlaneGeometry(20, 20),
  new THREE.MeshStandardMaterial({ color: 0x2a2a34, roughness: 1 }),
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = -0.02;
scene.add(ground);

/**
 * 実リグ（rig-port の createPlayerRig / createEnemyRig）をシーンへ接続する。
 * 以前はプロキシ2体だったが、Wave 1 で実ボーン＋手続きポーズへ差し替えた。
 */
const playerRig = createPlayerRig();
playerRig.attachTo(scene, { x: -0.55, y: 0, z: 0 });
// rig 側で顔パーツを head ローカル -Z に配置したため、カメラ（+Z 側）へ
// 顔を向けるには root を π 回す。敵も同様に顔をカメラ側へ向ける。
// rotation.y に代入すると rig 側の getWorldQuaternion() が呼ばれた瞬間に
// quaternion(identity) 側が優先されて 0 へ戻る。quaternion を直接設定する。
playerRig.root.quaternion.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.PI);

const enemyRig = createEnemyRig(0xd0605a);
enemyRig.attachTo(scene, { x: 0.55, y: 0, z: 0 });
enemyRig.root.quaternion.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.PI);

// カメラ押し込み API（Unity ThirdPersonCamera 相当）と腹パン演出の構図。
// Unity の ThirdPersonCamera 既定距離 5.2m に合わせる。
// HeldBellyPunchCamera の倍率 (腹 0.27 等) はこの基本距離への乗数なので、
// ここを 1.55m にすると実距離 0.42m になり頭に貼り付く。
const cameraRig = new CameraController(camera, { distance: 5.2 });
cameraRig.setTarget(new THREE.Vector3(0, 1.4, 0));
cameraRig.setDistance(5.2);
const heldPunchCamera = new HeldBellyPunchCamera(cameraRig);

// logic の EnemyBrainHooks へ渡す拳アンカー。rig の実ボーン座標を x/y/z 構造体化する。
const punchAnchor = createPunchAnchorProvider({
  getPunchAnchor: (zone) => enemyRig.getPunchAnchor(zone),
});

/** 場面ごとの動きの違い（撮影の差分が出るように決定的に変える）。 */
function motionFor(name: ScenarioName): { swing: number; approach: number; spin: number } {
  switch (name) {
    case 'motionseq': return { swing: 0.9, approach: 0.3, spin: 0.15 };
    case 'wristlock': return { swing: 0.45, approach: 0.55, spin: 0.05 };
    case 'pinball': return { swing: 1.1, approach: 0.8, spin: 0.3 };
    case 'wristpull': return { swing: 0.7, approach: 0.9, spin: 0.1 };
    case 'wristpullcounter': return { swing: 0.8, approach: 0.6, spin: 0.2 };
    case 'wallpin': return { swing: 0.6, approach: 0.75, spin: 0.05 };
    case 'frontpin': return { swing: 0.65, approach: 0.7, spin: 0.08 };
    case 'bearhug': return { swing: 0.25, approach: 1.0, spin: 0.03 };
    case 'wallsplit': return { swing: 0.85, approach: 0.85, spin: 0.12 };
    case 'belly': return { swing: 0.95, approach: 0.9, spin: 0.04 };
    case 'face': return { swing: 1.0, approach: 0.9, spin: 0.06 };
    case 'facemount': return { swing: 0.75, approach: 0.95, spin: 0.25 };
    case 'bellybody': return { swing: 1.0, approach: 0.85, spin: 0.03 };
    case 'ankle': return { swing: 0.4, approach: 0.6, spin: 0.18 };
    case 'haul': return { swing: 0.55, approach: 0.8, spin: 0.35 };
    case 'haulprone': return { swing: 0.55, approach: 0.8, spin: 0.4 };
    case 'mount': return { swing: 0.8, approach: 0.95, spin: 0.45 };
    case 'mountprone': return { swing: 0.8, approach: 0.95, spin: 0.5 };
    case 'idle': return { swing: 0.12, approach: 0.05, spin: 0.01 };
  }
}

const clock = new Clock();
let scenario: ScenarioName = 'idle';
let frame = 0;
let dt = Number(params.get('interval') ?? '0.05') || 0.05;
const prng = new Prng(0x2f6e2b1);
let seedOffset = 0;

const rigLog: LogArea = { events: [] };
const feelLog: LogArea = { events: [] };

function reset(sname: ScenarioName) {
  scenario = sname;
  frame = 0;
  clock.time = 0;
  clock.unscaledTime = 0;
  clock.scaledScale = 1;
  rigLog.events.length = 0;
  feelLog.events.length = 0;
  seedOffset = [...sname].reduce((a, c) => a + c.charCodeAt(0), 0) / 1000;
}

/** 場面ごとの animator 入力。差分が出るように決定的に変える。 */
function animatorInputFor(name: ScenarioName) {
  const m = motionFor(name);
  const t = clock.time + seedOffset;
  const noise = prng.next() - 0.5;
  const downScenarios = name === 'haul' || name === 'haulprone' ||
    name === 'mount' || name === 'mountprone' || name === 'facemount';
  const punchScenarios = name === 'belly' || name === 'bellybody' ||
    name === 'face' || name === 'wallsplit' || name === 'wallpin';
  return {
    time: t,
    dt,
    isMoving: m.approach > 0.6,
    isAttacking: punchScenarios && Math.sin(t * 5.0) > 0.2,
    isGrappled: punchScenarios,
    isDown: downScenarios && t > 1.0,
    downOnBack: name !== 'haulprone' && name !== 'mountprone',
    downRollSide: name === 'mount' ? 1 : 0,
    mountInverted: name === 'mount' || name === 'mountprone' || name === 'facemount',
    staggerTier: name === 'bearhug' ? 2 : 1,
    staggerZone: name === 'face' || name === 'facemount' ? 'head' as const : 'body' as const,
    planarSpeed: m.approach,
    swing: m.swing,
    spin: m.spin + noise * 0.02,
    approach: m.approach,
  };
}

function applyPose() {
  const input = animatorInputFor(scenario);
  playerRig.animator.tick(input);
  enemyRig.animator.tick({ ...input, isGrappled: false, isAttacking: !input.isAttacking });

  // 実リグの位置を場面に応じて動かす（正規化した座標系）。
  const t = clock.time + seedOffset;
  playerRig.root.position.x = -0.55 + Math.sin(t * 1.7) * 0.08 * input.approach;
  enemyRig.root.position.x = 0.55 + Math.sin(t * 1.3) * 0.05 * input.approach;

  // 腹/顔パンシナリオでは HeldBellyPunchCamera の構図を毎フレーム保持する。
  const punching = scenario === 'belly' || scenario === 'bellybody' ||
    scenario === 'face' || scenario === 'wallsplit' || scenario === 'wallpin';
  if (punching) {
    const zone = scenario === 'face' || scenario === 'facemount'
      ? PunchZone.Face : PunchZone.Body;
    heldPunchCamera.setInput({
      phase: scenario === 'wallpin' ? 2 : 2, // Combo
      zone,
      playerX: playerRig.root.position.x,
      facingSign: 1,
      fatigue: Math.min(1, clock.time * 0.05),
      impactPulse: Math.max(0, 1 - (clock.time % 0.6) * 2),
    });
    heldPunchCamera.holdFraming();
  } else {
    heldPunchCamera.clearFocus();
  }

  cameraRig.setTarget(new THREE.Vector3(playerRig.root.position.x, 0, 0));
  cameraRig.update(dt, clock.scaledDelta(dt));
}

// 撮影中は HUD のフレーム番号が画像差分に乗らないよう非表示にする。
if (params.get('capture') === '1' && hud) hud.style.display = 'none';

function currentState(): CaptureState {
  const query: Record<string, string> = {};
  params.forEach((value, key) => { query[key] = value; });
  // test-player が画像だけでなく数値で顔の可視性を判定できるようにする。
  // 敵プロキシは未接続なので遮蔽なしとして測り、facing/inFrame を優先して見る。
  const enemies: EnemyFigureView[] = [];
  const headObject = playerRig.bones.head;
  const fv = headObject
    ? measure(camera, headBoneFromObject(headObject), enemies)
    : null;
  return {
    clock: {
      time: clock.time,
      unscaledTime: clock.unscaledTime,
      scaledScale: clock.scaledScale,
    },
    scenario,
    frame,
    dt,
    rig: rigLog,
    feel: feelLog,
    face: fv ? {
      visible: fv.visible,
      inFrame: fv.inFrame,
      unblocked: fv.unblocked,
      blocker: fv.blocker,
      facingAngle: fv.facingAngle,
      readable: fv.readable,
    } : null,
    debug: { rootRotationY: playerRig.root.rotation.y },
    query,
  };
}

const driver: CaptureDriver = {
  step(n = 1) {
    const count = Math.max(0, Math.floor(n));
    for (let i = 0; i < count; i++) {
      clock.advance(dt);
      frame += 1;
      applyPose();
    }
    return currentState();
  },
  render() {
    applyPose();
    renderer.render(scene, camera);
    if (hud && hud.style.display !== 'none') {
      hud.textContent = `${scenario} #${frame} t=${clock.time.toFixed(2)}`;
    }
  },
  state() {
    return currentState();
  },
  scenario(name: string) {
    if (!SCENARIO_NAMES.includes(name as ScenarioName)) {
      throw new Error(`未知のシナリオ: ${name}（${SCENARIO_NAMES.join(', ')}）`);
    }
    const interval = Number(params.get('interval'));
    dt = Number.isFinite(interval) && interval > 0 ? interval : (name === 'idle' ? 0.5 : 0.05);
    reset(name as ScenarioName);
    applyPose();
  },
};

window.__portCapture = driver;

const initial = params.get('scenario');
if (initial && SCENARIO_NAMES.includes(initial as ScenarioName)) {
  driver.scenario(initial);
} else {
  reset('idle');
  applyPose();
}
driver.render();

// 通常の表示では実時間で回す。撮影中は step() が唯一の進行源なので、
// URL クエリ ?capture=1 のときは rAF ループを止める。
if (params.get('capture') !== '1') {
  let last = performance.now();
  const loop = (now: number) => {
    const delta = Math.min(0.1, (now - last) / 1000);
    last = now;
    clock.advance(delta);
    frame += 1;
    applyPose();
    renderer.render(scene, camera);
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}
