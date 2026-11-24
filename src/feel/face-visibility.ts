import { Camera, MathUtils, Matrix4, Object3D, PerspectiveCamera, Vector3 } from 'three';
import { clamp01 } from '../core/math';

/**
 * Unity 版 FaceVisibility.Measure の移植。
 *
 * 顔の 5 点（中心 ± camera.right / camera.up * FaceRadius）について、
 * 「固定アスペクト 16/9 の画面内か」と「敵プリミティブ（球 / カプセル / 箱）に
 * 遮られていないか」を数える。物理レイキャストは使わない（敵にコライダが無い）。
 *
 * rig / logic へ直接依存しないため、head と enemies は以下の interface で注入する。
 * 測らないもの: 前髪・腕・HUD の重なり。
 */
export interface HeadBone {
  position: Vector3;
  up: Vector3;
  forward: Vector3;
}

export type PrimitiveShape = 'sphere' | 'capsule' | 'cube';

export interface FaceProxy {
  /** プリミティブのワールド行列（ローカルの単位形状 → ワールド） */
  matrixWorld: Matrix4;
  shape: PrimitiveShape;
  active: boolean;
}

export interface EnemyFigureView {
  name: string;
  active: boolean;
  /** 0..1。OpaqueAlpha 未満なら遮蔽に数えない（OcclusionFade が透かしている） */
  alpha: number;
  proxies: FaceProxy[];
}

export interface FaceVisibilityResult {
  /** 画面内かつ非遮蔽の点の割合 (0..1) */
  visible: number;
  /** 画面内の点の割合 (0..1) */
  inFrame: number;
  /** 非遮蔽の点の割合 (0..1) */
  unblocked: number;
  /** 顔を隠していた敵の名前（最初の 1 体） */
  blocker: string | null;
  /** 顔の正面と「顔からカメラへの向き」のなす角（度） */
  facingAngle: number;
  /** visible >= GoodVisible かつ facingAngle <= FacingLimit */
  readable: boolean;
}

/** 判定に使う画面の縦横比。バッチ実行は実機と画面サイズが違うので固定する。 */
export const Aspect = 16 / 9;
/** 画面端からこの割合より内側なら画面内とみなす。 */
export const FrameMargin = 0.03;
/** 顔の中心から上下左右の点までの距離 (m)。 */
export const FaceRadius = 0.07;
/** 顔のすぐ手前は遮蔽に数えない（拳など）。 */
export const NearFaceSlack = 0.10;
/** 5 点中 3 点以上。 */
export const GoodVisible = 0.6;
/** これを超えると表情が読めないとみなす（度）。 */
export const FacingLimit = 60;
/** 壁拘束の各段階の可視率下限。実測 86/85/96/83% より少し下に置く。 */
export const RequiredRatio: readonly number[] = [0.75, 0.75, 0.90, 0.75];
/** この不透明度より薄い敵は遮蔽に数えない。 */
export const OpaqueAlpha = 0.5;

const CENTER_UP = 0.06;
const CENTER_FORWARD = 0.05;
const FACE_POINTS = 5;

/** three の Object3D から HeadBone を作る（rig-port の head をそのまま渡せる）。 */
export function headBoneFromObject(head: Object3D): HeadBone {
  head.updateWorldMatrix(true, false);
  const m = head.matrixWorld;
  return {
    position: new Vector3().setFromMatrixPosition(m),
    up: new Vector3(0, 1, 0).transformDirection(m),
    // Unity の Transform.forward は +Z。glTF の前方向が -Z のボーンは
    // 呼び側で -1 を掛けて渡す（rig 側の変換に任せる）。
    forward: new Vector3(0, 0, 1).transformDirection(m),
  };
}

export function faceCenter(head: HeadBone, out = new Vector3()): Vector3 {
  return out.copy(head.position)
    .addScaledVector(head.up, CENTER_UP)
    .addScaledVector(head.forward, CENTER_FORWARD);
}

