using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 体の重なり監視の実機テスト。
    /// カプセル距離や許容余裕の計算そのものは <c>Ryona.Tests.BodyOverlapTests</c>（EditMode）で見ている。
    /// </summary>
    public sealed class BodyOverlapRuntimeTests
    {
        CharacterAnimator _anim;
        ProceduralRig _rig;
        PlayerStats _stats;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            TestScene.RestoreAudio();
        }

        IEnumerator LoadPlayer()
        {
            yield return TestScene.LoadPrototype();

            _stats = TestScene.FindPlayer();

            yield return TestRig.WaitReady(_stats, 12f);
            _anim = TestRig.RequireReady(_stats);
            _rig = _anim.Rig;
        }

        static EnemyFigure[] SeparateAllFromPlayer(Transform playerRoot)
        {
            EnemyFigure[] enemies = Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] == null)
                    continue;
                // 互いに重ならないよう十分離して並べる。
                enemies[i].transform.position = playerRoot.position + new Vector3((i + 1) * 3f, 0f, 3f);
            }
            return enemies;
        }

        [UnityTest]
        public IEnumerator 重なり監視が自動で起動しプレイヤーと敵を計測できる()
        {
            yield return LoadPlayer();

            Assert.IsTrue(BodyOverlapWatch.Enabled, "Editor では重なり監視が既定で有効なはず");
            Assert.IsNotNull(BodyOverlapWatch.Instance, "BodyOverlapWatch.Instance が作られていない");

            BodyOverlapWatch watch = BodyOverlapWatch.Instance;
            EnemyFigure[] enemies = Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
            Assert.Greater(enemies.Length, 0, "敵がシーンにいない");

            Assert.IsTrue(watch.SampleNow(false), "プレイヤーと敵の重なりを計測できない");
            Assert.GreaterOrEqual(watch.ActorCount, 2, "計測対象のキャラクターが 2 人未満");
        }

        [UnityTest]
        public IEnumerator 十分に離れていれば重なりの警告を出さない()
        {
            yield return LoadPlayer();

            var go = new GameObject("Test Body Overlap Watch");
            BodyOverlapWatch watch = go.AddComponent<BodyOverlapWatch>();
            watch.LogWarnings = false;

            SeparateAllFromPlayer(_stats.transform);
            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);

            watch.ResetReport();
            Assert.IsTrue(watch.SampleNow(false), "離れた状態を計測できない");
            Assert.AreEqual(0, watch.Tally.ViolationSamples,
                $"離れた状態なのに重なり超過が出ている（{watch.Tally.WorstOverall?.Describe()}）");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator プレイヤーと敵が重なると警告を出し集計される()
        {
            yield return LoadPlayer();

            var go = new GameObject("Test Body Overlap Watch");
            BodyOverlapWatch watch = go.AddComponent<BodyOverlapWatch>();
            watch.LogWarnings = true;
            watch.WarnInterval = 0.1f;

            EnemyFigure[] enemies = SeparateAllFromPlayer(_stats.transform);
            Assert.Greater(enemies.Length, 0, "敵がシーンにいない");

            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);

            // 敵 1 体をプレイヤーと全く同じ位置に重ねる。
            enemies[0].transform.position = _stats.transform.position;

            watch.ResetReport();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[BodyOverlap\]"));
            Assert.IsTrue(watch.SampleNow(true), "重なりを計測できない");

            Assert.Greater(watch.WarningCount, 0, "重なり警告が出ていない");
            Assert.Greater(watch.Tally.TrackedPairs, 0, "重なりが集計されていない");
            Assert.IsTrue(watch.Tally.WorstOverall.HasValue, "最悪値が記録されていない");
            Assert.Greater(watch.Tally.WorstOverall.Value.ExcessMeters, 0f, "超過量が正でない");

            watch.LogSummaryNow();
            Assert.IsNotNull(watch.LastSummary, "まとめログが作られていない");
            StringAssert.Contains("超過", watch.LastSummary);

            Object.Destroy(go);
        }
    }
}
