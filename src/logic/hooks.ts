/**
 * rig → logic の配線ヘルパー。
 *
 * logic は three に依存しないため、rig が THREE.Vector3 を返しても
 * ここでは x/y/z を持つ構造体としてだけ受け取る。rig 側の
 * `getPunchAnchor(zone: 'head' | 'body')` を EnemyBrainHooks の
 * `getPunchAnchor(zone: PunchZone)` へ変換するのが主な役割。
 */
import { PunchZone, toRigPunchZone, type PunchAnchor, type RigPunchZone } from './types';
import type { EnemyBrainHooks } from './enemy-brain';

/** rig 側の punch anchor 提供者。THREE.Vector3 はそのまま PunchAnchor として渡せる。 */
export interface PunchAnchorRig {
  getPunchAnchor(zone: RigPunchZone): PunchAnchor | null;
}

/** 座標だけを返す minimal な rig 形も受け付ける（head/body の2キー）。 */
export type PunchAnchorMap = Record<RigPunchZone, PunchAnchor | null>;

/** createHooksFromRig に渡す rig の形。関数か、head/body の座標マップか。 */
export type RigLike = PunchAnchorRig | PunchAnchorMap;

function isRigWithProvider(rig: RigLike): rig is PunchAnchorRig {
  return typeof (rig as PunchAnchorRig).getPunchAnchor === 'function';
}

/**
 * rig の getPunchAnchor('head'|'body') を logic の PunchZone 版へ橋渡しする。
 *
 * @param rig 関数版（getPunchAnchor）か、head/body の座標を持つオブジェクト版
 * @returns EnemyBrainHooks.getPunchAnchor にそのまま入れられる関数
 */
export function createPunchAnchorProvider(rig: RigLike): (zone: PunchZone) => PunchAnchor | null {
  return (zone: PunchZone): PunchAnchor | null => {
    const rigZone = toRigPunchZone(zone);
    const raw = isRigWithProvider(rig)
      ? rig.getPunchAnchor(rigZone)
      : rig[rigZone];
    if (!raw) return null;
    const { x, y, z } = raw;
    if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return null;
    // 新しいオブジェクトを返し、rig 側の Vector3 を書き換えないようにする。
    return { x, y, z };
  };
}

/** EnemyBrainHooks のうち、アンカー以外を上書きするためのオプション。 */
export type OtherHooks = Omit<EnemyBrainHooks, 'getPunchAnchor'>;

/**
 * rig から EnemyBrainHooks を組み立てる。
 *
 * 使い方（lead / app 側）:
 *   const hooks = createHooksFromRig(rig, {
 *     getPlayerPosition: () => player.position,
 *     ...
 *   });
 *   const brain = new EnemyBrain(id, actor, hooks);
 *
 * rig は `getPunchAnchor('head'|'body')` を持つオブジェクト（THREE.Vector3 を返してよい）。
 */
export function createHooksFromRig(rig: RigLike, otherHooks: OtherHooks): EnemyBrainHooks {
  return {
    ...otherHooks,
    getPunchAnchor: createPunchAnchorProvider(rig),
  };
}
