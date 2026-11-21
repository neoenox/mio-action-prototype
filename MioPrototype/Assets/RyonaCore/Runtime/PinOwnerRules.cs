using System;
using System.Collections.Generic;

namespace Ryona
{
    /// <summary>
    /// 押さえつけ担当の候補。Core がゲーム層の型を参照しないための最小データ。
    /// </summary>
    public readonly struct PinCandidate
    {
        /// <summary>シーン上の名前。同点の決定にだけ使う。</summary>
        public readonly string Name;

        /// <summary>背丈（m）。</summary>
        public readonly float Height;

        /// <summary>シーンで押さえつけ担当として指定されているか。</summary>
        public readonly bool CanPin;

        public PinCandidate(string name, float height, bool canPin)
        {
            Name = name ?? string.Empty;
            Height = height;
            CanPin = canPin;
        }
    }

    /// <summary>
    /// 押さえつけ担当を 1 体だけ決める。列挙順に依存しない。
    /// <list type="number">
    /// <item><see cref="PinCandidate.CanPin"/> が立つ個体があれば、その中から選ぶ</item>
    /// <item>背丈が大きい個体を優先する（巨躯の代役）</item>
    /// <item>背丈が同点なら名前の序数順で決める（同じシーンなら常に同じ結果）</item>
    /// </list>
    /// </summary>
    public static class PinOwnerRules
    {
        /// <summary>背丈がこの差以内なら同点として名前で決める。</summary>
        public const float HeightEpsilon = 0.0001f;

        /// <summary>
        /// 候補から担当を 1 体選ぶ。
        /// </summary>
        /// <returns>選ばれた候補の添字。候補が無ければ -1。</returns>
        public static int Pick(IReadOnlyList<PinCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return -1;

            bool hasAuthored = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].CanPin)
                {
                    hasAuthored = true;
                    break;
                }
            }

            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                PinCandidate candidate = candidates[i];
                if (hasAuthored && !candidate.CanPin)
                    continue;

                if (best < 0)
                {
                    best = i;
                    continue;
                }

                PinCandidate current = candidates[best];
                float diff = candidate.Height - current.Height;

                bool taller = diff > HeightEpsilon;
                bool sameHeightButEarlierName =
                    Math.Abs(diff) <= HeightEpsilon &&
                    string.CompareOrdinal(candidate.Name, current.Name) < 0;

                if (taller || sameHeightButEarlierName)
                    best = i;
            }

            return best;
        }
    }
}
