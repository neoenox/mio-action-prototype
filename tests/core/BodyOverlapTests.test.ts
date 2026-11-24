import { describe, expect, it } from 'vitest';
import {
  BodyCapsule,
  BodyOverlap,
  BodyOverlapReading,
  BodyOverlapTally,
  BodyPartSpecs,
  BodyVec3,
  OverlapPart,
} from '../../src/core/index.js';

describe('BodyOverlapTests', () => {
  it('定義済みの部位はすべて名前と半径がある', () => {
    for (const part of Object.values(OverlapPart).filter((v): v is number => typeof v === 'number')) {
      const label = BodyPartSpecs.labelOf(part);
      const radius = BodyPartSpecs.radiusForHeight(part, 1.6);
      expect(label.length, `${part} の表示名が空`).toBeGreaterThan(0);
      expect(radius, `${part} の半径が小さすぎる`).toBeGreaterThan(0.01);
    }
  });

  it('平行な線分の最短距離を正しく測る', () => {
    const a = new BodyVec3(0, 0, 0);
    const b = new BodyVec3(0, 1, 0);
    const c = new BodyVec3(0.3, 0.2, 0);
    const d = new BodyVec3(0.3, 1.2, 0);

    const dist = BodyOverlap.segmentSegmentDistance(a, b, c, d);
    expect(dist).toBeCloseTo(0.3, 4);
  });

  it('ねじれの位置の線分同士の最短距離を正しく測る', () => {
    const a = new BodyVec3(-1, 0, 0);
    const b = new BodyVec3(1, 0, 0);
    const c = new BodyVec3(0, 0.25, -1);
    const d = new BodyVec3(0, 0.25, 1);

    const dist = BodyOverlap.segmentSegmentDistance(a, b, c, d);
    expect(dist).toBeCloseTo(0.25, 4);
  });

  it('長さゼロの点カプセル同士でも距離を測れる', () => {
    const p1 = new BodyVec3(0, 0, 0);
    const p2 = new BodyVec3(0.3, 0.4, 0);

    const dist = BodyOverlap.segmentSegmentDistance(p1, p1, p2, p2);
    expect(dist).toBeCloseTo(0.5, 4);
  });

  it('離れていれば重なり違反にならない', () => {
    const capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0, 0, 0), new BodyVec3(0, 0.4, 0), 0.1);
    const capB = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.5, 0, 0), new BodyVec3(0.5, 0.4, 0), 0.1);

    const reading = BodyOverlap.measure('プレイヤー', capA, '敵1', capB);
    expect(reading.isViolation).toBe(false);
    expect(reading.penetrationMeters).toBeLessThan(0);
    expect(reading.excessMeters).toBe(0);
  });

  it('許容範囲内のわずかな接触は警告にしない', () => {
    const capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0, 0, 0), new BodyVec3(0, 0.4, 0), 0.1);
    const capB = new BodyCapsule(OverlapPart.ForearmR, new BodyVec3(0.17, 0, 0), new BodyVec3(0.17, 0.4, 0), 0.1);

    const reading = BodyOverlap.measure('プレイヤー', capA, '敵1', capB, 0.04);
    expect(reading.penetrationMeters).toBeCloseTo(0.03, 4);
    expect(reading.isViolation).toBe(false);
    expect(reading.excessMeters).toBe(0);
  });

  it('許容を超えるめり込みは違反として超過量を出す', () => {
    const capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0, 0, 0), new BodyVec3(0, 0.4, 0), 0.1);
    const capB = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.09, 0, 0), new BodyVec3(0.09, 0.4, 0), 0.1);

    const reading = BodyOverlap.measure('プレイヤー', capA, '敵1', capB, 0.04);
    expect(reading.isViolation).toBe(true);
    expect(reading.penetrationMeters).toBeCloseTo(0.11, 4);
    expect(reading.excessMeters).toBeCloseTo(0.07, 4);
    expect(reading.describe()).toContain('プレイヤーの胴');
    expect(reading.describe()).toContain('敵1の胴');
  });

  it('集計器はペアごとの最悪値と回数を記録する', () => {
    const tally = new BodyOverlapTally();
    const capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0, 0, 0), new BodyVec3(0, 0.4, 0), 0.1);
    const capFar = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.5, 0, 0), new BodyVec3(0.5, 0.4, 0), 0.1);
    const capNear = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.1, 0, 0), new BodyVec3(0.1, 0.4, 0), 0.1);
    const capDeep = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.02, 0, 0), new BodyVec3(0.02, 0.4, 0), 0.1);

    tally.add(BodyOverlap.measure('プレイヤー', capA, '敵1', capFar));
    expect(tally.violationSamples).toBe(0);

    const mild: BodyOverlapReading = BodyOverlap.measure('プレイヤー', capA, '敵1', capNear);
    const deep: BodyOverlapReading = BodyOverlap.measure('プレイヤー', capA, '敵1', capDeep);
    tally.add(mild);
    tally.add(deep);

    expect(tally.violationSamples).toBe(2);
    expect(tally.trackedPairs).toBe(1);
    expect(tally.countOf(mild)).toBe(2);
    expect(tally.worstOverall).toBeDefined();
    expect(tally.worstOverall!.excessMeters).toBeCloseTo(deep.excessMeters, 4);

    tally.clear();
    expect(tally.violationSamples).toBe(0);
    expect(tally.trackedPairs).toBe(0);
  });
});
