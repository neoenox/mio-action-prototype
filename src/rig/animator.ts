import { Object3D, Quaternion, Vector2, Vector3 } from 'three';
import { clamp, clamp01, lerp, moveTowards, smoothstep } from '../core/math.js';
import {
  JointSkeleton,
  RIG_FORWARD,
  RIG_RIGHT,
  RIG_UP,
  relativeRotationToRoot,
} from './skeleton.js';

/** Unity `CharacterAnimator.State` 相当。 */
export enum AnimatorState {
  Idle = 0,
  Walk = 1,
  Stagger = 2,
  Down = 3,
  Attack = 4,
  Guard = 5,
  GetUp = 6,
  Grabbed = 7,
}

/** Unity `PoseValues` 相当。座標はキャラクタのローカル（x=右, y=上, z=前=Three の -Z）。 */
export interface PoseValues {
  footL: Vector3;
  footR: Vector3;
  hipsOffset: Vector3;
  armLUpper: Vector3;
  armLFore: Vector3;
  armRUpper: Vector3;
  armRFore: Vector3;
  handOffBrace: number;
  torsoPitch: number;
  torsoRoll: number;
  torsoYaw: number;
  headPitch: number;
  headYaw: number;
  headRoll: number;
  layAngle: number;
  laySide: number;
  legTuck: number;
  footPitchL: number;
  footPitchR: number;
  grip: number;
  handRollL: number;
  handRollR: number;
}

export function createPoseValues(overrides: Partial<PoseValues> = {}): PoseValues {
  return {
    footL: new Vector3(), footR: new Vector3(), hipsOffset: new Vector3(),
    armLUpper: new Vector3(), armLFore: new Vector3(),
    armRUpper: new Vector3(), armRFore: new Vector3(),
    handOffBrace: 0, torsoPitch: 0, torsoRoll: 0, torsoYaw: 0,
    headPitch: 0, headYaw: 0, headRoll: 0,
    layAngle: 0, laySide: 0, legTuck: 0, footPitchL: 0, footPitchR: 0,
    grip: 0.3, handRollL: 0, handRollR: 0,
    ...overrides,
  };
}

/** 構え定数（Unity `CharacterAnimator.HeldPunch.cs` の static readonly と同じ値）。 */
export const STANCE = {
  HipsY: -0.085,
  HipsZ: 0.005,
  TorsoPitch: 5,
  TorsoYaw: -12,
  HeadYaw: 10,
  HeadPitch: 1,
  ArmLUpper: new Vector3(0.18, -0.90, 0.26),
  ArmLFore: new Vector3(0.00, -0.10, 0.99),
  ArmRUpper: new Vector3(-0.18, -0.92, 0.18),
  ArmRFore: new Vector3(0.00, -0.05, 0.99),
  HandRollL: 45,
  HandRollR: -48,
  FootL: new Vector3(-0.05, 0, 0.11),
  FootR: new Vector3(0.05, 0, -0.14),
  IdleArmUpper: new Vector3(0.30, -1, 0.02),
  IdleArmFore: new Vector3(0.10, -1, 0.30),
  DownFallSeconds: 0.95,
} as const;

const DEG = Math.PI / 180;

function qAxis(axis: Vector3, degrees: number): Quaternion {
  return new Quaternion().setFromAxisAngle(axis, degrees * DEG);
}

function mirrorX(v: Vector3): Vector3 {
  return new Vector3(-v.x, v.y, v.z);
}

/**
 * Three.js 版 `ProceduralRig`：ボーンを「向き」で操作する薄い層。
 * 回転軸は決め打ちせず、キャラクタ基準の軸（RIG_RIGHT / RIG_UP / RIG_FORWARD）で回す。
 */
export class ProceduralRig {
  readonly root: Object3D;
  readonly skeleton: JointSkeleton;
  readonly rest = new Map<Object3D, { position: Vector3; quaternion: Quaternion }>();
  readonly restRelativeToRoot = new Map<Object3D, Quaternion>();
  readonly snapshotRotations = new Map<Object3D, Quaternion>();

  restHipHeight = 0;
  legLength = 0;
  readonly restFootL = new Vector3();
  readonly restFootR = new Vector3();
  readonly restHips = new Vector3();

  private snapshotHips = new Vector3();

  constructor(root: Object3D, skeleton: JointSkeleton) {
    this.root = root;
    this.skeleton = skeleton;
    this.capture();
  }

  get ready(): boolean {
    const b = this.skeleton.bones;
    return !!(b.hips && b.lUpperLeg && b.lLowerLeg && b.lFoot && b.rUpperLeg && b.rLowerLeg && b.rFoot);
  }

  get bones() {
    return this.skeleton.bones;
  }

  /** 現在のボーン状態を初期姿勢として控える（Unity `Capture()` 相当）。 */
  capture(): void {
    this.rest.clear();
    this.restRelativeToRoot.clear();
    for (const bone of Object.values(this.bones)) {
      if (!bone) continue;
      this.rest.set(bone, { position: bone.position.clone(), quaternion: bone.quaternion.clone() });
      this.restRelativeToRoot.set(bone, relativeRotationToRoot(this.root, bone));
    }

    const b = this.bones;
    if (!b.hips) return;
    this.restHipHeight = b.hips.getWorldPosition(new Vector3()).y - this.root.getWorldPosition(new Vector3()).y;

    if (b.lUpperLeg && b.lLowerLeg && b.lFoot) {
      this.legLength = distance(b.lUpperLeg, b.lLowerLeg) + distance(b.lLowerLeg, b.lFoot);
    }
    if (b.lFoot) this.restFootL.copy(this.root.worldToLocal(b.lFoot.getWorldPosition(new Vector3())));
    if (b.rFoot) this.restFootR.copy(this.root.worldToLocal(b.rFoot.getWorldPosition(new Vector3())));
    this.restHips.copy(this.root.worldToLocal(b.hips.getWorldPosition(new Vector3())));
  }

