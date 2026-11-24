import { BodyVec3 } from './BodyVec3.js';

/** 重なり判定の対象にする体の部位（1 キャラクターあたり 10 本のカプセル）。 */
export enum OverlapPart {
  Torso = 0,
  Head = 1,
  UpperArmL = 2,
  UpperArmR = 3,
  ForearmL = 4,
  ForearmR = 5,
  ThighL = 6,
  ThighR = 7,
  ShinL = 8,
  ShinR = 9,
}

/** 体の 1 部位を表すカプセル（線分 start〜end と半径 radius）。 */
export class BodyCapsule {
  readonly part: OverlapPart;
  readonly start: BodyVec3;
  readonly end: BodyVec3;
  readonly radius: number;

  constructor(part: OverlapPart, start: BodyVec3, end: BodyVec3, radius: number) {
    this.part = part;
    this.start = start;
    this.end = end;
    this.radius = radius;
    Object.freeze(this);
  }
}

const labels: readonly string[] = [
  '胴', '頭', '左二の腕', '右二の腕', '左前腕', '右前腕',
  '左太もも', '右太もも', '左すね', '右すね',
];

const heightRatios: readonly number[] = [
  0.07, 0.058, 0.028, 0.028, 0.024, 0.024, 0.042, 0.042, 0.032, 0.032,
];

/** 部位ごとの日本語名と、身長 1.0 あたりの標準半径。 */
export const BodyPartSpecs = {
  get count(): number {
    return labels.length;
  },

  labelOf(part: OverlapPart): string {
    const index = part as number;
    if (index < 0 || index >= labels.length) {
      throw new RangeError(`未定義の部位です: ${part}`);
    }
    return labels[index]!;
  },

  radiusForHeight(part: OverlapPart, heightMeters: number): number {
    const index = part as number;
    if (index < 0 || index >= heightRatios.length) {
      throw new RangeError(`未定義の部位です: ${part}`);
    }
    return Math.max(0.01, heightMeters) * heightRatios[index]!;
  },
} as const;

/** 2 キャラクターの部位ペア 1 組についての重なり計測結果。 */
export class BodyOverlapReading {
  readonly subjectA: string;
  readonly partA: OverlapPart;
  readonly subjectB: string;
  readonly partB: OverlapPart;
  readonly penetrationMeters: number;
  readonly slackMeters: number;

  constructor(
    subjectA: string | null | undefined,
    partA: OverlapPart,
    subjectB: string | null | undefined,
    partB: OverlapPart,
    penetrationMeters: number,
    slackMeters: number,
  ) {
    this.subjectA = subjectA ?? 'A';
    this.partA = partA;
    this.subjectB = subjectB ?? 'B';
    this.partB = partB;
    this.penetrationMeters = penetrationMeters;
    this.slackMeters = slackMeters;
    Object.freeze(this);
  }

  get excessMeters(): number {
    return this.penetrationMeters > this.slackMeters ? this.penetrationMeters - this.slackMeters : 0;
  }

  get isViolation(): boolean {
    return this.penetrationMeters > this.slackMeters;
  }

  describe(): string {
    const penCm = this.penetrationMeters * 100;
    const slackCm = this.slackMeters * 100;
    const excessCm = this.excessMeters * 100;
    return `${this.subjectA}の${BodyPartSpecs.labelOf(this.partA)} ↔ ` +
      `${this.subjectB}の${BodyPartSpecs.labelOf(this.partB)} ` +
      `が ${penCm.toFixed(1)}cm 重なり（許容 ${slackCm.toFixed(1)}cm・${excessCm.toFixed(1)}cm 超過）`;
  }
}

/** 既定の許容めり込み余裕（m）。 */
const defaultSlackMeters = 0.04;

