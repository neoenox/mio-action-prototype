import { PerspectiveCamera, Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { Clock } from '../../src/core/clock.js';
import { CameraController } from '../../src/feel/camera.js';
import { CameraShake } from '../../src/feel/shake.js';
import { HitStop } from '../../src/feel/hit-stop.js';

const DT = 1 / 60;

function makeController(): CameraController {
  const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
  const controller = new CameraController(camera, { distance: 5, minDistance: 0.85 });
  controller.setTarget(new Vector3(0, 0, 0));
  for (let i = 0; i < 30; i++) controller.update(DT, DT);
  return controller;
}

describe('CameraController Push API', () => {
  it('PushDistance(0.9) で寄り、1 超の引きより優先される', () => {
    const c = makeController();
    c.pushDistance(0.9, 0.2);
    c.pushDistance(1.1, 0.3);
    c.update(DT, DT);
    expect(c.effectDistanceScale).toBeLessThan(1);
    expect(c.effectDistanceScale).toBeGreaterThanOrEqual(0.9);
  });

  it('連続 Push では小さい倍率と長い残り時間を採用する', () => {
    const c = makeController();
    c.pushDistance(0.95, 0.5);
    c.pushDistance(0.85, 0.1);
    c.update(DT, DT);
    const first = c.effectDistanceScale;
    expect(first).toBeLessThan(0.9);

    const c2 = makeController();
    c2.pushDistance(0.85, 0.1);
    c2.pushDistance(0.95, 0.5);
    for (let i = 0; i < 20; i++) c2.update(DT, DT);
    // 0.1 秒の強い寄りは抜け、0.5 秒の弱い寄りが残る
    expect(c2.effectDistanceScale).toBeLessThan(1);
    expect(c2.effectDistanceScale).toBeGreaterThan(first);
  });

  it('PushPivot は負で下げ、絶対値が大きい方を採用する', () => {
    const c = makeController();
    c.pushPivot(-0.05, 0.3);
    c.pushPivot(-0.18, 0.4);
    c.update(DT, DT);
    expect(c.effectPivotOffset).toBeLessThan(-0.1);

    // Unity と同じく「減衰済みの現在値」と比べるため、強い方を先に呼んだ
    // 場合は弱い方に上書きされうる。同フレームの連続呼び出しでは強い方が残る。
    const c2 = makeController();
    c2.pushPivot(0.05, 0.3);
    c2.pushPivot(-0.18, 0.4);
    c2.update(DT, DT);
    expect(c2.effectPivotOffset).toBeLessThan(-0.1);
  });

  it('HoldFocus は呼んでいる間減衰せず、止めると seconds で戻る', () => {
    const c = makeController();
    for (let i = 0; i < 30; i++) {
      c.holdFocus(0.6, -0.1, 1.2, 0.5);
      c.update(DT, DT);
    }
    expect(c.effectDistanceScale).toBeCloseTo(0.6, 5);
    expect(c.effectPivotOffset).toBeCloseTo(-0.1, 5);
    expect(c.isFocusing).toBe(true);

    for (let i = 0; i < 40; i++) c.update(DT, DT);
    expect(c.effectDistanceScale).toBeCloseTo(1, 3);
    expect(c.isFocusing).toBe(false);
  });

  it('RequestCinematicShot(cut:true) は 1 フレームで位置が切り替わる', () => {
    const c = makeController();
    const before = c.camera.position.clone();
    c.requestCinematicShot(new Vector3(3, 1, -2), new Vector3(0, 1, 0), true, 30);
    c.update(DT, DT);
    expect(c.camera.position.distanceTo(before)).toBeGreaterThan(1);
    expect(c.camera.fov).toBeCloseTo(30, 2);
  });

  it('RequestCinematicShot(cut:false) は補間で寄っていく', () => {
    const slow = makeController();
    const fast = makeController();
    const shotPosition = new Vector3(3, 1, -2);
    const shotTarget = new Vector3(0, 1, 0);
    slow.requestCinematicShot(shotPosition, shotTarget, false, 0, 2);
    fast.requestCinematicShot(shotPosition, shotTarget, false, 0, 30);
    for (let i = 0; i < 3; i++) {
      slow.update(DT, DT);
      fast.update(DT, DT);
    }
    const dSlow = slow.camera.position.distanceTo(shotPosition);
    const dFast = fast.camera.position.distanceTo(shotPosition);
    expect(dFast).toBeLessThan(dSlow);
  });

  it('deathFramingActive 中は寄りを無視する', () => {
    const c = makeController();
    c.deathFramingActive = true;
    c.pushDistance(0.9, 0.2);
    c.pushDistance(1.25, 0.5);
    c.holdFocus(0.8, -0.2, 0, 0.5);
    c.update(DT, DT);
    expect(c.effectDistanceScale).toBeGreaterThan(1);
  });
});

describe('HitStop', () => {
  it('scaledScale を 0 にし、unscaled 時間で復帰する', () => {
    const clock = new Clock();
    const stop = new HitStop(clock);
    stop.play(0.05);
    expect(clock.scaledScale).toBe(0);
    expect(clock.scaledDelta(DT)).toBe(0);

    for (let i = 0; i < 3; i++) {
      clock.advance(DT);
      stop.update(DT);
    }
    expect(stop.isActive).toBe(true);
    expect(clock.scaledScale).toBe(0);

    for (let i = 0; i < 2; i++) {
      clock.advance(DT);
      stop.update(DT);
    }
    expect(stop.isActive).toBe(false);
    expect(clock.scaledScale).toBe(1);
  });

  it('連打では残り時間の長い方を採用し restoreScale は最初の値を保つ', () => {
    const clock = new Clock();
    clock.setScale(0.7);
    const stop = new HitStop(clock);
    stop.play(0.05);
    clock.advance(DT);
    stop.update(DT);
    stop.play(0.5);
    expect(stop.remainingSeconds).toBeGreaterThan(0.45);
    stop.restoreNow();
    expect(clock.scaledScale).toBeCloseTo(0.7, 6);
  });

  it('isBlocked 中は timeScale を書き戻さない', () => {
    const clock = new Clock();
    const stop = new HitStop(clock, { isBlocked: () => true });
    stop.play(0.01);
    stop.restoreNow();
    expect(clock.scaledScale).toBe(0);
  });
});

describe('CameraShake', () => {
  it('updateLate のたびに前フレームのオフセットを戻してから足す', () => {
    const clock = new Clock();
    const camera = new PerspectiveCamera();
    const shake = new CameraShake(camera, clock);

    const base = camera.position.clone();
    const baseRotation = camera.quaternion.clone();
    shake.shake(0.1, 1);
    for (let i = 0; i < 10; i++) {
      // 基準カメラ（ThirdPersonCamera 相当）が毎フレーム絶対代入する
      camera.position.copy(base);
      camera.quaternion.copy(baseRotation);
      clock.advance(DT);
      shake.updateLate(DT, base, baseRotation);
    }
    // 累積していなければ振幅 0.1m 以上には離れない
    expect(camera.position.distanceTo(base)).toBeLessThan(0.15);

    // reset は「shake が足した状態」に対して呼ぶ（Unity の OnDisable と同じ）
    shake.reset();
    expect(camera.position.distanceTo(base)).toBeLessThan(1e-6);
  });

  it('HitStop 中（scaledScale=0）でも unscaled 時間で減衰する', () => {
    const clock = new Clock();
    const camera = new PerspectiveCamera();
    const shake = new CameraShake(camera, clock);
    shake.shake(0.1, 0.05);
    clock.setScale(0);
    for (let i = 0; i < 5; i++) {
      clock.advance(DT);
      shake.updateLate(DT);
    }
    expect(shake.isShaking).toBe(false);
  });
});
