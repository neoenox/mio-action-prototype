/**
 * punch anchor（logic の EnemyBrainHooks.getPunchAnchor が要求する拳の座標）。
 *
 * logic は数値の { x, y, z } だけを知る。rig 側は実ボーンのワールド座標を
 * THREE.Vector3 で返す（Vector3 はそのまま x/y/z を持つのでフックへ渡せる）。
 */
import { Vector3 } from 'three';
import { JointSkeleton } from './skeleton.js';

/** 'head'/'face' は顔パンチ、'body'/'belly' は腹パンチ。数値は logic の PunchZone（0=Face, 1=Body）。 */
export type PunchZoneLike = 'head' | 'body' | 'face' | 'belly' | number;

export const PUNCH_ANCHOR_ZONES = ['head', 'body'] as const;

export type PunchAnchorZone = (typeof PUNCH_ANCHOR_ZONES)[number];

export function normalizePunchZone(zone: PunchZoneLike): PunchAnchorZone {
  if (typeof zone === 'number') return zone === 1 ? 'body' : 'head';
  const lower = zone.toLowerCase();
  return lower === 'body' || lower === 'belly' ? 'body' : 'head';
}

/**
 * 実ボーンから拳のワールド座標を返す。
 *
 * 顔は右拳（jab / 顔面殴打）、胴は左拳（腹パンチ）を使う。
 * どちらも「いまのボーン」を見るので、Animator が腕を振った後の座標になる。
 */
export function punchAnchorFromSkeleton(
  skeleton: JointSkeleton,
  zone: PunchZoneLike,
): Vector3 | null {
  const hand = normalizePunchZone(zone) === 'head'
    ? skeleton.bones.rHand
    : skeleton.bones.lHand;
  if (!hand) return null;
  hand.updateWorldMatrix(true, false);
  return hand.getWorldPosition(new Vector3());
}

/** フック渡し用のラッパー。logic の PunchZone enum（number）もそのまま受け取れる。 */
export function createPunchAnchorHook(
  skeleton: JointSkeleton,
): (zone: number | string) => Vector3 | null {
  return (zone) => punchAnchorFromSkeleton(skeleton, zone as PunchZoneLike);
}

/**
 * logic の狙い高さに合わせたアンカー（head = 頭のボーン位置、body = 胸のボーン位置）。
 * 当たり判定を「拳 vs 狙い」で取るときの comparisons 用に公開する。
 */
export function targetAnchorFromSkeleton(
  skeleton: JointSkeleton,
  zone: PunchZoneLike,
): Vector3 | null {
  const bone = normalizePunchZone(zone) === 'head' ? skeleton.bones.head : skeleton.bones.chest;
  if (!bone) return null;
  bone.updateWorldMatrix(true, false);
  return bone.getWorldPosition(new Vector3());
}
