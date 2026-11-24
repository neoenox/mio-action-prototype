import { describe, expect, it } from 'vitest';
import { PinCandidate, PinOwnerRules } from '../../src/core/PinOwnerRules.js';

function enemy(name: string, height: number, canPin = false): PinCandidate {
  return new PinCandidate(name, height, canPin);
}

describe('PinOwnerRulesTests', () => {
  it('CanPinが無ければ一番背の高い個体を選ぶ', () => {
    const candidates = [
      enemy('Enemy 1', 1.72),
      enemy('Enemy 2', 1.79),
      enemy('Enemy 3', 1.85),
    ];
    expect(PinOwnerRules.pick(candidates)).toBe(2);
  });

  it('CanPinが立つ個体を背丈より優先する', () => {
    const candidates = [
      enemy('Tall', 1.85),
      enemy('Short', 1.6, true),
    ];
    expect(PinOwnerRules.pick(candidates)).toBe(1);
  });

  it('CanPinが複数ならその中で一番背の高い個体を選ぶ', () => {
    const candidates = [
      enemy('A', 1.72, true),
      enemy('B', 1.85, true),
      enemy('C', 1.9),
    ];
    expect(PinOwnerRules.pick(candidates)).toBe(1);
  });

  it('背丈が同じなら名前の序数順で決める', () => {
    const candidates = [
      enemy('Enemy 2', 1.8),
      enemy('Enemy 10', 1.8),
    ];
    // 序数比較では '1' < '2' なので "Enemy 10" が先。
    expect(PinOwnerRules.pick(candidates)).toBe(1);
  });

  it('背丈の差が許容内なら名前で決める', () => {
    const candidates = [
      enemy('B', 1.80005),
      enemy('A', 1.8),
    ];
    expect(PinOwnerRules.pick(candidates)).toBe(1);
  });

  it('候補が空なら担当なし', () => {
    expect(PinOwnerRules.pick([])).toBe(-1);
    expect(PinOwnerRules.pick(null)).toBe(-1);
  });
});
