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

            var result = DamageResolver.Resolve(attacker, attack, defender, false);

            Assert.AreEqual(8f, result.Damage, 0.001f);
            Assert.AreEqual(92f, defender.Hp, 0.001f);
            Assert.AreEqual(88f, defender.Posture, 0.001f);
            Assert.AreEqual(StaggerTier.Small, result.Stagger);
            Assert.IsFalse(result.Blocked);
        }

        // --- 部位ダメージの影響 ---------------------------------------

        [Test]
        public void 腕が限界まで傷むと与ダメージが七五パーセントになる()
        {
            var attacker = NewCombatant();
            attacker.Body.Apply(BodyPart.Arms, 100f);
            var defender = NewCombatant();

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Light(), defender, false);

            Assert.AreEqual(6f, result.Damage, 0.001f);
        }

        [Test]
        public void 腕が傷むとガードが削られやすくなる()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();
            defender.Body.Apply(BodyPart.Arms, 100f);

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Light(), defender, true);

            // 基礎 10 / ガード耐久 0.5 = 20
            Assert.AreEqual(20f, result.GuardStaminaDamage, 0.001f);
            Assert.AreEqual(80f, defender.Stamina, 0.001f);
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

        // --- ガード ---------------------------------------------------

        [Test]
        public void ガードするとダメージは四分の一だが体勢は削られる()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Light(), defender, true);

            Assert.IsTrue(result.Blocked);
            Assert.AreEqual(2f, result.Damage, 0.001f);
            Assert.AreEqual(18f, result.PostureDamage, 0.001f);
            Assert.AreEqual(98f, defender.Hp, 0.001f);
        }

        [Test]
        public void 崩し攻撃はガードを無視する()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();

            var result = DamageResolver.Resolve(attacker, AttackLibrary.BreakAttack(), defender, true);

            Assert.IsFalse(result.Blocked);
            Assert.AreEqual(14f, result.Damage, 0.001f);
            Assert.AreEqual(86f, defender.Hp, 0.001f);
        }

        // --- 怯み -----------------------------------------------------

        [Test]
        public void 体勢がゼロになると大怯みになり体勢が半分戻る()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();
            defender.SetPosture(20f);

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Heavy(), defender, false);

            Assert.IsTrue(result.PostureBroken);
            Assert.AreEqual(StaggerTier.Large, result.Stagger);
            Assert.AreEqual(50f, defender.Posture, 0.001f);
        }

        [Test]
        public void スタミナ切れの被弾はガードブレイクになる()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();
            defender.ConsumeStamina(95f); // 残り 5

            var result = DamageResolver.Resolve(attacker, AttackLibrary.Light(), defender, true);

            Assert.IsTrue(result.GuardBroken);
            Assert.AreEqual(StaggerTier.GuardBreak, result.Stagger);
            Assert.AreEqual(0f, defender.Stamina, 0.001f);
        }

        [Test]
        public void 怯み時間は仕様どおり()
        {
            Assert.AreEqual(0.20f, CombatConstants.StunSeconds(StaggerTier.Small), 0.001f);
            Assert.AreEqual(0.40f, CombatConstants.StunSeconds(StaggerTier.Medium), 0.001f);
            Assert.AreEqual(0.80f, CombatConstants.StunSeconds(StaggerTier.Large), 0.001f);
            Assert.AreEqual(1.20f, CombatConstants.StunSeconds(StaggerTier.GuardBreak), 0.001f);
        }

        // --- 回復 -----------------------------------------------------

        [Test]
        public void 胴が傷むとスタミナ回復が鈍る()
        {
            var c = NewCombatant();
            c.Body.Apply(BodyPart.Torso, 100f);
            c.ConsumeStamina(100f);

            c.Tick(1f, moving: false);

            // StaminaRegenIdle * TorsoRegenFloor
            Assert.AreEqual(
                CombatConstants.StaminaRegenIdle * CombatConstants.TorsoRegenFloor,
                c.Stamina, 0.001f);
        }

        // --- ガード ---------------------------------------------------

        [Test]
        public void ガード中はスタミナ回復が鈍る()
        {
            var guarding = NewCombatant();
            var idle = NewCombatant();
            guarding.ConsumeStamina(60f);
            idle.ConsumeStamina(60f);

            for (int i = 0; i < 10; i++)
            {
                guarding.Tick(1f, moving: false, blocking: true);
                idle.Tick(1f, moving: false);
            }

            Assert.Less(guarding.Stamina, idle.Stamina - 20f,
                "構えている間も素早く回復すると、握り得になってガードブレイクが起きない");
        }

        [Test]
        public void ガードを続けるとガードブレイクでダウンする()
        {
            var attacker = NewCombatant();
            var defender = NewCombatant();
            var attack = AttackLibrary.Light();

            StaggerTier last = StaggerTier.None;
            for (int i = 0; i < 20 && last != StaggerTier.GuardBreak; i++)
                last = DamageResolver.Resolve(attacker, attack, defender, true).Stagger;

            Assert.AreEqual(StaggerTier.GuardBreak, last,
                "ガードし続けてもブレイクしないと、ダウン・立ち上がりの出番が無い");
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

            var a = DamageResolver.Resolve(attacker, attack, full, false);
            var b = DamageResolver.Resolve(attacker, attack, damped, false, 0.5f);

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
        public void 正面の掴みは八回でもがけば振りほどける()
        {
            var r = new GrappleResistance(
                GrappleKind.Front,
                ResistanceMath.RequiredInputs(GrappleKind.Front));

            bool escaped = false;
            for (int i = 0; i < 7; i++)
                escaped = r.RegisterInput();

            Assert.IsFalse(escaped);

            escaped = r.RegisterInput();

            Assert.IsTrue(escaped);
            Assert.IsTrue(r.IsEscaped);
        }

        [Test]
        public void 腕が傷むと振りほどくのに多く入力が要る()
        {
            float healthy = ResistanceMath.RequiredInputs(GrappleKind.Front, 1f, 0f);
            float injured = ResistanceMath.RequiredInputs(GrappleKind.Front, 1f, 100f);

            Assert.AreEqual(8f, healthy, 0.001f);
            Assert.AreEqual(10f, injured, 0.001f);
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
            Assert.Greater(
                ResistanceMath.RequiredInputs(GrappleKind.Back),
                ResistanceMath.RequiredInputs(GrappleKind.Front));
        }
    }
}
