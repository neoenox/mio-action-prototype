import { MathUtils, Vector3 } from 'three';

/**
 * Unity 版 WallSplitCamera の移植。壁の二体がかり（Y 字拘束）の固定ショット。
 *
 * 彼女の胸 0.70 + 拘束役 0.15 + アシスト 0.15 で注視点を作る。
 * 「彼女を中心に回す」と正面の敵が遮蔽物になるため、二人の関係軸に対して
 * 横から撮る。既定は斜め 6°・距離 1.55m・高さ 0.40m・注視点高 1.05m・横 0.06m。
 */
export type WallSplitShotMode = 'angle' | 'hold' | 'front';

export interface WallSplitCameraSettings {
  mode: WallSplitShotMode;
  angleDeg: number;
  distance: number;
  height: number;
  targetHeight: number;
  lateral: number;
}

export const DEFAULT_WALL_SPLIT_SETTINGS: WallSplitCameraSettings = {
  mode: 'angle',
  angleDeg: 6,
  distance: 1.55,
  height: 0.40,
  targetHeight: 1.05,
  lateral: 0.06,
};

/** 壁押さえつけ（1 体）のカメラ位置オフセット（hold モード用）。 */
const PIN_OFFSET = new Vector3(-2.25, 0.47, -1.75);

const UP = new Vector3(0, 1, 0);

export interface WallSplitShot {
  position: Vector3;
  target: Vector3;
}

/**
 * 二体がかりのショットを組む。
 *
 * @param herPos 彼女の位置
 * @param herForward 彼女の正面（+Z 前提）
 * @param captorPos 拘束役の位置
 * @param assistPos アシストの位置
 * @param extraDistance 距離の上乗せ（三人がかりで全員を収める）
 * @param lateralShift 狙いを拘束役側へ寄せる量
 */
export function buildWallSplitShot(
  herPos: Vector3,
  herForward: Vector3,
  captorPos: Vector3,
  assistPos: Vector3,
  settings: Partial<WallSplitCameraSettings> = {},
  extraDistance = 0,
  lateralShift = 0,
): WallSplitShot {
  const s: WallSplitCameraSettings = { ...DEFAULT_WALL_SPLIT_SETTINGS, ...settings };

  const front = herForward.clone().setY(0);
  if (front.lengthSq() <= 1e-4) front.set(0, 0, -1);
  front.normalize();
  const right = new Vector3().crossVectors(UP, front).normalize();

  const herChest = herPos.clone().addScaledVector(UP, s.targetHeight);
  const captorChest = captorPos.clone().addScaledVector(UP, 1.02);
  const assistChest = assistPos.clone().addScaledVector(UP, 1.02);

  if (s.mode === 'hold') {
    const herMid = herPos.clone().addScaledVector(UP, 1.02);
    const himMid = captorPos.clone().addScaledVector(UP, 1.02);
    const mid = herMid.lerp(himMid, 0.45);
    return { position: mid.clone().add(PIN_OFFSET), target: mid };
  }

  const target = herChest.clone().multiplyScalar(0.70)
    .addScaledVector(captorChest, 0.15)
    .addScaledVector(assistChest, 0.15);

  const spread = Math.abs(captorChest.clone().sub(assistChest).dot(right));
  const distance = s.distance + 0.35 * Math.max(0, spread - 1.6) + extraDistance;

  const angle = s.mode === 'front' ? 0 : s.angleDeg;
  const radians = angle * MathUtils.DEG2RAD;

  // 斜めは拘束役と反対（アシスト側）から。打つ腕が手前に来て腹パンが読める。
  let camSide = Math.sign(captorChest.clone().sub(herChest).dot(right));
  if (Math.abs(camSide) < 0.01) camSide = 1;
  const direction = front.clone().multiplyScalar(Math.cos(radians))
    .addScaledVector(right, Math.sin(radians) * -camSide);

  target.addScaledVector(right, -camSide * s.lateral);
  target.addScaledVector(right, lateralShift * camSide);

  const position = target.clone().addScaledVector(direction, distance).addScaledVector(UP, s.height);
  return { position, target };
}

/**
 * 三人がかり用の設定。距離を 0.25m 足し、狙いを拘束役側へ 0.12m 寄せる。
 * RequiredRatio[3] = 0.75 を満たす画を維持するための既定値。
 */
export const THREE_ENEMY_WALL_SPLIT = {
  extraDistance: 0.25,
  lateralShift: 0.12,
} as const;
