import { describe, expect, it } from 'vitest';
import { GrappleSession, SplitStrike, WallStage } from '../../src/logic/grapple-session.js';
import type { EnemyActor } from '../../src/logic/types.js';

const actor = (id: string): EnemyActor => ({
  id,
  isAlive: true,
  isActive: true,
  moveSpeed: 2,
  position: { x: 0, y: 0, z: 0 },
});

describe('GrappleSessionTests（Unity の GrappleSessionTests を移植）', () => {
  it('打撃枠が満杯なら null を返し既存の枠を壊さない', () => {
    const session = new GrappleSession();
    const a = actor('striker-a');
    const b = actor('striker-b');
    const c = actor('striker-c');

    const slotA = session.claimStrike(a);
    const slotB = session.claimStrike(b);
    expect(slotA).not.toBeNull();
    expect(slotB).not.toBeNull();
    expect(slotA).not.toBe(slotB); // 同時に打てるのは 2 枠まで
    slotA!.age = 0.2;

    expect(session.claimStrike(c)).toBeNull(); // 満杯なら 0 枠を使い回さない
    expect(slotA!.puncher).toBe(a); // 既存の枠の持ち主が変わらない
    expect(slotA!.age).toBeCloseTo(0.2, 3); // 経過秒がリセットされない
    // 2 枠目も壊れないこと
    expect(slotB!.puncher).toBe(b);
    expect(slotB!.age).toBe(0);

    session.clearStrikes();
    expect(session.claimStrike(c)).not.toBeNull(); // 空けば次の打撃が入る
  });

  it(' Age>=0 の枠だけを占有とみなし、Age=-1 の枠は再利用する', () => {
    const session = new GrappleSession();
    const a = actor('a');
    const wait = session.claimStrike(a)!;
    wait.age = -1; // 待ち状態（Puncher は残るが打っていない）
    const b = actor('b');
    const reused = session.claimStrike(b);
    expect(reused).toBe(wait);
    expect(reused!.puncher).toBe(b);
    expect(reused!.age).toBe(0);
  });

  it('strikeOf は打っている人の枠だけを返す', () => {
    const session = new GrappleSession();
    const a = actor('a');
    const b = actor('b');
    const c = actor('c');
    const slotA = session.claimStrike(a)!;
    session.claimStrike(b);
    expect(session.strikeOf(a)).toBe(slotA);
    expect(session.strikeOf(b)).toBeInstanceOf(SplitStrike);
    expect(session.strikeOf(c)).toBeNull();
    slotA.age = -1;
    expect(session.strikeOf(a)).toBeNull();
  });

  it('clearSplit は全枠とギャング状態を戻す', () => {
    const session = new GrappleSession();
    session.assist = actor('assist');
    session.assistCandidate = actor('candidate');
    session.splitStarted = true;
    session.splitReady = true;
    session.splitHits = 4;
    session.claimStrike(actor('a'));
    session.third = actor('third');
    session.gangActive = true;
    session.gangHits = 3;
    session.gangTotal = 15;
    session.tickGangWeight(1);

    session.clearSplit();

    expect(session.assist).toBeNull();
    expect(session.assistCandidate).toBeNull();
    expect(session.splitStarted).toBe(false);
    expect(session.splitReady).toBe(false);
    expect(session.splitHits).toBe(0);
    expect(session.splitPunchesDone).toBe(false);
    expect(session.third).toBeNull();
    expect(session.gangActive).toBe(false);
    expect(session.gangHits).toBe(0);
    expect(session.gangTotal).toBe(0);
    expect(session.gangWeight).toBe(0);
    expect(session.strikes.every((s) => s.puncher === null && s.age === -1)).toBe(true);
  });
});

