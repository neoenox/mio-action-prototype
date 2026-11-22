using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>シネマティックショットが終わるときのカメラの戻りを固定する。</summary>
    public sealed class CameraExitBlendTests
    {
        [UnityTest]
        public IEnumerator 演出終了は1フレームで飛ばずに戻る()
        {
            yield return TestScene.LoadPrototype();

            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            Assert.IsNotNull(cam);

            cam.RequestCinematicShot(new Vector3(24f, 2.2f, 4f), Vector3.zero);
            yield return null;
            yield return null;

            Vector3 shotPosition = cam.transform.position;
            Assert.Greater(Vector3.Distance(shotPosition, Vector3.zero), 5f, "演出ショットが反映される");

            // 以降はショット要求を出さない。出口ブレンドが入ると 1 フレームで飛ばない。
            yield return null;
            float firstStep = Vector3.Distance(shotPosition, cam.transform.position);
            Debug.Log($"[CameraExit] firstStep={firstStep:F3}m");
            Assert.Less(firstStep, 0.6f, $"演出終了の 1 フレーム目に飛ぶ（{firstStep:F2}m）");
        }
        [UnityTest]
        public IEnumerator 死亡中は拘束の寄りがカメラの引きを消さない()
        {
            yield return TestScene.LoadPrototype();

            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            var stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(cam);
            Assert.IsNotNull(stats);

            var lethal = AttackLibrary.Light();
            lethal.damage = stats.State.MaxHp * 10f;
            stats.ReceiveHit(new CombatState(), lethal, stats.transform);
            Assert.IsTrue(stats.IsDead, "致命傷で死亡していない");

            // 死亡カメラが引きを効かせるまで待つ（死亡演出は 0.5 秒かけて引く）。
            float deadline = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < deadline && cam.EffectDistanceScale <= 1.15f)
                yield return null;
            float pullBefore = cam.EffectDistanceScale;
            Assert.Greater(pullBefore, 1.15f, "死亡の引きが効いていない");

            // 拘束ポーズが毎フレーム HoldFocus を呼んでも、死亡の引きは残ること。
            for (int i = 0; i < 6; i++)
            {
                cam.HoldFocus(0.4f, 0f, cam.Target.position.x, 0.5f);
                cam.PushDistance(0.4f, 0.5f);
                yield return null;
            }
            Assert.Greater(cam.EffectDistanceScale, Mathf.Max(1.05f, pullBefore * 0.9f),
                $"拘束の寄りが死亡カメラの引きを消した（{pullBefore:F2} → {cam.EffectDistanceScale:F2}）");
        }

    }
}
