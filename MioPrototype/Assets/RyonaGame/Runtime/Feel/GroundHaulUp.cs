using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ダウンした彼女を、敵が手首を掴んで引きずり起こし、背後から羽交い締めへつなぐ。
    ///
    /// 流れ: 腰の脇へ歩いて寄る（Approach）→ かがんで手首と服の裾を掴む（Grab）→
    ///       体を起こしながら手首を上へ引き上げる（Haul）→ 背後へ回る（Settle）→
    ///       羽交い締め（<see cref="GrappleKind.Back"/>）。
    ///
    /// **彼女は自分では起きない。** 引かれた腕がピンと張って敵の手へ伸び、上体はその腕に
    /// 引っ張られて持ち上がる（床を押す手も、自分で踏ん張る脚も無い）。
    /// 姿勢は <see cref="CharacterAnimator"/> の引き起こし姿勢（<see cref="Lift"/> と
    /// <see cref="ArmPull"/> を読む）、敵の腕は <see cref="HaulUpPose"/> が作る。
    ///
    /// 掴んでから引き上げの間は、連打 <see cref="EscapeInputs"/> 回で振りほどける（敵はよろける）。
    /// 寝技（<see cref="GroundMountAttack"/>）と同じ「ダウン中に敵が一人を選ぶ」枠で動き、
    /// 通常の敵 AI はこの間は譲る（<see cref="GroundMountAttack.Owns"/>）。
    /// </summary>
    [DefaultExecutionOrder(21100)]
    internal sealed class GroundHaulUp : MonoBehaviour
    {
        internal enum Beat { Idle, Approach, Grab, Haul, Release, Settle }

        const int EscapeInputs = 6;
        const float GrabSeconds = 0.70f;
        const float HaulSeconds = 2.20f;
        const float ReleaseSeconds = 0.50f;
        const float SettleSeconds = 0.40f;

        /// <summary>彼女の腰の脇へ立つ距離（m）。</summary>
        const float FlankDistance = 0.15f;

        /// <summary>引き上げの間に、敵が彼女から下がる距離（m）。腕が張って見える。</summary>
        const float PullBack = 0.0f;

        /// <summary>寝技の代わりに引き起こしを選ぶ割合。</summary>
        internal static float Chance = 0.35f;

        /// <summary>起動引数 <c>-haultest</c>。必ず引き起こしを選ぶ（撮影・確認用）。</summary>
        internal static bool Forced;

        PlayerStats _player;
        PlayerFace _face;
        GroundMountAttack _mount;
        CharacterAnimator _animator;
        ThirdPersonCamera _camera;
        PlayerGrapple _grapple;
        EnemyFigure _figure;

        Vector3 _fromPosition;
        Quaternion _fromRotation;
        Vector3 _approachSpot;
        float _approachDistance;
        float _approachTravel;
        Vector3 _flank;
        Vector3 _axis;
        float _elapsed;
        float _duration;
        int _struggles;
        bool _firstShot = true;
        bool _secondYank;

        internal EnemyBrain Attacker { get; private set; }
        internal Beat Stage { get; private set; }
        internal bool Active => Attacker != null;
        internal bool IsProne => _player != null && _player.DownIsForward;

        /// <summary>引き起こしで「肩の下から上体を起こす(0)」→「抱きかかえて立たせる(1)」の重み。</summary>
        internal float EmbraceWeight =>
            Stage == Beat.Haul ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.62f, Progress))
            : Stage == Beat.Settle ? 1f : 0f;

        /// <summary>掴んでから羽交い締めへ移るまでは、自分では起き上がれない。</summary>
        internal bool BlocksGetUp => Active && Stage >= Beat.Grab;

        /// <summary>彼女の姿勢を引き起こしで作っている最中か。</summary>
        internal bool PosesHer => Active && Stage >= Beat.Grab;

        internal float StrugglePulse { get; private set; }

        /// <summary>敵の姿勢へ渡す。かがみ具合（0..1）と、引くときの反り（0..1）。</summary>
        internal float Crouch { get; private set; }
        internal float Lean { get; private set; }

        /// <summary>手首と裾を掴んでいる度合い（0..1）。敵の腕 IK の重み。</summary>
        internal float Grip { get; private set; }

        /// <summary>彼女の体が持ち上がった量（0 = 寝たまま、1 = 吊られて立った）。</summary>
        internal float Lift { get; private set; }

        /// <summary>彼女の掴まれた腕が敵の手へ引かれる重み（0..1）。</summary>
        internal float ArmPull { get; private set; }

        /// <summary>引き上げの瞬間の「ぐいっ」（0..1、すぐ減衰）。体が一拍遅れて付いてくる。</summary>
        internal float Yank { get; private set; }

        /// <summary>掴まれたのは彼女の左手首か。</summary>
        internal bool GrabbedLeftSide { get; private set; }

        /// <summary>手首を掴むのは敵の左腕か（もう片方の腕で服の裾を掴む）。</summary>
        internal bool EnemyLeftArm { get; private set; }

        /// <summary>
        /// 彼女の手首を引き寄せる先（ワールド）。敵の掴む側の肩の少し前・上。
        /// 引き上げが進むほど高くなる。彼女の腕はここへ向けて張る。
        /// </summary>
        internal Vector3 PullAnchor
        {
            get
            {
                if (_figure == null || Attacker == null)
                    return TargetRig != null ? TargetRig.Hips.position : transform.position;
                Transform shoulder = _figure.ShoulderBone(EnemyLeftArm);
                Vector3 toHer = TargetRig.Hips.position - Attacker.transform.position;
                toHer.y = 0f;
                toHer = toHer.sqrMagnitude > 1e-4f ? toHer.normalized : Attacker.transform.forward;
                // 引く手は早めに上がる（体が付いてくるより先に）。
                float raise = Stage == Beat.Haul ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.55f, Progress))
                    : Stage == Beat.Settle ? 1f : 0f;
                return shoulder.position + toHer * 0.14f + Vector3.up * (0.22f * raise - 0.10f);
            }
        }

        /// <summary>髪を掴んでいる敵の手（ワールド）。</summary>
        internal Vector3 GrabHand
        {
            get
            {
                if (_figure != null)
                {
                    Transform wrist = _figure.WristBone(EnemyLeftArm);
                    if (wrist != null)
                        return wrist.position;
                }
                return TargetRig != null
                    ? TargetRig.Head.position + Vector3.up * 0.15f
                    : transform.position + Vector3.up;
            }
        }

        /// <summary>手首を掴む敵の肩（ワールド）。</summary>
        internal Vector3 EnemyShoulder
        {
            get
            {
                Transform s = _figure != null ? _figure.ShoulderBone(EnemyLeftArm) : null;
                return s != null ? s.position : PullAnchor;
            }
        }

        /// <summary>手首を掴む敵の腕の長さ（肩〜手首、m）。二人の手が出会う点を決めるのに使う。</summary>
        internal float EnemyArmReach
        {
            get
            {
                if (_figure == null)
                    return 0.6f;
                Transform s = _figure.ShoulderBone(EnemyLeftArm);
                Transform e = _figure.ElbowBone(EnemyLeftArm);
                Transform w = _figure.WristBone(EnemyLeftArm);
                if (s == null || e == null || w == null)
                    return 0.6f;
                return (e.position - s.position).magnitude + (w.position - e.position).magnitude;
            }
        }

        internal ProceduralRig TargetRig
        {
            get
            {
                if (_animator == null)
                    _animator = GetComponentInChildren<CharacterAnimator>();
                return _animator != null ? _animator.Rig : null;
            }
        }
        internal float Progress => _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 0f;
        internal float EscapeProgress => _struggles / (float)EscapeInputs;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Forced = LaunchOptions.Has("-haultest");
        }

        void Awake()
        {
            _player = GetComponent<PlayerStats>();
            _face = GetComponent<PlayerFace>();
            _mount = GetComponent<GroundMountAttack>();
            _grapple = GetComponent<PlayerGrapple>();
        }

        /// <summary>この敵が彼女を引き起こすか（寝技の代わりに）。仰向けもうつ伏せも。</summary>
        internal bool ShouldHaul(EnemyBrain enemy)
        {
            if (Active || enemy == null)
                return false;

            // 遠くから歩いて寄ってくる引き起こしは狙わない。近くにいる敵だけが、
            // 倒れた彼女のそばで抱え上げる。
            if (!Forced && Vector3.Distance(enemy.transform.position, _player.transform.position) > 2.2f)
                return false;

            return Forced || Random.value < Chance;
        }

        internal void Begin(EnemyBrain enemy)
        {
            if (enemy == null || TargetRig == null || !TargetRig.Ready)
                return;

            Attacker = enemy;
            _figure = enemy.GetComponent<EnemyFigure>();
            _struggles = 0;
            _firstShot = true;
            _secondYank = false;
            Crouch = Lean = Grip = Lift = ArmPull = Yank = 0f;

            Vector3 hips = TargetRig.Hips.position;
            Vector3 head = TargetRig.Head.position;
            _axis = Vector3.ProjectOnPlane(head - hips, Vector3.up).normalized;
            if (_axis.sqrMagnitude < 0.01f)
                _axis = -transform.forward;

            // 敵のいる側の腰の脇へ立つ。
            Vector3 side = Vector3.Cross(Vector3.up, _axis);
            float sign = Vector3.Dot(enemy.transform.position - hips, side) < 0f ? -1f : 1f;
            _flank = side * sign;
            _approachSpot = Spot(0f);
            _approachDistance = Vector3.Distance(enemy.transform.position, _approachSpot);
            _approachTravel = 0f;

            // 敵に近い側の腕を掴む（仰向けでは左右の目安としても使う）。
            GrabbedLeftSide = Vector3.Distance(enemy.transform.position, TargetRig.LUpperArm.position)
                <= Vector3.Distance(enemy.transform.position, TargetRig.RUpperArm.position);

            BeginStage(Beat.Approach, 0f);
            GameLog.Log($"[Haul] begin {enemy.name} distance={_approachDistance:F2} herLeft={GrabbedLeftSide}");
        }

        void BeginStage(Beat stage, float duration)
        {
            Stage = stage;
            _elapsed = 0f;
            _duration = duration;
            _fromPosition = Attacker.transform.position;
            _fromRotation = Attacker.transform.rotation;
            GameLog.Log($"[Haul] stage={stage} t={Time.time:F2}");
        }

        void Update()
        {
            if (Attacker == null)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (_player.IsDead || !Attacker.IsAlive || TargetRig == null || !TargetRig.Ready || !_player.IsDown ||
                (Stage == Beat.Approach && (_player.GetUpRequested || _player.IsGettingUp)))
            {
                End(false);
                return;
            }

            StrugglePulse = Mathf.MoveTowards(StrugglePulse, 0f, dt * 4f);
            Yank = Mathf.MoveTowards(Yank, 0f, dt * 5.5f);
            _elapsed += dt;

            // 髪を引かれている間は痛み顔。離した直後も少しだけ余韻を残す。
            if (_face != null)
            {
                if (Stage == Beat.Haul)
                    _face.Apply(0.85f, 0.8f);
                else if (Stage == Beat.Release)
                    _face.Apply(0.45f, 0.9f);
            }

            // 掴んでから引き上げの間は、連打で振りほどける。
            if ((Stage == Beat.Grab || Stage == Beat.Haul) && InputReader.StrugglePressed())
            {
                Struggle();
                if (Attacker == null)
                    return;
            }

            switch (Stage)
            {
                case Beat.Approach: TickApproach(dt); break;
                case Beat.Grab: TickGrab(dt); break;
                case Beat.Haul: TickHaul(dt); break;
                case Beat.Release: TickRelease(dt); break;
                case Beat.Settle: TickSettle(); break;
            }

            RequestShot();
            LogFaceVisibility();
        }

        /// <summary>連打 1 回ぶん。規定回数で振りほどく（テストからも呼ぶ）。</summary>
        internal void Struggle()
        {
            if (Attacker == null || (Stage != Beat.Grab && Stage != Beat.Haul))
                return;
            StrugglePulse = 1f;
            ProceduralSfx.Instance?.Play(SfxId.Tell, 0.45f);
            if (++_struggles >= EscapeInputs)
                End(true);
        }

        /// <summary>彼女の腰の脇、敵の立つ位置（<paramref name="pull"/> だけ離れる）。足元の高さは敵のまま。</summary>
        Vector3 Spot(float pull)
        {
            // 頭側へ立つ。髪を掴む手が届き、彼女の顔も画に入る。
            Vector3 head = TargetRig.Head.position;
            Vector3 spot = head + _axis * 0.12f + _flank * 0.45f + _flank * pull;
            spot.y = Attacker.transform.position.y;
            return spot;
        }

        void TickApproach(float dt)
        {
            float speed = Attacker.MoveSpeed * Attacker.State.MoveSpeedMultiplier;
            if (speed <= 0f)
            {
                End(false);
                return;
            }

            _approachTravel = Mathf.Min(_approachTravel + speed * dt, _approachDistance);
            float t = _approachDistance > 0.001f ? _approachTravel / _approachDistance : 1f;
            Vector3 position = Vector3.Lerp(_fromPosition, _approachSpot, t);
            Vector3 toHer = -_flank;
            Attacker.transform.SetPositionAndRotation(position,
                Quaternion.Slerp(_fromRotation, Quaternion.LookRotation(toHer, Vector3.up),
                    Mathf.SmoothStep(0f, 1f, t)));

            if (_approachTravel >= _approachDistance)
            {
                // 後頭部に近い側の腕で髪を掴む。
                Vector3 head = TargetRig.Head.position;
                EnemyLeftArm = Vector3.Dot(head - Attacker.transform.position, Attacker.transform.right) < 0f;
                BeginStage(Beat.Grab, GrabSeconds);
            }
        }

        void TickGrab(float dt)
        {
            Follow(Spot(0f), dt);

            // かがんで手を伸ばし（0〜0.6）、手首を掴む（0.6〜）。掴んだ瞬間に腕が少し浮く。
            Crouch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.55f, Progress));
            Grip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.70f, Progress));
            // 掴んだら手首が床から浮く（敵の手へ半分ほど引き寄せられる）。床のままだと
            // しゃがんだ敵の手がわずかに届かず、引き上げの出だしで手が離れて見える（実測 0.13m）。
            ArmPull = 0.60f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, Progress));
            Lean = 0f;
            Lift = 0f;
            if (Progress >= 1f)
            {
                Yank = 1f;
                TriggerYankFeedback(false);
                BeginStage(Beat.Haul, HaulSeconds);
            }
        }

        void TickHaul(float dt)
        {
            // 敵は体を起こしながら後ろへ反り、少し下がる。2段目で抱え直してから無理やり立たせる。
            float u = Progress;

            // 先に立ち上がりきると、まだ床近くにある頭へ腕が届かず手が離れる。
            // 彼女の持ち上がり（Lift）に合わせて、敵も腰を落としたまま起こす。
            float pull1 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.34f, u));
            float sag = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.34f, 0.58f, u)) * 0.08f;
            float pull2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.94f, u));
            Lift = Mathf.Clamp01(pull1 * 0.52f + pull2 * 0.52f - sag + 0.05f * Yank);

            Crouch = Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, Lift)) + sag * 1.6f);
            Lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.25f)) * 1.1f;
            Grip = 1f;
            ArmPull = Mathf.Lerp(0.60f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.08f, u)));

            // 2 回目の「ぐいっ」（抱え直してから立ち上げに入る瞬間）。
            if (!_secondYank && u >= 0.58f)
            {
                _secondYank = true;
                Yank = 1f;
                TriggerYankFeedback(true);
            }

            Vector3 targetSpot = Spot(PullBack * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.3f)));
            Vector3 herCenter = TargetRig != null ? TargetRig.Hips.position : transform.position;
            Vector3 hugSpot = IsProne
                ? herCenter + _axis * 0.50f + _flank * 0.18f
                : herCenter - transform.forward * 0.46f + _flank * 0.12f;
            hugSpot.y = Attacker.transform.position.y;
            targetSpot = Vector3.Lerp(targetSpot, hugSpot, EmbraceWeight);
            Follow(targetSpot, dt, 26f);

            if (u >= 1f)
                BeginStage(Beat.Settle, SettleSeconds);
        }

        void TickRelease(float dt)
        {
            // 彼女がほぼ立ち上がったら髪を離す。彼女は少し前かがみに崩れる。
            float u = Progress;
            Crouch = 0f;
            Lean = 0f;
            Lift = 1f;
            Grip = 1f - Mathf.SmoothStep(0f, 1f, u);
            ArmPull = 0f;

            Follow(Spot(0f), dt, 12f);

            if (u >= 1f)
                BeginStage(Beat.Settle, SettleSeconds);
        }

        void TickSettle()
        {
            // 離したあと、仰向けなら背後へ、うつ伏せなら前方へ回って拘束位置へ。
            bool prone = _player.DownIsForward;
            Vector3 her = transform.position;
            Vector3 standDir = prone ? transform.forward : -transform.forward;
            Vector3 holdSpot = her + standDir * CombatConstants.BackGrabStandoff;
            holdSpot.y = _fromPosition.y;
            float t = Mathf.SmoothStep(0f, 1f, Progress);
            Quaternion facing = Quaternion.LookRotation(
                prone ? -transform.forward : transform.forward, Vector3.up);
            Attacker.transform.SetPositionAndRotation(Vector3.Lerp(_fromPosition, holdSpot, t),
                Quaternion.Slerp(_fromRotation, facing, t));
            Crouch = 0f;
            Lean = 0f;
            Lift = 1f;
            Grip = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, Progress));
            ArmPull = Grip;

            if (Progress >= 1f)
                Finish();
        }

        void Follow(Vector3 spot, float dt, float sharpness = 12f)
        {
            Attacker.transform.position = Vector3.Lerp(Attacker.transform.position, spot, 1f - Mathf.Exp(-sharpness * dt));
            Vector3 toHer = TargetRig.Hips.position - Attacker.transform.position;
            toHer.y = 0f;
            if (toHer.sqrMagnitude > 0.001f)
                Attacker.transform.rotation = Quaternion.Slerp(Attacker.transform.rotation,
                    Quaternion.LookRotation(toHer.normalized, Vector3.up), 1f - Mathf.Exp(-10f * dt));
        }

        /// <summary>引き起こしが終わって、羽交い締めへつなぐ。</summary>
        void Finish()
        {
            var enemy = Attacker;
            bool prone = _player.DownIsForward;
            ResetState();
            _player.EndDownByHaul();
            if (_grapple != null && !_grapple.IsGrappled && enemy != null && enemy.IsAlive)
            {
                // 仰向けは背後拘束、うつ伏せは前方拘束。
                // Begin（BeginContact ではなく）で直接拘束へ入る。ハンドオフの「手を離して
                // 受け直す」姿勢を挟むと、髪から離した手がもう一度頭へ行くように見える。
                GrappleKind kind = prone ? GrappleKind.Pin : GrappleKind.Back;
                _grapple.Begin(enemy, kind);
                enemy.TakeOverHold(float.PositiveInfinity);
                GameLog.Log($"[Haul] hand off to {kind} {enemy.name}");
            }
        }

        /// <summary>途中で終える。<paramref name="escaped"/> は彼女が連打で振りほどいたとき。</summary>
        internal void End(bool escaped)
        {
            var enemy = Attacker;
            bool lifted = Lift > 0.05f || Stage == Beat.Haul;
            ResetState();
            if (enemy == null)
                return;

            if (escaped)
            {
                enemy.ForceStagger(CombatConstants.GrappleEscapeStun);
                ProceduralSfx.Instance?.Play(SfxId.Swing, 0.85f);
                FindFirstObjectByType<CameraShake>()?.Shake(0.08f, 0.18f);
                GameLog.Log($"[Haul] escaped {enemy.name}");
            }
            else if (enemy.IsAlive)
                enemy.HoldFire(0.8f);

            // 途中まで持ち上がっていたら、そこから立ち上がりへ（床へ戻してから起きると二度手間に見える）。
            if (_player.IsDown && !_player.IsDead && _animator != null && (escaped || lifted))
            {
                _animator.BeginQuickGetUp(escaped ? 0.50f : 0.70f);
                if (escaped)
                    _animator.PreviewShove(0.75f);
            }

            _mount?.DelayNextSearch(2.5f);
        }

        void ResetState()
        {
            Attacker = null;
            _figure = null;
            Stage = Beat.Idle;
            Crouch = Lean = Grip = Lift = ArmPull = Yank = 0f;
            StrugglePulse = 0f;
            _struggles = 0;
            _firstShot = true;
        }

        /// <summary>
        /// カメラは Update で頼む。カメラ（LateUpdate）より後に頼むと次のフレームに反映され、
        /// 羽交い締めへ移ったコマ（姿勢が切り替わるコマ）が 1 枚だけ引き起こしの画角で見えてしまう（実測）。
        /// Update で頼めば、引き起こしが終わったフレームでちょうど通常のカメラへ戻る。
        /// </summary>
        void RequestShot()
        {
            if (Attacker == null || TargetRig == null || Stage < Beat.Grab)
                return;

            if (_camera == null && Camera.main != null)
                _camera = Camera.main.GetComponent<ThirdPersonCamera>();
            if (_camera == null)
                return;

            // 彼女の顔が映ることが前提（AGENTS.md）。敵は彼女の頭側の脇で屈んで抱える。
            // 敵と同じ側から撮ると、抱える敵の頭・肩が顔を覆う。
            // そこで敵と反対の斜め前（彼女の足の先の方向）から撮る。敵は彼女の体の向こうになる。
            //   仰向け: 足元の斜め上から、顔を見下ろす（顔は真上を向いているので、低いと読めない）。
            //           引き起こされて立つほど、目の高さへ下りて正面へ回る（立った彼女の顔が正面から読める）。
            //   うつ伏せ: 顔は床の横を向く。高めの斜め前から、横顔と持ち上がる肩を見せる。
            // 構図は引き上げの進み（Lift）で連続に変わる。段ごとに別のカメラへ切り替えない。
            Vector3 enemyChest = _figure != null ? _figure.HeadBone.position - Vector3.up * 0.35f
                : Attacker.transform.position + Vector3.up;
            bool prone = _player.DownIsForward;
            float lift = Mathf.SmoothStep(0f, 1f, Lift);
            float grabIn = Stage == Beat.Grab ? Mathf.SmoothStep(0f, 1f, Progress) : 1f;
            Vector3 front = -_axis;
            Vector3 face = TargetRig.Head.position;
            Vector3 chest = Vector3.Lerp(TargetRig.Hips.position, face, 0.8f);

            // 仰向け: 足元の斜め上（敵と反対の脇寄り）から顔を見下ろす。立つほど正面へ回り、目の高さへ下りる。
            //   敵は彼女の頭の脇で屈んで覆いかぶさるので、敵と同じ側・低い位置からだと顔が隠れる
            //   （実測: 顔が見えるコマの割合 62% → 97%、顔が読める割合 50% → 97%）。
            // うつ伏せ: 敵が頭の真上に跨るので、どの角度でも顔は隠れやすい（実測 78〜85%、読めるコマ 0%）。
            //   従来どおり、敵と反対の脇から低めに撮る。
            Vector3 target;
            Vector3 offset;
            if (prone)
            {
                target = Vector3.Lerp(face, enemyChest, 0.4f);
                offset = -_flank * 2.15f + front * 0.65f + Vector3.up * (1.05f + 0.12f * Lift);
            }
            else
            {
                target = Vector3.Lerp(Vector3.Lerp(face, chest, 0.35f), enemyChest, 0.20f + 0.10f * lift);
                offset = -_flank * Mathf.Lerp(0.85f, 0.35f, lift)
                    + front * Mathf.Lerp(1.45f, 1.95f, lift)
                    + Vector3.up * Mathf.Lerp(1.50f, 1.10f, lift);
            }

            // 掴む瞬間にぐっと寄る（出だしだけ少し遠く、掴んだら本来の距離）。
            offset *= Mathf.Lerp(1.12f, 1.0f, grabIn);
            _camera.RequestCinematicShot(target + offset, target, cut: _firstShot);
            _firstShot = false;
        }

        float _faceLogAt;

        /// <summary>引き起こし中の顔の見え方を 0.25 秒ごとに出す（<c>-haullog</c>）。</summary>
        void LogFaceVisibility()
        {
            if (!LaunchOptions.Has("-haullog") || Time.unscaledTime < _faceLogAt)
                return;
            _faceLogAt = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam == null || TargetRig == null || TargetRig.Head == null)
                return;
            var enemies = new System.Collections.Generic.List<EnemyFigure>(
                FindObjectsByType<EnemyFigure>(FindObjectsSortMode.None));
            var r = FaceVisibility.Measure(cam, TargetRig.Head, enemies);
            GameLog.Log($"[HaulFace] prone={(_player.DownIsForward ? 1 : 0)} stage={Stage} lift={Lift:F2} visible={r.Visible:F2} " +
                $"facing={r.FacingAngle:F0} readable={r.Readable} blocker={r.Blocker ?? "-"}");
        }

        void TriggerYankFeedback(bool heavy)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
            {
                sfx.Play(SfxId.Tell, heavy ? 0.85f : 0.65f);
                sfx.Play(SfxId.HurtSmall, heavy ? 0.80f : 0.55f);
            }
            FindFirstObjectByType<CameraShake>()?.Shake(heavy ? 0.065f : 0.045f, 0.16f);
        }
    }
}
