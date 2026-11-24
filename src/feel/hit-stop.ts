import type { Clock } from '../core/clock';

/**
 * Unity 版 HitStop の移植。
 *
 * timeScale=0 を unscaled 時間で復帰させる。復帰は常に 1 本のロジックだけ、
 * 連打では残り時間の長い方を採用し、止める前の scale へ必ず戻す。
 * Pause / Title が timeScale を握っている間は触らない（Unity 版のガード）。
 */
export interface HitStopOptions {
  /** Pause / Title 中は timeScale を書き戻さない */
  isBlocked?: () => boolean;
}

export class HitStop {
  private remaining = 0;
  private restoreScale = 1;
  private readonly isBlocked: () => boolean;

  constructor(private readonly clock: Clock, options: HitStopOptions = {}) {
    this.isBlocked = options.isBlocked ?? (() => false);
  }

  get isActive(): boolean { return this.remaining > 0; }
  get remainingSeconds(): number { return this.remaining; }

  /** seconds だけ時間を止める。すでに止まっている長い方を優先する。 */
  play(seconds: number): void {
    if (seconds <= 0) return;
    if (this.remaining <= 0) {
      this.restoreScale = this.clock.scaledScale > 0 ? this.clock.scaledScale : 1;
    }
    this.remaining = Math.max(this.remaining, seconds);
    this.clock.setScale(0);
  }

  /**
   * 毎フレーム unscaledDelta で呼ぶ。HitStop の復帰だけを担当する隠し
   * ドライバ（Unity の HitStopRunner）相当。false を返したら復帰させる。
   */
  tick(unscaledDelta: number): boolean {
    if (this.remaining <= 0) return false;
    this.remaining = Math.max(0, this.remaining - unscaledDelta);
    return this.remaining > 0;
  }

  /** timeScale を保存しておいた値へ戻す。何度呼んでも安全。 */
  restoreNow(): void {
    this.remaining = 0;
    if (this.isBlocked()) return;
    this.clock.setScale(this.restoreScale);
  }

  /**
   * ゲームループの 1 フレーム先頭から呼ぶ推奨エントリ。
   * 残り時間が切れたフレームにだけ復帰する。連打中は restoreScale を書き換えない。
   */
  update(unscaledDelta: number): void {
    if (this.remaining <= 0) return;
    const stillActive = this.tick(unscaledDelta);
    if (!stillActive) this.restoreNow();
  }
}
