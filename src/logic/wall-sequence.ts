import { CombatConstants } from '../core/CombatConstants';
import { GrappleSession, WallStage, WALL_STAGE_SECONDS } from './grapple-session';
import type { EnemyActor } from './types';

/** 壁シーケンスの段階。Unity の Coroutine を phase + elapsed に変換したもの。 */
export enum WallSequencePhase {
  Idle,
  /** 膝 5 発が終わるまで待つ。 */
  WaitKnees,
  /** 二体目が入ってくる間の猶予（10 入力）。 */
  PauseAfterKnees,
  /** 二人がかり（Y 字）の腹パン 6 発。 */
  Split,
  /** 二人がかり後の猶予（14 入力）。 */
  PauseAfterSplit,
  /** 3 人がかり（15 発、2 枠同時）。 */
  Gang,
  /** 投げ捨ての開始（1 フレーム）。 */
  Toss,
  Done,
}

export interface WallSequenceDeps {
  session: GrappleSession;
  /** 進行を続けられる状態か（掴まれている・生きている・壁 Held・引き渡し中でない）。 */
  isAlive(): boolean;
  /** 現在の入力数（wasPressedThisFrame をカウントした値）。 */
  inputCount(): number;
  captor(): EnemyActor | null;
  assist(): EnemyActor | null;
  third(): EnemyActor | null;
  callAssist?(): void;
  prepareThird?(): void;
  fireStrike?(puncher: EnemyActor | null, kind: WallStrikeKind, index: number, scale: number): void;
  beginToss?(): void;
  releaseAssist?(): void;
  releaseThird?(): void;
  log?(message: string): void;
}

export enum WallStrikeKind { Belly, Knee, Face, Kick }

const ASSIST_KINDS: readonly WallStrikeKind[] = [
  WallStrikeKind.Belly, WallStrikeKind.Face, WallStrikeKind.Belly,
  WallStrikeKind.Knee, WallStrikeKind.Face,
];
const CAPTOR_KINDS: readonly WallStrikeKind[] = [
  WallStrikeKind.Face, WallStrikeKind.Belly, WallStrikeKind.Face,
  WallStrikeKind.Belly, WallStrikeKind.Knee,
];
const THIRD_KINDS: readonly WallStrikeKind[] = [
  WallStrikeKind.Kick, WallStrikeKind.Kick, WallStrikeKind.Kick,
  WallStrikeKind.Kick, WallStrikeKind.Kick,
];
/** 2 巡目と 3 巡目はアシストと拘束役が同時に入る（Unity 版と同じ）。 */
const SIMULTANEOUS: readonly boolean[] = [false, true, true, false, false];

const STRIKE_SLOT_WAIT_SECONDS = 0.35;

/** 1 発の進行状態。2 枠同時でも独立に進めるため、状態をインスタンスごとに持つ。 */
class StrikeRunner {
  readonly puncher: EnemyActor | null;
  readonly kind: WallStrikeKind;
  readonly index: number;
  readonly windup: number;
  readonly interval: number;
  elapsed = 0;
  claimed = false;
  fired = false;

  constructor(puncher: EnemyActor | null, kind: WallStrikeKind, index: number) {
    this.puncher = puncher;
    this.kind = kind;
    this.index = index;
    const leg = kind === WallStrikeKind.Knee || kind === WallStrikeKind.Kick;
    this.windup = leg
      ? CombatConstants.wallGangLegWindup(index)
      : CombatConstants.wallGangWindup(index);
    this.interval = leg
      ? CombatConstants.wallGangLegInterval(index)
      : CombatConstants.wallGangInterval(index);
  }
}

/**
 * 壁拘束の一連の段階を進める状態機械。
 *
 * Unity 版は `WallSequenceRoutine` の
 * `try { yield return WallSequenceSteps(); } finally { FinishWallSequence(); }`
 * で後始末を保証していた。ここでは **どの経路で終わっても `stop()` を通る** ことで同じ保証にする：
 * - 正常終了（`tick()` が Toss → Done に到達）
 * - 途中で掴みが解けた／ダウン／死亡／引き渡し（`isAlive()` が false）
 * - 明示的な中断（外部から `stop()`）
 */
