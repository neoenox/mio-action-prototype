import { describe, expect, it } from 'vitest';
import { Bone, Group, Mesh, Scene, Vector3 } from 'three';
import {
  AnimatorState,
  CharacterAnimator,
  FACE_FORWARD_LOCAL,
  bindAnimator,
  buildPrimitiveHumanoid,
  createEnemyRig,
  createPlayerRig,
  normalizePunchZone,
  punchAnchorFromSkeleton,
} from '../../src/rig/index.js';

// ---------------------------------------------------------------------------
// 1. 実ボーンが Group にぶら下がる（数値テストだけだった部分の実体確認）
// ---------------------------------------------------------------------------

describe('実リグのボーン階層', () => {
  it('ルート Group の下に 17 本の Bone とメッシュがぶら下がる', () => {
    const built = buildPrimitiveHumanoid();
    expect(built.root).toBeInstanceOf(Group);

    const bones: Bone[] = [];
    const meshes: Mesh[] = [];
    built.root.traverse((o) => {
      if (o instanceof Bone) bones.push(o);
      if (o instanceof Mesh) meshes.push(o);
    });
    // JOINT_BONE_KEYS は 18 キーだが root は Group なので Bone は 17 本。
    expect(bones.length).toBe(17);
    expect(meshes.length).toBeGreaterThanOrEqual(17);
    expect(built.meshes.length).toBe(meshes.length);
  });

  it('親子関係がスケルトン辞書と一致する', () => {
    const { bones } = buildPrimitiveHumanoid();
    expect(bones.hips?.parent).toBe(bones.root);
    expect(bones.spine?.parent).toBe(bones.hips);
    expect(bones.chest?.parent).toBe(bones.spine);
    expect(bones.neck?.parent).toBe(bones.chest);
    expect(bones.head?.parent).toBe(bones.neck);
    expect(bones.lLowerArm?.parent).toBe(bones.lUpperArm);
    expect(bones.lHand?.parent).toBe(bones.lLowerArm);
    expect(bones.rLowerArm?.parent).toBe(bones.rUpperArm);
    expect(bones.rHand?.parent).toBe(bones.rLowerArm);
    expect(bones.lLowerLeg?.parent).toBe(bones.lUpperLeg);
    expect(bones.lFoot?.parent).toBe(bones.lLowerLeg);
    expect(bones.rLowerLeg?.parent).toBe(bones.rUpperLeg);
    expect(bones.rFoot?.parent).toBe(bones.rLowerLeg);
  });

  it('buildJointSkeleton 相当の辞書・Rest が有効で、メッシュがボーンに追随する', () => {
    const built = buildPrimitiveHumanoid();
    expect(built.skeleton.isValid).toBe(true);
    expect(built.skeleton.hasRestRotations).toBe(true);
    expect(built.skeleton.hasHinges).toBe(true);
    expect(built.skeleton.bones.root).toBe(built.root);
    expect(built.skeleton.bones.head).toBe(built.bones.head);

    const headMesh = built.root.getObjectByName('HeadMesh');
    expect(headMesh?.parent).toBe(built.bones.head);

    //  head ボーンを回すと子のメッシュのワールド座標も動く。
    built.root.updateMatrixWorld(true);
    const before = headMesh!.getWorldPosition(new Vector3());
    built.bones.head!.rotation.z = 0.4;
    built.root.updateMatrixWorld(true);
    const after = headMesh!.getWorldPosition(new Vector3());
    expect(before.distanceTo(after)).toBeGreaterThan(0.01);
  });

  it('createPlayerRig は Animator つきで、ルートをシーンへ追加できる', () => {
    const scene = new Scene();
    const player = createPlayerRig();
    expect(player.animator).toBeInstanceOf(CharacterAnimator);
    expect(player.root.parent).toBeNull();
    expect(player.attachTo(scene, { x: 1.5 })).toBe(player.root);
    expect(player.root.parent).toBe(scene);
    expect(player.root.position.x).toBe(1.5);
  });

  it('createEnemyRig は別色で同じ骨格を作る', () => {
    const enemy = createEnemyRig(0x00ff00);
    expect(enemy.root).toBeInstanceOf(Group);
    expect(enemy.skeleton.isValid).toBe(true);
    expect(enemy.bones.head?.parent).toBe(enemy.bones.neck);
    const material = enemy.meshes[0]!.material as unknown as { color: { getHex(): number } };
    expect(material.color.getHex()).toBe(0x00ff00);
  });
});

