import { describe, expect, it } from 'vitest';
import {
  HumanJointLimits,
  JointId,
  JointLimit,
  JointRom,
  JointRomReading,
  JointRomTally,
  JointRomVerdict,
  JointSide,
} from '../../src/core/index.js';

describe('JointRomTests', () => {
  it('定義済みの関節はすべてテーブルにある', () => {
    for (const joint of Object.values(JointId).filter((v): v is number => typeof v === 'number')) {
      expect(HumanJointLimits.isKnown(joint), `${joint} が可動域テーブルに無い`).toBe(true);

      const limit: JointLimit = HumanJointLimits.of(joint);
      expect(limit.id, 'テーブルの並びと JointId がずれている').toBe(joint);
      expect(limit.label.length, `${joint} の表示名が空`).toBeGreaterThan(0);
      expect(limit.min, `${joint} の可動域が逆転している`).toBeLessThan(limit.max);
      expect(limit.slackLow, `${joint} の下限の余裕が負`).toBeGreaterThanOrEqual(0);
      expect(limit.slackHigh, `${joint} の上限の余裕が負`).toBeGreaterThanOrEqual(0);
    }
  });

  it('主要な関節が人の可動域になっている', () => {
    expect(HumanJointLimits.of(JointId.Elbow).min).toBeCloseTo(0, 2);
    expect(HumanJointLimits.of(JointId.Elbow).max).toBeCloseTo(145, 2);
    expect(HumanJointLimits.of(JointId.Knee).max).toBeCloseTo(150, 2);
    expect(HumanJointLimits.of(JointId.ShoulderFlexion).max).toBeGreaterThanOrEqual(170);
    expect(HumanJointLimits.of(JointId.NeckYaw).max).toBeGreaterThanOrEqual(70);
    expect(HumanJointLimits.of(JointId.SpinePitch).max).toBeGreaterThan(
      HumanJointLimits.of(JointId.NeckPitch).max,
    );
  });

  it('未定義の関節を問い合わせると例外', () => {
    expect(() => HumanJointLimits.of(99 as JointId)).toThrow();
  });

  it('正常な角度は逸脱にならない', () => {
    const reading = JointRom.read(JointId.Elbow, JointSide.Right, 90);

    expect(reading.isViolation).toBe(false);
    expect(reading.verdict).toBe(JointRomVerdict.Within);
    expect(reading.excess).toBeCloseTo(0, 3);
    expect(reading.degrees).toBeCloseTo(90, 2);
    expect(reading.side).toBe(JointSide.Right);
  });

  it('可動域を超えた角度は曲げすぎになる', () => {
    const limit = HumanJointLimits.of(JointId.Knee);
    const reading = JointRom.read(JointId.Knee, JointSide.Left, limit.warnAbove + 5);

    expect(reading.isViolation).toBe(true);
    expect(reading.verdict).toBe(JointRomVerdict.OverFlexed);
    expect(reading.excess).toBeCloseTo(5, 2);
    expect(reading.describe()).toContain('ひざ');
    expect(reading.describe()).toContain('左');
    expect(reading.describe()).toContain('曲げすぎ');
  });

  it('逆方向へ曲がった角度は逆方向になる', () => {
    const limit = HumanJointLimits.of(JointId.Elbow);
    const reading = JointRom.read(JointId.Elbow, JointSide.Right, limit.warnBelow - 8);

    expect(reading.isViolation).toBe(true);
    expect(reading.verdict).toBe(JointRomVerdict.ReverseBend);
    expect(reading.excess).toBeCloseTo(8, 2);
    expect(reading.describe()).toContain('逆方向');
  });

  it('余裕の内側はまだ警告しない', () => {
    const limit = HumanJointLimits.of(JointId.Elbow);

    const inside = JointRom.read(
      JointId.Elbow, JointSide.Left, limit.max + limit.slackHigh - 1,
    );
    expect(inside.isViolation).toBe(false);

    const outside = JointRom.read(JointId.Elbow, JointSide.Left, limit.warnAbove + 1);
    expect(outside.isViolation).toBe(true);
  });

  it('集計は関節ごとの最悪値と回数を持つ', () => {
    const tally = new JointRomTally();

    tally.add(JointRom.read(JointId.Knee, JointSide.Left, 150));
    tally.add(JointRom.read(JointId.Knee, JointSide.Left, 180));
    tally.add(JointRom.read(JointId.Knee, JointSide.Left, 170));
    tally.add(JointRom.read(JointId.Elbow, JointSide.Right, -40));

    expect(tally.violationSamples).toBe(3);
    expect(tally.trackedKeys).toBe(2);
    expect(tally.countOf(JointId.Knee, JointSide.Left)).toBe(2);
    expect(tally.countOf(JointId.Elbow, JointSide.Right)).toBe(1);

    const worst = tally.tryGetWorst(JointId.Knee, JointSide.Left) as JointRomReading;
    expect(worst.degrees).toBeCloseTo(180, 2);

    expect(tally.worstOverall).toBeDefined();
    expect(tally.worstOverall!.degrees).toBeCloseTo(180, 2);

    tally.clear();
    expect(tally.violationSamples).toBe(0);
    expect(tally.trackedKeys).toBe(0);
    expect(tally.worstOverall).toBeUndefined();
  });
});
