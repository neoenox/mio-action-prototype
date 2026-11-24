import { clamp01, lerp, moveTowards, smoothstep } from '../core/math';
import { GrappleKind, type EnemyActor } from './types';

/**
 * 二人がかり・3 人がかりの「いまの 1 発」。
 * 2 枠を持ち、満杯のときは null を返す（Unity 版のバグ修正を維持）。
 */
export class SplitStrike {
  puncher: EnemyActor | null = null;
  age = -1;
  windup = 0.16;
  left = false;
  face = false;
  kick = false;
  finisher = false;

  isStriking(who: EnemyActor | null): boolean {
    return who !== null && this.puncher === who && this.age >= 0;
  }

  reset(): void {
    this.puncher = null;
    this.age = -1;
    this.windup = 0.16;
    this.left = false;
    this.face = false;
    this.kick = false;
    this.finisher = false;
  }
}

/** 壁拘束の段階。Unity 版の 12 段階をそのまま維持する。 */
export enum WallStage {
  None,
  Grab,
  Rise,
  Throw,
  Slam,
  Lean,
  Regrip,
  Restrain,
  Held,
  TossWindup,
  TossAir,
  TossLand,
}

/** 段階ごとの長さ（秒）。Restrain は既定の 0.52。 */
export const WALL_STAGE_SECONDS: Readonly<Partial<Record<WallStage, number>>> = {
  [WallStage.Grab]: 0.22,
  [WallStage.Rise]: 0.46,
  [WallStage.Throw]: 0.30,
  [WallStage.Slam]: 0.13,
  [WallStage.Lean]: 0.57,
  [WallStage.Regrip]: 0.28,
  [WallStage.Restrain]: 0.52,
  [WallStage.TossWindup]: 0.26,
};

/** 1 回の拘束の状態。Unity の GrappleSession 相当（UnityEngine 非依存）。 */
export class GrappleSession {
  kind: GrappleKind = GrappleKind.Back;
  captor: EnemyActor | null = null;
  limitRemaining = 0;
  heldSeconds = 0;
  suppressRemaining = 0;
  isWall = false;
  wallSequenceActive = false;
  wallPauseActive = false;

  private _stage: WallStage = WallStage.None;
  private _stageProgress = 0;
  private _stageElapsed = 0;

  get stage(): WallStage { return this._stage; }
  get stageProgress(): number { return this._stageProgress; }
  get stageElapsed(): number { return this._stageElapsed; }

  // --- 二体がかり（Y 字拘束） ---------------------------------
  assist: EnemyActor | null = null;
  assistCandidate: EnemyActor | null = null;
  splitHits = 0;
  splitStarted = false;
  splitReady = false;
  pinballActive = false;
  splitAssistAge = 0;
  splitPunchesDone = false;

  private readonly _strikes: SplitStrike[] = [new SplitStrike(), new SplitStrike()];
  get strikes(): readonly SplitStrike[] { return this._strikes; }

  // 互換アクセサ（Unity 版の 0 枠目の名前をそのまま使うコード向け）
  get splitPuncher(): EnemyActor | null { return this._strikes[0]!.puncher; }
  set splitPuncher(v: EnemyActor | null) { this._strikes[0]!.puncher = v; }
  get splitPunchAge(): number { return this._strikes[0]!.age; }
  set splitPunchAge(v: number) { this._strikes[0]!.age = v; }
  get splitWindup(): number { return this._strikes[0]!.windup; }
  set splitWindup(v: number) { this._strikes[0]!.windup = v; }
  get splitPunchLeft(): boolean { return this._strikes[0]!.left; }
  set splitPunchLeft(v: boolean) { this._strikes[0]!.left = v; }
  get splitFace(): boolean { return this._strikes[0]!.face; }
  set splitFace(v: boolean) { this._strikes[0]!.face = v; }
  get splitKick(): boolean { return this._strikes[0]!.kick; }
  set splitKick(v: boolean) { this._strikes[0]!.kick = v; }
  get splitFinisher(): boolean { return this._strikes[0]!.finisher; }
  set splitFinisher(v: boolean) { this._strikes[0]!.finisher = v; }

  /** この人がいま打っている枠（打っていなければ null）。 */
  strikeOf(who: EnemyActor | null): SplitStrike | null {
    for (const s of this._strikes) if (s.isStriking(who)) return s;
    return null;
  }

  /**
   * 空いている枠を取って 1 発を始める。満杯なら null で、既存の枠は一切触らない。
   */
  claimStrike(who: EnemyActor | null): SplitStrike | null {
    for (const s of this._strikes) {
      if (s.puncher !== null && s.age >= 0) continue;
      s.reset();
      s.puncher = who;
      s.age = 0;
      return s;
    }
    return null;
  }

  clearStrikes(): void {
    for (const s of this._strikes) s.reset();
  }

  // --- 3 人がかり（壁拘束の最後の段階） -------------------------
  third: EnemyActor | null = null;
  gangActive = false;
  gangStriker: EnemyActor | null = null;
  gangHits = 0;
  gangTotal = 0;
  private _gangWeight = 0;
  get gangWeight(): number { return this._gangWeight; }

