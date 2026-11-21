using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    public sealed class EnemyRoleRulesTests
    {
        static readonly RoleCandidate[] Candidates =
        {
            new RoleCandidate("Enemy 1", 1.72f),
            new RoleCandidate("Enemy 2", 1.79f),
            new RoleCandidate("Enemy 3", 1.85f)
        };

        [Test]
        public void 背丈で掴み役打撃役道具役が決まる()
        {
            Assert.AreEqual(EnemyRole.Grappler, EnemyRoleRules.RoleOf(Candidates, 2));
            Assert.AreEqual(EnemyRole.Tools, EnemyRoleRules.RoleOf(Candidates, 1));
            Assert.AreEqual(EnemyRole.Striker, EnemyRoleRules.RoleOf(Candidates, 0));
        }

        [Test]
        public void 列挙順を変えても同じ役割になる()
        {
            var shuffled = new[] { Candidates[2], Candidates[0], Candidates[1] };
            Assert.AreEqual(EnemyRole.Grappler, EnemyRoleRules.RoleOf(shuffled, 0));
            Assert.AreEqual(EnemyRole.Striker, EnemyRoleRules.RoleOf(shuffled, 1));
            Assert.AreEqual(EnemyRole.Tools, EnemyRoleRules.RoleOf(shuffled, 2));
        }

        [Test]
        public void シーンで明示した掴み役を優先する()
        {
            var authored = new[]
            {
                new RoleCandidate("Enemy 1", 1.72f, true),
                Candidates[1], Candidates[2]
            };
            Assert.AreEqual(EnemyRole.Grappler, EnemyRoleRules.RoleOf(authored, 0));
            Assert.AreEqual(EnemyRole.Striker, EnemyRoleRules.RoleOf(authored, 2));
        }
    }
}
