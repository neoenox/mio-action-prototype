namespace Ryona.Game
{
    /// <summary>
    /// ダメージを受けられるもの。プレイヤーの攻撃はこのインターフェースだけを見る。
    /// </summary>
    public interface IDamageable
    {
        HitResult TakeHit(CombatState attacker, AttackData attack);

        bool IsAlive { get; }
    }
}
