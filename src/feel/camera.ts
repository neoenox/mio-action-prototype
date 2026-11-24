import { Euler, MathUtils, PerspectiveCamera, Quaternion, Vector3 } from 'three';
import type { Clock } from '../core/clock';
import { clamp, smoothstep } from '../core/math';

/**
 * Unity 版 ThirdPersonCamera の「押し込み API」を移植したカメラ制御。
 *
 * 演出側は distance / pivot / focus を直接書かず、Push* / HoldFocus /
 * RequestCinematicShot だけを呼ぶ。距離そのものは SetDistance とホイール側が
 * 持ち、演出は `_distanceScale` など一時的な乗数だけを更新する。
 *
 * 実行順（1 フレーム）:
 *   1. update(unscaledDelta, scaledDelta) ... TickEffects + 構図 +
 *      ApplyCinematicShot（unscaled）
 *   2. CameraShake.updateLate() .......... 前フレーム分を戻してから足す
 */
export interface CameraSettings {
  distance: number;
  minDistance: number;
  maxDistance: number;
  pivotOffset: Vector3;
  followSharpness: number;
  effectDecay: number;
  cinematicExitSeconds: number;
  defaultCinematicSharpness: number;
  closestShotZoom: number;
  /** ベルトモード用（省略時は三人称追従） */
  beltMode?: boolean;
  beltPitch?: number;
  beltYaw?: number;
  beltPivotHeight?: number;
  beltFollowSharpness?: number;
  beltMinX?: number;
  beltMaxX?: number;
  beltDeadZone?: number;
}

export const DEFAULT_CAMERA_SETTINGS: CameraSettings = {
  distance: 5.0,
  minDistance: 0.85,
  maxDistance: 12.0,
  pivotOffset: new Vector3(0, 1.4, 0),
  followSharpness: 5,
  effectDecay: 4,
  cinematicExitSeconds: 0.35,
  defaultCinematicSharpness: 13,
  closestShotZoom: 0.85,
  beltMode: false,
  beltPitch: 12,
  beltYaw: 0,
  beltPivotHeight: 1.25,
  beltFollowSharpness: 5,
  beltMinX: -Infinity,
  beltMaxX: Infinity,
  beltDeadZone: 0,
};

const DEG = Math.PI / 180;
const UP = new Vector3(0, 1, 0);

export class CameraController {
  readonly camera: PerspectiveCamera;
  readonly settings: CameraSettings;

  /** 死亡カメラが引きを押し直している間は true。拘束側の寄りを消さない。 */
  deathFramingActive = false;

  private target: Vector3 | null = null;
  private yaw = 0;
  private pitch = 10;
  private distance: number;
  private initialDistance: number;
  private desiredDistance: number;
  private smoothPivot = new Vector3();
  private beltX = 0;
  private beltLead = 0;
  private framed = false;

  // 演出状態（ThirdPersonCamera と同じ意味）
  private pushScale = 1;
  private pushRemaining = 0;
  private pushDuration = 0;
  private pullScale = 1;
  private pullRemaining = 0;
  private pullDuration = 0;
  private pivotMeters = 0;
  private pivotRemaining = 0;
  private pivotDuration = 0;
  private focusX = 0;
  private focusPitch = 0;
  private focusYaw = 0;
  private focusRemaining = 0;
  private focusDuration = 0;

  private cinematicRequested = false;
  private cinematicActive = false;
  private cinematicCut = false;
  private cinematicPosition = new Vector3();
  private cinematicTarget = new Vector3();
  private cinematicFov = 0;
  private cinematicSharpness = 13;
  private shotPosition = new Vector3();
  private shotTarget = new Vector3();
  private exitFromPosition = new Vector3();
  private exitFromRotation = new Quaternion();
  private lastCinematicPosition = new Vector3();
  private lastCinematicRotation = new Quaternion();
  private lastCinematicCut = false;
  private exitRemaining = 0;

  /** このフレームで実際に使う値（TickEffects が更新） */
  private distanceScale = 1;
  private pivotOffsetY = 0;
  private holdingFocusThisFrame = false;
  private holdingFocusBelt = false;

  private baseFov: number;