// ---------------------------------------------------------------------------
// 2. punch anchor（実ボーンのワールド座標）
// ---------------------------------------------------------------------------

describe('punch anchor が実ボーンから取れる', () => {
  it('zone の別名・logic の PunchZone 数値を正規化できる', () => {
    expect(normalizePunchZone('head')).toBe('head');
    expect(normalizePunchZone('face')).toBe('head');
    expect(normalizePunchZone(0)).toBe('head');
    expect(normalizePunchZone('body')).toBe('body');
    expect(normalizePunchZone('belly')).toBe('body');
    expect(normalizePunchZone(1)).toBe('body');
  });

  it('頭と胴で異なる座標を返し、それぞれ対応する手ボーンの位置と一致する', () => {
    const enemy = createEnemyRig();
    enemy.animator.applyPose(0, 1 / 60, AnimatorState.Idle);
    enemy.root.updateMatrixWorld(true);

    const head = enemy.getPunchAnchor('head');
    const body = enemy.getPunchAnchor('body');
    expect(head).not.toBeNull();
    expect(body).not.toBeNull();
    expect(head!.distanceTo(body!)).toBeGreaterThan(0.02);

    expect(head!.distanceTo(enemy.bones.rHand!.getWorldPosition(head!.clone()))).toBeLessThan(1e-6);
    expect(body!.distanceTo(enemy.bones.lHand!.getWorldPosition(body!.clone()))).toBeLessThan(1e-6);
  });

  it('ルートを動かすとアンカーもワールド座標で追従する', () => {
    const enemy = createEnemyRig();
    enemy.root.position.set(2, 0, -1);
    const before = enemy.getPunchAnchor('head')!;
    enemy.root.position.set(4, 0, -1);
    const after = enemy.getPunchAnchor('head')!;
    expect(after.x - before.x).toBeCloseTo(2, 5);
  });

  it('bindAnimator で anchors を差し替え、解除すると戻る', () => {
    const { proceduralRig, skeleton } = createEnemyRig();
    const animator = new CharacterAnimator(proceduralRig);
    const unbind = bindAnimator(animator, skeleton);
    expect(animator.anchors.punchAnchor?.('head')).not.toBeNull();
    unbind();
    expect(animator.anchors.punchAnchor).toBeUndefined();
  });

  it('直接 skeleton からも取れる（フックへ渡す形を保つ）', () => {
    const { skeleton } = createEnemyRig();
    const hook = (zone: number) => punchAnchorFromSkeleton(skeleton, zone);
    expect(hook(0)).not.toBeNull();
    expect(hook(1)).not.toBeNull();
  });
});

// ---------------------------------------------------------------------------
// 3. 優先ポーズ（Down の地面ロール／マウント反転 → Stagger 4 段）
// ---------------------------------------------------------------------------