  resetPose(): void {
    for (const [bone, rest] of this.rest) {
      bone.position.copy(rest.position);
      bone.quaternion.copy(rest.quaternion);
    }
  }

  /** 状態遷移時に呼ぶ（Unity `SnapshotPose()` 相当）。 */
  snapshotPose(): void {
    this.snapshotRotations.clear();
    for (const bone of Object.values(this.bones)) {
      if (bone) this.snapshotRotations.set(bone, bone.quaternion.clone());
    }
    const hips = this.bones.hips;
    if (hips) this.snapshotHips.copy(hips.position);
  }

  /** t=0 で控えた姿勢、t=1 で現在の姿勢。 */
  blendFromSnapshot(t: number): void {
    if (this.snapshotRotations.size === 0) return;
    const k = clamp01(t);
    for (const [bone, q] of this.snapshotRotations) {
      bone.quaternion.copy(q).slerp(bone.quaternion, k);
    }
    const hips = this.bones.hips;
    if (hips) hips.position.lerpVectors(this.snapshotHips, hips.position, k);
  }

  tryGetRestRotation(bone: Object3D): Quaternion | undefined {
    return this.restRelativeToRoot.get(bone);
  }

  /**
   * ワールド空間の回転差をボーンのローカル回転へ反映する。
   * newLocal = parentWorld⁻¹ * delta * parentWorld * local
   * （bone 自身の world ではなく親の world を使う点が重要）。
   */
  private applyWorldDelta(bone: Object3D, delta: Quaternion): void {
    const parentWorld = bone.parent
      ? bone.parent.getWorldQuaternion(new Quaternion())
      : new Quaternion();
    bone.quaternion.premultiply(parentWorld.invert()).premultiply(delta).premultiply(parentWorld);
  }

  /** bone の先端部を worldDirection へ向ける（Unity `Aim` 相当）。 */
  aim(bone: Object3D | null, tip: Object3D | null, worldDirection: Vector3): void {
    if (!bone || !tip) return;
    const current = tip.getWorldPosition(new Vector3()).sub(bone.getWorldPosition(new Vector3()));
    if (current.lengthSq() < 1e-8 || worldDirection.lengthSq() < 1e-8) return;

    const delta = new Quaternion().setFromUnitVectors(current.normalize(), worldDirection.normalize());
    this.applyWorldDelta(bone, delta);
  }

  /** 2 ボーン IK（Unity `SolveTwoBone` 相当）。pole は関節が曲がる方向の目印。 */
  solveTwoBone(
    upper: Object3D | null,
    mid: Object3D | null,
    end: Object3D | null,
    target: Vector3,
    pole: Vector3,
    fallbackNormal: Vector3 = new Vector3(),
  ): void {
    if (!upper || !mid || !end) return;
    const root = upper.getWorldPosition(new Vector3());
    const l1 = distance(upper, mid);
    const l2 = distance(mid, end);
    if (l1 < 1e-5 || l2 < 1e-5) return;

    const toTarget = target.clone().sub(root);
    const rawDistance = toTarget.length();
    if (rawDistance < 1e-5) return;

    const reachMargin = 0.02;
    const distanceToTarget = clamp(rawDistance, Math.abs(l1 - l2) + reachMargin, l1 + l2 - reachMargin);
    const direction = toTarget.clone().divideScalar(rawDistance);

    const cosAngle = (l1 * l1 + distanceToTarget * distanceToTarget - l2 * l2) / (2 * l1 * distanceToTarget);
    const angle = Math.acos(clamp(cosAngle, -1, 1));

    const poleDirection = pole.clone().sub(root);
    let normal = new Vector3().crossVectors(direction, poleDirection);
    const degenerate = normal.lengthSq() <
      1e-4 * direction.lengthSq() * Math.max(poleDirection.lengthSq(), 1e-8);
    if (degenerate) {
      normal = fallbackNormal.clone();
      if (normal.lengthSq() < 1e-6) normal.crossVectors(direction, RIG_UP);
      if (normal.lengthSq() < 1e-6) normal.crossVectors(direction, RIG_RIGHT);
    }
    normal.normalize();

    let upperDirection = direction.clone().applyQuaternion(qAxis(normal, angle / DEG));
    if (degenerate && upperDirection.dot(poleDirection) < 0) {
      normal.negate();
      upperDirection = direction.clone().applyQuaternion(qAxis(normal, angle / DEG));
    }

    this.aim(upper, mid, upperDirection);
    this.aim(mid, end, target.clone().sub(mid.getWorldPosition(new Vector3())));
  }

