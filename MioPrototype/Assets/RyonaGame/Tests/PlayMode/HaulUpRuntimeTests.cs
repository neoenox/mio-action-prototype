using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// ダウンした彼女を、敵が手首と裾を掴んで引きずり起こし、羽交い締めへつなぐ（<see cref="GroundHaulUp"/>）。
    ///
    /// ・掴んで引き上げ、立たせたら背後から羽交い締めにする
    /// ・彼女は自分では起きない。掴まれた手首が敵の手と合ったまま上へ引かれ、体が持ち上がる
    /// ・掴んでから引き上げの間、連打で振りほどける（敵はよろける）
    /// ・引き起こしの間、通常の寝技（マウント）には入らない
    /// </summary>
    public sealed class HaulUpRuntimeTests
    {
        PlayerStats _stats;
        PlayerGrapple _grapple;
        CharacterAnimator _anim;
        EnemyBrain _enemy;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GroundHaulUp.Forced = false;
        }

        IEnumerator Load(bool prone = false)
        {
            yield return TestScene.LoadPrototype();
            GroundHaulUp.Forced = true;

            _stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_stats);
            _grapple = _stats.GetComponent<PlayerGrapple>();

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                    break;
                yield return null;
            }
            Assert.IsTrue(_anim != null && _anim.Rig != null && _anim.Rig.Ready, "リグが組み上がらない");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.Greater(enemies.Length, 0);
            _enemy = enemies[0];
            foreach (var enemy in enemies)
                enemy.enabled = enemy == _enemy;
            _enemy.LockForHandOff(0.1f);
            _enemy.transform.position = _stats.transform.position + Vector3.right * 1.6f;
            _stats.ForceDown(6f, "haul-test", launchDistance: 0f, forward: prone);
        }

        IEnumerator WaitStage(GroundHaulUp haul, GroundHaulUp.Beat stage, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (haul.Stage != stage && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(stage, haul.Stage, $"引き起こしが {stage} に進まない");
        }

        [UnityTest]
        public IEnumerator HaulsHerUpThenHoldsFromBehind()
        {
            yield return Load();
            var haul = _stats.GroundMount.Haul;

            yield return WaitStage(haul, GroundHaulUp.Beat.Approach, 6f);
            Assert.AreSame(_enemy, haul.Attacker);
            Assert.IsNull(_stats.GroundMount.Attacker, "引き起こし中は寝技に入らない");

            yield return WaitStage(haul, GroundHaulUp.Beat.Grab, 6f);
            Assert.IsTrue(_stats.IsDown, "掴んでいる間は、まだ倒れたまま");
            Assert.IsTrue(_stats.GroundMount.BlocksGetUp, "掴んでいる間は勝手に起き上がれない");

            yield return WaitStage(haul, GroundHaulUp.Beat.Haul, 3f);
            var rig = _anim.Rig;
            Transform head = rig.Head;
            float ground = _stats.transform.position.y;
            float headStart = head.position.y - ground;

            // 引き上げの間: 彼女は自分では起き上がらない（起き上がりの状態に入らない）。
            while (haul.Stage == GroundHaulUp.Beat.Haul)
            {
                yield return null;
                Assert.IsFalse(_stats.IsGettingUp, "引き起こしの間は自分で起き上がる動きを使わない");
            }
            Assert.AreEqual(GroundHaulUp.Beat.Settle, haul.Stage);
            float headEnd = head.position.y - ground;
            Debug.Log($"[HaulCheck] head {headStart:F2}->{headEnd:F2}");
            Assert.Greater(headEnd, 1.15f, "抱え上げたら頭が立った高さにある");
            Assert.Greater(headEnd - headStart, 0.70f, "頭が大きく持ち上げられる");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsGrappled, "立たせたら羽交い締めへつなぐ");
            Assert.IsFalse(_stats.IsDown, "羽交い締めのときは立っている");
            Assert.AreEqual(GrappleKind.Back, _grapple.Kind);
            Assert.AreSame(_enemy, _grapple.Captor);
            Assert.IsTrue(_enemy.IsHolding, "引き渡し後は敵も拘束状態に入る");
            Assert.AreEqual(EnemyPhase.Recover, _enemy.Phase, "引き渡し後は敵が硬直（拘束）になる");
            Assert.IsFalse(haul.Active);
        }

        [UnityTest]
        public IEnumerator HaulsProneUpIntoFrontHold()
        {
            yield return Load(prone: true);
            var haul = _stats.GroundMount.Haul;

            yield return WaitStage(haul, GroundHaulUp.Beat.Haul, 8f);
            float deadline = Time.realtimeSinceStartup + 4f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "うつ伏せから起こしたら前方拘束へつなぐ");
            Assert.IsFalse(_stats.IsDown, "拘束のときは立っている");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind, "うつ伏せダウンは前方拘束");
            Assert.AreSame(_enemy, _grapple.Captor);
            Assert.IsTrue(_enemy.IsHolding, "前方拘束でも敵が拘束状態に入る");
            Assert.IsFalse(haul.Active);
        }

        [UnityTest]
        public IEnumerator MashingBreaksFreeAndEnemyStaggers()
        {
            yield return Load();
            var haul = _stats.GroundMount.Haul;

            yield return WaitStage(haul, GroundHaulUp.Beat.Grab, 8f);
            for (int i = 0; i < 6; i++)
                haul.Struggle();

            Assert.IsFalse(haul.Active, "連打で振りほどける");
            Assert.IsFalse(_grapple.IsGrappled, "振りほどいたら拘束にはつながない");
            Assert.AreEqual(EnemyPhase.Stagger, _enemy.Phase, "振りほどかれた敵はよろける");
        }
    }
}
