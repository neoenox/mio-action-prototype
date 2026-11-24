import { StaggerTier } from '../core/CombatConstants.js';
import type { HitResult } from '../core/HitResult.js';
import { MathUtils } from 'three';
import type { CameraController } from './camera.js';

/**
 * Unity 版 CameraDirector の移植。
 *
 * HitLanded / HitReceived / Staggered だけを購読し、空振りでは何もしない。
 * 判定・状態へは干渉せず、CameraController の押し込み API だけを呼ぶ。
 */
export interface CameraDirectorTuning {
  pushInScale: number;
  pushInSeconds: number;
  hitPullScale: number;
  hitPullSeconds: number;
  hitPivotDrop: number;
  hitPivotSeconds: number;
  staggerPullScale: number;
  staggerPullSeconds: number;
  staggerPivotDrop: number;
  staggerPivotSeconds: number;
  deathPullScale: number;
  deathPivotDrop: number;
  deathPivotSeconds: number;
  deathHoldSeconds: number;
}

export const DEFAULT_CAMERA_DIRECTOR_TUNING: CameraDirectorTuning = {
  pushInScale: 0.90,
  pushInSeconds: 0.20,
  hitPullScale: 1.10,
  hitPullSeconds: 0.30,
  hitPivotDrop: -0.10,
  hitPivotSeconds: 0.30,
  staggerPullScale: 1.16,
  staggerPullSeconds: 0.45,
  staggerPivotDrop: -0.18,
  staggerPivotSeconds: 0.45,
  deathPullScale: 1.25,
  deathPivotDrop: -0.85,
  deathPivotSeconds: 0.5,
  deathHoldSeconds: 0.5,
};

export class CameraDirector {
  private dead = false;
  private deathElapsed = 0;
  private readonly tuning: CameraDirectorTuning;

  constructor(
    private readonly rig: CameraController,
    private readonly getIsDead: () => boolean,
    /** 腹パン演出がカメラを握っている間は true を返す（注入） */
    private readonly isHeldPunchCameraActive: () => boolean = () => false,
    tuning: Partial<CameraDirectorTuning> = {},
  ) {
    this.tuning = { ...DEFAULT_CAMERA_DIRECTOR_TUNING, ...tuning };
  }

  /** 自分が当てた。寄りのみ。 */
  onHitLanded(_result: HitResult): void {
    this.rig.pushDistance(this.tuning.pushInScale, this.tuning.pushInSeconds);
  }

  /** 被弾。Large 以上は Staggered 側に任せる。 */
  onHitReceived(result: HitResult): void {
    if (this.isHeldPunchCameraActive()) return;
    if (result.stagger >= StaggerTier.Large) return;
    this.rig.pushDistance(this.tuning.hitPullScale, this.tuning.hitPullSeconds);
    this.rig.pushPivot(this.tuning.hitPivotDrop, this.tuning.hitPivotSeconds);
  }

  /** Large 以上の怯み。 */
  onStaggered(result: HitResult): void {
    if (this.isHeldPunchCameraActive()) return;
    if (result.stagger < StaggerTier.Large) return;
    this.rig.pushDistance(this.tuning.staggerPullScale, this.tuning.staggerPullSeconds);
    this.rig.pushPivot(this.tuning.staggerPivotDrop, this.tuning.staggerPivotSeconds);
  }

  /** 毎フレーム（Update 相当）。死亡演出の監視だけを行う。 */
  update(unscaledDelta: number): void {
    if (!this.getIsDead()) {
      this.dead = false;
      this.deathElapsed = 0;
      this.rig.deathFramingActive = false;
      return;
    }
    if (!this.dead) {
      this.dead = true;
      this.deathElapsed = 0;
    }
    this.deathElapsed += unscaledDelta;
    this.applyDeathFraming(unscaledDelta);
  }

  /** 死亡演出を押し直して保持する（寄りが来ても引きが負けない）。 */
  private applyDeathFraming(unscaledDelta: number): void {
    const t = this.tuning.deathPivotSeconds > 0
      ? MathUtils.clamp(this.deathElapsed / this.tuning.deathPivotSeconds, 0, 1)
      : 1;
    const ease = t * t * (3 - 2 * t);
    const hold = Math.max(this.tuning.deathHoldSeconds, unscaledDelta);

    this.rig.deathFramingActive = true;
    this.rig.pushDistance(MathUtils.lerp(1, this.tuning.deathPullScale, ease), hold);
    this.rig.pushPivot(MathUtils.lerp(0, this.tuning.deathPivotDrop, ease), hold);
  }
}
