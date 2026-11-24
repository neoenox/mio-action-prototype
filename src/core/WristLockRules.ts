/** 手首ロック（拘束リング）の純粋ルール。 */
export class WristLockRules {
  readonly durationSeconds: number;
  readonly inputsToBreak: number;
  readonly moveScale: number;
  readonly escapeNeedScale: number;
  readonly cooldownSeconds: number;
  readonly graceSeconds: number;

  constructor(
    durationSeconds: number,
    inputsToBreak: number,
    moveScale: number,
    escapeNeedScale: number,
    cooldownSeconds: number,
    graceSeconds: number,
  ) {
    this.durationSeconds = durationSeconds > 0 ? durationSeconds : 4;
    this.inputsToBreak = inputsToBreak > 0 ? inputsToBreak : 8;
    this.moveScale = moveScale > 0 ? moveScale : 0.7;
    this.escapeNeedScale = escapeNeedScale > 0 ? escapeNeedScale : 1.3;
    this.cooldownSeconds = cooldownSeconds > 0 ? cooldownSeconds : 25;
    this.graceSeconds = graceSeconds > 0 ? graceSeconds : 1.5;
    Object.freeze(this);
  }

  static get Default(): WristLockRules {
    return new WristLockRules(4, 8, 0.7, 1.3, 25, 1.5);
  }

  isBroken(inputs: number): boolean {
    return inputs >= this.inputsToBreak;
  }

  isExpired(elapsed: number): boolean {
    return elapsed >= this.durationSeconds;
  }

  /** -1 は「まだ一度も解除していない」センチネル。 */
  isInGrace(now: number, endedAt: number): boolean {
    return endedAt >= 0 && now - endedAt < this.graceSeconds;
  }

  canAttach(now: number, endedAt: number, grappled: boolean, down: boolean): boolean {
    return (
      !grappled &&
      !down &&
      !this.isInGrace(now, endedAt) &&
      (endedAt < 0 || now - endedAt >= this.cooldownSeconds)
    );
  }

  scaledEscapeNeed(need: number): number {
    return need * this.escapeNeedScale;
  }
}
