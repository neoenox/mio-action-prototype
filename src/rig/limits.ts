/**
 * 関節可動域（Joint ROM）の判定レイヤー。
 *
 * 判定そのものは core-port の `src/core/JointRom.ts`（Unity `JointRom.cs` の 1:1 移植）を
 * そのまま再利用する。rig 側はここから再エクスポートし、計測結果を集計する
 * `formatRomLog` / `isWithin` だけを追加する。
 */
export {
  HumanJointLimits,
  JointId,
  JointLimit,
  JointRom,
  JointRomVerdict,
  JointRomReading,
  JointRomTally,
  JointSide,
} from '../core/JointRom.js';

import { JointId, JointRom, JointRomVerdict, JointRomReading, JointSide } from '../core/JointRom.js';

/** 1 フレームぶんの計測まとめ。 */
export interface RomSample {
  readonly label: string;
  readonly readings: readonly JointRomReading[];
}

/**
 * 計測結果から逸脱だけを取り出して LOG 用文字列にする。
 * Unity の `JointRomMonitor.LogSummary` と同じ書式。
 */
export function formatRomLog(label: string, readings: readonly JointRomReading[]): string | null {
  const bad = readings.filter((r) => r.isViolation);
  if (bad.length === 0) return null;
  const worst = bad.reduce((a, b) => (b.excess > a.excess ? b : a));
  const detail = bad
    .map((r) =>
      `${JointRom.sideName(r.side)}${r.limit.label} ${Math.round(r.degrees)}°` +
      `（可動域 ${r.limit.rangeText}・${JointRom.verdictName(r.verdict)}）`,
    )
    .join(' / ');
  return `[JointROM] ${label}: 逸脱 ${bad.length} 件。${detail}。最悪 ${Math.round(worst.degrees)}° ${JointRom.verdictName(worst.verdict)}`;
}

/** 指定関節の角度が「人体の可動域＋余裕」に収まっているかだけを返す便利関数。 */
export function isWithin(joint: JointId, side: JointSide, degrees: number): boolean {
  return JointRom.read(joint, side, degrees).verdict === JointRomVerdict.Within;
}
