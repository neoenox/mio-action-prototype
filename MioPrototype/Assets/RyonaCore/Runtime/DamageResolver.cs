namespace Ryona
{
    /// <summary>
    /// ヒット解決の唯一の入口。演出もAIもここを通す。
    /// MonoBehaviour に依存しないので、テストで完全に検証できる。
    /// </summary>
    public static class DamageResolver
    {
        public static HitResult Resolve(
            CombatState attacker,
            AttackData attack,
            CombatState defender,
            bool defenderBlocking,
            float scale = 1f)
        {
            if (attacker == null || attack == null || defender == null)
                return default;

            if (scale < 0f)
                scale = 0f;

            // 攻撃側の腕の損傷は「与ダメージ」を落とす（仕様書 §4.2）
            float damage = attack.damage * attacker.Body.AttackDamageMultiplier * scale;
            float partDamage = attack.partDamage * scale;

            // 崩し攻撃はガードを無視する
            bool blocked = defenderBlocking && !attack.IsUnblockable;

            if (blocked)
            {
                damage *= CombatConstants.GuardDamageScale;
                partDamage *= CombatConstants.GuardPartDamageScale;
            }

            // --- 適用 -------------------------------------------------
            defender.ApplyHpDamage(damage);
            if (partDamage > 0f)
                defender.Body.Apply(attack.targetPart, partDamage);

            return new HitResult(
                damage,
                TierFromKind(attack.kind),
                blocked);
        }

        static StaggerTier TierFromKind(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.Light: return StaggerTier.Small;
                case AttackKind.Heavy: return StaggerTier.Medium;
                case AttackKind.Break: return StaggerTier.Medium;
                case AttackKind.Grapple: return StaggerTier.Large;
                default: return StaggerTier.None;
            }
        }
    }
}
