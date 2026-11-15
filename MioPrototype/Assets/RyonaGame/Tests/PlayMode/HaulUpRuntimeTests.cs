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

        IEnumerator Load()
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
            _stats.ForceDown(6f, "haul-test", launchDistance: 0f, forward: false);
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
            Transform hand = haul.HerLeftWrist ? rig.LHand : rig.RHand;
            var figure = _enemy.GetComponent<EnemyFigure>();
            Transform enemyHand = figure.WristBone(haul.EnemyLeftArm);
            float ground = _stats.transform.position.y;
            float headStart = rig.Head.position.y - ground;
            float handStart = hand.position.y - ground;

            // 引き上げの間: 彼女は自分では起き上がらない（起き上がりの状態に入らない）。
            // 掴まれた手首は敵の手と合ったまま、上へ持ち上がる。
            float worstGap = 0f;
            float worstAt = 0f;
            while (haul.Stage == GroundHaulUp.Beat.Haul)
            {
                yield return null;
                Assert.IsFalse(_stats.IsGettingUp, "引き起こしの間は自分で起き上がる動きを使わない");
                if (haul.Stage == GroundHaulUp.Beat.Haul && haul.Grip > 0.95f)
                {
                    float gap = Vector3.Distance(hand.position, enemyHand.position);
                    if (gap > worstGap)
                    {
                        worstGap = gap;
                        worstAt = haul.Progress;
                    }
                }
            }
            Assert.AreEqual(GroundHaulUp.Beat.Settle, haul.Stage);
            float headEnd = rig.Head.position.y - ground;
            float handEnd = hand.position.y - ground;
            Debug.Log($"[HaulCheck] head {headStart:F2}->{headEnd:F2} hand {handStart:F2}->{handEnd:F2} " +
                      $"gap(max)={worstGap:F3} at={worstAt:F2}");
            Assert.Greater(headEnd, 1.15f, "引き上げ終わったら頭が立った高さにある");
            // 掴まれた瞬間に手首は床から少し浮く（実測 0.47m）ので、上がり量ではなく
            // 「立ったときに肩の高さ以上まで吊り上げられている」ことと、大きく動いたことを見る
            // （実測: 0.47 → 1.27m）。
            Assert.Greater(handEnd, 1.20f, "掴まれた手首が肩の高さ以上まで吊り上げられる");
            Assert.Greater(handEnd - handStart, 0.70f, "掴まれた手首が上へ大きく引き上げられる");
            Assert.Less(worstGap, 0.12f, "引き上げの間、掴まれた手首と敵の手が離れない");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(_grapple.IsGrappled, "立たせたら羽交い締めへつなぐ");
            Assert.IsFalse(_stats.IsDown, "羽交い締めのときは立っている");
            Assert.AreEqual(GrappleKind.Back, _grapple.Kind);
            Assert.AreSame(_enemy, _grapple.Captor);
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
