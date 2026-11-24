import { PerspectiveCamera, Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { StaggerTier } from '../../src/core/CombatConstants.js';
import { HitResult } from '../../src/core/HitResult.js';
import { CameraController } from '../../src/feel/camera.js';
import { CameraDirector } from '../../src/feel/camera-director.js';
import { buildWallSplitShot, DEFAULT_WALL_SPLIT_SETTINGS } from '../../src/feel/wall-split-camera.js';

const DT = 1 / 60;

function setup(): CameraController {
  const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
  const c = new CameraController(camera, { distance: 5, minDistance: 0.85 });
  c.setTarget(new Vector3(0, 0, 0));
  for (let i = 0; i < 30; i++) c.update(DT, DT);
  return c;
}

describe('CameraDirector', () => {
  it('HitLanded だけなら寄り（0.90）が効く', () => {
    const rig = setup();
    let dead = false;
    const director = new CameraDirector(rig, () => dead);
    director.onHitLanded(new HitResult(5, StaggerTier.None));
    rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeLessThan(1);
    expect(rig.effectDistanceScale).toBeGreaterThanOrEqual(0.9);
    expect(rig.effectPivotOffset).toBeCloseTo(0, 5);
  });

  it('HitReceived は 1.10 / -0.10 を押し込む', () => {
    const rig = setup();
    const director = new CameraDirector(rig, () => false);
    director.onHitReceived(new HitResult(5, StaggerTier.Small));
    rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeGreaterThan(1);
    expect(rig.effectPivotOffset).toBeLessThan(-0.05);
  });

  it('Large 以上の被弾は HitReceived を無視し Staggered で強く引く', () => {
    const rig = setup();
    const director = new CameraDirector(rig, () => false);
    const big = new HitResult(20, StaggerTier.Large);
    director.onHitReceived(big);
    rig.update(DT, DT);
    const afterReceived = rig.effectDistanceScale;

    const rig2 = setup();
    const d2 = new CameraDirector(rig2, () => false);
    d2.onStaggered(big);
    rig2.update(DT, DT);
    expect(rig2.effectDistanceScale).toBeGreaterThan(afterReceived);
    expect(rig2.effectPivotOffset).toBeLessThan(-0.1);
  });

  it('死亡中は毎フレーム引き＋見下ろしを押し直して保持する', () => {
    const rig = setup();
    let dead = false;
    const director = new CameraDirector(rig, () => dead, () => false, {
      deathPivotSeconds: 0.2,
      deathHoldSeconds: 0.5,
    });
    dead = true;
    for (let i = 0; i < 60; i++) {
      director.update(DT);
      rig.update(DT, DT);
    }
    expect(rig.deathFramingActive).toBe(true);
    expect(rig.effectDistanceScale).toBeGreaterThan(1.2);
    // envelope の減衰が掛かるため -0.85 よりは浅い（Unity と同じ）
    expect(rig.effectPivotOffset).toBeLessThan(-0.7);

    // 生死が戻ればフラグも解除
    dead = false;
    director.update(DT);
    expect(rig.deathFramingActive).toBe(false);
  });

  it('腹パン演出がカメラを握っている間は被弾・怯みを無視する', () => {
    const rig = setup();
    let held = true;
    const director = new CameraDirector(rig, () => false, () => held);
    director.onHitReceived(new HitResult(5, StaggerTier.Small));
    director.onStaggered(new HitResult(20, StaggerTier.Large));
    rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeCloseTo(1, 5);
  });
});

describe('WallSplitCamera', () => {
  it('既定は斜め 6°・距離 1.55m・高さ 0.40m', () => {
    const her = new Vector3(0, 0, 0);
    const forward = new Vector3(0, 0, -1);
    const captor = new Vector3(-0.8, 0, -0.5);
    const assist = new Vector3(0.8, 0, -0.5);
    const shot = buildWallSplitShot(her, forward, captor, assist);
    const horizontal = new Vector3().subVectors(shot.position, shot.target).setY(0);
    expect(horizontal.length()).toBeCloseTo(DEFAULT_WALL_SPLIT_SETTINGS.distance, 5);
    expect(shot.position.y - shot.target.y).toBeCloseTo(DEFAULT_WALL_SPLIT_SETTINGS.height, 5);
  });

  it('注視点は彼女 0.70 / 拘束役 0.15 / アシスト 0.15 の加重平均', () => {
    const her = new Vector3(0, 0, 0);
    const captor = new Vector3(1, 0, 0);
    const assist = new Vector3(-1, 0, 0);
    const shot = buildWallSplitShot(her, new Vector3(0, 0, -1), captor, assist);
    // 荷重は打ち消し合うので X は 0（lateral だけ残る）
    expect(Math.abs(shot.target.x)).toBeLessThanOrEqual(DEFAULT_WALL_SPLIT_SETTINGS.lateral + 1e-6);
    // herChest(1.05)*0.70 + captorChest(1.02)*0.15*2 = 1.041
    expect(shot.target.y).toBeCloseTo(1.041, 5);
  });

  it('front モードではカメラが関係軸の正面に来る', () => {
    const her = new Vector3(0, 0, 0);
    const captor = new Vector3(-0.8, 0, -0.5);
    const assist = new Vector3(0.8, 0, -0.5);
    const angle = buildWallSplitShot(her, new Vector3(0, 0, -1), captor, assist, { mode: 'angle' });
    const front = buildWallSplitShot(her, new Vector3(0, 0, -1), captor, assist, { mode: 'front' });
    const dirA = angle.position.clone().sub(angle.target).setY(0).normalize();
    const dirF = front.position.clone().sub(front.target).setY(0).normalize();
    expect(dirA.angleTo(dirF)).toBeGreaterThan(0.05);
    expect(front.position.clone().sub(front.target).setY(0).x).toBeCloseTo(0, 5);
  });

  it('hold モードは壁押さえのショットをそのまま使う', () => {
    const shot = buildWallSplitShot(
      new Vector3(0, 0, 0), new Vector3(0, 0, -1),
      new Vector3(0, 0, -0.5), new Vector3(0, 0, -0.5),
      { mode: 'hold' },
    );
    expect(shot.position.x).toBeCloseTo(-2.25, 5);
    // mid.y=1.02 + PinOffset.y=0.47
    expect(shot.position.y).toBeCloseTo(1.49, 5);
    // mid.z = -0.5*0.45 = -0.225、-1.75 足して -1.975
    expect(shot.position.z).toBeCloseTo(-1.975, 5);
  });
});
