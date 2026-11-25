import { describe, expect, it } from 'vitest';
import { GameWorld, type WorldEvent } from '../../src/logic/world.js';
import { WallStage } from '../../src/logic/grapple-session.js';
import { WallSequencePhase } from '../../src/logic/wall-sequence.js';
import { EnemyPhase, PunchZone } from '../../src/logic/types.js';
import { GrappleKind } from '../../src/core/GrappleResistance.js';
import { EnemyRole } from '../../src/core/EnemyRole.js';
import { BodyPart, CombatConstants } from '../../src/core/CombatConstants.js';
import type { EnemySettings } from '../../src/logic/enemy-brain.js';
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

/**
 * プレイヤーの攻撃だけを見るための敵設定。
 * 移動も攻撃もさせない（間合いを潰し、掴みも選ばせない）ので、判定と硬直だけを観察できる。
 * attackRange 0.4 → engageDistance 0.34。掴みの射程も同じ値なので、踏み込んでも掴まれない。
 */
const passiveEnemy = (over: Partial<EnemySettings> = {}): Partial<EnemySettings> => ({
  moveSpeed: 0,
  attackRange: 0.4,
  jabRange: 0.01,
  heavyRange: 0.01,
  breakRange: 0.01,
  grabRange: 0.01,
  ...over,
});

/**
 * 決まった立ち位置へ戻す（プレイヤー x=0 / 敵 x=0.9、間合い 1.08 の内側）。
 * AI の移動・よろけ戻り・踏み込みの蓄積を打ち消し、判定だけを見られるようにする。
 */
const strikeStage = (world: GameWorld, index = 0): void => {
  world.playerMotor.position.x = 0;
  world.slots[index]!.brain.position.x = 0.9;
};

