using Ryona.Game;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>
    /// テスト間で持ち越すと困る static な検証用フラグを、まとめて初期状態へ戻す。
    ///
    /// シーンを読み直すたびに <see cref="TestScene.LoadPrototype"/> から呼ぶ。
    /// ここで戻すのは「テスト本体より前に立てる必要がない」ものだけにする。
    /// <see cref="EnemyBrain.ForcedGrabKind"/> のようにテスト側が Load 前に立てる値は
    /// 触らない（各テストの SetUp / TearDown が戻す）。
    /// </summary>
    internal static class TestOverrides
    {
        public static void ResetAll()
        {
            // 引きずり起こしは既定で抽選しない（専用のテストだけが Forced を立てる）。
            GroundHaulUp.Chance = 0f;
            GroundHaulUp.Forced = false;

            // 前のテストがヒットストップの途中で終わっても、次のテストは動く状態から始める。
            if (HitStop.IsActive)
                HitStop.RestoreNow();
            Time.timeScale = 1f;
        }
    }
}
