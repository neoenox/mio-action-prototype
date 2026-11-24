/** 敵の役割。背丈で決定的に割り当てる。 */
export enum EnemyRole {
  /** 一番背が高い: 押さえつけ・ベアハッグ・壁拘束・連行 */
  Grappler = 0,
  /** 中間: 手首ロック・膝裏蹴り */
  Tools = 1,
  /** 一番低い: ジャブ・強打が速い */
  Striker = 2,
}

/** 役割割り当て用の最小データ（Core はゲーム層を参照しない）。 */
export class RoleCandidate {
  readonly name: string;
  readonly height: number;
  readonly authoredGrappler: boolean;

  constructor(name: string | null | undefined, height: number, authoredGrappler = false) {
    this.name = name ?? '';
    this.height = height;
    this.authoredGrappler = authoredGrappler;
    Object.freeze(this);
  }
}

/** 背丈（同点は名前）で役割を決める。列挙順には依存しない。 */
export const EnemyRoleRules = {
  HeightEpsilon: 0.0001,

  /** index の敵の役割。候補が無ければ Striker。 */
  roleOf(candidates: readonly RoleCandidate[] | null | undefined, index: number): EnemyRole {
    if (candidates === null || candidates === undefined || index < 0 || index >= candidates.length) {
      return EnemyRole.Striker;
    }

    let authored = -1;
    for (let i = 0; i < candidates.length; i++) {
      if (!candidates[i]!.authoredGrappler) continue;
      // 複数いる場合は身長→名前で決め、列挙順に依存させない。
      if (
        authored < 0 ||
        candidates[i]!.height > candidates[authored]!.height + EnemyRoleRules.HeightEpsilon ||
        (Math.abs(candidates[i]!.height - candidates[authored]!.height) <= EnemyRoleRules.HeightEpsilon &&
          candidates[i]!.name < candidates[authored]!.name)
      ) {
        authored = i;
      }
    }
    if (authored >= 0) {
      return index === authored ? EnemyRole.Grappler : EnemyRole.Striker;
    }

    const order: number[] = [];
    for (let i = 0; i < candidates.length; i++) order.push(i);
    order.sort((a, b) => {
      const diff = candidates[b]!.height - candidates[a]!.height;
      if (Math.abs(diff) > EnemyRoleRules.HeightEpsilon) return diff > 0 ? 1 : -1;
      // 序数比較（JS の < / > は C# の CompareOrdinal と同じ順序）。
      if (candidates[a]!.name < candidates[b]!.name) return -1;
      if (candidates[a]!.name > candidates[b]!.name) return 1;
      return 0;
    });

    const rank = order.indexOf(index);
    if (rank === 0) return EnemyRole.Grappler;
    if (rank === 1 && candidates.length === 3) return EnemyRole.Tools;
    return EnemyRole.Striker;
  },
} as const;
