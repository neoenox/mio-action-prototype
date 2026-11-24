/**
 * Unity の Time 相当。scaled と real (unscaled) の二系統を明示的に分ける。
 * HitStop は scaledScale=0 で表現し、CameraShake 等は real で進める。
 */
export class Clock {
  time = 0;
  unscaledTime = 0;
  scaledScale = 1;

  /** 現在の scaled delta。HitStop 中は 0。 */
  scaledDelta(unscaledDelta: number): number {
    return unscaledDelta * this.scaledScale;
  }

  advance(unscaledDelta: number): void {
    this.unscaledTime += unscaledDelta;
    this.time += this.scaledDelta(unscaledDelta);
  }

  /** Unity の Time.timeScale 相当。Pause 中は書き戻さない運用を呼び側が守る。 */
  setScale(scale: number): void {
    this.scaledScale = scale;
  }
}
