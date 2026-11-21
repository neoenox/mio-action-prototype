using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    public sealed class ArenaPlanTests
    {
        [Test]
        public void アリーナ中心は開始地点から等間隔に並ぶ()
        {
            var plan = ArenaPlan.Default;
            Assert.AreEqual(-6f, plan.CenterOf(0));
            Assert.AreEqual(4f, plan.CenterOf(1));
            Assert.AreEqual(14f, plan.CenterOf(2));
            Assert.AreEqual(24f, plan.CenterOf(3));
            Assert.AreEqual(34f, plan.CenterOf(4));
        }

        [Test]
        public void 波はアリーナに二つずつ割り当てられる()
        {
            var plan = ArenaPlan.Default;
            Assert.AreEqual(0, plan.ArenaOf(1));
            Assert.AreEqual(0, plan.ArenaOf(2));
            Assert.AreEqual(1, plan.ArenaOf(3));
            Assert.AreEqual(4, plan.ArenaOf(10));
        }

        [Test]
        public void 無限モードでは最後のアリーナに留まる()
        {
            var plan = ArenaPlan.Default;
            Assert.AreEqual(4, plan.ArenaOf(11));
            Assert.IsTrue(plan.IsLastArenaOf(10));
            Assert.IsTrue(plan.IsLastArenaOf(99));
            Assert.IsFalse(plan.IsLastArenaOf(8));
        }

        [Test]
        public void 入場位置はアリーナ右端の外()
        {
            var plan = ArenaPlan.Default;
            Assert.AreEqual(34f + 2.29f + 1f, plan.EntryX(4, 2.29f, 1f), 0.001f);
        }
    }
}
