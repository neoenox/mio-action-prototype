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
            float postureDamage = attack.postureDamage * scale;
            float partDamage = attack.partDamage * scale;
            float guardStaminaDamage = 0f;

            // 崩し攻撃はガードを無視する
            bool blocked = defenderBlocking && !attack.IsUnblockable;

            if (blocked)
            {
                damage *= CombatConstants.GuardDamageScale;

                // 腕が弱るとガードが持たない = 相手のガード削りが実質的に増える
                float guardDurability = defender.Body.GuardDurabilityMultiplier;
                postureDamage *= CombatConstants.GuardPostureScale;
                partDamage *= CombatConstants.GuardPartDamageScale;
                guardStaminaDamage = attack.guardStaminaDamage / guardDurability;
            }

            // --- 適用 -------------------------------------------------
            defender.ApplyHpDamage(damage);
            if (guardStaminaDamage > 0f)
                defender.ConsumeStamina(guardStaminaDamage);
            if (partDamage > 0f)
                defender.Body.Apply(attack.targetPart, partDamage);

            float postureAfter = defender.Posture - postureDamage;
            bool postureBroken = postureAfter <= 0f;

            bool guardBroken = guardStaminaDamage > 0f && defender.IsGuardBroken;

            StaggerTier tier;
            if (guardBroken)
            {
                tier = StaggerTier.GuardBreak;
                defender.SetPosture(CombatConstants.MaxPosture * CombatConstants.PostureBreakRecoverRatio);
            }
            else if (postureBroken)
            {
                tier = StaggerTier.Large;
                defender.SetPosture(CombatConstants.MaxPosture * CombatConstants.PostureBreakRecoverRatio);
            }
            else
            {
                defender.SetPosture(postureAfter);
                tier = TierFromKind(attack.kind);
            }

            return new HitResult(
                damage,
                postureDamage,
                guardStaminaDamage,
                tier,
                blocked,
                guardBroken,
                postureBroken);
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
