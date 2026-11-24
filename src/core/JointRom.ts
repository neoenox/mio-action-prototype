import {
  HumanJointLimits,
  JointId,
  JointLimit,
  JointRomVerdict,
  JointSide,
} from './JointLimit.js';

// rig-port が 1 ファイルから imported できるよう再エクスポートする。
export { HumanJointLimits, JointId, JointLimit, JointRomVerdict, JointSide };

/** 1 回ぶんの関節角度の計測結果。 */
export class JointRomReading {
  readonly joint: JointId;
  readonly side: JointSide;
  readonly degrees: number;
  readonly limit: JointLimit;

  constructor(joint: JointId, side: JointSide, degrees: number, limit: JointLimit) {
    this.joint = joint;
    this.side = side;
    this.degrees = degrees;
    this.limit = limit;
    Object.freeze(this);
  }

  get isViolation(): boolean {
    return this.verdict !== JointRomVerdict.Within;
  }

  get verdict(): JointRomVerdict {
    if (this.degrees > this.limit.warnAbove) return JointRomVerdict.OverFlexed;
    if (this.degrees < this.limit.warnBelow) return JointRomVerdict.ReverseBend;
    return JointRomVerdict.Within;
  }

  get excess(): number {
    if (this.degrees > this.limit.warnAbove) return this.degrees - this.limit.warnAbove;
    if (this.degrees < this.limit.warnBelow) return this.limit.warnBelow - this.degrees;
    return 0;
  }

  /** ログ用の説明（例: 「右ひざ 168°（可動域 0〜150°・曲げすぎ）」）。 */
  describe(): string {
    const verdict = JointRom.verdictName(this.verdict);
    return `${JointRom.sideName(this.side)}${this.limit.label} ${Math.round(this.degrees)}°` +
      `（可動域 ${this.limit.rangeText}・${verdict}）`;
  }
}

/** 関節角度の読み取りと表示の共通処理。 */
export const JointRom = {
  read(joint: JointId, side: JointSide, degrees: number): JointRomReading {
    return new JointRomReading(joint, side, degrees, HumanJointLimits.of(joint));
  },

  /** 左右の日本語名。中央は空文字。 */
  sideName(side: JointSide): string {
    switch (side) {
      case JointSide.Left: return '左';
      case JointSide.Right: return '右';
      default: return '';
    }
  },

  /** 判定の日本語名。 */
  verdictName(verdict: JointRomVerdict): string {
    switch (verdict) {
      case JointRomVerdict.OverFlexed: return '曲げすぎ';
      case JointRomVerdict.ReverseBend: return '逆方向';
      default: return '正常';
    }
  },
} as const;

/** 関節ごとの「最悪値」と逸脱回数を貯める集計。 */
export class JointRomTally {
  private readonly worst = new Map<string, JointRomReading>();
  private readonly counts = new Map<string, number>();

  violationSamples = 0;
  worstOverall: JointRomReading | undefined;

  get trackedKeys(): number {
    return this.worst.size;
  }

  get worstReadings(): Iterable<JointRomReading> {
    return this.worst.values();
  }

  add(reading: JointRomReading): void {
    if (!reading.isViolation) return;

    this.violationSamples++;
    const key = JointRomTally.key(reading.joint, reading.side);
    this.counts.set(key, (this.counts.get(key) ?? 0) + 1);

    const existing = this.worst.get(key);
    if (existing === undefined || reading.excess > existing.excess) {
      this.worst.set(key, reading);
    }

    if (this.worstOverall === undefined || reading.excess > this.worstOverall.excess) {
      this.worstOverall = reading;
    }
  }

  countOf(joint: JointId, side: JointSide): number {
    return this.counts.get(JointRomTally.key(joint, side)) ?? 0;
  }

  tryGetWorst(joint: JointId, side: JointSide): JointRomReading | undefined {
    return this.worst.get(JointRomTally.key(joint, side));
  }

  clear(): void {
    this.worst.clear();
    this.counts.clear();
    this.violationSamples = 0;
    this.worstOverall = undefined;
  }

  // タプルキーを文字列キーへ（C# の (int)joint * 3 + (int)side と同じ一意性）。
  private static key(joint: JointId, side: JointSide): string {
    return `${(joint as number) * 3 + (side as number)}`;
  }
}
