import { PerspectiveCamera, Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { CameraController } from '../../src/feel/camera.js';
import {
  advanceFinisherBlend,
  decayImpactPulse,
  DEFAULT_HELD_PUNCH_CAMERA_SETTINGS,
  fatigueFromHits,
  HeldBellyPunchCamera,
  Phase,
  PunchZone,
  REFRESH_HOLD_SECONDS,
} from '../../src/feel/held-belly-punch-camera.js';

const DT = 1 / 60;

function setupRig(): CameraController {
  const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
  const rig = new CameraController(camera, { distance: 5, minDistance: 0.85 });
  rig.setTarget(new Vector3(0, 0, 0));
  for (let i = 0; i < 30; i++) rig.update(DT, DT);
  return rig;
}

function director(rig: CameraController | null = null): HeldBellyPunchCamera {
  return new HeldBellyPunchCamera(rig);
}

describe('HeldBellyPunchCamera の既定値', () => {
  it('Unity 版と同じ 7 Phase / 2 Zone の値', () => {
    expect(Phase.Idle).toBe(0);
    expect(Phase.WaitingThrow).toBe(1);
    expect(Phase.Combo).toBe(2);
    expect(Phase.HandOffWait).toBe(3);
    expect(Phase.Finisher).toBe(4);
    expect(Phase.Collapse).toBe(5);
    expect(Phase.FinisherDodge).toBe(6);
    expect(PunchZone.Face).toBe(0);
    expect(PunchZone.Body).toBe(1);
  });

  it('腹 0.27 / 0.34 / -4° / -32°、顔 0.22 / 0.46 / 1.5° / -36°、決め 0.42 / -0.12 / -6°', () => {
    const s = DEFAULT_HELD_PUNCH_CAMERA_SETTINGS;
    expect(s.bodyDistanceScale).toBe(0.27);
    expect(s.bodyPivotOffset).toBe(0.34);
    expect(s.bodyPitchOffset).toBe(-4);
    expect(s.bodyYawOffset).toBe(-32);
    expect(s.faceDistanceScale).toBe(0.22);
    expect(s.facePivotOffset).toBe(0.46);
    expect(s.facePitchOffset).toBe(1.5);
    expect(s.faceYawOffset).toBe(-36);
    expect(s.finisherDistanceScale).toBe(0.42);
    expect(s.finisherPivotDelta).toBe(-0.12);
    expect(s.finisherPitchDelta).toBe(-6);
  });

  it('デフォルトの腹コンボは 距離0.27 ピボット0.34 ピッチ-4 ヨー-32', () => {
    const d = director();
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    const shot = d.shot();
    expect(shot.distanceScale).toBeCloseTo(0.27, 6);
    expect(shot.pivot).toBeCloseTo(0.34, 6);
    expect(shot.pitch).toBeCloseTo(-4, 6);
    expect(shot.yaw).toBeCloseTo(-32, 6);
  });

  it('デフォルトの顔コンボは 素の距離0.22 ピボット0.46 ピッチ1.5 ヨー-36（実距離下限で 1.4m にクランプ）', () => {
    const d = director();
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Face, playerX: 0 });
    const shot = d.shot();
    expect(d.rawShotDistanceScale()).toBeCloseTo(0.22, 6);
    expect(shot.distanceScale).toBeCloseTo(1.4 / 5.2, 6);
    expect(shot.pivot).toBeCloseTo(0.46, 6);
    expect(shot.pitch).toBeCloseTo(1.5, 6);
    expect(shot.yaw).toBeCloseTo(-36, 6);
  });
});