/** 2 つのカプセル間の最短距離とめり込み量を計算する純粋幾何ロジック。 */
export const BodyOverlap = {
  DefaultSlackMeters: defaultSlackMeters,

  measure(
    subjectA: string,
    capsuleA: BodyCapsule,
    subjectB: string,
    capsuleB: BodyCapsule,
    slackMeters: number = defaultSlackMeters,
  ): BodyOverlapReading {
    const centerDistance = segmentSegmentDistance(
      capsuleA.start, capsuleA.end, capsuleB.start, capsuleB.end,
    );
    const penetration = capsuleA.radius + capsuleB.radius - centerDistance;
    return new BodyOverlapReading(
      subjectA, capsuleA.part, subjectB, capsuleB.part, penetration, slackMeters,
    );
  },

  /** 2 つの線分 AB と CD の最短距離（m）。どちらかが点でも正しく計算する。 */
  segmentSegmentDistance(a: BodyVec3, b: BodyVec3, c: BodyVec3, d: BodyVec3): number {
    return segmentSegmentDistance(a, b, c, d);
  },
} as const;

/** 2 つの線分 AB と CD の最短距離（m）。どちらかが点（長さ 0）に縮退していても計算する。 */
function segmentSegmentDistance(a: BodyVec3, b: BodyVec3, c: BodyVec3, d: BodyVec3): number {
    const eps = 1e-8;
    const d1 = BodyVec3.sub(b, a);
    const d2 = BodyVec3.sub(d, c);
    const r = BodyVec3.sub(a, c);
    const len1Sq = BodyVec3.dot(d1, d1);
    const len2Sq = BodyVec3.dot(d2, d2);

    let s: number;
    let t: number;

    if (len1Sq <= eps && len2Sq <= eps) {
      return Math.sqrt(r.sqrMagnitude);
    }

    if (len1Sq <= eps) {
      s = 0;
      t = clamp01(BodyVec3.dot(d2, r) / len2Sq);
    } else {
      const cDot = BodyVec3.dot(d1, r);
      if (len2Sq <= eps) {
        t = 0;
        s = clamp01(-cDot / len1Sq);
      } else {
        const bDot = BodyVec3.dot(d1, d2);
        const fDot = BodyVec3.dot(d2, r);
        const denom = len1Sq * len2Sq - bDot * bDot;

        s = denom > eps ? clamp01((bDot * fDot - cDot * len2Sq) / denom) : 0;
        t = (bDot * s + fDot) / len2Sq;

        if (t < 0) {
          t = 0;
          s = clamp01(-cDot / len1Sq);
        } else if (t > 1) {
          t = 1;
          s = clamp01((bDot - cDot) / len1Sq);
        }
      }
    }

    const p1 = BodyVec3.add(a, BodyVec3.mul(d1, s));
    const p2 = BodyVec3.add(c, BodyVec3.mul(d2, t));
    return Math.sqrt(BodyVec3.sub(p1, p2).sqrMagnitude);
}

function clamp01(value: number): number {
  if (value < 0) return 0;
  if (value > 1) return 1;
  return value;
}

/** 重なり違反の最悪値と延べ回数をペアごとに記録する集計器。 */
export class BodyOverlapTally {
  private readonly worst = new Map<string, BodyOverlapReading>();
  private readonly counts = new Map<string, number>();

  violationSamples = 0;
  worstOverall: BodyOverlapReading | undefined;

  get trackedPairs(): number {
    return this.worst.size;
  }

  get worstReadings(): Iterable<BodyOverlapReading> {
    return this.worst.values();
  }

  add(reading: BodyOverlapReading): void {
    if (!reading.isViolation) return;

    this.violationSamples++;
    const key = BodyOverlapTally.countKey(reading);

    this.counts.set(key, (this.counts.get(key) ?? 0) + 1);

    const existing = this.worst.get(key);
    if (existing === undefined || reading.excessMeters > existing.excessMeters) {
      this.worst.set(key, reading);
    }

    if (this.worstOverall === undefined || reading.excessMeters > this.worstOverall.excessMeters) {
      this.worstOverall = reading;
    }
  }

  countOf(reading: BodyOverlapReading): number {
    return this.counts.get(BodyOverlapTally.countKey(reading)) ?? 0;
  }

  clear(): void {
    this.worst.clear();
    this.counts.clear();
    this.violationSamples = 0;
    this.worstOverall = undefined;
  }

  /** Dictionary タプルキーを文字列キーへ。 */
  static keyOf(reading: BodyOverlapReading): string {
    return `${reading.subjectA}:${reading.partA as number}|${reading.subjectB}:${reading.partB as number}`;
  }

  private static countKey(reading: BodyOverlapReading): string {
    return BodyOverlapTally.keyOf(reading);
  }
}
