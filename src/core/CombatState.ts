import { BodyDamage } from './BodyDamage.js';
import { CombatConstants } from './CombatConstants.js';

/** 1人の戦闘リソース。HP / 部位ダメージ / ひるみ（ダウンゲージ）をまとめて持つ。 */
export class CombatState {
  private _hp: number;
  readonly body = new BodyDamage();
  private _flinch = 0;
  /** 最後にひるみが増えてからの経過秒（減衰の待ち時間に使う）。 */
  private flinchIdle = 0;
  private _maxHp: number = CombatConstants.MaxHp;

  /** ダウン中かどうか。ゲーム側の状態機械が管理する。 */
  isDown = false;

  constructor() {
    this._hp = this._maxHp;
  }

  get hp(): number {
    return this._hp;
  }

  get flinch(): number {
    return this._flinch;
  }

  get maxHp(): number {
    return this._maxHp;
  }

  get isAlive(): boolean {
    return this._hp > 0;
  }

  /** 移動速度倍率（脚の損傷）。 */
  get moveSpeedMultiplier(): number {
    return this.body.moveSpeedMultiplier;
  }

  /** 個体ごとに最大 HP を変える（雑魚は低く、ボスは高く）。 */
  setMaxHp(value: number): void {
    this._maxHp = value < 1 ? 1 : value;
    this._hp = this._maxHp;
  }

  /** 時間経過でのひるみの減衰。 */
  tick(deltaSeconds: number): void {
    if (deltaSeconds <= 0) return;

    this.flinchIdle += deltaSeconds;
    if (this.flinchIdle >= CombatConstants.FlinchDecayDelay && this._flinch > 0) {
      this._flinch = clamp(
        this._flinch - CombatConstants.FlinchDecayPerSecond * deltaSeconds,
        0,
        CombatConstants.MaxFlinch,
      );
    }
  }

  /** ひるみを加算する。上限に達したら true（＝ダウンさせる）。 */
  addFlinch(amount: number): boolean {
    if (amount <= 0) return false;

    this.flinchIdle = 0;
    this._flinch = clamp(this._flinch + amount, 0, CombatConstants.MaxFlinch);
    return this._flinch >= CombatConstants.MaxFlinch;
  }

  /** ひるみを 0 に戻す（ダウンしたとき）。 */
  resetFlinch(): void {
    this._flinch = 0;
    this.flinchIdle = 0;
  }

  applyHpDamage(amount: number): void {
    if (amount <= 0) return;
    this._hp = clamp(this._hp - amount, 0, this._maxHp);
  }

  /** HP を回復する。 */
  healHp(amount: number): void {
    if (amount <= 0) return;
    this._hp = clamp(this._hp + amount, 0, this._maxHp);
  }

  /** HP をこの値未満にしない（ゴッドモードの死亡防止）。 */
  ensureHpAtLeast(value: number): void {
    if (this._hp < value) this._hp = clamp(value, 0, this._maxHp);
  }

  /** チェックポイント復帰用。部位ダメージは仕様上リセットする。 */
  restoreAll(): void {
    this._hp = this._maxHp;
    this.body.reset();
    this.isDown = false;
    this.resetFlinch();
  }
}

function clamp(v: number, min: number, max: number): number {
  return v < min ? min : v > max ? max : v;
}