describe('ShotDistanceScale / ShotPivot', () => {
  it('疲労 4 打で距離 -4%、インパクト 1 でさらに -5%', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Body, playerX: 0,
      fatigue: fatigueFromHits(4), impactPulse: 1,
    });
    expect(fatigueFromHits(4)).toBeCloseTo(1, 6);
    expect(d.rawShotDistanceScale()).toBeCloseTo(0.27 * (1 - 0.04 - 0.05), 6);
    // 実距離 0.27*0.91*5.2 = 1.28m なので下限 1.4m にクランプされる
    expect(d.shotDistanceScale()).toBeCloseTo(1.4 / 5.2, 6);
  });

  it('折れ・疲労・だらりでピボットを下げ、持ち上げで上げる', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Body, playerX: 0,
      fold: 1, fatigue: 1, dangle: 1, lift: 0,
    });
    // 0.34 - 0.05 - 0.02 - 0.04 = 0.23
    expect(d.shotPivot()).toBeCloseTo(0.23, 6);
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Body, playerX: 0,
      fold: 1, fatigue: 1, dangle: 1, lift: 1,
    });
    expect(d.shotPivot()).toBeCloseTo(0.245, 6);
  });

  it('抱え上げでは顔・腹どちらもピボット +0.03', () => {
    const d = director();
    d.setInput({ phase: Phase.Finisher, zone: PunchZone.Body, playerX: 0, finisherHoist: 1 });
    expect(d.shotPivot()).toBeCloseTo(0.37, 6);
    d.setInput({ phase: Phase.Finisher, zone: PunchZone.Face, playerX: 0, finisherHoist: 1 });
    expect(d.shotPivot()).toBeCloseTo(0.49, 6);
  });

  it('決めの全身ショットへ 100% 寄せると距離0.42・ピボット-0.12・ピッチ-6°', () => {
    const d = director();
    d.setInput({
      phase: Phase.Finisher, zone: PunchZone.Body, playerX: 0, finisherShotBlend: 1,
    });
    const shot = d.shot();
    expect(shot.distanceScale).toBeCloseTo(0.42, 6);
    expect(shot.pivot).toBeCloseTo(0.34 - 0.12, 6);
    expect(shot.pitch).toBeCloseTo(-4 - 6, 6);
  });

  it('決めの全身ショットは顔 Zone でも同じ距離倍率', () => {
    const d = director();
    d.setInput({
      phase: Phase.Finisher, zone: PunchZone.Face, playerX: 0, finisherShotBlend: 1,
    });
    expect(d.shotDistanceScale()).toBeCloseTo(0.42, 6);
  });
});

describe('FocusYaw / FocusX / FacingSign', () => {
  it('forward.x < -0.1 ならヨーとオフセットの符号が反転する', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Body, playerX: 2, facingSign: -0.4,
    });
    expect(d.focusYaw()).toBeCloseTo(32, 6);
    // 0.15 * 0.27/0.32 = 0.1265625
    expect(d.focusX()).toBeCloseTo(2 - 0.15 * (0.27 / 0.32), 6);
  });

  it('顔面は 0.30 * faceScale/0.31 だけ横へ寄せる', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Face, playerX: -1, facingSign: 1,
    });
    expect(d.focusX()).toBeCloseTo(-1 + 0.30 * (0.22 / 0.31), 6);
  });
});

describe('CameraController への押し込み', () => {
  it('refreshCamera は 1.6 秒の押し込みを流す（0.2 秒未満は丸める）', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    d.refreshCamera();
    rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeLessThan(0.4);
    expect(rig.effectPivotOffset).toBeGreaterThan(0.2);
    expect(rig.isFocusing).toBe(true);
    expect(REFRESH_HOLD_SECONDS).toBe(1.6);

    // 0.01 秒を渡しても 0.2 秒に丸められる（1 フレーム後は envelope の途中）
    const rig2 = setupRig();
    const d2 = director(rig2);
    d2.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    d2.refreshCamera(0.01);
    rig2.update(DT, DT);
    const shortPush = rig2.effectDistanceScale;
    expect(shortPush).toBeLessThan(1);
    expect(shortPush).toBeGreaterThan(0.27);
    // 1.6 秒の押し込みのほうが 1 フレーム後も深い
    expect(shortPush).toBeGreaterThan(rig.effectDistanceScale);
  });

  it('holdFraming は毎フレーム呼ぶと距離倍率を保持する', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    for (let i = 0; i < 180; i++) {
      d.holdFraming();
      rig.update(DT, DT);
    }
    expect(rig.effectDistanceScale).toBeCloseTo(0.27, 4);
    expect(rig.effectPivotOffset).toBeCloseTo(0.34, 4);
  });

  it('holdFraming をやめると 0.5 秒で戻る', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    d.holdFraming();
    rig.update(DT, DT);
    for (let i = 0; i < 60; i++) rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeCloseTo(1, 4);
    expect(rig.effectPivotOffset).toBeCloseTo(0, 4);
  });

  it('applyCollapseCamera は 2.4 秒保持する', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({
      phase: Phase.Collapse, zone: PunchZone.Body, playerX: 0, finisherShotBlend: 1,
    });
    d.applyCollapseCamera();
    for (let i = 0; i < 90; i++) rig.update(DT, DT);
    expect(rig.effectDistanceScale).toBeGreaterThan(0.4);
  });

  it('clearFocus は PushFocus(0,0) で中央寄せを即時リセットする', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 3 });
    d.refreshCamera();
    d.clearFocus();
    expect(d.isFocusCleared).toBe(true);
    rig.update(DT, DT);
    expect(rig.isFocusing).toBe(false);
  });

  it('カメラ未接続でも構図は計算できる', () => {
    const d = director();
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    expect(d.refreshCamera().distanceScale).toBeCloseTo(0.27, 6);
    expect(d.holdFraming().pivot).toBeCloseTo(0.34, 6);
  });
});