  solveLeg(left: boolean, worldTarget: Vector3, worldPole: Vector3): boolean {
    const b = this.bones;
    const upper = left ? b.lUpperLeg : b.rUpperLeg;
    const lower = left ? b.lLowerLeg : b.rLowerLeg;
    const foot = left ? b.lFoot : b.rFoot;
    this.solveTwoBone(upper, lower, foot, worldTarget, worldPole, RIG_RIGHT);
    return !!(upper && lower && foot);
  }

  /** 「外へ開く(x)・高さ(y)・前後(z)」指定で腕を置く（Unity `SetArm` 相当）。 */
  setArm(left: boolean, upperDirection: Vector3, forearmDirection: Vector3, handRoll = 0): void {
    const b = this.bones;
    const upper = left ? b.lUpperArm : b.rUpperArm;
    const lower = left ? b.lLowerArm : b.rLowerArm;
    const hand = left ? b.lHand : b.rHand;
    this.aim(upper, lower, upperDirection);
    this.aim(lower, hand, forearmDirection);
    if (Math.abs(handRoll) > 0.01 && lower && hand) {
      const axis = hand.getWorldPosition(new Vector3()).sub(lower.getWorldPosition(new Vector3()));
      if (axis.lengthSq() > 1e-8) {
        this.applyWorldDelta(hand, qAxis(axis.normalize(), handRoll));
      }
    }
  }

  /** 腰をワールド座標で動かす（親がある場合は親ローカルへ変換）。 */
  offsetHips(worldOffset: Vector3): void {
    const hips = this.bones.hips;
    if (!hips) return;
    const parentWorld = hips.parent
      ? hips.parent.getWorldQuaternion(new Quaternion())
      : new Quaternion();
    hips.position.add(worldOffset.clone().applyQuaternion(parentWorld.invert()));
  }

  /** 体幹をキャラクタのローカル軸まわりに曲げる（Unity `BendTorso` 相当）。 */
  bendTorso(pitch: number, roll: number, yaw: number): void {
    const spine = this.bones.spine;
    if (!spine) return;
    const local = qAxis(RIG_UP, yaw).multiply(qAxis(RIG_RIGHT, pitch)).multiply(qAxis(RIG_FORWARD, roll));
    const basis = this.root.getWorldQuaternion(new Quaternion());
    const world = basis.clone().multiply(local).multiply(basis.clone().invert());
    this.applyWorldDelta(spine, world);

    const chest = this.bones.chest;
    if (chest) {
      const half = qAxis(RIG_UP, yaw * 0.4).multiply(qAxis(RIG_RIGHT, pitch * 0.4))
        .multiply(qAxis(RIG_FORWARD, roll * 0.4));
      const halfWorld = basis.clone().multiply(half).multiply(basis.clone().invert());
      this.applyWorldDelta(chest, halfWorld);
    }
  }

  /** 頭を回す（Unity `TurnHead` 相当）。 */
  turnHead(pitch: number, yaw: number, roll = 0): void {
    const head = this.bones.head;
    if (!head) return;
    const local = qAxis(RIG_UP, yaw).multiply(qAxis(RIG_RIGHT, pitch)).multiply(qAxis(RIG_FORWARD, roll));
    const basis = this.root.getWorldQuaternion(new Quaternion());
    const world = basis.clone().multiply(local).multiply(basis.clone().invert());
    this.applyWorldDelta(head, world);
  }

  /** 体全体を腰を支点に倒す（Unity `LayBody` 相当）。 */
  layBody(degrees: number, sideDegrees = 0): void {
    const hips = this.bones.hips;
    if (!hips) return;
    let axis = RIG_RIGHT.clone();
    if (Math.abs(sideDegrees) > 0.01) axis = axis.applyQuaternion(qAxis(RIG_UP, sideDegrees));
    this.applyWorldDelta(hips, qAxis(axis, degrees));
  }

  /** 脚を初期姿勢へ寄せる（ Unity `BlendLegsToRest` 相当）。 */
  blendLegsToRest(t: number): void {
    if (t <= 0) return;
    for (const key of ['lUpperLeg', 'lLowerLeg', 'lFoot', 'rUpperLeg', 'rLowerLeg', 'rFoot'] as const) {
      const bone = this.bones[key];
      const rest = bone ? this.rest.get(bone) : undefined;
      if (bone && rest) bone.quaternion.slerp(rest.quaternion, clamp01(t));
    }
  }

  /** しゃがみへ寄せる（Unity `BlendLegsToCrouch` 相当、軸は体の右）。 */
  blendLegsToCrouch(t: number): void {
    if (t <= 0) return;
    const offsets: ReadonlyArray<[keyof typeof this.bones, number]> = [
      ['lUpperLeg', -68], ['lLowerLeg', 96], ['lFoot', -24],
      ['rUpperLeg', -68], ['rLowerLeg', 96], ['rFoot', -24],
    ];
    for (const [key, deg] of offsets) {
      const bone = this.bones[key];
      const rest = bone ? this.rest.get(bone) : undefined;
      if (!bone || !rest) continue;
      bone.quaternion.copy(rest.quaternion).multiply(qAxis(RIG_RIGHT, deg * clamp01(t)));
    }
  }

  /** 指を握る（VRM の指ボーンはモデル依存のため、いまは Grip を保持するだけ）。 */
  curlFingers(_left: boolean, _amount: number): void {
    // 指ボーンは JointSkeleton に含まれていないため、現段階では no-op。
    // 残課題: 指の曲がる軸を CaptureFingerAxis 相当で求めて適用する。
  }

