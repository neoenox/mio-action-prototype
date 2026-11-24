import { CombatConstants } from './CombatConstants.js';

/**
 * 掴まれ方。正面からの「腕を回す掴み」は作らない（はさみ込む形が取れないため、背後からのみ成立する）。
 * Pin（押さえつけ）は仕様書 §4.3 の 3 種目。正面から肩を押さえる形なので成立する。
 */
export enum GrappleKind {
  Back = 0,
  Pin = 1,
  BearHug = 2,
  /** 足取り。逃げる彼女の足首を掴んで転ばせる。 */
  AnkleGrab = 3,
}

/** 掴まれた状態の「もがき」。仕様書 §4.3 の抵抗システム。 */
export class GrappleResistance {
  private readonly requiredInputs: number;

  readonly kind: GrappleKind;
  inputCount = 0;
  progress = 0;

  /**
   * 進捗の足し込み（1/必要数 の累積）で判定すると、12 回必要でも
   * 浮動小数の誤差で 0.9999… になり 13 回目まで逃げられない。回数で数える。
   */
  get isEscaped(): boolean {
    return this.inputCount >= Math.ceil(this.requiredInputs);
  }

  constructor(kind: GrappleKind, requiredInputs: number) {
    this.kind = kind;
    this.requiredInputs = requiredInputs < 1 ? 1 : requiredInputs;
  }

  /** 1回のレバガチャ入力。振りほどけたら true。 */
  registerInput(): boolean {
    if (this.isEscaped) return true;

    this.inputCount++;
    // 脱出判定と同じ切り上げ回数を分母にする（99.9% で逃げられない表示を防ぐ）。
    this.progress = this.inputCount / Math.ceil(this.requiredInputs);
    if (this.progress > 1) this.progress = 1;

    return this.isEscaped;
  }
}

/** 必要入力数の算出。スキル「抵抗強化」と腕の損傷を反映する。 */
export const ResistanceMath = {
  /**
   * 足取り（AnkleGrab）の基礎入力回数。真実源は RyonaConfig.Grapple.AnkleMashInputs。
   * Game 側（PlayerGrapple）が Awake でここへ写す。
   */
  ankleMashInputs: 8,

  baseInputs(kind: GrappleKind): number {
    switch (kind) {
      case GrappleKind.Pin: return CombatConstants.MashBasePin;
      case GrappleKind.BearHug: return CombatConstants.MashBaseBearHug;
      case GrappleKind.AnkleGrab: return ResistanceMath.ankleMashInputs;
      default: return CombatConstants.MashBaseBack;
    }
  },

  /** upgradeMultiplier にはスキル「抵抗強化」で 0.80 を渡す。armInjuryValue は 0..100。 */
  requiredInputs(kind: GrappleKind, upgradeMultiplier = 1, armInjuryValue = 0): number {
    let t = armInjuryValue / CombatConstants.PartMax;
    if (t < 0) t = 0;
    if (t > 1) t = 1;

    const injuryPenalty = 1 + 0.25 * t;
    const required = ResistanceMath.baseInputs(kind) * injuryPenalty * upgradeMultiplier;

    return required < 1 ? 1 : required;
  },
} as const;
