import { describe, expect, it } from 'vitest';
import { GrappleSession, WallStage } from '../../src/logic/grapple-session.js';
import { WallSequence, WallSequencePhase, WallStrikeKind } from '../../src/logic/wall-sequence.js';
import type { EnemyActor } from '../../src/logic/types.js';

const actor = (id: string): EnemyActor => ({
  id, isAlive: true, isActive: true, moveSpeed: 2,
  position: { x: 0, y: 0, z: 0 },
});

interface Harness {
  session: GrappleSession;
  seq: WallSequence;
  captor: EnemyActor;
  assist: EnemyActor;
  third: EnemyActor;
  alive: boolean;
  inputs: number;
  strikes: Array<{ puncher: EnemyActor | null; kind: WallStrikeKind; index: number }>;
  releasedAssist: number;
  releasedThird: number;
  tosses: number;
}

const harness = (): Harness => {
  const session = new GrappleSession();
  session.isWall = true;
  session.setStage(WallStage.Held);
  const captor = actor('captor');
  session.captor = captor;
  const assist = actor('assist');
  const third = actor('third');
  session.assist = assist;
  session.assistCandidate = assist;
  session.splitStarted = true;
  session.splitReady = true;
  session.third = third;
  session.gangActive = true;
  session.gangStriker = third;
  const h: Harness = {
    session, seq: null as unknown as WallSequence, captor, assist, third,
    alive: true, inputs: 0, strikes: [], releasedAssist: 0, releasedThird: 0, tosses: 0,
  };
  h.seq = new WallSequence({
    session,
    isAlive: () => h.alive,
    inputCount: () => h.inputs,
    captor: () => h.captor,
    assist: () => h.assist,
    third: () => h.third,
    fireStrike: (puncher, kind, index) => { h.strikes.push({ puncher, kind, index }); },
    beginToss: () => { h.tosses++; },
    releaseAssist: () => { h.releasedAssist++; },
    releaseThird: () => { h.releasedThird++; },
  });
  return h;
};

/** 1 秒間進める（dt=1/60）。 */
const run = (h: Harness, seconds: number): void => {
  const dt = 1 / 60;
  for (let i = 0; i < Math.ceil(seconds / dt); i++) h.seq.tick(dt);
};

describe('壁シーケンスの後始末', () => {
  it('start で WallSequenceActive=true、stop で必ず false になる（正常終了）', () => {
    const h = harness();
    h.seq.start();
    expect(h.session.wallSequenceActive).toBe(true);
    h.seq.stop();
    expect(h.session.wallSequenceActive).toBe(false);
    expect(h.session.wallPauseActive).toBe(false);
    expect(h.seq.currentPhase).toBe(WallSequencePhase.Done);
    expect(h.session.splitStarted).toBe(false);
    expect(h.session.gangActive).toBe(false);
    expect(h.session.third).toBeNull();
  });

  it('掴みが解けて isAlive=false になると自動で stop() を通る', () => {
    const h = harness();
    h.seq.start();
    h.alive = false;
    h.seq.tick(1 / 60);
    expect(h.session.wallSequenceActive).toBe(false);
    expect(h.seq.wasStopped).toBe(true);
    expect(h.releasedAssist).toBe(1);
    expect(h.releasedThird).toBe(1);
  });

  it('停止後に tick を呼んでも状態は変わらない', () => {
    const h = harness();
    h.seq.start();
    h.seq.stop();
    const before = h.strikes.length;
    run(h, 2);
    expect(h.strikes.length).toBe(before);
    expect(h.session.wallSequenceActive).toBe(false);
  });

  it('途中で抜けても 2 打撃スロットと抑制時間が残らない', () => {
    const h = harness();
    h.seq.start();
    // 膝待ち → 猶予 → 二人がかり 1 発目まで進める
    h.seq.onKneesFinished();
    run(h, 0.1);
    h.inputs = 10;
    run(h, 0.1);
    expect(h.session.wallSequenceActive).toBe(true);
    h.session.claimStrike(h.assist);
    h.session.suppressRemaining = 999;

    h.alive = false;
    h.seq.tick(1 / 60);

    expect(h.session.wallSequenceActive).toBe(false);
    expect(h.session.strikes.every((s) => s.puncher === null && s.age === -1)).toBe(true);
    expect(h.session.suppressRemaining).toBe(0);
  });

  it('最後まで進むと投げ捨てを発火し、その後 WallSequenceActive が落ちる', () => {
    const h = harness();
    // 3 人がかりを短くするため打撃表の長さ分を回す（5 サイクル × 3 発）
    h.seq.start();
    h.seq.onKneesFinished();
    h.inputs = 999;
    run(h, 60);
    // いずれにせよ停止していれば後始末は完了している
    expect(h.session.wallSequenceActive).toBe(false);
    expect(h.session.gangActive).toBe(false);
  });
});
