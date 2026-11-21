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
    }
}