describe('isCameraActive / 時間の進行', () => {
  it('Combo / Finisher / FinisherDodge / Collapse だけカメラを握る', () => {
    const d = director();
    for (const phase of [Phase.Idle, Phase.WaitingThrow, Phase.HandOffWait]) {
      d.setInput({ phase, zone: PunchZone.Body, playerX: 0 });
      expect(d.isCameraActive).toBe(false);
    }
    for (const phase of [Phase.Combo, Phase.Finisher, Phase.FinisherDodge, Phase.Collapse]) {
      d.setInput({ phase, zone: PunchZone.Body, playerX: 0 });
      expect(d.isCameraActive).toBe(true);
    }
  });

  it('ImpactPulse は unscaledDelta * 8 で 1 → 0 へ戻る', () => {
    expect(decayImpactPulse(1, 0.05)).toBeCloseTo(0.6, 6);
    expect(decayImpactPulse(1, 0.2)).toBe(0);
    expect(decayImpactPulse(0, 1)).toBe(0);
  });

  it('FinisherShotBlend は target=1 を ComboTailSeconds で進める', () => {
    expect(advanceFinisherBlend(0, 1, 0.35, 0.35)).toBe(1);
    expect(advanceFinisherBlend(1, 0, 0.25)).toBe(0);
    expect(advanceFinisherBlend(0, 1, 0.1, 0.35)).toBeCloseTo(0.1 / 0.35, 6);
  });

  it('applyOverrides で起動引数の上書きが反映される', () => {
    const d = director();
    d.applyOverrides({ bodyDistanceScale: 0.5, bodyYawOffset: -20 });
    d.setInput({ phase: Phase.Combo, zone: PunchZone.Body, playerX: 0 });
    expect(d.zoneDistanceScale()).toBeCloseTo(0.5, 6);
    expect(d.focusYaw()).toBeCloseTo(-20, 6);
  });
});