describe('Down の地面ロールとマウント反転', () => {
  it('着地後に横へロールすると laySide が出る', () => {
    const player = createPlayerRig();
    player.animator.stateTime = 1.0; // 落下完了
    player.animator.downRollSide = 1;
    const pose = player.animator.applyPose(0, 1 / 60, AnimatorState.Down);
    expect(pose.laySide).toBeGreaterThan(20);
    expect(pose.layAngle).toBeLessThan(-80); // うつ伏せ側
  });

  it('マウント反転では仰向け・腰が前へ出て・腕が頭側へ回る', () => {
    const prone = createPlayerRig();
    prone.animator.stateTime = 1.0;
    const base = prone.animator.applyPose(0, 1 / 60, AnimatorState.Down);

    const inverted = createPlayerRig();
    inverted.animator.stateTime = 1.0;
    inverted.animator.mountInverted = true;
    const flipped = inverted.animator.applyPose(0, 1 / 60, AnimatorState.Down);

    expect(base.layAngle).toBeLessThan(0);
    expect(flipped.layAngle).toBeGreaterThan(0);
    expect(flipped.hipsOffset.z).toBeGreaterThan(base.hipsOffset.z);
    expect(flipped.armLUpper.y).toBeGreaterThan(base.armLUpper.y);
  });

  it('tick(input) からも派生形を渡せる', () => {
    const player = createPlayerRig();
    player.animator.tick({ time: 0, dt: 1 / 60, isDown: true, downOnBack: true, downRollSide: -1 });
    expect(player.animator.downOnBack).toBe(true);
    expect(player.animator.downRollSide).toBe(-1);
  });
});

describe('Stagger 4 段', () => {
  it('段が上がるほど体幹の折れが大きくなる', () => {
    const bends: number[] = [];
    for (let tier = 1; tier <= 4; tier++) {
      const player = createPlayerRig();
      player.animator.state = AnimatorState.Stagger;
      player.animator.stateTime = 0.15;
      player.animator.stateDuration = 0.6;
      player.animator.staggerTier = tier;
      const pose = player.animator.applyPose(0, 1 / 60, AnimatorState.Stagger);
      bends.push(pose.torsoPitch);
    }
    for (let i = 1; i < bends.length; i++) {
      expect(bends[i]!).toBeGreaterThan(bends[i - 1]!);
    }
  });

  it('顔への怯みは首の振り、胴への怯みは腰の沈みが強い', () => {
    const head = createPlayerRig();
    head.animator.state = AnimatorState.Stagger;
    head.animator.stateTime = 0.15;
    head.animator.stateDuration = 0.6;
    head.animator.staggerTier = 3;
    head.animator.staggerZone = 'head';
    const headPose = head.animator.applyPose(0, 1 / 60, AnimatorState.Stagger);

    const body = createPlayerRig();
    body.animator.state = AnimatorState.Stagger;
    body.animator.stateTime = 0.15;
    body.animator.stateDuration = 0.6;
    body.animator.staggerTier = 3;
    body.animator.staggerZone = 'body';
    const bodyPose = body.animator.applyPose(0, 1 / 60, AnimatorState.Stagger);

    expect(Math.abs(headPose.headYaw)).toBeGreaterThan(Math.abs(bodyPose.headYaw));
    expect(headPose.hipsOffset.y).toBeGreaterThan(bodyPose.hipsOffset.y);
  });
});

// ---------------------------------------------------------------------------
// 4. 顔の向き（glTF 規約: 前 = -Z、後頭部 = +Z）
// ---------------------------------------------------------------------------

