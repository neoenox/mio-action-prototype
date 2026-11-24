import { Object3D, Quaternion, Vector3 } from 'three';
import {
  JointId,
  JointRom,
  JointRomReading,
  JointSide,
} from '../core/JointRom.js';
import {
  JointSkeleton,
  RIG_FORWARD,
  RIG_RIGHT,
  RIG_UP,
} from './skeleton.js';

const MIN_SEGMENT_LENGTH = 1e-4;
const SHOULDER_SAGITTAL_MIN = 0.5;
const RAD2DEG = 180 / Math.PI;

/** テスト・デバッグ用に内部関数を公開する。 */
export const JointRomProbeInternals = {
  bendEuler,
  bendAngles,
  twistDegrees,
  planarAngle,
  toBodyFrame,
  bodyAxes,
} as const;

/**
 * スケルトンを測って 12 関節 × 左右/中央の JointRomReading を返す。
 * 設定値ではなく「いまのワールド座標の骨」を測る点が必須（IK 適用後を測る）。
 */
export function measureJoints(skeleton: JointSkeleton): JointRomReading[] {
  const results: JointRomReading[] = [];
  if (!skeleton.isValid) return results;

  measureTorso(skeleton, results);
  measureNeck(skeleton, results);

  const b = skeleton.bones;
  measureElbow(b.lUpperArm, b.lLowerArm, b.lHand, skeleton.elbowHingeLocalL, skeleton.hasHinges, JointSide.Left, results);
  measureElbow(b.rUpperArm, b.rLowerArm, b.rHand, skeleton.elbowHingeLocalR, skeleton.hasHinges, JointSide.Right, results);

  measureKnee(b.lUpperLeg, b.lLowerLeg, b.lFoot, skeleton.kneeHingeLocalL, skeleton.hasHinges, JointSide.Left, results);
  measureKnee(b.rUpperLeg, b.rLowerLeg, b.rFoot, skeleton.kneeHingeLocalR, skeleton.hasHinges, JointSide.Right, results);

  measureHip(skeleton, true, results);
  measureHip(skeleton, false, results);

  measureShoulder(skeleton, true, results);
  measureShoulder(skeleton, false, results);

  return results;
}

/** Unity `JointRomProbe.Measure` 互換（List へ追記する形）。 */
export function measureInto(skeleton: JointSkeleton, results: JointRomReading[]): void {
  results.splice(0, results.length, ...measureJoints(skeleton));
}

// --- 体幹・首 ------------------------------------------------------------

function measureTorso(s: JointSkeleton, out: JointRomReading[]): void {
  if (!s.hasRestRotations) return;
  const b = s.bones;
  if (!b.root || !b.hips || !b.chest) return;
  const restHips = s.restRotations.hips;
  const restChest = s.restRotations.chest;
  if (!restHips || !restChest) return;

  const bend = bendEuler(b.root, b.hips, b.chest, restHips, restChest);
  out.push(JointRom.read(JointId.SpinePitch, JointSide.Center, bend.x));
  out.push(JointRom.read(JointId.SpineYaw, JointSide.Center, bend.y));
  out.push(JointRom.read(JointId.SpineRoll, JointSide.Center, bend.z));
}

function measureNeck(s: JointSkeleton, out: JointRomReading[]): void {
  if (!s.hasRestRotations) return;
  const b = s.bones;
  if (!b.root || !b.chest || !b.head) return;
  const restChest = s.restRotations.chest;
  const restHead = s.restRotations.head;
  if (!restChest || !restHead) return;

  const bend = bendEuler(b.root, b.chest, b.head, restChest, restHead);
  out.push(JointRom.read(JointId.NeckPitch, JointSide.Center, bend.x));
  out.push(JointRom.read(JointId.NeckYaw, JointSide.Center, bend.y));
  out.push(JointRom.read(JointId.NeckRoll, JointSide.Center, bend.z));
}

/**
 * 子ボーンの向きを親ボーン基準に直し、初期姿勢からの差を
 * (pitch=前後, yaw=ひねり, roll=左右) に分ける（Unity `BendEuler` 相当）。
 *
 * delta = inverse(parentNow) * childNow * inverse(restChild) * restParent
 * 親の剛体回転で説明できる分を差し引くため、親が倒れて子が付いてきただけなら
 * 単位回転になる。軸は親ボーン自身の軸（root 軸を使うと寝た姿勢で化ける）。
 */
export function bendEuler(
  root: Object3D,
  parent: Object3D,
  child: Object3D,
  restParent: Quaternion,
  restChild: Quaternion,
): Vector3 {
  const rootInv = root.getWorldQuaternion(new Quaternion()).invert();
  const parentNow = rootInv.clone().multiply(parent.getWorldQuaternion(new Quaternion()));
  const childNow = rootInv.clone().multiply(child.getWorldQuaternion(new Quaternion()));

  const delta = parentNow.invert()
    .multiply(childNow)
    .multiply(restChild.clone().invert())
    .multiply(restParent);

  return bendAngles(delta);
}

