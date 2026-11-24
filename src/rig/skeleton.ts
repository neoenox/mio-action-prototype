import { Object3D, Quaternion, Vector3 } from 'three';

/**
 * glTF / Three.js は Y-up 右手系で、キャラクタの前はローカル -Z（glTF 規約）。
 * Unity は前が +Z。Unity の `Vector3.forward` に相当するものを `RIG_FORWARD` に
 * まとめ、probe 側ではこの軸だけを「体の前」として扱う（world +Z を直接使わない）。
 * これで VRM（Unity で +Z を向いていたモデルを glTF 化したもの）でも、
 * 自作プリミティブでも同じ計測式が使える。
 */
export const RIG_RIGHT = new Vector3(1, 0, 0);
export const RIG_UP = new Vector3(0, 1, 0);
export const RIG_FORWARD = new Vector3(0, 0, -1);

/** 正規化スケルトンが持つボーン名のキー。 */
export type JointBoneKey =
  | 'root'
  | 'hips'
  | 'spine'
  | 'chest'
  | 'neck'
  | 'head'
  | 'lUpperArm' | 'lLowerArm' | 'lHand'
  | 'rUpperArm' | 'rLowerArm' | 'rHand'
  | 'lUpperLeg' | 'lLowerLeg' | 'lFoot'
  | 'rUpperLeg' | 'rLowerLeg' | 'rFoot';

export const JOINT_BONE_KEYS: readonly JointBoneKey[] = [
  'root', 'hips', 'spine', 'chest', 'neck', 'head',
  'lUpperArm', 'lLowerArm', 'lHand', 'rUpperArm', 'rLowerArm', 'rHand',
  'lUpperLeg', 'lLowerLeg', 'lFoot', 'rUpperLeg', 'rLowerLeg', 'rFoot',
] as const;

/** 12 関節の計測に必要なボーン（指・足首は対象外だが IK の末端として使う）。 */
export type JointSkeletonBones = Record<JointBoneKey, Object3D | null>;

/**
 * Unity `JointSkeleton` の TS 版。
 *
 * - Rest 回転は「root から見た姿勢」で控える。VRM のボーン軸がモデルごとに違っても、
 *   「初期姿勢からどれだけ回ったか」をモデル非依存で測れる。
 * - 蝶番軸は上腕／腿のローカル座標で持つ。肩をひねっても脚を開いても軸が付いてくる。
 */
export interface JointSkeleton {
  readonly bones: JointSkeletonBones;
  readonly restRotations: Readonly<Partial<Record<JointBoneKey, Quaternion>>>;
  readonly hasRestRotations: boolean;

  /** 肘が曲がる軸（上腕ローカル）。正の回転 = 前腕が前へ折れる。 */
  readonly elbowHingeLocalL: Vector3;
  readonly elbowHingeLocalR: Vector3;
  /** 膝が曲がる軸（腿ローカル）。正の回転 = すねが後ろへ折れる。 */
  readonly kneeHingeLocalL: Vector3;
  readonly kneeHingeLocalR: Vector3;
  readonly hasHinges: boolean;

  readonly isValid: boolean;
}

/** `JointSkeleton` の simple ファクトリ（テストで骨を差し替えるときに使う）。 */
export function createJointSkeleton(
  bones: Partial<JointSkeletonBones>,
  options: {
    restRotations?: Partial<Record<JointBoneKey, Quaternion>>;
    hasRestRotations?: boolean;
    elbowHingeLocalL?: Vector3;
    elbowHingeLocalR?: Vector3;
    kneeHingeLocalL?: Vector3;
    kneeHingeLocalR?: Vector3;
    hasHinges?: boolean;
  } = {},
): JointSkeleton {
  const full = emptyBones();
  for (const key of JOINT_BONE_KEYS) {
    const bone = bones[key];
    if (bone) full[key] = bone;
  }
  return finalizeSkeleton(full, options);
}

function emptyBones(): JointSkeletonBones {
  return {
    root: null, hips: null, spine: null, chest: null, neck: null, head: null,
    lUpperArm: null, lLowerArm: null, lHand: null,
    rUpperArm: null, rLowerArm: null, rHand: null,
    lUpperLeg: null, lLowerLeg: null, lFoot: null,
    rUpperLeg: null, rLowerLeg: null, rFoot: null,
  };
}

