import { clamp, clamp01, moveTowards, smoothstep } from '../core/math';
import { Prng } from '../core/prng';
import { CombatConstants } from '../core/CombatConstants';
import {
  EnemyPhase, PunchZone,
  type EnemyActor,
} from './types';
import { EnemyRole } from '../core/EnemyRole';
import { GrappleKind } from '../core/GrappleResistance';

/** 敵が選ぶ攻撃の種類（Unity 版と同じ数値）。 */
export enum AttackId {
  Jab = 0,
  Heavy = 1,
  Break = 2,
  Grab = 3,
}

export interface EnemySettings {
  maxHp: number;
  moveSpeed: number;
  attackCooldown: number;
  jabSeconds: number;
  windupSeconds: number;
  heavySeconds: number;
  breakSeconds: number;
  grabSeconds: number;
  attackRange: number;
  jabRange: number;
  heavyRange: number;
  breakRange: number;
  grabRange: number;
  strikeTolerance: number;
  jabDamage: number;
  heavyDamage: number;
  breakDamage: number;
  grabDamage: number;
  jabFaceChance: number;
  heavyFaceChance: number;
  feintChance: number;
  feintDelaySeconds: number;
  punchHitRadius: number;
  recoverSeconds: number;
  staggerSeconds: number;
  riseSeconds: number;
  waitingRingScale: number;
  strafeSeconds: number;
  strafeSpeedScale: number;
  strafeCloseInScale: number;
  strafeCloseInChance: number;
  minPlayerGap: number;
}

export const DEFAULT_ENEMY_SETTINGS: EnemySettings = {
  maxHp: 60,
  moveSpeed: 2.0,
  attackCooldown: 0.9,
  jabSeconds: 0.28,
  windupSeconds: 0.4,
  heavySeconds: 0.62,
  breakSeconds: 0.44,
  grabSeconds: 0.34,
  attackRange: 1.6,
  jabRange: 1.25,
  heavyRange: 1.45,
  breakRange: 1.25,
  grabRange: 1.2,
  strikeTolerance: 0.18,
  jabDamage: 8,
  heavyDamage: 16,
  breakDamage: 11,
  grabDamage: 2,
  jabFaceChance: 0.35,
  heavyFaceChance: 0.25,
  feintChance: 0.0,
  feintDelaySeconds: 0.4,
  punchHitRadius: 0.32,
  recoverSeconds: 0.55,
  staggerSeconds: 0.35,
  riseSeconds: 0.7,
  waitingRingScale: 1.2,
  strafeSeconds: 0.5,
  strafeSpeedScale: 0.7,
  strafeCloseInScale: 0.85,
  strafeCloseInChance: 0.4,
  minPlayerGap: 0,
};

/** 攻撃結果。logic はダメージ値を返すだけで、適用先は呼び側が決める。 */
export interface AttackOutcome {
  readonly attacker: EnemyBrain;
  readonly attack: AttackId;
  readonly zone: PunchZone;
  readonly damage: number;
  readonly hit: boolean;
  readonly isGrab: boolean;
  readonly isTagPunch: boolean;
}

export interface EnemyBrainHooks {
  /** 拳のアンカー。rig が実体を持ち、logic はこれを呼ぶだけ（直接 rig を見ない）。 */
  getPunchAnchor(zone: PunchZone): { x: number; y: number; z: number } | null;
  /** プレイヤーの懐の座標（アンカー比較用）。 */
  getPlayerPosition(): { x: number; y: number; z: number };
  getPlayerForward(): { x: number; y: number; z: number };
  isPlayerDown(): boolean;
  isPlayerDead(): boolean;
  isPlayerGrappled(): boolean;
  canGrab(): boolean;
  onAttack?(outcome: AttackOutcome): void;
  onStagger?(brain: EnemyBrain): void;
  onDied?(brain: EnemyBrain): void;
}

/**
 * EnemyBrain の状態機械。Unity の Coroutine / Update を明示的な phase + elapsed に変換したもの。
 * 現在時刻は Clock（scaled）を受け取る。unscaled は演出側が別に持つ。
 */
