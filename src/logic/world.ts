/**
 * logic の入口。app は 1 フレームごとに `tick(dt, intent)` を呼び、
 * `snapshot()` を rig / feel へ渡す。**three には依存しない**（座標は x/y/z の構造体だけ）。
 *
 * app 側の使い方（例）:
 *   const world = new GameWorld({ rigs: [enemyRig0, enemyRig1] });
 *   loop(() => {
 *     world.tick(clock.scaledDelta(dt), intent);
 *     const snap = world.snapshot();   // rig / feel はこれを読むだけ
 *   });
 *
 * 拳のアンカーは敵ごとの rig から注入する（既存の createHooksFromRig と整合）。
 * rig を渡さなかった敵は `getPunchAnchor` が null になり、`EnemyBrain` の仕様どおり
 * 一度も当たらない（＝当たり判定を rig 側の実ボーンに預ける設計をそのまま保つ）。
 *
 * プレイヤーの攻撃（intent.light / heavy / break_）は、立ち上がりエッジで
 * `AttackLibrary` の攻撃を発生させ、発生フレームの終わりに 1 回だけ命中判定する。
 * ダメージは必ず `DamageResolver.resolve` を通す（腕の損傷倍率・部位ダメージ・スタンは core が決める）。
 * 硬直（`AttackData.totalSeconds`）が終わるまで次の攻撃は受け付けない。
 */
import { CombatState } from '../core/CombatState';
import { CombatConstants } from '../core/CombatConstants';
import { GrappleKind, GrappleResistance, ResistanceMath } from '../core/GrappleResistance';
import { AttackData, AttackLibrary } from '../core/AttackData';
import { DamageResolver } from '../core/DamageResolver';
import { EnemyRole, EnemyRoleRules, RoleCandidate } from '../core/EnemyRole';
import { EnemyPhase, PunchZone, type EnemyActor } from './types';
import {
  AttackId, DEFAULT_ENEMY_SETTINGS, EnemyBrain,
  type AttackOutcome, type EnemyBrainHooks, type EnemySettings,
} from './enemy-brain';
import { EnemyGroup, EnemyRegistry } from './enemy-group';
import { GrappleSession, WallStage } from './grapple-session';
import {
  WallSequence, WallSequencePhase, WallTransition, WallStrikeKind,
} from './wall-sequence';
import { DEFAULT_MOTOR_CONFIG, PlayerMotor, type MotorConfig, type MotorStats } from './player-motor';
import { createHooksFromRig, type OtherHooks, type RigLike } from './hooks';
import type { InputIntent } from '../input/intent';

/** 既定の敵の数（要件どおり 2 体）。 */
const DEFAULT_ENEMY_COUNT = 2;

/** Unity 版の敵の背丈（実測）。背丈で役割が分かれる。 */
const DEFAULT_ENEMY_HEIGHTS: readonly number[] = [1.85, 1.79, 1.72];
/** 敵の初期位置。プレイヤー（x=0）から見て全部右側に置く（Unity 版の右端入場に合わせる）。 */
const SPAWN_X = 2.4;
const SPAWN_GAP = 1.5;
/** 攻撃トークンを取れない敵を「撃たせない」ための holdFire 秒。 */
const TOKEN_HOLD_FIRE_SECONDS = 0.15;
/** 投げ捨て（TossWindup）を見せてから拘束を解くまでの秒。 */
const TOSS_SECONDS = 0.7;
/** ダウン中の敵が起き上がるまで、Stagger を引き直すときの倍率（EnemyBrain.takeHit の tier）。 */
const ENEMY_DOWN_STAGGER_TIER = 2.2;
/** 正面判定のしきい値（向きベクトルとの内積）。EnemyBrain.canGrabFacing と同じ値。 */
const FRONT_DOT = 0.7;

/** CombatState の HP を指定値へ寄せる（heal / ダメージの公開 API だけで行う）。 */
function setCombatHp(state: CombatState, value: number): void {
  const delta = value - state.hp;
  if (delta > 0) state.healHp(delta);
  else if (delta < 0) state.applyHpDamage(-delta);
}

/**
 * 攻撃ごとのひるみ量。core の AttackLibrary（SPEC §4.5）を唯一の出所にする。
 * ダウンは CombatState のひるみゲージ（MaxFlinch）で判定する。
 */
const FLINCH_BY_ATTACK: Record<AttackId, number> = {
  [AttackId.Jab]: AttackLibrary.light().flinchValue,
  [AttackId.Heavy]: AttackLibrary.heavy().flinchValue,
  [AttackId.Break]: AttackLibrary.breakAttack().flinchValue,
  [AttackId.Grab]: AttackLibrary.grappleAttack().flinchValue,
};

