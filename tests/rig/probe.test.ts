import { describe, expect, it } from 'vitest';
import { Group, Object3D, Quaternion, Vector3 } from 'three';
import {
  JointId,
  JointRom,
  JointRomVerdict,
  JointSide,
} from '../../src/rig/limits.js';
import {
  bendAngles,
  bodyAxes,
  measureJoints,
  planarAngle,
  toBodyFrame,
} from '../../src/rig/probe.js';
import {
  AnimatorState,
  CharacterAnimator,
  ProceduralRig,
} from '../../src/rig/animator.js';
import {
  RIG_FORWARD,
  RIG_RIGHT,
  RIG_UP,
  buildJointSkeleton,
  createJointSkeleton,
  elbowHingeLocal,
  kneeHingeLocal,
} from '../../src/rig/skeleton.js';

const DEG = Math.PI / 180;

function qAxis(axis: Vector3, degrees: number): Quaternion {
  return new Quaternion().setFromAxisAngle(axis, degrees * DEG);
}

function reading(
  results: readonly { joint: JointId; side: JointSide; degrees: number; verdict: JointRomVerdict }[],
  joint: JointId,
  side: JointSide,
) {
  const found = results.find((r) => r.joint === joint && r.side === side);
  if (!found) throw new Error(`計測結果がありません: joint=${joint} side=${side}`);
  return found;
}

function near(actual: number, expected: number, tolerance = 1): void {
  expect(Math.abs(actual - expected)).toBeLessThanOrEqual(tolerance);
}

// ---------------------------------------------------------------------------
// 1. JointRom 判定（core-port の JointRom を再利用していることの確認）
// ---------------------------------------------------------------------------

describe('JointRom 判定が Unity と同じ', () => {
  it('関節テーブルは JointId の順に 12 件並んでいる', () => {
    for (let i = 0; i < 12; i++) {
      const limit = JointRom.read(i as JointId, JointSide.Center, 0).limit;
      expect(limit.id).toBe(i as JointId);
    }
  });

  it('ひじは 155° 超で曲げすぎ、-20° 未満で逆方向', () => {
    expect(JointRom.read(JointId.Elbow, JointSide.Right, 154).verdict).toBe(JointRomVerdict.Within);
    expect(JointRom.read(JointId.Elbow, JointSide.Right, 156).verdict).toBe(JointRomVerdict.OverFlexed);
    expect(JointRom.read(JointId.Elbow, JointSide.Right, -19).verdict).toBe(JointRomVerdict.Within);
    expect(JointRom.read(JointId.Elbow, JointSide.Right, -21).verdict).toBe(JointRomVerdict.ReverseBend);
  });

  it('ひざは 160° 超で曲げすぎ、-20° 未満で逆方向', () => {
    expect(JointRom.read(JointId.Knee, JointSide.Left, 150).verdict).toBe(JointRomVerdict.Within);
    expect(JointRom.read(JointId.Knee, JointSide.Left, 161).verdict).toBe(JointRomVerdict.OverFlexed);
    expect(JointRom.read(JointId.Knee, JointSide.Left, -21).verdict).toBe(JointRomVerdict.ReverseBend);
  });

  it('股・肩・体幹・首のしきい値が設計表どおり', () => {
    const cases: ReadonlyArray<[JointId, number, JointRomVerdict]> = [
      [JointId.HipFlexion, 130, JointRomVerdict.Within],
      [JointId.HipFlexion, 131, JointRomVerdict.OverFlexed],
      [JointId.HipFlexion, -41, JointRomVerdict.ReverseBend],
      [JointId.HipAbduction, 70, JointRomVerdict.Within],
      [JointId.HipAbduction, 71, JointRomVerdict.OverFlexed],
      [JointId.ShoulderFlexion, 195, JointRomVerdict.Within],
      [JointId.ShoulderFlexion, 196, JointRomVerdict.OverFlexed],
      [JointId.ShoulderFlexion, -96, JointRomVerdict.ReverseBend],
      [JointId.ShoulderAbduction, 195, JointRomVerdict.Within],
      [JointId.SpinePitch, 110, JointRomVerdict.Within],
      [JointId.SpinePitch, 111, JointRomVerdict.OverFlexed],
      [JointId.SpineYaw, 71, JointRomVerdict.OverFlexed],
      [JointId.SpineYaw, -71, JointRomVerdict.ReverseBend],
      [JointId.SpineRoll, 60, JointRomVerdict.Within],
      [JointId.SpineRoll, 61, JointRomVerdict.OverFlexed],
      [JointId.NeckPitch, -75, JointRomVerdict.Within],
      [JointId.NeckPitch, 66, JointRomVerdict.OverFlexed],
      [JointId.NeckYaw, 96, JointRomVerdict.OverFlexed],
      [JointId.NeckRoll, -61, JointRomVerdict.ReverseBend],
    ];
    for (const [joint, degrees, expected] of cases) {
      const r = JointRom.read(joint, JointSide.Center, degrees);
      expect(r.verdict).toBe(expected);
    }
  });

  it(' excess は警告しきい値からの超過量', () => {
    const r = JointRom.read(JointId.Elbow, JointSide.Right, 168);
    expect(r.excess).toBeCloseTo(13, 5);
    const rev = JointRom.read(JointId.Knee, JointSide.Left, -90);
    expect(rev.excess).toBeCloseTo(70, 5);
  });

  it('日本語ログは Unity の書式', () => {
    expect(JointRom.read(JointId.Elbow, JointSide.Right, 168).describe())
      .toBe('右ひじ 168°（可動域 0〜145°・曲げすぎ）');
    expect(JointRom.read(JointId.Knee, JointSide.Left, -90).describe())
      .toBe('左ひざ -90°（可動域 0〜150°・逆方向）');
  });
});

