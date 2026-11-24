import { ArenaPlan } from '../core/ArenaPlan';
import { CombatConstants } from '../core/CombatConstants';
import { EnemyRoleRules, RoleCandidate } from '../core/EnemyRole';
import type { EnemyActor } from './types';

/** ベルト（横スクロール）の敌の出し方。Unity の EncounterDirector 相当。 */
export interface EncounterSettings {
  arenaPitch: number;
  wavesPerArena: number;
  arenaCount: number;
  entryOffset: number;
  waveGap: number;
  minSpawnGap: number;
  entrySpacing: number;
  bodyDropDelay: number;
}

export const DEFAULT_ENCOUNTER_SETTINGS: EncounterSettings = {
  arenaPitch: 10,
  wavesPerArena: 2,
  arenaCount: 5,
  entryOffset: 2,
  waveGap: 1.2,
  minSpawnGap: 2.2,
  entrySpacing: 1.6,
  bodyDropDelay: 0.7,
};

export interface EncounterHooks {
  getPlayerX(): number;
  getPlayerZ(): number;
  screenHalfWidth(z: number): number;
  /** 屏幕（ベルト）のロック状態を切り替える。 */
  lockAt?(centerX: number, initial?: boolean): void;
  releaseLock?(): void;
  isLocked?(): boolean;
  reviveEnemy(enemy: EnemyActor, x: number, z: number): void;
  /** 背丈（m）。EnemyFigure が無ければ 0。 */
  heightOf(enemy: EnemyActor): number;
  nameOf(enemy: EnemyActor): string;
  playTell?(x: number): void;
  playBodyDrop?(x: number, z: number): void;
  log?(message: string): void;
}

/**
 * 敵は画面の右端の少し外から入ってくる。倒しているあいだは画面がロックされ、
 * 全滅させるとロックが外れて右へ進める。
 */
export class EncounterDirector {
  settings: EncounterSettings;
  private hooks: EncounterHooks;
  private enemies: EnemyActor[] = [];
  private pendingDrops: Array<{ x: number; z: number }> = [];
  private pendingTimer = 0;
  private nextWaveAt = 0;
  private currentArena = 0;
  private runSeconds = 0;
  private collected = false;

  kills = 0;
  alive = 0;
  wave = 0;
  lastEntryX = 0;
  stageCleared = false;
  clearTimeSeconds = 0;
  bestClearSeconds = 0;
  maxWaves = 10;
  recordEligible = true;

  constructor(hooks: EncounterHooks, settings: Partial<EncounterSettings> = {}) {
    this.hooks = hooks;
    this.settings = { ...DEFAULT_ENCOUNTER_SETTINGS, ...settings };
    this.maxWaves = this.settings.arenaCount * this.settings.wavesPerArena;
  }

  get plan(): ArenaPlan {
    return new ArenaPlan(-6, this.settings.arenaPitch, this.settings.wavesPerArena, this.settings.arenaCount);
  }

  get cleared(): boolean { return this.alive <= 0; }
  get nextTriggerX(): number {
    return this.plan.centerOf(this.plan.arenaOf(this.wave + 1)) - this.settings.entryOffset;
  }
  get nextWaveRemaining(): number { return Math.max(0, this.nextWaveAt - this.time); }
  get arenaMovingNext(): boolean { return this.plan.arenaOf(this.wave + 1) !== this.currentArena; }

  /** 現在時刻（scaled）。Unity の Time.time 相当。 */
  time = 0;

  /** シーンの敵を 1 度だけ集める。 */
  collect(enemies: readonly EnemyActor[]): void {
    if (this.collected) return;
    this.collected = true;
    this.enemies = [...enemies];
    this.alive = this.countAlive();
    this.assignRoles();
    this.hooks.log?.(`[Encounter] 敵 ${this.enemies.length} 体を登録`);
    if (this.enemies.length > 0) {
      this.currentArena = 0;
      this.hooks.lockAt?.(this.plan.centerOf(0), true);
      this.enterWave(0);
    }
  }