export interface GameWorldOptions {
  /** 敵の数。既定 2。0 にすると敵なし（移動だけの確認用）。 */
  enemyCount?: number;
  /**
   * 敵の背丈（m）。個体差があると EnemyRoleRules が掴み役 / 道具役 / 打撃役を分ける。
   * Unity 版の実測は 1.85 / 1.79 / 1.72。省略時はその既定値。
   */
  enemyHeights?: readonly number[];
  /** シーンで明示した掴み役のインデックス（Unity の AuthoredGrappler 相当）。 */
  authoredGrapplerIndex?: number;
  /** EnemyBrain の設定（全敵に同じ値を入れる）。 */
  enemySettings?: Partial<EnemySettings>;
  /** 敵ごとの拳アンカーの出所。`rigs[i]` が敵 i の rig（PunchAnchorRig か head/body マップ）。 */
  rigs?: readonly RigLike[];
  /** 同時に攻撃を許す数。既定 2（EnemyGroup の初期値）。 */
  maxAttackers?: number;
  /** 敵 AI の乱数シード。同じシードと入力列なら結果は同じになる。 */
  seed?: number;
  /** プレイヤーの移動設定。 */
  motorConfig?: MotorConfig;
  /** 掴まれ方。既定は正面押さえ（GrappleKind.Pin）。 */
  grappleKind?: GrappleKind;
  /** ログ出力（WallSequence の段階遷移など）。 */
  log?: (message: string) => void;
}

/** snapshot().player の形。rig / HUD はこれを読むだけ。 */
export interface PlayerSnapshot {
  x: number;
  z: number;
  /** 向き（+1 = 右、-1 = 左）。 */
  facing: number;
  isGrappled: boolean;
  isDown: boolean;
  /** 掴まれていて、直前フレームにもがき入力があった。 */
  isStruggling: boolean;
  /** 追加フィールド（rig の歩行ポーズ用）。 */
  isMoving: boolean;
  /** 追加フィールド（HUD 用）。 */
  hp: number;
  /** 攻撃中か（発生〜硬直終わりまで）。rig はこの間だけ攻撃ポーズを選ぶ。 */
  isAttacking: boolean;
  /** 攻撃中の攻撃 ID（'light' | 'heavy' | 'break'）。攻撃していなければ null。 */
  attackKind: string | null;
}

/** snapshot().enemies[i] の形。 */
export interface EnemySnapshot {
  id: string;
  x: number;
  z: number;
  /** 向き（+1 = 右、-1 = 左）。プレイヤーの反対側を向く。 */
  facing: number;
  phase: EnemyPhase;
  isDead: boolean;
  /** 攻撃モーション中か（Windup / Attack / Recover のいずれか）。 */
  isAttacking: boolean;
  /** 追加フィールド（HUD 用）。 */
  hp: number;
  /** ダウン中か（HP 0・ダウンゲージ上限・死亡のいずれか）。 */
  isDown: boolean;
  /** 背丈（m）。rig のスケールに使う。 */
  height: number;
  /** 背丈から決まる役割（Grappler / Tools / Striker）。 */
  role: EnemyRole;
}

/** snapshot().grapple の形。掴まれていないときは stage=None（kind は最後の値のまま）。 */
export interface GrappleSnapshot {
  kind: GrappleKind;
  stage: WallStage;
  stageProgress: number;
  heldSeconds: number;
  splitWeight: number;
  gangActive: boolean;
}

/** 直前フレームに起きたこと。app はアニメーションのトリガにだけ使う。 */
export type WorldEvent =
  /**
   * 打撃が当たった。
   * `attacker` は「どちら側の攻撃か」（'player' = プレイヤー→敵 / 'enemy' = 敵→プレイヤー）。
   * `attackerId` はプレイヤーなら 'player'、敵なら敵 ID（壁の段階的な打撃は 'wall'）。
   */
  | {
      type: 'hit';
      attacker: 'player' | 'enemy';
      attackerId: string;
      targetId: string;
      zone: PunchZone;
      damage: number;
    }
  /** 掴まれた。 */
  | { type: 'grabbed'; attackerId: string; kind: GrappleKind }
  /** もがいて脱出した。 */
  | { type: 'escape' }
  /** プレイヤーがダウンした。 */
  | { type: 'down' }
  /** 壁拘束の段階が変わった。 */
  | { type: 'wall-stage'; from: WallStage; to: WallStage };

/** snapshot() の戻り値。読み取り専用として扱う（内部状態を書き換えないこと）。 */
export interface WorldSnapshot {
  time: number;
  player: PlayerSnapshot;
  enemies: EnemySnapshot[];
  grapple: GrappleSnapshot;
  events: WorldEvent[];
}

/** 1 体ぶんの内部状態。 */
interface EnemySlot {
  readonly brain: EnemyBrain;
  /** この個体の背丈（m）。役割の決定と rig のスケールに使う。 */
  readonly height: number;
  /**
   * 敵の戦闘リソース（HP / 部位ダメージ / ひるみゲージ）。
   * プレイヤー攻撃のダメージは DamageResolver がここへ適用する。
   * HP の表示上の真実は従来どおり `brain.hp` で、解決のたびに同期する。
   */
  readonly combat: CombatState;
  /** ダウン中に残っている秒（0 なら起きている）。 */
  downRemaining: number;
  /** 攻撃トークンを持っているか（EnemyGroup の attackers と同期させる）。 */
  holdingToken: boolean;
}

/**
 * ゲーム全体のロジック。app は「入力 → tick → snapshot」だけを見る。
 *
 * 依存の向き:
 *   app → GameWorld → logic（EnemyBrain / PlayerMotor / GrappleSession / WallSequence）
 *                   → core（CombatState / CombatConstants / GrappleResistance）
 *                   → rig は EnemyBrainHooks.getPunchAnchor 経由で注入されるだけ（逆依存なし）
 */
