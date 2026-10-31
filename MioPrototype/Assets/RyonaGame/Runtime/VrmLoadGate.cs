using System.Threading;

namespace Ryona.Game
{
    /// <summary>
    /// UniVRM の読み込みを 1 本に絞るための関門。
    ///
    /// プレイヤーと敵で同時に VRM を読み込むと、2 コアを占有したまま
    /// ゲームが応答しなくなる（実測）。VRM は 15MB 前後あり、
    /// 読み込みの中でメッシュ変換が走るため、同時に走らせると詰まる。
    ///
    /// 1 体ずつ読むと、3 体でも数秒で終わる。
    /// </summary>
    public static class VrmLoadGate
    {
        public static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
    }
}