export class EnemyBrain implements EnemyActor {
  readonly id: string;
  readonly actorInfo: EnemyActor;
  settings: EnemySettings;
  role: EnemyRole = EnemyRole.Striker;
  hooks: EnemyBrainHooks;
  private rng: Prng;

  phase: EnemyPhase = EnemyPhase.Chase;
  hp: number;
  isAlive = true;
  isHolding = false;
  isPinning = false;
  isBearHugging = false;
  isWallAssisting = false;
  canPin = false;
  zone: PunchZone = PunchZone.Face;
  forceZone: PunchZone | null = null;

  currentAttack: AttackId = AttackId.Jab;
  windupProgress = 0;
  staggerProgress = 0;
  staggerWeight = 1;
  staggerElapsed = 0;

  /** 現在時刻（scaled）。Unity の Time.time 相当。外部から渡す。 */
  time = 0;

  private timer = 0;
  private attackReadyAt = 0;
  private grabReadyAt = 0;
  private lastAttack: AttackId | null = null;
  private repeatCount = 0;
  private feinted = false;
  private runWindup = 0;
  private runRange = 0;
  private runDamage = 0;
  private runFromThrow = false;
  private tagPunchPending = false;
  private tagPunchSwing = false;
  private noFeint = false;
  private healWait = 0;

  constructor(
    id: string,
    actor: EnemyActor,
    hooks: EnemyBrainHooks,
    settings: Partial<EnemySettings> = {},
    seed = 0x2f6e2b1,
  ) {
    this.id = id;
    this.actorInfo = actor;
    this.hooks = hooks;
    this.settings = { ...DEFAULT_ENEMY_SETTINGS, ...settings };
    this.hp = this.settings.maxHp;
    this.rng = new Prng(seed);
  }

  get isActive(): boolean { return this.actorInfo.isActive; }
  get moveSpeed(): number {
    return this.settings.moveSpeed * (this.role === EnemyRole.Striker ? 1.05 : 1);
  }
  get position(): { x: number; y: number; z: number } { return this.actorInfo.position; }
  get engageDistance(): number {
    return clamp(this.settings.attackRange * 0.85, 0.1, 1.03);
  }
  get attackCooldownRemaining(): number { return Math.max(0, this.attackReadyAt - this.time); }
  get windupSeconds(): number {
    return Math.max(0.05, this.runWindup);
  }

  setNoFeint(value: boolean): void { this.noFeint = value; }
  setForceZone(zone: PunchZone | null): void { this.forceZone = zone; }

  queueGrab(): void {
    this.attackReadyAt = this.time;
  }

  holdFire(seconds: number): void {
    this.attackReadyAt = Math.max(this.attackReadyAt, this.time + Math.max(0, seconds));
  }

  /** タッグ技を予約する。発注時に渡した保持秒は「当たった瞬間」に使う。 */
  prepareTagPunch(zone: PunchZone, damage: number, holdSeconds = 0): void {
    this.forceZone = zone;
    this.tagPunchPending = true;
    this.tagPunchSwing = true;
    this.runDamage = damage;
    this.runRange = this.settings.attackRange;
    this.healWait = holdSeconds;
    this.phase = EnemyPhase.Windup;
    this.runWindup = Math.max(0.05, this.settings.jabSeconds * 0.8);
    this.timer = this.runWindup;
    this.windupProgress = 0;
    this.zone = zone;
  }

