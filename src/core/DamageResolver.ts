import { AttackData, AttackKind } from './AttackData.js';
import { StaggerTier } from './CombatConstants.js';
import { CombatState } from './CombatState.js';
import { DEFAULT_HIT_RESULT, HitResult } from './HitResult.js';

/** ヒット解決の唯一の入口。演出もAIもここを通す。 */
export const DamageResolver = {
  resolve(
    attacker: CombatState | null | undefined,
    attack: AttackData | null | undefined,
    defender: CombatState | null | undefined,
    scale = 1,
  ): HitResult {
    if (attacker == null || attack == null || defender == null) return DEFAULT_HIT_RESULT;

    if (scale < 0) scale = 0;

    // 攻撃側の腕の損傷は「与ダメージ」を落とす（仕様書 §4.2）
    const damage = attack.damage * attacker.body.attackDamageMultiplier * scale;
    const partDamage = attack.partDamage * scale;

    // --- 適用 ---
    defender.applyHpDamage(damage);
    if (partDamage > 0) defender.body.apply(attack.targetPart, partDamage);

    return new HitResult(damage, DamageResolver.tierFromKind(attack.kind));
  },

  tierFromKind(kind: AttackKind): StaggerTier {
    switch (kind) {
      case AttackKind.Light: return StaggerTier.Small;
      case AttackKind.Heavy: return StaggerTier.Medium;
      case AttackKind.Break: return StaggerTier.Medium;
      case AttackKind.Grapple: return StaggerTier.Large;
      default: return StaggerTier.None;
    }
  },
} as const;
