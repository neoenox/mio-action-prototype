import { CombatConstants, StaggerTier } from './CombatConstants.js';

/** 1回のヒット判定の結果。演出側はこの値だけを見て再生モーションを選べばよい。 */
export class HitResult {
  readonly damage: number;
  readonly stagger: StaggerTier;

  constructor(damage: number, stagger: StaggerTier) {
    this.damage = damage;
    this.stagger = stagger;
    Object.freeze(this);
  }

  get stunSeconds(): number {
    return CombatConstants.stunSeconds(this.stagger);
  }
}

/** C# の default(HitResult) 相当（C# enum の 0 = None）。 */
export const DEFAULT_HIT_RESULT = new HitResult(0, StaggerTier.None);
