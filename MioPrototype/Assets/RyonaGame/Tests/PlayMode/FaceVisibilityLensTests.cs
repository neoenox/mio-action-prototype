using System.Collections;
using NUnit.Framework;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>レンズ前の敵の体を測る LensClearance の回帰テスト。</summary>
    public sealed class FaceVisibilityLensTests
    {
        [UnityTest]
        public IEnumerator レンズ前の敵は近く遠くは大きく測れる()
        {
            yield return TestScene.LoadPrototype();

            var figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            Assert.Greater(figures.Length, 0, "敵がいない");

            Vector3 inside = figures[0].transform.position + Vector3.up;
            float near = FaceVisibility.LensClearance(inside, figures);
            float far = FaceVisibility.LensClearance(new Vector3(100f, 100f, 100f), figures);

            Debug.Log($"[LensClearance] near={near:F2} far={far:F2}");
            Assert.Less(near, 0.5f, "敵の体の中ではクリアランスが小さい");
            Assert.Greater(far, 10f, "遠くではクリアランスが大きい");
        }
    }
}