  constructor(camera: PerspectiveCamera, settings: Partial<CameraSettings> = {}) {
    this.camera = camera;
    this.settings = { ...DEFAULT_CAMERA_SETTINGS, ...settings };
    this.settings.pivotOffset = settings.pivotOffset?.clone() ?? DEFAULT_CAMERA_SETTINGS.pivotOffset.clone();
    this.distance = this.settings.distance;
    this.initialDistance = this.distance;
    this.desiredDistance = this.distance;
    this.baseFov = camera.fov;
    this.cinematicSharpness = this.settings.defaultCinematicSharpness;
    this.smoothPivot.copy(this.settings.pivotOffset);
  }

  get isFramed(): boolean { return this.framed; }
  get isFocusing(): boolean { return this.focusRemaining > 0; }
  get effectDistanceScale(): number { return this.distanceScale; }
  get effectPivotOffset(): number { return this.pivotOffsetY; }
  get currentX(): number { return this.beltX; }
  get renderedX(): number { return this.smoothPivot.x; }
  get baseFieldOfView(): number { return this.baseFov; }

  setTarget(target: Vector3 | null): void {
    const first = this.target === null && target !== null;
    this.target = target?.clone() ?? null;
    if (target && first) {
      this.beltX = target.x;
      this.smoothPivot.set(target.x, this.smoothPivot.y, this.smoothPivot.z);
    }
  }

  snapTo(x: number): void {
    this.beltX = x;
    this.smoothPivot.x = x;
  }

  setDistance(distance: number): void {
    const min = this.settings.beltMode
      ? Math.max(this.settings.minDistance, this.settings.maxDistance === Infinity ? 0 : this.settings.minDistance)
      : this.settings.minDistance;
    this.distance = MathUtils.clamp(distance, Math.min(min, this.settings.maxDistance), this.settings.maxDistance);
  }

  addYaw(deltaDeg: number): void { this.yaw += deltaDeg; }
  addPitch(deltaDeg: number): void {
    this.pitch = MathUtils.clamp(this.pitch - deltaDeg, -80, 80);
  }

  /**
   * 距離に一時的な倍率を掛ける。1 未満で寄り、1 超で引き。
   * 連続呼び出しでは「より小さい倍率（寄り）／より大きい倍率（引き）」と
   * 「残り時間の長い方」を採用する。seconds <= 0 で即時リセット。
   */
  pushDistance(scale: number, seconds: number): void {
    if (seconds <= 0) { this.resetDistanceEffect(); return; }
    if (scale <= 0) return;
    if (this.deathFramingActive && scale < 1) return;

    if (scale < 1) {
      const live = this.distanceScale < 1 ? this.distanceScale : 1;
      this.pushScale = Math.min(live, scale);
      this.pushRemaining = Math.max(this.pushRemaining, seconds);
      this.pushDuration = Math.max(this.pushDuration, this.pushRemaining);
    } else {
      const live = this.distanceScale > 1 ? this.distanceScale : 1;
      this.pullScale = Math.max(live, scale);
      this.pullRemaining = Math.max(this.pullRemaining, seconds);
      this.pullDuration = Math.max(this.pullDuration, this.pullRemaining);
    }
  }

  /** ピボットを上下させる（負で下げる）。絶対値が大きい方と残り時間の長い方を採用。 */
  pushPivot(meters: number, seconds: number): void {
    if (seconds <= 0) { this.resetPivotEffect(); return; }
    if (Math.abs(meters) < 1e-6) return;
    if (Math.abs(meters) >= Math.abs(this.pivotOffsetY)) this.pivotMeters = meters;
    this.pivotRemaining = Math.max(this.pivotRemaining, seconds);
    this.pivotDuration = Math.max(this.pivotDuration, this.pivotRemaining);
  }

  /** 画面中心 X を一時的に寄せる。pitchOffset / yawOffset も同時に効く。 */
  pushFocus(x: number, seconds: number, pitchOffset = 0, yawOffset = 0): void {
    if (seconds <= 0) {
      this.focusRemaining = 0;
      this.focusDuration = 0;
      this.holdingFocusBelt = false;
      return;
    }
    this.focusX = x;
    this.focusPitch = pitchOffset;
    this.focusYaw = yawOffset;
    this.focusRemaining = Math.max(this.focusRemaining, seconds);
    this.focusDuration = Math.max(this.focusDuration, this.focusRemaining);
  }