export class GameWorld {
  readonly playerCombat = new CombatState();
  readonly playerMotor: PlayerMotor;
  readonly group = new EnemyGroup();
  readonly registry = new EnemyRegistry();
  readonly session = new GrappleSession();
  readonly slots: EnemySlot[] = [];

  /** 経過秒（EnemyBrain に渡す scaled time）。 */
  time = 0;
  /** 掴まれているか（壁拘束も含む）。 */
  isGrappled = false;
  /** ダウン中か。 */
  isDown = false;

  private readonly grappleKind: GrappleKind;
  private readonly wallTransition: WallTransition;
  private readonly wallSeq: WallSequence;
  private readonly log: (message: string) => void;

  private strugglePressed = false;
  /** god モード（HP を 1 未満にしない）。intent.god を毎フレーム反映する。 */
  private godMode = false;
  private downRemaining = 0;
  private resistance: GrappleResistance | null = null;
  /** 壁ループの猶予入力（段階ごとに 0 へ戻す）。 */
  private mashCount = 0;
  private events: WorldEvent[] = [];
  private pendingOutcomes: AttackOutcome[] = [];
  private prevWallPhase = WallSequencePhase.Idle;
  private tossRemaining = 0;

  // --- プレイヤーの攻撃 ---
  /** 実行中の攻撃（発生〜硬直が終わるまで）。null なら次の入力を待っている。 */
  private playerAttack: AttackData | null = null;
  private attackElapsed = 0;
  /** この攻撃で命中判定を済ませたか（1 回の攻撃につき 1 回だけ判定する）。 */
  private attackHitDone = false;
  /** 立ち上がりエッジ検出用の前フレーム入力。 */
  private prevLight = false;
  private prevHeavy = false;
  private prevBreak = false;

  // --- 壁拘束中の膝 5 発（app の onKneesFinished 呼び出しに依存しない） ---
  private kneeCountValue = 0;
  private kneeTimer = 0;

  constructor(options: GameWorldOptions = {}) {
    this.log = options.log ?? (() => {});
    this.grappleKind = options.grappleKind ?? GrappleKind.Pin;
    this.playerMotor = new PlayerMotor({ ...(options.motorConfig ?? DEFAULT_MOTOR_CONFIG) });
    this.wallTransition = new WallTransition(this.session);

    const otherHooks = this.buildHooks();
    const enemySettings: EnemySettings = { ...DEFAULT_ENEMY_SETTINGS, ...(options.enemySettings ?? {}) };
    const seed = options.seed ?? 0x2f6e2b1;
    const count = Math.max(0, Math.floor(options.enemyCount ?? DEFAULT_ENEMY_COUNT));

    // 背丈で役割（掴み役 / 道具役 / 打撃役）を決める。Unity 版は個体ごとに背丈が
    // 1.85 / 1.79 / 1.72 と違う。全員同じ背丈だと全員 Striker になり、掴み役が居なくなる。
    const heights = options.enemyHeights ?? DEFAULT_ENEMY_HEIGHTS;
    const candidates: RoleCandidate[] = [];
    for (let i = 0; i < count; i++) {
      candidates.push(new RoleCandidate(
        `enemy-${i + 1}`,
        heights[i % heights.length] ?? DEFAULT_ENEMY_HEIGHTS[0]!,
        options.authoredGrapplerIndex === i,
      ));
    }

    for (let i = 0; i < count; i++) {
      const id = `enemy-${i + 1}`;
      const actor: EnemyActor = {
        id,
        isAlive: true,
        isActive: true,
        moveSpeed: enemySettings.moveSpeed,
        position: { x: SPAWN_X + i * SPAWN_GAP, y: 0, z: 0 },
      };
      const rig = options.rigs?.[i];
      // rig があれば createHooksFromRig で PunchZone → head/body の変換ごと任せる。
      const hooks: EnemyBrainHooks = rig
        ? createHooksFromRig(rig, otherHooks)
        : { ...otherHooks, getPunchAnchor: () => null };
      const brain = new EnemyBrain(id, actor, hooks, enemySettings, seed + i * 0x9e3779b1);
      // 役割は背丈から決める（EnemyRoleRules は列挙順に依存しない）。
      brain.role = EnemyRoleRules.roleOf(candidates, i);
      // 敵の戦闘リソース。最大 HP は EnemyBrain の初期 HP に合わせる（表示の hp とずれないように）。
      const combat = new CombatState();
      combat.setMaxHp(brain.hp);
      const slot: EnemySlot = {
        brain,
        combat,
        downRemaining: 0,
        holdingToken: false,
        height: candidates[i]!.height,
      };
      // Stagger / ダウン時に EnemyBrain.cancelWindup() から呼ばれる（トークンを返す）。
      brain.releaseToken = () => this.releaseToken(slot);
      this.slots.push(slot);
      this.group.register(brain);
      this.registry.register(brain);
    }

    this.group.maxAttackers = Math.max(1, Math.floor(options.maxAttackers ?? 2));

    this.wallSeq = new WallSequence({
      session: this.session,
      // 掴まれていて・生きていて・壁の段階が残っている間だけ進める。
      isAlive: () => this.isGrappled && this.session.isWall && this.playerCombat.isAlive,
      inputCount: () => this.mashCount,
      captor: () => this.session.captor,
      assist: () => this.session.assist,
      third: () => this.session.third,
      callAssist: () => this.assignAssist(),
      prepareThird: () => this.assignThird(),
      fireStrike: (puncher, kind, _index, scale) => this.applyWallStrike(puncher, kind, scale),
      beginToss: () => this.beginToss(),
      releaseAssist: () => { this.session.assist = null; },
      releaseThird: () => { this.session.third = null; },
      log: this.log,
    });
  }