  /** Phase を明示的に立てる。演出側の開始を想定。 */
  beginWindup(attack: AttackId, fromThrow = false): void {
    const scale = this.role === EnemyRole.Striker ? 0.82 : 1;
    let base = this.settings.windupSeconds;
    switch (attack) {
      case AttackId.Jab: base = this.settings.jabSeconds; break;
      case AttackId.Heavy: base = this.settings.heavySeconds; break;
      case AttackId.Break: base = this.settings.breakSeconds; break;
      case AttackId.Grab: base = this.settings.grabSeconds; break;
    }
    this.currentAttack = attack;
    this.runWindup = fromThrow ? 0.12 : Math.max(0.05, base * scale);
    this.runRange = this.rangeOf(attack);
    this.runDamage = this.damageOf(attack);
    this.runFromThrow = fromThrow;
    this.timer = this.runWindup;
    this.windupProgress = 0;
    this.zone = this.forceZone ?? (attack === AttackId.Break
      ? PunchZone.Body
      : attack === AttackId.Jab
        ? (this.rng.next() < this.settings.jabFaceChance ? PunchZone.Face : PunchZone.Body)
        : attack === AttackId.Heavy
          ? (this.rng.next() < this.settings.heavyFaceChance ? PunchZone.Face : PunchZone.Body)
          : PunchZone.Face);

    if (this.lastAttack === attack) this.repeatCount++;
    else { this.lastAttack = attack; this.repeatCount = 1; }
    this.phase = EnemyPhase.Windup;
  }

  /** Unity 版 Update の 1 フレーム。dt は Clock.scaledDelta。 */
  tick(dt: number, time: number): void {
    this.time = time;
    if (!this.isAlive || this.phase === EnemyPhase.Dead) return;

    if (this.healWait > 0) this.healWait = Math.max(0, this.healWait - dt);

    if (this.phase === EnemyPhase.Stagger) {
      this.tickStagger(dt);
      return;
    }

    const playerDown = this.hooks.isPlayerDown() || this.hooks.isPlayerDead();

    switch (this.phase) {
      case EnemyPhase.Chase:
        this.tickChase(dt, playerDown);
        break;
      case EnemyPhase.Strafe:
        this.tickStrafe(dt, playerDown);
        break;
      case EnemyPhase.Windup:
        this.tickWindup(dt, playerDown);
        break;
      case EnemyPhase.Attack:
        // 判定は 1 フレームだけ。ここで必ず Recover へ移る。
        this.strike();
        break;
      case EnemyPhase.Recover:
        this.timer -= dt;
        if (this.timer <= 0) this.endRecover();
        break;
      default:
        break;
    }
  }

  private distanceToPlayer(): number {
    const p = this.hooks.getPlayerPosition();
    return Math.hypot(p.x - this.position.x, p.z - this.position.z);
  }

  private tickChase(dt: number, playerDown: boolean): void {
    if (playerDown) return;
    const distance = this.distanceToPlayer();
    if (distance > this.engageDistance + 0.001) {
      this.moveTowardPlayer(dt, this.moveSpeed);
      return;
    }
    if (!this.tryBeginAttack(distance)) this.enterStrafe();
  }

  private tickStrafe(dt: number, playerDown: boolean): void {
    this.timer -= dt;
    if (playerDown) return;
    const distance = this.distanceToPlayer();
    if (distance <= this.engageDistance * 1.02 && this.tryBeginAttack(distance)) return;
    if (this.attackCooldownRemaining <= 0) {
      this.phase = EnemyPhase.Chase;
      return;
    }
    if (distance > this.engageDistance * 1.45) {
      this.phase = EnemyPhase.Chase;
      return;
    }
    const error = distance - this.engageDistance;
    if (Math.abs(error * 2.5) > 0.06) {
      this.moveTowardPlayer(dt, this.moveSpeed * this.settings.strafeSpeedScale, clamp(error * 2.5, -1, 1));
    }
    if (this.timer <= 0) this.enterStrafe();
  }

  private enterStrafe(): void {
    this.phase = EnemyPhase.Strafe;
    this.timer = Math.max(0.1, this.settings.strafeSeconds) *
      this.rng.range(0.75, 1.25);
  }

