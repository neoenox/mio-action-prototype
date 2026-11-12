using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        /// <summary>
        /// 拘束を別の敵へ引き渡す（たらい回し）。掴まれたまま相手だけが入れ替わる。
        /// もがきの進捗と制限時間はやり直し（抜けても次の敵に捕まる）。
        /// </summary>
        public void HandOff(EnemyBrain next, GrappleKind kind = GrappleKind.Back, bool shove = false)
        {
            if (!IsGrappled || IsHandingOff || next == null || !next.IsAlive ||
                next == Captor || Stats == null || Stats.IsDead)
                return;

            EndWallPin();

            var previous = Captor;
            if (shove && (previous == null || !previous.IsAlive))
                return;

            _session.Captor = next;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            _grabContactRemaining = 0f;

            if (shove)
            {
                // 接触待ち・二歩・受け止めの間は両者を固定する。
                const float lockSeconds = 1.30f;
                IsHandingOff = true;
                _shoveSender = previous;
                previous.LockForHandOff(lockSeconds);
                previous.PushAway();
                next.LockForHandOff(lockSeconds);
                StartCoroutine(Shove(next, kind));
                return;
            }

            // 押し出しの経路は戻り補間へ任せる（壁から剥がれながら受け手の前へ運ばれる）。
            // 即座に寄せる経路は SnapToCaptor が位置を決めるので、戻り補間が残ると
            // 置いた位置から壁際へ引き戻される。ここで打ち切る。
            ClearWallExit();

            SnapToCaptor(next, kind);
            ApplyHandOffHold(kind);
            Started?.Invoke(kind);
        }

        EnemyBrain _pinReceiver;
        float _pinReceiverGrip;

        internal bool IsPreparingPinHandOff => _pinReceiver != null;

        internal float PinHandOffGrip(EnemyBrain enemy) => _pinReceiver == enemy ? _pinReceiverGrip : 0f;

        internal bool TryStartPinHandOff(EnemyBrain receiver)
        {
            if (_pinReceiver != null || IsHandingOff || Captor == null)
                return false;

            _pinReceiver = receiver;
            receiver.LockForHandOff(1.2f);
            StartCoroutine(ReceiveFromPin(Captor, receiver));
            return true;
        }

        System.Collections.IEnumerator ReceiveFromPin(EnemyBrain sender, EnemyBrain receiver)
        {
            // 受け手が後ろから腕を取るまで、正面の押さえつけともがき入力を維持する。
            float elapsed = 0f;
            float grip = 0f;
            while (_pinReceiver == receiver && IsGrappled && Kind == GrappleKind.Pin &&
                   Captor == sender && sender != null && sender.isActiveAndEnabled && sender.IsAlive && sender.IsPinning &&
                   receiver != null && receiver.isActiveAndEnabled && receiver.IsAlive &&
                   receiver.Phase != EnemyPhase.Stagger && receiver.IsHandOffLocked &&
                   Stats != null && !Stats.IsDead && !Stats.IsDown && !Stats.HasHeldBellyDown && elapsed < 1.05f)
            {
                Vector3 forward = transform.forward;
                Vector3 spot = transform.position - forward * CombatConstants.BackGrabStandoff;
                spot.y = receiver.transform.position.y;
                receiver.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                receiver.transform.position = Vector3.MoveTowards(receiver.transform.position, spot,
                    receiver.MoveSpeed * receiver.State.MoveSpeedMultiplier * Time.deltaTime);

                if (elapsed >= KneeHoldSeconds && (receiver.transform.position - spot).sqrMagnitude < 0.0025f)
                {
                    grip += Time.deltaTime;
                    _pinReceiverGrip = Mathf.SmoothStep(0f, 1f, grip / 0.16f);
                    if (grip >= 0.23f)
                    {
                        // 両側から腕を取る短い間を置き、受け手だけの拘束へ切り替える。
                        receiver.transform.position = spot;
                        ClearPinHandOff();
                        _session.Captor = receiver;
                        ApplyHandOffHold(GrappleKind.Back);
                        _session.HeldSeconds = 0f;
                        FrontEscapeEffort = 0f;
                        _frontEffortHold = 0f;
                        _grabContactRemaining = 0f;
                        receiver.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
                        sender.LockForHandOff(0.24f);
                        Debug.Log($"[HandOff] pin → back {sender.name} → {receiver.name}");
                        Started?.Invoke(Kind);
                        yield break;
                    }
                }

                yield return null;
                elapsed += Time.deltaTime;
            }

            if (_pinReceiver == receiver)
                ClearPinHandOff();
        }

        void ClearPinHandOff()
        {
            _pinReceiver = null;
            _pinReceiverGrip = 0f;
        }

        void ApplyHandOffHold(GrappleKind kind)
        {
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, Stats.State.Body[BodyPart.Arms]);
            _resistance = new GrappleResistance(kind, RequiredInputs);
            _session.Kind = kind;
            _session.LimitRemaining = HoldDuration(kind);
            _session.SuppressRemaining = 0f;
        }

        static float HoldDuration(GrappleKind kind) => kind switch
        {
            GrappleKind.Back => float.PositiveInfinity,
            GrappleKind.BearHug => CombatConstants.BearHugLimitSeconds,
            _ => CombatConstants.GrappleLimitSeconds
        };

        /// <summary>
        /// 掴み位置まで一続きに移動する。突き飛ばしの距離と掴み位置を別々に扱うと、
        /// 行き過ぎた後に戻る動きが「ワープ」に見えるので、最初から終点へ運ぶ。
        /// </summary>
        System.Collections.IEnumerator Shove(EnemyBrain next, GrappleKind kind)
        {
            Vector3 fromPos = transform.position;
            Vector3 toPos = GrabSpot(next, kind);
            Vector3 toNext = next.transform.position - toPos;
            toNext.y = 0f;
            // どちらの拘束でも、受け手に正面から当たってから掴みに移る。
            Vector3 towardReceiver = toNext.normalized;
            bool behindHold = kind == GrappleKind.Back;
            Vector3 catchPos = toPos + towardReceiver * (behindHold ? -0.015f : 0.24f);
            Vector3 lookDir = behindHold ? -toNext : toNext;

            Quaternion fromRot = transform.rotation;
            Quaternion targetRot = lookDir.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : fromRot;

            float distance = Vector3.Distance(fromPos, catchPos);
            const float pushSeconds = 0.18f;
            const float impactSeconds = 0.13f;
            const float supportedSeconds = 0.22f;
            float catchSeconds = behindHold ? 0.45f : 0.32f;
            if (toNext.sqrMagnitude > 1e-4f)
                next.transform.rotation = Quaternion.LookRotation(-toNext, Vector3.up);

            // 送り手が両手を背に付けるまでは、その場で踏ん張る。
            float contactAt = Time.time + EnemyBrain.PushContactSeconds;
            while (Time.time < contactAt)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;
                if (_shoveSender == null || !_shoveSender.IsAlive || !_shoveSender.IsPushing)
                {
                    Release();
                    yield break;
                }
            }

            PushDirection = Vector3.ProjectOnPlane(catchPos - fromPos, Vector3.up).normalized;
            Quaternion travelRot = PushDirection.sqrMagnitude > 0f
                ? Quaternion.LookRotation(PushDirection, Vector3.up)
                : fromRot;
            PushPulse = 1f;
            RecoveryDistance = distance;
            const float flightSeconds = 0.33f;
            const float followThroughSeconds = 0.17f;
            const float recoverySeconds = flightSeconds + followThroughSeconds;
            float t = 0f;
            while (t < recoverySeconds)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;

                t += Time.deltaTime;
                float pushProgress = Mathf.Clamp01(t / pushSeconds);
                PushPulse = 1f - pushProgress;
                float flight = Mathf.Clamp01(t / flightSeconds);
                float followThrough = Mathf.InverseLerp(flightSeconds, recoverySeconds, t);
                RecoveryStep = flight + followThrough;
                RecoveryPoseWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.18f, RecoveryStep));
                // 着地の前後で水平速度が急に変わらないようにする。
                float drivenFlight = flight * (1.50f - 0.50f * flight);
                float travel = ShoveLandingFraction * drivenFlight + (1f - ShoveLandingFraction) * followThrough;
                // 押された体が先に動き、足が遅れて床を離れる。
                float airborne = Mathf.InverseLerp(0.12f, 1f, flight);
                RecoveryLift = 0.075f * (4f * airborne * (1f - airborne)) * Mathf.InverseLerp(0.1f, 0.8f, distance);
                RecoveryTravel = distance * travel;
                transform.position = Vector3.Lerp(fromPos, catchPos, travel) + Vector3.up * RecoveryLift;
                transform.rotation = Quaternion.Slerp(fromRot, travelRot, Mathf.SmoothStep(0f, 1f, pushProgress));
            }

            next.BeginCatch(catchSeconds);
            Debug.Log($"[HandOff] catch {next.name} kind={kind}");
            t = 0f;
            Quaternion catchRot = transform.rotation;
            Vector3 receiverStart = next.transform.position;
            Quaternion receiverRot = next.transform.rotation;
            // 受け手の後退は、接触までに残った速度に合わせる。
            Vector3 recoil = towardReceiver * Mathf.Min(0.16f, distance * 0.073f);
            while (t < catchSeconds)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;

                t += Time.deltaTime;
                float recoilTime = Mathf.Clamp01(t / impactSeconds);
                float recoilProgress = recoilTime * (2f - recoilTime);
                Vector3 drift = recoil * recoilProgress;
                next.transform.position = receiverStart + drift;

                // 衝突後はまず踏ん張って支える。後ろ拘束への回転は、それから二人で行う。
                float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(supportedSeconds, catchSeconds, t));
                HandOffTurn = behindHold ? settle : 0f;
                Quaternion pivot = Quaternion.AngleAxis(18f * HandOffTurn, Vector3.up);
                next.transform.rotation = pivot * receiverRot;
                Vector3 offset = Vector3.Lerp(catchPos, toPos, settle) - receiverStart;
                transform.position = next.transform.position + pivot * offset;
                transform.rotation = Quaternion.Slerp(catchRot, pivot * targetRot, settle);
                RecoveryPoseWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impactSeconds, catchSeconds, t));
            }

            EndShove();
            ApplyHandOffHold(kind);
            _session.HeldSeconds = 0f;
            next.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
            IsHandingOff = false;
            Started?.Invoke(kind);
        }

        void EndShove()
        {
            if (_shoveSender != null)
                _shoveSender.EndPush();
            _shoveSender = null;
            PushPulse = 0f;
            PushDirection = Vector3.zero;
            RecoveryStep = 0f;
            RecoveryTravel = 0f;
            RecoveryLift = 0f;
            RecoveryPoseWeight = 0f;
            HandOffTurn = 0f;
            RecoveryDistance = 0f;
        }

        bool CanContinueHandOff(EnemyBrain next)
        {
            if (!IsHandingOff || Captor != next)
                return false;

            if (next != null && next.IsAlive && next.Phase != EnemyPhase.Stagger &&
                Stats != null && !Stats.IsDead && !Stats.IsDown)
                return true;

            Release();
            return false;
        }

    }
}