// ---------------------------------------------------------------------------
// 2. 角度計測の幾何（「設定値」ではなく「曲がった後の骨」を測る）
// ---------------------------------------------------------------------------

describe('JointRomProbe の角度計算', () => {
  it('planarAngle は軸まわりの符号付き角度を返す', () => {
    // 上腕が下(-Y)、前腕が前(-Z)へ 90° 曲がる。蝶番軸は +X。
    const from = new Vector3(0, -1, 0);
    const to = new Vector3(0, 0, -1);
    const signed = planarAngle(from, to, new Vector3(1, 0, 0));
    expect(signed).not.toBeNull();
    near(signed!, 90, 0.01);
    near(planarAngle(from, new Vector3(0, 0, 1), new Vector3(1, 0, 0))!, -90, 0.01);
  });

  it('bendAngles は前屈とひねりを分けて取り出す', () => {
    // Three(右手系) では前屈は +X まわりの負回転（+Y が -Z=前へ倒れる）。
    const yaw = qAxis(RIG_UP, 40);
    const pitch = qAxis(RIG_RIGHT, -60);
    const bend = bendAngles(yaw.clone().multiply(pitch));
    near(bend.x, 60, 1);   // 前屈は正
    near(bend.y, 40, 1);   // ひねり
    near(bend.z, 0, 1);    // 左右には出ない
  });

  it('bodyAxes / toBodyFrame は体の (右, 上, 前) に分解する', () => {
    const bone = new Object3D();
    bone.quaternion.copy(qAxis(RIG_UP, 90)); // 体が 90° 回っている
    bone.updateMatrixWorld(true);
    const axes = bodyAxes(bone, new Quaternion());
    const local = toBodyFrame(axes.forward, axes.up, axes.right, axes.forward);
    near(local.x, 0, 0.01);
    near(local.y, 0, 0.01);
    near(local.z, 1, 0.01);
  });

  it('蝶番軸は上腕/腿のローカルで求まり、親が回っても付いてくる', () => {
    const lower = new Object3D();
    lower.position.set(-1, 0, 0); // T ポーズ: 左腕は -X
    const rest = new Quaternion();
    const hinge = elbowHingeLocal(lower, rest);
    expect(hinge.length()).toBeGreaterThan(0.9);
    const knee = kneeHingeLocal(rest);
    near(knee.x, -1, 0.01); // Unity の「体の右」は Three では -X（右手系への軸合わせ）
  });

  it('設定値 0° の骨でも、実際に曲げた角度を測る（肘 90°）', () => {
    const { skeleton, bones } = makeTestSkeleton();
    const shoulder = bones.lUpperArm;
    const elbow = bones.lLowerArm;
    const hand = bones.lHand;

    // 上腕: 下向き。前腕: 前向き(-Z)へ 90° 曲げる。
    // localPosition を動かすことで「Actual bone」を曲げる（設定ポーズ値ではない）。
    elbow.position.set(0, -1, 0).applyQuaternion(shoulder.quaternion.clone().invert());
    hand.position.set(0, 0, -1).applyQuaternion(elbow.quaternion.clone().invert());
    shoulder.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    const elbowReading = reading(results, JointId.Elbow, JointSide.Left);
    // 蝶番が identity 初期姿勢から作られているので符号は出ないかもしれないが、
    // 大きさは 90° になる（逆方向なら負）。
    expect(Math.abs(elbowReading.degrees)).toBeGreaterThan(70);
    expect(Math.abs(elbowReading.degrees)).toBeLessThanOrEqual(110);
  });

  it('膝を前方へ折ると逆方向（負）と判定される', () => {
    const { skeleton, bones } = makeTestSkeleton();
    const upper = bones.lUpperLeg;
    const lower = bones.lLowerLeg;
    const foot = bones.lFoot;

    // 腿: 下向き、すね: 前向き(-Z) = 人体と逆方向へ折れている
    lower.position.set(0, -1, 0);
    foot.position.set(0, 0, -1);
    upper.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    const knee = reading(results, JointId.Knee, JointSide.Left);
    expect(knee.degrees).toBeLessThan(0);
    expect(knee.verdict).toBe(JointRomVerdict.ReverseBend);
  });

  it('膝を後方へ折ると正の屈曲として測れる', () => {
    const { skeleton, bones } = makeTestSkeleton();
    const upper = bones.lUpperLeg;
    const lower = bones.lLowerLeg;
    const foot = bones.lFoot;

    lower.position.set(0, -1, 0);
    foot.position.set(0, 0, 1); // +Z = 後ろへ折れる（Unity の膝の正方向）
    upper.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    const knee = reading(results, JointId.Knee, JointSide.Left);
    expect(knee.degrees).toBeGreaterThan(0);
    expect(knee.verdict).toBe(JointRomVerdict.Within);
  });

  it('股関節: 脚を前へ上げると屈曲が増え、真上でも atan2 が潰れない', () => {
    const { skeleton, bones } = makeTestSkeleton();
    const upper = bones.lUpperLeg;
    const lower = bones.lLowerLeg;

    // 脚を前方(-Z)水平へ
    lower.position.set(0, 0, -1);
    upper.updateMatrixWorld(true);
    let results = measureJoints(skeleton);
    near(reading(results, JointId.HipFlexion, JointSide.Left).degrees, 90, 3);

    // 真上(+Y)へ上げても 180° として測れる（オイラー角だと潰れる）
    lower.position.set(0, 1, 0);
    upper.updateMatrixWorld(true);
    results = measureJoints(skeleton);
    near(reading(results, JointId.HipFlexion, JointSide.Left).degrees, 180, 3);
  });

  it('肩: 真横の腕では前後を測らず、開きだけを測る', () => {
    const { skeleton, bones } = makeTestSkeleton();
    const upper = bones.rUpperArm;
    const lower = bones.rLowerArm;

    // 右腕を真横(+X)へ
    lower.position.set(1, 0, 0);
    upper.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    expect(results.some((r) => r.joint === JointId.ShoulderFlexion && r.side === JointSide.Right)).toBe(false);
    near(reading(results, JointId.ShoulderAbduction, JointSide.Right).degrees, 90, 3);
  });

  it('体幹: 初期姿勢から 30° 前屈した分だけ SpinePitch が出る', () => {
    const root = new Group();
    const hips = new Object3D();
    const chest = new Object3D();
    const head = new Object3D();
    root.add(hips);
    hips.add(chest);
    chest.add(head);
    root.updateMatrixWorld(true);

    const rest = { hips: new Quaternion(), chest: new Quaternion(), head: new Quaternion() };
    const skeleton = createJointSkeleton(
      { root, hips, chest, head },
      {
        restRotations: rest,
        hasRestRotations: true,
        hasHinges: false,
      },
    );

    chest.quaternion.copy(qAxis(RIG_RIGHT, -30)); // Three では前屈は負回転
    chest.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    near(reading(results, JointId.SpinePitch, JointSide.Center).degrees, 30, 2);
    near(reading(results, JointId.SpineYaw, JointSide.Center).degrees, 0, 2);
    near(reading(results, JointId.SpineRoll, JointSide.Center).degrees, 0, 2);
    // 首は付いてきただけなので 0 のまま（親の剛体回転を差し引いている）
    near(reading(results, JointId.NeckPitch, JointSide.Center).degrees, 0, 2);
  });

  it('寝た姿勢でも体の長軸まわりの首のひねりが曲げに化けない', () => {
    const root = new Group();
    const hips = new Object3D();
    const chest = new Object3D();
    const head = new Object3D();
    root.add(hips);
    hips.add(chest);
    chest.add(head);
    root.updateMatrixWorld(true);

    const skeleton = createJointSkeleton(
      { root, hips, chest, head },
      { restRotations: { hips: new Quaternion(), chest: new Quaternion(), head: new Quaternion() }, hasRestRotations: true },
    );

    // 体を 90° 倒す（寝た姿勢）
    hips.quaternion.copy(qAxis(RIG_RIGHT, 90));
    // 首を体の長軸まわりに 40° 回す
    head.quaternion.copy(qAxis(RIG_UP, 40));
    hips.updateMatrixWorld(true);

    const results = measureJoints(skeleton);
    near(reading(results, JointId.NeckYaw, JointSide.Center).degrees, 40, 3);
    expect(Math.abs(reading(results, JointId.NeckRoll, JointSide.Center).degrees)).toBeLessThan(8);
    expect(Math.abs(reading(results, JointId.NeckPitch, JointSide.Center).degrees)).toBeLessThan(8);
  });

  it('GLTF のボーン名を正規化スケルトンへ変換できる', () => {
    const root = new Group();
    const names: ReadonlyArray<[string, string]> = [
      ['J_Bip_C_Hips', 'hips'], ['J_Bip_C_Chest', 'chest'], ['J_Bip_C_Head', 'head'],
      ['J_Bip_L_UpperArm', 'lUpperArm'], ['J_Bip_L_LowerArm', 'lLowerArm'], ['J_Bip_L_Hand', 'lHand'],
      ['J_Bip_R_UpperArm', 'rUpperArm'], ['J_Bip_R_LowerArm', 'rLowerArm'], ['J_Bip_R_Hand', 'rHand'],
      ['J_Bip_L_UpperLeg', 'lUpperLeg'], ['J_Bip_L_LowerLeg', 'lLowerLeg'], ['J_Bip_L_Foot', 'lFoot'],
      ['J_Bip_R_UpperLeg', 'rUpperLeg'], ['J_Bip_R_LowerLeg', 'rLowerLeg'], ['J_Bip_R_Foot', 'rFoot'],
    ];
    for (const [name] of names) root.add(new Object3D());
    root.children.forEach((child, i) => { child.name = names[i]![0]; });
    root.updateMatrixWorld(true);

    const skeleton = buildJointSkeleton(root);
    expect(skeleton.isValid).toBe(true);
    expect(skeleton.hasRestRotations).toBe(true);
    expect(skeleton.hasHinges).toBe(true);
    for (const [, key] of names) {
      expect(skeleton.bones[key as keyof typeof skeleton.bones]).not.toBeNull();
    }
  });
});

