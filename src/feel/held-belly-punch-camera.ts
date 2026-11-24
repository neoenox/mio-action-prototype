import type { CameraController } from './camera.js';
import { clamp01, invLerp, lerp } from '../core/math.js';

/**
 * Unity 版 HeldBellyPunchDirector.Camera.cs の移植。
 *
 * 演出の状態機械（logic 側）から「いまの段・狙い・姿勢係数」だけを受け取り、
 * 構図の計算と CameraController の押し込み API 呼び出しだけを担当する。
 * 判定・リグ・Time.timeScale には触れない（app 側が接続する）。
 */

/** 演出の段階。Unity 版 HeldBellyPunchDirector.Phase と同じ値。 */
export enum Phase {
  Idle = 0,
  WaitingThrow = 1,
  Combo = 2,
  HandOffWait = 3,
  Finisher = 4,
  Collapse = 5,
  FinisherDodge = 6,
}

/** パンチの狙い。logic の PunchZone と同じ値（Face=0 / Body=1）。 */
export enum PunchZone {
  Face = 0,
  Body = 1,
}

/** 起動引数 -heldpunch* で上書きできるカメラ値。未指定なら DEFAULT を使う。 */
export interface HeldPunchCameraSettings {
  bodyDistanceScale: number;
  bodyPivotOffset: number;
  bodyPitchOffset: number;
  bodyYawOffset: number;
  faceDistanceScale: number;
  facePivotOffset: number;
  facePitchOffset: number;
  faceYawOffset: number;
  finisherDistanceScale: number;
  finisherPivotDelta: number;
  finisherPitchDelta: number;
  /** 距離倍率の基準になる基本距離 (m)。Unity 版 ThirdPersonCamera は 5.2m。 */
  baseDistance: number;
  /** 実距離の下限 (m)。顔と腹を同時に映すため 1.4m を割らない。 */
  minShotDistance: number;
}

export const DEFAULT_HELD_PUNCH_CAMERA_SETTINGS: HeldPunchCameraSettings = {
  bodyDistanceScale: 0.27,
  bodyPivotOffset: 0.34,
  bodyPitchOffset: -4,
  bodyYawOffset: -32,
  faceDistanceScale: 0.22,
  facePivotOffset: 0.46,
  facePitchOffset: 1.5,
  faceYawOffset: -36,
  finisherDistanceScale: 0.42,
  finisherPivotDelta: -0.12,
  finisherPitchDelta: -6,
  baseDistance: 5.2,
  minShotDistance: 1.4,
};

/**
 * 1 フレームの入力。app 側が演出状態から埋めて渡す。
 * 未指定の係数は 0（＝姿勢による補正なし）として扱う。
 */
export interface HeldPunchCameraInput {
  phase: Phase;
  zone: PunchZone;
  /** 彼女のワールド X。FocusX の基準。 */
  playerX: number;
  /**
   * Unity 版は transform.forward.x をそのまま見て < -0.1 なら -1、それ以外 +1 とする。
   * 既に符号だけを求めている場合は +1 / -1 を渡しても同じ結果になる。
   */
  facingSign?: number;
  /** HeldBellyPunchFold (0..1)。Body のみ効く。 */
  fold?: number;
  /** HeldBellyPunchFatigue (0..1)。Body の距離・ピボットに効く。 */
  fatigue?: number;
  /** HeldBellyDangle (0..1)。Body のピボットを上げる。 */
  dangle?: number;
  /** HeldBellyCaptorLift (0..1)。Body のピボットを下げる。 */
  lift?: number;
  /** FinisherHoistWeight (0..1)。ピボットに +0.03 する。 */
  finisherHoist?: number;
  /** 命中直後の寄せ (1 → 0、約 0.12 秒)。距離に -0.05 まで効く。 */
  impactPulse?: number;
  /** 決めの全身ショットへの寄せ具合 (0..1)。 */
  finisherShotBlend?: number;
}

/** 計算された構図。テストと app の両方から読める。 */
export interface HeldPunchShot {
  distanceScale: number;
  pivot: number;
  pitch: number;
  yaw: number;
  focusX: number;
}

/** 押し込みの秒数。Unity 版と同じ。 */
export const REFRESH_HOLD_SECONDS = 1.6;
export const HOLD_FRAMING_SECONDS = 0.5;
export const COLLAPSE_HOLD_SECONDS = 2.4;