  private assignRoles(): void {
    const candidates = this.enemies.map((e) =>
      new RoleCandidate(this.hooks.nameOf(e), this.hooks.heightOf(e), false));
    for (let i = 0; i < this.enemies.length; i++) {
      const role = EnemyRoleRules.roleOf(candidates, i);
      this.enemies[i]!.role = role;
      this.hooks.log?.(`[Encounter] role ${candidates[i]!.name}=${role}`);
    }
  }

  private countAlive(): number {
    return this.enemies.filter((e) => e.isAlive && e.isActive).length;
  }

  onDied(_enemy: EnemyActor): void {
    this.kills++;
    this.alive = this.countAlive();
    const x = this.hooks.getPlayerX();
    this.pendingDrops.push({ x, z: this.hooks.getPlayerZ() });
    this.pendingTimer = this.settings.bodyDropDelay;
    this.hooks.log?.(`[Encounter] 撃破 ${this.kills} 残り ${this.alive} t=${this.time.toFixed(2)}`);
    if (this.alive <= 0) {
      this.nextWaveAt = this.time + this.settings.waveGap;
      if (this.plan.arenaOf(this.wave + 1) !== this.currentArena) this.hooks.releaseLock?.();
    }
  }

  /** Unity 版 Update 相当。 */
  tick(dt: number, time: number): void {
    this.time = time;
    if (!this.stageCleared) this.runSeconds += dt;

    if (this.pendingDrops.length > 0) {
      this.pendingTimer -= dt;
      if (this.pendingTimer <= 0) {
        for (const p of this.pendingDrops) this.hooks.playBodyDrop?.(p.x, p.z);
        this.pendingDrops.length = 0;
      }
    }

    if (this.alive > 0 || this.stageCleared) return;

    if (this.maxWaves > 0 && this.wave >= this.maxWaves) {
      this.stageCleared = true;
      this.clearTimeSeconds = this.runSeconds;
      if (this.recordEligible && (this.bestClearSeconds <= 0 || this.clearTimeSeconds < this.bestClearSeconds)) {
        this.bestClearSeconds = this.clearTimeSeconds;
      }
      this.hooks.releaseLock?.();
      this.hooks.log?.(`[Encounter] STAGE CLEAR ${this.wave} 波 / 撃破 ${this.kills}`);
      return;
    }

    const nextArena = this.plan.arenaOf(this.wave + 1);
    const sameArena = nextArena === this.currentArena && (this.hooks.isLocked?.() ?? true);
    if (sameArena) {
      if (this.time < this.nextWaveAt) return;
    } else {
      const entryX = this.plan.centerOf(nextArena) - this.settings.entryOffset;
      if (this.hooks.getPlayerX() < entryX) return;
      this.hooks.lockAt?.(this.plan.centerOf(nextArena));
      this.currentArena = nextArena;
    }
    this.enterWave(nextArena);
  }

  private enterWave(arena: number): void {
    const playerX = this.hooks.getPlayerX();
    const z = this.hooks.getPlayerZ();
    const half = this.hooks.screenHalfWidth(z);
    const edge = Math.max(
      this.plan.entryX(arena, half, 1),
      playerX + Math.max(0, this.settings.minSpawnGap),
    );

    for (let i = 0; i < this.enemies.length; i++) {
      const enemy = this.enemies[i]!;
      this.hooks.reviveEnemy(enemy, edge + i * this.settings.entrySpacing, z);
    }
    this.hooks.playTell?.(edge);
    this.alive = this.countAlive();
    this.wave++;
    this.lastEntryX = edge;
    this.hooks.log?.(`[Encounter] wave ${this.wave} arena ${arena} 敵 ${this.alive} 体が右端から入場 (x=${edge.toFixed(1)})`);
  }
}

/** 敵の役割を背丈（同点は名前）で決める（Unity 版と同じルールを core から使う）。 */
export type { EnemyRole } from '../core/EnemyRole';
export { CombatConstants };
