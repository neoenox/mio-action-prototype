using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    public sealed class WristLockRulesTests
    {
        [Test]
        public void 八回で割れる四秒で切れる()
        {
            var rules = WristLockRules.Default;
            Assert.IsFalse(rules.IsBroken(7));
            Assert.IsTrue(rules.IsBroken(8));
            Assert.IsFalse(rules.IsExpired(3.9f));
            Assert.IsTrue(rules.IsExpired(4f));
        }

        [Test]
        public void 掴みとダウン中は付けない()
        {
            var rules = WristLockRules.Default;
            Assert.IsTrue(rules.CanAttach(10f, 0f, false, false));
            Assert.IsFalse(rules.CanAttach(10f, 0f, true, false));
            Assert.IsFalse(rules.CanAttach(10f, 0f, false, true));
        }

        [Test]
        public void 解除後の猶予とクールタイム()
        {
            var rules = WristLockRules.Default;
            Assert.IsTrue(rules.IsInGrace(10f, 9f));
            Assert.IsFalse(rules.IsInGrace(12f, 9f));
            Assert.IsFalse(rules.CanAttach(10f, 9f, false, false));
            Assert.IsTrue(rules.CanAttach(40f, 9f, false, false));
        }

        [Test]
        public void 掴まれたときの必要回数が増える()
        {
            var rules = WristLockRules.Default;
            Assert.Greater(rules.ScaledEscapeNeed(10f), 10f);
        }
    }
}
