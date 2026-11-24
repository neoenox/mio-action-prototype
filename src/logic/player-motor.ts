import { clamp, moveTowards } from '../core/math';

/** 座標は Unity 版と同じ X がベルト軸、Z がレーン（奥行き）。 */
export interface Vec3 { x: number; y: number; z: number }

export interface MotorStats {
  isDead: boolean;
  isStunned: boolean;
  isDown: boolean;
  isMoving: boolean;
  /** CombatState.State.MoveSpeedMultiplier 相当。 */
  moveSpeedMultiplier: number;
}

export interface MotorConfig {
  baseSpeed: number;
  acceleration: number;
  turnSpeed: number;
  gravity: number;
  beltFacing: boolean;
  /** 手首ロックの MoveScale。 */
  wristMoveScale: number;
}

export const DEFAULT_MOTOR_CONFIG: MotorConfig = {
  baseSpeed: 4.2,
  acceleration: 28,
  turnSpeed: 14,
  gravity: -22,
  beltFacing: true,
  wristMoveScale: 1,
};

/** 横一列の移動。脚の部位ダメージは moveSpeedMultiplier に反映される。 */
export class PlayerMotor {
  position: Vec3 = { x: 0, y: 0, z: 0 };
  /** 向き（X 成分の符号）。 */
  facingSign = 1;
  config: MotorConfig;

  forwardLimitX = Number.POSITIVE_INFINITY;
  backLimitX = Number.NEGATIVE_INFINITY;

  private _laneZ = 0;
  private hasLaneZOverride = false;
  private laneZOverride = 0;
  private velocityX = 0;
  private verticalVelocity = 0;
  private grounded = true;
  private lungeSpeed = 0;
  private lungeLeft = 0;
  private lungeTravel = 0;
  private lungeFade = 0;
  private lungeStartX = 0;
  private lungeAttackId: string | null = null;
  private knockLeft = 0;
  private knockSpeed = 0;
  private knockDir: Vec3 = { x: -1, y: 0, z: 0 };
  private knockStartX = 0;
  private launchUp = 0;
  private launchLeft = 0;
  private lastFacingSign = 1;
  private isGrappled = false;

  isMoving = false;

  constructor(config: MotorConfig = DEFAULT_MOTOR_CONFIG) {
    this.config = config;
  }

  get laneZ(): number { return this._laneZ; }
  get isAirborne(): boolean { return this.launchLeft > 0; }
  get lungeTravelDistance(): number { return this.lungeTravel; }
  get laneFacingSign(): number { return this.lastFacingSign; }

  setGrappled(value: boolean): void { this.isGrappled = value; }

  setLaneZOverride(z: number): void {
    this.hasLaneZOverride = true;
    this.laneZOverride = z;
  }

  clearLaneZOverride(): void { this.hasLaneZOverride = false; }

  /** LateUpdate 相当。Z は必ずレーン（または上書き）へ戻す。 */
  lateTick(): void {
    this.position.z = this.hasLaneZOverride ? this.laneZOverride : this._laneZ;
  }

