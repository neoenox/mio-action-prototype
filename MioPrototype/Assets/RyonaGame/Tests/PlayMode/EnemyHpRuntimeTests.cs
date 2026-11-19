using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 敵の最大 HP が設定アセット（<see cref="RyonaConfig.EnemySettings.MaxHp"/>）で初期化されることを固定する。
    /// フィールドの既定値に戻ったり、1 HP になるとここで落ちる。
    /// </summary>
    public sealed class EnemyHpRuntimeTests
    {
        [UnityTest]
        public IEnumerator 敵の最大HPは設定アセットの値になる()
        {
            yield return TestScene.LoadPrototype();

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 1, "シーンに敵が無い");

            float expected = RyonaConfig.Instance.Enemy.MaxHp;
            Assert.GreaterOrEqual(expected, 1f, "設定値が 1 未満");

            foreach (var enemy in enemies)
            {
                Assert.AreEqual(expected, enemy.State.MaxHp, 0.001f, $"{enemy.name} の最大 HP");
                Assert.AreEqual(expected, enemy.State.Hp, 0.001f, $"{enemy.name} の初期 HP");
            }
        }
    }
}