/** プレイヤーが敵に当てたヒットだけを取り出す。 */
const playerHits = (events: WorldEvent[]): Extract<WorldEvent, { type: 'hit' }>[] =>
  events.filter((e): e is Extract<WorldEvent, { type: 'hit' }> =>
    e.type === 'hit' && e.attacker === 'player');

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
      [
        'attackKind', 'facing', 'hp', 'isAttacking', 'isDown',
        'isGrappled', 'isMoving', 'isStruggling', 'x', 'z',
      ],
    );
    expect(snap.player.attackKind).toBeNull();
    expect(snap.player.isAttacking).toBe(false);
    expect(snap.enemies).toHaveLength(2);
    const enemy = snap.enemies[0]!;
    expect(Object.keys(enemy).sort()).toEqual(
      ['facing', 'height', 'hp', 'id', 'isAttacking', 'isDead', 'isDown', 'phase', 'role', 'x', 'z'],
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

describe('プレイヤーの攻撃（intent.light / heavy / break_）', () => {
  it('軽攻撃で敵の HP が減り、命中イベントに詳細が入る', () => {
    const world = buildWorld({ enemyCount: 1, enemySettings: passiveEnemy() });
    strikeStage(world);
    const before = world.snapshot().enemies[0]!.hp;

    const events = run(world, 20, (i) => intent({ light: i === 0 }));

    const after = world.snapshot().enemies[0]!.hp;
    // AttackLibrary.light().damage = 8
    expect(before - after).toBeCloseTo(8, 6);

    const hits = playerHits(events);
    expect(hits).toHaveLength(1);
    expect(hits[0]!.attacker).toBe('player');
    expect(hits[0]!.attackerId).toBe('player');
    expect(hits[0]!.targetId).toBe('enemy-1');
    expect(hits[0]!.damage).toBeCloseTo(8, 6);
    expect(hits[0]!.zone).toBe(PunchZone.Body);
  });

  it('腕の損傷倍率が効く（腕ダメージを上げると与ダメージが減る）', () => {
    const dealtWithArms = (armsDamage: number): number => {
      const world = buildWorld({ enemyCount: 1, enemySettings: passiveEnemy() });
      strikeStage(world);
      world.playerCombat.body.apply(BodyPart.Arms, armsDamage);
      const before = world.snapshot().enemies[0]!.hp;
      run(world, 20, (i) => intent({ light: i === 0 }));
      return before - world.snapshot().enemies[0]!.hp;
    };

    const healthy = dealtWithArms(0);
    const injured = dealtWithArms(CombatConstants.PartMax); // ArmsDamageFloor = 0.75

    expect(healthy).toBeCloseTo(8, 6);
    expect(injured).toBeCloseTo(8 * CombatConstants.ArmsDamageFloor, 6);
    expect(injured).toBeLessThan(healthy);
  });

  it('硬直中は次の攻撃を受け付けない（硬直が明ければ出る）', () => {
    const world = buildWorld({ enemyCount: 1, enemySettings: passiveEnemy() });
    strikeStage(world);

    let hits = 0;
    let attackingDuringRecovery = false;
    // 軽攻撃の総フレーム = (6 + 4 + 10) / 60 秒 = 20 フレーム。
    // 0 で 1 発目、12 は硬直中（捨てられる）、22 は硬直明け（通る）。
    for (let i = 0; i < 45; i++) {
      if (i === 0 || i === 22) strikeStage(world);
      world.tick(1 / 60, intent({ light: i === 0 || i === 12 || i === 22 }));
      hits += playerHits(world.snapshot().events).length;
      if (i === 12) {
        const player = world.snapshot().player;
        attackingDuringRecovery = player.isAttacking && player.attackKind === 'light';
      }
    }

    expect(hits).toBe(2);
    expect(attackingDuringRecovery).toBe(true);
    expect(world.snapshot().player.isAttacking).toBe(false);
    expect(world.snapshot().player.attackKind).toBeNull();
  });

  it('何発かで敵がダウン・死亡し、isDead になる', () => {
    const world = buildWorld({ enemyCount: 1, enemySettings: passiveEnemy({ maxHp: 24 }) });
    strikeStage(world);

    // 重攻撃 = 18 ダメージ。24 HP なので 2 発で 0 になる。
    for (let i = 0; i < 120; i++) {
      if (i === 0 || i === 45) strikeStage(world);
      world.tick(1 / 60, intent({ heavy: i === 0 || i === 45 }));
    }

    const enemy = world.snapshot().enemies[0]!;
    expect(enemy.hp).toBe(0);
    expect(enemy.isDead).toBe(true);
    expect(enemy.isDown).toBe(true);
    expect(world.slots[0]!.brain.phase).toBe(EnemyPhase.Dead);
  });

  it('敵を全滅させると snapshot().enemies の全員が isDead になる', () => {
    const world = buildWorld({ enemyCount: 2, enemySettings: passiveEnemy({ maxHp: 8 }) });

    for (let i = 0; i < 200; i++) {
      // 25 フレームごとに 1 発。生きている敵を毎回正面の間合いへ戻す。
      if (i % 25 === 0) for (let n = 0; n < world.slots.length; n++) strikeStage(world, n);
      world.tick(1 / 60, intent({ light: i % 25 === 0 }));
    }

    const snap = world.snapshot();
    expect(snap.enemies).toHaveLength(2);
    expect(snap.enemies.every((e) => e.isDead)).toBe(true);
    expect(snap.enemies.every((e) => e.isDown)).toBe(true);
  });
});

describe('壁拘束の膝 5 発（logic 側で発生）', () => {
  it('onKneesFinished を呼ばなくても膝が 5 発出て壁シーケンスが進む', () => {
    const world = new GameWorld({ enemyCount: 2 });
    expect(world.beginWallGrapple()).toBe(true);

    let hpAtKneesDone = -1;
    let phaseAfterKnees: WallSequencePhase | null = null;

    for (let i = 0; i < 700; i++) {
      world.tick(1 / 60, intent({ god: true }));
      if (hpAtKneesDone < 0 && world.kneeCount === CombatConstants.PinKneeCount) {
        hpAtKneesDone = world.playerCombat.hp;
      }
      if (phaseAfterKnees === null &&
          world.wallPhase !== WallSequencePhase.Idle &&
          world.wallPhase !== WallSequencePhase.WaitKnees) {
        phaseAfterKnees = world.wallPhase;
      }
    }

    // 膝 5 発 = PinKneeDamage × 5（この時点では二人がかりの腹パンはまだ始まっていない）
    expect(world.kneeCount).toBe(CombatConstants.PinKneeCount);
    expect(hpAtKneesDone).toBeCloseTo(
      CombatConstants.MaxHp - CombatConstants.PinKneeCount * CombatConstants.PinKneeDamage,
      6,
    );
    expect(phaseAfterKnees).toBe(WallSequencePhase.PauseAfterKnees);
    // 猶予のあと二人目が入る（app が onKneesFinished を呼ばなくても自然に進む）
    expect(world.session.assistCandidate).not.toBeNull();
  });

  it('既存の onKneesFinished 呼び出しは後方互換で残り、早送りできる', () => {
    const world = new GameWorld({ enemyCount: 2 });
    world.beginWallGrapple();
    run(world, 200, () => intent({ god: true })); // Held まで進めて膝の途中まで
    expect(world.wallPhase).toBe(WallSequencePhase.WaitKnees);

    world.onKneesFinished();
    run(world, 3, () => intent({ god: true }));
    expect(world.wallPhase).toBe(WallSequencePhase.PauseAfterKnees);
  });
});

describe('敵の背丈と役割', () => {
  it('背丈で掴み役 / 道具役 / 打撃役が分かれる', () => {
    const world = new GameWorld({ enemyCount: 3, enemyHeights: [1.85, 1.79, 1.72] });
    const snap = world.snapshot();
    expect(snap.enemies.map((e) => e.height)).toEqual([1.85, 1.79, 1.72]);
    expect(snap.enemies.map((e) => e.role)).toEqual([
      EnemyRole.Grappler,
      EnemyRole.Tools,
      EnemyRole.Striker,
    ]);
  });

  it('同じ背丈でも名前順で 1 体が掴み役になる（列挙順に依存しない）', () => {
    const world = new GameWorld({ enemyCount: 2, enemyHeights: [1.75, 1.75] });
    const roles = world.snapshot().enemies.map((e) => e.role);
    expect(roles).toContain(EnemyRole.Grappler);
    expect(roles.filter((r) => r === EnemyRole.Grappler)).toHaveLength(1);
  });

  it('明示した掴み役は背丈より優先される', () => {
    const world = new GameWorld({
      enemyCount: 2,
      enemyHeights: [1.85, 1.72],
      authoredGrapplerIndex: 1,
    });
    const snap = world.snapshot();
    expect(snap.enemies[1]!.role).toBe(EnemyRole.Grappler);
    expect(snap.enemies[0]!.role).toBe(EnemyRole.Striker);
  });
});