describe('顔パーツは head ローカル -Z（カメラが回り込めば見える側）', () => {
  it('FACE_FORWARD_LOCAL は -Z', () => {
    expect(FACE_FORWARD_LOCAL.z).toBe(-1);
    expect(FACE_FORWARD_LOCAL.x).toBe(0);
    expect(FACE_FORWARD_LOCAL.y).toBe(0);
  });

  it('目・口・鼻は head の -Z 側、髪は +Z 側にある', () => {
    const player = createPlayerRig();
    const head = player.bones.head!;
    for (const name of ['EyeLMesh', 'EyeRMesh', 'MouthMesh', 'NoseMesh']) {
      const mesh = player.root.getObjectByName(`P-${name}`);
      expect(mesh, name).toBeDefined();
      expect(mesh!.parent).toBe(head);
      expect(mesh!.position.z, name).toBeLessThan(0);
    }
    const hair = player.root.getObjectByName('P-HairMesh')!;
    expect(hair.parent).toBe(head);
    expect(hair.position.z).toBeGreaterThan(0);
  });

  it('顔パーツのワールド座標は頭ボーンより前方（-Z）にある', () => {
    const player = createPlayerRig();
    player.root.updateMatrixWorld(true);
    const headWorld = player.bones.head!.getWorldPosition(new Vector3());
    const eyeWorld = player.root.getObjectByName('P-EyeLMesh')!.getWorldPosition(new Vector3());
    expect(eyeWorld.z).toBeLessThan(headWorld.z);
    // 髪は逆側
    const hairWorld = player.root.getObjectByName('P-HairMesh')!.getWorldPosition(new Vector3());
    expect(hairWorld.z).toBeGreaterThan(headWorld.z);
  });

  it('ルートを 180° 回すと顔が +Z 側のカメラへ向く（app 側の推奨 rotation）', () => {
    const player = createPlayerRig();
    player.root.rotation.y = Math.PI;
    player.root.updateMatrixWorld(true);
    const headWorld = player.bones.head!.getWorldPosition(new Vector3());
    const eyeWorld = player.root.getObjectByName('P-EyeLMesh')!.getWorldPosition(new Vector3());
    expect(eyeWorld.z).toBeGreaterThan(headWorld.z);
  });
});

// ---------------------------------------------------------------------------
// 5. isDown 入力で本当に Down へ遷移し、姿勢が変わる（haul の背面直立バグ）
// ---------------------------------------------------------------------------

describe('isDown が Down 状態と姿勢を駆動する', () => {
  it('Idle から isDown=true で即 Down へ入り、着地後に横たわる', () => {
    const player = createPlayerRig();
    expect(player.animator.state).toBe(AnimatorState.Idle);

    for (let i = 0; i < 30; i++) {
      player.animator.tick({ time: i * 0.05, dt: 0.05, isDown: true });
    }
    expect(player.animator.state).toBe(AnimatorState.Down);
    expect(player.animator.stateTime).toBeGreaterThan(1);

    const pose = player.animator.applyPose(1.5, 1 / 60, AnimatorState.Down);
    expect(pose.layAngle).toBeLessThan(-80);
  });

  it('isDown の前後で実際のボーン姿勢が変わる（#30 と #59 が同じにならない）', () => {
    const standing = createPlayerRig();
    for (let i = 0; i < 30; i++) {
      standing.animator.tick({ time: i * 0.05, dt: 0.05, isDown: false });
    }
    const downed = createPlayerRig();
    for (let i = 0; i < 30; i++) {
      downed.animator.tick({ time: i * 0.05, dt: 0.05, isDown: true });
    }

    const hipsA = standing.bones.hips!.getWorldPosition(new Vector3());
    const hipsB = downed.bones.hips!.getWorldPosition(new Vector3());
    expect(hipsA.distanceTo(hipsB)).toBeGreaterThan(0.1);

    const headA = standing.bones.head!.getWorldPosition(new Vector3());
    const headB = downed.bones.head!.getWorldPosition(new Vector3());
    expect(headA.distanceTo(headB)).toBeGreaterThan(0.3);
  });

  it('掴まれ中でも isDown なら Down が優先される', () => {
    const player = createPlayerRig();
    player.animator.tick({ time: 0, dt: 0.05, isGrappled: true });
    expect(player.animator.state).toBe(AnimatorState.Grabbed);
    player.animator.tick({ time: 0.05, dt: 0.05, isGrappled: true, isDown: true });
    expect(player.animator.state).toBe(AnimatorState.Down);
  });

  it('isDown が解けると GetUp へ遷移する', () => {
    const player = createPlayerRig();
    player.animator.tick({ time: 0, dt: 0.05, isDown: true });
    expect(player.animator.state).toBe(AnimatorState.Down);
    player.animator.tick({ time: 0.05, dt: 0.05, isDown: false });
    expect(player.animator.state).toBe(AnimatorState.GetUp);
  });
});
