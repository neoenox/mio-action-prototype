import { MathUtils, PerspectiveCamera, Vector3 } from 'three';

/**
 * Unity 版 OcclusionFade.cs の判定部分の移植。
 *
 * マテリアル操作（Three.js の Material.transparent / opacity 書き換え）は app 側が
 * 行い、このモジュールは「どの敵を薄くするか」だけを決める。
 * FaceVisibility 側は alpha < OpaqueAlpha の敵を遮蔽に数えないため、
 * ここで返した alpha をそのまま EnemyFigureView.alpha へ流せる。
 */

/** 既定値。Unity 版のインスペクタ初期値＋ NonSerialized 定数と同じ。 */
export const DEFAULT_OCCLUSION_FADE_SETTINGS = {
  fadeSeconds: 0.10,
  minAlpha: 0.25,
  fadeAngle: 15,
  nearFadeDistance: 3.6,
  nearFadeRatio: 0.72,
  closeBodyFadeDistance: 2.4,
  /** カメラからこの距離より近い敵は、画面座標の比較が成立しないので無条件でフェード。 */
  cameraInsideDistance: 1.3,
} as const;

export type OcclusionFadeSettings = {
  -readonly [K in keyof typeof DEFAULT_OCCLUSION_FADE_SETTINGS]: number;
};

/**
 * フェードから守る役割。Unity 版 LateUpdate の
 * splitShot / heldParticipant / wallShot / inMountShot に相当する。
 */
export interface FadeExemption {
  /** 壁2/3人がかりの参加者（拘束役・アシスト・3人目・待機中の2人目）。 */
  wallSplitParticipant: boolean;
  /** 壁拘束の拘束役（本人は消さない）。 */
  wallCaptor: boolean;
  /** 腹パンの殴打役（LastAttackerBrain）。 */
  bellyPunchAttacker: boolean;
  /** マウントの攻撃役。 */
  mountAttacker: boolean;
  /** 壁正面ショットで彼女の手前に入っている敵（完全に消す側）。 */
  inFrontOfWallShot: boolean;
}

export const NO_FADE_EXEMPTION: FadeExemption = {
  wallSplitParticipant: false,
  wallCaptor: false,
  bellyPunchAttacker: false,
  mountAttacker: false,
  inFrontOfWallShot: false,
};

/** 判定の入力。位置はワールド座標。 */
export interface OcclusionCandidate {
  name: string;
  /** 敵の足元（transform.position）。判定では +0.9m した胴を使う。 */
  position: Vector3;
  exemption?: Partial<FadeExemption>;
}

export interface OcclusionFadeResult {
  name: string;
  /** 現在の不透明度 (0..1)。FaceVisibility の OpaqueAlpha 判定にそのまま使う。 */
  alpha: number;
  /** 判定の理由（デバッグ用）。 */
  why: string;
}

/**
 * 彼女より奥にいても、カメラから近い敵は体が画面を覆うのでフェード対象。
 * 距離だけで決まるのでテストからも呼べる（Unity 版 NeedsCloseBodyFade）。
 */
export function needsCloseBodyFade(
  selfDistance: number,
  playerDistance: number,
  settings: OcclusionFadeSettings = { ...DEFAULT_OCCLUSION_FADE_SETTINGS },
): boolean {
  return selfDistance > playerDistance && selfDistance < settings.closeBodyFadeDistance;
}

/**
 * カメラ → 彼女 の線の上に、彼女より手前で入っているか。
 * 角度ではなく画面座標の gap と距離比で判定する。
 */