const MIN_HOLD_SECONDS = 0.2;
const LOG_THRESHOLD_SECONDS = 1.5;

export class HeldBellyPunchCamera {
  readonly settings: HeldPunchCameraSettings;

  /** 最後に渡された入力。テスト・デバッグ表示用。 */
  input: HeldPunchCameraInput = {
    phase: Phase.Idle,
    zone: PunchZone.Body,
    playerX: 0,
  };

  /** PushFocus を即時リセットした後かどうか。ResetHold 相当。 */
  private focusCleared = false;

  constructor(
    private readonly rig: CameraController | null = null,
    settings: Partial<HeldPunchCameraSettings> = {},
  ) {
    this.settings = { ...DEFAULT_HELD_PUNCH_CAMERA_SETTINGS, ...settings };
  }

  /** 演出中にカメラを握っているか（Unity 版 IsCameraActive と同じ）。 */
  get isCameraActive(): boolean {
    const p = this.input.phase;
    return p === Phase.Combo || p === Phase.Finisher ||
      p === Phase.FinisherDodge || p === Phase.Collapse;
  }

  get isFinisherActive(): boolean {
    const p = this.input.phase;
    return p === Phase.Finisher || p === Phase.FinisherDodge || p === Phase.Collapse;
  }

  setInput(input: HeldPunchCameraInput): void {
    this.input = input;
    this.focusCleared = false;
  }

  /** 起動引数で上書きされた値を反映する（Unity 版 ApplyCameraOverrides 相当）。 */
  applyOverrides(overrides: Partial<HeldPunchCameraSettings>): void {
    Object.assign(this.settings, overrides);
  }

  private facingSign(): number {
    return this.input.facingSign !== undefined && this.input.facingSign < -0.1
      ? -1
      : 1;
  }

  /**  Zone 別の距離倍率。 */
  zoneDistanceScale(): number {
    const s = this.settings;
    return this.input.zone === PunchZone.Face ? s.faceDistanceScale : s.bodyDistanceScale;
  }

  /** 折れ・疲労・だらり・持ち上げで補正した距離倍率。 */
  effectiveDistanceScale(): number {
    const fatigue = this.input.zone === PunchZone.Body ? (this.input.fatigue ?? 0) : 0;
    const impact = this.input.impactPulse ?? 0;
    return this.zoneDistanceScale() * (1 - 0.04 * fatigue - 0.05 * impact);
  }

  /** 決めの全身ショットとの補間込みの距離倍率（Unity 版の素の値）。 */
  rawShotDistanceScale(): number {
    return lerp(
      this.effectiveDistanceScale(),
      this.settings.finisherDistanceScale,
      clamp01(this.input.finisherShotBlend ?? 0),
    );
  }

  /**
   * CameraController へ渡す距離倍率。実距離が minShotDistance を下回らないよう、
   * baseDistance に対する倍率に変換して下限をかける。
   * Unity 版は基本距離 5.2m × 0.27 = 1.40m で腹と拳を両立させており、
   * これより寄ると頭に貼り付いて顔も腹も画から消える（実測）。
   */
  shotDistanceScale(): number {
    const raw = this.rawShotDistanceScale();
    const minScale = this.settings.minShotDistance / this.settings.baseDistance;
    return Math.max(raw, minScale);
  }

  /** ピボット上下。顔／腹の別＋疲労・折れ・だらり・持ち上げ・抱え上げの補正。 */
  shotPivot(): number {
    const s = this.settings;
    const face = this.input.zone === PunchZone.Face;
    const base = face ? s.facePivotOffset : s.bodyPivotOffset;
    const fatigue = face ? 0 : (this.input.fatigue ?? 0);
    const fold = face ? 0 : (this.input.fold ?? 0);
    const dangle = face ? 0 : (this.input.dangle ?? 0);
    const lift = face ? 0 : (this.input.lift ?? 0);
    const combo = base
      + 0.03 * (this.input.finisherHoist ?? 0)
      - 0.05 * fold - 0.02 * fatigue - 0.04 * dangle + 0.015 * lift;
    return combo + s.finisherPivotDelta * clamp01(this.input.finisherShotBlend ?? 0);
  }