  // ---------------------------------------------------------------------
  // 公開 API
  // ---------------------------------------------------------------------

  /** プレイヤーの x（app / テスト用）。 */
  get playerX(): number { return this.playerMotor.position.x; }
  /** プレイヤーの z（app / テスト用）。 */
  get playerZ(): number { return this.playerMotor.position.z; }
  /** 壁シーケンスの現在フェーズ（テスト・ログ用）。 */
  get wallPhase(): WallSequencePhase { return this.wallSeq.currentPhase; }
  /** 壁拘束が有効か（段階が None でない）。 */
  get isWallActive(): boolean { return this.session.isWall && this.session.stage !== WallStage.None; }
  /** プレイヤーが攻撃中か（発生〜硬直終わりまで）。 */
  get isPlayerAttacking(): boolean { return this.playerAttack !== null; }
  /** この壁拘束で logic が出した膝の本数（テスト・ログ用。最大 PinKneeCount）。 */
  get kneeCount(): number { return this.kneeCountValue; }

  /**
   * 壁拘束を開始する（Unity 版の PlayerGrapple.Wall の入口）。
   * 段階（Grab → … → Held）は tick が進め、Held で WallSequence が始まる。
   */
  beginWallGrapple(enemyId?: string): boolean {
    const captor = this.findAliveEnemy(enemyId);
    if (!captor || !this.playerCombat.isAlive) return false;

    this.releaseGrapple();           // 通常の掴みが残っていれば解く
    this.session.clearWall();
    this.session.clearSplit();
    this.session.isWall = true;
    this.session.kind = this.grappleKind;
    this.session.captor = captor;
    this.session.limitRemaining = 0;
    this.session.heldSeconds = 0;
    this.session.setStage(WallStage.Grab);
    this.wallSeq.reset();
    this.prevWallPhase = WallSequencePhase.Idle;
    this.tossRemaining = 0;
    this.mashCount = 0;
    this.resetKnees();

    this.isGrappled = true;
    this.playerMotor.setGrappled(true);
    this.events.push({ type: 'wall-stage', from: WallStage.None, to: WallStage.Grab });
    this.log(`[World] wall grapple by ${captor.id}`);
    return true;
  }

  /**
   * 膝蹴り 5 発が終わったことを伝える（Unity 版の onKneesFinished 相当）。
   *
   * logic は `tickWall` から膝 5 発を自分で出すようになったので、app は呼ばなくてよい。
   * 後方互換（明示的に早送りしたい場合）のために残している。
   */
  onKneesFinished(): void { this.wallSeq.onKneesFinished(); }

  /**
   * 1 フレーム進める。順序は固定（この順序自体が仕様）:
   *
   *   1. 入力の取り込み … intent の「押した瞬間」を数える（もがき・猶予入力）
   *   2. プレイヤーの移動 … PlayerMotor（掴まれ中は動かない）
   *   3. プレイヤーの攻撃 … 立ち上がりエッジで発生 → 発生終わりに 1 回だけ命中判定
   *   4. 敵 AI          … 攻撃トークン仲裁（EnemyGroup）→ EnemyBrain
   *   5. 拘束・壁        … GrappleResistance / GrappleSession / WallTransition / WallSequence（膝 5 発）
   *   6. ヒット解決      … 4 で出た AttackOutcome をここで適用（命中・掴み・ダウン）
   */
  tick(dt: number, intent: InputIntent): void {
    // HitStop などで dt=0 のフレームが来ても、前フレームのイベントを再送しない。
    this.events = [];
    if (!(dt > 0) || !Number.isFinite(dt)) return;

    this.time += dt;

    // --- 1. 入力の取り込み ---------------------------------------------
    if (intent.struggle) this.mashCount++;
    this.strugglePressed = intent.struggle;
    this.godMode = intent.god;

    // --- 2. プレイヤーの移動 -------------------------------------------
    this.tickPlayer(dt, intent);

    // --- 3. プレイヤーの攻撃 -------------------------------------------
    this.tickPlayerAttack(dt, intent);

    // --- 4. 敵 AI（トークン仲裁 → EnemyBrain） -------------------------
    this.tickEnemies(dt);

    // --- 5. 拘束・壁シーケンス -----------------------------------------
    this.tickGrapple(dt);

    // --- 6. ヒット解決 -------------------------------------------------
    this.resolveHits();
  }

