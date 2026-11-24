/** 押さえつけ担当の候補。Core がゲーム層の型を参照しないための最小データ。 */
export class PinCandidate {
  readonly name: string;
  readonly height: number;
  readonly canPin: boolean;

  constructor(name: string | null | undefined, height: number, canPin: boolean) {
    this.name = name ?? '';
    this.height = height;
    this.canPin = canPin;
    Object.freeze(this);
  }
}

/** 押さえつけ担当を 1 体だけ決める。列挙順に依存しない。 */
export const PinOwnerRules = {
  /** 背丈がこの差以内なら同点として名前で決める。 */
  HeightEpsilon: 0.0001,

  /** 候補から担当を 1 体選ぶ。選ばれた添字。候補が無ければ -1。 */
  pick(candidates: readonly PinCandidate[] | null | undefined): number {
    if (candidates === null || candidates === undefined || candidates.length === 0) return -1;

    let hasAuthored = false;
    for (let i = 0; i < candidates.length; i++) {
      if (candidates[i]!.canPin) {
        hasAuthored = true;
        break;
      }
    }

    let best = -1;
    for (let i = 0; i < candidates.length; i++) {
      const candidate = candidates[i]!;
      if (hasAuthored && !candidate.canPin) continue;

      if (best < 0) {
        best = i;
        continue;
      }

      const current = candidates[best]!;
      const diff = candidate.height - current.height;

      const taller = diff > PinOwnerRules.HeightEpsilon;
      // 序数比較は JS の < で C# の string.CompareOrdinal < 0 と一致する。
      const sameHeightButEarlierName =
        Math.abs(diff) <= PinOwnerRules.HeightEpsilon && candidate.name < current.name;

      if (taller || sameHeightButEarlierName) best = i;
    }

    return best;
  },
} as const;
