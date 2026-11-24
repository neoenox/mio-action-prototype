import { describe, expect, it } from 'vitest';
import {
  AttackLibrary,
  BodyPart,
  CombatConstants,
  CombatState,
  DamageResolver,
  GrappleKind,
  GrappleResistance,
  ResistanceMath,
  StaggerTier,
} from '../../src/core/index.js';

function newCombatant(): CombatState {
  return new CombatState();
}

describe('DamageResolverTests', () => {
  it('無傷の弱攻撃は基礎ダメージを与える', () => {
    const attacker = newCombatant();
    const defender = newCombatant();
    const attack = AttackLibrary.light();

    const result = DamageResolver.resolve(attacker, attack, defender);

    expect(result.damage).toBeCloseTo(8, 3);
    expect(defender.hp).toBeCloseTo(92, 3);
    expect(result.stagger).toBe(StaggerTier.Small);
  });

  it('腕が限界まで傷むと与ダメージが七五パーセントになる', () => {
    const attacker = newCombatant();
    attacker.body.apply(BodyPart.Arms, 100);
    const defender = newCombatant();

    const result = DamageResolver.resolve(attacker, AttackLibrary.light(), defender);

    expect(result.damage).toBeCloseTo(6, 3);
  });

  it('脚が限界まで傷むと移動速度が七十パーセントになる', () => {
    const c = newCombatant();
    c.body.apply(BodyPart.Legs, 100);

    expect(c.moveSpeedMultiplier).toBeCloseTo(0.7, 3);
  });

  it('部位ダメージは百で頭打ちになる', () => {
    const c = newCombatant();
    c.body.apply(BodyPart.Legs, 60);
    c.body.apply(BodyPart.Legs, 60);

    expect(c.body.get(BodyPart.Legs)).toBeCloseTo(100, 3);
  });

  it('怯み時間は仕様どおり', () => {
    expect(CombatConstants.stunSeconds(StaggerTier.Small)).toBeCloseTo(0.2, 3);
    expect(CombatConstants.stunSeconds(StaggerTier.Medium)).toBeCloseTo(0.4, 3);
    expect(CombatConstants.stunSeconds(StaggerTier.Large)).toBeCloseTo(0.8, 3);
  });

  it('個体ごとの最大HPを変えられる', () => {
    const c = newCombatant();
    expect(c.hp).toBeCloseTo(CombatConstants.MaxHp, 3);

    c.setMaxHp(48);
    expect(c.hp).toBeCloseTo(48, 3);
    expect(c.maxHp).toBeCloseTo(48, 3);

    c.applyHpDamage(100);
    expect(c.hp).toBeCloseTo(0, 3);

    c.restoreAll();
    expect(c.hp).toBeCloseTo(48, 3);
  });

  it('ダメージ倍率を掛けられる', () => {
    const attacker = newCombatant();
    const full = newCombatant();
    const damped = newCombatant();
    const attack = AttackLibrary.light();

    const startHp = damped.hp;

    const a = DamageResolver.resolve(attacker, attack, full);
    const b = DamageResolver.resolve(attacker, attack, damped, 0.5);

    expect(b.damage).toBeCloseTo(a.damage * 0.5, 3);
    expect(damped.hp).toBeCloseTo(startHp - b.damage, 3);
  });
});

describe('GrappleResistanceTests', () => {
  it('背面の掴みは十回でもがけば振りほどける', () => {
    const r = new GrappleResistance(
      GrappleKind.Back,
      ResistanceMath.requiredInputs(GrappleKind.Back),
    );

    let escaped = false;
    for (let i = 0; i < 9; i++) escaped = r.registerInput();

    expect(escaped).toBe(false);

    escaped = r.registerInput();

    expect(escaped).toBe(true);
    expect(r.isEscaped).toBe(true);
  });

  it('腕が傷むと振りほどくのに多く入力が要る', () => {
    const healthy = ResistanceMath.requiredInputs(GrappleKind.Back, 1, 0);
    const injured = ResistanceMath.requiredInputs(GrappleKind.Back, 1, 100);

    expect(healthy).toBeCloseTo(10, 3);
    expect(injured).toBeCloseTo(12.5, 3);
    expect(injured).toBeGreaterThan(healthy);
  });

  it('抵抗強化スキルで必要入力が二割減る', () => {
    const normal = ResistanceMath.requiredInputs(GrappleKind.Back, 1, 0);
    const upgraded = ResistanceMath.requiredInputs(GrappleKind.Back, 0.8, 0);

    expect(normal).toBeCloseTo(10, 3);
    expect(upgraded).toBeCloseTo(8, 3);
  });

  it('押さえつけは最も重い', () => {
    expect(ResistanceMath.requiredInputs(GrappleKind.Pin)).toBeGreaterThan(
      ResistanceMath.requiredInputs(GrappleKind.Back),
    );
  });
});
