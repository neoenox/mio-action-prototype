using NUnit.Framework;
using Ryona.Game;

namespace Ryona.Tests
{
    /// <summary>
    /// 決めの全身ショットで第三の敵がレンズ前に入ってもフェードされることを固定する。
    /// </summary>
    public sealed class OcclusionFadeTests
    {
        [Test]
        public void 彼女より奥でもカメラから近い敵はフェード対象()
        {
            Assert.IsTrue(OcclusionFade.NeedsCloseBodyFade(1.8f, 1.4f),
                "彼女より 0.4m 奥の敵は体が画面を覆う");
            Assert.IsFalse(OcclusionFade.NeedsCloseBodyFade(3.1f, 1.4f),
                "十分に離れた敵はそのまま出す");
            Assert.IsFalse(OcclusionFade.NeedsCloseBodyFade(1.0f, 1.4f),
                "彼女より手前は既存の near 判定に任せる");
        }
    }
}