  /** 俯角（度）。決めの差分を足す。 */
  shotPitch(): number {
    const s = this.settings;
    const base = this.input.zone === PunchZone.Face ? s.facePitchOffset : s.bodyPitchOffset;
    return base + s.finisherPitchDelta * clamp01(this.input.finisherShotBlend ?? 0);
  }

  /** ヨー（度）。左右で符号を反転して彼女の正面側から撮る。 */
  focusYaw(): number {
    const s = this.settings;
    const zoneYaw = this.input.zone === PunchZone.Face ? s.faceYawOffset : s.bodyYawOffset;
    return zoneYaw * this.facingSign();
  }

  /** 画面中央を合わせる X。寄るほど同じオフセットでもずれが大きくなるので距離に比例。 */
  focusX(): number {
    const s = this.settings;
    const face = this.input.zone === PunchZone.Face;
    const offset = face
      ? 0.30 * (s.faceDistanceScale / 0.31)
      : 0.15 * (this.zoneDistanceScale() / 0.32);
    return this.input.playerX + this.facingSign() * offset;
  }

  shot(): HeldPunchShot {
    return {
      distanceScale: this.shotDistanceScale(),
      pivot: this.shotPivot(),
      pitch: this.shotPitch(),
      yaw: this.focusYaw(),
      focusX: this.focusX(),
    };
  }

  /**
   * 命中時に 1.6 秒押し込む（Unity 版 RefreshCamera）。
   * 0.2 秒未満は 0.2 秒に丸める。毎フレームの保持は HoldFraming 側。
   */
  refreshCamera(seconds = REFRESH_HOLD_SECONDS): HeldPunchShot {
    const shot = this.shot();
    if (this.rig === null) return shot;

    const hold = Math.max(MIN_HOLD_SECONDS, seconds);
    this.rig.pushDistance(shot.distanceScale, hold);
    this.rig.pushPivot(shot.pivot, hold);
    this.rig.pushFocus(shot.focusX, hold, shot.pitch, shot.yaw);
    return shot;
  }

  /** コンボ中ずっと同じ構図を保つ（Unity 版 HoldFraming）。 */
  holdFraming(): HeldPunchShot {
    const shot = this.shot();
    if (this.rig === null) return shot;

    this.rig.holdFocus(
      shot.distanceScale, shot.pivot, shot.focusX,
      HOLD_FRAMING_SECONDS, shot.pitch, shot.yaw,
    );
    return shot;
  }

  /** 崩れ落ちる瞬間に現在の寄りからゆっくり引く（Unity 版 ApplyCollapseCamera）。 */
  applyCollapseCamera(): HeldPunchShot {
    const shot = this.shot();
    if (this.rig === null) return shot;

    this.rig.holdFocus(
      shot.distanceScale, shot.pivot, shot.focusX,
      COLLAPSE_HOLD_SECONDS, shot.pitch, shot.yaw,
    );
    return shot;
  }

  /** 拘束が解けたときの即時リセット（Unity 版 ResetHold の PushFocus(0,0)）。 */
  clearFocus(): void {
    if (this.rig === null) return;
    this.rig.pushFocus(0, 0);
    this.focusCleared = true;
  }

  get isFocusCleared(): boolean { return this.focusCleared; }
}

/**
 * ImpactPulse を 1 → 0 へ戻す（Unity 版 Update の MoveTowards(pulse, 0, dt*8)）。
 * HitStop 中も実時間で進めるため、呼び側は unscaledDelta を渡す。
 */
export function decayImpactPulse(pulse: number, unscaledDelta: number): number {
  return pulse <= 0 ? 0 : Math.max(0, pulse - unscaledDelta * 8);
}

/**
 * FinisherShotBlend を target へ寄せる（Unity 版 MoveTowards）。
 * target=1 のときは ComboTailSeconds、0 のときは 0.25 秒で動く。
 */
export function advanceFinisherBlend(
  current: number,
  target: number,
  scaledDelta: number,
  comboTailSeconds = 0.35,
): number {
  const goal = target > 0.5 ? 1 : 0;
  const duration = goal > 0 ? Math.max(0.12, comboTailSeconds) : 0.25;
  const step = scaledDelta / duration;
  const diff = goal - current;
  return Math.abs(diff) <= step ? goal : current + Math.sign(diff) * step;
}

/** 疲労を hits から求める（Unity 版 InverseLerp(1, 4, bellyHits)）。 */
export function fatigueFromHits(hits: number): number {
  return invLerp(1, 4, hits);
}
