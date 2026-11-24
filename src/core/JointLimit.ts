/** 可動域チェックの対象にする関節。左右は JointSide で分ける。 */
export enum JointId {
  Elbow = 0,
  Knee = 1,
  HipFlexion = 2,
  HipAbduction = 3,
  ShoulderFlexion = 4,
  ShoulderAbduction = 5,
  SpinePitch = 6,
  SpineYaw = 7,
  SpineRoll = 8,
  NeckPitch = 9,
  NeckYaw = 10,
  NeckRoll = 11,
}

/** 関節の左右。体幹・首は Center。 */
export enum JointSide {
  Center = 0,
  Left = 1,
  Right = 2,
}

/** 可動域チェックの判定。 */
export enum JointRomVerdict {
  Within = 0,
  OverFlexed = 1,
  ReverseBend = 2,
}

/** 1 関節ぶんの人の可動域。角度はすべて度。 */
export class JointLimit {
  readonly id: JointId;
  readonly label: string;
  readonly min: number;
  readonly max: number;
  readonly slackLow: number;
  readonly slackHigh: number;

  constructor(id: JointId, label: string, min: number, max: number, slackLow: number, slackHigh: number) {
    this.id = id;
    this.label = label;
    this.min = min;
    this.max = max;
    this.slackLow = slackLow;
    this.slackHigh = slackHigh;
    Object.freeze(this);
  }

  get warnBelow(): number {
    return this.min - this.slackLow;
  }

  get warnAbove(): number {
    return this.max + this.slackHigh;
  }

  /** 表示用の可動域（例: 「0〜145°」）。 */
  get rangeText(): string {
    return `${Math.round(this.min)}〜${Math.round(this.max)}°`;
  }
}

const STANDARD_SLACK = 10;

// JointId の数値がそのまま添字になるように並べる（テストで検査している）。
const jointTable: readonly JointLimit[] = [
  new JointLimit(JointId.Elbow, 'ひじ', 0, 145, 20, STANDARD_SLACK),
  new JointLimit(JointId.Knee, 'ひざ', 0, 150, 20, STANDARD_SLACK),
  new JointLimit(JointId.HipFlexion, '股関節(曲げ)', -25, 120, 15, STANDARD_SLACK),
  new JointLimit(JointId.HipAbduction, '股関節(開き)', -30, 50, STANDARD_SLACK, 20),
  new JointLimit(JointId.ShoulderFlexion, '肩(前後)', -70, 180, 25, 15),
  new JointLimit(JointId.ShoulderAbduction, '肩(開き)', -45, 180, 15, 15),
  new JointLimit(JointId.SpinePitch, '体幹(前後)', -35, 90, 15, 20),
  new JointLimit(JointId.SpineYaw, '体幹(ひねり)', -50, 50, 20, 20),
  new JointLimit(JointId.SpineRoll, '体幹(左右)', -45, 45, 15, 15),
  new JointLimit(JointId.NeckPitch, '首(前後)', -60, 50, 15, 15),
  new JointLimit(JointId.NeckYaw, '首(ひねり)', -80, 80, 15, 15),
  new JointLimit(JointId.NeckRoll, '首(左右)', -45, 45, 15, 15),
];

/** 人体の主要な関節の可動域テーブル。 */
export const HumanJointLimits = {
  StandardSlack: STANDARD_SLACK,
  All: jointTable,
  Count: jointTable.length,

  of(joint: JointId): JointLimit {
    const index = joint as number;
    if (index < 0 || index >= jointTable.length) {
      throw new RangeError(`未定義の関節です: ${joint}`);
    }
    return jointTable[index]!;
  },

  isKnown(joint: JointId): boolean {
    const index = joint as number;
    return index >= 0 && index < jointTable.length;
  },
} as const;
