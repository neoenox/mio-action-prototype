using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        void BeginHold(EnemyBrain captor, GrappleKind kind, bool blendContact)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            // 前の壁押さえつけから戻っている途中なら、ここでレーンへ戻しきる。
            if (!IsWallPin && _wallExitRemaining > 0f)
                ClearWallExit();

            _session.Captor = captor;

            if (blendContact)
            {
                _grabContactFrom = transform.position;
                _grabContactRotation = transform.rotation;
                _grabContactRemaining = GrabContactSeconds;
            }
            else
                SnapToCaptor(captor, kind);

            // 腕が壊れているほど逃げにくい（仕様書 §4.3）
            float armInjury = Stats.State.Body[BodyPart.Arms];
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, armInjury);

            _resistance = new GrappleResistance(kind, RequiredInputs);
            _session.Kind = kind;
            _session.LimitRemaining = HoldDuration(kind);
            _session.HeldSeconds = 0f;
            _session.SuppressRemaining = 0f;
            IsGrappled = true;
            StrugglePulse = 0f;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            KneePulse = 0f;
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
            Debug.Log($"[Grapple] begin kind={kind} need={RequiredInputs:F1} " +
                      $"limit={limit} arm={armInjury:F0} captor={(captor != null ? captor.name : "none")}");

            Started?.Invoke(kind);
        }

    }
}