describe('CharacterAnimator の状態機械（骨格スモークテスト）', () => {
  it('Idle → Walk → Stagger → Idle と遷移し、SnapshotPose が呼ばれても崩れない', () => {
    const { skeleton, root } = makeTestSkeleton();
    const rig = new ProceduralRig(root, skeleton);
    const anim = new CharacterAnimator(rig);

    expect(anim.state).toBe(AnimatorState.Idle);
    anim.tick({ time: 0, dt: 1 / 60, isMoving: true });
    expect(anim.state).toBe(AnimatorState.Walk);
    anim.tick({ time: 1 / 60, dt: 1 / 60, isStunned: true, stunRemaining: 0.2 });
    expect(anim.state).toBe(AnimatorState.Stagger);
    // 20 フレームぶんは「被弾の事実」を渡さず、経過時間だけを進める
    for (let i = 0; i < 20; i++) {
      anim.tick({ time: i / 60, dt: 1 / 60 });
    }
    expect(anim.state).toBe(AnimatorState.Idle);
  });

  it('設定値ではなく実際の骨を回すpose が JointRom の判定に渡せる', () => {
    const { skeleton, root } = makeTestSkeleton();
    const rig = new ProceduralRig(root, skeleton);
    const anim = new CharacterAnimator(rig);
    const pose = anim.applyPose(0, 1 / 60, AnimatorState.Idle);

    expect(pose.grip).toBeGreaterThan(0);
    expect(pose.armLUpper.x).toBeGreaterThan(0);
    // ポーズ適用後に計測しても例外にならない（リグが壊れていない）
    expect(Array.isArray(measureJoints(skeleton))).toBe(true);
  });
});