  getHeadPosition(): Vector3 | null {
    return this.bones.head?.getWorldPosition(new Vector3()) ?? null;
  }
}

function distance(a: Object3D, b: Object3D): number {
  return a.getWorldPosition(new Vector3()).distanceTo(b.getWorldPosition(new Vector3()));
}

/** CharacterAnimator の設定（Unity の public フィールドに対応）。 */
export interface AnimatorSettings {
  strideLength: number;
  stepHeight: number;
  armSwing: number;
  idleBlendSeconds: number;
  attackBlendSeconds: number;
  downBlendSeconds: number;
  guardBlendSeconds: number;
  getUpSeconds: number;
  downArmLUpper: Vector3;
  downArmLFore: Vector3;
  downArmRUpper: Vector3;
  downArmRFore: Vector3;
}

export const DEFAULT_ANIMATOR_SETTINGS: AnimatorSettings = {
  strideLength: 0.42,
  stepHeight: 0.075,
  armSwing: 0.85,
  idleBlendSeconds: 0.16,
  attackBlendSeconds: 0.06,
  downBlendSeconds: 0.10,
  guardBlendSeconds: 0.09,
  getUpSeconds: 0.80,
  downArmLUpper: new Vector3(0.42, -0.82, 0.18),
  downArmLFore: new Vector3(0.28, -0.45, 0.78),
  downArmRUpper: new Vector3(-0.32, -0.86, 0.28),
  downArmRFore: new Vector3(0.55, -0.20, 0.76),
};

/**
 * Unity `CharacterAnimator` の手続きポーズ・状態機械。
 *
 * logic への直接依存を避けるため、外部の状態（死亡・攻撃中・掴まれ・移動など）は
 * `AnimatorInput` として注入する。`anchor` も注入前提（ここでは保持するだけ）。
 */
export interface AnimatorInput {
  readonly isDead?: boolean;
  readonly isStunned?: boolean;
  readonly stunRemaining?: number;
  readonly isMoving?: boolean;
  readonly isAttacking?: boolean;
  readonly isGrappled?: boolean;
  readonly isDown?: boolean;
  readonly downRemaining?: number;
  readonly getUpRequested?: boolean;
  readonly planarSpeed?: number;
  /** 体から見た進行方向 (x=横, y=前)。 */
  readonly walkDir?: Vector2;
  readonly time: number;
  readonly dt: number;
}

/** 脚のアンカー（logic から注入される想定）。rig は参照するだけ。 */
export interface RigAnchors {
  readonly punchAnchor?: (zone: string) => Vector3 | null;
}

export class CharacterAnimator {
  readonly rig: ProceduralRig;
  readonly settings: AnimatorSettings;
  anchors: RigAnchors = {};

  state = AnimatorState.Idle;
  stateTime = 0;
  stateDuration = 0;

  /** Diagnostic: 直近の ApplyPose で渡した腕・体幹の向き。 */
  readonly debug = {
    armLUpper: new Vector3(), armLFore: new Vector3(),
    armRUpper: new Vector3(), armRFore: new Vector3(),
    torsoPitch: 0, headPitch: 0, torsoYaw: 0, headYaw: 0,
  };

  private lastPosition = new Vector3();
  private planarSpeedValue = 0;
  private walkDir = new Vector2(0, 1);
  private walkPhase = 0;
  private lastWalkU = -1;
  private blend = 1;
  private blendSeconds = 0.16;
  private guardRecoil = 0;
  private downForward = false;
  private downLaunched = false;
  private downFallSeconds = STANCE.DownFallSeconds;
  private downEndLay = -92;
  private armFollow = { yaw: 0, pitch: 0, roll: 0 };
  private leftOut = new Vector3(-1, 0, 0);
  private rightOut = new Vector3(1, 0, 0);
  private axesReady = false;

  constructor(rig: ProceduralRig, settings: AnimatorSettings = DEFAULT_ANIMATOR_SETTINGS) {
    this.rig = rig;
    this.settings = settings;
    this.blendSeconds = settings.idleBlendSeconds;
  }

  /** 状態を即座に切り替える（Unity `SetState` 相当）。 */
  setState(state: AnimatorState, duration: number): void {
    this.state = state;
    this.stateTime = 0;
    this.stateDuration = duration;
    if (state === AnimatorState.Down) {
      this.downForward = false;
      this.downLaunched = false;
      this.downFallSeconds = STANCE.DownFallSeconds;
      this.downEndLay = -92;
    }
  }

  /** 迁移動 detección + 姿勢適用（Unity `LateUpdate` 相当）。 */
  tick(input: AnimatorInput): void {
    if (!this.rig.ready) return;
    const dt = Math.max(0, input.dt);
    this.stateTime += dt;
    this.guardRecoil = Math.max(0, this.guardRecoil - dt);

    const rootPos = this.rig.root.getWorldPosition(new Vector3());
    if (dt > 1e-4) {
      const move = rootPos.clone().sub(this.lastPosition);
      const instant = Math.hypot(move.x, move.z) / dt;
      this.planarSpeedValue = lerp(this.planarSpeedValue, instant, 1 - Math.exp(-18 * dt));
      const flat = new Vector2(move.x, move.z);
      if (flat.lengthSq() > 1e-8) {
        const wanted = input.walkDir ?? flat.normalize();
        const k = 1 - Math.exp(-12 * dt);
        this.walkDir.lerp(wanted, k).normalize();
      }
    }
    this.lastPosition.copy(rootPos);

    const before = this.state;
    this.updateState(input);

    if (this.state !== before) {
      this.rig.snapshotPose();
      this.blend = 0;
      this.blendSeconds = this.blendSecondsFor(this.state, before);
    }

    if (this.state === AnimatorState.Walk) {
      const stride = this.stepLength();
      const lat = Math.abs(this.walkDir.x);
      const effective = lerp(stride, stride * 0.45, lat);
      this.walkPhase += dt * (Math.PI * Math.max(this.planarSpeedValue, 0.15) / effective);
    } else {
      this.lastWalkU = -1;
    }

    this.applyPose(input.time, dt);
  }

