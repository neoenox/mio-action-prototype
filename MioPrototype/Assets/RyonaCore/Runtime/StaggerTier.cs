namespace Ryona
{
    /// <summary>
    /// 怯みの段階。持続時間は <see cref="CombatConstants.StunSeconds"/> で解決する。
    /// </summary>
    public enum StaggerTier
    {
        None = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        GuardBreak = 4
    }
}
