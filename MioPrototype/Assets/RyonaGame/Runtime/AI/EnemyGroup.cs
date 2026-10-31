using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 複数の敵が一斉に殴ってこないようにする調停役。
    /// 仕様書 SPEC.md §5 のフェアネス規定「同時攻撃は最大 2 体まで」を、
    /// 攻撃トークンの枚数（<see cref="MaxAttackers"/>）で実装する。
    /// <para>
    /// 使い方: シーンに 1 つ置き、各 <see cref="EnemyBrain"/> の <c>Group</c> に割り当てる
    /// （未割り当てでも <see cref="EnemyBrain"/> が自動で探す）。
    /// グループが存在しない場合、敵はトークンなしで単体行動する。
    /// </para>
    /// </summary>
    [AddComponentMenu("Ryona/Enemy Group")]
    public sealed class EnemyGroup : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("同時に予備動作〜攻撃に入れる敵の数。SPEC §5 の上限は 2")]
        [Min(1)]
        public int MaxAttackers = 2;

        readonly List<EnemyBrain> _members = new List<EnemyBrain>();
        readonly List<EnemyBrain> _attackers = new List<EnemyBrain>();

        /// <summary>登録されている（破棄されていない）敵の数。</summary>
        public int MemberCount
        {
            get
            {
                Prune();
                return _members.Count;
            }
        }

        /// <summary>現在攻撃トークンを持っている敵の数。</summary>
        public int AttackerCount
        {
            get
            {
                Prune();
                return _attackers.Count;
            }
        }

        /// <summary>現在攻撃トークンを持っている敵。読み取り専用。</summary>
        public IReadOnlyList<EnemyBrain> Attackers => _attackers;

        /// <summary>登録されている敵。読み取り専用（間合いの重なり回避に使う）。</summary>
        public IReadOnlyList<EnemyBrain> Members
        {
            get
            {
                Prune();
                return _members;
            }
        }

        /// <summary>登録順の番号。待機位置を左右に振り分けるのに使う。</summary>
        public int IndexOf(EnemyBrain brain)
        {
            Prune();
            return _members.IndexOf(brain);
        }

        /// <summary>攻撃トークンを取得できる空きがあるか。</summary>
        public bool HasFreeSlot
        {
            get
            {
                Prune();
                return _attackers.Count < Mathf.Max(1, MaxAttackers);
            }
        }

        /// <summary>敵を登録する。<see cref="EnemyBrain"/> の Start から呼ばれる。</summary>
        public void Register(EnemyBrain brain)
        {
            if (brain == null)
                return;

            Prune();

            if (!_members.Contains(brain))
                _members.Add(brain);
        }

        /// <summary>敵の登録を解除し、持っていれば攻撃トークンも返す。</summary>
        public void Unregister(EnemyBrain brain)
        {
            if (brain != null)
            {
                _members.Remove(brain);
                _attackers.Remove(brain);
            }

            // 破棄済み（Unity の null）もここで落とす
            Prune();
        }

        /// <summary>
        /// 攻撃トークンを取る。既に持っていれば true、空きが無ければ false。
        /// false を返した敵は待機中も回り込み続けること（棒立ちにしない）。
        /// </summary>
        public bool TryAcquireAttackToken(EnemyBrain brain)
        {
            if (brain == null)
                return false;

            Prune();

            if (_attackers.Contains(brain))
                return true;

            // 同じ側にいる手前の敵を追い越して攻撃権を取らない。
            // 一列では奥の敵が権利を取ると、手前の敵に塞がれて進行が止まる。
            if (IsBlockedByNearer(brain))
                return false;

            // MaxAttackers を実行中に減らしても、既に持っている敵の権利は取り上げない。
            // 新規の取得だけを止めるので、次の解放から新しい上限が効く。
            if (_attackers.Count >= Mathf.Max(1, MaxAttackers))
                return false;

            _attackers.Add(brain);
            return true;
        }

        /// <summary>
        /// 同じ側のより手前に仲間がいて、前へ出られないか。
        /// 掴み中の仲間も数える（掴みを解いても彼女の前に残り、奥の敵を塞ぎ続ける）。
        /// </summary>
        public bool IsBlockedByNearer(EnemyBrain brain)
        {
            if (brain == null || brain.Target == null)
                return false;

            float own = brain.transform.position.x - brain.Target.position.x;
            foreach (var other in _members)
            {
                if (other == null || other == brain || !other.IsAlive || other.IsFlanking)
                    continue;
                float offset = other.transform.position.x - brain.Target.position.x;
                if (own * offset > 0f && Mathf.Abs(offset) < Mathf.Abs(own) - 0.05f)
                    return true;
            }

            return false;
        }

        /// <summary>攻撃トークンを返す。持っていなくても安全に呼べる。</summary>
        public void ReleaseAttackToken(EnemyBrain brain)
        {
            if (brain == null)
                return;

            _attackers.Remove(brain);
        }

        /// <summary>この敵が攻撃トークンを持っているか。</summary>
        public bool HasAttackToken(EnemyBrain brain)
        {
            return brain != null && _attackers.Contains(brain);
        }

        /// <summary>すべての攻撃トークンを返す。シーン切り替え時の保険。</summary>
        public void ReleaseAllTokens()
        {
            _attackers.Clear();
        }

        void OnDisable()
        {
            // 無効化中にトークンを持ち越すと、再有効化後に誰も殴れなくなる
            _attackers.Clear();
        }

        /// <summary>破棄された敵をリストから落とす。</summary>
        void Prune()
        {
            for (int i = _attackers.Count - 1; i >= 0; i--)
            {
                if (_attackers[i] == null)
                    _attackers.RemoveAt(i);
            }

            for (int i = _members.Count - 1; i >= 0; i--)
            {
                if (_members[i] == null)
                    _members.RemoveAt(i);
            }
        }
    }
}
