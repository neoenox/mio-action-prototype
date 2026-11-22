using System.Collections;
using NUnit.Framework;
using Ryona;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>手首ロック連動の引き寄せ（輪を掴んで引き込み、正面拘束の膝へ）の回帰テスト。</summary>
    public sealed class WristPullRuntimeTests
    {
        PlayerStats _stats;
        PlayerGrapple _grapple;
        PlayerWristLock _lock;
        WristPullDirector _director;
        CharacterAnimator _anim;
        EnemyBrain _tools;
        EnemyBrain _puller;

        /// <summary>道具役 1 体と引き寄せ役 1 体だけを残し、引き寄せ役を彼女の正面 1.9m に置く。</summary>
        IEnumerator Setup()
        {
            yield return TestScene.LoadPrototype();
            _stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(_stats);
            _grapple = _stats.GetComponent<PlayerGrapple>();
            _lock = _stats.GetComponent<PlayerWristLock>();
            _director = Object.FindFirstObjectByType<WristPullDirector>();
            _anim = _stats.GetComponentInChildren<CharacterAnimator>();
            Assert.IsNotNull(_grapple);
            Assert.IsNotNull(_lock);
            Assert.IsNotNull(_director, "WristPullDirector が起動していない");

            foreach (var e in Object.FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (_tools == null && e.Role == EnemyRole.Tools)
                    _tools = e;
                else if (_puller == null && e.Role != EnemyRole.Tools)
                    _puller = e;
                else
                    e.enabled = false;
            }
            Assert.IsNotNull(_tools, "道具役がいない");
            Assert.IsNotNull(_puller, "引き寄せ役（道具役以外）がいない");

            Vector3 her = _stats.transform.position;
            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            _puller.transform.SetPositionAndRotation(her + forward * 1.9f, Quaternion.LookRotation(-forward, Vector3.up));
            _tools.transform.position = her - forward * 3.5f;
            _tools.LockForHandOff(20f);
            _puller.HoldFire(20f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 輪が付くと正面の敵が遠くから引き寄せて膝の拘束に入る()
        {
            yield return Setup();
            Vector3 pullerStart = _puller.transform.position;
            float startGap = Vector3.Distance(pullerStart, _stats.transform.position);
            Assert.IsTrue(_lock.TryAttach(_tools), "輪が付かない");

            yield return TestWait.UntilOrTimeout(() => _director.Puller == _puller, 1.0f,
                "輪が付いても引き寄せの構えに入らない");
            Assert.IsTrue(_puller.IsPreparingWristPull);

            yield return TestWait.UntilOrTimeout(() => _grapple.IsGrappled, 2.0f, "引き寄せで掴まれない");
            Assert.AreEqual(GrappleKind.Pin, _grapple.Kind, "引き寄せの後は正面拘束（膝）");
            Assert.AreSame(_puller, _grapple.Captor);
            Assert.AreEqual(1, _director.PullsLanded);
            Assert.IsFalse(_grapple.IsWallPin, "引き寄せは壁へ押し出さない");

            // 敵は踏み込まず、彼女の方が引き込まれる（踏み込むと通常の掴みと区別がつかない）。
            float pullerMoved = Vector3.Distance(pullerStart, _puller.transform.position);
            Assert.Less(pullerMoved, 0.15f, $"引き寄せ役が踏み込んでいる ({pullerMoved:F2}m)");
            Assert.Greater(startGap, CombatConstants.PinMaxDistance, "通常の押さえつけより遠くから掴む前提");

            // 引き込まれてから膝蹴りが始まるまでの間、顔が映っている（全場面共通の下限 0.75）。
            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            int frames = 0, good = 0;
            float until = Time.realtimeSinceStartup + 2.0f;
            while (_grapple.IsGrappled && Time.realtimeSinceStartup < until)
            {
                yield return null;
                if (_anim == null || _anim.Rig == null)
                    _anim = Object.FindFirstObjectByType<CharacterAnimator>();
                if (_anim == null || _anim.Rig == null || !_anim.Rig.Ready)
                    continue;
                frames++;
                if (FaceVisibility.Measure(Camera.main, _anim.Rig.Head, figures).Visible >= FaceVisibility.GoodVisible)
                    good++;
            }
            Debug.Log($"[WristPullFace] frames={frames} good={good}");
            Assert.Greater(frames, 10);
            Assert.GreaterOrEqual(good / (float)frames, 0.75f,
                $"引き込まれた後に顔が映っていない ({good}/{frames})");
        }

        [UnityTest]
        public IEnumerator 掴まれる直前に押し返すと拘束されず相手がよろける()
        {
            yield return Setup();
            Assert.IsTrue(_lock.TryAttach(_tools));
            yield return TestWait.UntilOrTimeout(() => _director.Puller == _puller, 1.0f,
                "引き寄せの構えに入らない");

            yield return TestWait.UntilOrTimeout(
                () => _puller.WristPullSecondsToStrike <= WristPullDirector.CounterWindow * 0.5f, 1.5f,
                "掴む瞬間が近づかない");
            Assert.IsTrue(_director.TryCounter(), "窓の中の入力で押し返せない");
            Assert.AreEqual(EnemyPhase.Stagger, _puller.Phase, "押し返した相手がよろけない");

            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(_grapple.IsGrappled, "押し返したのに掴まれた");
            Assert.AreEqual(1, _director.Counters);
        }

        [UnityTest]
        public IEnumerator 早すぎる入力は一回分を使い切り引き寄せられる()
        {
            yield return Setup();
            Assert.IsTrue(_lock.TryAttach(_tools));
            yield return TestWait.UntilOrTimeout(() => _director.Puller == _puller, 1.0f,
                "引き寄せの構えに入らない");

            Assert.IsFalse(_director.TryCounter(), "構えの直後の入力で押し返せてしまう");
            yield return TestWait.UntilOrTimeout(
                () => _puller.WristPullSecondsToStrike <= WristPullDirector.CounterWindow * 0.5f, 1.5f,
                "掴む瞬間が近づかない");
            Assert.IsFalse(_director.TryCounter(), "早すぎた入力のあとにもう一度押し返せる（連打で狙える）");

            yield return TestWait.UntilOrTimeout(() => _grapple.IsGrappled, 1.0f, "引き寄せられない");
            Assert.AreEqual(0, _director.Counters);
        }
    }
}