  /** 読み取り専用の状態。app はこれを rig へ渡す（内部オブジェクトを書き換えないこと）。 */
  snapshot(): WorldSnapshot {
    const p = this.playerMotor.position;
    return {
      time: this.time,
      player: {
        x: p.x,
        z: p.z,
        facing: this.playerMotor.facingSign,
        isGrappled: this.isGrappled,
        isDown: this.isDown,
        isStruggling: this.isGrappled && this.strugglePressed,
        isMoving: this.playerMotor.isMoving,
        hp: this.playerCombat.hp,
        isAttacking: this.playerAttack !== null,
        attackKind: this.playerAttack?.id ?? null,
      },
      enemies: this.slots.map((slot) => {
        const brain = slot.brain;
        const position = brain.position;
        const isAttacking = brain.phase === EnemyPhase.Windup ||
          brain.phase === EnemyPhase.Attack ||
          brain.phase === EnemyPhase.Recover;
        return {
          id: brain.id,
          x: position.x,
          z: position.z,
          facing: p.x - position.x < 0 ? -1 : 1,
          phase: brain.phase,
          isDead: !brain.isAlive,
          isAttacking,
          hp: brain.hp,
          isDown: slot.combat.isDown || !brain.isAlive,
          height: slot.height,
          role: brain.role,
        };
      }),
      grapple: {
        kind: this.session.kind,
        stage: this.session.stage,
        stageProgress: this.session.stageProgress,
        heldSeconds: this.session.heldSeconds,
        splitWeight: this.session.splitWeight,
        gangActive: this.session.gangActive,
      },
      events: [...this.events],
    };
  }

  // ---------------------------------------------------------------------
  // 2. プレイヤー
  // ---------------------------------------------------------------------

  private tickPlayer(dt: number, intent: InputIntent): void {
    if (this.isDown) {
      this.downRemaining -= dt;
      if (this.downRemaining <= 0) {
        this.downRemaining = 0;
        this.isDown = false;
        this.playerCombat.isDown = false;
        this.playerCombat.resetFlinch();
      }
    }

    this.playerMotor.setGrappled(this.isGrappled);
    const stats: MotorStats = {
      isDead: !this.playerCombat.isAlive,
      isStunned: false,
      isDown: this.isDown,
      isMoving: false,
      moveSpeedMultiplier: this.playerCombat.moveSpeedMultiplier,
    };
    this.playerMotor.tick(dt, stats, intent.moveX);
    this.playerMotor.lateTick();
    this.playerCombat.tick(dt); // ひるみの減衰
  }

  // ---------------------------------------------------------------------
  // 3. プレイヤーの攻撃（intent.light / heavy / break_）
  // ---------------------------------------------------------------------

  /**
   * 押した瞬間（立ち上がりエッジ）で攻撃を発生させ、発生が終わったフレームで 1 回だけ当たりを取る。
   * 硬直（`AttackData.totalSeconds`）が終わるまでは次の入力を捨てる。
   */
  private tickPlayerAttack(dt: number, intent: InputIntent): void {
    const lightPressed = intent.light && !this.prevLight;
    const heavyPressed = intent.heavy && !this.prevHeavy;
    const breakPressed = intent.break_ && !this.prevBreak;
    this.prevLight = intent.light;
    this.prevHeavy = intent.heavy;
    this.prevBreak = intent.break_;

    // 実行中の攻撃を進める。
    const attack = this.playerAttack;
    if (attack) {
      // 掴まれた・ダウン・死亡したら振りかけを中断する（硬直ごと消す）。
      if (this.playerBusy()) {
        this.endPlayerAttack();
        return;
      }
      this.attackElapsed += dt;
      if (!this.attackHitDone && this.attackElapsed >= attack.startupSeconds) {
        this.attackHitDone = true;
        this.resolvePlayerAttackHit(attack);
      }
      if (this.attackElapsed >= attack.totalSeconds) this.endPlayerAttack();
      return; // 硬直中は新しい攻撃を受け付けない
    }

    if (!lightPressed && !heavyPressed && !breakPressed) return;
    if (this.playerBusy()) return;

    // 同時押しは強攻撃を優先する（Break > Heavy > Light）。
    const next = breakPressed ? AttackLibrary.breakAttack()
      : heavyPressed ? AttackLibrary.heavy()
        : AttackLibrary.light();
    this.startPlayerAttack(next);
  }

  private playerBusy(): boolean {
    return this.isDown || this.isGrappled || !this.playerCombat.isAlive;
  }

  private startPlayerAttack(attack: AttackData): void {
    this.playerAttack = attack;
    this.attackElapsed = 0;
    this.attackHitDone = false;
    // 踏み込みは PlayerMotor の既存機構へ任せる（発生が終わるまでに lungeDistance ぶん前へ）。
    this.playerMotor.lunge(attack.lungeDistance, attack.startupSeconds, attack.id);
    this.log(`[World] player attack ${attack.id}`);
  }

  private endPlayerAttack(): void {
    this.playerAttack = null;
    this.attackElapsed = 0;
    this.attackHitDone = false;
  }

