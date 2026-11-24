import { BodyPart } from './CombatConstants.js';

export enum AttackKind {
  Light = 0,
  Heavy = 1,
  Break = 2,
  Grapple = 3,
}

/** 1つの攻撃の定義。フレームデータは 60fps 基準で秒に換算して持つ。 */
export class AttackData {
  id = 'light';
  kind: AttackKind = AttackKind.Light;
  damage = 8;
  partDamage = 6;
  targetPart: BodyPart = BodyPart.Torso;
  startupSeconds = 0.05;
  activeSeconds = 0.08;
  recoverySeconds = 0.18;
  /** 踏み込み。発生の瞬間までに root が前へ出る距離（m）。 */
  lungeDistance = 0.05;
  /** 0 のままなら kind から既定値を出す。 */
  flinch = 0;

  get flinchValue(): number {
    return this.flinch > 0 ? this.flinch : AttackData.defaultFlinch(this.kind);
  }

  get totalSeconds(): number {
    return this.startupSeconds + this.activeSeconds + this.recoverySeconds;
  }

  private static defaultFlinch(kind: AttackKind): number {
    switch (kind) {
      case AttackKind.Light: return 12;
      case AttackKind.Heavy: return 26;
      case AttackKind.Break: return 34;
      case AttackKind.Grapple: return 6;
      default: return 0;
    }
  }
}

/** SPEC §4.5 の攻撃テーブル。3F / 9F / 15F の発生を秒に直してある。 */
export const AttackLibrary = {
  light(): AttackData {
    const a = new AttackData();
    a.id = 'light';
    a.kind = AttackKind.Light;
    a.damage = 8;
    a.partDamage = 6;
    a.targetPart = BodyPart.Torso;
    a.startupSeconds = 6 / 60;
    a.activeSeconds = 4 / 60;
    a.recoverySeconds = 10 / 60;
    a.lungeDistance = 0.38;
    return a;
  },

  heavy(): AttackData {
    const a = new AttackData();
    a.id = 'heavy';
    a.kind = AttackKind.Heavy;
    a.damage = 18;
    a.partDamage = 14;
    a.targetPart = BodyPart.Torso;
    a.startupSeconds = 12 / 60;
    a.activeSeconds = 5 / 60;
    a.recoverySeconds = 18 / 60;
    a.lungeDistance = 0.42;
    return a;
  },

  breakAttack(): AttackData {
    const a = new AttackData();
    a.id = 'break';
    a.kind = AttackKind.Break;
    a.damage = 14;
    a.partDamage = 10;
    a.targetPart = BodyPart.Legs;
    a.startupSeconds = 18 / 60;
    a.activeSeconds = 5 / 60;
    a.recoverySeconds = 24 / 60;
    a.lungeDistance = 0.34;
    return a;
  },

  /** 掴み。ダメージはほぼ無く、当たった後は GrappleResistance に移る。 */
  grappleAttack(): AttackData {
    const a = new AttackData();
    a.id = 'grapple';
    a.kind = AttackKind.Grapple;
    a.damage = 3;
    a.partDamage = 0;
    a.targetPart = BodyPart.Arms;
    a.startupSeconds = 33 / 60;
    a.activeSeconds = 5 / 60;
    a.recoverySeconds = 30 / 60;
    a.lungeDistance = 0.1;
    return a;
  },
} as const;
