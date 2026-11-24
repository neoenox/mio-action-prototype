/** 踏みとどまり入力の判定。 */
export enum PinballTiming {
  TooEarly = 0,
  InWindow = 1,
  TooLate = 2,
  RoundClosed = 3,
}

/** 挟み撃ちの突き飛ばし（ピンポン）の純粋ルール。 */
export class PinballRules {
  readonly rounds: number;
  readonly baseDistance: number;
  readonly distanceGain: number;
  readonly baseSpeed: number;
  readonly speedGain: number;
  readonly windowHalfSeconds: number;
  readonly windowNarrowing: number;

  constructor(
    rounds: number,
    baseDistance: number,
    distanceGain: number,
    baseSpeed: number,
    speedGain: number,
    windowHalfSeconds: number,
    windowNarrowing: number,
  ) {
    this.rounds = Math.max(1, rounds);
    this.baseDistance = Math.max(0, baseDistance);
    this.distanceGain = Math.max(0, distanceGain);
    this.baseSpeed = Math.max(0.1, baseSpeed);
    this.speedGain = Math.max(0, speedGain);
    this.windowHalfSeconds = Math.max(0.01, windowHalfSeconds);
    this.windowNarrowing = Math.max(0, windowNarrowing);
    Object.freeze(this);
  }

  static get Default(): PinballRules {
    return new PinballRules(3, 1.15, 0.18, 3.2, 0.5, 0.12, 0.02);
  }

  /** 0 始まりの往復で飛ぶ距離（m）。 */
  distanceOf(round: number): number {
    return this.baseDistance + this.distanceGain * Math.max(0, round);
  }

  /** 0 始まりの往復の速さ（m/s）。 */
  speedOf(round: number): number {
    return this.baseSpeed + this.speedGain * Math.max(0, round);
  }

  /** 往復ごとに狭まる受け止め窓の半幅（秒）。 */
  halfWindowOf(round: number): number {
    return Math.max(0.03, this.windowHalfSeconds - this.windowNarrowing * Math.max(0, round));
  }

  /** 入力時刻と受け止め時刻の差から判定する。 */
  judge(inputTime: number, catchTime: number, round: number): PinballTiming {
    if (round < 0 || round >= this.rounds) return PinballTiming.RoundClosed;
    const delta = inputTime - catchTime;
    const half = this.halfWindowOf(round);
    if (delta < -half) return PinballTiming.TooEarly;
    if (delta > half) return PinballTiming.TooLate;
    return PinballTiming.InWindow;
  }

  isFinal(round: number): boolean {
    return round >= this.rounds - 1;
  }
}
