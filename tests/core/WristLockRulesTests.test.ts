import { describe, expect, it } from 'vitest';
import { WristLockRules } from '../../src/core/WristLockRules.js';

describe('WristLockRulesTests', () => {
  it('八回で割れる四秒で切れる', () => {
    const rules = WristLockRules.Default;
    expect(rules.isBroken(7)).toBe(false);
    expect(rules.isBroken(8)).toBe(true);
    expect(rules.isExpired(3.9)).toBe(false);
    expect(rules.isExpired(4)).toBe(true);
  });

  it('掴みとダウン中は付けない', () => {
    const rules = WristLockRules.Default;
    expect(rules.canAttach(10, -1, false, false)).toBe(true);
    expect(rules.canAttach(10, -1, true, false)).toBe(false);
    expect(rules.canAttach(10, -1, false, true)).toBe(false);
  });

  it('解除後の猶予とクールタイム', () => {
    const rules = WristLockRules.Default;
    expect(rules.isInGrace(10, 9)).toBe(true);
    expect(rules.isInGrace(12, 9)).toBe(false);
    expect(rules.canAttach(10, 9, false, false)).toBe(false);
    expect(rules.canAttach(40, 9, false, false)).toBe(true);
  });

  it('掴まれたときの必要回数が増える', () => {
    const rules = WristLockRules.Default;
    expect(rules.scaledEscapeNeed(10)).toBeGreaterThan(10);
  });
});
