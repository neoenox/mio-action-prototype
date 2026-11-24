import { describe, expect, it } from 'vitest';
import { PinballRules, PinballTiming } from '../../src/core/PinballRules.js';

describe('PinballRulesTests', () => {
  it('往復ごとに距離と速さが増える', () => {
    const rules = PinballRules.Default;
    expect(rules.distanceOf(0)).toBeLessThan(rules.distanceOf(1));
    expect(rules.distanceOf(1)).toBeLessThan(rules.distanceOf(2));
    expect(rules.speedOf(0)).toBeLessThan(rules.speedOf(2));
  });

  it('受け止め窓は往復ごとに狭まる', () => {
    const rules = PinballRules.Default;
    expect(rules.halfWindowOf(0)).toBeGreaterThan(rules.halfWindowOf(1));
    expect(rules.halfWindowOf(1)).toBeGreaterThan(rules.halfWindowOf(2));
  });

  it('窓の内外と早遅を判定する', () => {
    const rules = PinballRules.Default;
    expect(rules.judge(1.05, 1, 0)).toBe(PinballTiming.InWindow);
    expect(rules.judge(0.8, 1, 0)).toBe(PinballTiming.TooEarly);
    expect(rules.judge(1.2, 1, 0)).toBe(PinballTiming.TooLate);
    expect(rules.judge(1, 1, 3)).toBe(PinballTiming.RoundClosed);
  });

  it('最後の往復は締めとみなす', () => {
    const rules = PinballRules.Default;
    expect(rules.isFinal(2)).toBe(true);
    expect(rules.isFinal(1)).toBe(false);
  });
});
