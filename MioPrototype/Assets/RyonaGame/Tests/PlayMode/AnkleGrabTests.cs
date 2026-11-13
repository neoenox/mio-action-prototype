using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 足取り（<see cref="GrappleKind.AnkleGrab"/>）の回帰テスト。
    ///
    /// 走って逃げる彼女の背後から足首を掴んで転ばせる。短い制限時間で、8 回もがけば振りほどける。
    /// 振りほどけなければ前へ転ぶ（うつ伏せ）。掴みの種類は、走っているときだけ羽交い締めと抽選になる。
    /// </summary>
    public sealed class AnkleGrabTests
    {
        PlayerStats _player;
        PlayerGrapple _grapple;
        EnemyBrain _captor;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            EnemyBrain.ForcedGrabKind = null;
            if (_player != null)
                _player.IsMoving = false;
            if (_grapple != null && _grapple.IsGrappled)
                _grapple.Release();
        }

        /// <summary>シーンを読み、掴む側を彼女の背後（-X）1.0m に置く。</summary>
        IEnumerator Load()
        {
            yield return TestScene.LoadPrototype();

            EnemyBrain.ForcedGrabKind = null;

            _player = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_player, "PlayerStats がシーンに無い");

            _grapple = _player.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(_grapple, "PlayerGrapple が無い");

            var enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 1, "掴む敵が必要");

            _captor = enemies[0];
            for (int i = 1; i < enemies.Length; i++)
                enemies[i].enabled = false;

            // 彼女は +X 向きに走っている。掴む側は背後（-X）で、同じ向き。
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);
            _captor.transform.position = _player.transform.position + Vector3.left * 1.0f;
            _captor.transform.rotation = Quaternion.LookRotation(Vector3.right);

            // 掴む動きだけを検証する。敵の AI は動かさない。
            _captor.enabled = false;

            yield return null;
        }

        [UnityTest]
        public IEnumerator 足取りは短い制限時間つきで始まり背中を向けたまま掴まれる()
        {
            yield return Load();

            Vector3 facingBefore = _player.transform.forward;
            _grapple.Begin(_captor, GrappleKind.AnkleGrab);

            Assert.IsTrue(_grapple.IsGrappled, "足取りが始まらない");
            Assert.AreEqual(GrappleKind.AnkleGrab, _grapple.Kind);
            Assert.IsTrue(_grapple.HasTimeLimit, "足取りは時間切れがある");
            Assert.AreEqual(CombatConstants.AnkleGrabLimitSeconds, _grapple.Remaining, 0.05f,
                $"制限時間は {CombatConstants.AnkleGrabLimitSeconds:F1} 秒");
            Assert.AreEqual(CombatConstants.MashBaseAnkleGrab, _grapple.RequiredInputs, 0.01f,
                "必要入力は 8 回");
            Assert.AreEqual(0f, _grapple.FrontEscapeRemaining, 0.001f,
                "足取りに正面拘束の膝蹴りまでの猶予は無い");

            // 背後から掴まれるので、向きは変わらない（相手の方を向かない）。
            Assert.Greater(Vector3.Dot(_player.transform.forward, facingBefore), 0.99f,
                "足取りでは向きを変えない");

            // 低く沈んだ敵の腕が足首まで届く距離に置かれる。
            Vector3 toPlayer = _player.transform.position - _captor.transform.position;
            toPlayer.y = 0f;
            Assert.AreEqual(CombatConstants.AnkleGrabStandoff, toPlayer.magnitude, 0.05f,
                "掴んだ距離が足取りの間合いになっていない");
        }

        [UnityTest]
        public IEnumerator もがき8回で振りほどけ掴んだ側がよろけて転ばない()
        {
            yield return Load();

            _grapple.Begin(_captor, GrappleKind.AnkleGrab);
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);
            Assert.AreEqual(CombatConstants.MashBaseAnkleGrab, need);

            for (int i = 0; i < need - 1; i++)
            {
                _grapple.Struggle();
                Assert.IsTrue(_grapple.IsGrappled, $"{i + 1} 回目でもがききっていない");
            }

            _grapple.Struggle();
            yield return null;

            Assert.IsFalse(_grapple.IsGrappled, "8 回もがけば振りほどける");
            Assert.IsFalse(_player.IsDown, "振りほどけば転ばない");
            Assert.AreEqual(EnemyPhase.Stagger, _captor.Phase, "振りほどかれた側はよろける（反撃の隙）");
        }

        [UnityTest]
        public IEnumerator 振りほどけないと時間切れで前へ転ぶ()
        {
            yield return Load();

            bool failed = false;
            _grapple.Failed += () => failed = true;

            _grapple.Begin(_captor, GrappleKind.AnkleGrab);
            yield return new WaitForSeconds(CombatConstants.AnkleGrabLimitSeconds + 0.4f);

            Assert.IsTrue(failed, "時間切れで Failed が飛んでいない");
            Assert.IsFalse(_grapple.IsGrappled, "転んだら拘束は解ける");
            Assert.IsTrue(_player.IsDown, "足を取られて転ぶ");
            Assert.IsTrue(_player.DownIsForward, "前へ崩れる（うつ伏せ）");
        }

        [UnityTest]
        public IEnumerator 走っていないときの背後からの掴みは羽交い締めになる()
        {
            yield return Load();

            _player.IsMoving = false;
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(_captor.TryGrab(), "背後からの掴みが成立しない");
                Assert.AreEqual(GrappleKind.Back, _grapple.Kind, "走っていなければ羽交い締め");
                _grapple.Release();
            }
        }

        [UnityTest]
        public IEnumerator 走って逃げる彼女の背後からは足取りと羽交い締めが抽選になる()
        {
            yield return Load();

            _player.IsMoving = true;
            int ankle = 0;
            int back = 0;
            for (int i = 0; i < 60; i++)
            {
                Assert.IsTrue(_captor.TryGrab(), "背後からの掴みが成立しない");
                if (_grapple.Kind == GrappleKind.AnkleGrab) ankle++;
                else if (_grapple.Kind == GrappleKind.Back) back++;
                _grapple.Release();
                _player.IsMoving = true;
            }

            // 60 回中どちらかが 0 回になる確率は 2^-59 程度。
            Assert.Greater(ankle, 0, "走っている彼女に足取りが一度も出ない");
            Assert.Greater(back, 0, "走っている彼女に羽交い締めが一度も出ない");
        }

        [UnityTest]
        public IEnumerator 掴みの種類を固定すると足取りになる()
        {
            yield return Load();

            _player.IsMoving = false;
            EnemyBrain.ForcedGrabKind = GrappleKind.AnkleGrab;

            Assert.IsTrue(_captor.TryGrab(), "背後からの掴みが成立しない");
            Assert.AreEqual(GrappleKind.AnkleGrab, _grapple.Kind, "固定した掴みの種類になっていない");
        }
    }
}
