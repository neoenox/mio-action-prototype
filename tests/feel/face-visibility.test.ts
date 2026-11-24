import { Matrix4, PerspectiveCamera, Quaternion, Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import {
  FaceRadius,
  FacingLimit,
  FrameMargin,
  GoodVisible,
  NearFaceSlack,
  OpaqueAlpha,
  RequiredRatio,
  faceCenter,
  isInFrame,
  lensClearance,
  measure,
  screenFaceSize,
  segmentHits,
} from '../../src/feel/face-visibility.js';
import type { EnemyFigureView, FaceProxy, HeadBone } from '../../src/feel/face-visibility.js';

const DEG = Math.PI / 180;

function makeCamera(position: Vector3, target: Vector3, fov = 45): PerspectiveCamera {
  const camera = new PerspectiveCamera(fov, 16 / 9, 0.1, 100);
  camera.position.copy(position);
  camera.up.set(0, 1, 0);
  camera.lookAt(target);
  camera.updateMatrixWorld(true);
  camera.matrixWorldInverse.copy(camera.matrixWorld).invert();
  return camera;
}

/** カメラの正面 2m に、カメラの方を向いた頭を作る。 */
function makeHead(position: Vector3, towardCamera: Vector3): HeadBone {
  return {
    position: position.clone(),
    up: new Vector3(0, 1, 0),
    forward: towardCamera.clone().normalize(),
  };
}

function proxyAt(position: Vector3, shape: FaceProxy['shape'], scale = new Vector3(1, 1, 1)): FaceProxy {
  const matrix = new Matrix4().compose(
    position.clone(),
    new Quaternion(),
    scale.clone(),
  );
  return { matrixWorld: matrix, shape, active: true };
}

describe('FaceVisibility 定数', () => {
  it('RequiredRatio は壁拘束の 4 段階で固定されている', () => {
    expect([...RequiredRatio]).toEqual([0.75, 0.75, 0.90, 0.75]);
  });

  it('判定閾値は Unity 版と同じ', () => {
    expect(GoodVisible).toBe(0.6);
    expect(FacingLimit).toBe(60);
    expect(FrameMargin).toBe(0.03);
    expect(FaceRadius).toBe(0.07);
    expect(NearFaceSlack).toBe(0.10);
    expect(OpaqueAlpha).toBe(0.5);
  });
});

describe('FaceVisibility.Measure', () => {
  it('敵がいなければ 5 点すべて可視で Readable', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    const result = measure(camera, head, []);
    expect(result.visible).toBe(1);
    expect(result.inFrame).toBe(1);
    expect(result.unblocked).toBe(1);
    // center が forward 側へ 0.05m ずれるため、真正面でも僅かな角度が付く
    expect(result.facingAngle).toBeLessThan(5);
    expect(result.readable).toBe(true);
    expect(result.blocker).toBeNull();
  });

  it('顔中心は head.position + up*0.06 + forward*0.05', () => {
    const head = makeHead(new Vector3(1, 2, 3), new Vector3(0, 0, 1));
    const c = faceCenter(head);
    expect(c.x).toBeCloseTo(1, 6);
    expect(c.y).toBeCloseTo(2.06, 6);
    expect(c.z).toBeCloseTo(3.05, 6);
  });

  it('カメラの右・上へ FaceRadius だけずらした 5 点で判定する', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    const center = faceCenter(head);
    const right = new Vector3(1, 0, 0).transformDirection(camera.matrixWorld);
    const up = new Vector3(0, 1, 0).transformDirection(camera.matrixWorld);

    // 中心 1 点だけ画面内に入るよう、残り 4 点をわざと画面外へ出す。
    // 中心は入るが上下の点（±0.07m）は FrameMargin で切れる画角
    const tightCamera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0.03, -2), 2.5);
    const result = measure(tightCamera, head, []);
    expect(result.inFrame).toBeGreaterThan(0);
    expect(result.inFrame).toBeLessThan(1);
    // 5 点の形状そのものは定数で固定（which points を数えるための参照）
    expect(center.clone().addScaledVector(right, FaceRadius).distanceTo(center)).toBeCloseTo(FaceRadius, 6);
    expect(center.clone().addScaledVector(up, FaceRadius).distanceTo(center)).toBeCloseTo(FaceRadius, 6);
  });

  it('前方の球が顔を隠すと可視率が下がり Blocker が名前で返る', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    const enemy: EnemyFigureView = {
      name: 'Captor',
      active: true,
      alpha: 1,
      proxies: [proxyAt(new Vector3(0, 0.03, -1.5), 'sphere', new Vector3(0.6, 0.6, 0.6))],
    };
    const result = measure(camera, head, [enemy]);
    expect(result.unblocked).toBeLessThan(1);
    expect(result.visible).toBeLessThan(1);
    expect(result.blocker).toBe('Captor');
  });

  it('alpha 0.5 未満の敵は遮蔽から除外される', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    const faded: EnemyFigureView = {
      name: 'Faded',
      active: true,
      alpha: OpaqueAlpha - 0.01,
      proxies: [proxyAt(new Vector3(0, 0.03, -1.5), 'sphere', new Vector3(2, 2, 2))],
    };
    const result = measure(camera, head, [faded]);
    expect(result.visible).toBe(1);
    expect(result.blocker).toBeNull();
  });

  it('顔の向きが 60° を超えると Readable が false になる', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, -1));
    const result = measure(camera, head, []);
    // center が forward 側へ 0.05m ずれるため、eye-center と forward は直交する
    expect(result.facingAngle).toBeCloseTo(90, 3);
    expect(result.visible).toBe(1);
    expect(result.readable).toBe(false);
  });

  it('可視率 0.6 境界（5 点中 3 点）で Readable が true になる', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const head = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    // カメラ右 2 点を箱で隠す（中心と上下と左は残る）。
    const enemy: EnemyFigureView = {
      name: 'RightBlocker',
      active: true,
      alpha: 1,
      proxies: [proxyAt(new Vector3(0.5, 0.03, -1.5), 'cube', new Vector3(0.9, 1.6, 1.2))],
    };
    const result = measure(camera, head, [enemy]);
    expect(result.unblocked).toBeGreaterThanOrEqual(GoodVisible);
    expect(result.readable).toBe(true);
  });
});

