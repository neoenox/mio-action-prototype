using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// 3 人がかり（壁拘束の最後）のテンポ設計のテスト。
    ///
    /// 以前は 15 発すべて同じ間隔（0.50 秒固定）で、同じ 3 連が 5 回繰り返すだけだった。
    /// 序盤は速く軽く、中盤で標準、締めは溜めて重く、という段階を持たせている。
    /// ここでは「段階が付いていること」と「以前より緩急が付いたこと」を固定する。
    /// </summary>
    public sealed class WallGangTempoTests
    {
        const int Last = CombatConstants.WallGangStrikes - 1;

        [Test]
        public void 段階は序盤と中盤と締めの3つに分かれる()
        {
            Assert.AreEqual(0, CombatConstants.WallGangStage(0), "1 発目は序盤");
            Assert.AreEqual(0, CombatConstants.WallGangStage(CombatConstants.WallGangOpeningHits - 1), "序盤の最後");

            Assert.AreEqual(1, CombatConstants.WallGangStage(CombatConstants.WallGangOpeningHits), "序盤の次は中盤");
            Assert.AreEqual(1, CombatConstants.WallGangStage(Last - 1), "最後の 1 発の前までが中盤");

            Assert.AreEqual(2, CombatConstants.WallGangStage(Last), "最後の 1 発が締め");
            Assert.GreaterOrEqual(CombatConstants.WallGangOpeningHits, 3, "序盤が短すぎる");
            Assert.Less(CombatConstants.WallGangOpeningHits, CombatConstants.WallGangStrikes,
                "序盤だけで全部を占めている");
        }

        [Test]
        public void 振りかぶりは締めへ向けて溜まる()
        {
            Assert.Less(CombatConstants.WallGangWindup(0), CombatConstants.WallGangWindup(Last),
                "締めの振りかぶりが序盤より短い（溜まっていない）");
            Assert.LessOrEqual(CombatConstants.WallGangWindup(0), CombatConstants.WallGangWindup(CombatConstants.WallGangOpeningHits));
            Assert.LessOrEqual(CombatConstants.WallGangWindup(CombatConstants.WallGangOpeningHits), CombatConstants.WallGangWindup(Last));
        }

        [Test]
        public void 間隔は締めへ向けて伸びる()
        {
            Assert.Less(CombatConstants.WallGangInterval(0), CombatConstants.WallGangInterval(Last),
                "締めの間隔が序盤より短い（緩急が逆）");
        }

        [Test]
        public void 序盤の拳は以前の固定テンポより速い()
        {
            // 以前: 振りかぶり 0.16 + 間隔 0.50 = 0.66 秒。
            float before = 0.16f + 0.50f;
            float opening = CombatConstants.WallGangWindup(0) + CombatConstants.WallGangInterval(0);

            Assert.Less(opening, before, $"序盤が速くなっていない（{opening:0.00} 秒）");
        }

        [Test]
        public void 脚の一撃は拳より長くかかる()
        {
            for (int i = 0; i < CombatConstants.WallGangStrikes; i++)
            {
                Assert.Greater(CombatConstants.WallGangLegWindup(i), CombatConstants.WallGangWindup(i),
                    $"{i + 1} 発目: 脚の振りかぶりが拳以下（足が上がりきらない）");
                Assert.Greater(CombatConstants.WallGangLegInterval(i), CombatConstants.WallGangInterval(i),
                    $"{i + 1} 発目: 脚の間隔が拳以下（脚が戻りきらない）");
            }
        }

        [Test]
        public void 締めの脚は以前の脚より重い()
        {
            // 以前の脚: 振りかぶり 0.45 + 間隔 0.85 = 1.30 秒。
            float before = 0.45f + 0.85f;
            float finisher = CombatConstants.WallGangLegWindup(Last) + CombatConstants.WallGangLegInterval(Last);

            Assert.GreaterOrEqual(finisher, before, $"締めの脚が以前より軽くなっている（{finisher:0.00} 秒）");
        }

        [Test]
        public void 威力の倍率は段階ごとに上がる()
        {
            float opening = CombatConstants.WallGangImpactScale(0);
            float build = CombatConstants.WallGangImpactScale(CombatConstants.WallGangOpeningHits);
            float finisher = CombatConstants.WallGangImpactScale(Last);

            Assert.Greater(build, opening, "中盤が序盤より軽い");
            Assert.Greater(finisher, build, "締めが中盤より軽い");
        }
    }
}
