import { describe, expect, test } from 'vitest';
import {
  UnityFov,
  scenarioCameraProfile,
  subjectHeightRatio,
} from '../../src/feel/scenario-camera.js';

describe('シナリオ別カメラ', () => {
  test('Unity のカメラ FOV は Prototype.unity と同じ 40 度', () => {
    expect(UnityFov).toBe(40);
  });

  test('三人称の既定は Unity の belt カメラの値（3.8 / 14 / 0.86）', () => {
    const p = scenarioCameraProfile('idle');
    expect(p.distance).toBe(3.8);
    expect(p.pitch).toBe(14);
    expect(p.pivotHeight).toBe(0.86);
  });

  test('拘束系は三人称より寄る', () => {
    const belt = scenarioCameraProfile('idle');
    for (const name of ['frontpin', 'wallpin']) {
      const p = scenarioCameraProfile(name);
      expect(p.distance).toBeLessThan(belt.distance);
    }
  });

  test('地面の相手は三人称より見下ろす', () => {
    const belt = scenarioCameraProfile('idle');
    for (const name of ['haul', 'haulprone', 'mount', 'mountprone']) {
      expect(scenarioCameraProfile(name).pitch).toBeGreaterThan(belt.pitch);
    }
  });

  test('未知のシナリオは三人称の既定に落ちる', () => {
    expect(scenarioCameraProfile('unknown-scenario')).toEqual(scenarioCameraProfile('idle'));
  });

  test('占有率の見積りが Unity の実測に近い', () => {
    // Unity は fov 40・belt 距離 3.8m で身長 1.8m が画面の 6 割強を占める。
    const ratio = subjectHeightRatio(3.8, UnityFov);
    expect(ratio).toBeGreaterThan(0.6);
    expect(ratio).toBeLessThan(0.72);
    // 遠いほど小さくなる。
    expect(subjectHeightRatio(5.2, UnityFov)).toBeLessThan(ratio);
  });

  test('全シナリオのプロファイルが身長を画面に収める', () => {
    const names = [
      'idle', 'motionseq', 'pinball', 'wristlock', 'wristpull', 'wristpullcounter',
      'bearhug', 'ankle', 'frontpin', 'wallpin', 'wallsplit', 'belly', 'bellybody',
      'face', 'facemount', 'haul', 'haulprone', 'mount', 'mountprone',
    ];
    for (const name of names) {
      const p = scenarioCameraProfile(name);
      // 可視高が身長 1.8m を下回ると、足元か頭が切れる。
      expect(subjectHeightRatio(p.distance, p.fov)).toBeLessThanOrEqual(1.0);
      expect(p.distance).toBeGreaterThan(0);
      expect(p.fov).toBeGreaterThan(0);
    }
  });
});
