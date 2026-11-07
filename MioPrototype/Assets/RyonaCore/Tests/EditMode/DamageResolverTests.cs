using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// 仕様書 SPEC.md §4 の挙動を固定する。ここが通らない限りゲームには進まない。
    /// </summary>
    public class DamageResolverTests
    {
        static CombatState NewCombatant() => new CombatState();

        // --- 基本 -----------------------------------------------------

        [Test]
        public void 無傷の弱攻撃は基礎ダメージを与える()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();
            var attack = AttackLibrary.Light();

            var result = DamageResolver.Resolve(attacker, attack, defender);

            Assert.AreEqual(8f, result.Damage, 0.001f);
            Assert.AreEqual(92f, defender.Hp, 0.001f);
            Assert.AreEqual(StaggerTier.Small, result.Stagger);
        }

        // --- 部位ダメージの影響 ---------------------------------------

        [Test]
        public void 腕が限界まで傷むと与ダメージが七五パーセントになる()
        {
            var attacker = NewCombatant();
            attacker.Body.Apply(BodyPart.Arms, 100f);
            var defender = NewCombatant();

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Light(), defender);

            Assert.AreEqual(6f, result.Damage, 0.001f);
        }

        [Test]
        public void 脚が限界まで傷むと移動速度が七十パーセントになる()
        {
            var c = NewCombatant();
            c.Body.Apply(BodyPart.Legs, 100f);

            Assert.AreEqual(0.70f, c.MoveSpeedMultiplier, 0.001f);
        }

        [Test]
        public void 部位ダメージは百で頭打ちになる()
        {
            var c = NewCombatant();
            c.Body.Apply(BodyPart.Legs, 60f);
            c.Body.Apply(BodyPart.Legs, 60f);

            Assert.AreEqual(100f, c.Body[BodyPart.Legs], 0.001f);
        }

        // --- 怯み -----------------------------------------------------

        [Test]
        public void 怯み時間は仕様どおり()
        {
            Assert.AreEqual(0.20f, CombatConstants.StunSeconds(StaggerTier.Small), 0.001f);
            Assert.AreEqual(0.40f, CombatConstants.StunSeconds(StaggerTier.Medium), 0.001f);
            Assert.AreEqual(0.80f, CombatConstants.StunSeconds(StaggerTier.Large), 0.001f);
        }

        // --- 難易度まわり（2026-10 の調整で追加） ------------------------

        [Test]
        public void 個体ごとの最大HPを変えられる()
        {
            var c = NewCombatant();
            Assert.AreEqual(CombatConstants.MaxHp, c.Hp, 0.001f);

            c.SetMaxHp(48f);
            Assert.AreEqual(48f, c.Hp, 0.001f);
            Assert.AreEqual(48f, c.MaxHp, 0.001f);

            // 超過ダメージは 0 で止まる（最大 HP を超えて減らない）
            c.ApplyHpDamage(100f);
            Assert.AreEqual(0f, c.Hp, 0.001f);

            // 復帰は個体の最大 HP に戻す（100 に戻らない）
            c.RestoreAll();
            Assert.AreEqual(48f, c.Hp, 0.001f);
        }

        [Test]
        public void ダメージ倍率を掛けられる()
        {
            var attacker = NewCombatant();
            var full = NewCombatant();
            var damped = NewCombatant();
            var attack = AttackLibrary.Light();

            float startHp = damped.Hp;

            var a = DamageResolver.Resolve(attacker, attack, full);
            var b = DamageResolver.Resolve(attacker, attack, damped, 0.5f);

            Assert.AreEqual(a.Damage * 0.5f, b.Damage, 0.001f);
            Assert.AreEqual(startHp - b.Damage, damped.Hp, 0.001f);
        }
    }

    /// <summary>
    /// 仕様書 §4.3 の抵抗システム。
    /// </summary>
    public class GrappleResistanceTests
    {
        [Test]
        public void 背面の掴みは十回でもがけば振りほどける()
        {
            var r = new GrappleResistance(
                GrappleKind.Back,
                ResistanceMath.RequiredInputs(GrappleKind.Back));

            bool escaped = false;
            for (int i = 0; i < 9; i++)
                escaped = r.RegisterInput();

            Assert.IsFalse(escaped);

            escaped = r.RegisterInput();

            Assert.IsTrue(escaped);
            Assert.IsTrue(r.IsEscaped);
        }

        [Test]
        public void 腕が傷むと振りほどくのに多く入力が要る()
        {
            float healthy = ResistanceMath.RequiredInputs(GrappleKind.Back, 1f, 0f);
            float injured = ResistanceMath.RequiredInputs(GrappleKind.Back, 1f, 100f);

            Assert.AreEqual(10f, healthy, 0.001f);
            Assert.AreEqual(12.5f, injured, 0.001f);
            Assert.Greater(injured, healthy);
        }

        [Test]
        public void 抵抗強化スキルで必要入力が二割減る()
        {
            float normal = ResistanceMath.RequiredInputs(GrappleKind.Back, 1f, 0f);
            float upgraded = ResistanceMath.RequiredInputs(GrappleKind.Back, 0.8f, 0f);

            Assert.AreEqual(10f, normal, 0.001f);
            Assert.AreEqual(8f, upgraded, 0.001f);
        }

        [Test]
        public void 押さえつけは最も重い()
        {
            Assert.Greater(
                ResistanceMath.RequiredInputs(GrappleKind.Pin),
                ResistanceMath.RequiredInputs(GrappleKind.Back));
        }
    }
}
