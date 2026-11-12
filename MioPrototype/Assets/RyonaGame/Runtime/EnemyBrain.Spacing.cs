using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        void Move(Vector3 direction)
        {
            Move(direction, MoveSpeed);
        }

        void Move(Vector3 direction, float speed)
        {
            // 脚の損傷で移動速度が落ちる（CombatState.MoveSpeedMultiplier）
            direction.z = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                direction.Normalize();
            float rate = speed * State.MoveSpeedMultiplier;
            if (rate <= 0f || direction.sqrMagnitude <= 0.0001f)
                return;

            transform.position += direction * (rate * Time.deltaTime);
        }

        /// <summary>
        /// 体の間隔を毎フレーム保つ。移動していないとき（予備動作中など）に
        /// 彼女が歩いて重なってくるのを防ぐ。
        /// </summary>
        void KeepSpacing()
        {
            if (Phase == EnemyPhase.Dead || IsHolding || IsHandOffLocked)
                return;

            // タッグ腹パンは演出側（HeldBellyPunchDirector）が当たる位置を決めている。
            // ここで仲間と押し分けると、置いた位置から横へずれて拳が届かなくなる
            // （実測: 0.33m ずれて gap 0.33 の空振りが続いた）。
            // 顔パンは _bellyPunchPendingHold が 0 なので、発注から当たるまでの
            // _tagPunchPending でも除外する（でないと顔パンだけ位置がずらされて外れる）。
            if (_bellyPunchPendingHold > 0f || _punchPoseHoldSeconds > 0f || _tagPunchPending ||
                IsPreparingGrab || HeldThrowPull > 0f)
                return;

            KeepOutOfPlayer();
            KeepOutOfOthers();
        }

        /// <summary>
        /// 彼女にめり込まないための下限。近づきすぎたら敵の側で距離を戻す。
        /// 真上に重なったときは後ろへ逃がす（0 除算を避ける）。
        /// </summary>
        void KeepOutOfPlayer()
        {
            if (Target == null || MinPlayerGap <= 0f)
                return;

            // 掴んでいる間は詰めた距離を保つ。
            // ここで 0.95 まで戻すと、伸ばした腕が彼女に届かず手が宙に浮く。
            if (IsHolding)
                return;

            Vector3 d = transform.position - Target.position;
            d.y = 0f;
            float len = d.magnitude;

            if (len >= MinPlayerGap)
                return;

            Vector3 dir = len > 0.001f ? d / len : -transform.forward;
            Vector3 wanted = Target.position + dir * MinPlayerGap;

            // 1 フレームで飛ばさず詰めて戻す。
            // 掴みを解いた瞬間は 0.60 → 0.95 を戻すので、硬く代入すると 35cm ワープする。
            transform.position = Vector3.MoveTowards(
                transform.position, wanted, 3f * Time.deltaTime);
        }

        /// <summary>
        /// 仲間と重ならないための下限。soft な <see cref="Separation"/> だけだと
        /// 二人羽織のように重なるので、硬い下限も置く（半分ずつ押し分ける）。
        /// </summary>
        void KeepOutOfOthers()
        {
            if (Group == null)
                return;

            const float min = 0.82f;
            var members = Group.Members;

            for (int i = 0; i < members.Count; i++)
            {
                var other = members[i];
                if (other == null || other == this || other.Phase == EnemyPhase.Dead || other.IsFlanking)
                    continue;

                Vector3 d = transform.position - other.transform.position;
                d.y = 0f;
                float len = d.magnitude;

                if (len > 0.001f && len < min)
                    transform.position = other.transform.position + d / len * (min * 0.5f + len * 0.5f);
            }
        }

        void Face(Vector3 forward, float dt)
        {
            Quaternion look = Quaternion.LookRotation(forward, Vector3.up);
            float t = Mathf.Clamp01(Mathf.Max(0f, TurnSpeed) * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, t);
        }

        /// <summary>
        /// ベルト（1 ライン）の左右へ向きを揃える。攻撃の予備動作に入るときに呼ぶ。
        /// 彼女はレーン上に固定されているので、向きは ±X のどちらかへ倒せばよい。
        /// ここで揃えないと、彼女が目の前を通り過ぎた直後（旋回の途中）に殴り始めたとき、
        /// 最大 26°（<see cref="TryBeginAttack"/> の 0.9 判定）斜めを向いたまま振り抜く。
        /// </summary>
        void SnapToLaneFacing()
        {
            if (Player == null)
                return;

            float dx = Player.transform.position.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.001f)
                return;

            transform.rotation = Quaternion.LookRotation(Vector3.right * Mathf.Sign(dx), Vector3.up);
        }

        // --- テレグラフ ----------------------------------------------------------

    }
}
