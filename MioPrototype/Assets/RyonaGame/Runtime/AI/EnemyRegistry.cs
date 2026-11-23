using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 有効な <see cref="EnemyBrain"/> の登録簿。Unity の FindObjectsByType の代わりに使う。
    /// 登録順（おおむね生成順）を保つので、FindObjectsSortMode.None と同じ並びで読める。
    /// 無効化・破棄で登録を外すため、FindObjectsInactive.Exclude と同じ意味になる。
    /// </summary>
    public static class EnemyRegistry
    {
        static readonly List<EnemyBrain> s_all = new List<EnemyBrain>();
        static EnemyBrain[] s_snapshot = new EnemyBrain[0];
        static bool s_dirty = true;

        /// <summary>
        /// 現在有効な敵のスナップショット。列挙中に敵が倒れてもコレクションは壊れない。
        /// 変更時だけ配列を作り直すので、FindObjectsByType より割り当てが少ない。
        /// </summary>
        public static IReadOnlyList<EnemyBrain> All
        {
            get
            {
                Prune();
                if (s_dirty)
                {
                    s_snapshot = s_all.ToArray();
                    s_dirty = false;
                }
                return s_snapshot;
            }
        }

        /// <summary>登録順の先頭。FindFirstObjectByType の置き換え。</summary>
        public static EnemyBrain First
        {
            get { Prune(); return s_all.Count > 0 ? s_all[0] : null; }
        }

        public static int Count
        {
            get { Prune(); return s_all.Count; }
        }

        /// <summary>配列が要る呼び出し側向け。All とは別のコピーを返す。</summary>
        public static EnemyBrain[] Snapshot()
        {
            Prune();
            return s_all.ToArray();
        }

        public static void Register(EnemyBrain brain)
        {
            if (brain == null)
                return;
            Prune();
            if (!s_all.Contains(brain))
            {
                s_all.Add(brain);
                s_dirty = true;
            }
        }

        public static void Unregister(EnemyBrain brain)
        {
            if (brain == null)
                return;
            if (s_all.Remove(brain))
                s_dirty = true;
            Prune();
        }

        /// <summary>テストやシーン再読込で静的状態を捨てるときに使う。</summary>
        public static void Clear()
        {
            s_all.Clear();
            s_snapshot = new EnemyBrain[0];
            s_dirty = false;
        }

        static void Prune()
        {
            for (int i = s_all.Count - 1; i >= 0; i--)
            {
                if (s_all[i] == null)
                {
                    s_all.RemoveAt(i);
                    s_dirty = true;
                }
            }
        }
    }
}
