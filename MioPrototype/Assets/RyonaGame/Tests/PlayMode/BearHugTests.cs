using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// ベアハッグ（<see cref="GrappleKind.BearHug"/>）の回帰テスト。
    ///
    /// 正面から胴を抱えて締める。4.2 秒の制限時間つきで、12 回もがけば脱出できる。
    /// 押さえつけと違って膝は入らず、一定間隔で締め付けて HP を少し削る。
    /// </summary>
    public sealed class BearHugTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

        IEnumerator Load()
        {
            yield return TestScene.LoadPrototype();

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 1);

            _captor = enemies[0];
            for (int i = 1; i < enemies.Length; i++)
                enemies[i].enabled = false;

            // プレイヤーは +X 向き。掴む側は正面（+X）。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.right * 1.15f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.left);
            _captor.enabled = false;

            yield return null;
        }

        [UnityTest]
        public IEnumerator ベアハッグは制限時間つきで始まり相手の方を向く()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.BearHug);

            Assert.IsTrue(_grapple.IsGrappled, "ベアハッグが始まらない");
            Assert.AreEqual(GrappleKind.BearHug, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit, "ベアハッグは時間切れがある");
            Assert.AreEqual(CombatConstants.GrappleLimitSeconds, _grapple.Remaining, 0.05f);
            Assert.AreEqual(CombatConstants.MashBaseBearHug, _grapple.RequiredInputs, 0.01f,
                "必要入力は 12 回");

            Vector3 toCaptor = _captor.transform.position - _player.transform.position;
            toCaptor.y = 0f;
            Assert.Greater(Vector3.Dot(_player.transform.forward, toCaptor.normalized), 0.9f,
                "ベアハッグは正面からなので、彼女は相手を向く");
            Assert.AreEqual(0.56f, toCaptor.magnitude, 0.05f, "掴んだ距離が 0.56m になっていない");
        }

        [UnityTest]
        public IEnumerator 締め付けてHPを少し削りもがけば脱出できる()
        {
            yield return Load();

            _captor.enabled = true;
            _captor.QueueBearHug();

            float deadline = Time.realtimeSinceStartup + 6f;
            while ((_grapple.Kind != GrappleKind.BearHug || !_grapple.IsGrappled)
                && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(_grapple.IsGrappled && _grapple.Kind == GrappleKind.BearHug,
                "正面からベアハッグに入らない");
            Assert.IsTrue(_captor.IsBearHugging, "締め付けの状態になっていない");

            float hpBefore = _player.State.Hp;
            float squeezeDeadline = Time.realtimeSinceStartup + 3f;
            while (_grapple.SqueezePulse <= 0f && Time.realtimeSinceStartup < squeezeDeadline)
                yield return null;
            Assert.Greater(_grapple.SqueezePulse, 0f, "締め付けのパルスが来ない");

            // 密着 → 少し離れる → 密着 を繰り返す。距離の振れ幅と、密着時のダメージを見る。
            float minDistance = float.MaxValue;
            float maxDistance = 0f;
            bool sawContact = false;
            float observeUntil = Time.realtimeSinceStartup + 2.4f;
            while (Time.realtimeSinceStartup < observeUntil)
            {
                yield return null;
                float distance = Vector3.Distance(
                    _player.transform.position, _captor.transform.position);
                minDistance = Mathf.Min(minDistance, distance);
                maxDistance = Mathf.Max(maxDistance, distance);
                if (_grapple.IsBearHugContact)
                    sawContact = true;
            }

            Assert.IsTrue(sawContact, "密着した瞬間がない");
            Assert.Greater(maxDistance - minDistance, 0.06f,
                $"体が密着と離れを繰り返していない（{minDistance:F2}〜{maxDistance:F2}m）");
            Assert.Less(_player.State.Hp, hpBefore - 2f, "密着時のダメージが入っていない");
            Assert.Greater(_grapple.LiftPulse, 0.5f, "抱え上げられていない");
            Assert.IsFalse(_player.IsDead, "ベアハッグだけで死亡している");

            // 12 回もがけば脱出できる。
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            for (int i = 0; i < need; i++)
                _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "12 回もがけば振りほどける");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase, "振りほどかれた側はよろける");

            // 脱出後に締め付けが続かない。
            float hpAfter = _player.State.Hp;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(hpAfter, _player.State.Hp, 0.01f, "脱出後も締め付けが続いている");
            Assert.Less(_grapple.LiftPulse, 0.1f, "脱出後も抱え上げられたまま");
        }
    }
}
