using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// 押さえつけ担当の決定則を固定する。列挙順に依存しないことが要件。
    /// </summary>
    public class PinOwnerRulesTests
    {
        static PinCandidate Enemy(string name, float height, bool canPin = false)
            => new PinCandidate(name, height, canPin);

        [Test]
        public void CanPinが無ければ一番背の高い個体を選ぶ()
        {
            var candidates = new[]
            {
                Enemy("Enemy 1", 1.72f),
                Enemy("Enemy 2", 1.79f),
                Enemy("Enemy 3", 1.85f),
            };

            Assert.AreEqual(2, PinOwnerRules.Pick(candidates));
        }

        [Test]
        public void CanPinが立つ個体を背丈より優先する()
        {
            var candidates = new[]
            {
                Enemy("Tall", 1.85f),
                Enemy("Short", 1.60f, canPin: true),
            };

            Assert.AreEqual(1, PinOwnerRules.Pick(candidates));
        }

        [Test]
        public void CanPinが複数ならその中で一番背の高い個体を選ぶ()
        {
            var candidates = new[]
            {
                Enemy("A", 1.72f, canPin: true),
                Enemy("B", 1.85f, canPin: true),
                Enemy("C", 1.90f),
            };

            Assert.AreEqual(1, PinOwnerRules.Pick(candidates));
        }

        [Test]
        public void 背丈が同じなら名前の序数順で決める()
        {
            var candidates = new[]
            {
                Enemy("Enemy 2", 1.80f),
                Enemy("Enemy 10", 1.80f),
            };

            // 序数比較では '1' < '2' なので "Enemy 10" が先。
            Assert.AreEqual(1, PinOwnerRules.Pick(candidates));
        }

        [Test]
        public void 背丈の差が許容内なら名前で決める()
        {
            var candidates = new[]
            {
                Enemy("B", 1.80005f),
                Enemy("A", 1.8f),
            };

            Assert.AreEqual(1, PinOwnerRules.Pick(candidates));
        }

        [Test]
        public void 候補が空なら担当なし()
        {
            Assert.AreEqual(-1, PinOwnerRules.Pick(new PinCandidate[0]));
            Assert.AreEqual(-1, PinOwnerRules.Pick(null));
        }
    }
}
