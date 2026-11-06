using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// ひるみ（ダウンゲージ）が溜まったときのダウン。
    ///
    /// ・軽い攻撃を当て続けるとダウンする
    /// ・ダウンは後ろへ放物線で飛ぶ（その場で崩れるだけにしない）
    /// ・ダウン中はひるみが溜まらない（起き上がるそばから倒れ続けない）
    /// </summary>
    public sealed class DownRuntimeTests
    {
        PlayerStats _stats;
        PlayerMotor _motor;
        CharacterAnimator _anim;
        GameObject _attacker;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_attacker != null)
                Object.Destroy(_attacker);
            _attacker = null;
        }

        IEnumerator LoadPlayer()
        {
            yield return TestScene.LoadPrototype();

            _stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_stats, "PlayerStats がシーンに無い");
            _motor = _stats.GetComponent<PlayerMotor>();
            Assert.IsNotNull(_motor, "PlayerMotor が PlayerStats と同じオブジェクトに無い");

            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                    break;
                yield return null;
            }

            Assert.IsTrue(_anim != null && _anim.Rig != null && _anim.Rig.Ready,
                "キャラクターのリグが組み上がらない");

            // テスト中は AI に殴らせない（当てるのはこのテストだけ）
            foreach (var enemy in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy != null)
                    enemy.HoldFire(9999f);
            }

            // 殴る側は彼女の正面 0.8m に置く（吹き飛ぶ向きが後ろになる）
            _attacker = new GameObject("Down Test Attacker");
            _attacker.transform.position = _stats.transform.position + _stats.transform.forward * 0.8f;
        }

        /// <summary>軽い攻撃を、ダウンするまで当てる。当てた回数を返す。</summary>
        IEnumerator HitUntilDown(System.Action<int> onHit = null)
        {
            var attackerState = new CombatState();
            for (int i = 0; i < 30 && !_stats.State.IsDown; i++)
            {
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
                onHit?.Invoke(i + 1);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator 軽い攻撃を当て続けるとダウンする()
        {
            yield return LoadPlayer();

            int hits = 0;
            yield return HitUntilDown(n => hits = n);

            Assert.IsTrue(_stats.State.IsDown,
                $"{hits} 発当ててもダウンしない（ひるみ={_stats.State.Flinch:F0}）");
            Assert.AreEqual(9, hits, "軽い攻撃は 9 発でダウンする想定");
        }

        [UnityTest]
        public IEnumerator ダウンは後ろへ放物線で飛ぶ()
        {
            yield return LoadPlayer();

            Vector3 start = _stats.transform.position;
            yield return HitUntilDown();

            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.IsTrue(_motor.IsAirborne, "ダウンの瞬間に浮いていない（その場で崩れるだけになっている）");

            // 0.08 秒後には目に見えて浮いている（滞空は 0.24 秒）
            yield return new WaitForSeconds(0.08f);
            Assert.Greater(_stats.transform.position.y, start.y + 0.05f,
                $"浮いていない（y={_stats.transform.position.y:F2}）");

            // 0.6 秒後には着地して、後ろへ下がっている（姿勢側でも倒れるので 0.3m で見る）
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(_motor.IsAirborne, "着地していない");

            Vector3 moved = _stats.transform.position - start;
            moved.y = 0f;
            Assert.Greater(moved.magnitude, 0.2f, $"吹き飛んでいない（{moved.magnitude:F2}m）");

            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current,
                "倒れた姿勢になっていない");
        }

        [UnityTest]
        public IEnumerator ダウン中はひるみが溜まらない()
        {
            yield return LoadPlayer();

            yield return HitUntilDown();
            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.AreEqual(0f, _stats.State.Flinch, 0.001f, "ダウン時にひるみが 0 に戻っていない");

            // 倒れたまま 3 発殴られる
            var attackerState = new CombatState();
            for (int i = 0; i < 3; i++)
            {
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
                yield return null;
            }

            Assert.AreEqual(0f, _stats.State.Flinch, 0.001f,
                "ダウン中にひるみが溜まっている（起き上がるそばから倒れ続ける）");
        }

        [UnityTest]
        public IEnumerator ダウンから起き上がって構えに戻る()
        {
            yield return LoadPlayer();

            yield return HitUntilDown();
            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");

            // ダウン 1.2 秒 + 起き上がり 0.8 秒 + 余裕
            yield return new WaitForSeconds(CombatConstants.DownDuration + 0.5f);

            Assert.IsFalse(_stats.State.IsDown, "ダウンが明けていない");
            Assert.AreEqual(CharacterAnimator.State.Idle, _anim.Current,
                $"起き上がって構えに戻っていない（{_anim.Current}）");
        }
    }
}
