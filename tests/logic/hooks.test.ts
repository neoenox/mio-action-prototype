import { describe, expect, it } from 'vitest';
import { AttackId, EnemyBrain } from '../../src/logic/enemy-brain.js';
import {
  createHooksFromRig,
  createPunchAnchorProvider,
  type PunchAnchorRig,
} from '../../src/logic/hooks.js';
import { EnemyPhase, PunchZone, type EnemyActor, type PunchAnchor } from '../../src/logic/types.js';
import type { EnemyBrainHooks } from '../../src/logic/enemy-brain.js';

const actor = (x = 2): EnemyActor => ({
  id: 'e', isAlive: true, isActive: true, moveSpeed: 2,
  position: { x, y: 0, z: 0 },
});

const headAnchor: PunchAnchor = { x: 1, y: 1.35, z: 0 };
const bodyAnchor: PunchAnchor = { x: 1, y: 1.03, z: 0 };

/** rig 側の実装を想定したスタブ（zone 文字列で返す）。 */
const rig: PunchAnchorRig = {
  getPunchAnchor(zone) {
    return zone === 'head' ? headAnchor : bodyAnchor;
  },
};

const baseOtherHooks = (player: { x: number; y: number; z: number }): Omit<EnemyBrainHooks, 'getPunchAnchor'> => ({
  getPlayerPosition: () => player,
  getPlayerForward: () => ({ x: -1, y: 0, z: 0 }),
  isPlayerDown: () => false,
  isPlayerDead: () => false,
  isPlayerGrappled: () => false,
  canGrab: () => false,
});

describe('createHooksFromRig', () => {
  it('head と body で異なる座標が渡る（PunchZone が rig の zone 名へ変換される）', () => {
    const calls: string[] = [];
    const hooks = createHooksFromRig({
      getPunchAnchor(zone) {
        calls.push(zone);
        return zone === 'head' ? headAnchor : bodyAnchor;
      },
    }, baseOtherHooks({ x: 1, y: 0, z: 0 }));

    expect(hooks.getPunchAnchor(PunchZone.Face)).toEqual({ x: 1, y: 1.35, z: 0 });
    expect(hooks.getPunchAnchor(PunchZone.Body)).toEqual({ x: 1, y: 1.03, z: 0 });
    expect(calls).toEqual(['head', 'body']);
    expect(hooks.getPunchAnchor(PunchZone.Face)).not.toEqual(hooks.getPunchAnchor(PunchZone.Body));
  });

  it('head/body の座標マップを渡しても同じ変換が動く', () => {
    const hooks = createHooksFromRig(
      { head: headAnchor, body: bodyAnchor },
      baseOtherHooks({ x: 1, y: 0, z: 0 }),
    );
    expect(hooks.getPunchAnchor(PunchZone.Face)).toEqual(headAnchor);
    expect(hooks.getPunchAnchor(PunchZone.Body)).toEqual(bodyAnchor);
  });

  it('無効な座標や null は null として扱う（rig 未初期化でも落ちない）', () => {
    const provider = createPunchAnchorProvider({
      getPunchAnchor: () => ({ x: Number.NaN, y: 1.35, z: 0 }),
    });
    expect(provider(PunchZone.Face)).toBeNull();
    expect(createPunchAnchorProvider({ head: null, body: null })(PunchZone.Body)).toBeNull();
  });

  it('注入した anchor が命中判定に使われる（zone ごとの座標で hit が変わる）', () => {
    // プレイヤー中心 (1, 0, 0)。顔の狙い高さは +1.35、腹は約 +1.034。
    // head anchor は顔狙いに、body anchor は腹狙いに一致する。
    const hooks = createHooksFromRig(rig, baseOtherHooks({ x: 1, y: 0, z: 0 }));
    const brain = new EnemyBrain('e', actor(1), hooks, { punchHitRadius: 0.12 });

    brain.zone = PunchZone.Face;
    expect(brain.punchTouchesPlayer()).toBe(true);

    brain.zone = PunchZone.Body;
    expect(brain.punchTouchesPlayer()).toBe(true);

    // 逆の zone の anchor を差し込むと外れる（anchor が判定に使われている証拠）。
    const swapped = createHooksFromRig({
      getPunchAnchor: (zone) => (zone === 'head' ? bodyAnchor : headAnchor),
    }, baseOtherHooks({ x: 1, y: 0, z: 0 }));
    const brain2 = new EnemyBrain('e2', actor(1), swapped, { punchHitRadius: 0.12 });
    brain2.zone = PunchZone.Face;
    expect(brain2.punchTouchesPlayer()).toBe(false);
  });

  it('anchor が外れていれば strike の hit が false で damage 0 になる', () => {
    const outcomes: Array<{ hit: boolean; damage: number }> = [];
    const hooks = createHooksFromRig({
      getPunchAnchor: () => ({ x: 5, y: 1.35, z: 0 }), // 明らかに届かない
    }, {
      ...baseOtherHooks({ x: 1, y: 0, z: 0 }),
      onAttack: (o) => { outcomes.push({ hit: o.hit, damage: o.damage }); },
    });
    const brain = new EnemyBrain('e', actor(1.0), hooks, {
      punchHitRadius: 0.1, jabSeconds: 0.1, recoverSeconds: 0.1, attackCooldown: 1,
    });
    brain.setForceZone(PunchZone.Face);
    brain.beginWindup(AttackId.Jab);
    for (let i = 0; i < 20; i++) {
      brain.tick(1 / 60, (i + 1) / 60);
      if (brain.phase === EnemyPhase.Recover) break;
    }
    expect(outcomes.length).toBe(1);
    expect(outcomes[0]!.hit).toBe(false);
    expect(outcomes[0]!.damage).toBe(0);
  });

  it('距離内かつ anchor が一致すれば strike の hit が true でダメージが入る', () => {
    const outcomes: Array<{ hit: boolean; damage: number }> = [];
    const hooks = createHooksFromRig(rig, {
      ...baseOtherHooks({ x: 1, y: 0, z: 0 }),
      onAttack: (o) => { outcomes.push({ hit: o.hit, damage: o.damage }); },
    });
    const brain = new EnemyBrain('e', actor(1.0), hooks, {
      punchHitRadius: 0.12, jabSeconds: 0.1, recoverSeconds: 0.1, attackCooldown: 1,
    });
    brain.setForceZone(PunchZone.Face);
    brain.beginWindup(AttackId.Jab);
    for (let i = 0; i < 20; i++) {
      brain.tick(1 / 60, (i + 1) / 60);
      if (brain.phase === EnemyPhase.Recover) break;
    }
    expect(outcomes.length).toBe(1);
    expect(outcomes[0]!.hit).toBe(true);
    expect(outcomes[0]!.damage).toBeGreaterThan(0);
  });
});