  private blendSecondsFor(state: AnimatorState, before: AnimatorState): number {
    switch (state) {
      case AnimatorState.Attack: return this.settings.attackBlendSeconds;
      case AnimatorState.Stagger: return 0.04;
      case AnimatorState.Down: return this.settings.downBlendSeconds;
      case AnimatorState.GetUp: return before === AnimatorState.Down ? 0.05 : 0.05;
      case AnimatorState.Guard: return this.settings.guardBlendSeconds;
      case AnimatorState.Grabbed: return before === AnimatorState.Down ? 0.25 : 0.08;
      case AnimatorState.Idle:
        return before === AnimatorState.Stagger ? 0.30 : this.settings.idleBlendSeconds;
      default: return this.settings.idleBlendSeconds;
    }
  }

  private updateState(input: AnimatorInput): void {
    const held = input.isGrappled === true && input.isDead !== true;
    if (held) {
      if (this.state !== AnimatorState.Grabbed) this.setState(AnimatorState.Grabbed, 0);
      return;
    }
    if (this.state === AnimatorState.Grabbed) {
      this.state = input.isDead ? AnimatorState.Down : AnimatorState.Idle;
      if (this.state === AnimatorState.Down) this.setState(AnimatorState.Down, 999);
    }

    switch (this.state) {
      case AnimatorState.Stagger:
        if (this.stateTime >= this.stateDuration) this.state = AnimatorState.Idle;
        break;
      case AnimatorState.Down:
        if (input.isDead !== true && (input.isDown !== true || input.getUpRequested === true)) {
          const up = Math.max(0.2, this.settings.getUpSeconds);
          this.setState(AnimatorState.GetUp, up);
        }
        break;
      case AnimatorState.GetUp:
        if (this.stateTime >= this.stateDuration) this.state = AnimatorState.Idle;
        break;
      case AnimatorState.Attack:
        if (input.isAttacking !== true) this.state = AnimatorState.Idle;
        break;
      default:
        if (input.isDead === true) {
          this.setState(AnimatorState.Down, 999);
        } else if (input.isAttacking === true) {
          this.setState(AnimatorState.Attack, 0);
        } else if (input.isStunned === true) {
          this.setState(AnimatorState.Stagger, Math.max(0.1, input.stunRemaining ?? 0.1));
        } else {
          this.state = input.isMoving === true ? AnimatorState.Walk : AnimatorState.Idle;
        }
        break;
    }
  }

  private stepLength(): number {
    return clamp(this.planarSpeedValue * this.settings.strideLength, 0.30, 1.10);
  }

  /** 1 フレームの姿勢を評価してリグへ流し込む（Unity `ApplyPose` 相当）。 */
  applyPose(time: number, dt: number, overrideState?: AnimatorState): PoseValues {
    const state = overrideState ?? this.state;
    this.rig.resetPose();
    const pose = this.buildPose(state, time);
    this.applyPoseToRig(pose, dt);
    return pose;
  }

  private buildPose(state: AnimatorState, time: number): PoseValues {
    switch (state) {
      case AnimatorState.Idle: return this.poseIdle(time);
      case AnimatorState.Guard: return this.poseGuard(time);
      case AnimatorState.Walk: return this.poseWalk();
      case AnimatorState.Stagger: return this.poseStagger();
      case AnimatorState.Down: return this.poseDown();
      case AnimatorState.GetUp: return this.poseGetUp();
      case AnimatorState.Attack: return this.poseAttack();
      case AnimatorState.Grabbed: return this.poseGrabbed();
      default: return this.poseDefaults();
    }
  }

  private poseDefaults(): PoseValues {
    return createPoseValues({
      footL: this.rig.restFootL.clone(),
      footR: this.rig.restFootR.clone(),
      armLUpper: STANCE.IdleArmUpper.clone(),
      armLFore: STANCE.IdleArmFore.clone(),
      armRUpper: mirrorX(STANCE.IdleArmUpper),
      armRFore: mirrorX(STANCE.IdleArmFore),
      grip: 0.30,
    });
  }