/**
 * 体幹・首の曲がりを pitch / yaw / roll に分ける（Unity `BendAngles` 相当）。
 * 上軸まわりのひねりを先に抜き、残りの swing を「角度 × 軸」に分けるので、
 * 深く前屈しながらひねっても各角度が化けない。
 */
export function bendAngles(delta: Quaternion): Vector3 {
  const yaw = wrapDegrees(twistDegrees(delta, RIG_UP));
  const twist = new Quaternion().setFromAxisAngle(RIG_UP, yaw / RAD2DEG);
  const swing = twist.invert().multiply(delta);

  const axis = new Vector3();
  let angleRad = 2 * Math.acos(Math.min(1, Math.abs(swing.w)));
  const sinHalf = Math.sqrt(Math.max(0, 1 - swing.w * swing.w));
  if (sinHalf < 1e-6) {
    axis.set(0, 0, 0);
  } else {
    axis.set(swing.x / sinHalf, swing.y / sinHalf, swing.z / sinHalf);
  }
  let swingDegrees = angleRad * RAD2DEG;
  if (swingDegrees > 180) swingDegrees = 360 - swingDegrees;

  const flat = projectOnPlane(axis, RIG_UP);
  if (flat.lengthSq() < 1e-8) return new Vector3(0, yaw, 0);
  flat.normalize();

  // Unity(左手系: forward=+Z) → Three(右手系: forward=-Z) では forward が反転し、
  // Cross の向きも反転する。Unity と同じ符号（正 = 前屈 / 正 = 右へ）にするため
  // pitch と roll を反転する。yaw は体の前が -Z になった分だけ埋め合わせになるため反転しない。
  const pitch = -swingDegrees * flat.dot(RIG_RIGHT);
  const roll = swingDegrees * flat.dot(RIG_FORWARD);
  return new Vector3(pitch, yaw, roll);
}

/** 指定軸まわりの回転成分（度）。Unity `TwistDegrees` 相当。 */
export function twistDegrees(rotation: Quaternion, axis: Vector3): number {
  const projected = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
  if (projected * projected + rotation.w * rotation.w < 1e-12) return 0;
  return 2 * Math.atan2(projected, rotation.w) * RAD2DEG;
}

function wrapDegrees(degrees: number): number {
  let d = degrees % 360;
  if (d > 180) d -= 360;
  else if (d < -180) d += 360;
  return d;
}

function projectOnPlane(v: Vector3, normal: Vector3): Vector3 {
  const d = v.dot(normal);
  return new Vector3(v.x - normal.x * d, v.y - normal.y * d, v.z - normal.z * d);
}

// --- 肘・膝 ---------------------------------------------------------------

function measureElbow(
  shoulder: Object3D | null,
  elbow: Object3D | null,
  hand: Object3D | null,
  hingeLocal: Vector3,
  hasHinge: boolean,
  side: JointSide,
  out: JointRomReading[],
): void {
  if (!shoulder || !elbow || !hand) return;

  const upper = direction(shoulder, elbow);
  const fore = direction(elbow, hand);
  if (!upper || !fore) return;

  let flex = angleBetween(upper, fore);

  // 蝶番軸は上腕のローカル軸を「いまの上腕の回転」でワールドへ戻したもの。
  // 曲がる面が蝶番と 35° 以上外れていたら向き判定をしない（誤警告防止）。
  if (hasHinge && hingeLocal.lengthSq() > 1e-8) {
    const hinge = hingeLocal.clone().applyQuaternion(shoulder.getWorldQuaternion(new Quaternion()));
    const bend = new Vector3().crossVectors(upper, fore);
    if (bend.lengthSq() > 1e-8 && Math.abs(bend.normalize().dot(hinge)) > 0.35) {
      const signed = planarAngle(upper, fore, hinge);
      if (signed !== null && signed < 0) flex = -flex;
    }
  }

  out.push(JointRom.read(JointId.Elbow, side, flex));
}

function measureKnee(
  upperBone: Object3D | null,
  lowerBone: Object3D | null,
  foot: Object3D | null,
  hingeLocal: Vector3,
  hasHinge: boolean,
  side: JointSide,
  out: JointRomReading[],
): void {
  if (!upperBone || !lowerBone || !foot) return;

  const thigh = direction(upperBone, lowerBone);
  const shin = direction(lowerBone, foot);
  if (!thigh || !shin) return;

  let flex = angleBetween(thigh, shin);

  if (hasHinge && hingeLocal.lengthSq() > 1e-8) {
    const hinge = hingeLocal.clone().applyQuaternion(upperBone.getWorldQuaternion(new Quaternion()));
    const bend = new Vector3().crossVectors(thigh, shin);
    if (bend.lengthSq() > 1e-8 && Math.abs(bend.normalize().dot(hinge)) > 0.35) {
      const signed = planarAngle(thigh, shin, hinge);
      if (signed !== null && signed < 0) flex = -flex;
    }
  }

  out.push(JointRom.read(JointId.Knee, side, flex));
}

// --- 股関節・肩 ------------------------------------------------------------

