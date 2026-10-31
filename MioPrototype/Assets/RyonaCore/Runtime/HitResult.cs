namespace Ryona
{
    /// <summary>
    /// 1回のヒット判定の結果。演出側はこの値だけを見て再生モーションを選べばよい。
    /// </summary>
    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly float PostureDamage;
        public readonly float GuardStaminaDamage;
        public readonly StaggerTier Stagger;
        public readonly bool Blocked;
        public readonly bool GuardBroken;
        public readonly bool PostureBroken;

        public HitResult(
            float damage,
            float postureDamage,
            float guardStaminaDamage,
            StaggerTier stagger,
            bool blocked,
            bool guardBroken,
            bool postureBroken)
        {
            Damage = damage;
            PostureDamage = postureDamage;
            GuardStaminaDamage = guardStaminaDamage;
            Stagger = stagger;
            Blocked = blocked;
            GuardBroken = guardBroken;
            PostureBroken = postureBroken;
        }

        public float StunSeconds => CombatConstants.StunSeconds(Stagger);
    }
}
