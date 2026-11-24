import { Euler, Object3D, Quaternion, Vector3 } from 'three';
import { clamp01 } from '../core/math';
import type { Clock } from '../core/clock';

/**
 * Unity 版 CameraShake の移植。
 *
 * Unity では LateUpdate の先頭で「前回足したオフセットを引いてから足す」ことで
 * 累積を防いでいた。Three.js 側でも同じ結果を保証するため、基準（CameraController
 * が書いた揺れ無しの位置・回転）を引数で受け、毎フレーム
 *   target = base + offset / baseQuaternion * rotation
 * と置き直す。これなら呼び出し順や前フレームの状態に依存しない。
 * 時間は unscaledTime / unscaledDelta を使い、HitStop 中も揺れを進める。
 */
export interface ShakeTuning {
  /** 揺れ 1m あたり何度傾けるか */
  rotationPerMeter: number;
  /** 上下の揺れをどれだけ抑えるか (0..1) */
  verticalScale: number;
  /** 1 秒あたりの振動数 */
  frequency: number;
}

export const DEFAULT_SHAKE_TUNING: ShakeTuning = {
  rotationPerMeter: 45,
  verticalScale: 0.6,
  frequency: 34,
};

/** 決定論的な 1 次元値ノイズ（Unity の PerlinNoise 相当の滑らかさ）。 */
function valueNoise1D(seed: number, x: number): number {
  const i = Math.floor(x);
  const f = x - i;
  const h = (n: number): number => {
    const v = Math.sin(n * 127.1 + seed * 311.7) * 43758.5453123;
    return (v - Math.floor(v)) * 2 - 1;
  };
  const u = f * f * (3 - 2 * f);
  return h(i) * (1 - u) + h(i + 1) * u;
}

const DEG = Math.PI / 180;

export class CameraShake {
  private amplitude = 0;
  private remaining = 0;
  private duration = 0;
  private noiseSeed: number;
  private lastOffset = new Vector3();
  private lastRotation = new Quaternion();
  private readonly tuning: ShakeTuning;

  constructor(
    private readonly target: Object3D,
    private readonly clock: Clock,
    tuning: Partial<ShakeTuning> = {},
    seed = 0.37,
  ) {
    this.tuning = { ...DEFAULT_SHAKE_TUNING, ...tuning };
    this.noiseSeed = seed * 100;
  }

  get isShaking(): boolean { return this.remaining > 0; }

  /** 複数要求は「振幅は大きい方・残り時間は長い方」。足し算しない。 */
  shake(amplitude: number, duration: number): void {
    if (amplitude <= 0 || duration <= 0) return;
    const live = this.amplitude * this.envelope();
    this.amplitude = Math.max(live, amplitude);
    this.remaining = Math.max(this.remaining, duration);
    this.duration = Math.max(this.duration, this.remaining);
  }

  /**
   * CameraController.update() の後に呼ぶ。
   * basePosition / baseQuaternion は揺れ無しの基準。前回分を引いてから足す
   * （＝基準へ置き直してから足す）のでオフセットが累積しない。
   */
  updateLate(
    unscaledDelta: number,
    basePosition: Vector3 = this.target.position,
    baseQuaternion: Quaternion = this.target.quaternion,
  ): void {
    this.revert(basePosition, baseQuaternion);
    if (this.remaining <= 0) return;

    this.remaining = Math.max(0, this.remaining - Math.max(0, unscaledDelta));
    const strength = this.amplitude * this.envelope();
    if (strength <= 0) return;

    const n = this.clock.unscaledTime * this.tuning.frequency;
    const nx = valueNoise1D(this.noiseSeed, n);
    const ny = valueNoise1D(this.noiseSeed + 13.7, n);
    const nz = valueNoise1D(this.noiseSeed + 41.3, n);

    const offset = new Vector3(nx, ny * this.tuning.verticalScale, nz).multiplyScalar(strength);
    const r = this.tuning.rotationPerMeter * strength * DEG;
    const rotation = new Quaternion().setFromEuler(new Euler(nz * r, nx * r, ny * r, 'YXZ'));

    this.target.position.add(offset);
    this.target.quaternion.multiply(rotation);
    this.lastOffset.copy(offset);
    this.lastRotation.copy(rotation);
  }

  /**
   * 無効化時に足した分を戻す。現在の揺れ込み位置から前回分だけを引く
   * （Unity の OnDisable の Revert() と同じ）。
   */
  reset(): void {
    this.target.position.sub(this.lastOffset);
    this.target.quaternion.multiply(this.lastRotation.clone().invert());
    this.lastOffset.set(0, 0, 0);
    this.lastRotation.identity();
    this.amplitude = 0;
    this.remaining = 0;
    this.duration = 0;
  }

  private envelope(): number {
    if (this.duration <= 0) return 0;
    const t = clamp01(this.remaining / this.duration);
    return t * t;
  }

  private revert(basePosition: Vector3, baseQuaternion: Quaternion): void {
    // 基準へ置き直す（前回分を引くのと同じ結果になるが、累積しない）。
    this.target.position.copy(basePosition);
    this.target.quaternion.copy(baseQuaternion);
    this.lastOffset.set(0, 0, 0);
    this.lastRotation.identity();
  }
}