function measureHip(s: JointSkeleton, left: boolean, out: JointRomReading[]): void {
  const b = s.bones;
  const upper = left ? b.lUpperLeg : b.rUpperLeg;
  const lower = left ? b.lLowerLeg : b.rLowerLeg;
  const hips = b.hips;
  if (!upper || !lower || !hips) return;
  const rest = s.restRotations.hips;
  if (!rest) return;

  const thigh = direction(upper, lower);
  if (!thigh) return;

  const axes = bodyAxes(hips, rest);
  const local = toBodyFrame(thigh, axes.up, axes.right, axes.forward);
  const side = left ? JointSide.Left : JointSide.Right;

  // 前後: 真下から前へ持ち上がった角度。atan2 なので真上まで上がっても潰れない。
  const flexion = Math.atan2(local.z, -local.y) * RAD2DEG;
  out.push(JointRom.read(JointId.HipFlexion, side, flexion));

  // 開き: 前後面から横へ外れた角度。左右とも外転を正にそろえる。
  let abduction = Math.asin(clamp(local.x, -1, 1)) * RAD2DEG;
  if (left) abduction = -abduction;
  out.push(JointRom.read(JointId.HipAbduction, side, abduction));
}

function measureShoulder(s: JointSkeleton, left: boolean, out: JointRomReading[]): void {
  const b = s.bones;
  const upper = left ? b.lUpperArm : b.rUpperArm;
  const lower = left ? b.lLowerArm : b.rLowerArm;
  const chest = b.chest;
  if (!upper || !lower || !chest) return;
  const rest = s.restRotations.chest;
  if (!rest) return;

  const arm = direction(upper, lower);
  if (!arm) return;

  const axes = bodyAxes(chest, rest);
  const local = toBodyFrame(arm, axes.up, axes.right, axes.forward);
  const side = left ? JointSide.Left : JointSide.Right;

  // 前後面への射影が短い（腕が真横に近い）ときは前後を測らない。
  const sagittal = Math.hypot(local.y, local.z);
  if (sagittal >= SHOULDER_SAGITTAL_MIN) {
    let flexion = Math.atan2(local.z, -local.y) * RAD2DEG;
    // 真上を越えて後ろ寄りへ上げた腕は atan2 が -180° 側へ折り返す。
    // 伸展は -110° より深くならないので、それより下は 180° 超の屈曲へ戻す。
    if (flexion < -110) flexion += 360;
    out.push(JointRom.read(JointId.ShoulderFlexion, side, flexion));
  }

  let abduction = Math.asin(clamp(local.x, -1, 1)) * RAD2DEG;
  if (left) abduction = -abduction;
  out.push(JointRom.read(JointId.ShoulderAbduction, side, abduction));
}

/** ボーンの「体の上・右・前」を初期姿勢から求める（Unity `BodyAxes` 相当）。 */
export function bodyAxes(bone: Object3D, restRotation: Quaternion): {
  up: Vector3;
  right: Vector3;
  forward: Vector3;
} {
  const now = bone.getWorldQuaternion(new Quaternion());
  const toLocal = restRotation.clone().invert();
  const up = RIG_UP.clone().applyQuaternion(toLocal).applyQuaternion(now).normalize();
  const right = RIG_RIGHT.clone().applyQuaternion(toLocal.clone()).applyQuaternion(now).normalize();
  const forward = RIG_FORWARD.clone().applyQuaternion(toLocal.clone()).applyQuaternion(now).normalize();
  return { up, right, forward };
}

/** ワールドの向きを体の (右, 上, 前) 成分へ落とす。戻り値は (x=右, y=上, z=前)。 */
export function toBodyFrame(
  worldDirection: Vector3,
  up: Vector3,
  right: Vector3,
  forward: Vector3,
): Vector3 {
  return new Vector3(
    worldDirection.dot(right),
    worldDirection.dot(up),
    worldDirection.dot(forward),
  );
}

// --- 共通の計算 ------------------------------------------------------------

function direction(from: Object3D, to: Object3D): Vector3 | null {
  const a = from.getWorldPosition(new Vector3());
  const b = to.getWorldPosition(new Vector3());
  const delta = b.sub(a);
  if (delta.lengthSq() < MIN_SEGMENT_LENGTH * MIN_SEGMENT_LENGTH) return null;
  return delta.normalize();
}

function angleBetween(a: Vector3, b: Vector3): number {
  const dot = clamp(a.dot(b), -1, 1);
  return Math.acos(dot) * RAD2DEG;
}

/**
 * axis のまわりで from → to が何度回っているか。軸と平行な成分を落として測る。
 * 回転面が潰れているときは null。
 */
export function planarAngle(
  from: Vector3,
  to: Vector3,
  axis: Vector3,
  minProjection = 1e-2,
): number | null {
  const a = projectOnPlane(from, axis);
  const b = projectOnPlane(to, axis);
  if (a.length() < minProjection || b.length() < minProjection) return null;

  const angle = angleBetween(a, b);
  const cross = new Vector3().crossVectors(a, b);
  return cross.dot(axis) >= 0 ? angle : -angle;
}

function clamp(v: number, lo: number, hi: number): number {
  return v < lo ? lo : v > hi ? hi : v;
}