  /** 立ち（ファイティングスタンス）。呼吸と体重移動だけを足す。 */
  poseIdle(time: number): PoseValues {
    const pose = this.poseDefaults();
    const shift = Math.sin(time * 0.62);
    const bounce = Math.sin(time * 3.4) * 0.004;
    const slowBreath = Math.sin(time * 1.7);
    const breathHips = Math.sin(time * 1.7 - 0.30);
    const breathHead = Math.sin(time * 1.7 - 0.85);

    pose.hipsOffset.y = STANCE.HipsY + breathHips * 0.006 + bounce;
    pose.hipsOffset.x = shift * 0.010;
    pose.hipsOffset.z = STANCE.HipsZ;

    pose.torsoPitch = STANCE.TorsoPitch + slowBreath * 0.7;
    pose.torsoYaw = STANCE.TorsoYaw + shift * 2.0;
    pose.torsoRoll = shift * 1.0;
    pose.headYaw = STANCE.HeadYaw - shift * 2.0;
    pose.headPitch = STANCE.HeadPitch + breathHead * 0.5;

    pose.armLUpper.copy(STANCE.ArmLUpper);
    pose.armLFore.copy(STANCE.ArmLFore);
    pose.armRUpper.copy(STANCE.ArmRUpper);
    pose.armRFore.copy(STANCE.ArmRFore);

    const armBreath = slowBreath * 0.013;
    pose.armLUpper.y += armBreath;
    pose.armLFore.y += armBreath * 0.6;
    pose.armRUpper.y += armBreath * 0.8;
    pose.armRFore.y += armBreath * 0.5;

    pose.handRollL = STANCE.HandRollL + Math.sin(time * 0.83) * 2.5;
    pose.handRollR = STANCE.HandRollR + Math.sin(time * 0.71 + 1.7) * 2.5;
    pose.grip = 0.95;

    pose.footL.add(STANCE.FootL);
    pose.footR.add(STANCE.FootR);
    return pose;
  }

  /** ガード。 */
  poseGuard(time: number): PoseValues {
    const pose = this.poseDefaults();
    const recoil = smoothstep(clamp01(this.guardRecoil / 0.18));
    const sway = Math.sin(time * 2.1) * 0.004;

    pose.armLUpper.set(0.16, -0.55, 0.62 + recoil * 0.08);
    pose.armLFore.set(0.03, 0.85 - recoil * 0.10, 0.30 - recoil * 0.14);
    pose.armRUpper.copy(pose.armLUpper);
    pose.armRFore.copy(pose.armLFore);
    pose.grip = 0.92;

    pose.hipsOffset.y = STANCE.HipsY - 0.045 - recoil * 0.012 + sway;
    pose.hipsOffset.z = STANCE.HipsZ - 0.035 * recoil;
    pose.torsoPitch = 8 - recoil * 12;
    pose.torsoYaw = -3;
    pose.headPitch = 4 + recoil * 7;

    pose.footR.add(new Vector3(0.030, 0, -0.100));
    pose.footL.add(new Vector3(-0.030, 0, 0.050));
    return pose;
  }

  /** 歩き。 */
  poseWalk(): PoseValues {
    const pose = this.poseDefaults();
    const p = this.walkPhase;
    const lat = Math.abs(this.walkDir.x);
    const stepLen = lerp(this.stepLength(), this.stepLength() * 0.45, lat);
    const half = stepLen * 0.5;
    const dir = this.walkDir;
    const u = ((p / (2 * Math.PI)) % 1 + 1) % 1;

    const footOffset = (uu: number): Vector3 => {
      let sweep: number;
      let lift = 0;
      if (uu < 0.5) {
        sweep = half - uu * 2 * stepLen;
      } else {
        const s = (uu - 0.5) * 2;
        sweep = -half + s * stepLen;
        lift = Math.sin(s * Math.PI) * this.settings.stepHeight;
      }
      return new Vector3(dir.x * sweep, lift, dir.y * sweep);
    };

    const stanceL = STANCE.FootL.clone().add(new Vector3(-0.035 * lat, 0, 0));
    const stanceR = STANCE.FootR.clone().add(new Vector3(0.035 * lat, 0, 0));
    pose.footL.add(stanceL).add(footOffset(u));
    pose.footR.add(stanceR).add(footOffset((u + 0.5) % 1));

    pose.hipsOffset.y = STANCE.HipsY - Math.abs(Math.cos(p)) * 0.018;
    pose.hipsOffset.x = Math.sin(p) * 0.012;
    pose.hipsOffset.z = STANCE.HipsZ;

    pose.torsoPitch = STANCE.TorsoPitch + 0.5 * dir.y;
    pose.torsoRoll = -dir.x * 3;
    pose.torsoYaw = STANCE.TorsoYaw + Math.sin(p) * 3;
    pose.headYaw = STANCE.HeadYaw - Math.sin(p) * 2;
    pose.headPitch = STANCE.HeadPitch;

    const swing = Math.cos(p) * this.settings.armSwing * 0.55;
    pose.armLUpper.copy(STANCE.ArmLUpper).add(new Vector3(0.02, 0, -swing * 0.40));
    pose.armLFore.copy(STANCE.ArmLFore).add(new Vector3(0, 0, -swing * 0.28));
    pose.armRUpper.copy(STANCE.ArmRUpper).add(new Vector3(-0.02, 0, swing * 0.40));
    pose.armRFore.copy(STANCE.ArmRFore).add(new Vector3(0, 0, swing * 0.28));
    pose.handRollL = STANCE.HandRollL;
    pose.handRollR = STANCE.HandRollR;
    pose.grip = 0.95;
    return pose;
  }