  /**
   * プレイヤー → 敵の命中判定。正面の一番近い 1 体にだけ当たる。
   * 間合いは `CombatConstants.BodyGap`（体の隙間）に攻撃の踏み込み距離を足した値。
   */
  private resolvePlayerAttackHit(attack: AttackData): void {
    const goal = this.findPlayerAttackTarget(attack);
    if (!goal) return;

    // 敵の HP は brain 側が真実。解決の前に CombatState を合わせて二重計上を防ぐ。
    setCombatHp(goal.combat, goal.brain.hp);
    const hpBefore = goal.combat.hp;
    // ダメージ・部位ダメージ・スタン段階は core が決める（腕の損傷倍率もここで効く）。
    const hit = DamageResolver.resolve(this.playerCombat, attack, goal.combat, 1);
    const dealt = hpBefore - goal.combat.hp;
    if (dealt <= 0) return;

    // core のスタン段階（秒）を EnemyBrain.takeHit の倍率へ換算する。
    const stunSeconds = CombatConstants.stunSeconds(hit.stagger);
    const tier = stunSeconds / Math.max(0.05, goal.brain.settings.staggerSeconds);
    goal.brain.takeHit(dealt, tier, 'Torso');

    this.events.push({
      type: 'hit',
      attacker: 'player',
      attackerId: 'player',
      targetId: goal.brain.id,
      zone: PunchZone.Body,
      damage: dealt,
    });

    // ひるみゲージ（core の値）が上限に達したらダウン。HP 0 は死亡＝ダウン。
    if (goal.brain.isAlive && goal.combat.addFlinch(attack.flinchValue)) this.downEnemy(goal);
  }

  /** 正面・間合い内で一番近い敵。 */
  private findPlayerAttackTarget(attack: AttackData): EnemySlot | null {
    const p = this.playerMotor.position;
    const facing = this.playerMotor.facingSign;
    const reach = CombatConstants.BodyGap + attack.lungeDistance;
    let best: EnemySlot | null = null;
    let bestDistance = Number.POSITIVE_INFINITY;

    for (const slot of this.slots) {
      if (!slot.brain.isAlive) continue;
      const dx = slot.brain.position.x - p.x;
      const dz = slot.brain.position.z - p.z;
      const distance = Math.hypot(dx, dz);
      if (distance > reach) continue;
      // 正面にいるか（向きとの内積）。ゼロ距離はその場とみなす。
      if (distance > 1e-4 && (facing * dx) / distance < FRONT_DOT) continue;
      if (distance < bestDistance) {
        best = slot;
        bestDistance = distance;
      }
    }
    return best;
  }

  /** 敵をダウンさせる（DownDuration の間は行動できない）。 */
  private downEnemy(slot: EnemySlot): void {
    if (!slot.brain.isAlive || slot.combat.isDown) return;
    slot.combat.isDown = true;
    slot.combat.resetFlinch();
    slot.downRemaining = CombatConstants.DownDuration;
    this.log(`[World] enemy down ${slot.brain.id}`);
  }

  // ---------------------------------------------------------------------
  // 4. 敵 AI と攻撃トークン
  // ---------------------------------------------------------------------

  private tickEnemies(dt: number): void {
    // 壁拘束中は敵 AI を止める（壁シーケンスが拘束役・アシストの動きを駆動する）。
    if (this.isWallActive) return;

    for (const slot of this.slots) {
      const brain = slot.brain;
      if (!brain.isAlive) {
        this.releaseToken(slot);
        continue;
      }

      // ダウン中は起き上がるまで Stagger を引き直して、攻撃させない。
      if (slot.downRemaining > 0) {
        slot.downRemaining -= dt;
        if (slot.downRemaining <= 0) {
          slot.downRemaining = 0;
          slot.combat.isDown = false;
        } else if (brain.phase !== EnemyPhase.Stagger) {
          brain.takeHit(0, ENEMY_DOWN_STAGGER_TIER);
        }
      }

      const attacking = brain.phase === EnemyPhase.Windup ||
        brain.phase === EnemyPhase.Attack ||
        brain.phase === EnemyPhase.Recover;

      // 攻撃が終わったらトークンを返す（Chase / Strafe / Stagger / Dead に戻ったら手放す）。
      if (slot.holdingToken && !attacking) this.releaseToken(slot);

      if (!attacking && !slot.holdingToken) {
        const wantsToAttack = brain.phase === EnemyPhase.Chase || brain.phase === EnemyPhase.Strafe;
        if (wantsToAttack) {
          slot.holdingToken = this.group.tryAcquireAttackToken(brain, this.playerMotor.position.x);
          // 空きが無い／手前の仲間に塞がれている間は撃たせない（詰まって突っ立つのを避ける）。
          if (!slot.holdingToken) brain.holdFire(TOKEN_HOLD_FIRE_SECONDS);
        }
      }

      brain.tick(dt, this.time);
    }
  }

  private releaseToken(slot: EnemySlot): void {
    this.group.releaseAttackToken(slot.brain);
    slot.holdingToken = false;
  }

  // ---------------------------------------------------------------------
  // 4. 拘束・壁
  // ---------------------------------------------------------------------

  private tickGrapple(dt: number): void {
    if (!this.isGrappled) {
      this.session.heldSeconds = 0;
      return;
    }
    this.session.heldSeconds += dt;

    // 拘束中に力尽きたら、壁・通常拘束とも必ず解いて止める（掴まれたまま固まらない）。
    if (!this.playerCombat.isAlive) {
      if (this.session.isWall) this.finishWall();
      else this.releaseGrapple();
      return;
    }

    if (this.isWallActive) {
      this.tickWall(dt);
      return;
    }

    // 通常の掴み（GrappleResistance）。もがき入力で脱出、時間切れでダウン。
    const resistance = this.resistance;
    if (!resistance) return;
    if (this.strugglePressed && resistance.registerInput()) {
      this.escapeGrapple();
      return;
    }
    this.session.limitRemaining -= dt;
    if (this.session.limitRemaining <= 0) {
      this.session.limitRemaining = 0;
      this.failGrapple();
    }
  }