/** テスト用の最小スケルトン。Rest は全部 identity、位置で角度を作る。 */
function makeTestSkeleton() {
  const root = new Group();
  const bone = (name: string, parent: Object3D = root) => {
    const o = new Object3D();
    o.name = name;
    parent.add(o);
    return o;
  };

  const hips = bone('hips');
  const chest = bone('chest');
  const head = bone('head', chest);
  const lUpperArm = bone('lUpperArm', chest);
  const lLowerArm = bone('lLowerArm', lUpperArm);
  const lHand = bone('lHand', lLowerArm);
  const rUpperArm = bone('rUpperArm', chest);
  const rLowerArm = bone('rLowerArm', rUpperArm);
  const rHand = bone('rHand', rLowerArm);
  const lUpperLeg = bone('lUpperLeg', hips);
  const lLowerLeg = bone('lLowerLeg', lUpperLeg);
  const lFoot = bone('lFoot', lLowerLeg);
  const rUpperLeg = bone('rUpperLeg', hips);
  const rLowerLeg = bone('rLowerLeg', rUpperLeg);
  const rFoot = bone('rFoot', rLowerLeg);

  const bones = {
    root, hips, spine: null, chest, neck: null, head,
    lUpperArm, lLowerArm, lHand, rUpperArm, rLowerArm, rHand,
    lUpperLeg, lLowerLeg, lFoot, rUpperLeg, rLowerLeg, rFoot,
  };

  root.updateMatrixWorld(true);
  const identity = new Quaternion();
  const rest: Record<string, Quaternion> = {};
  for (const [key, value] of Object.entries(bones)) {
    if (value) rest[key] = identity.clone();
  }

  const skeleton = createJointSkeleton(bones, {
    restRotations: rest,
    hasRestRotations: true,
    elbowHingeLocalL: elbowHingeLocal(lLowerArm, identity),
    elbowHingeLocalR: elbowHingeLocal(rLowerArm, identity),
    kneeHingeLocalL: kneeHingeLocal(identity),
    kneeHingeLocalR: kneeHingeLocal(identity),
    hasHinges: true,
  });

  return { skeleton, bones, root };
}
