import { describe, expect, it } from 'vitest';
import { AttackData, AttackKind, AttackLibrary, CombatConstants, CombatState, StaggerTier } from '../../src/core/index.js';

function newCombatant(): CombatState {
  return new CombatState();
}

describe('FlinchTests', () => {
  it('攻撃のひるみ値は種類ごとに決まっている', () => {
    expect(AttackLibrary.light().flinchValue).toBeCloseTo(12, 3);
    expect(AttackLibrary.heavy().flinchValue).toBeCloseTo(26, 3);
    expect(AttackLibrary.breakAttack().flinchValue).toBeCloseTo(34, 3);
    expect(AttackLibrary.grappleAttack().flinchValue).toBeCloseTo(6, 3);
  });

  it('その場で組んだ攻撃も種類からひるみ値が出る', () => {
    const knee = new AttackData();
    knee.id = 'pin_knee';
    knee.kind = AttackKind.Light;
    expect(knee.flinchValue).toBeCloseTo(12, 3);

    knee.flinch = 8;
    expect(knee.flinchValue).toBeCloseTo(8, 3);
  });

  it('軽い攻撃は九発でダウンする', () => {
    const defender = newCombatant();
    const attack = AttackLibrary.light();

    let hits = 0;
    while (hits < 20 && !defender.addFlinch(attack.flinchValue)) hits++;

    // 100 / 12 = 8.33 → 9 発目で閾値に届く
    expect(hits).toBe(8);
    expect(defender.flinch).toBeCloseTo(CombatConstants.MaxFlinch, 3);
  });

  it('重い攻撃は四発でダウンする', () => {
    const defender = newCombatant();
    const attack = AttackLibrary.heavy();

    let hits = 0;
    while (hits < 20 && !defender.addFlinch(attack.flinchValue)) hits++;

    expect(hits).toBe(3);
  });

  it('ひるみは最後の被弾からしばらく減らない', () => {
    const c = newCombatant();
    c.addFlinch(50);

    c.tick(CombatConstants.FlinchDecayDelay - 0.1);
    expect(c.flinch).toBeCloseTo(50, 3);

    c.tick(1);
    expect(c.flinch).toBeCloseTo(50 - CombatConstants.FlinchDecayPerSecond, 2);
  });

  it('殴られている間はひるみが減らない', () => {
    const c = newCombatant();

    for (let i = 0; i < 5; i++) {
      c.addFlinch(12);
      c.tick(0.5);
    }

    expect(c.flinch).toBeCloseTo(60, 3);
  });

  it('ダウンでひるみはゼロに戻る', () => {
    const c = newCombatant();
    c.addFlinch(CombatConstants.MaxFlinch);

    c.resetFlinch();

    expect(c.flinch).toBeCloseTo(0, 3);
  });

  it('ダウン時間は倒れと起き上がりの合計と揃っている', () => {
    // 倒れ 1.2 秒 + 起き上がり 0.8 秒 = 2.0 秒
    expect(CombatConstants.DownDuration).toBeCloseTo(
      CombatConstants.stunSeconds(StaggerTier.GuardBreak) + 0.8, 3,
    );
  });
});