export class WallSequence {
  readonly session: GrappleSession;
  private readonly deps: WallSequenceDeps;
  private phase: WallSequencePhase = WallSequencePhase.Idle;
  private elapsed = 0;
  private stepIndex = 0;
  private strikeTime = 0;
  private strikeFired = false;
  private gangCycle = 0;
  private gangSub = 0;
  private activeStrikes: StrikeRunner[] = [];
  private kneesDone = false;
  private stopped = false;

  constructor(deps: WallSequenceDeps) {
    this.deps = deps;
    this.session = deps.session;
  }

  get currentPhase(): WallSequencePhase { return this.phase; }
  get isRunning(): boolean {
    return this.phase !== WallSequencePhase.Idle && this.phase !== WallSequencePhase.Done;
  }
  get wasStopped(): boolean { return this.stopped; }

  onKneesFinished(): void { this.kneesDone = true; }

  start(): void {
    // 前回の進行フラグだけを落とす。ここで FinishWallSequence を呼ぶと
    // 待機中の AssistCandidate まで解放されてしまう（Unity 版と同じ理由）。
    this.session.wallSequenceActive = false;
    this.session.wallPauseActive = false;
    this.kneesDone = false;
    this.elapsed = 0;
    this.stepIndex = 0;
    this.stopped = false;
    this.session.wallSequenceActive = true;
    this.setPhase(WallSequencePhase.WaitKnees);
  }

  /** 1 フレーム。dt は Clock.scaledDelta。 */
  tick(dt: number): void {
    if (!this.isRunning) return;
    if (!this.deps.isAlive()) {
      // try/finally の finally 相当。全ての早期離脱はここを通る。
      this.stop();
      return;
    }
    this.elapsed += dt;
    switch (this.phase) {
      case WallSequencePhase.WaitKnees: this.tickWaitKnees(); break;
      case WallSequencePhase.PauseAfterKnees: this.tickPause(CombatConstants.WallPauseInputsAfterKnees); break;
      case WallSequencePhase.Split: this.tickSplit(dt); break;
      case WallSequencePhase.PauseAfterSplit: this.tickPause(CombatConstants.WallPauseInputsAfterSplit); break;
      case WallSequencePhase.Gang: this.tickGang(dt); break;
      case WallSequencePhase.Toss:
        this.deps.beginToss?.();
        this.finishNormally();
        break;
      default: break;
    }
  }

  /**
   * 後始末を 1 か所に集約。正常終了・早期離脱・外部中断のどこからでも呼ぶ。
   * `FinishWallSequence()` 相当で、必ず `WallSequenceActive=false` にする。
   */
  stop(): void {
    if (this.phase === WallSequencePhase.Idle && !this.stopped) return;
    this.stopped = true;
    this.session.wallSequenceActive = false;
    this.session.wallPauseActive = false;
    this.clearActiveStrikes();
    this.session.splitPuncher = null;
    this.session.splitPunchAge = -1;
    this.endGang();
    this.endSplit();
    this.setPhase(WallSequencePhase.Done);
  }

  /** Done から Idle へ戻す（次の拘束に備える）。後始末は既に stop() が済ませている。 */
  reset(): void {
    this.stop();
    this.phase = WallSequencePhase.Idle;
    this.elapsed = 0;
    this.stepIndex = 0;
    this.stopped = false;
  }

  private setPhase(phase: WallSequencePhase): void {
    if (this.phase === phase) return;
    this.phase = phase;
    this.elapsed = 0;
    this.deps.log?.(`[WallSequence] ${WallSequencePhase[phase]}`);
  }

  private tickWaitKnees(): void {
    const timeout = CombatConstants.FrontGrappleEscapeSeconds +
      CombatConstants.PinKneeCount * CombatConstants.PinKneeInterval + 4;
    if (!this.kneesDone && this.elapsed < timeout) return;
    this.deps.callAssist?.();
    this.setPhase(WallSequencePhase.PauseAfterKnees);
  }

  private tickPause(inputs: number): void {
    this.session.wallPauseActive = true;
    this.session.limitRemaining = CombatConstants.WallPauseSeconds;
    const needed = Math.ceil(inputs);
    if (this.deps.inputCount() < needed && this.elapsed < CombatConstants.WallPauseSeconds) {
      this.session.limitRemaining = CombatConstants.WallPauseSeconds - this.elapsed;
      return;
    }
    this.session.wallPauseActive = false;
    this.session.limitRemaining = 0;
    if (this.phase === WallSequencePhase.PauseAfterKnees) {
      this.setPhase(WallSequencePhase.Split);
      this.stepIndex = 0;
      this.beginSplitStrike();
    } else {
      this.setPhase(WallSequencePhase.Gang);
      this.gangCycle = 0;
      this.gangSub = 0;
    }
  }

