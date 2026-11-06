namespace Ryona
{
    /// <summary>
    /// 1回のヒット判定の結果。演出側はこの値だけを見て再生モーションを選べばよい。
    /// </summary>
    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly StaggerTier Stagger;
        public readonly bool Blocked;

        public HitResult(
            float damage,
            StaggerTier stagger,
            bool blocked)
        {
            Damage = damage;
            Stagger = stagger;
            Blocked = blocked;
        }

        public float StunSeconds => CombatConstants.StunSeconds(Stagger);
    }
}