/** 点が画面の中にあるか。アスペクトは固定 16/9、tan(fov/2) で正規化する。 */
export function isInFrame(camera: Camera, world: Vector3): boolean {
  const local = world.clone().applyMatrix4(camera.matrixWorldInverse);
  const near = camera instanceof PerspectiveCamera ? camera.near : 0.1;
  if (local.z >= -near) return false;
  const depth = -local.z;
  const cam = camera as PerspectiveCamera;
  const tanV = Math.tan(MathUtils.degToRad(cam.fov) * 0.5);
  const x = local.x / (depth * tanV * Aspect);
  const y = local.y / (depth * tanV);
  const limit = 1 - FrameMargin * 2;
  return Math.abs(x) <= limit && Math.abs(y) <= limit;
}

/** 顔（直径 FaceRadius*2）が画面の高さに占める割合。 */
export function screenFaceSize(camera: PerspectiveCamera, head: HeadBone): number {
  const center = faceCenter(head);
  const local = center.applyMatrix4(camera.matrixWorldInverse);
  const depth = -local.z;
  if (depth <= camera.near) return 0;
  const screenHeight = 2 * depth * Math.tan(MathUtils.degToRad(camera.fov) * 0.5);
  return screenHeight > 1e-4 ? (FaceRadius * 2) / screenHeight : 0;
}

/** カメラ位置から敵プリミティブ表面までの最短距離。 */
export function lensClearance(cameraPosition: Vector3, enemies: readonly EnemyFigureView[]): number {
  let best = Number.MAX_VALUE;
  for (const enemy of enemies) {
    if (!enemy.active || enemy.alpha < OpaqueAlpha) continue;
    for (const proxy of enemy.proxies) {
      if (!proxy.active) continue;
      const inv = proxy.matrixWorld.clone().invert();
      const p = cameraPosition.clone().applyMatrix4(inv);
      let distance: number;
      switch (proxy.shape) {
        case 'cube': {
          const qx = Math.max(0, Math.abs(p.x) - 0.5);
          const qy = Math.max(0, Math.abs(p.y) - 0.5);
          const qz = Math.max(0, Math.abs(p.z) - 0.5);
          distance = Math.hypot(qx, qy, qz);
          break;
        }
        case 'capsule':
          distance = Math.sqrt(segmentToPointSqr(
            new Vector3(0, -0.5, 0), new Vector3(0, 0.5, 0), p,
          )) - 0.5;
          break;
        default:
          distance = p.length() - 0.5;
          break;
      }
      const scale = new Vector3().setFromMatrixScale(proxy.matrixWorld);
      distance *= Math.min(scale.x, Math.min(scale.y, scale.z));
      if (distance < best) best = distance;
    }
  }
  return best;
}

export function measure(
  camera: PerspectiveCamera,
  head: HeadBone,
  enemies: readonly EnemyFigureView[] = [],
): FaceVisibilityResult {
  const result: FaceVisibilityResult = {
    visible: 0, inFrame: 0, unblocked: 0, blocker: null, facingAngle: 0, readable: false,
  };

  camera.updateMatrixWorld(true);
  camera.matrixWorldInverse.copy(camera.matrixWorld).invert();

  const eye = new Vector3().setFromMatrixPosition(camera.matrixWorld);
  const center = faceCenter(head);
  const toEye = eye.clone().sub(center);
  result.facingAngle = MathUtils.radToDeg(
    Math.acos(clamp01(head.forward.clone().normalize().dot(toEye.clone().normalize()))),
  );

  const right = new Vector3(1, 0, 0).transformDirection(camera.matrixWorld);
  const up = new Vector3(0, 1, 0).transformDirection(camera.matrixWorld);
  const points = [
    center.clone(),
    center.clone().addScaledVector(right, FaceRadius),
    center.clone().addScaledVector(right, -FaceRadius),
    center.clone().addScaledVector(up, FaceRadius),
    center.clone().addScaledVector(up, -FaceRadius),
  ];

  let inFrame = 0;
  let unblocked = 0;
  let visible = 0;
  for (const point of points) {
    const framed = isInFrame(camera, point);
    let clear = true;
    for (const enemy of enemies) {
      if (!enemy.active || enemy.alpha < OpaqueAlpha) continue;
      if (blocks(enemy, eye, point)) {
        clear = false;
        if (result.blocker === null) result.blocker = enemy.name;
      }
    }
    if (framed) inFrame++;
    if (clear) unblocked++;
    if (framed && clear) visible++;
  }

  result.inFrame = inFrame / FACE_POINTS;
  result.unblocked = unblocked / FACE_POINTS;
  result.visible = visible / FACE_POINTS;
  result.readable = result.visible >= GoodVisible && result.facingAngle <= FacingLimit;
  return result;
}

