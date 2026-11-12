using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// 掴みが当たったとき。プレイヤーを掴んだまま硬直を伸ばし、
        /// 振りほどかれる（=こちらがよろける）か時間切れで解放されるまで待つ。
        /// 掴めるのは背後に回れているときだけ（正面掴みは廃止した）。
        /// </summary>
        internal bool TryGrab()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            // 判定は候補の抽選（<see cref="CollectCandidates"/>）と同じ IsBehindPlayer を使う。
            // ここだけ別の閾値にすると「選ばれたのに空振りする」角度ができる。
            if (!IsBehindPlayer())
                return false;

            grapple.BeginContact(this, GrappleKind.Back);
            return true;
        }

        /// <summary>
        /// 拘束中のプレイヤーへ、タッグ技のジャブを開始する（狙いは腹 or 顔）。
        /// 正面・間合い内・攻撃トークンありのときだけ開始する。
        /// 演出側（<see cref="HeldBellyPunchDirector"/>）が位置と向きを合わせてから呼ぶ。
        /// </summary>
        public bool TryStartTagPunch(PunchZone zone, bool allowRecover = false, float holdPoseSeconds = 0f,
            bool? forceLeft = null, float forcePower = 1f)
            => TryStartTagPunch(zone, allowRecover, holdPoseSeconds, fromThrow: false, forceLeft, forcePower);

        internal bool TryStartOpeningPunch(PunchZone zone, float holdPoseSeconds, float forcePower = 1f)
            => isActiveAndEnabled &&
                TryStartTagPunch(zone, true, holdPoseSeconds, fromThrow: zone == PunchZone.Body,
                    forceLeft: null, forcePower: forcePower);

        bool TryStartTagPunch(PunchZone zone, bool allowRecover, float holdPoseSeconds, bool fromThrow,
            bool? forceLeft = null, float forcePower = 1f)
        {
            if (Phase == EnemyPhase.Dead || Phase == EnemyPhase.Windup ||
                Phase == EnemyPhase.Attack || Phase == EnemyPhase.Stagger || IsRising)
                return false;

            // コンボ中は前の一発の硬直を切って次を打たせる（5 連続をテンポよく見せる）。
            if (Phase == EnemyPhase.Recover && !allowRecover)
                return false;

            if (Player == null || Player.IsDead)
                return false;

            var grapple = Player.GetComponent<PlayerGrapple>();
            if (grapple == null || !grapple.IsGrappled || grapple.IsHandingOff || grapple.Captor == this)
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > RangeOf(AttackId.Jab) + 0.25f)
                return false;

            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (to.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(facing.normalized, to.normalized) < 0.85f)
                return false;

            // 同時攻撃の上限は守る。取れなければ演出側が少し待って選び直す。
            if (Group != null && !Group.TryAcquireAttackToken(this))
                return false;

            ForceZone = zone;
            BeginAttackWindup(AttackId.Jab, fromThrow);

            // 彼女が折れる間、拳を腹に押し込んだままにする。数え始めは「当たった瞬間」
            // （Strike）。ここで数えると予備動作の 0.35 秒を食って、折れている間に足りなくなる。
            // 顔を狙うときは押し込まない（通常の顔面パンチと同じ当たり方にする）。
            _bellyPunchPendingHold = zone == PunchZone.Body ? Mathf.Max(0f, holdPoseSeconds) : 0f;
            _tagPunchPending = true;
            _tagPunchSwing = true;
            Camera camera = Camera.main;
            _tagFaceLeft = zone == PunchZone.Face &&
                (forceLeft ?? (camera != null &&
                    Vector3.Dot(camera.transform.position - transform.position, transform.right) > 0f));
            _tagFacePower = zone == PunchZone.Face ? Mathf.Clamp(forcePower, 0.6f, 1.4f) : 1f;
            ForceZone = null;

            Debug.Log($"[HeldPunch] order {name} zone={zone} dist={distance:F2} " +
                      $"pos=({transform.position.x:F2},{transform.position.z:F2}) " +
                      $"side={(UsesLeftPunch ? "L" : "R")} power={_tagFacePower:F2}");
            return true;
        }

        /// <summary>
        /// 引き渡しで拘束を受け持つ。掴んだまま硬直（Recover）を維持し、
        /// 相手がもがき終えるまで離さない。
        /// </summary>
        public void TakeOverHold(float seconds)
        {
            AbsorbLungeOffset();
            IsHolding = true;
            HideTelegraph();
            _currentAttack = AttackId.Grab;
            _timer = Mathf.Max(0.5f, seconds);
            Phase = EnemyPhase.Recover;
            ReleaseToken();

            // 正面拘束（Pin）を引き継いだときは、そのまま膝蹴りまで続ける。
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            bool pin = grapple != null && grapple.Kind == GrappleKind.Pin;
            IsPinning = pin;
            _pinKneesLeft = pin ? Mathf.Max(1, CombatConstants.PinKneeCount) : 0;
            _pinNextKneeAt = pin ? Time.time + CombatConstants.FrontGrappleEscapeSeconds : 0f;

            EndCatch();

            // 受け止め終えた位置で、向きを彼女へ合わせる。
            if (Player != null)
            {
                Vector3 look = Player.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            Debug.Log($"[HandOff] {name} が拘束を受け持つ {seconds:F1}s pin={pin}");
        }

        /// <summary>外部から怯ませる（振りほどかれた時など）。</summary>
        public void ForceStagger(float seconds)        {
            if (Phase == EnemyPhase.Dead)
                return;

            AbsorbLungeOffset();
            HideTelegraph();
            _riseTimer = 0f;
            IsHolding = false;
            ClearGrappleState(clearAttackState: true);
            ReleaseToken();
            _timer = Mathf.Max(0.05f, seconds);
            _staggerTotal = _timer;
            StaggerProgress = 1f;
            StaggerElapsed = 0f;
            StaggerWeight = 1.6f;
            Phase = EnemyPhase.Stagger;

            Debug.Log($"[Enemy] force stagger {name} {seconds:F2}s");
        }

        /// <summary>
        /// 攻撃中の見た目の踏み込み（root の前進）を、攻撃終了時に実際の位置へ移す。
        /// そのまま root を戻すと、振り終わりに 0.4m 後ろへ滑って見える。
        /// 体の位置を同じだけ進めて root を 0 に戻すので、画は動かない。
        /// </summary>
        void AbsorbLungeOffset()
        {
            var figure = GetComponent<EnemyFigure>();
            float lunge = figure != null ? figure.LungeOffset : 0f;
            if (lunge <= 0.001f)
                return;

            Vector3 position = transform.position;
            position.x += (transform.forward.x >= 0f ? 1f : -1f) * lunge;
            transform.position = position;
            figure.ClearLungeOffset();
        }

        void EndRecover()
        {
            AbsorbLungeOffset();
            ReleaseToken();

            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            IsHolding = false;
            ClearGrappleState();
            Phase = EnemyPhase.Chase;
        }

        bool IsRedundantGrab()
        {
            if (_run.Id != AttackId.Grab || Player == null)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            if (IsPreparingHeldThrow)
                return !CanTargetHeldThrow() || _grapple.Captor != _heldThrowCaptor;

            return _grapple != null && _grapple.IsGrappled;
        }

        /// <summary>プレイヤーが倒れた等で予備動作を中断する。硬直は挟まない。</summary>
        void CancelWindup()
        {
            AbsorbLungeOffset();
            HideTelegraph();
            ClearTransientAttackState();
            ReleaseToken();
            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            Phase = EnemyPhase.Chase;
        }

        /// <summary>
        /// 拘束（押さえつけ / ベアハッグ / 引き渡し）の状態を落とす。
        /// 攻撃の一時フラグまで消すかは呼び出し側で決める。
        /// </summary>
        void ClearGrappleState(bool clearAttackState = false, bool resetKneeTimer = false)
        {
            if (IsWallPinning)
            {
                IsWallPinning = false;
                _wallReturn = WallReturnSeconds;
                _wallReturnX = transform.position.x;
                _wallReturnZ = transform.position.z;
            }

            IsPinning = false;
            IsBearHugging = false;
            _pinKneesLeft = 0;
            _squeezeActive = false;
            if (resetKneeTimer)
                _pinNextKneeAt = 0f;
            if (clearAttackState)
                ClearTransientAttackState();
        }

        /// <summary>
        /// 進行中の一振りに付随するフラグを落とす。
        /// 予備動作を中断したり怯んだりしたときに残すと、次の攻撃を始めるまで
        /// KeepSpacing（重なり回避）・フェイント・片手打ちの姿勢が効き続ける。
        /// 振り抜き（Strike）と硬直（Recover）では落とさないこと。
        /// </summary>
        void ClearTransientAttackState()
        {
            EndPush();
            _heldThrowCaptor = null;
            _heldThrowPullUntil = 0f;
            _tagPunchPending = false;
            _tagPunchSwing = false;
            _bellyPunchPendingHold = 0f;
            _punchPoseHoldSeconds = 0f;
            _pinPending = false;
            _bearHugPending = false;
        }

        // --- 攻撃の抽選 ----------------------------------------------------------

        internal bool TryBeginAttack(float distance)
        {
            if (Player == null || Player.IsDead)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            // 回り込み直後は反対を向いていることがある。予備動作中は旋回を
            // 固定するため、向き直る前に攻撃を始めると必ず空振りになる。
            Vector3 facingTarget = Player.transform.position - transform.position;
            facingTarget.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (facingTarget.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(facing.normalized, facingTarget.normalized) < 0.9f)
                return false;

            if (Time.time < _attackReadyAt)
                return false;

            AttackId id;
            bool force = _forceGrab;
            bool pin = false;
            bool bearHug = false;

            bool fromBehind = Vector3.Dot(Player.transform.forward, transform.position - Player.transform.position) < -0.25f;

            if (_grapple != null && !_grapple.IsGrappled && fromBehind && distance <= RangeOf(AttackId.Grab)
                && Time.time >= _grabReadyAt && (!_hasLast || _lastAttack != AttackId.Grab)
                && UnityEngine.Random.value < 0.65f)
            {
                id = AttackId.Grab;

            }
            else if (_pinOrder && CanStartPin(distance))
            {
                // 指示（-pintest）は必ず押さえつけにする。
                id = AttackId.Grab;
                pin = true;
            }
            else if (CanStartBearHug(distance) && !CanStartWallPin(distance)
                && (_bearHugOrder || UnityEngine.Random.value < CombatConstants.BearHugChance))
            {
                // 正面から胴を抱えるベアハッグ。同じ正面の掴みでも Pin とは形が違う。
                // 壁の前では選ばない。壁押さえつけの機会をベアハッグが食うと、
                // 壁際で正面から掴まれても壁へ押し出されない（実測: テストで Pin が選ばれなかった）。
                id = AttackId.Grab;
                bearHug = true;
            }
            else if (CanStartPin(distance))
            {
                // 正面からの押さえつけ。振りほどく猶予の後に膝蹴りへ移る。
                id = AttackId.Grab;
                pin = true;
            }
            else if (force && (_grapple == null || !_grapple.IsGrappled) && distance <= RangeOf(AttackId.Grab))
            {
                _forceGrab = false;
                id = AttackId.Grab;
            }
            else if (!TrySelectAttack(distance, out id))
                return false;

            // グループがあれば同時攻撃の順番を取る。取れなければ Strafe を続ける。
            if (Group != null && !Group.TryAcquireAttackToken(this))
                return false;

            BeginWindup(id);
            _pinPending = pin;
            _bearHugPending = bearHug;

            // 押さえつけが選ばれなかった理由を 1 秒に 1 回だけ出す（-pinlog）
            if (_pinLog && !pin && CanPin && Time.time >= _pinLogNextAt)
            {
                _pinLogNextAt = Time.time + 1f;
                Debug.Log($"[PinCheck] {name} id={id} dist={distance:F2} " +
                          $"behind={IsBehindPlayer()} grappled={(_grapple != null && _grapple.IsGrappled)} " +
                          $"tok={HasAttackToken} sincePin={Time.time - _lastPinAt:F1} " +
                          $"range={CombatConstants.PinMinDistance:F2}-{CombatConstants.PinMaxDistance:F2}");
            }
            return true;
        }

        /// <summary>次の [PinCheck] ログまで（-pinlog）。</summary>
        float _pinLogNextAt;

        /// <summary>押さえつけが選ばれない理由をログに出す（-pinlog）。</summary>
        bool _pinLog;

        /// <summary>
        /// 押さえつけ（Pin）を始めてよいか。正面・間合い・クールダウンを満たすときだけ true。
        /// 攻撃トークン（同時攻撃の上限）は呼び出し側の <see cref="TryBeginAttack"/> が見る。
        /// 背後からは羽交い締め（Back）になるので出さない。
        /// </summary>
        public bool CanStartPin(float distance) =>
            (CanPin || _pinOrder) && CanStartFrontGrapple(distance);

        /// <summary>
        /// 壁の近くで押さえつけられる状態か。壁押さえつけは正面 0.80〜1.30m の
        /// 入口条件こそ通常の Pin と同じだが、壁が無ければ成立しない。
        /// ベアハッグの抽選から壁押さえつけの機会を守るために使う。
        /// </summary>
        bool CanStartWallPin(float distance) =>
            CanStartPin(distance) && Player != null &&
            WallPinSites.Find(Player.transform.position) != null;

        /// <summary>
        /// ベアハッグを始めてよいか。正面・間合い・クールダウンは Pin と同じ。
        /// Pin と違って敵ごとの許可（<see cref="CanPin"/>）は要らず、全員が使える。
        /// </summary>
        public bool CanStartBearHug(float distance) => CanStartFrontGrapple(distance);

        /// <summary>正面から掴む技（Pin / BearHug）に共通の入口条件。</summary>
        bool CanStartFrontGrapple(float distance)
        {
            if (Player == null || Player.IsDead)
                return false;

            if (distance < CombatConstants.PinMinDistance || distance > CombatConstants.PinMaxDistance)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            if (_grapple == null || _grapple.IsGrappled)
                return false;

            // 背後からは羽交い締め。正面の掴みは正面からだけ。
            if (IsBehindPlayer())
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f)
                return false;

            if (Vector3.Dot(transform.forward, to.normalized) < 0.6f)
                return false;

            // 指示されたとき（-pintest）以外は、連発しないよう間を空ける。
            if (!_pinOrder && Time.time < _lastPinAt + CombatConstants.PinCooldown)
                return false;

            return true;
        }

        /// <summary>
        /// 正面の掴み（Pin / BearHug）を開始する共通部分。
        /// 抽選（CanStartPin / CanStartBearHug）と同じ向き・間合いで判定する。
        /// </summary>
        bool TryBeginFrontGrapple(GrappleKind kind, out float distance)
        {
            distance = 0f;

            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            if (IsBehindPlayer())
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            distance = to.magnitude;
            grapple.BeginContact(this, kind);
            _lastPinAt = Time.time;
            _pinOrder = false;
            _bearHugOrder = false;
            return true;
        }

        /// <summary>
        /// 押さえつけが当たったとき。正面から掴んで、振りほどく猶予を作る。
        /// </summary>
        bool TryPin()
        {
            // 壁の近くで正面から掴めたときは、レーンから壁へ押し出す（壁押さえつけ）。
            WallPinSite site = Player != null ? WallPinSites.Find(Player.transform.position) : null;
            if (site != null)
                return TryWallPin(site);

            if (!TryBeginFrontGrapple(GrappleKind.Pin, out float distance))
                return false;

            ClearGrappleState();
            IsPinning = true;
            // 振りほどけなければ、押さえつけている間に膝を 5 発入れる。
            _pinKneesLeft = Mathf.Max(1, CombatConstants.PinKneeCount);
            _pinNextKneeAt = Time.time + CombatConstants.FrontGrappleEscapeSeconds;
            Debug.Log($"[Pin] begin {name} dist={distance:F2} knees={_pinKneesLeft}");
            return true;
        }

        /// <summary>
        /// 壁へ押し出して押さえつける。彼女は壁に背を向けて立たせ、相手はその手前（-Z）から
        /// 肩を押さえる。距離と膝蹴り・制限時間は通常の押さえつけと同じ。
        /// </summary>
        bool TryWallPin(WallPinSite site)
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            // 彼女のカプセル半径は 0.30 だが、背中を壁の表面ちょうどに付けると、
            // 反った上体の表面（肩甲骨・後頭部）と後ろ髪が壁へ入る。
            // 描画メッシュの実測で、体は -0.006m・髪は +0.004m まで来ていた。
            // 髪は毛束の面が関節より外へ張り出すため、関節の押し戻しだけでは数 mm 残る。
            // 立ち位置そのものを離し、見た目で確実に手前に収める。
            const float playerRadius = 0.46f;
            // 頭上の手首を掴むには、腕の長さ（0.55m）の内側まで寄る必要がある。
            // 0.55m 離すと手が 0.3m 届かず、彼女の胸の前で手が止まって「腹を掴んでいる」
            // ように見えた（実測）。肩から手首まで 0.5m 以内になる距離にする。
            const float captorGap = 0.38f;

            float playerZ = site.FaceZ - playerRadius;
            float captorZ = playerZ - captorGap;

            Vector3 playerSpot = new Vector3(
                Player.transform.position.x, Player.transform.position.y, playerZ);
            Quaternion playerRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            Vector3 captorSpot = new Vector3(
                Player.transform.position.x, transform.position.y, captorZ);
            Quaternion captorRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            _wallFromPos = transform.position;
            _wallToPos = captorSpot;
            _wallFromRot = transform.rotation;
            _wallToRot = captorRotation;

            ClearGrappleState();
            IsPinning = true;
            IsWallPinning = true;
            _wallReturn = 0f;

            grapple.BeginWallPin(this, playerSpot, playerRotation, Vector3.back, site.FaceZ, site);

            _lastPinAt = Time.time;
            _pinOrder = false;
            _bearHugOrder = false;
            _pinKneesLeft = Mathf.Max(1, CombatConstants.PinKneeCount);
            _pinNextKneeAt = Time.time + CombatConstants.FrontGrappleEscapeSeconds;

            Debug.Log($"[WallPin] begin {name} x={Player.transform.position.x:F2} " +
                      $"playerZ={playerZ:F2} captorZ={captorZ:F2} wall={site.name}");
            return true;
        }

        /// <summary>
        /// ベアハッグが当たったとき。正面から胴を抱えて、振りほどく猶予を作る。
        /// 押さえつけと違って膝は入らず、一定間隔で締め付けて HP を少し削る。
        /// </summary>
        bool TryBearHug()
        {
            if (!TryBeginFrontGrapple(GrappleKind.BearHug, out float distance))
                return false;

            ClearGrappleState();
            IsBearHugging = true;
            IsHolding = true;
            _squeezeNextAt = Time.time + CombatConstants.FrontGrappleEscapeSeconds;
            Debug.Log($"[BearHug] begin {name} dist={distance:F2}");
            return true;
        }

        /// <summary>ベアハッグの締め付け。掴んだまま HP を少し削る（ダウンは狙わない）。</summary>
        void DoBearHugSqueeze()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || !grapple.IsGrappled || grapple.Captor != this
                || grapple.Kind != GrappleKind.BearHug)
                return;

            if (Player.IsDead)
                return;

            var squeeze = new AttackData
            {
                id = "bearhug_squeeze",
                kind = AttackKind.Grapple,
                damage = Mathf.Max(0f, CombatConstants.BearHugSqueezeDamage),
                partDamage = 0f,
                targetPart = BodyPart.Torso,
                flinch = CombatConstants.BearHugSqueezeFlinch,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            Player.LastHitZone = PunchZone.Body;
            Player.ReceiveHit(State, squeeze, transform);
            PlaySfx(SfxId.Impact, 0.55f);
        }

        /// <summary>
        /// 押さえつけ中の膝蹴り。彼女は掴まれたままなので、姿勢は崩れずダメージだけ入る
        /// （見た目は <see cref="PlayerGrapple.KneePulse"/> を両者が読んで作る）。
        /// </summary>
        void DoPinKnee()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || !grapple.IsGrappled || grapple.Captor != this)
                return;

            var knee = new AttackData
            {
                id = "pin_knee",
                kind = AttackKind.Light,
                damage = Mathf.Max(0f, CombatConstants.PinKneeDamage),
                partDamage = 0f,
                targetPart = BodyPart.Torso,
                flinch = 6f,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            // 腹への一撃。表情も腹ヒット用に切り替える（解放の直前で消す必要はない）。
            Player.LastHitZone = PunchZone.Body;
            Player.ReceiveHit(State, knee, transform, CombatConstants.HeldKneeBellyDamage);
            grapple.KickKnee();
            PlaySfx(SfxId.Impact, 0.85f);

            Debug.Log($"[Pin] knee {name} left={_pinKneesLeft} hp={Player.State.Hp:F0}");

            if (_pinKneesLeft == 0 && grapple.Captor == this)
                FindFirstObjectByType<GrappleHandOff>()?.TryPinChain(grapple);
        }

        // --- 壁・二体がかり（Y 字）のアシスト役 -------------------------

        /// <summary>二体目として壁押さえつけに加わる。横の持ち場へ移動する。</summary>
        internal void BeginWallAssist(PlayerGrapple hold)
        {
            if (hold == null || !hold.IsWallPin)
                return;

            _wallAssistHold = hold;
            _wallAssistReady = false;
            // 押さえ役は彼女の左側（+X）で左腕を右手で持つので、アシストは右側（-X）に付く。
            _wallAssistSide = -1f;

            IsPinning = false;
            IsHolding = false;
            StopFlanking();
            if (Group != null)
                Group.ReleaseAttackToken(this);
            if (Phase == EnemyPhase.Windup || Phase == EnemyPhase.Attack)
                CancelWindup();

            Debug.Log($"[WallSplit] assist {name} side={_wallAssistSide:F0}");
        }

        /// <summary>二体がかりを降りる（6 発が終わった／脱出された）。</summary>
        internal void ReleaseWallAssist()
        {
            if (_wallAssistHold == null)
                return;

            _wallAssistHold = null;
            _wallAssistReady = false;
            HoldFire(0.8f);
            Debug.Log($"[WallSplit] assist {name} release");
        }

        /// <summary>アシストが来なかったとき、拘束役の膝蹴りを再開する。</summary>
        internal void ResumePinKnees()
        {
            if (!IsPinning)
                return;

            _pinKneesLeft = Mathf.Max(1, CombatConstants.PinKneeCount);
            _pinNextKneeAt = Time.time + 0.25f;
        }

        /// <summary>横の持ち場へ寄り、彼女の方を向く。着いたら ready を立てる。</summary>
        void UpdateWallAssist(float dt)
        {
            var hold = _wallAssistHold;
            if (hold == null || !hold.IsGrappled || !hold.IsWallPin)
            {
                ReleaseWallAssist();
                return;
            }

            Vector3 her = hold.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(hold.transform.forward, Vector3.up).normalized;
            // 二人とも彼女の正面側へ立つ。横に並ぶと腹パンが横から棒で叩く形になる（実測）。
            Vector3 spot = her
                + Vector3.right * (_wallAssistSide * CombatConstants.WallSplitStandGap)
                + forward * CombatConstants.WallSplitStandFront;
            spot.y = transform.position.y;

            // 持ち場へは小走りで寄る（通常の移動速度だと間に合わない）。
            float speed = Mathf.Max(2.4f, MoveSpeed * State.MoveSpeedMultiplier);
            transform.position = Vector3.MoveTowards(transform.position, spot, speed * dt);

            Vector3 look = her - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f)
            {
                // 真横を向いたままだと、空き手が腹を横切って「太鼓を叩いている」画になる。
                // カメラ（プレイヤー）側へ体を開いて、前から腹へ入る形にする。
                Quaternion facing = Quaternion.LookRotation(look.normalized, Vector3.up);
                facing = Quaternion.AngleAxis(-_wallAssistSide * 15f, Vector3.up) * facing;
                transform.rotation = Quaternion.Slerp(transform.rotation, facing,
                    1f - Mathf.Exp(-10f * dt));
            }

            if (!_wallAssistReady && (transform.position - spot).sqrMagnitude < 0.02f)
            {
                _wallAssistReady = true;
                Debug.Log($"[WallSplit] assist {name} ready");
            }
        }

        /// <summary>
        /// 二人がかり中の拘束役。彼女の左側（+X）へ回り込み、左腕を右手で持ったまま
        /// 空いた左手で腹を打つ。正面に立ったままだと正面のカメラが背中になる。
        /// </summary>
        void UpdateWallSplitCaptor(float dt, PlayerGrapple hold)
        {
            Vector3 her = hold.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(hold.transform.forward, Vector3.up).normalized;
            Vector3 spot = her
                + Vector3.right * CombatConstants.WallSplitStandGap
                + forward * CombatConstants.WallSplitStandFront;
            spot.y = transform.position.y;

            // 押さえつけ中は移動速度倍率が 0 のことがあるので、位置合わせは定速で行う。
            float speed = Mathf.Max(1.2f, MoveSpeed);
            transform.position = Vector3.MoveTowards(transform.position, spot, speed * dt);

            Vector3 look = her - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f)
            {
                // アシストと同じく、カメラ（プレイヤー）側へ体を開く。
                Quaternion facing = Quaternion.LookRotation(look.normalized, Vector3.up);
                facing = Quaternion.AngleAxis(-15f, Vector3.up) * facing;
                transform.rotation = Quaternion.Slerp(transform.rotation, facing,
                    1f - Mathf.Exp(-10f * dt));
            }
        }

        /// <summary>二人がかりの腹パン 1 発。見た目は空き手のポーズ側が作る。</summary>
        internal void DoWallSplitPunch()        {
            if (Player == null || Player.IsDead)
                return;

            var punch = new AttackData
            {
                id = "wall_split_punch",
                kind = AttackKind.Light,
                damage = Mathf.Max(0f, CombatConstants.WallSplitPunchDamage),
                partDamage = 0f,
                targetPart = BodyPart.Torso,
                flinch = 4f,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            Player.LastHitZone = PunchZone.Body;
            Player.ReceiveHit(State, punch, transform, CombatConstants.WallSplitPunchBelly);
            PlaySfx(SfxId.Impact, 0.8f);
            Debug.Log($"[WallSplit] punch {name} hp={Player.State.Hp:F0} belly={Player.HeldBellyDamage:F0}");
        }

    }
}