  private tickWall(dt: number): void {
    const prevStage = this.session.stage;

    if (this.session.stage !== WallStage.None && this.session.stage < WallStage.Held) {
      // 姿勢の段階（Grab → Rise → … → Restrain → Held）。
      const reachedHeld = this.wallTransition.tick(dt);
      if (reachedHeld && !this.wallSeq.isRunning) {
        this.wallSeq.start(); // WaitKnees から
        this.prevWallPhase = this.wallSeq.currentPhase;
        this.mashCount = 0;
      }
    } else if (this.wallSeq.isRunning) {
      // 膝待ち → 猶予 → 二人がかり → 猶予 → 三人がかり → 投げ捨て。
      this.tickWallKnees(dt);
      this.wallSeq.tick(dt);
      const phase = this.wallSeq.currentPhase;
      if (phase !== this.prevWallPhase) {
        this.prevWallPhase = phase;
        this.mashCount = 0; // 猶予の入力は段階ごとに数え直す
      }
    }

    // 見た目用の重み（純粋関数を毎フレーム進める）。
    this.session.tickSplitWeight(dt);
    this.session.tickGangWeight(dt);

    if (this.tossRemaining > 0) {
      this.tossRemaining -= dt;
      if (this.tossRemaining <= 0) {
        this.tossRemaining = 0;
        this.finishWall();
      }
    }

    if (this.session.stage !== prevStage) {
      this.events.push({ type: 'wall-stage', from: prevStage, to: this.session.stage });
    }
  }

  // --- 膝 5 発（WallSequence.WaitKnees の間、logic が自分で出す） -------------

  /**
   * 膝蹴り 5 発を自分で発生させる。
   * Unity 版は `PlayerGrapple` 側のコルーチンが `onKneesFinished` を呼ぶ契約だったが、
   * app が呼び忘れると壁シーケンスが止まるため、ここで `PinKneeInterval` ごとに出す。
   */
  private tickWallKnees(dt: number): void {
    if (!this.wallSeq.isRunning || this.wallSeq.currentPhase !== WallSequencePhase.WaitKnees) {
      this.kneeTimer = 0;
      return;
    }
    if (this.kneeCountValue >= CombatConstants.PinKneeCount) {
      this.wallSeq.onKneesFinished();
      return;
    }
    this.kneeTimer += dt;
    if (this.kneeTimer < CombatConstants.PinKneeInterval) return;
    this.kneeTimer -= CombatConstants.PinKneeInterval;

    this.kneeCountValue++;
    this.applyPinKnee();
    if (this.kneeCountValue >= CombatConstants.PinKneeCount) this.wallSeq.onKneesFinished();
  }

  /** 押さえつけの膝 1 発。ダメージは core の PinKneeDamage。 */
  private applyPinKnee(): void {
    const captor = this.session.captor;
    const damage = CombatConstants.PinKneeDamage;
    this.playerCombat.applyHpDamage(damage);
    if (this.godMode) this.playerCombat.ensureHpAtLeast(1);
    this.events.push({
      type: 'hit',
      attacker: 'enemy',
      attackerId: captor?.id ?? 'wall',
      targetId: 'player',
      zone: PunchZone.Body,
      damage,
    });
    if (!this.godMode && this.playerCombat.addFlinch(damage)) this.knockDown();
    this.log(`[World] pin knee ${this.kneeCountValue}/${CombatConstants.PinKneeCount}`);
  }

  private resetKnees(): void {
    this.kneeCountValue = 0;
    this.kneeTimer = 0;
  }

  /** 二人目のアシストを入れる（WallSequence.callAssist）。 */
  private assignAssist(): void {
    const captor = this.session.captor;
    const candidate = this.slots.find((s) => s.brain.isAlive && s.brain !== captor)?.brain;
    if (!candidate) return;
    this.session.assist = candidate;
    this.session.assistCandidate = candidate;
    this.session.splitStarted = true;
    this.session.splitReady = true;
    this.log(`[World] assist ${candidate.id}`);
  }

  /** 三人目を入れる（WallSequence.prepareThird）。2 体しかいなければ何もしない。 */
  private assignThird(): void {
    const captor = this.session.captor;
    const assist = this.session.assist;
    const candidate = this.slots.find((s) =>
      s.brain.isAlive && s.brain !== captor && s.brain !== assist)?.brain;
    if (!candidate) return;
    this.session.third = candidate;
    this.log(`[World] third ${candidate.id}`);
  }

  /**
   * 壁の 1 発をプレイヤーへ通す。
   * ダメージ表はまだ Unity の HeldBellyPunch 系を移植していないので仮値（CombatConstants から）。
   */
  private applyWallStrike(puncher: EnemyActor | null, kind: WallStrikeKind, scale: number): void {
    const base = kind === WallStrikeKind.Knee
      ? CombatConstants.HeldKneeBellyDamage
      : kind === WallStrikeKind.Face
        ? CombatConstants.WallSplitPunchDamage
        : CombatConstants.WallSplitPunchBelly;
    const damage = base * Math.max(0, scale);
    this.playerCombat.applyHpDamage(damage);
    if (this.godMode) this.playerCombat.ensureHpAtLeast(1);
    this.events.push({
      type: 'hit',
      attacker: 'enemy',
      attackerId: puncher?.id ?? 'wall',
      targetId: 'player',
      zone: kind === WallStrikeKind.Face ? PunchZone.Face : PunchZone.Body,
      damage,
    });
    if (!this.godMode && this.playerCombat.addFlinch(damage)) this.knockDown();
  }