  /** 怯み（簡易版）。Unity の 4 段・顔/腹・方向別は残課題。 */
  poseStagger(): PoseValues {
    const pose = this.poseDefaults();
    const t = clamp01(this.stateTime / Math.max(0.01, this.stateDuration));
    const bend = 42 * Math.sin(Math.PI * t);
    pose.hipsOffset.y = STANCE.HipsY - 0.075 * (bend / 60);
    pose.hipsOffset.z = STANCE.HipsZ - 0.09 * (bend / 60);
    pose.torsoPitch = STANCE.TorsoPitch + bend;
    pose.headPitch = -bend * 0.35;
    pose.armLUpper.copy(STANCE.ArmLUpper).lerp(new Vector3(0.10, -0.45, 0.85), bend / 60);
    pose.armRUpper.copy(STANCE.ArmRUpper).lerp(new Vector3(-0.10, -0.45, 0.85), bend / 60);
    pose.footL.add(STANCE.FootL);
    pose.footR.add(STANCE.FootR);
    pose.grip = 0.6;
    return pose;
  }

  /** ダウン（簡易版）。Unity の地面ロール・うつ伏せ・引きずり等は残課題。 */
  poseDown(): PoseValues {
    const pose = this.poseDefaults();
    const fallT = clamp01(this.stateTime / Math.max(0.05, this.downFallSeconds));
    const lay = smoothstep(fallT) * this.downEndLay;
    pose.layAngle = lay;
    pose.hipsOffset.y = STANCE.HipsY + lerp(0, -0.84, fallT);
    pose.hipsOffset.z = STANCE.HipsZ + lerp(0, -0.87, fallT);
    pose.torsoPitch = STANCE.TorsoPitch + 10 * fallT;
    pose.armLUpper.copy(STANCE.ArmLUpper).lerp(this.settings.downArmLUpper, fallT);
    pose.armLFore.copy(STANCE.ArmLFore).lerp(this.settings.downArmLFore, fallT);
    pose.armRUpper.copy(STANCE.ArmRUpper).lerp(this.settings.downArmRUpper, fallT);
    pose.armRFore.copy(STANCE.ArmRFore).lerp(this.settings.downArmRFore, fallT);
    pose.legTuck = 1 - fallT;
    pose.grip = 0.4;
    return pose;
  }

  /** 立ち上がり（簡易版）。 */
  poseGetUp(): PoseValues {
    const pose = this.poseDown();
    const t = clamp01(this.stateTime / Math.max(0.01, this.stateDuration));
    const stand = smoothstep(clamp01((t - 0.2) / 0.8));
    const target = this.poseIdle(0);
    pose.layAngle = lerp(pose.layAngle, 0, stand);
    pose.hipsOffset.lerp(target.hipsOffset, stand);
    pose.armLUpper.lerp(target.armLUpper, stand);
    pose.armLFore.lerp(target.armLFore, stand);
    pose.armRUpper.lerp(target.armRUpper, stand);
    pose.armRFore.lerp(target.armRFore, stand);
    pose.legTuck = 0;
    pose.grip = lerp(0.4, 0.95, stand);
    return pose;
  }

  /** 攻撃（簡易版）。Unity の 3 段コンボ・ AttackData 依存は残課題。 */
  poseAttack(): PoseValues {
    const pose = this.poseDefaults();
    const t = this.stateDuration > 0 ? clamp01(this.stateTime / this.stateDuration) :
      clamp01((this.stateTime % 0.5) / 0.5);
    const windup = smoothstep(clamp01(t / 0.3));
    const strike = smoothstep(clamp01((t - 0.3) / 0.25));
    const k = windup * (1 - strike * 0.6);
    pose.armRUpper.copy(STANCE.ArmRUpper).lerp(new Vector3(-0.45, -0.30, 0.84), k);
    pose.armRFore.copy(STANCE.ArmRFore).lerp(new Vector3(0, 0.10, 0.99), strike);
    pose.armLUpper.copy(STANCE.ArmLUpper).lerp(new Vector3(0.20, -0.70, 0.40), k * 0.5);
    pose.armLFore.copy(STANCE.ArmLFore);
    pose.torsoPitch = STANCE.TorsoPitch + 6 * strike;
    pose.torsoYaw = STANCE.TorsoYaw + 12 * strike;
    pose.hipsOffset.y = STANCE.HipsY - 0.02 * strike;
    pose.footL.add(STANCE.FootL);
    pose.footR.add(STANCE.FootR);
    pose.grip = 1;
    return pose;
  }

  /** 掴まれ（簡易版）。Unity の GrappleKind 別は残課題。 */
  poseGrabbed(): PoseValues {
    const pose = this.poseDefaults();
    pose.hipsOffset.y = STANCE.HipsY - 0.08;
    pose.torsoPitch = STANCE.TorsoPitch + 10;
    pose.headPitch = STANCE.HeadPitch + 6;
    pose.armLUpper.set(0.24, -0.55, 0.72);
    pose.armLFore.set(0.02, 0.22, 0.97);
    pose.armRUpper.set(-0.24, -0.55, 0.72);
    pose.armRFore.set(-0.02, 0.22, 0.97);
    pose.footL.add(STANCE.FootL);
    pose.footR.add(STANCE.FootR);
    pose.grip = 0.5;
    return pose;
  }

