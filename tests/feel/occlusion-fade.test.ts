import { PerspectiveCamera, Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { OpaqueAlpha } from '../../src/feel/face-visibility.js';
import {
  DEFAULT_OCCLUSION_FADE_SETTINGS,
  isInFrontOfWallShot,
  isOccluding,
  needsCloseBodyFade,
  NO_FADE_EXEMPTION,
  updateOcclusionFades,
} from '../../src/feel/occlusion-fade.js';

function makeCamera(): PerspectiveCamera {
  const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
  camera.position.set(0, 1.2, 4);
  camera.lookAt(0, 1.1, 0);
  camera.updateMatrixWorld(true);
  camera.matrixWorldInverse.copy(camera.matrixWorld).invert();
  return camera;
}

describe('NeedsCloseBodyFade（距離だけの判定）', () => {
  it('彼女より奥でもカメラから近い敵はフェード対象', () => {
    expect(needsCloseBodyFade(1.8, 1.4)).toBe(true);
  });

  it('十分に離れた敵・手前の敵・同距離は対象外', () => {
    expect(needsCloseBodyFade(3.1, 1.4)).toBe(false);
    expect(needsCloseBodyFade(1.0, 1.4)).toBe(false);
    expect(needsCloseBodyFade(1.4, 1.4)).toBe(false);
  });

  it('既定値は Unity 版と同じ', () => {
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.fadeSeconds).toBe(0.10);
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.minAlpha).toBe(0.25);
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.fadeAngle).toBe(15);
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.nearFadeDistance).toBe(3.6);
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.nearFadeRatio).toBe(0.72);
    expect(DEFAULT_OCCLUSION_FADE_SETTINGS.closeBodyFadeDistance).toBe(2.4);
  });
});

describe('画面座標 gap と距離比の判定', () => {
  const camera = makeCamera();
  const player = new Vector3(0, 0, 0);

  it('彼女と同じ画面位置に手前の敵がいれば遮蔽', () => {
    const result = isOccluding(camera, new Vector3(0, 0, 1.0), player);
    expect(result.occluding).toBe(true);
    expect(result.ratio).toBeLessThan(1);
  });

  it('画面端にいる手前の敵は彼女を隠さない', () => {
    const result = isOccluding(camera, new Vector3(3, 0, 2.5), player);
    expect(result.occluding).toBe(false);
    expect(result.why).toBe('ok');
    expect(result.gap).toBeGreaterThan(result.allowed);
  });

  it('彼女より奥の敵は behind で遮蔽に数えない', () => {
    const result = isOccluding(camera, new Vector3(0, 0, -0.5), player);
    expect(result.why).toBe('behind');
    expect(result.occluding).toBe(false);
  });

  it('カメラから 1.3m より近い敵は無条件で遮蔽', () => {
    const near = makeCamera();
    near.position.set(0, 1.2, 2.0);
    near.lookAt(0, 1.1, 0);
    near.updateMatrixWorld(true);
    near.matrixWorldInverse.copy(near.matrixWorld).invert();
    const result = isOccluding(near, new Vector3(0, 0, 1.3), player);
    expect(result.why).toBe('inside');
    expect(result.occluding).toBe(true);
  });

  it('近すぎる敵は ratio < 0.72 で near フェード', () => {
    const result = isOccluding(camera, new Vector3(0, 0, 2.0), player);
    expect(result.occluding).toBe(true);
    expect(['near', 'gap', 'inside']).toContain(result.why);
  });
});

describe('フェード対象外', () => {
  const camera = makeCamera();
  const player = new Vector3(0, 0, 0);
  const blocker = { name: 'blocker', position: new Vector3(0, 0, 1.0) };

  it('壁2/3人がかり参加者と腹パン殴打役はalpha=1のまま', () => {
    const results = updateOcclusionFades(camera, player, [
      blocker,
      { name: 'captor', position: blocker.position, exemption: { wallSplitParticipant: true } },
      { name: 'assist', position: blocker.position, exemption: { bellyPunchAttacker: true } },
      { name: 'third', position: blocker.position, exemption: { wallCaptor: true } },
    ], {}, 0.1);
    const byName = Object.fromEntries(results.map((r) => [r.name, r]));
    expect(byName['blocker']!.alpha).toBeLessThan(1);
    expect(byName['captor']!.alpha).toBe(1);
    expect(byName['assist']!.alpha).toBe(1);
    expect(byName['third']!.alpha).toBe(1);
  });

  it('壁正面ショットの手前敵は完全に消える', () => {
    const results = updateOcclusionFades(camera, player, [
      { name: 'front', position: blocker.position, exemption: { inFrontOfWallShot: true } },
    ]);
    expect(results[0]!.alpha).toBe(0);
    expect(results[0]!.why).toBe('front-of-wall-shot');
  });

  it('フェードは fadeSeconds で目標へ近づく', () => {
    const settings = { ...DEFAULT_OCCLUSION_FADE_SETTINGS };
    const first = updateOcclusionFades(camera, player, [blocker], {}, 0.05, settings);
    expect(first[0]!.alpha).toBeCloseTo(0.5, 6);
    const second = updateOcclusionFades(
      camera, player, [blocker], { blocker: first[0]!.alpha }, 0.05, settings,
    );
    expect(second[0]!.alpha).toBeCloseTo(settings.minAlpha, 6);
  });

  it('alpha<0.5 の敵は FaceVisibility の遮蔽に数えない', () => {
    const faded = updateOcclusionFades(camera, player, [blocker], {}, 0.1);
    expect(faded[0]!.alpha).toBe(DEFAULT_OCCLUSION_FADE_SETTINGS.minAlpha);
    expect(faded[0]!.alpha).toBeLessThan(OpaqueAlpha);
  });

  it('デフォルトの例外フラグはすべて false', () => {
    expect(NO_FADE_EXEMPTION.wallSplitParticipant).toBe(false);
    expect(NO_FADE_EXEMPTION.wallCaptor).toBe(false);
    expect(NO_FADE_EXEMPTION.bellyPunchAttacker).toBe(false);
    expect(NO_FADE_EXEMPTION.mountAttacker).toBe(false);
    expect(NO_FADE_EXEMPTION.inFrontOfWallShot).toBe(false);
  });
});

describe('InFrontOfWallShot', () => {
  it('カメラ側にいる敵だけを壁正面ショットの手前とみなす', () => {
    const camera = makeCamera();
    const player = new Vector3(0, 0, 0);
    const captor = new Vector3(0, 0, -0.5);
    expect(isInFrontOfWallShot(camera, player, captor, new Vector3(0, 0, 1.5))).toBe(true);
    expect(isInFrontOfWallShot(camera, player, captor, new Vector3(0, 0, -3))).toBe(false);
  });
});
