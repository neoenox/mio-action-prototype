/**
 * シナリオ別カメラの設定表。
 *
 * Unity 版はシナリオごとに別のカメラ（ThirdPersonCamera / BeltScreenLock /
 * PinHoldPose / WallSplitCamera / HeldBellyPunchDirector.Camera / 地面カメラ）を
 * 使っているため、単一の既定値では構図が合わない。
 *
 * 基準値は Unity の Prototype.unity（Camera の FOV = 40）と
 * RyonaConfig.Camera（Distance 5.2 / BeltPitch 14 / BeltPivotHeight 0.86 /
 * BeltMinZoomDistance 3.8 / BeltCloseUpMinDistance 0.82 / MinDistance 1.2）から取った。
 *
 * 可視高 = 2 * distance * tan(fov / 2)。身長 1.8m の人物が画面高さの
 * どれだけを占めるかは 1.8 / 可視高 で見積もれる。
 * 例: fov 40・distance 3.8 → 可視高 2.77m → 65%（Unity の実測とほぼ一致）。
 */

/** Unity Prototype.unity の Camera FOV。 */
export const UnityFov = 40;

export interface ScenarioCameraProfile {
  /** 目標距離 (m)。punch 演出中は倍率が掛かる。 */
  readonly distance: number;
  /** 見下ろし角 (度)。 */
  readonly pitch: number;
  /** 注視点の高さ (m)。 */
  readonly pivotHeight: number;
  /** 垂直画角 (度)。 */
  readonly fov: number;
}

/** 三人称の既定（BeltScreenLock 相当）。 */
const BELT: ScenarioCameraProfile = { distance: 3.8, pitch: 14, pivotHeight: 0.86, fov: UnityFov };

/**
 * 拘束の近景（PinHoldPose 相当）。顔と腹を同時に見せ、足元も切らない。
 * 可視高 2.33m（身長の 77%）で、Unity の pin の画に近い。
 */
const PIN: ScenarioCameraProfile = { distance: 3.2, pitch: 12, pivotHeight: 1.0, fov: UnityFov };

/** 壁 2 人がかりの横からの画（WallSplitCamera 相当）。可視高 2.04m（75%）。 */
const WALL_SPLIT: ScenarioCameraProfile = { distance: 2.8, pitch: 9, pivotHeight: 1.05, fov: UnityFov };

/**
 * 羽交い締めの腹/顔パン。
 * HeldBellyPunchCamera の倍率（腹 0.27 / 顔 0.22）は Unity の基本距離 5.2m に
 * 掛ける前提で作られているため、ここも 5.2m にしないと実距離が 1.0m まで詰まり、
 * 頭だけが画面を埋めて腹と拳が消える。5.2 × 0.269（クランプ下限）= 1.4m で Unity と一致。
 */
const HELD_PUNCH: ScenarioCameraProfile = { distance: 5.2, pitch: 10, pivotHeight: 1.0, fov: UnityFov };

/** 地面の相手（引き起こし・マウント）。見下ろしを強め、倒れた体が収まる距離。 */
const GROUND: ScenarioCameraProfile = { distance: 3.4, pitch: 22, pivotHeight: 0.8, fov: UnityFov };

const PROFILES: Readonly<Record<string, ScenarioCameraProfile>> = {
  idle: BELT,
  motionseq: BELT,
  pinball: BELT,
  wristlock: BELT,
  wristpull: BELT,
  wristpullcounter: BELT,
  bearhug: BELT,
  ankle: BELT,
  frontpin: PIN,
  wallpin: PIN,
  wallsplit: WALL_SPLIT,
  belly: HELD_PUNCH,
  bellybody: HELD_PUNCH,
  face: HELD_PUNCH,
  facemount: HELD_PUNCH,
  haul: GROUND,
  haulprone: GROUND,
  mount: GROUND,
  mountprone: GROUND,
};

/** シナリオ名からカメラ設定を引く。未知の名前は三人称の既定。 */
export function scenarioCameraProfile(name: string): ScenarioCameraProfile {
  return PROFILES[name] ?? BELT;
}

/** 距離と画角から、画面高さに対する身長 1.8m の占有率を見積もる（検証用）。 */
export function subjectHeightRatio(distance: number, fovDeg: number, subjectHeight = 1.8): number {
  const visible = 2 * distance * Math.tan((fovDeg * Math.PI) / 360);
  return visible > 0 ? subjectHeight / visible : 0;
}
