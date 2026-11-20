using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>手首ロック（拘束リング）の回帰テスト。</summary>
    public sealed class WristLockRuntimeTests
    {
        PlayerWristLock _lock;
        PlayerStats _stats;
        EnemyBrain _enemy;

        IEnumerator Setup()
        {
            yield return TestScene.LoadPrototype();
            _stats = Object.FindFirstObjectByType<PlayerStats>();
            _lock = _stats.GetComponent<PlayerWristLock>();
            Assert.IsNotNull(_lock);
            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            _enemy = enemies[0];
        }

        [UnityTest]
        public IEnumerator 八回で割れ四秒で自然に解ける()
        {
            yield return Setup();
            Assert.IsTrue(_lock.TryAttach(_enemy));
            Assert.IsTrue(_lock.IsLocked);
            Assert.Less(_lock.MoveScale, 1f);
            for (int i = 0; i < 7; i++)
                _lock.BreakInput();
            Assert.IsTrue(_lock.IsLocked, "7 回では割れない");
            _lock.BreakInput();
            Assert.IsFalse(_lock.IsLocked, "8 回で割れる");

            Assert.IsFalse(_lock.IsLocked);
            Assert.IsTrue(_lock.InGrace);
            Assert.IsFalse(_lock.TryAttach(_enemy), "猶予中は付けない");
        }

        [UnityTest]
        public IEnumerator 掴まれると外れて必要回数が増える()
        {
            yield return Setup();
            Assert.IsTrue(_lock.TryAttach(_enemy));
            var grapple = _stats.GetComponent<PlayerGrapple>();
            float before = 10f;
            _lock.DetachOnGrab();
            Assert.IsFalse(_lock.IsLocked);
            Assert.Greater(_lock.EscapeNeedScale * before, before);
        }
    }
}