  private moveTowardPlayer(dt: number, speed: number, scale = 1): void {
    const p = this.hooks.getPlayerPosition();
    const dx = p.x - this.position.x;
    const dz = p.z - this.position.z;
    const len = Math.hypot(dx, dz);
    if (len < 1e-4) return;
    // 1 ラインなので Z の移動は小さく、X が主軸（Unity 版の Move 相当）。
    this.position.x = moveTowards(this.position.x, this.position.x + (dx / len) * speed * scale * dt, speed * dt);
    this.position.z = moveTowards(this.position.z, this.position.z + (dz / len) * speed * scale * dt, speed * dt);
  }

  private tryBeginAttack(distance: number): boolean {
    if (this.attackCooldownRemaining > 0) return false;
    const id = this.selectAttack(distance);
    if (id === null) return false;
    this.beginWindup(id);
    return true;
  }

  /** 距離と乱数で攻撃を選ぶ。直前と同じ攻撃を避け、3 連続を作らない。 */
  selectAttack(distance: number): AttackId | null {
    const pick = (excludeLast: boolean): AttackId | null => {
      const ids: AttackId[] = [];
      const weights: number[] = [];
      for (let i = 0; i < 4; i++) {
        const id = i as AttackId;
        if (distance > this.rangeOf(id)) continue;
        if (id === AttackId.Grab && (this.time < this.grabReadyAt || this.hooks.isPlayerGrappled())) continue;
        if (excludeLast && this.lastAttack === id && this.repeatCount >= 1) continue;
        ids.push(id);
        weights.push(this.weightOf(id));
      }
      const total = weights.reduce((a, b) => a + b, 0);
      if (total <= 0 || ids.length === 0) return null;
      let roll = this.rng.next() * total;
      for (let i = 0; i < ids.length; i++) {
        roll -= weights[i]!;
        if (roll <= 0) return ids[i]!;
      }
      return ids[ids.length - 1]!;
    };

    const first = pick(true);
    if (first !== null) return first;
    if (this.lastAttack !== null && this.repeatCount >= 2) return null;
    return pick(false);
  }

  private weightOf(id: AttackId): number {
    switch (this.role) {
      case EnemyRole.Grappler:
        return id === AttackId.Jab ? 0.8 : id === AttackId.Heavy ? 1.0 : id === AttackId.Break ? 0.9 : 1.6;
      case EnemyRole.Striker:
        return id === AttackId.Jab ? 1.6 : id === AttackId.Heavy ? 1.2 : id === AttackId.Break ? 0.7 : 0.25;
      default:
        return id === AttackId.Jab ? 0.9 : id === AttackId.Heavy ? 0.5 : id === AttackId.Break ? 0.5 : 0.4;
    }
  }

  private rangeOf(id: AttackId): number {
    const s = this.settings;
    switch (id) {
      case AttackId.Jab: return Math.min(s.jabRange > 0 ? s.jabRange : s.attackRange, 1.25);
      case AttackId.Heavy: return Math.min(s.heavyRange > 0 ? s.heavyRange : s.attackRange, 1.45);
      case AttackId.Break: return Math.min(s.breakRange > 0 ? s.breakRange : s.attackRange, 1.25);
      case AttackId.Grab: return Math.max(s.grabRange > 0 ? s.grabRange : s.attackRange, this.engageDistance);
      default: return s.attackRange;
    }
  }

  private damageOf(id: AttackId): number {
    const s = this.settings;
    switch (id) {
      case AttackId.Jab: return s.jabDamage;
      case AttackId.Heavy: return s.heavyDamage;
      case AttackId.Break: return s.breakDamage;
      case AttackId.Grab: return s.grabDamage;
      default: return 0;
    }
  }

  private tickWindup(dt: number, playerDown: boolean): void {
    this.timer -= dt;
    const total = this.windupSeconds;
    this.windupProgress = 1 - clamp01(this.timer / total);
    if (this.timer > 0) return;

    // フェイント。予備动作を見せてから振らずに引く（1 回の接近につき 1 度）。
    const chance = this.noFeint ? 0 : this.settings.feintChance;
    if (!this.tagPunchPending && this.currentAttack !== AttackId.Grab && !this.feinted &&
        chance > 0 && this.rng.next() < chance) {
      this.feinted = true;
      this.windupProgress = 0;
      this.attackReadyAt = this.time + Math.max(0.1, this.settings.feintDelaySeconds);
      this.phase = EnemyPhase.Chase;
      return;
    }
    if (playerDown) {
      this.cancelWindup();
      return;
    }
    this.phase = EnemyPhase.Attack;
  }

