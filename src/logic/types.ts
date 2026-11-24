/**
 * logic が外部（rig / feel / app）へ依存せずに敵を扱うための最小インターフェース。
 * Unity の EnemyBrain のうち、ゲームロジックが実際に読む面だけを残す。
 * rig 側の実体（Three.js の Object3D を持つクラス）はこれを実装する。
 */
export interface EnemyActor {
  readonly id: string;
  /** 生死。Unity の IsAlive 相当。 */
  readonly isAlive: boolean;
  /** 役割。EncounterDirector が背丈から割り当てる。 */
  role?: import('../core/EnemyRole').EnemyRole;
  /** 有効／無効。Unity の isActiveAndEnabled 相当。 */
  readonly isActive: boolean;
  /** 移動速度 m/s。0 以下は候補から除外する。 */
  readonly moveSpeed: number;
  /** 現在位置。logic は x/z のみを使う（y は 0 平面）。 */
  readonly position: { x: number; y: number; z: number };
}

/** 掴み方・敵の役割は core が真実源。logic からは再 export して使う。 */
export { GrappleKind } from '../core/GrappleResistance';
export { EnemyRole, RoleCandidate, EnemyRoleRules } from '../core/EnemyRole';

/** 攻撃の着弾先。Unity の PunchZone 相当。 */
export enum PunchZone {
  Face = 0,
  Body = 1,
}

/** 敵の行動フェーズ。数値は HUD が参照するため Unity 版と同じ。 */
export enum EnemyPhase {
  Chase = 0,
  Windup = 1,
  Recover = 2,
  Dead = 3,
  Attack = 4,
  Stagger = 5,
  Strafe = 6,
}

/**
 * punch anchor の構造体。THREE.Vector3 でも自前の {x,y,z} でも受け取れるよう、
 * logic 側は x/y/z だけを要求する（three 非依存を保つ）。
 */
export interface PunchAnchor {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

/** 構造体だけを要求する点で THREE.Vector3 もそのまま渡せる。 */
export type Vector3Like = PunchAnchor;

/**
 * rig 側が実装する getPunchAnchor の zone 名。
 * logic の PunchZone（Face/Body）と 'head'/'body' を createHooksFromRig で変換する。
 */
export type RigPunchZone = 'head' | 'body';

/** PunchZone → rig 側 zone 名。rig へ渡すときだけ使う。 */
export function toRigPunchZone(zone: PunchZone): RigPunchZone {
  return zone === PunchZone.Face ? 'head' : 'body';
}

/**
 * 敵Brain がダメージ解決・当たり判定のために外へ要求する最小の口。
 * EnemyBrain は rig を直接持たず、この関数だけを注入される（getPunchAnchor(zone)）。
 * 戻り値は構造体のみ。THREE.Vector3 をそのまま返す rig もそのまま繋がる。
 */
export type PunchAnchorProvider = (zone: PunchZone) => PunchAnchor | null;
