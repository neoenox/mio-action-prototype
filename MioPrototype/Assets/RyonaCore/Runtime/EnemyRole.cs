using System;
using System.Collections.Generic;

namespace Ryona
{
    /// <summary>敵の役割。背丈で決定的に割り当てる。</summary>
    public enum EnemyRole
    {
        Grappler,   // 一番背が高い: 押さえつけ・ベアハッグ・壁拘束・連行
        Tools,      // 中間: 手首ロック・膝裏蹴り
        Striker     // 一番低い: ジャブ・強打が速い
    }

    /// <summary>役割割り当て用の最小データ（Core はゲーム層を参照しない）。</summary>
    public readonly struct RoleCandidate
    {
        public readonly string Name;
        public readonly float Height;
        public readonly bool AuthoredGrappler;

        public RoleCandidate(string name, float height, bool authoredGrappler = false)
        {
            Name = name ?? string.Empty;
            Height = height;
            AuthoredGrappler = authoredGrappler;
        }
    }

    /// <summary>
    /// 背丈（同点は名前）で役割を決める。シーンで掴み役が明示されていればそれを優先する。
    /// 列挙順には依存しない。
    /// </summary>
    public static class EnemyRoleRules
    {
        public const float HeightEpsilon = 0.0001f;

        /// <summary><paramref name="index"/> の敵の役割。候補が無ければ Striker。</summary>
        public static EnemyRole RoleOf(IReadOnlyList<RoleCandidate> candidates, int index)
        {
            if (candidates == null || index < 0 || index >= candidates.Count)
                return EnemyRole.Striker;

            int authored = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].AuthoredGrappler)
                {
                    authored = i;
                    break;
                }
            }
            if (authored >= 0)
                return index == authored ? EnemyRole.Grappler : EnemyRole.Striker;

            var order = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                float diff = candidates[b].Height - candidates[a].Height;
                if (Math.Abs(diff) > HeightEpsilon)
                    return diff > 0f ? 1 : -1;
                return string.CompareOrdinal(candidates[a].Name, candidates[b].Name);
            });

            int rank = order.IndexOf(index);
            if (rank == 0)
                return EnemyRole.Grappler;
            if (rank == 1 && candidates.Count == 3)
                return EnemyRole.Tools;
            return EnemyRole.Striker;
        }
    }
}
