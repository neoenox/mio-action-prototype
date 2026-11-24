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
import { GameWorld, type WorldSnapshot } from '../logic/world.js';
import { emptyIntent, type InputIntent } from '../input/intent.js';
import { headBoneFromObject, measure, type EnemyFigureView } from '../feel/face-visibility.js';
import { UnityFov, scenarioCameraProfile } from '../feel/scenario-camera.js';
import { HitStop } from '../feel/hit-stop.js';
import { CameraShake } from '../feel/shake.js';
import { CameraDirector } from '../feel/camera-director.js';
import { HitResult } from '../core/HitResult.js';
import { StaggerTier } from '../core/CombatConstants.js';
import type { WorldEvent } from '../logic/world.js';

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
  /** logic の実際の状態（撮影と QA の突き合わせ用）。 */
  world: WorldSnapshot | null;
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
// Unity の Prototype.unity は FOV 40。50 のままだと被写体が Unity より小さく写る。
const camera = new THREE.PerspectiveCamera(UnityFov, 16 / 9, 0.1, 100);
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

const ENEMY_COLORS = [0xd0605a, 0xc87850] as const;
const enemyRigs = ENEMY_COLORS.map((color) => {
  const rig = createEnemyRig(color);
  rig.attachTo(scene, { x: 2.4, y: 0, z: 0 });
  rig.root.quaternion.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.PI);
  return rig;
});
const enemyRig = enemyRigs[0]!;

// カメラ押し込み API（Unity ThirdPersonCamera 相当）と腹パン演出の構図。
// このゲームはレーン固定なので Unity の BeltScreenLock と同じ belt モードを既定にする。
// 距離・ピッチ・注視点高はシナリオごとに scenarioCameraProfile() で切り替える。
const cameraRig = new CameraController(camera, {
  beltMode: true,
  distance: 3.8,
  beltPitch: 14,
  beltPivotHeight: 0.86,
  minDistance: 0.82,
  maxDistance: 6,
});
cameraRig.setTarget(new THREE.Vector3(0, 0, 0));
cameraRig.setDistance(3.8);
const heldPunchCamera = new HeldBellyPunchCamera(cameraRig);

/** scenario の変更時に Unity 相当のカメラへ切り替える。 */
function applyScenarioCamera(name: string) {
  const profile = scenarioCameraProfile(name);
  camera.fov = profile.fov;
  camera.updateProjectionMatrix();
  cameraRig.settings.beltPitch = profile.pitch;
  cameraRig.settings.beltPivotHeight = profile.pivotHeight;
  cameraRig.settings.distance = profile.distance;
  cameraRig.setDistance(profile.distance);
}

// logic の EnemyBrainHooks へ渡す拳アンカー。rig の実ボーン座標を x/y/z 構造体化する。
const punchAnchor = createPunchAnchorProvider({
  getPunchAnchor: (zone) => enemyRig.getPunchAnchor(zone),
});

/**
 * ゲーム世界。敵 AI・拘束・壁シーケンス・移動をここで回す。
 * 以前はシナリオ名からポーズを決め打ちしていただけで、因果が動いていなかった。
 */
const clock = new Clock();

const world = new GameWorld({
  enemyCount: enemyRigs.length,
  rigs: enemyRigs.map((rig) => ({ getPunchAnchor: (zone) => rig.getPunchAnchor(zone) })),
  seed: 0x2f6e2b1,
});

/**
 * ヒット演出。Unity 版は当たった瞬間に timeScale を落として揺らし、カメラを押し込む。
 * これが無いと 19 場面比較でピークだけが低く出る（平均は近いのに 9〜27 対 25〜70）。
 */
const hitStop = new HitStop(clock);
const shake = new CameraShake(camera, clock);
const cameraDirector = new CameraDirector(
  cameraRig,
  () => world.snapshot().player.hp <= 0,
  () => heldPunchCamera.isCameraActive,
);

function staggerTierForDamage(damage: number): StaggerTier {
  if (damage >= 16) return StaggerTier.Large;
  if (damage >= 10) return StaggerTier.Medium;
  return StaggerTier.Small;
}

/** world の hit イベントを Unity 相当の手触りへ変換する。 */
function applyHitFeel(event: Extract<WorldEvent, { type: 'hit' }>): void {
  const tier = staggerTierForDamage(event.damage);
  const result = new HitResult(event.damage, tier);
  const stop = tier === StaggerTier.Large ? 0.10 : tier === StaggerTier.Medium ? 0.07 : 0.045;
  hitStop.play(stop);
  shake.shake(tier === StaggerTier.Large ? 0.055 : 0.032, 0.14);
  if (event.attacker === 'player') cameraDirector.onHitLanded(result);
  else cameraDirector.onHitReceived(result);
}

/** 手で遊ぶときのキー入力。撮影（capture）中は使わない。 */
const held = new Set<string>();
window.addEventListener('keydown', (event) => { held.add(event.code); });
window.addEventListener('keyup', (event) => { held.delete(event.code); });
window.addEventListener('blur', () => { held.clear(); });