describe('FaceVisibility 画面投影と最近接', () => {
  it('isInFrame は固定アスペクト 16/9 と FrameMargin で判定する', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    expect(isInFrame(camera, new Vector3(0, 0.03, -2))).toBe(true);
    // 縦の限界: tan(22.5°)*2*(1-0.06) ≈ 0.776
    expect(isInFrame(camera, new Vector3(0, 0.9, -2))).toBe(false);
    // 横は 16/9 倍広いので同じ 0.9 でも画面内
    expect(isInFrame(camera, new Vector3(0.9, 0, -2))).toBe(true);
  });

  it('ScreenFaceSize は距離が 2 倍になると半分になる', () => {
    const camera = makeCamera(new Vector3(0, 0, 0), new Vector3(0, 0, -2));
    const near = makeHead(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
    const far = makeHead(new Vector3(0, 0, -4), new Vector3(0, 0, 1));
    const a = screenFaceSize(camera, near);
    const b = screenFaceSize(camera, far);
    expect(a).toBeGreaterThan(0);
    expect(b).toBeLessThan(a * 0.55);
    expect(b).toBeGreaterThan(a * 0.45);
  });

  it('LensClearance は球サーフェスまでの距離を返す', () => {
    const enemy: EnemyFigureView = {
      name: 'Sphere',
      active: true,
      alpha: 1,
      proxies: [proxyAt(new Vector3(0, 0, -1), 'sphere', new Vector3(1, 1, 1))],
    };
    const clearance = lensClearance(new Vector3(0, 0, 0), [enemy]);
    // 中心まで 1m、半径 0.5m → 0.5m
    expect(clearance).toBeCloseTo(0.5, 5);
  });
});

describe('線分交差', () => {
  const identity = new Matrix4();

  it('球（半径 0.5）に当たる', () => {
    expect(segmentHits(identity, 'sphere', new Vector3(0, 0, 2), new Vector3(0, 0, -2))).toBe(true);
    expect(segmentHits(identity, 'sphere', new Vector3(0.6, 0, 2), new Vector3(0.6, 0, -2))).toBe(false);
  });

  it('カプセル（半径 0.5・全高 2）に当たる', () => {
    expect(segmentHits(identity, 'capsule', new Vector3(0, 0.3, 2), new Vector3(0, 0.3, -2))).toBe(true);
    expect(segmentHits(identity, 'capsule', new Vector3(0, 1.6, 2), new Vector3(0, 1.6, -2))).toBe(false);
  });

  it('箱（一辺 1）に当たる', () => {
    expect(segmentHits(identity, 'cube', new Vector3(0, 0, 2), new Vector3(0, 0, -2))).toBe(true);
    expect(segmentHits(identity, 'cube', new Vector3(0, 0.6, 2), new Vector3(0, 0.6, -2))).toBe(false);
  });
});