  /** ステータスを受け取る mini-tick。Unity の Update 相当。 */
  tick(dt: number, stats: MotorStats, moveX: number, wristMoveScale = 1): void {
    if (stats.isDead) {
      this.isMoving = false;
      return;
    }

    if (this.isGrappled) {
      // 掴まれている間は動けない。踏み込み・ノックバック・吹き飛びの残りも切る
      // （残すと解放直後に不意に滑り出す）。
      this.isMoving = false;
      stats.isMoving = false;
      this.velocityX = 0;
      this.lungeLeft = 0;
      this.lungeTravel = 0;
      this.knockLeft = 0;
      this.launchLeft = 0;
      return;
    }

    const input = stats.isStunned || stats.isDown ? 0 : clamp(moveX, -1, 1);
    const wishX = input;
    let speed = this.config.baseSpeed * stats.moveSpeedMultiplier * wristMoveScale;
    const target = wishX * speed;
    this.velocityX = moveTowards(this.velocityX, target, this.config.acceleration * dt);

    if (Math.abs(wishX) > 0.0001) {
      if (this.config.beltFacing) {
        if (Math.abs(wishX) >= 0.2) {
          this.lastFacingSign = Math.sign(wishX);
          this.facingSign = this.lastFacingSign;
        }
      } else {
        // 線形係数 * dt だとフレームレート依存になるので指数補間を使う（Unity 版と同じ）。
        const t = 1 - Math.exp(-this.config.turnSpeed * dt);
        this.lastFacingSign += (Math.sign(wishX) - this.lastFacingSign) * t;
        this.facingSign = this.lastFacingSign;
      }
    }

    if (this.grounded && this.verticalVelocity < 0) this.verticalVelocity = -2;
    this.verticalVelocity += this.config.gravity * dt;

    this.position.x += this.velocityX * dt;
    this.position.y += this.verticalVelocity * dt;
    if (this.position.y <= 0) {
      this.position.y = 0;
      this.grounded = true;
      if (this.verticalVelocity < 0) this.verticalVelocity = 0;
    } else {
      this.grounded = false;
    }

    if (this.position.x > this.forwardLimitX || this.position.x < this.backLimitX) {
      this.position.x = clamp(this.position.x, this.backLimitX, this.forwardLimitX);
      this.velocityX = 0;
    }

    // 踏み込み。被弾したら止める（スタッガー中に滑っていかない）。
    if (this.lungeLeft > 0) {
      if (stats.isStunned) {
        this.lungeLeft = 0;
      } else {
        const step = Math.min(dt, this.lungeLeft);
        this.lungeLeft -= step;
        const move = Math.sign(this.facingSign) * (this.lungeSpeed * step);
        this.position.x += move;
        this.lungeTravel += this.lungeSpeed * step;
        if (this.lungeLeft <= 0) this.lungeAttackId = null;
      }
    }
    if (this.lungeLeft <= 0 && this.lungeTravel > 0) {
      this.lungeTravel = moveTowards(this.lungeTravel, 0, this.lungeFade * dt);
    }

    if (this.knockLeft > 0) {
      const step = Math.min(dt, this.knockLeft);
      this.knockLeft -= step;
      this.position.x += this.knockDir.x * (this.knockSpeed * step);
      this.position.z += this.knockDir.z * (this.knockSpeed * step);
    }

    if (this.launchLeft > 0) {
      this.launchLeft = Math.max(0, this.launchLeft - dt);
    }

    this.isMoving = this.velocityX * this.velocityX > 0.05;
    stats.isMoving = this.isMoving;
  }

  /** 攻撃の踏み込み。発生の瞬間までに distance ぶん前へ出る。 */
  lunge(distance: number, seconds: number, attackId: string | null = null): void {
    if (distance <= 0 || seconds <= 0.01) return;
    this.lungeSpeed = distance / seconds;
    this.lungeLeft = seconds;
    this.lungeAttackId = attackId;
    // 前の踏み込みの戻しが終わる前に次が来ても足を跳ばさないよう 0 に戻さない。
    this.lungeFade = distance / 0.22;
    this.lungeStartX = this.position.x;
  }

  /** 後ろへ突き飛ばされる。掴みから逃げられなかったときに使う。 */
  knockback(distance: number, seconds: number, direction: Vec3 | null = null): void {
    if (distance <= 0 || seconds <= 0.01) return;
    const dir = direction ?? { x: -this.facingSign, y: 0, z: 0 };
    let dx = dir.x;
    let dz = dir.z;
    const len = Math.hypot(dx, dz);
    if (len > 1e-2) { dx /= len; dz /= len; } else { dx = -1; dz = 0; }
    this.knockDir = { x: dx, y: 0, z: dz };
    this.knockSpeed = distance / seconds;
    this.knockLeft = seconds;
    this.knockStartX = this.position.x;
  }

  /** 後ろへ放物線を描いて吹き飛ばす。height は最高到達点（m）。 */
  launchBack(distance: number, height: number, direction: Vec3 | null = null): number {
    const g = Math.abs(this.config.gravity) > 0.01 ? Math.abs(this.config.gravity) : 22;
    this.launchUp = Math.sqrt(2 * g * Math.max(0.05, height));
    const air = 2 * this.launchUp / g;
    this.launchLeft = air;
    this.verticalVelocity = this.launchUp;
    this.grounded = false;
    this.knockback(distance, air, direction);
    return air;
  }

  /** 検証・テスト用のスナップショット。 */
  snapshot(): Record<string, number | boolean | string | null> {
    return {
      x: this.position.x, y: this.position.y, z: this.position.z,
      facingSign: this.facingSign,
      isMoving: this.isMoving,
      isAirborne: this.isAirborne,
      lungeTravel: this.lungeTravel,
      lungeAttackId: this.lungeAttackId,
      knockLeft: this.knockLeft,
      launchLeft: this.launchLeft,
      lungeStartX: this.lungeStartX,
      knockStartX: this.knockStartX,
    };
  }
}
