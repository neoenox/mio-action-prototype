import { describe, expect, it } from 'vitest';
import { EnemyRole, EnemyRoleRules, RoleCandidate } from '../../src/core/EnemyRole.js';

const candidates: readonly RoleCandidate[] = [
  new RoleCandidate('Enemy 1', 1.72),
  new RoleCandidate('Enemy 2', 1.79),
  new RoleCandidate('Enemy 3', 1.85),
];

describe('EnemyRoleRulesTests', () => {
  it('背丈で掴み役打撃役道具役が決まる', () => {
    expect(EnemyRoleRules.roleOf(candidates, 2)).toBe(EnemyRole.Grappler);
    expect(EnemyRoleRules.roleOf(candidates, 1)).toBe(EnemyRole.Tools);
    expect(EnemyRoleRules.roleOf(candidates, 0)).toBe(EnemyRole.Striker);
  });

  it('列挙順を変えても同じ役割になる', () => {
    const shuffled = [candidates[2]!, candidates[0]!, candidates[1]!];
    expect(EnemyRoleRules.roleOf(shuffled, 0)).toBe(EnemyRole.Grappler);
    expect(EnemyRoleRules.roleOf(shuffled, 1)).toBe(EnemyRole.Striker);
    expect(EnemyRoleRules.roleOf(shuffled, 2)).toBe(EnemyRole.Tools);
  });

  it('シーンで明示した掴み役を優先する', () => {
    const authored = [
      new RoleCandidate('Enemy 1', 1.72, true),
      candidates[1]!,
      candidates[2]!,
    ];
    expect(EnemyRoleRules.roleOf(authored, 0)).toBe(EnemyRole.Grappler);
    expect(EnemyRoleRules.roleOf(authored, 2)).toBe(EnemyRole.Striker);
  });
});