  /** 組み立てた姿勢をリグへ流し込む（Unity `ApplyPoseToRig` 相当）。 */
  private applyPoseToRig(pose: PoseValues, blendDt: number): void {
    const rootQuat = this.rig.root.getWorldQuaternion(new Quaternion());
    const rootInv = rootQuat.clone().invert();
    this.leftOut = this.horizontal(
      (this.rig.bones.lUpperArm?.getWorldPosition(new Vector3()) ?? new Vector3())
        .sub(this.rig.bones.hips?.getWorldPosition(new Vector3()) ?? new Vector3())
        .applyQuaternion(rootInv),
      new Vector3(-1, 0, 0),
    );
    this.rightOut = this.horizontal(
      (this.rig.bones.rUpperArm?.getWorldPosition(new Vector3()) ?? new Vector3())
        .sub(this.rig.bones.hips?.getWorldPosition(new Vector3()) ?? new Vector3())
        .applyQuaternion(rootInv),
      new Vector3(1, 0, 0),
    );
    this.axesReady = true;

    this.armFollow.yaw = pose.torsoYaw;
    this.armFollow.pitch = pose.torsoPitch;
    this.armFollow.roll = pose.torsoRoll;

    this.rig.offsetHips(pose.hipsOffset.clone().applyQuaternion(rootQuat));
    this.rig.bendTorso(pose.torsoPitch, pose.torsoRoll, pose.torsoYaw);
    this.rig.turnHead(pose.headPitch, pose.headYaw, pose.headRoll);

    this.rig.setArm(true, this.armDirection(true, pose.armLUpper).normalize(),
      this.armDirection(true, pose.armLFore).normalize(), pose.handRollL);
    this.rig.setArm(false, this.armDirection(false, pose.armRUpper).normalize(),
      this.armDirection(false, pose.armRFore).normalize(), pose.handRollR);

    this.rig.curlFingers(true, pose.grip);
    this.rig.curlFingers(false, pose.grip);

    const hipMove = pose.hipsOffset.length();
    const needLeg = hipMove > 0.02 ||
      pose.footL.distanceToSquared(this.rig.restFootL) > 0.0004 ||
      pose.footR.distanceToSquared(this.rig.restFootR) > 0.0004;

    const pole = this.rig.root.getWorldPosition(new Vector3())
      .add(RIG_FORWARD.clone().applyQuaternion(rootQuat).multiplyScalar(2))
      .addScaledVector(RIG_UP, 0.55);

    if (needLeg) {
      const toWorld = (local: Vector3) => this.rig.root.localToWorld(local.clone());
      this.rig.solveLeg(true, toWorld(pose.footL), pole);
      this.rig.solveLeg(false, toWorld(pose.footR), pole);
    } else if (pose.legTuck > 0.001) {
      this.rig.blendLegsToCrouch(pose.legTuck);
    } else if (pose.layAngle < -1) {
      this.rig.blendLegsToRest(clamp01(-pose.layAngle / 88) * 0.96);
    }

    if (Math.abs(pose.layAngle) > 0.01) this.rig.layBody(pose.layAngle, pose.laySide);

    if (this.blend < 1) {
      this.blend = Math.min(1, this.blend + blendDt / Math.max(0.01, this.blendSeconds));
      this.rig.blendFromSnapshot(smoothstep(this.blend));
    }

    this.debug.armLUpper.copy(this.armDirection(true, pose.armLUpper));
    this.debug.armLFore.copy(this.armDirection(true, pose.armLFore));
    this.debug.armRUpper.copy(this.armDirection(false, pose.armRUpper));
    this.debug.armRFore.copy(this.armDirection(false, pose.armRFore));
    this.debug.torsoPitch = pose.torsoPitch;
    this.debug.headPitch = pose.headPitch;
    this.debug.torsoYaw = pose.torsoYaw;
    this.debug.headYaw = pose.headYaw;
  }

  private horizontal(v: Vector3, fallback: Vector3): Vector3 {
    v.y = 0;
    return v.lengthSq() < 1e-6 ? fallback.clone() : v.normalize();
  }

  /** 「開き・高さ・前後」指定を実際のボーン向きへ変換（Unity `ArmDirection` 相当）。 */
  armDirection(left: boolean, spec: Vector3): Vector3 {
    if (!this.axesReady) {
      this.leftOut = new Vector3(-1, 0, 0);
      this.rightOut = new Vector3(1, 0, 0);
    }
    const basis = this.rig.root.getWorldQuaternion(new Quaternion());
    const side = (left ? this.leftOut : this.rightOut).clone().applyQuaternion(basis);
    const forward = RIG_FORWARD.clone().applyQuaternion(basis);
    forward.y = 0;
    if (forward.lengthSq() < 1e-6) forward.copy(RIG_FORWARD);
    forward.normalize();

    let dir = side.multiplyScalar(Math.abs(spec.x))
      .addScaledVector(RIG_UP, spec.y)
      .addScaledVector(forward, spec.z);

    // 体幹が回った分だけ肩の面も回す（Spine 1.0 + Chest 0.4 = 1.4）。
    const share = this.state === AnimatorState.Attack ? 0.35 : 1;
    const torso = qAxis(RIG_UP, this.armFollow.yaw * 1.4 * share)
      .multiply(qAxis(RIG_RIGHT, this.armFollow.pitch * 1.4 * share))
      .multiply(qAxis(RIG_FORWARD, this.armFollow.roll * 1.4 * share));
    const worldTorso = basis.clone().multiply(torso).multiply(basis.clone().invert());
    return dir.applyQuaternion(worldTorso);
  }
}
