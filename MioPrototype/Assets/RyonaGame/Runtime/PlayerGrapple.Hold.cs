using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        void BeginHold(EnemyBrain captor, GrappleKind kind, bool blendContact, float contactSeconds = GrabContactSeconds)
        {
            var wrist = GetComponent<PlayerWristLock>();
            if (wrist != null && !wrist.IsLocked && wrist.InGrace)
                return;
            bool wristPenalty = wrist != null && wrist.IsLocked;
            if (wristPenalty)
                wrist.DetachOnGrab();
            else if (wrist != null && wrist.HasGrabPenalty)
                wristPenalty = true;

            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            HaulHandoff = false;

            // 前の壁押さえつけから戻っている途中なら、ここでレーンへ戻しきる。
            if (!IsWallPin && _wallExitRemaining > 0f)
                ClearWallExit();

            _session.Captor = captor;

            if (blendContact)
            {
                _grabContactFrom = transform.position;
                _grabContactRotation = transform.rotation;
                _grabContactTotal = contactSeconds;
                _grabContactRemaining = contactSeconds;
            }
            else
                SnapToCaptor(captor, kind);

            // 腕が壊れているほど逃げにくい（仕様書 §4.3）
            float armInjury = Stats.State.Body[BodyPart.Arms];
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, armInjury);
            if (wristPenalty && wrist != null)
                RequiredInputs = RequiredInputs * wrist.EscapeNeedScale;

            _resistance = new GrappleResistance(kind, RequiredInputs);
            _session.Kind = kind;
            _session.LimitRemaining = HoldDuration(kind);
            _session.HeldSeconds = 0f;
            _session.SuppressRemaining = 0f;
            IsGrappled = true;
            // 掴まれた瞬間は「解除」ではないので ReleasedAt は更新しない
            // （更新すると、掴まれた直後の攻撃入力に逆にロックがかかる）。
            StrugglePulse = 0f;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            KneePulse = 0f;
            KneeCount = 0;
            if (kind == GrappleKind.BearHug)
            {
                _hugStage = HugStage.Rest;
                _hugTime = 0f;
                _hugPhase = 0f;
                SqueezePulse = 0f;
                PullPulse = 0f;
            }

            // 掴まれている間は移動できない
            Stats.IsMoving = false;

            PlaySfx(SfxId.Impact, 0.9f);

            string limit = HasTimeLimit ? $"{_session.LimitRemaining:F2}s" : "none";
            GameLog.Log($"[Grapple] begin kind={kind} need={RequiredInputs:F1} " +
                      $"limit={limit} arm={armInjury:F0} captor={(captor != null ? captor.name : "none")}");

            Started?.Invoke(kind);
        }

    }
}