export function isOccluding(
  camera: PerspectiveCamera,
  enemyPosition: Vector3,
  playerPosition: Vector3,
  settings: OcclusionFadeSettings = { ...DEFAULT_OCCLUSION_FADE_SETTINGS },
): { occluding: boolean; ratio: number; gap: number; allowed: number; why: string } {
  const cameraPosition = new Vector3().setFromMatrixPosition(camera.matrixWorld);
  const toPlayer = playerPosition.clone().setY(playerPosition.y + 1.0).sub(cameraPosition);
  const toSelf = enemyPosition.clone().setY(enemyPosition.y + 0.9).sub(cameraPosition);
  const playerDistance = toPlayer.length();
  const selfDistance = toSelf.length();

  if (playerDistance < 0.01) return { occluding: false, ratio: 0, gap: 0, allowed: 0, why: 'nodist' };

  if (needsCloseBodyFade(selfDistance, playerDistance, settings)) {
    return { occluding: true, ratio: selfDistance / playerDistance, gap: 0, allowed: 0, why: 'close' };
  }
  if (selfDistance > playerDistance) {
    return { occluding: false, ratio: selfDistance / playerDistance, gap: 0, allowed: 0, why: 'behind' };
  }

  const ratio = selfDistance / playerDistance;

  camera.updateMatrixWorld(true);
  camera.matrixWorldInverse.copy(camera.matrixWorld).invert();
  const vpSelf = enemyPosition.clone().setY(enemyPosition.y + 0.9).project(camera);
  const vpPlayer = playerPosition.clone().setY(playerPosition.y + 1.0).project(camera);
  if (vpSelf.z > 1 || vpPlayer.z > 1 || vpSelf.z < -1 || vpPlayer.z < -1) {
    return { occluding: false, ratio, gap: 0, allowed: 0, why: 'offscreen' };
  }

  if (selfDistance < settings.cameraInsideDistance) {
    return { occluding: true, ratio, gap: 0, allowed: 0, why: 'inside' };
  }
  if (selfDistance < settings.nearFadeDistance && ratio < settings.nearFadeRatio) {
    return { occluding: true, ratio, gap: 0, allowed: 0, why: 'near' };
  }

  // 画面座標の差をピクセルに直す（Unity 版は Screen.width/height を使う）。
  // ここでは 16:9・高さ 1080 を基準にして-screen サイズ非依存にする。
  const screenHeight = 1080;
  const screenWidth = screenHeight * 16 / 9;
  const dx = (vpSelf.x - vpPlayer.x) * 0.5 * screenWidth;
  const dy = (vpSelf.y - vpPlayer.y) * 0.5 * screenHeight;
  const gap = Math.hypot(dx, dy);
  const allowed = screenHeight * lerp(0.46, 0.16, MathUtils.clamp(ratio, 0, 1));
  return { occluding: gap < allowed, ratio, gap, allowed, why: gap < allowed ? 'gap' : 'ok' };
}

const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;

/**
 * 遮蔽判定と例外をまとめて、各敵の alpha を返す。
 * alpha は fadeSeconds で目標へ近づける（MoveTowards 相当）。
 */
export function updateOcclusionFades(
  camera: PerspectiveCamera,
  playerPosition: Vector3,
  enemies: readonly OcclusionCandidate[],
  currentAlphas: Readonly<Record<string, number>> = {},
  scaledDelta = 0,
  settings: OcclusionFadeSettings = { ...DEFAULT_OCCLUSION_FADE_SETTINGS },
): OcclusionFadeResult[] {
  return enemies.map((enemy) => {
    const ex = { ...NO_FADE_EXEMPTION, ...(enemy.exemption ?? {}) };
    let alpha = currentAlphas[enemy.name] ?? 1;
    let why: string;

    if (ex.inFrontOfWallShot) {
      alpha = 0;
      why = 'front-of-wall-shot';
    } else if (ex.wallSplitParticipant || ex.wallCaptor || ex.bellyPunchAttacker || ex.mountAttacker) {
      alpha = 1;
      why = 'exempt';
    } else {
      const judged = isOccluding(camera, enemy.position, playerPosition, settings);
      why = judged.why;
      const target = judged.occluding ? settings.minAlpha : 1;
      const step = settings.fadeSeconds > 0.001 ? scaledDelta / settings.fadeSeconds : Infinity;
      const diff = target - alpha;
      alpha = Math.abs(diff) <= step ? target : alpha + Math.sign(diff) * step;
    }
    return { name: enemy.name, alpha: MathUtils.clamp(alpha, 0, 1), why };
  });
}

/**
 * 壁正面ショットの手前判定（Unity 版 InFrontOfWallShot）。
 * 腕や胴が画面を塞ぐので、敵の中心が画面外でも奥行きで判定する。
 */
export function isInFrontOfWallShot(
  camera: PerspectiveCamera,
  playerPosition: Vector3,
  captorPosition: Vector3,
  enemyPosition: Vector3,
): boolean {
  const middle = playerPosition.clone().add(captorPosition).multiplyScalar(0.5);
  const forward = new Vector3(0, 0, -1).applyQuaternion(camera.quaternion);
  return enemyPosition.clone().sub(middle).dot(forward) < 0.45;
}
