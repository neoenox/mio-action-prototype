import { describe, expect, it } from 'vitest';
import { GameWorld, type WorldEvent } from '../../src/logic/world.js';
import { WallStage } from '../../src/logic/grapple-session.js';
import { WallSequencePhase } from '../../src/logic/wall-sequence.js';
import { EnemyPhase } from '../../src/logic/types.js';
import { GrappleKind } from '../../src/core/GrappleResistance.js';
import type { RigLike } from '../../src/logic/hooks.js';
import { emptyIntent, type InputIntent } from '../../src/input/intent.js';

const intent = (over: Partial<InputIntent> = {}): InputIntent => ({ ...emptyIntent(), ...over });

/**
 * rig の代役。プレイヤーの現在位置に拳を置く（＝必ず届く）。
 * logic は「rig から注入されたアンカー」でしか命中を見ないので、
 * これで EnemyBrain の判定経路がそのまま通る。
 */
const anchorRig = (player: () => { x: number; y: number; z: number }): RigLike => ({
  getPunchAnchor: (zone) => {
    const p = player();
    return { x: p.x, y: p.y + (zone === 'head' ? 1.35 : 1.07), z: p.z };
  },
});

/** 1/60 秒で n フレーム回し、その間のイベントを全部集める。 */
const run = (
  world: GameWorld,
  frames: number,
  make: (i: number) => InputIntent = () => intent(),
): WorldEvent[] => {
  const events: WorldEvent[] = [];
  for (let i = 0; i < frames; i++) {
    world.tick(1 / 60, make(i));
    events.push(...world.snapshot().events);
  }
  return events;
};

/** プレイヤー位置に拳を置く rig を差し込んだ world を作る。 */
const buildWorld = (options: ConstructorParameters<typeof GameWorld>[0] = {}): GameWorld => {
  let world: GameWorld | null = null;
  const rig = anchorRig(() => world!.playerMotor.position);
  world = new GameWorld({ ...options, rigs: [rig, rig] });
  return world;
};