describe('実距離の下限（頭貼り付き防止）', () => {
  const MIN = DEFAULT_HELD_PUNCH_CAMERA_SETTINGS.minShotDistance;
  const BASE = DEFAULT_HELD_PUNCH_CAMERA_SETTINGS.baseDistance;

  it('既定値 1.4m / 5.2m、腹の 0.27 は 1.404m でそのまま通る', () => {
    expect(MIN).toBe(1.4);
    expect(BASE).toBe(5.2);
    expect(0.27 * BASE).toBeGreaterThanOrEqual(MIN);
  });

  it('顔・疲労・インパクトのどの組み合わせでも実距離が 1.4m を下回らない', () => {
    const d = director();
    const zones = [PunchZone.Body, PunchZone.Face];
    const pulses = [0, 0.5, 1, 1.35, 1.55];
    const fatigues = [0, 0.5, 1];
    const blends = [0, 0.5, 1];
    for (const zone of zones) {
      for (const pulse of pulses) {
        for (const fatigue of fatigues) {
          for (const blend of blends) {
            d.setInput({
              phase: Phase.Combo, zone, playerX: 0,
              impactPulse: pulse, fatigue, finisherShotBlend: blend,
            });
            expect(d.shotDistanceScale() * BASE).toBeGreaterThanOrEqual(MIN - 1e-9);
          }
        }
      }
    }
  });

  it('一番寄る条件でも 1.4m にクランプされ、顔 Zone の素の倍率は 0.22 のまま', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Face, playerX: 0,
      impactPulse: 1.55, finisherShotBlend: 0,
    });
    expect(d.rawShotDistanceScale()).toBeCloseTo(0.22 * (1 - 0.05 * 1.55), 6);
    expect(d.shotDistanceScale() * BASE).toBeCloseTo(MIN, 6);
  });

  it('holdFraming / refreshCamera がクランプ後の倍率を CameraController に渡す', () => {
    const rig = setupRig();
    const d = director(rig);
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Face, playerX: 0, impactPulse: 1,
    });
    d.holdFraming();
    rig.update(DT, DT);
    // rig の基本距離 5m に対しても 1.4/5.2 が渡るので実距離は 1.346m 以上
    expect(rig.effectDistanceScale).toBeCloseTo(MIN / BASE, 6);

    const rig2 = setupRig();
    const d2 = director(rig2);
    d2.setInput({
      phase: Phase.Combo, zone: PunchZone.Face, playerX: 0, impactPulse: 1,
    });
    d2.refreshCamera();
    rig2.update(DT, DT);
    // RefreshCamera は 1.6 秒の envelope が掛かるので 1 フレーム目は scale に達しない。
    // 素の顔倍率 0.22 よりは緩やか（＝1.4m 側へクランプされている）。
    expect(rig2.effectDistanceScale).toBeGreaterThan(MIN / BASE);
    expect(rig2.effectDistanceScale).toBeGreaterThan(0.22);
  });

  it('顔と腹が両方映る範囲の注視点高さを保つ（ピボットが頭を突き抜けない）', () => {
    const d = director();
    d.setInput({
      phase: Phase.Combo, zone: PunchZone.Face, playerX: 0,
      impactPulse: 1.55, finisherHoist: 1,
    });
    // 顔の基準 0.46 + 抱え上げ 0.03。1.4m 先のピボットが顔より高くなりすぎない。
    expect(d.shotPivot()).toBeCloseTo(0.49, 6);
    expect(d.shotPivot()).toBeLessThan(d.shotDistanceScale() * BASE * 0.5);
  });
});

describe('カメラ初動の不連続', () => {
  it('target 初設定直後の 1 フレーム目で pivot と距離が確定し、#1→#2 で飛ばない', () => {
    const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
    const rig = new CameraController(camera, { distance: 5, minDistance: 0.85 });
    rig.setTarget(new Vector3(0, 0, 0));

    rig.update(DT, DT);
    const frame1 = camera.position.clone();
    rig.update(DT, DT);
    const frame2 = camera.position.clone();

    expect(frame1.distanceTo(frame2)).toBeLessThan(1e-6);
    // smoothPivot が target + pivotOffset へ snap しているかを
    // camera.lookAt の注視点（= camera から forward へ distance 進んだ点）で見る。
    const lookTarget = camera.position.clone()
      .addScaledVector(camera.getWorldDirection(new Vector3()), 5);
    expect(lookTarget.y).toBeCloseTo(1.4, 4);
    expect(lookTarget.x).toBeCloseTo(0, 4);
    expect(lookTarget.z).toBeCloseTo(0, 4);
  });

  it('初回 target が null 以外なら smoothPivot が即座に設定される', () => {
    const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
    const rig = new CameraController(camera, { distance: 5, minDistance: 0.85 });
    rig.setTarget(new Vector3(3, 0, 2));
    rig.update(DT, DT);
    const lookTarget = camera.position.clone()
      .addScaledVector(camera.getWorldDirection(new Vector3()), 5);
    expect(lookTarget.x).toBeCloseTo(3, 4);
    expect(lookTarget.z).toBeCloseTo(2, 4);
    expect(lookTarget.y).toBeCloseTo(1.4, 4);
  });

  it('beltMode でも初回に smoothPivot が target の X と高さへ snap する', () => {
    const camera = new PerspectiveCamera(45, 16 / 9, 0.1, 100);
    const rig = new CameraController(camera, {
      distance: 5, beltMode: true, beltPivotHeight: 1.25, beltPitch: 12,
    });
    rig.setTarget(new Vector3(2, 0, 0));
    rig.update(DT, DT);
    const frame1 = camera.position.clone();
    rig.update(DT, DT);
    expect(frame1.distanceTo(camera.position)).toBeLessThan(1e-6);
    expect(rig.renderedX).toBeCloseTo(2, 4);
  });
});
