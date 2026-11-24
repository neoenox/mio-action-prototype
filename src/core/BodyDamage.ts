import { BodyPart, CombatConstants } from './CombatConstants.js';

/**
 * 部位ごとの蓄積ダメージ。0 = 無傷 / 100 = 限界。
 * ステージ内では自然回復しない。
 */
export class BodyDamage {
  private readonly values: number[] = [0, 0, 0];

  get(part: BodyPart): number {
    return this.values[part as number]!;
  }

  apply(part: BodyPart, amount: number): void {
    if (amount <= 0) return;
    const index = part as number;
    this.values[index] = BodyDamage.clampPart(this.values[index]! + amount);
  }

  reset(): void {
    for (let i = 0; i < this.values.length; i++) this.values[i] = 0;
  }

  get total(): number {
    return this.values[0]! + this.values[1]! + this.values[2]!;
  }

  /** 脚の損傷による移動速度倍率（1.0 → 0.70）。 */
  get moveSpeedMultiplier(): number {
    return BodyDamage.lerp(
      1,
      CombatConstants.LegsSpeedFloor,
      this.get(BodyPart.Legs) / CombatConstants.PartMax,
    );
  }

  /** 腕の損傷による与ダメージ倍率（1.0 → 0.75）。 */
  get attackDamageMultiplier(): number {
    return BodyDamage.lerp(
      1,
      CombatConstants.ArmsDamageFloor,
      this.get(BodyPart.Arms) / CombatConstants.PartMax,
    );
  }

  private static lerp(a: number, b: number, t: number): number {
    return a + (b - a) * BodyDamage.clamp01(t);
  }

  private static clamp01(v: number): number {
    return v < 0 ? 0 : v > 1 ? 1 : v;
  }

  private static clampPart(v: number): number {
    return v < 0 ? 0 : v > CombatConstants.PartMax ? CombatConstants.PartMax : v;
  }
}