  /** 壁の段階を進める。前の段階の進捗は捨てる。 */
  setStage(stage: WallStage): void {
    this._stage = stage;
    this._stageProgress = 0;
    this._stageElapsed = 0;
  }

  /** 今の段階の経過を進める。seconds<=0 なら進捗 1。 */
  advanceStage(deltaSeconds: number, seconds: number): void {
    this._stageElapsed += deltaSeconds;
    this._stageProgress = seconds > 0 ? clamp01(this._stageElapsed / seconds) : 1;
  }

  clearWall(): void {
    this.isWall = false;
    this.wallSequenceActive = false;
    this.wallPauseActive = false;
    this._stage = WallStage.None;
    this._stageProgress = 0;
    this._stageElapsed = 0;
  }

  clearSplit(): void {
    this.assist = null;
    this.assistCandidate = null;
    this.clearStrikes();
    this.splitHits = 0;
    this.splitPunchesDone = false;
    this.clearGang();
    this.splitStarted = false;
    this.splitReady = false;
    this.splitAssistAge = 0;
    this.resetSplitWeight();
  }

  // --- 壁の見た目（純粋関数） ----------------------------------
  get isWallTransition(): boolean { return this.isWall && this._stage !== WallStage.Held; }
  get isWallToss(): boolean { return this.isWall && this._stage >= WallStage.TossWindup; }

  get wallRestraintWeight(): number {
    if (this._stage === WallStage.Restrain) return smoothstep(this._stageProgress);
    if (this._stage === WallStage.Held || this._stage === WallStage.TossWindup) return 1;
    return 0;
  }

  get wallTossLower(): number { return smoothstep(this._stageProgress); }

  get wallGripWeight(): number {
    if (this._stage === WallStage.Regrip) return smoothstep(this._stageProgress);
    return this.wallRestraintWeight > 0 || this._stage === WallStage.Restrain ? 1 : 0;
  }

  get wallSlideWeight(): number {
    switch (this._stage) {
      case WallStage.Lean: return smoothstep(this._stageProgress);
      case WallStage.Regrip: return 1;
      case WallStage.Restrain: return 1 - this.wallRestraintWeight;
      default: return 0;
    }
  }

  get wallCaptorTravel(): number {
    switch (this._stage) {
      case WallStage.Throw: return 0.55 * this._stageProgress;
      case WallStage.Slam:
      case WallStage.Lean: return 0.55;
      case WallStage.Regrip: return lerp(0.55, 0.90, this.wallGripWeight);
      case WallStage.Restrain: return lerp(0.90, 1, this.wallRestraintWeight);
      case WallStage.Held:
      case WallStage.TossWindup:
      case WallStage.TossAir:
      case WallStage.TossLand: return 1;
      default: return 0;
    }
  }

  // --- 3 人がかりの見た目 -------------------------------------
  tickGangWeight(dt: number): void {
    this._gangWeight = moveTowards(this._gangWeight, this.gangActive ? 1 : 0, dt / 0.35);
  }

  clearGang(): void {
    this.third = null;
    this.gangActive = false;
    this.gangStriker = null;
    this.gangHits = 0;
    this.gangTotal = 0;
    this._gangWeight = 0;
  }

  // --- 二体がかりの見た目 -------------------------------------
  private _splitWeight = 0;
  private _splitWeightPhase = 0;
  private _splitWeightR = 0;
  private _splitWeightPhaseR = 0;
  private _splitCaptorBeat = 0;
  private _splitClearAge = 0;

  get splitWeight(): number { return this._splitWeight; }
  get splitWeightR(): number { return this._splitWeightR; }

  static readonly SplitClearSeconds = 0.30;
  static readonly SplitCaptorBeatSeconds = 0.35;

  get isSplitActive(): boolean {
    return this.isWall && this._stage === WallStage.Held && this.assist !== null &&
      this.splitReady && this.assist.isAlive && this.assist.isActive;
  }

  get isSplitCaptorShifting(): boolean {
    return this.isSplitActive && this._splitCaptorBeat >= GrappleSession.SplitCaptorBeatSeconds;
  }

  /** 右腕(0.55s) → 一拍(0.35s) → 左腕(0.60s)。SmoothStep で入り抜きを滑らかにする。 */
  tickSplitWeight(dt: number): void {
    const active = this.isSplitActive;
    this._splitClearAge = active ? this._splitClearAge + dt : 0;
    const reach = active && this._splitClearAge >= GrappleSession.SplitClearSeconds;
    this._splitWeightPhaseR = moveTowards(this._splitWeightPhaseR, reach ? 1 : 0, dt / 0.55);
    this._splitWeightR = smoothstep(this._splitWeightPhaseR);

    if (!active) this._splitCaptorBeat = 0;
    else if (this._splitWeightPhaseR >= 1) this._splitCaptorBeat += dt;

    const target = this.isSplitCaptorShifting ? 1 : 0;
    this._splitWeightPhase = moveTowards(this._splitWeightPhase, target, dt / 0.60);
    this._splitWeight = smoothstep(this._splitWeightPhase);
  }

  resetSplitWeight(): void {
    this._splitWeight = 0;
    this._splitWeightPhase = 0;
    this._splitWeightR = 0;
    this._splitWeightPhaseR = 0;
    this._splitCaptorBeat = 0;
    this._splitClearAge = 0;
  }
}
