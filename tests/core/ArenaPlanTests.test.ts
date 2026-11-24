import { describe, expect, it } from 'vitest';
import { ArenaPlan } from '../../src/core/ArenaPlan.js';

describe('ArenaPlanTests', () => {
  it('アリーナ中心は開始地点から等間隔に並ぶ', () => {
    const plan = ArenaPlan.Default;
    expect(plan.centerOf(0)).toBe(-6);
    expect(plan.centerOf(1)).toBe(4);
    expect(plan.centerOf(2)).toBe(14);
    expect(plan.centerOf(3)).toBe(24);
    expect(plan.centerOf(4)).toBe(34);
  });

  it('波はアリーナに二つずつ割り当てられる', () => {
    const plan = ArenaPlan.Default;
    expect(plan.arenaOf(1)).toBe(0);
    expect(plan.arenaOf(2)).toBe(0);
    expect(plan.arenaOf(3)).toBe(1);
    expect(plan.arenaOf(10)).toBe(4);
  });

  it('無限モードでは最後のアリーナに留まる', () => {
    const plan = ArenaPlan.Default;
    expect(plan.arenaOf(11)).toBe(4);
    expect(plan.isLastArenaOf(10)).toBe(true);
    expect(plan.isLastArenaOf(99)).toBe(true);
    expect(plan.isLastArenaOf(8)).toBe(false);
  });

  it('入場位置はアリーナ右端の外', () => {
    const plan = ArenaPlan.Default;
    expect(plan.entryX(4, 2.29, 1)).toBeCloseTo(34 + 2.29 + 1, 3);
  });
});