function finalizeSkeleton(
  bones: JointSkeletonBones,
  options: {
    restRotations?: Partial<Record<JointBoneKey, Quaternion>>;
    hasRestRotations?: boolean;
    elbowHingeLocalL?: Vector3;
    elbowHingeLocalR?: Vector3;
    kneeHingeLocalL?: Vector3;
    kneeHingeLocalR?: Vector3;
    hasHinges?: boolean;
  },
): JointSkeleton {
  return {
    bones,
    restRotations: options.restRotations ?? {},
    hasRestRotations: options.hasRestRotations ?? false,
    elbowHingeLocalL: options.elbowHingeLocalL ?? new Vector3(),
    elbowHingeLocalR: options.elbowHingeLocalR ?? new Vector3(),
    kneeHingeLocalL: options.kneeHingeLocalL ?? new Vector3(),
    kneeHingeLocalR: options.kneeHingeLocalR ?? new Vector3(),
    hasHinges: options.hasHinges ?? false,
    isValid: bones.root !== null && bones.hips !== null && bones.chest !== null && bones.head !== null,
  };
}

/** root から見たボーンの回転（Unity `RelativeRotationToRoot` 相当）。 */
export function relativeRotationToRoot(root: Object3D, bone: Object3D): Quaternion {
  const rootWorld = root.getWorldQuaternion(new Quaternion());
  const boneWorld = bone.getWorldQuaternion(new Quaternion());
  return rootWorld.invert().multiply(boneWorld);
}

/**
 * 肘の蝶番軸を上腕ローカルで求める（Unity `ElbowHingeLocal` 相当）。
 * 初期姿勢の上腕の向き × 体の前。前腕が前へ折れる向きが正。
 */
export function elbowHingeLocal(lowerArm: Object3D | null, restUpperArm: Quaternion): Vector3 {
  if (!lowerArm) return new Vector3();
  const arm = lowerArm.position.clone(); // localPosition 相当
  if (arm.lengthSq() < 1e-8) return new Vector3();

  const restArmInRoot = arm.normalize().applyQuaternion(restUpperArm);
  let hinge = new Vector3().crossVectors(restArmInRoot, RIG_FORWARD);
  if (hinge.lengthSq() < 1e-6) hinge = new Vector3().crossVectors(restArmInRoot, RIG_UP);
  if (hinge.lengthSq() < 1e-6) return new Vector3();

  hinge.normalize();
  // Unity(左手系) → Three(右手系) では forward の符号が反転するため、
  // Cross の向きも反転する。Unity と同じ「前へ折れる＝正」にそろえるため反転する。
  hinge.negate();
  return hinge.applyQuaternion(restUpperArm.clone().invert());
}

/** 膝の蝶番軸を腿のローカルで求める（Unity `KneeHingeLocal` 相当）。 */
export function kneeHingeLocal(restUpperLeg: Quaternion): Vector3 {
  return RIG_RIGHT.clone().negate().applyQuaternion(restUpperLeg.clone().invert()).normalize();
}

/**
 * GLTF のボーン名 → 正規化スケルトン。
 *
 * 既定で VRM (`J_Bip_C_Hips` 等)、Mixamo (`mixamorig:Hips` 等)、
 * 素の名前 (`Hips`, `Chest`, `LeftUpperArm` 等) を順に試す。
 * `nameMap` でモデル固有の名前を上書きできる。
 */
export interface SkeletonBuildOptions {
  /** キー → ボーン名の overrides（部分指定可）。 */
  readonly nameMap?: Partial<Record<JointBoneKey, string>>;
  /** 探索を deep にする（既定 true）。 */
  readonly recursive?: boolean;
}

const DEFAULT_NAME_CANDIDATES: Record<JointBoneKey, readonly string[]> = {
  root: [],
  hips: ['J_Bip_C_Hips', 'mixamorig:Hips', 'Hips', 'Pelvis'],
  spine: ['J_Bip_C_Spine', 'mixamorig:Spine', 'Spine'],
  chest: ['J_Bip_C_Chest', 'mixamorig:Spine2', 'Chest', 'UpperChest'],
  neck: ['J_Bip_C_Neck', 'mixamorig:Neck', 'Neck'],
  head: ['J_Bip_C_Head', 'mixamorig:Head', 'Head'],
  lUpperArm: ['J_Bip_L_UpperArm', 'mixamorig:LeftArm', 'LeftUpperArm', 'LUpperArm'],
  lLowerArm: ['J_Bip_L_LowerArm', 'mixamorig:LeftForeArm', 'LeftLowerArm', 'LLowerArm'],
  lHand: ['J_Bip_L_Hand', 'mixamorig:LeftHand', 'LeftHand', 'LHand'],
  rUpperArm: ['J_Bip_R_UpperArm', 'mixamorig:RightArm', 'RightUpperArm', 'RUpperArm'],
  rLowerArm: ['J_Bip_R_LowerArm', 'mixamorig:RightForeArm', 'RightLowerArm', 'RLowerArm'],
  rHand: ['J_Bip_R_Hand', 'mixamorig:RightHand', 'RightHand', 'RHand'],
  lUpperLeg: ['J_Bip_L_UpperLeg', 'mixamorig:LeftUpLeg', 'LeftUpperLeg', 'LUpperLeg'],
  lLowerLeg: ['J_Bip_L_LowerLeg', 'mixamorig:LeftLeg', 'LeftLowerLeg', 'LLowerLeg'],
  lFoot: ['J_Bip_L_Foot', 'mixamorig:LeftFoot', 'LeftFoot', 'LFoot'],
  rUpperLeg: ['J_Bip_R_UpperLeg', 'mixamorig:RightUpLeg', 'RightUpperLeg', 'RUpperLeg'],
  rLowerLeg: ['J_Bip_R_LowerLeg', 'mixamorig:RightLeg', 'RightLowerLeg', 'RLowerLeg'],
  rFoot: ['J_Bip_R_Foot', 'mixamorig:RightFoot', 'RightFoot', 'RFoot'],
};

