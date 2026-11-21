using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>挟み撃ちの突き飛ばし（ピンポン）の回帰テスト。</summary>
    public sealed class PinballRuntimeTests
    {
        PinballDirector _director;
        PlayerStats _stats;
        EnemyBrain[] _enemies;
        CharacterAnimator _anim;

        IEnumerator Setup()
        {
            yield return TestScene.LoadPrototype();
            RyonaConfig.Instance.Pinball.Chance = 0f;
            _director = Object.FindFirstObjectByType<PinballDirector>();
            _stats = Object.FindFirstObjectByType<PlayerStats>();
            _anim = _stats != null ? _stats.GetComponentInChildren<CharacterAnimator>() : null;
            Assert.IsNotNull(_director);
            Assert.IsNotNull(_stats);
            _enemies = Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(_enemies.Length, 2);
            for (int i = 2; i < _enemies.Length; i++)
                _enemies[i].enabled = false;
            _enemies[0].transform.position = _stats.transform.position + Vector3.right * 1.4f;
            _enemies[1].transform.position = _stats.transform.position - Vector3.right * 1.4f;
            _enemies[0].LockForHandOff(0.1f);
            _enemies[1].LockForHandOff(0.1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 三往復のあと腹でうつ伏せにダウンする()
        {
            yield return Setup();
            _director.ForceStart();
            Assert.IsTrue(_director.IsActive, "ピンポンが始まらない");

            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            int faceFrames = 0, faceGood = 0;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!_stats.IsDown && Time.realtimeSinceStartup < deadline)
            {
                if (_anim == null || _anim.Rig == null)
                    _anim = Object.FindFirstObjectByType<CharacterAnimator>();
                if (_anim == null || _anim.Rig == null)
                {
                    yield return null;
                    continue;
                }
                var face = FaceVisibility.Measure(Camera.main, _anim.Rig.Head, figures);
                faceFrames++;
                if (face.Visible >= FaceVisibility.GoodVisible)
                    faceGood++;
                yield return null;
            }
            Assert.IsTrue(_stats.IsDown, "最後の腹パンでダウンしない");
            Assert.IsTrue(_stats.DownIsForward, "ピンポンの締めはうつ伏せ");
            Assert.IsFalse(_director.IsActive, "演出が終わっていない");
            if (faceFrames > 5)
                // 全場面共通の下限 0.75（Readable ではなく Visible）。
                Assert.GreaterOrEqual(faceGood / (float)faceFrames, 0.75f,
                    $"往復中に顔が読めない ({(faceGood / (float)faceFrames):F2})");
        }

        [UnityTest]
        public IEnumerator 窓の中で踏ん張ると抜け窓の外の連打では抜けない()
        {
            yield return Setup();
            _director.ForceStart();
            Assert.IsTrue(_director.IsActive);

            Assert.AreEqual(PinballTiming.TooEarly, _director.Brace());
            Assert.IsTrue(_director.IsActive, "窓の外の入力で抜けてしまった");

            float firstCatch = _director.NextCatchAt;
            float deadline = Time.time + 4f;
            while (_director.NextCatchAt <= firstCatch + 0.01f && Time.time < deadline)
                yield return null;
            deadline = Time.time + 2f;
            while (Time.time < _director.NextCatchAt && Time.time < deadline)
                yield return null;
            Assert.AreEqual(PinballTiming.InWindow, _director.Brace());
            Assert.IsFalse(_director.IsActive, "窓の中の入力で抜けない");
        }

        /// <summary>
        /// 顔トドメの回避は命中の直前に拘束を一瞬解く。その隙にピンポンが始まると、退いた拘束役を
        /// もう一度掴ませ、トドメが届かず顔パンの連撃が並行して走った（実測、顔面パンチ 5 発のテストが 180 秒で打ち切り）。
        /// 拘束中と、解けた直後の猶予の間は始まらないことを固定する。
        /// </summary>
        [UnityTest]
        public IEnumerator 拘束中と解けた直後はピンポンが始まらない()
        {
            yield return Setup();
            var grapple = _stats.GetComponent<PlayerGrapple>();
            Assert.IsNotNull(grapple);

            Assert.IsFalse(_director.BlockedByOtherSequence, "何も起きていないのに止められている");

            grapple.Begin(_enemies[1], GrappleKind.Back);
            yield return null;
            Assert.IsTrue(grapple.IsGrappled);
            Assert.IsTrue(_director.BlockedByOtherSequence, "拘束中に始まり得る");

            grapple.Release();
            yield return null;
            Assert.IsFalse(grapple.IsGrappled);
            Assert.IsTrue(_director.BlockedByOtherSequence, "拘束が解けた直後に始まり得る（顔トドメの回避の隙）");

            yield return new WaitForSeconds(1.7f);
            Assert.IsFalse(_director.BlockedByOtherSequence, "猶予が明けても始まらない");
        }

        /// <summary>ピンポンの背後の受け止め（Back）で、タッグ腹パンの連撃が始まらない。</summary>
        [UnityTest]
        public IEnumerator ピンポン中はタッグ腹パンの連撃が始まらない()
        {
            yield return Setup();
            var held = Object.FindFirstObjectByType<HeldBellyPunchDirector>();
            Assert.IsNotNull(held);

            _director.ForceStart();
            Assert.IsTrue(_director.IsActive);

            float deadline = Time.realtimeSinceStartup + 6f;
            while (_director.IsActive && Time.realtimeSinceStartup < deadline)
            {
                Assert.AreEqual(HeldBellyPunchDirector.Phase.Idle, held.CurrentPhase,
                    "ピンポンの最中にタッグ腹パンの演出が動いている");
                yield return null;
            }
        }
    }
}
