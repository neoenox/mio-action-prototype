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
        float _elapsed;
        float _duration;
        float _downTime;
        float _nextSearch;
        float _side;
        bool _escaped;
        int _punches;
        int _struggles;
        float _reactionAge = float.PositiveInfinity;

        internal EnemyBrain Attacker { get; private set; }
        internal Beat Stage { get; private set; }
        internal float Progress => Stage == Beat.Approach
            ? Mathf.InverseLerp(0f, _approachDistance, _approachTravel)
            : _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 0f;
        internal bool BlocksGetUp => Attacker != null && Stage >= Beat.Mount;
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
        internal float StrugglePulse { get; private set; }
        internal Vector3 StrikeTarget { get; private set; }
        internal Vector3 StrikeSide => Attacker.transform.right * (LeftPunch ? -1f : 1f);
        internal Vector3 ImpactSide { get; private set; }
        internal bool ImpactWasLeftPunch { get; private set; }
        internal Vector3 ShotOffset => _shotOffset;
        internal Vector3 AttackerHead => _figure.HeadBone.position;
        internal bool HasTakenPunch { get; private set; }
        internal float HeadRecoil => ImpactWasLeftPunch
            ? Recoil(-0.012f, 0.058f, 0.24f)
            : Recoil(-0.018f, 0.036f, 0.22f);
        internal float TorsoRecoil => Recoil(0.010f, 0.073f, 0.28f);
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
        }

        internal void ResetKnockdown()
        {
            End();
            _downTime = 0f;
            _nextSearch = 0f;
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
                MoveTowardsPose(FeetSpot(), TargetRig.Head.position, Progress);

            if (Progress >= 1f && Stage != Beat.Strike && Stage != Beat.Roll)
                Advance();
        }

        internal void Struggle()
        {
            if (Stage < Beat.Mount || Stage >= Beat.Dismount || _player.DownIsForward)
                return;
            StrugglePulse = 1f;
            if (++_struggles >= EscapeInputs)
            {
                _escaped = true;
                Begin(Beat.Dismount, 0.40f);
            }
        }

        void FindAttacker()
        {
            float best = 3f * 3f;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
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

            _side = Vector3.Dot(Attacker.transform.position - TargetRig.Hips.position, transform.right) < 0f
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
                    End();
                    if (escaped)
                        _player.RequestGetUp();
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
            Vector3 target = TargetRig.Hips.position + axis * 0.09f + Vector3.up * 0.32f;
            _camera.RequestCinematicShot(target + flank * 1.85f + axis * 0.53f + Vector3.up * 1.18f, target);
        }

        void FrameMount()
        {
            if (!ResolveCamera())
                return;

            var head = TargetRig.Head;
            bool firstFrame = _shotOffset == Vector3.zero;
            if (firstFrame)
            {
                Vector3 towardHead = Vector3.ProjectOnPlane(head.position - TargetRig.Hips.position, Vector3.up).normalized;
                Vector3 flank = Vector3.Cross(Vector3.up, towardHead);
                flank *= Vector3.Dot(head.forward, flank) < 0f ? -1f : 1f;
                _shotOffset = flank * 0.71f + towardHead * 0.31f + Vector3.up * 0.88f;
            }

            Vector3 target = Vector3.Lerp(head.position, TargetRig.Hips.position, 0.31f) + Vector3.up * 0.19f;
            _camera.RequestCinematicShot(target + _shotOffset, target, cut: firstFrame);
        }

        void FinishRoll()
        {
            _player.FinishGroundRoll();
            _animator?.FinishGroundRoll();
        }

        void End()
        {
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
        }
    }
}