  /**
   * 毎フレーム同じ値を入れて減衰を止める。呼ぶのをやめると seconds かけて戻る。
   * 内部で _holdingFocusThisFrame を立て、TickEffects の dt を 0 にする。
   */
  holdFocus(
    distanceScale: number,
    pivotMeters: number,
    x: number,
    seconds: number,
    pitchOffset = 0,
    yawOffset = 0,
  ): void {
    if (seconds <= 0) {
      this.pushFocus(0, 0);
      this.pushDistance(1, 0);
      this.pushPivot(0, 0);
      return;
    }
    if (this.deathFramingActive) return;

    if (distanceScale > 0) {
      this.pushScale = distanceScale;
      this.pushRemaining = seconds;
      this.pushDuration = seconds;
      this.pullScale = 1;
      this.pullRemaining = 0;
      this.pullDuration = 0;
    }
    this.pivotMeters = pivotMeters;
    this.pivotRemaining = seconds;
    this.pivotDuration = seconds;
    this.focusX = x;
    this.focusPitch = pitchOffset;
    this.focusYaw = yawOffset;
    this.focusRemaining = seconds;
    this.focusDuration = seconds;
    this.holdingFocusThisFrame = true;
    this.holdingFocusBelt = true;
  }

  /**
   * 固定ショットを要求する。cut:true のときは補間なしで即座に切り替わる。
   * fov <= 0 なら通常の画角。sharpness <= 0 なら既定値。
   */
  requestCinematicShot(
    position: Vector3,
    target: Vector3,
    cut = false,
    fov = 0,
    sharpness = 0,
  ): void {
    this.cinematicRequested = true;
    this.cinematicCut = cut;
    this.cinematicPosition.copy(position);
    this.cinematicTarget.copy(target);
    this.cinematicFov = fov;
    this.cinematicSharpness = sharpness > 0 ? sharpness : this.settings.defaultCinematicSharpness;
    this.exitRemaining = 0;
  }

  /** 演出を即座に終了させ、通常カメラへ戻す。 */
  clearCinematicShot(): void { this.cinematicRequested = false; }

  resetEffects(): void {
    this.resetDistanceEffect();
    this.resetPivotEffect();
    this.focusRemaining = 0;
    this.focusDuration = 0;
    this.holdingFocusThisFrame = false;
    this.holdingFocusBelt = false;
    this.deathFramingActive = false;
    this.cinematicRequested = false;
    this.cinematicActive = false;
    this.focusPitch = 0;
    this.focusYaw = 0;
  }

  /**
   * 1 フレーム更新。scaledDelta は追従・down 用、unscaledDelta は演出残り時間と
   * シネマティック補間に使う（HitStop 中も構図が止まらないように）。
   */
  update(unscaledDelta: number, scaledDelta = unscaledDelta, _clock?: Clock): void {
    this.tickEffects(unscaledDelta);
    if (!this.target) return;

    if (this.settings.beltMode) {
      this.tickBelt(unscaledDelta, scaledDelta);
      this.applyCinematicShot(unscaledDelta);
      this.framed = true;
      return;
    }

    const pivot = this.target.clone().add(this.settings.pivotOffset);
    pivot.y += this.pivotOffsetY;
    this.smoothPivot.lerp(pivot, 1 - Math.exp(-this.settings.followSharpness * scaledDelta));

    // Unity の Euler(pitch, yaw, 0) 相当を YXZ 順で組み、backward 方向を取る。
    const rotation = new Quaternion().setFromEuler(
      new Euler(this.pitch * DEG, this.yaw * DEG, 0, 'YXZ'),
    );
    const direction = new Vector3(0, 0, 1).applyQuaternion(rotation);

    const scaledDistance = Math.max(
      this.settings.minDistance,
      this.distance * this.distanceScale,
    );
    this.desiredDistance = MathUtils.lerp(
      this.desiredDistance,
      scaledDistance,
      1 - Math.exp(-22 * scaledDelta),
    );

    this.camera.position.copy(this.smoothPivot).addScaledVector(direction, this.desiredDistance);
    this.camera.up.copy(UP);
    this.camera.lookAt(this.smoothPivot);
    this.applyCinematicShot(unscaledDelta);
    this.framed = true;
  }

