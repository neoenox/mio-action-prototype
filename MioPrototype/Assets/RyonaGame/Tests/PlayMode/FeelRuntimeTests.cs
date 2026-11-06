using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 手触り系（ヒットストップ / カメラ揺れ / 敵の被弾リアクション）の回帰テスト。
    /// 特に <see cref="HitStop"/> は Time.timeScale を触るので、
    /// 「必ず戻る」ことをゲームが壊れる前に担保しておく。
    /// </summary>
    public sealed class FeelRuntimeTests
    {
        [TearDown]
        public void TearDown()
        {
            // テストが落ちてもゲーム全体の時計を戻す
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator ヒットストップは連打しても必ず戻る()
        {
            Time.timeScale = 1f;

            for (int i = 0; i < 20; i++)
                HitStop.Play(0.02f);

            Assert.IsTrue(HitStop.IsActive, "ヒットストップが始まっていない");
            Assert.AreEqual(0f, Time.timeScale, 1e-4f, "時間が止まっていない");

            float elapsed = 0f;
            while (HitStop.IsActive && elapsed < 3f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(HitStop.IsActive, "ヒットストップが終わらない");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f,
                $"timeScale が戻っていない: {Time.timeScale}");

            // 止めたあとも普通に時間が進む
            float before = Time.time;
            yield return null;
            yield return null;
            Assert.Greater(Time.time, before, "時間が進まない");
        }

        [UnityTest]
        public IEnumerator シーンに手触りと敵リアクションが組み込まれている()
        {
            yield return TestScene.LoadPrototype();

            var feel = Object.FindFirstObjectByType<CombatFeel>();
            Assert.IsNotNull(feel, "CombatFeel がシーンに無い");

            var reaction = Object.FindFirstObjectByType<EnemyReaction>();
            Assert.IsNotNull(reaction, "EnemyReaction が敵に付いていない");
            Assert.IsNotNull(reaction.Brain, "EnemyReaction に EnemyBrain が繋がっていない");

            var shake = Object.FindFirstObjectByType<CameraShake>();
            Assert.IsNotNull(shake, "カメラに CameraShake が付いていない");
        }

        [UnityTest]
        public IEnumerator 重い被弾に金属音を使わない()
        {
            yield return TestScene.LoadPrototype();
            var feel = Object.FindFirstObjectByType<CombatFeel>();
            Assert.IsNotNull(feel);
            var played = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (message.StartsWith("[Sfx] play ")) played.Add(message);
            };
            Application.logMessageReceived += capture;
            ProceduralSfx.LogPlays = true;
            try
            {
                var method = typeof(CombatFeel).GetMethod("OnHitReceived", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                method.Invoke(feel, new object[] { new HitResult(10f, StaggerTier.Large, false) });
                Assert.IsTrue(played.Exists(message => message.StartsWith("[Sfx] play Impact ")));
                Assert.IsFalse(played.Exists(message => message.StartsWith("[Sfx] play Pipe ")));
            }
            finally
            {
                ProceduralSfx.LogPlays = false;
                Application.logMessageReceived -= capture;
            }
            // 次のテストにヒットストップの残り時間を持ち越さない。
            float deadline = Time.realtimeSinceStartup + 2f;
            while (HitStop.IsActive && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(HitStop.IsActive);
        }

        [UnityTest]
        public IEnumerator 被弾するとカメラが揺れて時間が止まる()
        {
            yield return TestScene.LoadPrototype();

            var stats = Object.FindFirstObjectByType<PlayerStats>();
            var shake = Object.FindFirstObjectByType<CameraShake>();
            var feel = Object.FindFirstObjectByType<CombatFeel>();
            Assert.IsNotNull(stats);
            Assert.IsNotNull(shake);
            Assert.IsNotNull(feel);

            // ヒットストップは既定 0.05 秒。1 フレームがそれより長い環境
            // （シェーダコンパイル直後など）では、再生した直後の 1 Tick で
            // 使い切ってしまい「止まっていない」と誤判定する。
            // この確認だけ観測できる長さに延ばす。
            float saved = feel.ReceivedHitStop;
            feel.ReceivedHitStop = 0.5f;

            try
            {
                stats.IsBlocking = false;
                stats.ReceiveHit(stats.State, new AttackData
                {
                    kind = AttackKind.Light,
                    damage = 6f,
                    targetPart = BodyPart.Torso
                });

                Assert.IsTrue(shake.IsShaking, "被弾したのにカメラが揺れていない");
                Assert.IsTrue(HitStop.IsActive, "被弾したのにヒットストップが無い");
                Assert.AreEqual(0f, Time.timeScale, 1e-4f, "ヒットストップ中なのに時間が進んでいる");
            }
            finally
            {
                feel.ReceivedHitStop = saved;
            }

            // 揺れとヒットストップが自然に収束する
            float elapsed = 0f;
            while (elapsed < 2f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsFalse(HitStop.IsActive, "ヒットストップが残っている");
            Assert.AreEqual(1f, Time.timeScale, 1e-4f, "timeScale が戻っていない");
        }
    }
}