  // --- 二人がかり（腹パン 6 発） ---------------------------------
  private beginSplitStrike(): void {
    const puncher = this.stepIndex % 2 === 0 ? this.deps.assist() : this.deps.captor();
    this.session.splitPuncher = puncher;
    this.session.splitPunchLeft = puncher === this.deps.captor();
    this.session.splitFinisher = false;
    this.session.splitFace = false;
    this.session.splitKick = false;
    this.session.splitPunchAge = 0;
    this.session.splitWindup = CombatConstants.wallSplitHitWindup(this.stepIndex);
    this.strikeTime = 0;
    this.strikeFired = false;
  }

  private tickSplit(dt: number): void {
    const windup = CombatConstants.wallSplitHitWindup(this.stepIndex);
    const interval = CombatConstants.wallSplitHitInterval(this.stepIndex);
    this.strikeTime += dt;
    this.session.splitPunchAge = this.strikeTime;
    if (this.strikeTime >= windup && !this.strikeFired) {
      this.strikeFired = true;
      const puncher = this.stepIndex % 2 === 0 ? this.deps.assist() : this.deps.captor();
      this.deps.fireStrike?.(puncher, WallStrikeKind.Belly, this.stepIndex,
        CombatConstants.wallSplitHitScale(this.stepIndex));
      this.session.splitHits++;
    }
    if (this.strikeTime < interval) return;
    this.session.splitPunchAge = -1;
    this.stepIndex++;
    if (this.stepIndex >= CombatConstants.WallSplitPunchCount) {
      this.session.splitPuncher = null;
      this.session.splitPunchesDone = true;
      if (this.deps.third() !== null) this.deps.prepareThird?.();
      this.setPhase(WallSequencePhase.PauseAfterSplit);
    } else {
      this.beginSplitStrike();
    }
  }

  // --- 3 人がかり（15 発、2 枠同時） -----------------------------
  private tickGang(dt: number): void {
    const third = this.deps.third();
    if (!third || !third.isAlive || !third.isActive) {
      this.endGang();
      this.finishNormally();
      return;
    }
    if (this.gangCycle === 0 && this.gangSub === 0 && this.activeStrikes.length === 0) {
      this.session.gangTotal = CombatConstants.WallGangStrikes;
      this.session.gangHits = 0;
      this.session.gangActive = true;
      this.session.gangStriker = third;
      this.session.suppressRemaining = 999;
      this.deps.log?.('[WallGang] begin');
    }

    // 同時発の 2 本が走っている間は、両方が終わるまで次の発を始めない（枠を奪わない）。
    if (this.activeStrikes.length > 0) {
      let allDone = true;
      for (const runner of this.activeStrikes) {
        if (!this.advanceStrike(runner, dt)) allDone = false;
      }
      if (allDone) {
        this.clearActiveStrikes();
        this.advanceGangSub();
      }
      return;
    }

    const cycles = Math.min(SIMULTANEOUS.length, ASSIST_KINDS.length, CAPTOR_KINDS.length, THIRD_KINDS.length);
    if (this.gangCycle >= cycles) {
      this.endGang();
      this.finishNormally();
      return;
    }

    const base = this.gangCycle * 3;
    if (SIMULTANEOUS[this.gangCycle]) {
      const assist = this.deps.assist();
      const holder = this.deps.captor();
      if (!assist || !holder) {
        this.advanceGangSub();
        return;
      }
      this.session.gangStriker = assist;
      // 2 枠が空くまで待つ。空きが無ければ既存の打撃は壊さない。
      if (this.session.strikes.filter((s) => s.puncher !== null && s.age >= 0).length >= 2 &&
          this.elapsed < STRIKE_SLOT_WAIT_SECONDS) {
        return;
      }
      this.activeStrikes.push(new StrikeRunner(assist, ASSIST_KINDS[this.gangCycle]!, base));
      this.activeStrikes.push(new StrikeRunner(holder, CAPTOR_KINDS[this.gangCycle]!, base + 1));
      return;
    }

    const kind = this.gangSub === 0 ? ASSIST_KINDS[this.gangCycle]!
      : this.gangSub === 1 ? CAPTOR_KINDS[this.gangCycle]!
      : THIRD_KINDS[this.gangCycle]!;
    const puncher = this.gangSub === 0 ? this.deps.assist()
      : this.gangSub === 1 ? this.deps.captor() : third;
    this.session.gangStriker = puncher;
    this.activeStrikes.push(new StrikeRunner(puncher, kind, base + this.gangSub));
  }