/**
 * 入力。撮影中はシナリオと時計から決定的に作り、手で遊ぶときはキーボードを読む。
 * J/K/L = 軽/重/崩し、Space = もがき、A/D または ←/→ = 移動。
 */
function buildIntent(): InputIntent {
  if (params.get('capture') !== '1') {
    const left = held.has('KeyA') || held.has('ArrowLeft');
    const right = held.has('KeyD') || held.has('ArrowRight');
    return {
      ...emptyIntent(),
      moveX: (right ? 1 : 0) - (left ? 1 : 0),
      light: held.has('KeyJ'),
      heavy: held.has('KeyK'),
      break_: held.has('KeyL'),
      struggle: held.has('Space'),
    };
  }
  const t = clock.time + seedOffset;
  const m = motionFor(scenario);
  // 敵は x=2.4 付近に湧く。最初に寄って接触し、そのあとは小さく揺れる。
  const engage = t < 1.6 ? 1 : Math.sin(t * 1.4) * 0.35 * m.approach;
  return {
    ...emptyIntent(),
    moveX: engage,
    // 0.22 秒ごとに立ち上がるので、もがきは「押した瞬間」として数えられる。
    struggle: m.approach > 0.4 && Math.floor(t / 0.22) % 2 === 0,
    light: m.approach > 0.8 && Math.floor(t / 0.5) % 2 === 0,
  };
}

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
  applyScenarioCamera(sname);
}

/** animator の入力は world の snapshot から作る（決め打ちをやめた）。 */
function animatorInputFromSnapshot(snap: WorldSnapshot, elapsed: number) {
  const m = motionFor(scenario);
  const noise = prng.next() - 0.5;
  return {
    time: elapsed,
    dt,
    isMoving: snap.player.isMoving,
    isAttacking: snap.player.isAttacking,
    isGrappled: snap.player.isGrappled,
    isDown: snap.player.isDown,
    downOnBack: scenario !== 'haulprone' && scenario !== 'mountprone',
    downRollSide: scenario === 'mount' ? 1 : 0,
    mountInverted: scenario === 'mount' || scenario === 'mountprone' || scenario === 'facemount',
    staggerTier: 2,
    staggerZone: 'body' as const,
    planarSpeed: Math.abs(m.approach),
    swing: m.swing,
    spin: m.spin + noise * 0.02,
    approach: m.approach,
  };
}

function applyPose() {
  // 1. ヒットストップの復帰（unscaled）。ここで scale が戻ってから scaled を測る。
  hitStop.update(dt);
  const scaled = clock.scaledDelta(dt);
  world.tick(scaled, buildIntent());
  const snap = world.snapshot();
  const elapsed = clock.time + seedOffset;

  // 2. 当たった瞬間の演出（HitStop / 揺れ / カメラの押し込み）。
  for (const event of snap.events) {
    if (event.type === 'hit') applyHitFeel(event);
  }
  // 3. 死亡演出の監視（unscaled）。
  cameraDirector.update(dt);

  // 位置は logic が持つ。rig はそれを読むだけ。
  playerRig.root.position.x = snap.player.x;
  playerRig.root.position.z = snap.player.z;
  for (let i = 0; i < enemyRigs.length; i++) {
    const rig = enemyRigs[i]!;
    const enemy = snap.enemies[i];
    rig.root.position.x = enemy ? enemy.x : 4 + i;
    rig.root.position.z = enemy ? enemy.z : 0;
  }

  const playerInput = animatorInputFromSnapshot(snap, elapsed);
  playerRig.animator.tick(playerInput);

  for (let i = 0; i < enemyRigs.length; i++) {
    const enemy = snap.enemies[i];
    if (!enemy) continue;
    enemyRigs[i]!.animator.tick({
      ...playerInput,
      isGrappled: false,
      isDown: enemy.isDown,
      isAttacking: enemy.isAttacking,
      isMoving: !enemy.isAttacking && !enemy.isDead,
    });
  }

  // 拘束中の腹/顔パンだけは Unity と同じ寄りの構図にする。
  const punchZone = snap.events.find((e) => e.type === 'hit');
  if (snap.player.isGrappled && punchZone) {
    heldPunchCamera.setInput({
      phase: 2, // Combo
      zone: PunchZone.Body,
      playerX: snap.player.x,
      facingSign: snap.player.facing,
      fatigue: Math.min(1, snap.grapple.heldSeconds * 0.2),
      impactPulse: 1,
    });
    heldPunchCamera.holdFraming();
  } else if (snap.player.isGrappled) {
    heldPunchCamera.setInput({
      phase: 2,
      zone: PunchZone.Body,
      playerX: snap.player.x,
      facingSign: snap.player.facing,
    });
    heldPunchCamera.holdFraming();
  } else {
    heldPunchCamera.clearFocus();
  }

  cameraRig.setTarget(new THREE.Vector3(snap.player.x, 0, snap.player.z));
  cameraRig.update(dt, scaled);
  // 4. 最後に揺れを足す（前フレーム分を戻してから足すので累積しない）。
  shake.updateLate(dt);
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
    world: world.snapshot(),
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
