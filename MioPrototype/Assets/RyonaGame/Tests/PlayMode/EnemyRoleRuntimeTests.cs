using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>敵の役割分けの回帰テスト。</summary>
    public sealed class EnemyRoleRuntimeTests
    {
        [UnityTest]
        public IEnumerator 背丈で役割が決まりCanPinが掴み役だけに立つ()
        {
            yield return TestScene.LoadPrototype();
            yield return null;

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.AreEqual(3, enemies.Length);

            int grapplers = 0, tools = 0, strikers = 0;
            foreach (var enemy in enemies)
            {
                switch (enemy.Role)
                {
                    case EnemyRole.Grappler: grapplers++; break;
                    case EnemyRole.Tools: tools++; break;
                    case EnemyRole.Striker: strikers++; break;
                }
                Assert.AreEqual(enemy.Role == EnemyRole.Grappler, enemy.CanPin,
                    $"{enemy.name} の CanPin が役割と一致しない");
            }
            Assert.AreEqual(1, grapplers);
            Assert.AreEqual(1, tools);
            Assert.AreEqual(1, strikers);

            foreach (var enemy in enemies)
            {
                if (enemy.Role == EnemyRole.Striker)
                    Assert.Greater(enemy.RoleMoveScale, 1f, "打撃役は移動が速い");
                if (enemy.Role == EnemyRole.Tools)
                    Assert.AreEqual(1f, enemy.RoleMoveScale, "道具役は移動倍率を変えない");
            }
        }
    }
}