  private tickEffects(deltaTime: number): void {
    const dt = this.holdingFocusThisFrame ? 0 : Math.max(0, deltaTime);
    this.holdingFocusThisFrame = false;

    this.pushRemaining = Math.max(0, this.pushRemaining - dt);
    this.pullRemaining = Math.max(0, this.pullRemaining - dt);
    this.pivotRemaining = Math.max(0, this.pivotRemaining - dt);
    this.focusRemaining = Math.max(0, this.focusRemaining - dt);

    const pushIn = MathUtils.lerp(1, this.pushScale, this.envelope(this.pushRemaining, this.pushDuration));
    const pullOut = MathUtils.lerp(1, this.pullScale, this.envelope(this.pullRemaining, this.pullDuration));
    this.distanceScale = pushIn < 1 ? pushIn : pullOut;
    this.pivotOffsetY = MathUtils.lerp(0, this.pivotMeters, this.envelope(this.pivotRemaining, this.pivotDuration));
  }

  /** 0..1 の減衰。残り時間いっぱいで 1、終わると 0。端で値が飛ばない。 */
  private envelope(remaining: number, duration: number): number {
    if (remaining <= 0 || duration <= 0) return 0;
    const decay = Math.max(0.01, this.settings.effectDecay);
    const edge = Math.exp(-decay);
    const t = MathUtils.clamp(remaining / duration, 0, 1);
    return (Math.exp(-decay * (1 - t)) - edge) / (1 - edge);
  }

  private resetDistanceEffect(): void {
    this.pushScale = 1; this.pushRemaining = 0; this.pushDuration = 0;
    this.pullScale = 1; this.pullRemaining = 0; this.pullDuration = 0;
    this.distanceScale = 1;
  }

  private resetPivotEffect(): void {
    this.pivotMeters = 0; this.pivotRemaining = 0; this.pivotDuration = 0;
    this.pivotOffsetY = 0;
  }

  private tickBelt(unscaledDelta: number, scaledDelta: number): void {
    const s = this.settings;
    const facing = clamp(this.target!.x - this.beltX, -1, 1);
    this.beltLead = MathUtils.lerp(this.beltLead, facing * 0.4, 1 - Math.exp(-4 * scaledDelta));

    const focusing = this.focusRemaining > 0;
    const focusWeight = focusing && this.focusDuration > 0
      ? this.envelope(this.focusRemaining, this.focusDuration)
      : (focusing ? 1 : 0);

    const pitch = (s.beltPitch ?? 12) + (focusing ? this.focusPitch * focusWeight : 0);
    const baseDistance = focusing && this.distanceScale < 1
      ? Math.max(this.distance, this.initialDistance * s.closestShotZoom)
      : this.distance;
    const distance = Math.max(s.minDistance, baseDistance * this.distanceScale);
    const height = (s.beltPivotHeight ?? 1.25) + this.pivotOffsetY;

    const want = this.target!.x + this.beltLead;
    const dead = Math.max(0, s.beltDeadZone ?? 0);
    if (want > this.beltX + dead) this.beltX = want - dead;
    else if (want < this.beltX - dead) this.beltX = want + dead;

    if (focusing) {
      if (this.holdingFocusBelt) this.holdingFocusBelt = false;
      else this.focusRemaining = Math.max(0, this.focusRemaining - unscaledDelta);
      this.beltX = clamp(this.focusX, s.beltMinX ?? -Infinity, s.beltMaxX ?? Infinity);
    } else {
      this.holdingFocusBelt = false;
      this.beltX = clamp(this.beltX, s.beltMinX ?? -Infinity, s.beltMaxX ?? Infinity);
    }

    const pivot = new Vector3(this.beltX, height, 0);
    const follow = focusing ? 26 : (s.beltFollowSharpness ?? 5);
    const followDt = focusing ? unscaledDelta : scaledDelta;
    this.smoothPivot.lerp(pivot, 1 - Math.exp(-follow * followDt));

    const rotation = new Quaternion().setFromAxisAngle(UP, ((s.beltYaw ?? 0) + (focusing ? this.focusYaw * focusWeight : 0)) * DEG);
    const tilt = new Quaternion().setFromAxisAngle(new Vector3(1, 0, 0), pitch * DEG);
    const direction = new Vector3(0, 0, 1).applyQuaternion(rotation).applyQuaternion(tilt);
    this.camera.position.copy(this.smoothPivot).addScaledVector(direction, distance);
    this.camera.lookAt(this.smoothPivot);
  }

