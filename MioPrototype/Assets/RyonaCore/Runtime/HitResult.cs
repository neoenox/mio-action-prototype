namespace Ryona
{
    /// <summary>
    /// 1回のヒット判定の結果。演出側はこの値だけを見て再生モーションを選べばよい。
    /// </summary>
    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly StaggerTier Stagger;

        public HitResult(float damage, StaggerTier stagger)
        {
            Damage = damage;
            Stagger = stagger;
        }

        public float StunSeconds => CombatConstants.StunSeconds(Stagger);
    }
}