function blocks(enemy: EnemyFigureView, eye: Vector3, point: Vector3): boolean {
  const toPoint = point.clone().sub(eye);
  const length = toPoint.length();
  if (length <= NearFaceSlack + 0.01) return false;
  const end = eye.clone().addScaledVector(toPoint, (length - NearFaceSlack) / length);

  for (const proxy of enemy.proxies) {
    if (!proxy.active) continue;
    const inv = proxy.matrixWorld.clone().invert();
    if (segmentHits(inv, proxy.shape, eye, end)) return true;
  }
  return false;
}

/**
 * 線分をプリミティブのローカル空間へ移し、単位形状（球: 半径 0.5 /
 * カプセル: 半径 0.5・全高 2 / 箱: 一辺 1）との交差を取る。
 */
export function segmentHits(inverseMatrix: Matrix4, shape: PrimitiveShape, from: Vector3, to: Vector3): boolean {
  const a = from.clone().applyMatrix4(inverseMatrix);
  const b = to.clone().applyMatrix4(inverseMatrix);
  switch (shape) {
    case 'cube':
      return segmentHitsUnitCube(a, b);
    case 'capsule':
      return segmentToSegmentSqr(a, b, new Vector3(0, -0.5, 0), new Vector3(0, 0.5, 0)) <= 0.25;
    default:
      return segmentToPointSqr(a, b, new Vector3(0, 0, 0)) <= 0.25;
  }
}

function segmentToPointSqr(a: Vector3, b: Vector3, p: Vector3): number {
  const ab = b.clone().sub(a);
  const len = ab.lengthSq();
  const t = len > 1e-10 ? clamp01(p.clone().sub(a).dot(ab) / len) : 0;
  return a.clone().addScaledVector(ab, t).sub(p).lengthSq();
}

function segmentHitsUnitCube(a: Vector3, b: Vector3): boolean {
  const d = b.clone().sub(a);
  let tMin = 0;
  let tMax = 1;
  for (const axis of ['x', 'y', 'z'] as const) {
    const o = a[axis];
    const dir = d[axis];
    if (Math.abs(dir) < 1e-8) {
      if (o < -0.5 || o > 0.5) return false;
      continue;
    }
    let t1 = (-0.5 - o) / dir;
    let t2 = (0.5 - o) / dir;
    if (t1 > t2) [t1, t2] = [t2, t1];
    tMin = Math.max(tMin, t1);
    tMax = Math.min(tMax, t2);
    if (tMin > tMax) return false;
  }
  return true;
}

/** 2 本の線分の最短距離の 2 乗（Ericson の Real-Time Collision Detection と同じ式）。 */
function segmentToSegmentSqr(p1: Vector3, q1: Vector3, p2: Vector3, q2: Vector3): number {
  const d1 = q1.clone().sub(p1);
  const d2 = q2.clone().sub(p2);
  const r = p1.clone().sub(p2);
  const a = d1.dot(d1);
  const e = d2.dot(d2);
  const f = d2.dot(r);
  let s: number;
  let t: number;

  if (a <= 1e-10 && e <= 1e-10) return r.lengthSq();
  if (a <= 1e-10) {
    s = 0;
    t = clamp01(f / e);
  } else {
    const c = d1.dot(r);
    if (e <= 1e-10) {
      t = 0;
      s = clamp01(-c / a);
    } else {
      const b = d1.dot(d2);
      const denom = a * e - b * b;
      s = denom > 1e-10 ? clamp01((b * f - c * e) / denom) : 0;
      t = (b * s + f) / e;
      if (t < 0) {
        t = 0;
        s = clamp01(-c / a);
      } else if (t > 1) {
        t = 1;
        s = clamp01((b - c) / a);
      }
    }
  }
  return p1.clone().addScaledVector(d1, s).sub(p2.clone().addScaledVector(d2, t)).lengthSq();
}
