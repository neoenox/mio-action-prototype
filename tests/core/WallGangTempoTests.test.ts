import { describe, expect, it } from 'vitest';
import { CombatConstants } from '../../src/core/index.js';

const last = CombatConstants.WallGangStrikes - 1;

describe('WallGangTempoTests', () => {
  it('段階は序盤と中盤と締めの3つに分かれる', () => {
    expect(CombatConstants.wallGangStage(0), '1 発目は序盤').toBe(0);
    expect(CombatConstants.wallGangStage(CombatConstants.WallGangOpeningHits - 1), '序盤の最後').toBe(0);

    expect(CombatConstants.wallGangStage(CombatConstants.WallGangOpeningHits), '序盤の次は中盤').toBe(1);
    expect(CombatConstants.wallGangStage(last - 1), '最後の 1 発の前までが中盤').toBe(1);

    expect(CombatConstants.wallGangStage(last), '最後の 1 発が締め').toBe(2);
    expect(CombatConstants.WallGangOpeningHits, '序盤が短すぎる').toBeGreaterThanOrEqual(3);
    expect(CombatConstants.WallGangOpeningHits, '序盤だけで全部を占めている').toBeLessThan(
      CombatConstants.WallGangStrikes,
    );
  });

  it('振りかぶりは締めへ向けて溜まる', () => {
    expect(CombatConstants.wallGangWindup(0), '締めの振りかぶりが序盤より短い').toBeLessThan(
      CombatConstants.wallGangWindup(last),
    );
    expect(CombatConstants.wallGangWindup(0)).toBeLessThanOrEqual(
      CombatConstants.wallGangWindup(CombatConstants.WallGangOpeningHits),
    );
    expect(CombatConstants.wallGangWindup(CombatConstants.WallGangOpeningHits)).toBeLessThanOrEqual(
      CombatConstants.wallGangWindup(last),
    );
  });

  it('間隔は締めへ向けて伸びる', () => {
    expect(CombatConstants.wallGangInterval(0), '締めの間隔が序盤より短い').toBeLessThan(
      CombatConstants.wallGangInterval(last),
    );
  });

  it('序盤の拳は以前の固定テンポより速い', () => {
    const before = 0.16 + 0.5;
    const opening = CombatConstants.wallGangWindup(0) + CombatConstants.wallGangInterval(0);

    expect(opening).toBeLessThan(before);
  });

  it('脚の一撃は拳より長くかかる', () => {
    for (let i = 0; i < CombatConstants.WallGangStrikes; i++) {
      expect(CombatConstants.wallGangLegWindup(i), `${i + 1} 発目: 脚の振りかぶりが拳以下`).toBeGreaterThan(
        CombatConstants.wallGangWindup(i),
      );
      expect(CombatConstants.wallGangLegInterval(i), `${i + 1} 発目: 脚の間隔が拳以下`).toBeGreaterThan(
        CombatConstants.wallGangInterval(i),
      );
    }
  });

  it('締めの脚は以前の脚より重い', () => {
    const before = 0.45 + 0.85;
    const finisher = CombatConstants.wallGangLegWindup(last) + CombatConstants.wallGangLegInterval(last);

    expect(finisher).toBeGreaterThanOrEqual(before);
  });

  it('威力の倍率は段階ごとに上がる', () => {
    const opening = CombatConstants.wallGangImpactScale(0);
    const build = CombatConstants.wallGangImpactScale(CombatConstants.WallGangOpeningHits);
    const finisher = CombatConstants.wallGangImpactScale(last);

    expect(build, '中盤が序盤より軽い').toBeGreaterThan(opening);
    expect(finisher, '締めが中盤より軽い').toBeGreaterThan(build);
  });
});
