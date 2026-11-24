/**
 * rig-port の公開 API。app（lead が統合）はここだけを import する。
 *
 *   const player = createPlayerRig();
 *   scene.add(player.root);
 *   player.animator.tick({ time, dt, ... });
 *
 *   const enemy = createEnemyRig();
 *   scene.add(enemy.root);
 *   hooks.getPunchAnchor = enemy.getPunchAnchor; // Vector3 は {x,y,z} として渡せる
 */
import { Group, Mesh, Object3D, Vector3 } from 'three';
import {
  BuiltHumanoid,
  BuildHumanoidOptions,
  DEFAULT_ENEMY_COLOR,
  DEFAULT_PLAYER_COLOR,
  buildPrimitiveHumanoid,
} from './bones.js';
import {
  PunchZoneLike,
  createPunchAnchorHook,
  punchAnchorFromSkeleton,
} from './anchors.js';
import {
  AnimatorSettings,
  CharacterAnimator,
  ProceduralRig,
} from './animator.js';
import { JointSkeleton, JointSkeletonBones } from './skeleton.js';

export * from './skeleton.js';
export * from './animator.js';
export * from './anchors.js';
export * from './limits.js';
export {
  DEFAULT_ENEMY_COLOR,
  DEFAULT_PLAYER_COLOR,
  FACE_FORWARD_LOCAL,
  buildPrimitiveHumanoid,
} from './bones.js';
export type {
  BuildHumanoidOptions,
  BuiltHumanoid,
} from './bones.js';

export interface PlayerRigOptions extends BuildHumanoidOptions {
  readonly animatorSettings?: AnimatorSettings;
}

export interface PlayerRig {
  /** シーンへそのまま add できるルート。 */
  readonly root: Group;
  /** 既存の JointSkeleton 互換（probe / ROM 計測に渡せる）。 */
  readonly skeleton: JointSkeleton;
  /** ボーン辞書。直接触るならこれ経由。 */
  readonly bones: JointSkeletonBones;
  /** 実ボーンを回す薄いレイヤー。 */
  readonly proceduralRig: ProceduralRig;
  /** 状態機械つきのポーズドライバ。 */
  readonly animator: CharacterAnimator;
  /** ルートにぶら下がったメッシュ一覧。 */
  readonly meshes: Mesh[];
  /** logic の EnemyBrainHooks へ渡す想定の拳アンカー。 */
  readonly getPunchAnchor: (zone: PunchZoneLike) => Vector3 | null;
  /** シーンへ追加しつつ初期位置を設定する便利メソッド。 */
  attachTo(scene: Object3D, position?: { x?: number; y?: number; z?: number }): Group;
}

export interface EnemyRig {
  readonly root: Group;
  readonly skeleton: JointSkeleton;
  readonly bones: JointSkeletonBones;
  readonly proceduralRig: ProceduralRig;
  readonly animator: CharacterAnimator;
  readonly meshes: Mesh[];
  readonly getPunchAnchor: (zone: PunchZoneLike) => Vector3 | null;
  attachTo(scene: Object3D, position?: { x?: number; y?: number; z?: number }): Group;
}

function assemble(
  built: BuiltHumanoid,
  settings?: AnimatorSettings,
): Omit<PlayerRig, 'attachTo'> {
  const proceduralRig = new ProceduralRig(built.root, built.skeleton);
  const animator = new CharacterAnimator(proceduralRig, settings);
  const getPunchAnchor = (zone: PunchZoneLike) =>
    punchAnchorFromSkeleton(built.skeleton, zone);

  // Animator の anchors にも同じ実ボーンを結び付ける（pose 計算からも参照できる）。
  animator.anchors = { punchAnchor: createPunchAnchorHook(built.skeleton) };

  return {
    root: built.root,
    skeleton: built.skeleton,
    bones: built.bones,
    proceduralRig,
    animator,
    meshes: built.meshes,
    getPunchAnchor,
  };
}

type RigCore = Omit<PlayerRig, 'attachTo'>;

function attach(
  core: RigCore,
  scene: Object3D,
  position?: { x?: number; y?: number; z?: number },
): Group {
  if (position?.x !== undefined) core.root.position.x = position.x;
  if (position?.y !== undefined) core.root.position.y = position.y;
  if (position?.z !== undefined) core.root.position.z = position.z;
  scene.add(core.root);
  return core.root;
}

function withAttach(core: RigCore): PlayerRig {
  return { ...core, attachTo: (scene, position) => attach(core, scene, position) };
}

/** プレイヤー（澪）用の実リグ。Animator を同時に組み立てて返す。 */
export function createPlayerRig(options: PlayerRigOptions = {}): PlayerRig {
  const { animatorSettings, ...buildOptions } = options;
  const built = buildPrimitiveHumanoid({
    color: DEFAULT_PLAYER_COLOR,
    namePrefix: 'P-',
    ...buildOptions,
  });
  return withAttach(assemble(built, animatorSettings));
}

/** 敵用の実リグ。色を変えるだけで同じ骨格・同じ Anchors を使う。 */
export function createEnemyRig(
  color: number = DEFAULT_ENEMY_COLOR,
  options: BuildHumanoidOptions = {},
): EnemyRig {
  const built = buildPrimitiveHumanoid({
    color,
    namePrefix: 'E-',
    ...options,
  });
  return withAttach(assemble(built));
}

/**
 * 既に Animator がある場合に、実スケルトンの拳アンカーを結び付ける。
 * 戻り値は解除関数（元の anchors 設定を復元する）。
 */
export function bindAnimator(
  animator: CharacterAnimator,
  skeleton: JointSkeleton,
): () => void {
  const previous = animator.anchors;
  animator.anchors = { punchAnchor: createPunchAnchorHook(skeleton) };
  return () => { animator.anchors = previous; };
}

/** シーン追加だけをまとめた薄いヘルパー（戻り値は追加された root）。 */
export function addRigToScene(
  rig: { readonly root: Group },
  scene: Object3D,
  position?: { x?: number; y?: number; z?: number },
): Group {
  if (position?.x !== undefined) rig.root.position.x = position.x;
  if (position?.y !== undefined) rig.root.position.y = position.y;
  if (position?.z !== undefined) rig.root.position.z = position.z;
  scene.add(rig.root);
  return rig.root;
}
