using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        /// <summary>
        /// 罰なしで解放する（相手が倒れた・プレイヤーが倒れた・腹パン演出が終わった）。
        /// </summary>
        public void Release()
        {
            EndWallSplit();
            EndWallPin();
            bool wasGrappled = IsGrappled;
            ClearPinHandOff();

            if (IsHandingOff)
            {
                if (Captor != null)
                    Captor.EndCatch();
                IsHandingOff = false;
                EndShove();
            }

            IsGrappled = false;

            // 解除時刻を残す（もがきの連打が攻撃として暴発しないように。Issue #55）。
            // ここを通る解除経路は、時間切れ（Fail）／壁の投げ捨てと段階遷移の打ち切り
            // （TickWallToss・TickWallTransition）／引き渡しの中断（CanContinueHandOff）／
            // 押さえつけの受け渡し中止・掴んでいた相手の死亡／彼女の死亡／プレビュー用のリセット。
            // 掴まれていないときに呼ばれることもある（ResetForMotionPreview）ので、
            // 実際に解けたときだけ時刻を進める（解除済みの判定は wasGrappled と同じ）。
            if (wasGrappled)
                ReleasedAt = Time.time;

            HaulHandoff = false;
            HaulHandoffSource = false;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            _grabContactRemaining = 0f;
            _session.LimitRemaining = 0f;
            _session.SuppressRemaining = 0f;
            _session.Captor = null;
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;
            KneeCount = 0;
            PullPulse = 0f;

            if (wasGrappled)
                Released?.Invoke();
        }

        /// <summary>
        /// 掴まれた瞬間の引き寄せ。相手の体の前に、掴み方に応じた距離まで詰める。
        /// これをしないと、殴り合い用の間合い（1.4m）のまま腕だけ伸びて手が届かない。
        /// </summary>
        void SnapToCaptor(EnemyBrain captor, GrappleKind kind)
        {
            if (captor == null)
                return;

            // 壁押さえつけは「捕まえた相手の前」ではなく「壁の前」に立つ。
            // 向きも -Z（室内側）で固定する。レーンへ戻すときに PlayerMotor が ±X へ直す。
            if (IsWallPin)
            {
                transform.position = _wallPinSpot;
                transform.rotation = _wallPinRotation;
                return;
            }

            Vector3 spot = GrabSpot(captor, kind);
            transform.position = spot;

            // 羽交い締め・頭上拘束は背中を向けたまま掴まれる（向きは変えない）。
            // 押さえつけ / ベアハッグは正面から掴まれるので、相手の方を向かせる。
            if (kind != GrappleKind.Back && kind != GrappleKind.AnkleGrab)
            {
                Vector3 look = captor.transform.position - spot;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// 掴まれる位置（相手から見て、掴み方に応じた距離）。
        /// 突き飛ばしと瞬間的な寄せの両方で同じ計算を使う。
        /// </summary>
        Vector3 GrabSpot(EnemyBrain captor, GrappleKind kind)
        {
            Vector3 dir = transform.position - captor.transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = -captor.transform.forward;
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 1e-4f)
                return transform.position;

            dir.Normalize();

            // 羽交い締めは密着させる。押さえつけは腕を伸ばす余地を残し、
            // ベアハッグは胴を抱えるので少しだけ近い。
            float distance = kind switch
            {
                // 頭上拘束も背後から密着して掴む（腕を上げる余地を残し、羽交い締めより少し離す）。
                GrappleKind.Back => CombatConstants.BackGrabStandoff,
                GrappleKind.BearHug => CombatConstants.BearHugStandoff,
                GrappleKind.AnkleGrab => _grappleSettings.AnkleStandoff,
                _ => CombatConstants.GrabStandoff
            };
            Vector3 spot = captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            return spot;
        }

        internal void ResetForMotionPreview()
        {
            Release();
            ClearWallExit();
            WallPinWeight = 0f;
            WallAdhereWeight = 0f;
            KneePulse = 0f;
            KneeCount = 0;
            SqueezePulse = 0f;
            PullPulse = 0f;
            LiftPulse = 0f;
            PushPulse = 0f;
            StrugglePulse = 0f;
        }

    }
}
