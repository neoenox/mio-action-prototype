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

        [UnityTest]
        public IEnumerator HPゼロから復帰すると起き上がって操作に戻れる()
        {
            yield return LoadPlayer();

            var lethal = AttackLibrary.Light();
            lethal.damage = _stats.State.MaxHp * 10f;
            _stats.ReceiveHit(new CombatState(), lethal, _attacker.transform);
            Assert.IsTrue(_stats.IsDead, "HP が 0 になっていない");

            float deadline = Time.realtimeSinceStartup + 2f;
            while (_anim.Current != CharacterAnimator.State.Down && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current);

            // HP が 0 のとき、HUD が R キーで呼ぶ復帰処理。
            _stats.RestoreAll();
            yield return null;
            yield return null;

            Assert.AreEqual(_stats.State.MaxHp, _stats.State.Hp);
            Assert.AreEqual(CharacterAnimator.State.GetUp, _anim.Current);
            Assert.IsTrue(_stats.IsStunned, "起き上がり中は操作を受け付けない");

            yield return new WaitForSeconds(_anim.GetUpSeconds + 0.1f);
            Assert.IsFalse(_anim.IsDowned);
            Assert.IsFalse(_stats.IsDown);
            Assert.IsFalse(_stats.IsStunned);
        }

        [UnityTest]
        public IEnumerator ゴッドモード中は致死ダメージでも死なず体力が回復する()
        {
            yield return LoadPlayer();

            _stats.SetGodMode(true);

            var lethal = AttackLibrary.Heavy();
            lethal.damage = _stats.State.MaxHp * 10f;
            _stats.ReceiveHit(new CombatState(), lethal, _attacker.transform);

            Assert.IsFalse(_stats.IsDead, "ゴッドモード中に死んでいる");
            Assert.GreaterOrEqual(_stats.State.Hp, 1f, "HP が 0 になっている");

            float before = _stats.State.Hp;
            yield return new WaitForSeconds(0.25f);
            Assert.Greater(_stats.State.Hp, before, "体力が自動回復していない");

            _stats.SetGodMode(false);
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
            Assert.IsFalse(_stats.DownIsForward, "正面からの一撃で仰向けに倒れる");
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
        public IEnumerator 正面からの蓄積でも最後が背後からなら前へ崩れる()
        {
            yield return LoadPlayer();

            var attackerState = new CombatState();
            for (int i = 0; i < 8; i++)
                _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
            Assert.IsFalse(_stats.IsDown);

            _attacker.transform.position = _stats.transform.position - _stats.transform.forward;
            _stats.ReceiveHit(attackerState, AttackLibrary.Light(), _attacker.transform);
            yield return null;
            yield return null;

            Assert.IsTrue(_stats.State.IsDown, "ダウンしていない");
            Assert.IsTrue(_stats.DownIsForward, "最後の一撃が背後なのに前へ崩れていない");
            Assert.IsFalse(_motor.IsAirborne, "前へ崩れるときに飛ばしている（その場で膝が抜ける動きにする）");
            Assert.AreEqual(CharacterAnimator.State.Down, _anim.Current, "倒れた姿勢になっていない");
        }

        [UnityTest]
        public IEnumerator うつ伏せのとき手も床についている()
        {
            yield return LoadPlayer();

            _stats.State.AddFlinch(CombatConstants.MaxFlinch - 1f);
            _attacker.transform.position = _stats.transform.position - _stats.transform.forward;
            _stats.LastHitZone = PunchZone.Face;
            _stats.ReceiveHit(new CombatState(), AttackLibrary.Light(), _attacker.transform);

            Assert.IsTrue(_stats.IsDown && _stats.DownIsForward, "背後からの一撃でうつ伏せに倒れる");

            // 倒れきるまで待つ（0.78 秒 + 余裕）
            yield return new WaitForSeconds(1.0f);

            var rig = _anim.Rig;
            Assert.IsNotNull(rig, "リグが無い");
            Assert.IsNotNull(rig.LHand, "左手が無い");
            Assert.IsNotNull(rig.RHand, "右手が無い");

            float groundY = _stats.transform.position.y;
            float lh = rig.LHand.position.y - groundY;
            float rh = rig.RHand.position.y - groundY;
            float head = rig.Head.position.y - groundY;

            // 腕が宙に浮いていない（肩の高さ 0.4m 前後に浮いていたら失敗）
            Assert.Less(lh, 0.30f, $"左手が浮いている（y={lh:F2}）");
            Assert.Less(rh, 0.30f, $"右手が浮いている（y={rh:F2}）");
            Assert.Greater(lh, -0.05f, $"左手が床に埋まっている（y={lh:F2}）");
            Assert.Greater(rh, -0.05f, $"右手が床に埋まっている（y={rh:F2}）");

            // 顔も伏せている（立ち姿勢の 1.5m ではなく、床近く）
            Assert.Less(head, 0.45f, $"顔が浮いている（y={head:F2}）");
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
