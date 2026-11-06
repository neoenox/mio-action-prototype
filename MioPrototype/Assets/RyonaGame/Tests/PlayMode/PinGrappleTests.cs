using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）の回帰テスト。
    ///
    /// 後ろ拘束（Back）は時間切れで解けないが、押さえつけは
    /// 4.2 秒の制限時間内に 12 回もがけば逃げられ、時間切れなら投げられる。
    /// 入口は正面 0.90〜1.30m・攻撃トークンあり・クールダウン外だけ。
    /// </summary>
    public sealed class PinGrappleTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;
        EnemyBrain _other;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

        /// <summary>シーンを読み、押さえる側を正面 1.15m に置く。</summary>
        IEnumerator Load()
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 2, "押さえる側と、殴らないことを確かめる側で 2 体必要");

            _captor = enemies[0];
            _other = enemies[1];
            for (int i = 2; i < enemies.Length; i++)
                enemies[i].enabled = false;

            // プレイヤーは +X 向き。押さえる側は正面（+X）。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);

            _captor.CanPin = true;
            _captor.enabled = false;
            _other.enabled = false;

            yield return null;
        }

        [UnityTest]
        public IEnumerator 押さえつけは制限時間つきで始まり相手の方を向く()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.Pin);

            Assert.IsTrue(_grapple.IsGrappled, "押さえつけが始まらない");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit, "押さえつけは時間切れがある");
            Assert.AreEqual(CombatConstants.GrappleLimitSeconds, _grapple.Remaining, 0.05f,
                $"制限時間は {CombatConstants.GrappleLimitSeconds:F1} 秒");
            Assert.AreEqual(CombatConstants.MashBasePin, _grapple.RequiredInputs, 0.01f,
                "必要入力は 12 回");

            // 正面から掴まれるので、相手の方を向く
            Vector3 toCaptor = _captor.transform.position - _player.transform.position;
            toCaptor.y = 0f;
            Assert.Greater(Vector3.Dot(_player.transform.forward, toCaptor.normalized), 0.9f,
                "押さえつけは正面からなので、彼女は相手を向く");

            // 掴んだ距離は腕を伸ばす余地を残す 0.60m
            Assert.AreEqual(0.60f, toCaptor.magnitude, 0.05f, "掴んだ距離が 0.60m になっていない");
        }

        [UnityTest]
        public IEnumerator もがき12回で振りほどけ押さえた側がよろける()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.Pin);
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);

            for (int i = 0; i < need - 1; i++)
            {
                _grapple.Struggle();
                Assert.IsTrue(_grapple.IsGrappled, $"{i + 1} 回目でもがききっていない");
            }

            _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "12 回もがけば振りほどける");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase, "振りほどかれた側はよろける（反撃の隙）");
        }

        [UnityTest]
        public IEnumerator 時間切れで投げられ腕に傷害とスタミナ消費が残る()
        {
            yield return Load();

            bool failed = false;
            _grapple.Failed += () => failed = true;

            float staminaBefore = _player.State.Stamina;
            float armDamageBefore = _player.State.Body[BodyPart.Arms];

            _grapple.Begin(_captor, GrappleKind.Pin);
            yield return new WaitForSeconds(CombatConstants.GrappleLimitSeconds + 0.4f);

            Assert.IsTrue(failed, "時間切れで Failed が飛んでいない");
            Assert.IsFalse(_grapple.IsGrappled, "投げられたら掴みは解ける");
            Assert.Less(_player.State.Stamina, staminaBefore - 1f, "スタミナが削られる");
            // Body は「損傷量」（0 = 無傷）。投げで腕に +30 入る。
            Assert.Greater(_player.State.Body[BodyPart.Arms], armDamageBefore + 20f,
                "腕に傷害が残る");
        }

        [UnityTest]
        public IEnumerator 背後や間合い外では押さえつけを始めない()
        {
            yield return Load();

            // 正面 1.15m・トークン不要の直接指示なら始められる
            Assert.IsTrue(_captor.CanStartPin(1.15f), "正面 1.15m では始められること");

            // 間合いの外
            Assert.IsFalse(_captor.CanStartPin(0.60f), "近すぎる（0.60m）と始めない");
            Assert.IsFalse(_captor.CanStartPin(1.60f), "遠すぎる（1.60m）と始めない");

            // 背後からは羽交い締め（Back）なので、押さえつけは出さない
            _captor.transform.position = _player.transform.position - Vector3.right * 1.15f;
            yield return null;
            Assert.IsFalse(_captor.CanStartPin(1.15f), "背後からは押さえつけない");

            // CanPin が false なら、正面でも始めない
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.CanPin = false;
            yield return null;
            Assert.IsFalse(_captor.CanStartPin(1.15f), "CanPin が false なら始めない");
        }

        [UnityTest]
        public IEnumerator 押さえつけ中に膝蹴りが5回入る()
        {
            yield return Load();

            // 実際の入口（予備動作 → 当たり → 押さえつけ）から始める。膝蹴りは Recover 中に入る。
            _captor.enabled = true;
            _captor.QueuePin();

            float deadline = Time.realtimeSinceStartup + 4f;
            while (!_grapple.IsGrappled && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "押さえつけが始まらない");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind);

            int hits = 0;
            float hpBefore = _player.State.Hp;
            _player.HitReceived += _ => hits++;

            Assert.AreEqual(0, hits, "掴んだ瞬間はまだ膝蹴りは入らない");

            // 膝蹴りは 1.1 秒後から 0.55 秒間隔で 5 発（最後は 3.3 秒）。
            yield return new WaitForSeconds(3.6f);

            Assert.AreEqual(CombatConstants.PinKneeCount, hits, "押さえつけ中に膝蹴りが 5 発入る");
            Assert.Less(_player.State.Hp, hpBefore - 10f, "膝蹴り 5 発ぶん HP が減る");

            // 時間切れの投げ（4.2 秒）より前に撃ち終わっている
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(CombatConstants.PinKneeCount, hits, "制限時間内に 5 発で収まる");
        }

        [UnityTest]
        public IEnumerator 拘束は別の敵へ引き渡せる()
        {
            yield return Load();
            _grapple.Begin(_captor, GrappleKind.Back);

            int started = 0;
            _grapple.Started += _ => started++;

            float before = _grapple.RequiredInputs;
            _grapple.HandOff(_other, GrappleKind.Back);
            yield return null;

            Assert.IsTrue(_grapple.IsGrappled, "引き渡しても掴まれたまま");
            Assert.AreEqual(_other, _grapple.Captor, "掴んでいる相手が入れ替わる");
            Assert.AreEqual(1, started, "掴み直しとして Started が飛ぶ");
            Assert.AreEqual(before, _grapple.RequiredInputs, 0.01f, "必要入力は同じ");
        }

        [UnityTest]
        public IEnumerator 押さえつけ中は他の敵が殴らない()
        {
            yield return Load();

            // もう 1 体を正面 0.75m に置いて、殴れる間合いに入れておく
            _other.transform.position = _player.transform.position + Vector3.right * 0.75f;
            _other.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _other.enabled = true;

            _grapple.Begin(_captor, GrappleKind.Pin);

            float end = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < end)
            {
                Assert.AreNotEqual(EnemyPhase.Windup, _other.Phase, "押さえつけ中に殴りかかってきた");
                Assert.AreNotEqual(EnemyPhase.Attack, _other.Phase, "押さえつけ中に殴ってきた");
                yield return null;
            }
        }
    }
}
