using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// ひるみ（ダウンゲージ）の数値。ここが変わるとダウンの頻度が変わるので固定する。
    /// </summary>
    public class FlinchTests
    {
        static CombatState NewCombatant() => new CombatState();

        [Test]
        public void 攻撃のひるみ値は種類ごとに決まっている()
        {
            Assert.AreEqual(12f, AttackLibrary.Light().Flinch, 0.001f);
            Assert.AreEqual(26f, AttackLibrary.Heavy().Flinch, 0.001f);
            Assert.AreEqual(34f, AttackLibrary.BreakAttack().Flinch, 0.001f);
            Assert.AreEqual(6f, AttackLibrary.GrappleAttack().Flinch, 0.001f);
        }

        [Test]
        public void その場で組んだ攻撃も種類からひるみ値が出る()
        {
            // 押さえつけの膝のように AttackData を直に組む経路でも 0 にならない
            var knee = new AttackData { id = "pin_knee", kind = AttackKind.Light };
            Assert.AreEqual(12f, knee.Flinch, 0.001f);

            // 明示すればそちらが優先される
            knee.flinch = 8f;
            Assert.AreEqual(8f, knee.Flinch, 0.001f);
        }

        [Test]
        public void 軽い攻撃は九発でダウンする()
        {
            var defender = NewCombatant();
            var attack = AttackLibrary.Light();

            int hits = 0;
            while (hits < 20 && !defender.AddFlinch(attack.Flinch))
                hits++;

            // 100 / 12 = 8.33 → 9 発目で閾値に届く
            Assert.AreEqual(8, hits, "8 発目までは倒れない");
            Assert.AreEqual(CombatConstants.MaxFlinch, defender.Flinch, 0.001f);
        }

        [Test]
        public void 重い攻撃は四発でダウンする()
        {
            var defender = NewCombatant();
            var attack = AttackLibrary.Heavy();

            int hits = 0;
            while (hits < 20 && !defender.AddFlinch(attack.Flinch))
                hits++;

            Assert.AreEqual(3, hits, "4 発目で倒れる");
        }

        [Test]
        public void ひるみは最後の被弾からしばらく減らない()
        {
            var c = NewCombatant();
            c.AddFlinch(50f);

            // 待ち時間（1.5 秒）の間は減らない
            c.Tick(CombatConstants.FlinchDecayDelay - 0.1f);
            Assert.AreEqual(50f, c.Flinch, 0.001f);

            // 過ぎたら 10/秒で抜ける
            c.Tick(1f);
            Assert.AreEqual(50f - CombatConstants.FlinchDecayPerSecond, c.Flinch, 0.01f);
        }

        [Test]
        public void 殴られている間はひるみが減らない()
        {
            var c = NewCombatant();

            // 0.5 秒おきに 5 発（毎回待ち時間をリセット）→ 合計 60
            for (int i = 0; i < 5; i++)
            {
                c.AddFlinch(12f);
                c.Tick(0.5f);
            }

            Assert.AreEqual(60f, c.Flinch, 0.001f, "殴られ続けている間は抜けない");
        }

        [Test]
        public void ダウンでひるみはゼロに戻る()
        {
            var c = NewCombatant();
            c.AddFlinch(CombatConstants.MaxFlinch);

            c.ResetFlinch();

            Assert.AreEqual(0f, c.Flinch, 0.001f);
        }

        [Test]
        public void ダウン時間は倒れと起き上がりの合計と揃っている()
        {
            // 倒れ 1.2 秒 + 起き上がり 0.8 秒 = 2.0 秒
            Assert.AreEqual(CombatConstants.DownDuration,
                CombatConstants.StunSeconds(StaggerTier.GuardBreak) + 0.80f, 0.001f);
        }
    }
}
