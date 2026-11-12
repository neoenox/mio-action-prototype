using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        void TickChase(Vector3 toTarget, float distance)
        {
            float ring = DesiredRing();

            // 攻撃権を持っていても、手前の仲間に塞がれて間合いまで入れないなら返す。
            // 持ったまま 1.7m で止まると、殴れる敵がいなくなる。
            if (distance > ring && Group != null && HasAttackToken && Group.IsBlockedByNearer(this))
            {
                ReleaseToken();
                EnterStrafe();
                return;
            }

            if (distance > ring)
            {
                // 遠いときだけ歩く。仲間と重ならないよう少し横へ逃げる。
                Vector3 dir = toTarget / Mathf.Max(0.001f, distance) + Separation();
                Move(dir.normalized);
                return;
            }

            // 間合いに入った。殴れるなら殴り、順番待ちなら回り込む。
            if (!TryBeginAttack(distance))
                EnterStrafe();
        }

        /// <summary>
        /// この敵が今いるべき距離。殴る番（トークン持ち）だけが間合いまで入り、
        /// 順番待ちは輪の外側で待つ。全員が同じ距離まで詰めると三人称カメラの画が敵で埋まる。
        /// </summary>
        float DesiredRing()
        {
            // グループが無ければ1体で戦うので、間合いまで普通に詰める
            if (HasAttackToken || Group == null)
                return EngageDistance;

            return EngageDistance * Mathf.Max(1.1f, WaitingRingScale) * _ringJitter;
        }

        /// <summary>仲間と近すぎるときに、横へ逃げる力。密集して1点に固まるのを防ぐ。</summary>
        Vector3 Separation()
        {
            if (Group == null)
                return Vector3.zero;

            const float radius = 1.7f;
            Vector3 push = Vector3.zero;

            var members = Group.Members;
            for (int i = 0; i < members.Count; i++)
            {
                var other = members[i];
                if (other == null || other == this)
                    continue;

                Vector3 d = transform.position - other.transform.position;
                d.y = 0f;
                float len = d.magnitude;
                if (len > 0.001f && len < radius)
                    push += d / len * (1f - len / radius);
            }

            return push * 0.9f;
        }

        // --- フェーズ: Strafe ----------------------------------------------------

        void TickStrafe(float dt, Vector3 toTarget, float distance)
        {
            _timer -= dt;

            // 間合いの中なら、トークンを取って殴る。取れなければ待つ。
            if (distance <= EngageDistance * 1.02f && TryBeginAttack(distance))
                return;

            // 順番が回ってきたら、殴る前にトークンを先取りして間合いまで歩く。
            // 近づいてよいのはトークンを持つ1体だけになるので、待機組が画面に詰め寄らない。
            if (!HasAttackToken && Time.time >= _attackReadyAt
                && (Group == null || Group.TryAcquireAttackToken(this)))
            {
                Phase = EnemyPhase.Chase;
                return;
            }

            // 間合いを維持するための前後補正。近すぎれば離れ、遠ければ詰める。
            float desired = DesiredRing();
            if (HasAttackToken && _strafeCloseIn)
                desired *= StrafeCloseInScale;

            // 引き離されすぎたら追い直す
            if (distance > desired * 1.45f)
            {
                Phase = EnemyPhase.Chase;
                return;
            }

            Vector3 radial = distance > 0.001f ? toTarget / distance : transform.forward;
            // 1 ラインでは回り込まず、前後の間合いだけ調整する。
            float error = distance - desired;
            Vector3 correction = radial * Mathf.Clamp(error * 2.5f, -1f, 1f);

            Vector3 direction = correction + Separation();
            if (Mathf.Abs(direction.x) > 0.06f)
                Move(direction.normalized, MoveSpeed * StrafeSpeedScale);

            if (_timer <= 0f)
                EnterStrafe();
        }

        void EnterStrafe()
        {
            Phase = EnemyPhase.Strafe;

            _timer = Mathf.Max(0.1f, StrafeSeconds) * UnityEngine.Random.Range(0.75f, 1.25f);
            _strafeCloseIn = UnityEngine.Random.value < StrafeCloseInChance;
        }

        // --- フェーズ: Windup / Attack / Recover ---------------------------------

    }
}