  private advanceGangSub(): void {
    this.gangSub++;
    if (this.gangSub >= 3) {
      this.gangSub = 0;
      this.gangCycle++;
    }
  }

  /** 1 発を進める。true = 完了。 */
  private advanceStrike(runner: StrikeRunner, dt: number): boolean {
    if (!runner.puncher) return true;
    if (!runner.claimed) {
      const strike = this.session.claimStrike(runner.puncher);
      if (!strike) {
        // 枠が埋まっている。待つ（既存の打撃は奪わない）。
        return this.elapsed >= STRIKE_SLOT_WAIT_SECONDS;
      }
      strike.left = runner.puncher === this.deps.captor();
      strike.finisher = runner.kind === WallStrikeKind.Knee || runner.kind === WallStrikeKind.Kick;
      strike.kick = runner.kind === WallStrikeKind.Kick;
      strike.face = runner.kind === WallStrikeKind.Face;
      strike.windup = runner.windup;
      strike.age = 0;
      runner.claimed = true;
    }

    runner.elapsed += dt;
    const strike = this.session.strikeOf(runner.puncher);
    if (strike) strike.age = runner.elapsed;

    if (runner.elapsed >= runner.windup && !runner.fired) {
      runner.fired = true;
      const finisher = runner.index === CombatConstants.WallGangStrikes - 1;
      this.deps.fireStrike?.(runner.puncher, runner.kind, runner.index, this.scaleOf(runner.kind, finisher));
      this.session.gangHits++;
    }
    if (runner.elapsed < runner.interval) return false;
    // この枠を空けてから次へ（Unity 版の finally { strike.Reset(); } 相当）。
    const mine = this.session.strikeOf(runner.puncher);
    if (mine) mine.reset();
    return true;
  }

  private scaleOf(kind: WallStrikeKind, finisher: boolean): number {
    switch (kind) {
      case WallStrikeKind.Knee: return finisher ? CombatConstants.WallGangFinisherScale : CombatConstants.WallGangKneeScale;
      case WallStrikeKind.Kick: return finisher ? CombatConstants.WallGangFinisherScale : CombatConstants.WallGangKickScale;
      case WallStrikeKind.Face: return CombatConstants.WallGangFaceScale;
      default: return CombatConstants.WallGangBellyScale;
    }
  }

  private clearActiveStrikes(): void {
    this.activeStrikes = [];
    this.session.clearStrikes();
  }

  private finishNormally(): void {
    // try/finally の finally と同じ入口。ここからも stop() を通す。
    this.stop();
  }

  private endSplit(): void {
    this.session.clearSplit();
    this.deps.releaseAssist?.();
  }

  private endGang(): void {
    const third = this.session.third;
    this.session.gangActive = false;
    this.session.gangStriker = null;
    this.session.suppressRemaining = 0;
    if (third) this.deps.releaseThird?.();
    this.session.third = null;
  }
}

/** 壁押さえつけの段階進行（Grab→…→Held）。Unity の TickWallTransition 相当。 */
export class WallTransition {
  constructor(private readonly session: GrappleSession) {}

  tick(dt: number): boolean {
    const stage = this.session.stage;
    if (stage === WallStage.None) return false;
    if (stage >= WallStage.Held) return true;
    const seconds = WALL_STAGE_SECONDS[stage] ?? 0.52;
    this.session.advanceStage(dt, seconds);
    if (this.session.stageProgress < 1) return false;
    if (stage === WallStage.Restrain) {
      this.session.setStage(WallStage.Held);
      this.session.heldSeconds = 0;
      return true;
    }
    this.session.setStage((stage + 1) as WallStage);
    return false;
  }
}
