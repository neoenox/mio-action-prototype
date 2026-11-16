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
        const float GrabSeconds = 0.65f;
        const float HaulSeconds = 1.25f;
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

            // 後頭部の髪を掴むので、掴む側は頭の向きで決める。
            Vector3 herHead = TargetRig.Head.position;
            GrabbedLeftSide = Vector3.Dot(herHead - hips, _flank) > 0f;

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
        }

        /// <summary>連打 1 回ぶん。規定回数で振りほどく（テストからも呼ぶ）。</summary>
        internal void Struggle()
        {
            if (Attacker == null || (Stage != Beat.Grab && Stage != Beat.Haul))
                return;
            StrugglePulse = 1f;
            if (++_struggles >= EscapeInputs)
                End(true);
        }

        /// <summary>彼女の腰の脇、敵の立つ位置（<paramref name="pull"/> だけ離れる）。足元の高さは敵のまま。</summary>
        Vector3 Spot(float pull)
        {
            // 頭側へ立つ。髪を掴む手が届き、彼女の顔も画に入る。
            Vector3 head = TargetRig.Head.position;
            Vector3 spot = head + _axis * 0.05f + _flank * (0.35f + pull);
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
                BeginStage(Beat.Haul, HaulSeconds);
            }
        }

        void TickHaul(float dt)
        {
            // 敵は体を起こしながら後ろへ反り、少し下がる。手首を引く手が肩の上まで上がる。
            float u = Progress;

            // 先に立ち上がりきると、まだ床近くにある頭へ腕が届かず手が離れる。
            // 彼女の持ち上がり（Lift）に合わせて、敵も腰を落としたまま起こす。
            float pull1 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.38f, u));
            float pull2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.92f, u));
            float sag = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.38f, 0.55f, u)) * 0.06f;
            Lift = Mathf.Clamp01(pull1 * 0.55f + pull2 * 0.55f - sag + 0.05f * Yank);

            Crouch = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, Lift));
            Lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.3f)) * 1.1f;
            Grip = 1f;
            ArmPull = Mathf.Lerp(0.60f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.08f, u)));

            // 2 回目の「ぐいっ」。1 回目は掴み終わり（TickGrab）で立てている。
            if (!_secondYank && u >= 0.50f)
            {
                _secondYank = true;
                Yank = 1f;
            }

            Follow(Spot(PullBack * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.3f))), dt, 26f);

            if (u >= 1f)
                BeginStage(Beat.Release, ReleaseSeconds);
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
                _animator?.BeginHaulDaze(0.8f);
                _grapple.Begin(enemy, kind);
                _grapple.HaulHandoff = true;
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
                GameLog.Log($"[Haul] escaped {enemy.name}");
            }
            else if (enemy.IsAlive)
                enemy.HoldFire(0.8f);

            // 途中まで持ち上がっていたら、そこから立ち上がりへ（床へ戻してから起きると二度手間に見える）。
            if (_player.IsDown && !_player.IsDead && _animator != null && (escaped || lifted))
                _animator.BeginQuickGetUp(0.70f);

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

            // 二人の関係軸（彼女 ↔ 敵 = 横方向）に対して横、つまり彼女の足の先（正面）から撮る。
            // 敵の反対側の脇から撮ると、引かれる腕が彼女の体の陰になって見えない（実測）。
            // 正面からなら、仰向けの顔・引き上げられて伸びた腕・引く敵が一つの画に入る。
            // 少しだけ敵の側へ振って、敵が彼女の後ろに重ならないようにする。
            Vector3 her = Vector3.Lerp(TargetRig.Hips.position, TargetRig.Head.position, 0.55f);
            Vector3 enemyChest = _figure != null ? _figure.HeadBone.position - Vector3.up * 0.35f
                : Attacker.transform.position + Vector3.up;
            bool prone = _player.DownIsForward;
            Vector3 target = prone
                ? Vector3.Lerp(TargetRig.Head.position, enemyChest, 0.4f)
                : Vector3.Lerp(her, enemyChest, 0.35f);
            Vector3 front = -_axis;
            // うつ伏せのとき、正面（足元）から撮ると彼女の背中と敵の体しか映らない。
            // 敵と反対の脇へ回り、横から髪を掴む手と体を見せる。
            Vector3 offset = prone
                ? -_flank * 2.4f + front * 0.7f + Vector3.up * (1.15f + 0.20f * Lift)
                : front * 2.5f + _flank * 0.55f + Vector3.up * (1.05f + 0.15f * Lift);
            _camera.RequestCinematicShot(target + offset, target, cut: _firstShot);
            _firstShot = false;
        }
    }
}
