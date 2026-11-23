using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    // The normal enemy AI yields while this controller owns the selected attacker.
    [DefaultExecutionOrder(21100)]
    internal sealed class GroundMountAttack : MonoBehaviour
    {
        internal enum Beat { Idle, Approach, Mount, Roll, Windup, Strike, Recover, Dismount }
        const int MaxPunches = 5;
        const int EscapeInputs = 6;
        const float RemountDelay = 2.5f;
        const float FeetRadius = 1.02f;

        PlayerStats _player;
        CharacterAnimator _animator;
        PlayerMotor _motor;
        ThirdPersonCamera _camera;
        EnemyFigure _figure;
        Vector3 _fromPosition;
        Quaternion _fromRotation;
        Vector3 _approachSpot;
        float _approachDistance;
        float _approachTravel;
        Vector3 _shotOffset;

        internal float MountFaceVisible { get; private set; } = 1f;

        float _nextMountFaceLog;

        float _nextFigureRefresh;

        EnemyFigure[] _faceFigures;
        // マウントのカメラ候補。彼女の体の脇を中心に、頭の方向へ ±30° まで振る。
        // 2.6m → 2.3m → 1.95m と段階的に寄せた（2026-11-20/23）。レンズ前の余裕は下限で守る。
        static readonly float[] FaceDistances = { 1.35f, 1.55f, 1.75f, 1.95f };
        // 仰向けの顔は空を向いているので、低い角度だけだと殴られて首が振れるたびに顔を背けた画になる
        // （計測 2026-11-23: 固定中の 5〜8 割が「見えているが背けている」）。見下ろしの深い高さも候補に入れる。
        static readonly float[] FaceHeights = { 0.9f, 1.2f, 1.5f, 1.8f, 2.1f };
        static readonly float[] FaceYaws = { -30f, -15f, 0f, 15f, 30f };

        /// <summary>
        /// 見下ろしの上限（度）。真上近くから見ると注視が真下になり、画が反転する。
        /// 45° では仰向けの顔を斜めからしか撮れず、表情が読めるコマが 3〜5 割に落ちた（2026-11-23）。
        /// </summary>
        internal const float MountMaxElevation = 60f;

        /// <summary>レンズと敵の体の最短距離の下限（m）。これより近いと関節球が画面を覆う。</summary>
        internal const float MountMinLens = 0.5f;

        /// <summary>
        /// 画を選ぶときのレンズ下限（m）。ハード下限 0.5m に 0.12m の余裕を残す。
        /// 0.75m だと 1.95m の画しか使えず「遠すぎる」ので下げた（確認 2026-11-23）。
        /// </summary>
        const float MountSelectLens = 0.62f;

        /// <summary>顔が画面の高さに占める割合の下限。これより小さいと表情が読めない。</summary>
        internal const float MountMinFaceSize = 0.04f;

        /// <summary>
        /// 候補の全探索の間隔（秒）。候補は最大 150 個あり、毎フレーム全部測ると
        /// フレームレートが落ちる（実測: テストで 2 秒 31〜39 フレーム、マウントのパンチが時間内に届かない）。
        /// 間のフレームは、いまの画だけを測って使えなくなっていないかを見る。
        /// </summary>
        const float MountSearchInterval = 0.2f;

        Camera _faceProbe;
        Vector3 _mountWanted;
        float _nextMountSearch;
        float _mountLockUntil;
        float _mountLens = float.MaxValue;
        bool _mountFallback;
        internal float MountLensClearance => _mountLens;
        internal float MountFaceSize { get; private set; }


        void OnDestroy()
        {
            if (_faceProbe != null)
                Destroy(_faceProbe.gameObject);
        }
        float _elapsed;
        float _duration;
        float _downTime;
        float _nextSearch;
        float _side;
        bool _escaped;
        int _punches;
        int _struggles;
        float _reactionAge = float.PositiveInfinity;

        /// <summary>ロール明けに頭側へ跨り直している最中か。</summary>
        bool _resettling;
        float _resettleElapsed;
        Vector3 _resettleFromPosition;
        Quaternion _resettleFromRotation;

        /// <summary>跨り直しにかける秒数。アニメ側の反転ブレンド（0.15 秒）に合わせる。</summary>
        const float ResettleSeconds = 0.18f;

        internal EnemyBrain Attacker { get; private set; }
        internal Beat Stage { get; private set; }
        internal float Progress => Stage == Beat.Approach
            ? Mathf.InverseLerp(0f, _approachDistance, _approachTravel)
            : _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 0f;
        internal bool BlocksGetUp => MountBlocksGetUp || (Haul != null && Haul.BlocksGetUp);

        /// <summary>寝技（跨り）そのものが起き上がりを止めているか（引き起こしは含まない）。</summary>
        internal bool MountBlocksGetUp => Attacker != null && Stage >= Beat.Mount;

        /// <summary>ダウンを引きずり起こす制御（寝技と同じ「一人を選ぶ」枠で動く）。</summary>
        internal GroundHaulUp Haul { get; private set; }

        /// <summary>この敵を、寝技か引き起こしが占有しているか（通常の敵 AI は譲る）。</summary>
        internal bool Owns(EnemyBrain enemy)
        {
            // 引き起こし中は全員の AI を止める。他の敵が横を歩き回ると、
            // 抱え上げている二人の横をすり抜けて「ちょこちょこ歩く」画になる。
            if (enemy != null && Haul != null && Haul.Active)
                return true;
            return enemy != null && Attacker == enemy;
        }

        internal void DelayNextSearch(float seconds) =>
            _nextSearch = Mathf.Max(_nextSearch, Time.time + seconds);

        internal void ClearSearchDelay() => _nextSearch = 0f;
        internal float Fear => Stage switch
        {
            Beat.Mount when !_player.DownIsForward => Mathf.SmoothStep(0f, 1f, Progress),
            Beat.Windup or Beat.Strike or Beat.Recover => 1f,
            Beat.Dismount => 1f - Mathf.SmoothStep(0f, 1f, Progress),
            _ => 0f
        };
        internal float ShoulderRoll => RollPhase(0.20f, 0.77f);
        internal float RollProgress => Mathf.Pow(RollPhase(0.28f, 0.92f), 1.50f);
        internal float RollPhase(float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, Progress));
        internal bool RollLeftShoulder { get; private set; }
        internal bool LeftPunch => (_punches & 1) != 0;
        internal float Side => _side;
        internal bool Escaped => _escaped;
        internal float StrugglePulse { get; private set; }
        internal Vector3 StrikeTarget { get; private set; }
        internal Vector3 StrikeSide => Attacker.transform.right * (LeftPunch ? -1f : 1f);
        internal Vector3 ImpactSide { get; private set; }
        internal bool ImpactWasLeftPunch { get; private set; }
        internal Vector3 ShotOffset => _shotOffset;
        internal Vector3 AttackerHead => _figure.HeadBone.position;
        internal bool HasTakenPunch { get; private set; }
        internal int PunchesLanded => _punches;
        internal float MountFatigue => Mathf.Clamp01(_punches / (float)MaxPunches);
        internal bool IsMountKnockedOut => _punches >= MaxPunches && _reactionAge > 0.22f;
        float RecoilScale => Mathf.Lerp(1f, 1.25f, MountFatigue);
        internal float HeadRecoil => (ImpactWasLeftPunch
            ? Recoil(-0.012f, 0.058f, 0.24f)
            : Recoil(-0.018f, 0.036f, 0.22f)) * RecoilScale;
        internal float TorsoRecoil => Recoil(0.010f, 0.073f, 0.28f) * RecoilScale;
        internal float ArmRecoil => Recoil(-0.052f, 0.10f, 0.30f);
        internal float LegRecoil => Recoil(0.044f, 0.118f, 0.33f);
        internal bool IsApplyingPunch => Attacker != null && Stage == Beat.Strike && _reactionAge == 0f;

        float Recoil(float start, float peak, float end)
        {
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, peak, _reactionAge));
            return rise * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(peak, end, _reactionAge)));
        }
        internal float Cock => Stage switch
        {
            Beat.Windup => Mathf.SmoothStep(0f, 1f, Progress),
            Beat.Strike => 1f - Drive,
            _ => 0f
        };
        internal float Drive => Stage switch
        {
            Beat.Strike => Mathf.SmoothStep(0f, 1f, Progress),
            Beat.Recover => 1f - Mathf.SmoothStep(0f, 1f, Progress / 0.80f),
            _ => 0f
        };
        const float OverlapLift = 0.31f;
        internal float ArmWindup => Stage == Beat.Windup
            ? Mathf.Lerp(_punches == 0 ? 0f : OverlapLift, 1f, Cock)
            : 0f;
        internal float NextArmWindup => Stage == Beat.Recover && _punches + 1 < MaxPunches
            ? OverlapLift * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, Progress))
            : 0f;
        internal float LeftHookCentering => !LeftPunch ? 0f : Stage switch
        {
            Beat.Windup => Cock,
            Beat.Strike => 1f,
            Beat.Recover => 1f - Mathf.SmoothStep(0f, 1f, Progress),
            _ => 0f
        };
        internal ProceduralRig TargetRig => _animator != null ? _animator.Rig : null;
        internal float EscapeProgress => _struggles / (float)EscapeInputs;

        void Awake()
        {
            _player = GetComponent<PlayerStats>();
            _motor = GetComponent<PlayerMotor>();
            Haul = gameObject.AddComponent<GroundHaulUp>();
        }

        /// <summary>
        /// ダウンが明けた（または新しいダウンが始まった）ときの後始末。
        /// <para>
        /// **<see cref="End"/> が決めた再マウント待ち（<see cref="RemountDelay"/>）は消さない。**
        /// ここは <see cref="Update"/> からダウン中でない間ずっと毎フレーム呼ばれるので、
        /// 以前のように <c>_nextSearch = 0</c> を書くと待ち時間が毎フレーム潰れ、
        /// 解除直後のダウンで 0.75 秒後にまた跨られてしまう（実測）。
        /// 初期値は 0 なので、開始直後のダウンはこれまでどおりすぐ探索できる。
        /// </para>
        /// </summary>
        internal void ResetKnockdown()
        {
            End();
            _downTime = 0f;
        }

        void OnDisable() => End();

        void Update()
        {
            if (!_player.IsDown || _player.IsDead || _player.IsGettingUp)
            {
                ResetKnockdown();
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            if (_animator == null)
                _animator = GetComponentInChildren<CharacterAnimator>();

            _downTime += dt;
            StrugglePulse = Mathf.MoveTowards(StrugglePulse, 0f, dt * 4f);
            _reactionAge += dt;
            if (Attacker == null)
            {
                if (Stage != Beat.Idle)
                    End();

                if (_downTime >= 0.75f && Time.time >= _nextSearch && !_player.GetUpRequested &&
                    !Haul.Active &&
                    (_motor == null || !_motor.IsAirborne) &&
                    _animator != null && _animator.Current == CharacterAnimator.State.Down &&
                    TargetRig != null && TargetRig.Ready)
                {
                    _nextSearch = Time.time + 0.2f;
                    FindAttacker();
                }
                return;
            }

            if (TargetRig == null || !TargetRig.Ready || !Attacker.CanMountDownedPlayer ||
                (!BlocksGetUp && _player.GetUpRequested))
            {
                End();
                return;
            }

            if (Stage >= Beat.Mount && Stage < Beat.Dismount && !_player.DownIsForward
                && InputReader.StrugglePressed())
                Struggle();

            _elapsed += dt;
            if (_resettling && Stage >= Beat.Windup && Stage < Beat.Dismount)
                TickResettle(dt);

            if (Stage == Beat.Approach)
            {
                float speed = Attacker.MoveSpeed * Attacker.State.MoveSpeedMultiplier;
                if (speed <= 0f)
                {
                    End();
                    return;
                }
                _approachTravel = Mathf.Min(_approachTravel + speed * dt, _approachDistance);
                WalkToMountSpot();
                if (_approachTravel >= _approachDistance)
                    Advance();
                return;
            }
            else if (Stage == Beat.Mount)
                MoveTowardsPose(MountSpot(), TargetRig.Head.position, Mathf.InverseLerp(0.04f, 0.94f, Progress));
            else if (Stage == Beat.Dismount)
            {
                Vector3 retreat = _escaped ? FeetSpot() - BodyAxis() * 0.45f : FeetSpot();
                MoveTowardsPose(retreat, TargetRig.Head.position, Progress);
            }
            else if (Stage >= Beat.Windup && !_resettling)
                StayOnMountSpot(dt);

            if (Progress >= 1f && Stage != Beat.Strike && Stage != Beat.Roll)
                Advance();
        }

        internal void Struggle()
        {
            if (Stage < Beat.Mount || Stage >= Beat.Dismount || _player.DownIsForward)
                return;
            StrugglePulse = 1f;
            ProceduralSfx.Instance?.Play(SfxId.Tell, 0.45f);
            if (++_struggles >= EscapeInputs)
            {
                _escaped = true;
                ProceduralSfx.Instance?.Play(SfxId.Swing, 0.85f);
                FindFirstObjectByType<CameraShake>()?.Shake(0.08f, 0.18f);
                Begin(Beat.Dismount, 0.40f);
            }
        }

        void FindAttacker()
        {
            float best = 3f * 3f;
            foreach (var enemy in EnemyRegistry.All)
            {
                if (enemy.Player != _player || !enemy.CanMountDownedPlayer || enemy.MoveSpeed <= 0f ||
                    !enemy.TryGetComponent(out EnemyFigure figure))
                    continue;

                float distance = (enemy.transform.position - TargetRig.Hips.position).sqrMagnitude;
                if (distance >= best)
                    continue;
                best = distance;
                Attacker = enemy;
                _figure = figure;
            }
            if (Attacker == null)
                return;

            // 寝技の代わりに、手首と裾を掴んで引きずり起こす（羽交い締めへつなぐ）。
            if (Haul.ShouldHaul(Attacker))
            {
                var hauler = Attacker;
                Attacker = null;
                _figure = null;
                Haul.Begin(hauler);
                return;
            }

            _side =Vector3.Dot(Attacker.transform.position - TargetRig.Hips.position, transform.right) < 0f
                ? -1f : 1f;
            SetApproachSpot();
            Begin(Beat.Approach, 0f);
        }

        Vector3 BodyAxis()
        {
            return Vector3.ProjectOnPlane(TargetRig.Head.position - TargetRig.Hips.position, Vector3.up).normalized;
        }

        Vector3 MountSpot() => TargetRig.Hips.position + BodyAxis() * 0.06f;

        Vector3 FeetSpot() => TargetRig.Hips.position - BodyAxis() * FeetRadius;

        void SetApproachSpot()
        {
            Vector3 start = Attacker.transform.position;
            Vector3 hips = TargetRig.Hips.position;
            hips.y = start.y;
            Vector3 from = start - hips;
            // 今いる側から腰へ向かう。頭側と足側では体の端で止まり、跨る動きにつなげる。
            float alongBody = Mathf.Abs(Vector3.Dot(from.normalized, BodyAxis()));
            float clearance = Mathf.Lerp(0.60f, 0.86f, alongBody);
            _approachSpot = hips + Vector3.ClampMagnitude(from, clearance);
            _approachDistance = Vector3.Distance(start, _approachSpot);
            _approachTravel = 0f;
        }

        void WalkToMountSpot()
        {
            if (_approachDistance <= 0.001f)
                return;
            Vector3 position = Vector3.Lerp(_fromPosition, _approachSpot, Progress);
            Quaternion facing = Quaternion.LookRotation(_approachSpot - _fromPosition, Vector3.up);
            Quaternion rotation = Quaternion.Slerp(_fromRotation, facing, Mathf.SmoothStep(0f, 1f, Progress));
            Attacker.transform.SetPositionAndRotation(position, rotation);
        }

        void MoveTowardsPose(Vector3 spot, Vector3 lookAt, float progress)
        {
            spot.y = _fromPosition.y;
            Vector3 facing = lookAt - spot;
            facing.y = 0f;
            float blend = Mathf.SmoothStep(0f, 1f, progress);
            Quaternion rotation = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : _fromRotation;
            Attacker.transform.SetPositionAndRotation(Vector3.Lerp(_fromPosition, spot, blend),
                Quaternion.Slerp(_fromRotation, rotation, blend));
        }

        /// <summary>
        /// 殴っている間（振りかぶり〜戻り）も跨る位置に留まる。
        /// 以前は跨る段階（Mount）でだけ腰へ寄せ、その後は位置を固定していたため、跨った後に
        /// 彼女の体が動くと敵だけ取り残され、頭の 1m 先で空を殴り続けた（放置プレイ実測:
        /// 顔パンの締めのあとのマウントで、パンチが一発も当たらず被弾ログ 0）。
        /// 反動の小さな揺れまで追うと敵が揺れるので、5cm を超えたずれだけ滑らかに詰める。
        /// </summary>
        void StayOnMountSpot(float dt)
        {
            Vector3 spot = MountSpot();
            spot.y = Attacker.transform.position.y;
            Vector3 position = Attacker.transform.position;
            Vector3 delta = spot - position;
            delta.y = 0f;
            float drift = delta.magnitude;
            if (drift > 0.25f && !_driftLogged)
            {
                _driftLogged = true;
                GameLog.Log($"[Mount] drift {drift:F2}m stage={Stage} — 跨る位置へ戻す");
            }
            if (LaunchOptions.Has("-mountlog") && Time.unscaledTime >= _driftLogAt)
            {
                _driftLogAt = Time.unscaledTime + 0.25f;
                Vector3 hips = TargetRig.Hips.position;
                GameLog.Log($"[MountPos] stage={Stage} drift={drift:F2} att=({position.x:F2},{position.z:F2}) " +
                            $"spot=({spot.x:F2},{spot.z:F2}) hips=({hips.x:F2},{hips.z:F2}) " +
                            $"root=({transform.position.x:F2},{transform.position.z:F2})");
            }
            if (drift > 0.05f)
                Attacker.transform.position = Vector3.Lerp(position, spot, 1f - Mathf.Exp(-8f * dt));

            Vector3 look = TargetRig.Head.position - Attacker.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0004f)
                Attacker.transform.rotation = Quaternion.Slerp(Attacker.transform.rotation,
                    Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-8f * dt));
        }

        bool _driftLogged;
        float _driftLogAt;

        /// <summary>
        /// ロール明けに新しい頭側へ跨り直す。
        /// 伏せダウンを仰向けに返すと、仰向け姿が逆端を向いて作られているためアニメ側が
        /// リグを 180° 反転する。跨ったままの向きだと頭と逆を向いて殴ることになるので、
        /// 実際の頭の位置を取り直しながら短時間で向き（と僅かな位置）を合わせる。
        /// </summary>
        void BeginResettle()
        {
            _resettling = true;
            _resettleElapsed = 0f;
            _resettleFromPosition = Attacker.transform.position;
            _resettleFromRotation = Attacker.transform.rotation;
        }

        void TickResettle(float dt)
        {
            if (Attacker == null || TargetRig == null)
            {
                _resettling = false;
                return;
            }

            _resettleElapsed += dt;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_resettleElapsed / ResettleSeconds));
            Vector3 spot = MountSpot();
            spot.y = _resettleFromPosition.y;
            Vector3 lookAt = TargetRig.Head.position - spot;
            lookAt.y = 0f;
            Quaternion rotation = lookAt.sqrMagnitude > 0.0004f
                ? Quaternion.LookRotation(lookAt.normalized, Vector3.up)
                : _resettleFromRotation;
            Attacker.transform.SetPositionAndRotation(
                Vector3.Lerp(_resettleFromPosition, spot, blend),
                Quaternion.Slerp(_resettleFromRotation, rotation, blend));
            if (blend >= 1f)
                _resettling = false;
        }

        void Begin(Beat stage, float duration)
        {
            Stage = stage;
            _elapsed = 0f;
            _duration = duration;
            _fromPosition = Attacker.transform.position;
            _fromRotation = Attacker.transform.rotation;
        }

        void Advance()
        {
            switch (Stage)
            {
                case Beat.Approach: Begin(Beat.Mount, 0.68f); break;
                case Beat.Mount:
                    if (_player.DownIsForward)
                    {
                        Vector3 liftingSide = Vector3.Cross(Vector3.up, BodyAxis()) * _side;
                        RollLeftShoulder = Vector3.Dot(TargetRig.LUpperArm.position - TargetRig.RUpperArm.position,
                            liftingSide) > 0f;
                        Begin(Beat.Roll, 1.00f);
                    }
                    else
                        Begin(Beat.Windup, 0.53f);
                    break;
                case Beat.Roll:
                    FinishRoll();
                    Begin(Beat.Windup, 0.53f);
                    // 寝返りで彼女の体の向きが変わる（仰向け姿は逆端を向いて作られているため
                    // ロール明けにリグが 180° 反転される）。跨っている側のままだと頭と逆向きに
                    // なるので、次の 1 発を振るまでに新しい頭側へ跨り直す。
                    BeginResettle();
                    break;
                case Beat.Windup:
                    StrikeTarget = TargetRig.Head.position;
                    Begin(Beat.Strike, 0.14f);
                    break;
                case Beat.Recover:
                    if (_punches + 1 >= MaxPunches)
                        Begin(Beat.Dismount, 0.42f);
                    else
                    {
                        _punches++;
                        Begin(Beat.Windup, 0.30f);
                    }
                    break;
                case Beat.Dismount:
                    bool escaped = _escaped;
                    var enemy = Attacker;
                    End();
                    if (escaped)
                    {
                        if (enemy != null && enemy.IsAlive)
                            enemy.ForceStagger(CombatConstants.GrappleEscapeStun);
                        _player.RequestGetUp();
                    }
                    break;
            }
        }

        void LateUpdate()
        {
            if (Attacker == null || _figure == null || _player.IsDead || !_player.IsDown || TargetRig == null)
                return;

            _figure.ApplyGroundMount(this);
            if (Stage == Beat.Roll && Progress >= 1f)
            {
                FrameProneRoll();
                Advance();
                return;
            }
            if (Stage == Beat.Strike && Progress >= 1f)
            {
                if (Vector3.Distance(_figure.Fist(LeftPunch).position, TargetRig.Head.position) < 0.20f)
                {
                    ImpactSide = StrikeSide;
                    ImpactWasLeftPunch = LeftPunch;
                    _reactionAge = 0f;
                    HasTakenPunch = true;
                    var punch = AttackLibrary.Light();
                    punch.flinch = 0f;
                    _player.LastHitZone = PunchZone.Face;
                    _player.ReceiveHit(Attacker.State, punch, Attacker.transform);
                }
                Begin(Beat.Recover, 0.33f);
            }

            if (Stage == Beat.Roll || (Stage == Beat.Mount && _player.DownIsForward && Progress >= 0.55f)
                || (Stage == Beat.Windup && _shotOffset == Vector3.zero && Progress < 0.28f))
                FrameProneRoll();
            else if (Stage >= Beat.Mount && Fear >= 0.40f
                && (Stage != Beat.Mount || Progress >= 0.72f))
                FrameMount();
        }

        bool ResolveCamera()
        {
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.GetComponent<ThirdPersonCamera>();
            return _camera != null;
        }

        void FrameProneRoll()
        {
            if (!ResolveCamera())
                return;

            Vector3 axis = BodyAxis();
            Vector3 flank = Vector3.Cross(Vector3.up, axis);
            if (Vector3.Dot(flank, Vector3.back) < 0f)
                flank = -flank;
            // 注視点を頭寄りにすると顔はわずかに残るが、カメラの移動が大きくなり
            // フレーム間差分が 4.2 -> 5.3 倍に悪化した（実測 2026-11-21）。腰固定に戻す。
            Vector3 target = TargetRig.Hips.position + axis * 0.09f + Vector3.up * 0.32f;
            _camera.RequestCinematicShot(target + flank * 1.85f + axis * 0.53f + Vector3.up * 1.18f, target);
        }

        void FrameMount()
        {
            if (!ResolveCamera())
                return;

            var head = TargetRig.Head;
            bool firstFrame = _shotOffset == Vector3.zero;

            // マウント中はカメラを動かさない。跨った最初の 1 回で「顔が読める近めの画」を選び、
            // 以降はその位置・注視点で止める（指定 2026-11-22）。
            if (!firstFrame && _holdValid)
            {
                _camera.RequestCinematicShot(_holdPos, _holdTarget, fov: _holdFov);
                Vector3 heldOffset = _holdPos - _holdTarget;
                var held = ProbeFace(head, _faceFigures, _holdTarget, heldOffset);
                MountFaceVisible = held.Visible;
                MountFaceSize = FaceVisibility.ScreenFaceSize(FaceProbe(), head);
                _mountLens = FaceVisibility.LensClearance(_holdPos, _faceFigures);
                if (LaunchOptions.Has("-mountlog") && Time.unscaledTime >= _nextMountFaceLog)
                {
                    _nextMountFaceLog = Time.unscaledTime + 0.1f;
                    GameLog.Log($"[MountHold] visible={held.Visible:F2} readable={(held.Readable ? 1 : 0)} facing={held.FacingAngle:F0} " +
                                $"size={MountFaceSize * 100f:F1}% stage={Stage} blocker={held.Blocker ?? "none"}");
                }

                // 固定の画は跨った瞬間の姿勢で選ぶので、敵が構え直すと体で顔が隠れたままになることがある
                // （計測 2026-11-23: 固定中ずっと visible=0, blocker=Enemy）。隠れた状態が続いたら、
                // その時点の姿勢で選び直して切り替える。殴られた瞬間の一時的な遮りでは切り替えない。
                // 顔を背けたまま（Readable でない）も、長く続けば同じく選び直す（殴られて首が振れる
                // 一瞬は待つ）。計測: 固定中の 6 割が「見えているが背けている」画があった。
                bool hidden = held.Visible < 0.5f;
                bool turned = !held.Readable;
                _holdBlocked = hidden ? _holdBlocked + Time.unscaledDeltaTime : 0f;
                _holdTurned = turned ? _holdTurned + Time.unscaledDeltaTime : 0f;
                if ((_holdBlocked >= HoldBlockedSeconds || _holdTurned >= HoldTurnedSeconds) &&
                    Time.unscaledTime >= _nextHoldRepick)
                {
                    _nextHoldRepick = Time.unscaledTime + 2.0f;
                    _holdTurned = 0f;
                    _holdBlocked = 0f;
                    _holdValid = false;
                    _shotOffset = Vector3.zero;
                    _mountWanted = Vector3.zero;
                    if (LaunchOptions.Has("-mountlog"))
                        GameLog.Log($"[MountHold] repick ({(hidden ? "face blocked" : "face turned away")})");
                }
                return;
            }

            Vector3 towardHead = Vector3.ProjectOnPlane(head.position - TargetRig.Hips.position, Vector3.up);
            towardHead = towardHead.sqrMagnitude > 1e-6f ? towardHead.normalized : Vector3.forward;
            Vector3 flank = Vector3.Cross(Vector3.up, towardHead).normalized;

            if (Time.time >= _nextFigureRefresh || _faceFigures == null)
            {
                _nextFigureRefresh = Time.time + 0.25f;
                _faceFigures = FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None);
            }
            Vector3 target = Vector3.Lerp(head.position, TargetRig.Hips.position, 0.31f) + Vector3.up * 0.19f;

            // 候補は彼女の体の脇から、顔が読める距離（水平 1.4〜2.3m）で撮るものだけ。
            // レンズと敵の距離は「条件」として使い、遠いほど得をする加点はしない
            // （加点すると、上空から見下ろす豆粒の画が必ず勝つ。実測 face #155〜#190）。
            bool hasCurrent = !firstFrame && _mountWanted != Vector3.zero;
            float currentScore = hasCurrent
                ? ScoreMountShot(head, target, _mountWanted, _mountWanted)
                : float.MinValue;
            bool currentInvalid = hasCurrent && currentScore <= float.MinValue / 2f;

            // 候補の探索は 1 回 50〜60ms かかる（候補 ~110 個 × 顔の可視判定）。1 フレームでまとめて測ると
            // そのフレームが止まり、さらに退避先の画は毎回「使えない」と判定されるため毎フレーム探索が走り、
            // マウント中が 15fps 前後に落ちていた（実測 dt 58〜92ms、放置プレイで「処理落ち感」）。
            // 最初の 1 回（跨った瞬間のカット）だけまとめて測り、以降は毎フレーム少しずつ測る。
            // 退避先を使っている間は「使えない」ことが前提なので、それを理由に探し直さない。
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool searched = false;
            if (!hasCurrent)
            {
                StartMountSearch(hasCurrent, currentInvalid);
                StepMountSearch(head, target, flank, int.MaxValue, currentScore);
                searched = true;
            }
            else
            {
                bool urgent = currentInvalid && !_mountFallback;
                if (_searchCursor < 0 && (urgent || Time.unscaledTime >= _nextMountSearch))
                {
                    _nextMountSearch = Time.unscaledTime + MountSearchInterval;
                    StartMountSearch(hasCurrent, currentInvalid);
                }
                if (_searchCursor >= 0)
                {
                    StepMountSearch(head, target, flank, MountCandidatesPerFrame, currentScore);
                    searched = true;
                }
            }
            if (searched && LaunchOptions.Has("-mountlog"))
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                            System.Diagnostics.Stopwatch.Frequency;
                if (ms > 4.0 || Time.unscaledDeltaTime > 0.03f)
                    GameLog.Log($"[MountPerf] search={ms:F1}ms dt={Time.unscaledDeltaTime * 1000f:F1}ms " +
                                $"fallback={(_mountFallback ? 1 : 0)} stage={Stage}");
            }

            if (firstFrame)
                _shotOffset = _mountWanted;
            else
            {
                float t = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
                _shotOffset = BlendShotOffset(_shotOffset, _mountWanted, t);
            }

            ReportMountShot(head, target, flank, firstFrame);

            if (firstFrame)
            {
                _holdTarget = target;
                _holdPos = target + BackOffFromLens(target, _shotOffset);
                ZoomOnFace(head);
                _holdValid = true;
                // 跨った瞬間のカットは寄せた画へ直接切る（遠い画から寄っていく動きは出さない）。
                _camera.RequestCinematicShot(_holdPos, _holdTarget, cut: true, fov: _holdFov);
            }
        }

        /// <summary>寄せたときに顔が占める、画面の高さに対する割合の目標。</summary>
        const float MountFaceTarget = 0.16f;

        /// <summary>寄せるときの画角の下限（度）。これより絞ると打撃の揺れで顔が枠から外れる。</summary>
        const float MountMinFov = 20f;

        /// <summary>
        /// 固定の画で顔を大きく映す。位置は選んだ画のまま、注視点を顔へ寄せて画角を絞る（ズーム）。
        /// 候補は水平 1.35m 以上の距離からしか選ばず、顔が画面の高さの 8% 前後にしかならなかった
        /// （指定 2026-11-22「顔がちゃんと見えるくらいまで寄って」）。
        /// 位置を顔へ寄せる方法は、跨った敵の脇越しに撮る画なので、少し寄るだけでレンズが敵の体に
        /// ぶつかった（実測: 0.85m でレンズと敵 0.15m）。ズームなら遮蔽の条件は変わらない。
        /// </summary>
        void ZoomOnFace(Transform head)
        {
            _holdFov = 0f;
            Vector3 face = head.position;
            Vector3 aim = Vector3.Lerp(face, TargetRig.Hips.position, 0.12f) + Vector3.up * 0.03f;
            var reading = ProbeFace(head, _faceFigures, aim, _holdPos - aim);
            if (!reading.Readable || reading.InFrame < 0.99f)
            {
                if (LaunchOptions.Has("-mountlog"))
                    GameLog.Log($"[MountZoom] keep (aim face unreadable v={reading.Visible:F2} f={reading.InFrame:F2})");
                return;
            }
            _holdTarget = aim;

            // 基準はカメラの通常の画角。選び直しのときは既に絞った画角になっているので、
            // 今の画角で測ると「もう十分大きい」と判定してズームを外してしまった。
            Camera probe = FaceProbe();
            float baseFov = _camera.BaseFov();
            probe.fieldOfView = baseFov;
            float size = FaceVisibility.ScreenFaceSize(probe, head);
            if (size <= 1e-4f || size >= MountFaceTarget)
                return;

            float tan = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) * size / MountFaceTarget;
            float fov = Mathf.Max(MountMinFov, 2f * Mathf.Atan(tan) * Mathf.Rad2Deg);
            // 絞った画で顔が枠から外れるなら、収まるまで広げる。
            for (; fov < baseFov; fov += 3f)
            {
                probe.fieldOfView = fov;
                var zoomed = FaceVisibility.Measure(probe, head, _faceFigures);
                if (zoomed.InFrame >= 0.99f && zoomed.Readable)
                    break;
            }
            if (fov >= baseFov - 0.5f)
                return;
            _holdFov = fov;
            if (LaunchOptions.Has("-mountlog"))
            {
                probe.fieldOfView = fov;
                GameLog.Log($"[MountZoom] fov {baseFov:F0}->{fov:F0} face {size * 100f:F1}%->" +
                            $"{FaceVisibility.ScreenFaceSize(probe, head) * 100f:F1}%");
            }
        }

        float _holdFov;
        float _holdBlocked;
        float _holdTurned;
        float _nextHoldRepick;

        /// <summary>固定の画で顔が隠れ続けたら選び直すまでの秒数。</summary>
        const float HoldBlockedSeconds = 0.4f;

        /// <summary>固定の画で顔を背け続けたら選び直すまでの秒数。</summary>
        const float HoldTurnedSeconds = 0.6f;

        bool _holdValid;
        Vector3 _holdPos;
        Vector3 _holdTarget;

        /// <summary>
        /// 画の移り変わり。水平の向きは鉛直軸まわりに回し、高さと水平距離は線形に補間する。
        /// 直線補間だと反対側へ移るときに頭の真上を通り、3 次元の Slerp では大円が真上を通る
        /// （左右対称の 2 画の間で見下ろしが 45° → 50° 超になり、跨った敵の背中へレンズが寄って
        /// 下限 0.5m を割った。DownRuntimeTests 実測、単体で 10 回中 2 回）。
        /// この補間なら途中の高さは両端の間に収まる。
        /// </summary>
        internal static Vector3 BlendShotOffset(Vector3 from, Vector3 to, float t)
        {
            Vector3 fromFlat = new Vector3(from.x, 0f, from.z);
            Vector3 toFlat = new Vector3(to.x, 0f, to.z);
            float fromLength = fromFlat.magnitude;
            float toLength = toFlat.magnitude;
            float length = Mathf.Lerp(fromLength, toLength, t);
            float height = Mathf.Lerp(from.y, to.y, t);
            if (fromLength < 1e-4f || toLength < 1e-4f)
                return Vector3.Lerp(from, to, t);

            float angle = Vector3.SignedAngle(fromFlat, toFlat, Vector3.up);
            Vector3 dir = Quaternion.AngleAxis(angle * t, Vector3.up) * (fromFlat / fromLength);
            return dir * length + Vector3.up * height;
        }

        /// <summary>1 フレームで測る候補の数（1 候補 ~0.45ms。12 個で ~5ms、1 周 ~10 フレーム）。</summary>
        const int MountCandidatesPerFrame = 12;

        int _searchCursor = -1;
        float _searchBestScore;
        Vector3 _searchBest;
        float _searchFallbackRank;
        Vector3 _searchFallback;
        bool _searchHasCurrent;
        bool _searchCurrentInvalid;

        /// <summary>候補の総数（側 × 振り × 距離 × 高さ。見下ろしの上限で弾くものも数える）。</summary>
        static int MountCandidateCount => 2 * FaceYaws.Length * FaceDistances.Length * FaceHeights.Length;

        void StartMountSearch(bool hasCurrent, bool currentInvalid)
        {
            _searchCursor = 0;
            _searchBestScore = float.MinValue;
            _searchBest = Vector3.zero;
            _searchFallbackRank = float.MinValue;
            _searchFallback = Vector3.zero;
            _searchHasCurrent = hasCurrent;
            _searchCurrentInvalid = currentInvalid;
        }

        /// <summary>
        /// 候補を <paramref name="budget"/> 個だけ測る。全部測り終えたら、切り替えるべきなら
        /// <see cref="_mountWanted"/> を更新する（判定は全部測ってから 1 度だけ）。
        /// </summary>
        void StepMountSearch(Transform head, Vector3 target, Vector3 flank, int budget, float currentScore)
        {
            int total = MountCandidateCount;
            if (_searchFallback == Vector3.zero)
                _searchFallback = flank * FaceDistances[0] + Vector3.up * FaceHeights[0];

            for (int n = 0; n < budget && _searchCursor < total; _searchCursor++)
            {
                int i = _searchCursor;
                int h = i % FaceHeights.Length; i /= FaceHeights.Length;
                int d = i % FaceDistances.Length; i /= FaceDistances.Length;
                int y = i % FaceYaws.Length; i /= FaceYaws.Length;
                int side = i;

                float elevation = Mathf.Atan2(FaceHeights[h], FaceDistances[d]) * Mathf.Rad2Deg;
                if (elevation > MountMaxElevation)
                    continue;
                n++;

                Vector3 sideFlank = side == 0 ? flank : -flank;
                // 脇の向きから、頭の方へ振る（± は側によらず頭の方向へ回すため符号を合わせる）。
                float yaw = FaceYaws[y] * (side == 0 ? 1f : -1f);
                Vector3 horizontal = Quaternion.AngleAxis(yaw, Vector3.up) * sideFlank;
                Vector3 candidate = horizontal * FaceDistances[d] + Vector3.up * FaceHeights[h];
                float score = ScoreMountShot(head, target, candidate, _searchHasCurrent ? _mountWanted : candidate,
                    out FaceVisibility.Result reading, out float lens);
                if (score > _searchBestScore)
                {
                    _searchBestScore = score;
                    _searchBest = candidate;
                }

                // 退避先: 遠くへ逃げず、顔が読める候補。同点なら近い方。
                // レンズ下限（敵の関節球が画面を覆う距離）を割らない候補を先に選び、
                // 同じ読みやすさならレンズに余裕がある方を選ぶ。読める候補は Visible が
                // ほぼ満点で並ぶので、「同点なら近い方」に任せると敵に一番近い画を選び、
                // 殴る動きで下限を割り続けた（DownRuntimeTests 実測 18 コマ）。
                float rank = (lens >= MountMinLens ? 100f : 0f) + FallbackRank(reading) * 10f
                             + Mathf.Min(lens, 1.5f);
                if (rank > _searchFallbackRank ||
                    (Mathf.Approximately(rank, _searchFallbackRank) &&
                     candidate.sqrMagnitude < _searchFallback.sqrMagnitude))
                {
                    _searchFallbackRank = rank;
                    _searchFallback = candidate;
                }
            }

            if (_searchCursor < total)
                return;
            _searchCursor = -1;

            bool anyValid = _searchBestScore > float.MinValue / 2f;
            Vector3 chosen = anyValid ? _searchBest : _searchFallback;
            _mountFallback = !anyValid;

            // ヒステリシス: いまの画より明らかに良いときだけ、固定時間が明けてから切り替える。
            // いまの画が使えなくなった（レンズ前に敵が入った等）ときは待たない。
            bool currentInvalid = _searchCurrentInvalid || currentScore <= float.MinValue / 2f;
            if (!_searchHasCurrent || currentInvalid ||
                (Time.time >= _mountLockUntil && _searchBestScore > currentScore + 0.15f))
            {
                if (!_searchHasCurrent || chosen != _mountWanted)
                    _mountLockUntil = Time.time + 0.6f;
                _mountWanted = chosen;
            }
        }

        /// <summary>画を要求し、その画の測定値を残す（テストと -mountlog が読む）。</summary>
        void ReportMountShot(Transform head, Vector3 target, Vector3 flank, bool cut)
        {
            Vector3 offset = BackOffFromLens(target, _shotOffset);
            _camera.RequestCinematicShot(target + offset, target, cut: cut);

            // 採用した画の測定値（テストと -mountlog が読む）。要求した画で測る（実カメラは
            // ApplyCinematicShot が同じフレームの後で置くので、ここで Camera.main を測ると 1 フレーム古い）。
            var shot = ProbeFace(head, _faceFigures, target, offset);
            MountFaceVisible = shot.Visible;
            MountFaceSize = FaceVisibility.ScreenFaceSize(FaceProbe(), head);
            _mountLens = FaceVisibility.LensClearance(target + offset, _faceFigures);

            if (LaunchOptions.Has("-mountlog") && Time.unscaledTime >= _nextMountFaceLog)
            {
                _nextMountFaceLog = Time.unscaledTime + 0.25f;
                Vector3 flat = new Vector3(_shotOffset.x, 0f, _shotOffset.z);
                float elevation = Mathf.Atan2(_shotOffset.y, Mathf.Max(0.001f, flat.magnitude)) * Mathf.Rad2Deg;
                float side = Mathf.Sign(Vector3.Dot(_shotOffset, flank));
                GameLog.Log($"[MountFace] visible={shot.Visible:F2} readable={(shot.Readable ? 1 : 0)} facing={shot.FacingAngle:F0} " +
                            $"size={MountFaceSize * 100f:F1}% lens={_mountLens:F2} dist={flat.magnitude:F2} " +
                            $"elev={elevation:F0} side={side:F0} fallback={(_mountFallback ? 1 : 0)} stage={Stage} " +
                            $"blocker={shot.Blocker ?? "none"}");
            }
        }

        /// <summary>
        /// 要求する画のレンズ前に敵の体が入ったら、カメラを少しずらす（毎フレーム）。
        /// 画の選び直しは <see cref="MountSearchInterval"/> おきで、パンチの振りかぶり
        /// （腕がレンズ側へ上がる）に追いつかない。選んだ時点で 0.65m あった余裕が、振りかぶりの
        /// 3 フレームで 0.41m まで詰まり、下限 0.5m を割った（DownRuntimeTests 実測、単体で 5 回中 2 回）。
        /// 後ろへ引くだけだと、遠めの画（2.9m）は距離の上限（3.0m）ですぐ詰まって 0.1m しか引けなかった。
        /// 後ろ・左右へ最大 0.35m ずらし、余裕が最も大きい位置を使う。
        /// </summary>
        Vector3 BackOffFromLens(Vector3 target, Vector3 offset)
        {
            const float margin = 0.06f;
            const float maxShift = 0.35f;
            const float maxLength = 3.0f;
            float length = offset.magnitude;
            if (length < 0.01f || _faceFigures == null)
                return offset;
            float best = FaceVisibility.LensClearance(target + offset, _faceFigures);
            if (best >= MountMinLens + margin)
                return offset;

            Vector3 back = offset / length;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            // 上へはずらさない。見下ろしが深くなり、頭から測った角度がテストの上限（60°）に届いた。
            Vector3[] dirs =
            {
                back, side, -side, (back + side).normalized, (back - side).normalized
            };

            Vector3 chosen = offset;
            for (float shift = 0.05f; shift <= maxShift + 1e-4f; shift += 0.05f)
            {
                for (int i = 0; i < dirs.Length; i++)
                {
                    Vector3 candidate = offset + dirs[i] * shift;
                    if (candidate.magnitude > maxLength)
                        continue;
                    // 地面へ潜らせない。
                    if (candidate.y < 0.3f)
                        continue;
                    float lens = FaceVisibility.LensClearance(target + candidate, _faceFigures);
                    if (lens > best)
                    {
                        best = lens;
                        chosen = candidate;
                    }
                }
                // 小さくずらして足りたら、それ以上は動かさない（画の揺れを最小にする）。
                if (best >= MountMinLens + margin)
                    break;
            }
            return chosen;
        }

        float ScoreMountShot(Transform head, Vector3 target, Vector3 candidate, Vector3 current)
            => ScoreMountShot(head, target, candidate, current, out _, out _);

        /// <summary>
        /// 使える画が無いときの退避先の順位（大きいほど良い）。
        /// 以前は Visible だけで選んでいたため、敵が覆いかぶさって全候補が落ちると
        /// 「遮るものの無い後頭部」（Visible 満点・表情は読めない）が選ばれていた
        /// （テストプレイ 2026-11-21 mount #14〜55: 頭頂の髪しか映らない）。
        /// 顔の向きを先に見て、次に可視率を見る。
        /// </summary>
        internal static float FallbackRank(FaceVisibility.Result reading)
        {
            if (reading.Readable)
                return 2f + reading.Visible;
            if (reading.FacingAngle <= FaceVisibility.FacingLimit)
                return 1f + reading.Visible;
            // 顔がこちらを向いていない画は、向きが近いほど良い（0〜1 の範囲に収める）。
            return 1f - Mathf.Clamp01(reading.FacingAngle / 180f);
        }

        /// <summary>
        /// マウントの画の得点。使えない画（レンズ前に敵・顔が小さすぎる）は <see cref="float.MinValue"/>。
        /// </summary>
        float ScoreMountShot(Transform head, Vector3 target, Vector3 candidate, Vector3 current,
            out FaceVisibility.Result reading, out float lens)
        {
            lens = FaceVisibility.LensClearance(target + candidate, _faceFigures);
            reading = ProbeFace(head, _faceFigures, target, candidate);
            if (lens < MountSelectLens)
                return float.MinValue;

            // 画は固定なので、この後に敵が覆いかぶさっても顔が隠れない位置を選ぶ。
            // 覆いかぶさった敵の頭・胴は、顔の少し上から立ち上がるカプセルで見立てる
            // （撮影 2026-11-22 mount #30/#45/#60: 脇から低く撮ると頭と胴で顔が隠れた）。
            if (LeanBlocksFace(head.position, TargetRig.Hips.position, target + candidate))
                return float.MinValue;

            float size = FaceVisibility.ScreenFaceSize(FaceProbe(), head);
            if (size < MountMinFaceSize)
                return float.MinValue;

            // 後頭部の画は Visible が満点でも表情が読めない。減点ではなく不採用にする。
            // （実測: mount #10〜 で敵の腕の下の後頭部が最高得点になっていた）。
            if (!reading.Readable)
                return float.MinValue;

            float score = reading.Visible
                          + (reading.InFrame >= 0.99f ? 0f : -0.5f);

            // レンズが下限に近い画（0.65〜0.95m）は少し減点。それより遠くても加点はしない。
            if (lens < 0.95f)
                score -= 0.3f * (0.95f - lens) / 0.3f;

            // 顔が大きいほど少しだけ得（最大 +0.2）。遠い画が同点で勝たないようにする。
            score += Mathf.Min(size, 0.10f) * 2f;

            // 顔の正面に近い画ほど得（最大 +0.4）。読める上限（60°）ぎりぎりの画は、殴られて
            // 首が振れるとすぐ読めなくなる。
            score += 0.4f * Mathf.Clamp01(1f - reading.FacingAngle / FaceVisibility.FacingLimit);

            float far = FaceDistances[FaceDistances.Length - 1];
            float near = FaceDistances[0];
            float horizontal = Mathf.Sqrt(candidate.x * candidate.x + candidate.z * candidate.z);
            score -= 0.35f * Mathf.InverseLerp(near, far, horizontal);

            // 反対側へ回る画は、明らかに良いときだけにする（側が毎フレーム入れ替わると画が揺れる）。
            Vector3 a = new Vector3(candidate.x, 0f, candidate.z);
            Vector3 b = new Vector3(current.x, 0f, current.z);
            if (Vector3.Dot(a, b) < 0f)
                score -= 0.25f;

            return score;
        }

        /// <summary>覆いかぶさった敵（顔の上 0.35〜1.2m、胴側へ 0.2m 寄る）がレンズと顔の間に入るか。</summary>
        static bool LeanBlocksFace(Vector3 face, Vector3 hips, Vector3 lens)
        {
            Vector3 toHips = Vector3.ProjectOnPlane(hips - face, Vector3.up);
            toHips = toHips.sqrMagnitude > 1e-6f ? toHips.normalized : Vector3.zero;
            Vector3 a = face + Vector3.up * 0.35f + toHips * 0.2f;
            Vector3 b = face + Vector3.up * 1.2f + toHips * 0.2f;
            // レンズ→顔の線分と、カプセル軸 a-b の最短距離。
            Vector3 d1 = face - lens, d2 = b - a, r = lens - a;
            float s1 = d1.sqrMagnitude, e = d2.sqrMagnitude, f = Vector3.Dot(d2, r);
            float c = Vector3.Dot(d1, r), bb = Vector3.Dot(d1, d2);
            float den = s1 * e - bb * bb;
            float s = den > 1e-6f ? Mathf.Clamp01((bb * f - c * e) / den) : 0f;
            float t = Mathf.Clamp01((bb * s + f) / e);
            s = Mathf.Clamp01((bb * t - c) / s1);
            float dist = ((lens + d1 * s) - (a + d2 * t)).magnitude;
            return dist < 0.28f;
        }

        Camera FaceProbe()
        {
            if (_faceProbe == null)
            {
                var go = new GameObject("Mount Face Probe");
                _faceProbe = go.AddComponent<Camera>();
                // 無効カメラは投影行列が更新されないため、描画しない有効カメラにする。
                _faceProbe.enabled = true;
                _faceProbe.cullingMask = 0;
                _faceProbe.clearFlags = CameraClearFlags.Nothing;
                _faceProbe.depth = -100f;
                _faceProbe.useOcclusionCulling = false;
            }
            var main = Camera.main;
            if (main != null)
            {
                _faceProbe.fieldOfView = main.fieldOfView;
                _faceProbe.nearClipPlane = main.nearClipPlane;
                _faceProbe.farClipPlane = main.farClipPlane;
            }
            _faceProbe.aspect = FaceVisibility.Aspect;
            return _faceProbe;
        }
        FaceVisibility.Result ProbeFace(Transform head, IReadOnlyList<EnemyFigure> figures, Vector3 target, Vector3 offset)
        {
            Camera probe = FaceProbe();
            Vector3 position = target + offset;
            probe.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            return FaceVisibility.Measure(probe, head, figures);
        }

        void FinishRoll()
        {
            _player.FinishGroundRoll();
            _animator?.FinishGroundRoll();
        }

        void End()
        {
            // 実際に跨っていた（いた）ときだけ、次の探索まで間を空ける。
            // ここで待ちを立てても、ResetKnockdown 側で消さないこと。
            if (Stage != Beat.Idle)
                _nextSearch = Time.time + RemountDelay;
            if (Stage == Beat.Roll && RollProgress >= 0.5f && _player.IsDown && !_player.IsDead)
                FinishRoll();
            if (Attacker != null && Attacker.IsAlive)
                Attacker.HoldFire(0.8f);
            Attacker = null;
            Stage = Beat.Idle;
            _punches = 0;
            _struggles = 0;
            _escaped = false;
            StrugglePulse = 0f;
            _reactionAge = float.PositiveInfinity;
            HasTakenPunch = false;
            ImpactSide = Vector3.zero;
            ImpactWasLeftPunch = false;
            _shotOffset = Vector3.zero;
            _holdValid = false;
            _holdFov = 0f;
            _holdBlocked = 0f;
            _holdTurned = 0f;
            _mountWanted = Vector3.zero;
            _searchCursor = -1;
            _driftLogged = false;
            _mountLockUntil = 0f;
            _resettling = false;
        }
    }
}
