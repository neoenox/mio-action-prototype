import { describe, expect, it } from 'vitest';
import { DEFAULT_MOTOR_CONFIG, PlayerMotor, type MotorStats } from '../../src/logic/player-motor.js';

const stats = (over: Partial<MotorStats> = {}): MotorStats => ({
  isDead: false, isStunned: false, isDown: false, isMoving: false,
  moveSpeedMultiplier: 1, ...over,
});

/** dt=1/60 で n フレーム回す。 */
const run = (motor: PlayerMotor, s: MotorStats, moveX: number, n: number, t0 = 0): number => {
  let t = t0;
  for (let i = 0; i < n; i++) {
    motor.tick(1 / 60, s, moveX);
    motor.lateTick();
    t += 1 / 60;
  }
  return t;
};

describe('PlayerMotor', () => {
  it('入力で横へ動き、Z はレーンに固定される', () => {
    const m = new PlayerMotor({ ...DEFAULT_MOTOR_CONFIG, baseSpeed: 4.2 });
    const s = stats();
    run(m, s, 1, 60);
    expect(m.position.x).toBeGreaterThan(3);
    expect(m.position.z).toBe(0);
    expect(m.isMoving).toBe(true);
    expect(m.laneFacingSign).toBe(1);
  });

  it('掴まれている間は動かず、踏み込み・ノックバック・吹き飛びの残りも切る', () => {
    const m = new PlayerMotor();
    m.lunge(1, 0.5);
    m.knockback(1, 0.5);
    m.launchBack(0.5, 0.3);
    expect(m.isAirborne).toBe(true);
    m.setGrappled(true);
    const s = stats();
    run(m, s, 1, 10);
    expect(m.isMoving).toBe(false);
    expect(m.snapshot().lungeTravel).toBe(0);
    expect(m.snapshot().knockLeft).toBe(0);
    expect(m.isAirborne).toBe(false);
  });

  it('LaunchBack は v=√(2gh), air=2v/g で放物線の滞空を決める', () => {
    const m = new PlayerMotor();
    const g = Math.abs(DEFAULT_MOTOR_CONFIG.gravity);
    const height = 0.5;
    const air = m.launchBack(1, height);
    const v = Math.sqrt(2 * g * height);
    expect(air).toBeCloseTo(2 * v / g);
    expect(m.isAirborne).toBe(true);
    // 滞空時間だけ進めると着地する
    run(m, stats(), 0, Math.ceil(air * 60) + 2);
    expect(m.isAirborne).toBe(false);
  });

  it('ベルトの左右端を超えない', () => {
    const m = new PlayerMotor();
    m.forwardLimitX = 2;
    m.backLimitX = -1;
    run(m, stats(), 1, 180);
    expect(m.position.x).toBeLessThanOrEqual(2);
    run(m, stats(), -1, 180);
    expect(m.position.x).toBeGreaterThanOrEqual(-1);
  });

  it('LaneZOverride は lateTick で Z へ反映され、解除で元のレーンへ戻る', () => {
    const m = new PlayerMotor();
    m.lateTick();
    m.setLaneZOverride(1.5);
    m.lateTick();
    expect(m.position.z).toBeCloseTo(1.5);
    m.clearLaneZOverride();
    m.lateTick();
    expect(m.position.z).toBe(m.laneZ);
  });
});