  /** 判定を出す瞬間。1 フレームだけ Attack に滞在する。 */
  strike(): void {
    const distance = this.distanceToPlayer();
    const strikeRange = this.currentAttack === AttackId.Grab
      ? this.rangeOf(AttackId.Grab) + this.settings.strikeTolerance
      : this.runRange + this.settings.strikeTolerance;
    const inRange = distance <= strikeRange;
    const hit = inRange && this.punchTouchesPlayer();
    const isGrab = this.currentAttack === AttackId.Grab && inRange && this.canGrabFacing();

    const outcome: AttackOutcome = {
      attacker: this,
      attack: this.currentAttack,
      zone: this.zone,
      damage: hit ? Math.max(0, this.runDamage) : 0,
      hit,
      isGrab,
      isTagPunch: this.tagPunchPending,
    };
    this.hooks.onAttack?.(outcome);

    this.feinted = false;
    this.tagPunchPending = false;
    if (isGrab) {
      this.isHolding = true;
      this.grabReadyAt = this.time + 7.2;
      this.timer = CombatConstants.GrappleLimitSeconds + 0.2;
    } else {
      this.isHolding = false;
      this.timer = Math.max(0, this.settings.recoverSeconds);
    }
    this.phase = EnemyPhase.Recover;
  }

  /**
   * 命中判定。punch anchor は外部注入（getPunchAnchor）を使う。
   * 拳は狙い座標へ向けて振られるので、アンカーと狙いの距離で当たりを取る。
   */
  punchTouchesPlayer(): boolean {
    const anchor = this.hooks.getPunchAnchor(this.zone);
    if (!anchor) return false;
    const target = this.hooks.getPlayerPosition();
    const bodyBias = this.isBellyPunch ? 0.28 : 0.40;
    // 顔はそのまま、腹は腰と胸の間（ここではプレイヤー中心 + 高さオフセットで近似）。
    const ty = this.zone === PunchZone.Face ? target.y + 1.35 : target.y + 0.95 + bodyBias * 0.3;
    const dx = anchor.x - target.x;
    const dy = anchor.y - ty;
    const dz = anchor.z - target.z;
    const gap = Math.hypot(dx, dy, dz);
    return gap <= Math.max(0.05, this.settings.punchHitRadius);
  }

  private canGrabFacing(): boolean {
    const p = this.hooks.getPlayerPosition();
    const f = this.hooks.getPlayerForward();
    const dx = this.position.x - p.x;
    const dz = this.position.z - p.z;
    const len = Math.hypot(dx, dz);
    if (len < 1e-4) return false;
    return (f.x * dx + f.z * dz) / len > 0.7;
  }

  get isBellyPunch(): boolean { return this.tagPunchPending && this.zone === PunchZone.Body; }
  get bodyTargetBias(): number { return this.isBellyPunch ? 0.28 : 0.40; }

  private endRecover(): void {
    this.isHolding = false;
    this.tagPunchSwing = false;
    this.forceZone = null;
    this.attackReadyAt = this.time + this.settings.attackCooldown;
    this.phase = EnemyPhase.Chase;
  }

  cancelWindup(): void {
    if (this.phase !== EnemyPhase.Windup && this.phase !== EnemyPhase.Attack) return;
    this.phase = EnemyPhase.Chase;
    this.windupProgress = 0;
    this.tagPunchPending = false;
    this.releaseToken?.();
  }

