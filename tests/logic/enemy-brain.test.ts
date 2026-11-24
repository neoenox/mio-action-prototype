import { describe, expect, it } from 'vitest';
import { AttackId, EnemyBrain, type AttackOutcome } from '../../src/logic/enemy-brain.js';
import { EnemyPhase, PunchZone, type EnemyActor } from '../../src/logic/types.js';
import type { EnemyBrainHooks } from '../../src/logic/enemy-brain.js';

const actor = (x = 2): EnemyActor => ({
  id: 'e', isAlive: true, isActive: true, moveSpeed: 2,
  position: { x, y: 0, z: 0 },
});

const makeHooks = (opts: {
  anchor?: { x: number; y: number; z: number } | null;
  distance?: number;
  outcomes?: AttackOutcome[];
} = {}): EnemyBrainHooks => {
  const d = opts.distance ?? 1.0;
  return {
    getPunchAnchor: () => opts.anchor ?? null,
    getPlayerPosition: () => ({ x: d, y: 0, z: 0 }),
    getPlayerForward: () => ({ x: -1, y: 0, z: 0 }),
    isPlayerDown: () => false,
    isPlayerDead: () => false,
    isPlayerGrappled: () => false,
    canGrab: () => true,
    onAttack: (o) => { opts.outcomes?.push(o); },
  };
};

const advance = (brain: EnemyBrain, frames: number, t0 = 0): number => {
  let t = t0;
  for (let i = 0; i < frames; i++) { t += 1 / 60; brain.tick(1 / 60, t); }
  return t;
};

describe('EnemyBrain 状態機械', () => {
  it('Chase → Windup → Attack → Recover → Chase を回す', () => {
    const outcomes: AttackOutcome[] = [];
    const brain = new EnemyBrain('e', actor(1.0), makeHooks({ outcomes }),
      { attackCooldown: 0.1, recoverSeconds: 0.2, jabSeconds: 0.2, feintChance: 0 });
    brain.beginWindup(AttackId.Jab); // AttackId.Jab
    expect(brain.phase).toBe(EnemyPhase.Windup);
    advance(brain, 20); // 0.333s > windup
    expect([EnemyPhase.Attack, EnemyPhase.Recover]).toContain(brain.phase);
    // 硬直が明けて待機（Chase または Strafe）へ戻るまで進める
    for (let i = 0; i < 60 && ![EnemyPhase.Chase, EnemyPhase.Strafe].includes(brain.phase); i++) {
      brain.tick(1 / 60, 0.333 + (i + 1) / 60);
    }
    expect([EnemyPhase.Chase, EnemyPhase.Strafe]).toContain(brain.phase);
    expect(outcomes.length).toBeGreaterThanOrEqual(1);
  });

  it('punch anchor は外部注入を使い、届かない距離では当たらない', () => {
    const near = new EnemyBrain('near', actor(1), makeHooks({ anchor: { x: 1, y: 1.35, z: 0 } }));
    const far = new EnemyBrain('far', actor(1), makeHooks({ anchor: { x: 1, y: 3, z: 0 } }));
    near.zone = PunchZone.Face;
    far.zone = PunchZone.Face;
    expect(near.punchTouchesPlayer()).toBe(true);
    expect(far.punchTouchesPlayer()).toBe(false);
  });

  it('getPunchAnchor が null なら当たらない（rig 非依存を保つ）', () => {
    const brain = new EnemyBrain('e', actor(1), makeHooks({ anchor: null }));
    expect(brain.punchTouchesPlayer()).toBe(false);
  });

  it('被弾で Stagger になり、時間経過で Chase に戻る', () => {
    const brain = new EnemyBrain('e', actor(1), makeHooks(), { staggerSeconds: 0.3 });
    brain.takeHit(10, 1);
    expect(brain.phase).toBe(EnemyPhase.Stagger);
    expect(brain.isAlive).toBe(true);
    advance(brain, 30);
    expect([EnemyPhase.Chase, EnemyPhase.Strafe]).toContain(brain.phase);
  });

  it('HP が 0 になると Dead になり Died フックが一度だけ走る', () => {
    let died = 0;
    const hooks = makeHooks();
    hooks.onDied = () => { died++; };
    const brain = new EnemyBrain('e', actor(1), hooks, { maxHp: 5 });
    brain.takeHit(5, 1);
    expect(brain.phase).toBe(EnemyPhase.Dead);
    expect(brain.isAlive).toBe(false);
    expect(died).toBe(1);
    brain.takeHit(5, 1);
    expect(died).toBe(1);
  });

  it('フェイントは予備動作を途中で引いて攻撃しない', () => {
    const outcomes: AttackOutcome[] = [];
    const hooks = makeHooks({ outcomes });
    // 乱数を 0 に近づけるため、強制的にフェイント確率 1 で試す
    const brain = new EnemyBrain('e', actor(1), hooks,
      { feintChance: 1, feintDelaySeconds: 0.4, jabSeconds: 0.2 });
    brain.beginWindup(AttackId.Jab);
    advance(brain, 20);
    expect([EnemyPhase.Chase, EnemyPhase.Strafe]).toContain(brain.phase);
    expect(outcomes.length).toBe(0);
  });
});
