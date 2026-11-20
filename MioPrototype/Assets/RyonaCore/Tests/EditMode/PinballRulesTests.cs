using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    public sealed class PinballRulesTests
    {
        [Test]
        public void 往復ごとに距離と速さが増える()
        {
            var rules = PinballRules.Default;
            Assert.Less(rules.DistanceOf(0), rules.DistanceOf(1));
            Assert.Less(rules.DistanceOf(1), rules.DistanceOf(2));
            Assert.Less(rules.SpeedOf(0), rules.SpeedOf(2));
        }

        [Test]
        public void 受け止め窓は往復ごとに狭まる()
        {
            var rules = PinballRules.Default;
            Assert.Greater(rules.HalfWindowOf(0), rules.HalfWindowOf(1));
            Assert.Greater(rules.HalfWindowOf(1), rules.HalfWindowOf(2));
        }

        [Test]
        public void 窓の内外と早遅を判定する()
        {
            var rules = PinballRules.Default;
            Assert.AreEqual(PinballTiming.InWindow, rules.Judge(1.05f, 1f, 0));
            Assert.AreEqual(PinballTiming.TooEarly, rules.Judge(0.80f, 1f, 0));
            Assert.AreEqual(PinballTiming.TooLate, rules.Judge(1.20f, 1f, 0));
            Assert.AreEqual(PinballTiming.RoundClosed, rules.Judge(1f, 1f, 3));
        }

        [Test]
        public void 最後の往復は締めとみなす()
        {
            var rules = PinballRules.Default;
            Assert.IsTrue(rules.IsFinal(2));
            Assert.IsFalse(rules.IsFinal(1));
        }
    }
}
