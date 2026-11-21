using NUnit.Framework;
using Ryona.Game;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>
    /// 三人がかりの打撃枠（2 枠）が満杯のとき、既存の打撃を壊さないことを固定する。
    /// </summary>
    public sealed class GrappleSessionTests
    {
        [Test]
        public void 打撃枠が満杯ならNullを返し既存の枠を壊さない()
        {
            var session = new GrappleSession();
            var a = new GameObject("striker-a").AddComponent<EnemyBrain>();
            var b = new GameObject("striker-b").AddComponent<EnemyBrain>();
            var c = new GameObject("striker-c").AddComponent<EnemyBrain>();

            try
            {
                var slotA = session.ClaimStrike(a);
                var slotB = session.ClaimStrike(b);
                Assert.IsNotNull(slotA);
                Assert.IsNotNull(slotB);
                Assert.AreNotSame(slotA, slotB, "同時に打てるのは 2 枠まで");
                slotA.Age = 0.2f;

                Assert.IsNull(session.ClaimStrike(c), "満杯なら 0 枠を使い回さない");
                Assert.AreSame(a, slotA.Puncher, "既存の枠の持ち主が変わらない");
                Assert.AreEqual(0.2f, slotA.Age, 0.001f, "経過秒がリセットされない");

                session.ClearStrikes();
                Assert.IsNotNull(session.ClaimStrike(c), "空けば次の打撃が入る");
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }
    }
}
