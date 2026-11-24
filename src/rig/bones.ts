/**
 * 実リグの組み立て（Unity の ProceduralRig が VRM にぶら下げていたボーンを
 * Three.js の Group / Bone として作り直す）。
 *
 * これまで skeleton.ts / animator.ts は Object3D を外から注入する前提で、
 * 数値テストだけが走っていた。ここでは「ボーンが実際に Hierarchy にぶら下がり、
 * メッシュが追随する」ところまでを担う。座標系は glTF 規約（前 = -Z、上 = +Y）。
 */
import {
  Bone,
  BoxGeometry,
  CapsuleGeometry,
  Group,
  Mesh,
  MeshStandardMaterial,
  Object3D,
  SphereGeometry,
  Vector3,
} from 'three';
import {
  JointSkeleton,
  JointSkeletonBones,
  buildPrimitiveSkeleton,
} from './skeleton.js';

/**
 * 実リグの顔が向くローカル方向（glTF 規約）。髪は反対側の +Z に置く。
 * headBoneFromObject() はこの向きを forward として渡す必要がある（visual-port 側の推奨値）。
 */
export const FACE_FORWARD_LOCAL = new Vector3(0, 0, -1);

/** 既定色。app 側の仮プロキシ（hero / enemy）と同じ配色。 */
export const DEFAULT_PLAYER_COLOR = 0x6aa9ff;
export const DEFAULT_ENEMY_COLOR = 0xd0605a;
const DEFAULT_SKIN_COLOR = 0xe8c9a8;
const DEFAULT_HAIR_COLOR = 0x1c1c1e;

export interface BuildHumanoidOptions {
  readonly color?: number;
  readonly skinColor?: number;
  readonly hairColor?: number;
  /** 全体スケール。既定 1（身長約 1.8m）。 */
  readonly scale?: number;
  /** ボーン名の接頭辞（複数体を同じシーンに置くときの衝突回避）。 */
  readonly namePrefix?: string;
}

export interface BuiltHumanoid {
  readonly root: Group;
  readonly bones: JointSkeletonBones;
  readonly skeleton: JointSkeleton;
  readonly meshes: Mesh[];
}

/** 正規化スケルトンが探す名前（skeleton.ts の DEFAULT_NAME_CANDIDATES と一致させる）。 */
const BONE_NAMES = {
  hips: 'Hips',
  spine: 'Spine',
  chest: 'Chest',
  neck: 'Neck',
  head: 'Head',
  lUpperArm: 'LeftUpperArm',
  lLowerArm: 'LeftLowerArm',
  lHand: 'LeftHand',
  rUpperArm: 'RightUpperArm',
  rLowerArm: 'RightLowerArm',
  rHand: 'RightHand',
  lUpperLeg: 'LeftUpperLeg',
  lLowerLeg: 'LeftLowerLeg',
  lFoot: 'LeftFoot',
  rUpperLeg: 'RightUpperLeg',
  rLowerLeg: 'RightLowerLeg',
  rFoot: 'RightFoot',
} as const;

type NamedBone = keyof typeof BONE_NAMES;

/**
 * 7 頭身・身長約 1.8m のプリミティブ人間を組み立てる。
 *
 * root は Group（シーンへそのまま add できる）、関節はすべて Bone。
 * 各ボーンの position は「親ボーンから見たオフセット」で、IK / aim が
 * そのまま使える。Rest 回転はすべて identity。
 */