describe('壁段階と純粋な重み', () => {
  it('WallStage は 12 段階で TossLand=11', () => {
    expect(WallStage.None).toBe(0);
    expect(WallStage.Grab).toBe(1);
    expect(WallStage.Rise).toBe(2);
    expect(WallStage.Throw).toBe(3);
    expect(WallStage.Slam).toBe(4);
    expect(WallStage.Lean).toBe(5);
    expect(WallStage.Regrip).toBe(6);
    expect(WallStage.Restrain).toBe(7);
    expect(WallStage.Held).toBe(8);
    expect(WallStage.TossWindup).toBe(9);
    expect(WallStage.TossAir).toBe(10);
    expect(WallStage.TossLand).toBe(11);
  });

  it('setStage は進捗を捨て、advanceStage は Clamp01 する', () => {
    const session = new GrappleSession();
    session.setStage(WallStage.Grab);
    session.advanceStage(0.1, 0.22);
    expect(session.stageElapsed).toBeCloseTo(0.1);
    expect(session.stageProgress).toBeCloseTo(0.1 / 0.22);
    session.advanceStage(1, 0.22);
    expect(session.stageProgress).toBe(1);
    session.setStage(WallStage.Rise);
    expect(session.stageProgress).toBe(0);
    expect(session.stageElapsed).toBe(0);
    session.advanceStage(0.5, 0);
    expect(session.stageProgress).toBe(1);
  });

  it('Restrain/Held/TossWindup で壁への拘束重みが立ち上がる', () => {
    const session = new GrappleSession();
    session.setStage(WallStage.Restrain);
    session.advanceStage(0.26, 0.52);
    expect(session.wallRestraintWeight).toBeCloseTo(0.5);
    expect(session.wallGripWeight).toBe(1);
    expect(session.wallSlideWeight).toBeCloseTo(0.5);
    session.setStage(WallStage.Held);
    expect(session.wallRestraintWeight).toBe(1);
    expect(session.wallGripWeight).toBe(1);
    expect(session.wallSlideWeight).toBe(0);
  });

  it('投げ捨ての進行と壁からの移動は段階の純粋関数', () => {
    const session = new GrappleSession();
    session.isWall = true;
    session.setStage(WallStage.TossWindup);
    session.advanceStage(0.13, 0.26);
    expect(session.wallTossLower).toBeCloseTo(0.5);
    expect(session.isWallToss).toBe(true);
    expect(session.isWallTransition).toBe(true);
    expect(session.wallCaptorTravel).toBe(1);

    session.setStage(WallStage.Held);
    expect(session.isWallToss).toBe(false);
    expect(session.isWallTransition).toBe(false);

    session.setStage(WallStage.Throw);
    session.advanceStage(0.15, 0.30);
    expect(session.wallCaptorTravel).toBeCloseTo(0.275);
  });

  it('clearWall は壁シーケンスの進行フラグも落とす', () => {
    const session = new GrappleSession();
    session.isWall = true;
    session.wallSequenceActive = true;
    session.wallPauseActive = true;
    session.setStage(WallStage.Held);
    session.clearWall();
    expect(session.isWall).toBe(false);
    expect(session.wallSequenceActive).toBe(false);
    expect(session.wallPauseActive).toBe(false);
    expect(session.stage).toBe(WallStage.None);
    expect(session.stageProgress).toBe(0);
    expect(session.stageElapsed).toBe(0);
  });
});

describe('Y字／3人がかりの重み', () => {
  const readySession = (): GrappleSession => {
    const s = new GrappleSession();
    s.isWall = true;
    s.setStage(WallStage.Held);
    s.assist = actor('assist');
    s.splitReady = true;
    return s;
  };

  it('SplitClearSeconds=0.30 のあと右腕(0.55s)→一拍(0.35s)→左腕(0.60s) で上がる', () => {
    const s = readySession();
    for (let i = 0; i < 6; i++) s.tickSplitWeight(0.05); // 0.30s
    expect(s.splitWeightR).toBeGreaterThan(0);
    expect(s.splitWeight).toBe(0);

    for (let i = 0; i < 12; i++) s.tickSplitWeight(0.05); // +0.60s（右腕は 0.85s で満了）
    expect(s.splitWeightR).toBeCloseTo(1);
    expect(s.splitWeight).toBe(0); // 一拍待つのでまだ左腕は動かない

    // R が 1 になってから 0.35 秒（=7 Ticks）で拘束役が左腕へ回り込む
    for (let i = 0; i < 7; i++) s.tickSplitWeight(0.05);
    expect(s.isSplitCaptorShifting).toBe(true);
    expect(s.splitWeight).toBeGreaterThan(0);

    for (let i = 0; i < 14; i++) s.tickSplitWeight(0.05); // +0.70s
    expect(s.splitWeight).toBeCloseTo(1);
  });

  it('非アクティブなら位相も重みも 0 へ戻る', () => {
    const s = readySession();
    for (let i = 0; i < 40; i++) s.tickSplitWeight(0.05);
    expect(s.splitWeight).toBeGreaterThan(0);
    s.assist = null;
    for (let i = 0; i < 40; i++) s.tickSplitWeight(0.05);
    expect(s.splitWeight).toBe(0);
    expect(s.splitWeightR).toBe(0);
  });

  it('GangWeight は 0.35 秒で目標へ MoveTowards する', () => {
    const s = new GrappleSession();
    s.gangActive = true;
    s.tickGangWeight(0.175);
    expect(s.gangWeight).toBeCloseTo(0.5);
    s.tickGangWeight(0.175);
    expect(s.gangWeight).toBeCloseTo(1);
    s.gangActive = false;
    s.tickGangWeight(0.35);
    expect(s.gangWeight).toBe(0);
  });
});