function findByName(root: Object3D, names: readonly string[], recursive: boolean): Object3D | null {
  for (const name of names) {
    const found = root.getObjectByName(name);
    if (found) return found;
  }
  if (!recursive) return null;
  // 名前が partial に一致するケース（blender の "Hips.001" 等）を拾う。
  for (const name of names) {
    let found: Object3D | null = null;
    root.traverse((o) => {
      if (!found && (o.name === name || o.name.startsWith(`${name}.`))) found = o;
    });
    if (found) return found;
  }
  return null;
}

/**
 * GLTF シーンから `JointSkeleton` を組み立てる。
 * Rest 姿勢は「いまのボーン配置」を Capture する（Unity `ProceduralRig.Capture()` 相当）。
 * 読み込み直後に呼ぶこと。
 */
export function buildJointSkeleton(root: Object3D, options: SkeletonBuildOptions = {}): JointSkeleton {
  const recursive = options.recursive ?? true;
  const bones = emptyBones();
  bones.root = root;

  for (const key of JOINT_BONE_KEYS) {
    if (key === 'root') continue;
    const override = options.nameMap?.[key];
    const names = override ? [override, ...DEFAULT_NAME_CANDIDATES[key]] : DEFAULT_NAME_CANDIDATES[key];
    bones[key] = findByName(root, names, recursive);
  }

  const rest: Partial<Record<JointBoneKey, Quaternion>> = {};
  for (const key of JOINT_BONE_KEYS) {
    const bone = bones[key];
    if (bone) rest[key] = relativeRotationToRoot(root, bone);
  }

  const hasRest = bones.hips !== null && bones.chest !== null && bones.head !== null &&
    rest.hips !== undefined && rest.chest !== undefined && rest.head !== undefined;

  const restArmL = rest.lUpperArm;
  const restArmR = rest.rUpperArm;
  const restLegL = rest.lUpperLeg;
  const restLegR = rest.rUpperLeg;
  const hasHinges = restArmL !== undefined && restArmR !== undefined &&
    restLegL !== undefined && restLegR !== undefined;

  return finalizeSkeleton(bones, {
    restRotations: rest,
    hasRestRotations: hasRest,
    elbowHingeLocalL: restArmL ? elbowHingeLocal(bones.lLowerArm, restArmL) : new Vector3(),
    elbowHingeLocalR: restArmR ? elbowHingeLocal(bones.rLowerArm, restArmR) : new Vector3(),
    kneeHingeLocalL: restLegL ? kneeHingeLocal(restLegL) : new Vector3(),
    kneeHingeLocalR: restLegR ? kneeHingeLocal(restLegR) : new Vector3(),
    hasHinges,
  });
}

/**
 * プリミティブ敵（EnemyFigure 相当）用。関節の初期回転は root と同じ（identity）なので、
 * rest を identity として扱う点が VRM とだけ違う。
 */
export function buildPrimitiveSkeleton(
  root: Object3D,
  nameMap: Partial<Record<JointBoneKey, string>>,
): JointSkeleton {
  const built = buildJointSkeleton(root, { nameMap });
  const identity = new Quaternion();
  const rest: Partial<Record<JointBoneKey, Quaternion>> = {};
  for (const key of JOINT_BONE_KEYS) {
    if (built.bones[key]) rest[key] = identity.clone();
  }
  return finalizeSkeleton(built.bones, {
    restRotations: rest,
    hasRestRotations: true,
    elbowHingeLocalL: built.bones.lLowerArm ? elbowHingeLocal(built.bones.lLowerArm, identity) : new Vector3(),
    elbowHingeLocalR: built.bones.rLowerArm ? elbowHingeLocal(built.bones.rLowerArm, identity) : new Vector3(),
    kneeHingeLocalL: kneeHingeLocal(identity),
    kneeHingeLocalR: kneeHingeLocal(identity),
    hasHinges: true,
  });
}

/** Unity の JointSkeletonBuilder 互換の名前。 */
export const JointSkeletonBuilder = {
  fromGltf: buildJointSkeleton,
  fromPrimitive: buildPrimitiveSkeleton,
  elbowHingeLocal,
  kneeHingeLocal,
} as const;