  /** 被弾。Unity 版 EnemyBrain.TakeHit 相当の最小処理。 */
  takeHit(damage: number, tier = 1, part: 'Head' | 'Torso' = 'Torso', fromThrow = false): void {
    if (!this.isAlive) return;
    this.hp = Math.max(0, this.hp - damage);
    this.cancelWindup();
    if (this.hp <= 0) {
      this.die();
      return;
    }
    this.phase = EnemyPhase.Stagger;
    this.timer = Math.max(0.05, this.settings.staggerSeconds * tier);
    this.staggerWeight = clamp(tier, 1, 2.2);
    this._staggerTotal = this.timer;
    this.staggerElapsed = 0;
    this._staggerPart = part;
    this._staggerFromThrow = fromThrow;
    this.hooks.onStagger?.(this);
  }

  private _staggerTotal = 0;
  private _staggerPart: 'Head' | 'Torso' = 'Torso';
  private _staggerFromThrow = false;

  private tickStagger(dt: number): void {
    this.timer -= dt;
    this.staggerProgress = clamp01(this.timer / Math.max(0.01, this._staggerTotal));
    this.staggerElapsed += dt;
    // 後ろへよろける（1 ラインなので X のみ）。
    const speed = 1.15 * clamp(this.staggerWeight, 1, 2.2) * this.staggerProgress;
    const away = Math.sign(this.position.x - this.hooks.getPlayerPosition().x) || 1;
    this.position.x += away * speed * dt;
    if (this.timer <= 0) this.endStagger();
  }

  private endStagger(): void {
    this.staggerProgress = 0;
    this.staggerWeight = 1;
    this.staggerElapsed = 0;
    this.phase = EnemyPhase.Chase;
    this.attackReadyAt = Math.max(this.attackReadyAt, this.time + 0.25);
  }

  private die(): void {
    this.isAlive = false;
    this.phase = EnemyPhase.Dead;
    this.isHolding = false;
    this.isPinning = false;
    this.isBearHugging = false;
    this.isWallAssisting = false;
    this.hooks.onDied?.(this);
  }

  /** EnemyGroup がトークンを差し込むための口（未接続なら null）。 */
  releaseToken: (() => void) | null = null;

  /** 演出用の補助量。rig はこれを読むだけで、logic へ書き込まない。 */
  get punchPoseWeight(): number {
    if (this.currentAttack === AttackId.Grab) return 0;
    if (this.phase === EnemyPhase.Windup) {
      if (this.isBellyPunch) {
        return smoothstep(clamp01((this.windupProgress - 0.02) / 0.32));
      }
      const start = this.tagPunchPending ? 0.30 : 0.70;
      const span = this.tagPunchPending ? 0.45 : 0.30;
      return smoothstep(clamp01((this.windupProgress - start) / span));
    }
    if (this.phase === EnemyPhase.Attack) return 1;
    if (this.phase === EnemyPhase.Recover) return clamp01(this.timer / 0.18);
    return 0;
  }

  get tagFaceCoil(): number {
    const isFace = this.tagPunchSwing && this.zone === PunchZone.Face;
    if (!isFace || this.phase !== EnemyPhase.Windup) return 0;
    const wind = smoothstep(clamp01((this.windupProgress - 0.06) / 0.42));
    const release = 1 - smoothstep(clamp01((this.windupProgress - 0.78) / 0.205));
    return wind * release;
  }

  get tagFaceDrive(): number {
    if (!this.tagPunchSwing || this.zone !== PunchZone.Face) return 0;
    if (this.phase === EnemyPhase.Windup) {
      return smoothstep(clamp01((this.windupProgress - 0.72) / 0.265));
    }
    if (this.phase === EnemyPhase.Attack) return 1;
    return 0;
  }

  snapshot(): Record<string, number | boolean | string | null> {
    return {
      phase: this.phase,
      hp: this.hp,
      isAlive: this.isAlive,
      currentAttack: this.currentAttack,
      zone: this.zone,
      windupProgress: this.windupProgress,
      staggerProgress: this.staggerProgress,
      isHolding: this.isHolding,
      cooldown: this.attackCooldownRemaining,
      x: this.position.x, z: this.position.z,
    };
  }
}

/** 掴み役かどうか（logic 側で読む用に再 export）。 */
export { GrappleKind };