describe('GameWorld', () => {
  it('敵が近づいて攻撃し、プレイヤーの HP が減る', () => {
    const world = buildWorld();
    const start = world.snapshot();
    expect(start.player.hp).toBeGreaterThan(0);

    const events = run(world, 180); // 3 秒

    const after = world.snapshot();
    expect(after.player.hp).toBeLessThan(start.player.hp);
    const hits = events.filter((e): e is Extract<WorldEvent, { type: 'hit' }> => e.type === 'hit');
    expect(hits.length).toBeGreaterThan(0);
    expect(hits[0]!.damage).toBeGreaterThan(0);
    // 敵は近づいて攻撃モーションを経ている
    expect(after.enemies.some((e) => e.x < start.enemies[0]!.x)).toBe(true);
  });

  it('入力でプレイヤーが横移動し、向きが移動方向になる', () => {
    const world = new GameWorld({ enemyCount: 0 });
    const start = world.snapshot();

    run(world, 30, () => intent({ moveX: 1 }));
    const right = world.snapshot();
    expect(right.player.x).toBeGreaterThan(start.player.x + 0.5);
    expect(right.player.facing).toBe(1);

    run(world, 120, () => intent({ moveX: -1 }));
    const left = world.snapshot();
    expect(left.player.x).toBeLessThan(right.player.x - 0.5);
    expect(left.player.facing).toBe(-1);
  });

  it('掴まれると isGrappled になり、もがき入力で脱出する', () => {
    // 掴みだけが選ばれるように間合いを絞る（jab/heavy/break の射程を潰す）。
    const world = buildWorld({
      enemyCount: 1,
      enemySettings: {
        attackRange: 1.2,
        jabRange: 0.01,
        heavyRange: 0.01,
        breakRange: 0.01,
        grabRange: 1.2,
        recoverSeconds: 0.2,
      },
    });

    let grabbedAt = -1;
    for (let i = 0; i < 240 && grabbedAt < 0; i++) {
      world.tick(1 / 60, intent());
      if (world.snapshot().player.isGrappled) grabbedAt = i;
    }
    expect(grabbedAt).toBeGreaterThanOrEqual(0);

    const grabbed = world.snapshot();
    expect(grabbed.player.isGrappled).toBe(true);
    expect(grabbed.grapple.kind).toBe(GrappleKind.Pin);
    expect(grabbed.player.isStruggling).toBe(false);

    // もがき 1 回で「もがいている」が出る
    world.tick(1 / 60, intent({ struggle: true }));
    expect(world.snapshot().player.isStruggling).toBe(true);

    const events = run(world, 20, () => intent({ struggle: true }));

    const after = world.snapshot();
    expect(after.player.isGrappled).toBe(false);
    expect(after.player.isStruggling).toBe(false);
    expect(events.some((e) => e.type === 'escape')).toBe(true);
  });

  it('壁シーケンスが段階を進める（Grab → … → Held → 二人がかり）', () => {
    const world = new GameWorld({ enemyCount: 2 });
    expect(world.beginWallGrapple()).toBe(true);

    const started = world.snapshot();
    expect(started.player.isGrappled).toBe(true);
    expect(started.grapple.stage).toBe(WallStage.Grab);

    const stages: WallStage[] = [WallStage.Grab];
    for (let i = 0; i < 240; i++) { // 4 秒 > 段階合計 2.48 秒
      world.tick(1 / 60, intent());
      for (const e of world.snapshot().events) {
        if (e.type === 'wall-stage') stages.push(e.to);
      }
    }

    const held = world.snapshot();
    expect(held.grapple.stage).toBe(WallStage.Held);
    expect(held.grapple.heldSeconds).toBeGreaterThan(0);
    expect(stages).toContain(WallStage.Rise);
    expect(stages).toContain(WallStage.Regrip);
    expect(stages).toContain(WallStage.Held);
    expect(world.wallPhase).toBe(WallSequencePhase.WaitKnees);

    // 膝 5 発が終わったら 2 体目が入り、猶予（10 入力）→ 二人がかり
    world.onKneesFinished();
    run(world, 3);
    expect(world.wallPhase).toBe(WallSequencePhase.PauseAfterKnees);
    expect(world.session.assistCandidate).not.toBeNull();
    run(world, 12, () => intent({ struggle: true }));
    expect(world.wallPhase).toBe(WallSequencePhase.Split);
  });

  it('同じ入力列なら同じ結果になる（決定論的）', () => {
    const a = buildWorld();
    const b = buildWorld();
    const script = (i: number): InputIntent => intent({
      moveX: i % 90 < 45 ? 1 : -1,
      struggle: i % 17 === 0,
    });

    const snapsA: string[] = [];
    const snapsB: string[] = [];
    for (let i = 0; i < 300; i++) {
      const input = script(i);
      a.tick(1 / 60, input);
      b.tick(1 / 60, { ...input });
      snapsA.push(JSON.stringify(a.snapshot()));
      snapsB.push(JSON.stringify(b.snapshot()));
    }

    expect(snapsB).toEqual(snapsA);
    expect(a.snapshot().player.hp).toBe(b.snapshot().player.hp);
    expect(a.snapshot().enemies.map((e) => e.phase)).toEqual(b.snapshot().enemies.map((e) => e.phase));
  });

  it('snapshot は必要な読み取り専用の形を返す', () => {
    const world = new GameWorld({ enemyCount: 2 });
    const snap = world.snapshot();

    expect(Object.keys(snap.player).sort()).toEqual(
      ['facing', 'hp', 'isDown', 'isGrappled', 'isMoving', 'isStruggling', 'x', 'z'],
    );
    expect(snap.enemies).toHaveLength(2);
    const enemy = snap.enemies[0]!;
    expect(Object.keys(enemy).sort()).toEqual(
      ['facing', 'hp', 'id', 'isAttacking', 'isDead', 'phase', 'x', 'z'],
    );
    expect(enemy.id).toBe('enemy-1');
    expect(enemy.phase).toBe(EnemyPhase.Chase);
    expect(Object.keys(snap.grapple).sort()).toEqual(
      ['gangActive', 'heldSeconds', 'kind', 'splitWeight', 'stage', 'stageProgress'],
    );
    expect(snap.events).toEqual([]);

    // snapshot を書き換えても内部状態は変わらない
    snap.enemies[0]!.x = 999;
    snap.player.x = 999;
    expect(world.snapshot().enemies[0]!.x).not.toBe(999);
    expect(world.snapshot().player.x).not.toBe(999);
  });

  it('拘束中に力尽きたら拘束が解ける（掴まれたまま固まらない）', () => {
    const world = buildWorld({
      enemyCount: 1,
      enemySettings: {
        attackRange: 1.2, jabRange: 0.01, heavyRange: 0.01, breakRange: 0.01, grabRange: 1.2,
      },
    });
    let grabbed = false;
    for (let i = 0; i < 240 && !grabbed; i++) {
      world.tick(1 / 60, intent());
      grabbed = world.snapshot().player.isGrappled;
    }
    expect(grabbed).toBe(true);

    world.playerCombat.applyHpDamage(999);
    run(world, 3);
    expect(world.snapshot().player.isGrappled).toBe(false);

    // 壁拘束も同じ経路で解ける
    const wall = new GameWorld({ enemyCount: 2 });
    wall.beginWallGrapple();
    wall.playerCombat.applyHpDamage(999);
    run(wall, 3);
    expect(wall.snapshot().player.isGrappled).toBe(false);
    expect(wall.snapshot().grapple.stage).toBe(WallStage.None);
  });

  it('敵を全滅させても world は落ちず、死んだ敵はトークンを手放す', () => {    const world = new GameWorld({ enemyCount: 1 });
    const brain = world.slots[0]!.brain;
    brain.takeHit(999, 2);
    expect(world.snapshot().enemies[0]!.isDead).toBe(true);
    expect(world.group.attackerCount).toBe(0);
    expect(() => run(world, 10)).not.toThrow();
    expect(world.snapshot().enemies[0]!.isAttacking).toBe(false);
  });
});