export function buildPrimitiveHumanoid(options: BuildHumanoidOptions = {}): BuiltHumanoid {
  const color = options.color ?? DEFAULT_PLAYER_COLOR;
  const skinColor = options.skinColor ?? DEFAULT_SKIN_COLOR;
  const hairColor = options.hairColor ?? DEFAULT_HAIR_COLOR;
  const scale = options.scale ?? 1;
  const prefix = options.namePrefix ?? '';

  const cloth = new MeshStandardMaterial({ color, roughness: 0.68, metalness: 0.05 });
  const skin = new MeshStandardMaterial({ color: skinColor, roughness: 0.82 });
  const hair = new MeshStandardMaterial({ color: hairColor, roughness: 0.9 });
  const sole = new MeshStandardMaterial({ color: 0x15151a, roughness: 0.95 });

  const root = new Group();
  root.name = `${prefix}Root`;
  root.scale.setScalar(scale);

  const name = (key: NamedBone) => `${prefix}${BONE_NAMES[key]}`;
  const bone = (key: NamedBone, parent: Object3D, x: number, y: number, z: number): Bone => {
    const b = new Bone();
    b.name = name(key);
    b.position.set(x, y, z);
    parent.add(b);
    return b;
  };

  const meshes: Mesh[] = [];
  const addMesh = (parent: Object3D, mesh: Mesh): Mesh => {
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    parent.add(mesh);
    meshes.push(mesh);
    return mesh;
  };

  /** 親ボーンから子ボーンへ伸びる Capsule を親にぶら下げる。 */
  const limb = (
    parent: Bone,
    childLocal: Vector3,
    radius: number,
    material: MeshStandardMaterial,
    meshName: string,
  ): Mesh => {
    const length = childLocal.length();
    const geometry = new CapsuleGeometry(radius, Math.max(0.01, length - radius * 2), 4, 10);
    const mesh = new Mesh(geometry, material);
    mesh.name = `${prefix}${meshName}`;
    mesh.position.copy(childLocal).multiplyScalar(0.5);
    if (length > 1e-6) {
      mesh.quaternion.setFromUnitVectors(new Vector3(0, 1, 0), childLocal.clone().normalize());
    }
    return addMesh(parent, mesh);
  };

  // --- 体幹 ---------------------------------------------------------------
  const hips = bone('hips', root, 0, 0.92, 0);
  const spine = bone('spine', hips, 0, 0.18, 0);
  const chest = bone('chest', spine, 0, 0.26, 0);
  const neck = bone('neck', chest, 0, 0.14, 0);
  const head = bone('head', neck, 0, 0.10, 0);

  const pelvis = new Mesh(new BoxGeometry(0.30, 0.19, 0.21), cloth);
  pelvis.name = `${prefix}PelvisMesh`;
  pelvis.position.set(0, -0.03, 0);
  addMesh(hips, pelvis);
  limb(spine, new Vector3(0, 0.26, 0), 0.145, cloth, 'SpineMesh');
  limb(chest, new Vector3(0, 0.14, 0), 0.175, cloth, 'ChestMesh');

  const headMesh = new Mesh(new SphereGeometry(0.125, 20, 16), skin);
  headMesh.name = `${prefix}HeadMesh`;
  headMesh.position.set(0, 0.075, 0);
  addMesh(head, headMesh);

  // glTF 規約どおり「顔 = ローカル -Z、後頭部 = +Z」にする。
  // 髪は後頭部側（+Z）だけに置き、前髪で顔を隠さない。
  const hairMesh = new Mesh(new SphereGeometry(0.128, 18, 14), hair);
  hairMesh.name = `${prefix}HairMesh`;
  hairMesh.position.set(0, 0.075, 0.045);
  hairMesh.scale.set(1, 0.92, 0.72);
  addMesh(head, hairMesh);

  // 顔パーツは头ボーンの -Z 側（前）。目・口が無いとカメラ側が後頭部でも
  // face-visibility の visible だけが 1 になるため、見た目でも向きを判定できるようにする。
  const faceDark = new MeshStandardMaterial({ color: 0x14121a, roughness: 0.6 });
  const eyeGeometry = new SphereGeometry(0.017, 10, 8);
  for (const [side, x] of [['L', -0.046], ['R', 0.046]] as const) {
    const eye = new Mesh(eyeGeometry, faceDark);
    eye.name = `${prefix}Eye${side}Mesh`;
    eye.position.set(x, 0.095, -0.122);
    eye.scale.set(1, 0.72, 0.4);
    addMesh(head, eye);
  }
  const mouth = new Mesh(new SphereGeometry(0.019, 10, 8), faceDark);
  mouth.name = `${prefix}MouthMesh`;
  mouth.position.set(0, 0.035, -0.124);
  mouth.scale.set(1.25, 0.32, 0.35);
  addMesh(head, mouth);
  const nose = new Mesh(new SphereGeometry(0.014, 10, 8), skin);
  nose.name = `${prefix}NoseMesh`;
  nose.position.set(0, 0.066, -0.132);
  nose.scale.set(0.7, 1, 0.8);
  addMesh(head, nose);

  // --- 腕（肩 → 肘 → 手）---------------------------------------------------
  const armOffsets: ReadonlyArray<{
    upper: NamedBone; lower: NamedBone; hand: NamedBone; x: number;
  }> = [
    { upper: 'lUpperArm', lower: 'lLowerArm', hand: 'lHand', x: -0.20 },
    { upper: 'rUpperArm', lower: 'rLowerArm', hand: 'rHand', x: 0.20 },
  ];
  for (const arm of armOffsets) {
    const upper = bone(arm.upper, chest, arm.x, 0.09, 0);
    const lower = bone(arm.lower, upper, arm.x > 0 ? 0.035 : -0.035, -0.28, 0);
    const hand = bone(arm.hand, lower, 0, -0.26, 0);
    limb(upper, lower.position, 0.056, cloth, `${arm.upper}Mesh`);
    limb(lower, hand.position, 0.045, skin, `${arm.lower}Mesh`);
    const fist = new Mesh(new SphereGeometry(0.060, 12, 10), skin);
    fist.name = `${prefix}${arm.hand}Mesh`;
    fist.position.copy(hand.position).multiplyScalar(0.35);
    addMesh(hand, fist);
  }

  // --- 脚（股 → ひざ → 足首）-----------------------------------------------
  const legOffsets: ReadonlyArray<{
    upper: NamedBone; lower: NamedBone; foot: NamedBone; x: number;
  }> = [
    { upper: 'lUpperLeg', lower: 'lLowerLeg', foot: 'lFoot', x: -0.105 },
    { upper: 'rUpperLeg', lower: 'rLowerLeg', foot: 'rFoot', x: 0.105 },
  ];
  for (const leg of legOffsets) {
    const upper = bone(leg.upper, hips, leg.x, -0.04, 0);
    const lower = bone(leg.lower, upper, 0, -0.45, 0);
    const foot = bone(leg.foot, lower, 0, -0.43, 0);
    limb(upper, lower.position, 0.087, cloth, `${leg.upper}Mesh`);
    limb(lower, foot.position, 0.066, cloth, `${leg.lower}Mesh`);
    const shoe = new Mesh(new BoxGeometry(0.115, 0.085, 0.235), sole);
    shoe.name = `${prefix}${leg.foot}Mesh`;
    shoe.position.set(0, -0.025, -0.055); // -Z = 体の前
    addMesh(foot, shoe);
  }

  root.updateMatrixWorld(true);

  const bones: JointSkeletonBones = {
    root,
    hips,
    spine,
    chest,
    neck,
    head,
    lUpperArm: root.getObjectByName(name('lUpperArm')) as Bone | null,
    lLowerArm: root.getObjectByName(name('lLowerArm')) as Bone | null,
    lHand: root.getObjectByName(name('lHand')) as Bone | null,
    rUpperArm: root.getObjectByName(name('rUpperArm')) as Bone | null,
    rLowerArm: root.getObjectByName(name('rLowerArm')) as Bone | null,
    rHand: root.getObjectByName(name('rHand')) as Bone | null,
    lUpperLeg: root.getObjectByName(name('lUpperLeg')) as Bone | null,
    lLowerLeg: root.getObjectByName(name('lLowerLeg')) as Bone | null,
    lFoot: root.getObjectByName(name('lFoot')) as Bone | null,
    rUpperLeg: root.getObjectByName(name('rUpperLeg')) as Bone | null,
    rLowerLeg: root.getObjectByName(name('rLowerLeg')) as Bone | null,
    rFoot: root.getObjectByName(name('rFoot')) as Bone | null,
  };

  const nameMap = Object.fromEntries(
    (Object.keys(BONE_NAMES) as NamedBone[]).map((key) => [key, name(key)]),
  ) as Partial<Record<NamedBone, string>>;
  const skeleton = buildPrimitiveSkeleton(root, nameMap);
  return { root, bones, skeleton, meshes };
}