  /** 投げ捨ての開始（WallSequence.beginToss）。段階を見せてから拘束を解く。 */
  private beginToss(): void {
    this.session.setStage(WallStage.TossWindup);
    this.tossRemaining = TOSS_SECONDS;
  }

  private finishWall(): void {
    this.session.clearWall();
    this.session.clearSplit();
    this.session.captor = null;
    this.session.assist = null;
    this.session.third = null;
    this.session.heldSeconds = 0;
    this.resistance = null;
    this.wallSeq.reset();
    this.prevWallPhase = WallSequencePhase.Idle;
    this.isGrappled = false;
    this.playerMotor.setGrappled(false);
    this.resetKnees();
    this.log('[World] wall grapple finished');
  }

  // ---------------------------------------------------------------------
  // 5. ヒット解決（命中・掴み・ダウン）
  // ---------------------------------------------------------------------

  private resolveHits(): void {
    const pending = this.pendingOutcomes;
    this.pendingOutcomes = [];
    for (const outcome of pending) {
      if (outcome.hit) this.applyPlayerDamage(outcome);
      if (outcome.isGrab) this.beginGrapple(outcome.attacker);
    }
  }

  private applyPlayerDamage(outcome: AttackOutcome): void {
    const damage = Math.max(0, outcome.damage);
    if (damage <= 0) return;

    this.playerCombat.applyHpDamage(damage);
    // god モードは HP を 1 未満にしない（CombatState の既存 API を使う）。
    if (this.godMode) this.playerCombat.ensureHpAtLeast(1);
    this.events.push({
      type: 'hit',
      attacker: 'enemy',
      attackerId: outcome.attacker.id,
      targetId: 'player',
      zone: outcome.zone,
      damage,
    });

    const flinch = FLINCH_BY_ATTACK[outcome.attack] ?? 0;
    // ひるみゲージが上限に達したらダウン（ダウン中でなければ）。
    if (!this.godMode && this.playerCombat.addFlinch(flinch)) this.knockDown();
  }

  private beginGrapple(attacker: EnemyActor): void {
    if (this.isWallActive || this.isGrappled) return;
    const kind = this.grappleKind;
    this.session.kind = kind;
    this.session.captor = attacker;
    this.session.heldSeconds = 0;
    this.session.limitRemaining = kind === GrappleKind.Pin
      ? CombatConstants.FrontGrappleEscapeSeconds
      : CombatConstants.GrappleLimitSeconds;
    this.resistance = new GrappleResistance(kind, ResistanceMath.requiredInputs(kind));
    this.isGrappled = true;
    this.playerMotor.setGrappled(true);
    this.log(`[World] grabbed by ${attacker.id} kind=${GrappleKind[kind]}`);
  }

  private escapeGrapple(): void {
    this.releaseGrapple();
    this.events.push({ type: 'escape' });
    this.log('[World] escaped');
  }

  private failGrapple(): void {
    this.releaseGrapple();
    this.knockDown();
  }

  /** 拘束そのものを解く（脱出・失敗・壁への切り替えの共通経路）。 */
  private releaseGrapple(): void {
    this.isGrappled = false;
    this.resistance = null;
    this.session.captor = null;
    this.session.heldSeconds = 0;
    this.session.limitRemaining = 0;
    this.playerMotor.setGrappled(false);
  }

  private knockDown(): void {
    if (this.isDown || !this.playerCombat.isAlive) return;
    this.isDown = true;
    this.playerCombat.isDown = true;
    this.downRemaining = CombatConstants.DownDuration;
    this.playerCombat.resetFlinch();
    this.events.push({ type: 'down' });
    this.log('[World] player down');
  }

  private findAliveEnemy(id?: string): EnemyActor | null {
    if (id !== undefined) {
      return this.slots.find((s) => s.brain.id === id && s.brain.isAlive)?.brain ?? null;
    }
    return this.slots.find((s) => s.brain.isAlive)?.brain ?? null;
  }

  // ---------------------------------------------------------------------
  // rig → logic のフック
  // ---------------------------------------------------------------------

  /** EnemyBrainHooks のうち、拳アンカー以外（プレイヤー状態の読み出しと攻撃結果の受け取り）。 */
  private buildHooks(): OtherHooks {
    return {
      getPlayerPosition: () => this.playerMotor.position,
      getPlayerForward: () => ({ x: this.playerMotor.facingSign, y: 0, z: 0 }),
      isPlayerDown: () => this.isDown,
      isPlayerDead: () => !this.playerCombat.isAlive,
      isPlayerGrappled: () => this.isGrappled,
      canGrab: () => !this.isGrappled && !this.isDown,
      // 命中判定は 5（ヒット解決）でまとめて適用する。ここでは溜めるだけ。
      onAttack: (outcome) => { this.pendingOutcomes.push(outcome); },
    };
  }
}
