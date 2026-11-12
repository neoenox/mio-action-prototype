using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        internal void TickWindup(float dt)
        {
            // 掴みと投げは、手を伸ばす段階でだけ踏み込む。
            _timer -= dt;

            float total = Mathf.Max(0.05f, _run.Windup);
            float t = 1f - Mathf.Clamp01(_timer / total);
            WindupProgress = t;
            if (IsPreparingGrab)
                StepIntoGrab(1f - Mathf.Clamp01((_timer + dt) / total), t);
            ApplyTelegraphVisual(t);

            if (_timer <= 0f)
            {
                // フェイント: 予備動作まで見せてから振らずに引く。
                // 踏み込ませて空振りさせるための揺さぶり。1 回の接近につき 1 度だけ。
                // 羽交い締め中は構えを途中で引かない。タッグ技も必ず振る。
                bool rearHold = _grapple != null && _grapple.IsGrappled && _grapple.Kind == GrappleKind.Back;
                if (!_tagPunchPending && _run.Id != AttackId.Grab && !_feinted &&
                    !rearHold && FeintChance > 0f && UnityEngine.Random.value < FeintChance)
                {
                    _feinted = true;
                    HideTelegraph();
                    WindupProgress = 0f;
                    _attackReadyAt = Time.time + Mathf.Max(0.1f, FeintDelaySeconds);
                    ReleaseToken();
                    Phase = EnemyPhase.Chase;
                    Debug.Log($"[Enemy] feint {name}");
                    return;
                }

                Phase = EnemyPhase.Attack;
            }
        }

        /// <summary>判定を出す瞬間。1 フレームだけ <see cref="EnemyPhase.Attack"/> に滞在する。</summary>
        internal void Strike(float distance)
        {
            // 予備動作が終わった直後でも、別の敵に倒されたり掴まれたりすることがある。
            if ((Player != null && Player.IsDown) || IsRedundantGrab())
            {
                CancelWindup();
                return;
            }

            HideTelegraph();

            // ここで実際に振ったので、次の接近ではまたフェイントを選べる
            _feinted = false;

            // 振り抜きの風切り音。当たったかどうかとは別に、振ったことは必ず鳴らす
            PlaySfx(SfxId.Swing, 0.85f);

            // 倒れた相手は殴らない
            bool grabbed = false;

            if (Player != null && !Player.IsDead && _run.Data != null)
            {
                float strikeRange = IsPreparingHeldThrow ? HeldThrowReach + HeldThrowContactTolerance
                    : _run.Id == AttackId.Grab ? GrabContactRange : _run.Range + StrikeTolerance;
                if (distance <= strikeRange)
                {
                    if (IsPreparingHeldThrow)
                        LandHeldThrow();
                    else if (_run.Id == AttackId.Grab)
                    {
                        Vector3 toPlayer = Player.transform.position - transform.position;
                        toPlayer.y = 0f;
                        if (Vector3.Dot(transform.forward, toPlayer.normalized) >= 0.7f)
                            grabbed = _pinPending ? TryPin()
                                : _bearHugPending ? TryBearHug()
                                : TryGrab();
                    }
                    else if (PunchTouchesPlayer())
                    {
                        Player.LastHitZone = Zone;
                        float bellyDamage = _tagPunchPending && Zone == PunchZone.Body
                            ? CombatConstants.HeldBellyPunchDamage : 0f;
                        Player.ReceiveHit(State, _run.Data, transform, bellyDamage,
                            isTagPunch: _tagPunchPending);

                        // タッグ腹パンは、当たった瞬間から拳を腹に押し込む。
                        // 外れた／別の部位なら押し込まない（空振りで腕だけ残ると不自然）。
                        if (_bellyPunchPendingHold > 0f && Zone == PunchZone.Body)
                            _punchPoseHoldSeconds = _bellyPunchPendingHold;
                    }
                    else if (_hitLog || (_grapple != null && _grapple.IsGrappled))
                    {
                        Debug.Log($"[StrikeMiss] {name} tag={_tagPunchPending} zone={Zone} " +
                                  $"dist={distance:F2} range={_run.Range:F2}");
                    }
                }
                else if (_hitLog || (_grapple != null && _grapple.IsGrappled))
                {
                    Debug.Log($"[StrikeMiss] {name} out-of-range tag={_tagPunchPending} " +
                              $"dist={distance:F2} range={_run.Range:F2}+{StrikeTolerance:F2}");
                }
            }

            // この一振りで予約は使い切る。次の通常攻撃に持ち越さない。
            _bellyPunchPendingHold = 0f;
            _tagPunchPending = false;
            _heldThrowCaptor = null;
            _pinPending = false;
            _bearHugPending = false;
            if (!grabbed)
                ClearGrappleState();

            _punchVisualTimer = _run.Id == AttackId.Grab ? 0f : 0.18f;
            Phase = EnemyPhase.Recover;

            // 掴んだときは「相手がもがき終わるまで」硬直を伸ばす。
            // ここで上書きすると 0.7 秒で手を離してしまい、掴みポーズも消える。
            _timer = grabbed
                ? (IsBearHugging ? CombatConstants.BearHugLimitSeconds : CombatConstants.GrappleLimitSeconds) + 0.2f
                : Mathf.Max(0f, RecoverSeconds);
            IsHolding = grabbed;
            if (grabbed)
            {
                _forceGrab = false;
                _grabReadyAt = Time.time + 7.2f;
                ReleaseToken();
            }
        }

        public bool TryStartHeldThrow(bool positioned = false)
        {
            if (!isActiveAndEnabled || !IsAlive || IsRising || IsHolding ||
                (IsHandOffLocked && !positioned) ||
                Phase == EnemyPhase.Windup || Phase == EnemyPhase.Attack || Phase == EnemyPhase.Stagger)
                return false;

            if (Player == null || Player.IsDead || Player.IsDown)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            if (!CanTargetHeldThrow())
                return false;

            Vector3 toPlayer = Player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > RangeOf(AttackId.Grab) ||
                Vector3.Dot(transform.forward, toPlayer.normalized) < 0.6f ||
                !TryReserveTagPunchToken())
                return false;

            BeginWindup(AttackId.Grab);
            _heldThrowCaptor = _grapple.Captor;
            return true;
        }

        bool CanTargetHeldThrow()
        {
            if (_grapple == null || !_grapple.IsGrappled || _grapple.IsHandingOff ||
                _grapple.Kind != GrappleKind.Back || _grapple.Captor == null ||
                !_grapple.Captor.IsAlive || _grapple.Captor == this)
                return false;

            Vector3 fromPlayer = transform.position - Player.transform.position;
            fromPlayer.y = 0f;
            return Vector3.Dot(Player.transform.forward, fromPlayer.normalized) > 0.55f;
        }

        void StepIntoGrab(float previous, float progress)
        {
            float before = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, previous));
            float after = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, progress));
            if (after <= before)
                return;

            Vector3 toPlayer = Player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f)
                return;

            Vector3 direction = toPlayer.normalized;
            bool heldThrow = IsPreparingHeldThrow;
            if (!heldThrow && (IsBehindPlayer() == IsPreparingFrontGrab ||
                Vector3.Dot(transform.forward, direction) < 0.7f))
                return;

            float reach = heldThrow ? HeldThrowReach : GrabReach;
            float maxStep = heldThrow ? 0.60f : 0.52f;
            float distance = Mathf.Max(0f, toPlayer.magnitude - reach);
            float step = Mathf.Min(distance, maxStep * (after - before));
            transform.position += direction * step;
            GrabAdvance += step;
            if (heldThrow)
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        void LandHeldThrow()
        {
            // 投げはダメージを与える一撃ではなく、腹パン体勢（腹を差し出す）への移行。
            // ReceiveHit は通さない：ひるみ・スタン・痛み表情・ヒットストップ・揺れを出さないため。
            // 彼女は引き込まれて体勢を変え、そのまま最初の腹パンを待つ。
            _heldThrowPullUntil = Time.time + HeldThrowPullSeconds;
            PlaySfx(SfxId.Tell, 0.6f);
            _grapple.NotifyHeldThrowLanded(this);
        }

        // Attack の前フレームで描いた拳の位置を使い、見えていない距離での命中を防ぐ。
        // 拳は狙いの座標（顔 / 腹）へ向けて振られているので、その座標への近さで当たりを取る。
        bool PunchTouchesPlayer()
        {
            var figure = GetComponent<EnemyFigure>();
            var fist = figure != null ? figure.Fist(UsesLeftPunch) : null;
            if (fist == null || Player == null) return false;
            Vector3 target = PunchTarget(Player, Zone, BodyTargetBias);
            Vector3 toPlayer = Player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (Vector3.Dot(transform.forward, toPlayer) <= 0f) return false;
            float gap = Vector3.Distance(fist.position, target);
            // 腹パンの距離は -hitlog のときだけ出す（常時出すとログが埋まる）。
            if (_hitLog && (_punchPoseHoldSeconds > 0f || _bellyPunchPendingHold > 0f || _tagPunchPending))
            {
                Transform wrist = figure.WristBone(UsesLeftPunch);
                float wristGap = wrist != null ? Vector3.Distance(wrist.position, target) : -1f;
                Debug.Log($"[BellyHit] att={name} pos=({transform.position.x:F2},{transform.position.z:F2}) " +
                          $"yaw={transform.eulerAngles.y:F0} " +
                          $"fist=({fist.position.x:F2},{fist.position.y:F2},{fist.position.z:F2}) " +
                          $"target=({target.x:F2},{target.y:F2},{target.z:F2}) gap={gap:F2} wristGap={wristGap:F2} " +
                          $"bias={BodyTargetBias:F2}");
            }

            return gap <= Mathf.Max(0.05f, PunchHitRadius);
        }

    }
}