  private applyCinematicShot(unscaledDelta: number): void {
    const baseFov = this.baseFov;
    if (!this.cinematicRequested) {
      this.blendFov(baseFov, this.lastCinematicCut, unscaledDelta);
      if (this.cinematicActive && !this.lastCinematicCut) {
        this.exitFromPosition.copy(this.lastCinematicPosition);
        this.exitFromRotation.copy(this.lastCinematicRotation);
        this.exitRemaining = this.settings.cinematicExitSeconds;
      }
      this.cinematicActive = false;

      if (this.exitRemaining > 0) {
        let t = 1 - clamp(this.exitRemaining / this.settings.cinematicExitSeconds, 0, 1);
        t = smoothstep(t);
        const basePosition = this.camera.position.clone();
        const baseRotation = this.camera.quaternion.clone();
        this.camera.position.lerpVectors(this.exitFromPosition, basePosition, t);
        this.camera.quaternion.slerpQuaternions(this.exitFromRotation, baseRotation, t);
        this.exitRemaining = Math.max(0, this.exitRemaining - unscaledDelta);
      }
      return;
    }

    if (!this.cinematicActive) {
      this.shotPosition.copy(this.camera.position);
      this.shotTarget.copy(this.camera.position).addScaledVector(
        this.camera.getWorldDirection(new Vector3()),
        Math.max(1, this.initialDistance),
      );
    }

    const follow = this.cinematicCut ? 1 : 1 - Math.exp(-this.cinematicSharpness * unscaledDelta);
    this.shotPosition.lerp(this.cinematicPosition, follow);
    this.shotTarget.lerp(this.cinematicTarget, follow);

    const offset = this.shotPosition.clone().sub(this.shotTarget);
    const zoom = Math.max(this.settings.closestShotZoom, this.distance / Math.max(0.01, this.initialDistance));
    const zoomedOffset = zoom < 1
      ? offset.clone().setLength(Math.max(0.75, offset.length() * zoom))
      : offset.clone().multiplyScalar(zoom);
    const look = offset.clone().normalize().negate();
    let shotUp = UP.clone();
    if (MathUtils.radToDeg(look.angleTo(UP)) > 70) {
      let right = new Vector3().crossVectors(look, UP);
      if (right.lengthSq() < 1e-6) right = new Vector3(1, 0, 0);
      shotUp = new Vector3().crossVectors(right.normalize(), look).normalize();
    }
    this.camera.position.copy(this.shotTarget).add(zoomedOffset);
    this.camera.up.copy(shotUp);
    this.camera.lookAt(this.shotTarget);
    this.blendFov(this.cinematicFov > 0 ? this.cinematicFov : baseFov, this.cinematicCut, unscaledDelta);

    this.lastCinematicPosition.copy(this.camera.position);
    this.lastCinematicRotation.copy(this.camera.quaternion);
    this.lastCinematicCut = this.cinematicCut;
    this.cinematicActive = true;
    this.cinematicRequested = false;
  }

  private blendFov(wanted: number, instant: boolean, unscaledDelta: number): void {
    if (wanted <= 0 || Math.abs(this.camera.fov - wanted) < 1e-4) return;
    const t = instant ? 1 : 1 - Math.exp(-8 * unscaledDelta);
    this.camera.fov = MathUtils.lerp(this.camera.fov, wanted, t);
    if (Math.abs(this.camera.fov - wanted) < 0.05) this.camera.fov = wanted;
    this.camera.updateProjectionMatrix();
  }
}
