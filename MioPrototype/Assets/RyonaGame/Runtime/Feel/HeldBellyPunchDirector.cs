using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Game
{
    /// <summary>
    /// 後ろ拘束中の「腹パン」演出。
    ///
    /// 流れ:
    /// <list type="number">
    /// <item>後ろ拘束が始まったら <see cref="ThrowDelaySeconds"/> 秒待つ</item>
    /// <item>正面にいる別の敵が投げを仕掛け、成功したら連撃を開始する</item>
    /// <item>投げはダメージではなく、彼女を腹パン体勢（腹を差し出す）へ移す演出として見せる</item>
    /// <item>投げは打数に含めず、<see cref="ComboHits"/> 発のパンチを数える</item>
    /// </list>
    ///
    /// 後ろ拘束は時間切れで解けないので、プレイヤーは
    /// 「連打で逃げる」か「腹パンを 5 発食らって解放される」かを選ぶことになる。
    /// 判定（<see cref="PlayerStats.ReceiveHit"/>）には干渉せず、イベントを見て演出だけを足す。
    /// </summary>
    [AddComponentMenu("Ryona/Held Belly Punch Director")]
    public sealed partial class HeldBellyPunchDirector : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("後ろ拘束が始まってから、正面の敵に投げを指示するまでの秒数")]
        public float ThrowDelaySeconds = 1.6f;

        [Tooltip("1 回の連撃で食らうパンチの回数")]
        public int ComboHits = 5;

        [Range(0f, 1f)]
        [Tooltip("連撃が顔面パンチになる確率。0 で腹パンのみ、1 で顔面のみ")]
        public float FacePunchChance = 0.5f;

        [Tooltip("5 発の速さの全体つまみ。間隔そのものは段の表（BellyGapBeats / FaceGapBeats）が持つ")]
        public float ComboIntervalSeconds = 0.68f;

        [Tooltip("1 発ごとの被弾姿勢の長さ")]
        public float HitPoseSeconds = 0.80f;

        [Tooltip("腹に当ててから拳を引き切るまでの秒数")]
        public float FistHoldSeconds = 0.24f;

        [Tooltip("最後の一発のあと、グロッキー脱力を見せる秒数（この間にカメラが決めの全身へ引く）")]
        public float ComboTailSeconds = 0.35f;

        [Tooltip("グロッキー後に拘束役が脇を引き上げて晒す秒数（持ち上がりきってからトドメの振りかぶりに入る）")]
        public float FinisherHoistSeconds = 0.55f;

        [Tooltip("トドメの一撃を当ててから拘束を離して崩れ落ちるまでの余韻秒数")]
        public float CollapseHoldSeconds = 0.46f;

        [Tooltip("カメラが通常に戻ってから引き渡しを試すまでの秒数")]
        public float HandOffDelaySeconds = 1f;

        [Tooltip("演出後に他の敵が追撃を控える秒数")]
        public float ReleaseGraceSeconds = 0.6f;

        [Tooltip("腹パンが外れた／中断されたときに、次を試すまでの秒数")]
        public float RetrySeconds = 0.6f;

        [Tooltip("5 発を撃ち終えてから、次の 5 発を始めるまでの秒数（拘束は続く）")]
        public float RestartCooldownSeconds = 3.0f;

        [Tooltip("1 回の拘束で腹パンを指示する最大試行回数")]
        public int MaxAttempts = 8;

        [Header("Camera")]
        [Tooltip("腹パン中のカメラ距離倍率（1 未満で寄る）。0.30 で約 1.6m（基本距離 5.2m × 0.30）")]
        public float CameraDistanceScale = 0.27f;

        [Tooltip("腹パン中のピボット上下（正で上がる）。脚を切って腹と顔を入れる高さ")]
        public float CameraPivotOffset = 0.34f;

        [Tooltip("腹パン中の俯角オフセット（負で見上げる）")]
        public float CameraPitchOffset = -4f;

        [Tooltip("腹パン中のヨーオフセット（負で彼女の正面側へ回り込み、顔が見える）。殴る腕が体の奥に隠れない角度にする")]
        public float CameraYawOffset = -32f;

        [Header("Camera (face)")]
        [Tooltip("顔面パンチ中のカメラ距離倍率（1 未満で寄る）")]
        public float FaceCameraDistanceScale = 0.22f;

        [Tooltip("顔面パンチ中のピボット上下。顔と拳・肩元が同じ画に入る高さ")]
        public float FaceCameraPivotOffset = 0.46f;

        [Tooltip("顔面パンチ中の俯角オフセット。大きいほど下へ向き、顔が画面の上寄りになって胸元と拳が入る（-3 で見上げ気味、9 で大きく見下ろす）")]
        public float FaceCameraPitchOffset = 1.5f;

        [Tooltip("顔面パンチ中のヨーオフセット。腹パンと同程度に回り込んで、彼女の顔と拳を同時に見せる")]
        public float FaceCameraYawOffset = -36f;

        [Header("Camera (finisher)")]
        [Tooltip("決め（抱え上げ）で引く距離倍率。基本距離 5.2m × この値。0.42 で約 2.2m（全身と顔が入る）")]
        public float FinisherCameraDistanceScale = 0.42f;

        [Tooltip("決めの注視点。コンボ中の注視点からの差分（負で下＝浮いた足が入る）")]
        public float FinisherCameraPivotDelta = -0.12f;

        [Tooltip("決めの俯角。コンボ中からの差分（負で見上げ）")]
        public float FinisherCameraPitchDelta = -6f;

        [Header("Aim")]
        [Tooltip("腹パン役を立たせる、プレイヤーの腹からの前方距離（m）。離して踏み込ませると拳の移動が見える")]
        public float AttackerDistance = 0.85f;

        [Tooltip("顔面パンチ役を立たせる、プレイヤーからの前方距離（m）。拳を振り抜く弧が見える距離にする")]
        public float FaceAttackerDistance = 1.05f;

        [Tooltip("（未使用）ベルトでは敵の z が毎フレーム彼女に合わせられるため、側方オフセットは効かない")]
        public float AttackerSideOffset = 0.68f;

        [Tooltip("この距離より遠い敵は腹パン役に選ばない（m）。遠すぎると位置合わせがワープに見える")]
        public float AttackerSearchRange = 2.5f;

        /// <summary>
        /// 演出の段階。フラグの組み合わせではなく 1 つの値で持つ（不正な組み合わせを作らない）。
        /// </summary>
        public enum Phase
        {
            /// <summary>何も待っていない（拘束中でない・演出が終わった）。</summary>
            Idle = 0,

            /// <summary>正面の敵に投げを発注する待ち。クールタイム中もここ。</summary>
            WaitingThrow = 1,

            /// <summary>投げが決まり、腹パンを数えている。</summary>
            Combo = 2,

            /// <summary>コンボ後。カメラが戻ってから引き渡すまでの待ち。</summary>
            HandOffWait = 3,

            /// <summary>グロッキー後に引き起こして晒し、トドメの一撃を振り抜く。</summary>
            Finisher = 4,

            /// <summary>トドメ命中後、反応を見せ切ってから拘束を離して崩れ落ちる。</summary>
            Collapse = 5,
        }

        /// <summary>いまの段階（テスト・デバッグ表示用）。</summary>
        public Phase CurrentPhase => _phase;

        /// <summary>5 発の連撃中か（投げが決まってからトドメ／引き渡しへ移るまで）。</summary>
        public bool IsPerforming => _phase == Phase.Combo;
        public bool IsFinisherActive => _phase == Phase.Finisher || _phase == Phase.Collapse;
        public bool IsCameraActive => _phase == Phase.Combo || _phase == Phase.Finisher || _phase == Phase.Collapse;
        internal bool IsBodySequence => IsCameraActive && _comboZone == PunchZone.Body;
        internal bool IsFaceSequence => IsCameraActive && _comboZone == PunchZone.Face;
        internal bool IsFaceCombo => IsPerforming && _comboZone == PunchZone.Face;
        internal bool IsBodyCombo => IsPerforming && _comboZone == PunchZone.Body;
        internal EnemyBrain BellyPuncher => DreadEnemy != null ? DreadEnemy : _thrower;

        public bool IsThrowIncoming => _phase != Phase.Combo && _phase != Phase.Finisher && _phase != Phase.Collapse
            && _thrower != null && _thrower.isActiveAndEnabled &&
            (_thrower.IsPreparingHeldThrow || _thrower.IsHandOffLocked);

        /// <summary>腹パン役が見つかっているか（HUD の表示に使う）。</summary>
        public bool HasAttacker { get; private set; }

        /// <summary>
        /// 構え始めに彼女が目を向ける相手（投げ役 → 腹パン役）。
        /// </summary>
        public Transform DreadTarget => DreadEnemy != null ? DreadEnemy.transform : null;
        internal EnemyBrain DreadEnemy { get; private set; }
        internal Vector3 DreadStartPoint { get; private set; }

        internal void SetDreadTarget(EnemyBrain enemy)
        {
            if (DreadEnemy == enemy)
                return;

            DreadEnemy = enemy;
            if (enemy == null)
                return;

            var figure = enemy.GetComponent<EnemyFigure>();
            Transform head = figure != null ? figure.HeadBone : null;
            DreadStartPoint = head != null ? head.position : enemy.transform.position + Vector3.up * 1.55f;
        }

        /// <summary>いま当たった腹パンの数。</summary>
        public int ComboHitsLanded => _hits;

        /// <summary>最後に当たったパンチの狙い（顔 / 腹）。HUD と見た目の分岐に使う。</summary>
        public PunchZone LastLandedZone { get; private set; } = PunchZone.Body;

        /// <summary>この拘束で食らう腹パンの総数。</summary>
        public int ComboHitsTotal => Mathf.Max(1, ComboHits);

        /// <summary>次に投げを試すまでの秒数。予約していなければ 0。</summary>
        public float NextThrowRemaining =>
            _phase == Phase.WaitingThrow ? Mathf.Max(0f, _throwTimer) : 0f;

        /// <summary>最初の腹パンが当たった瞬間。デバッグ撮影が購読する。</summary>
        public event Action Performed;

        PlayerGrapple _grapple;
        PlayerStats _stats;
        CharacterAnimator _anim;
        ThirdPersonCamera _camera;

        Phase _phase = Phase.Idle;

        float _throwTimer;      // WaitingThrow のときだけ意味を持つ
        EnemyBrain _thrower;
        EnemyBrain _guardPuncher;

        int _hits;
        PunchZone _comboZone;

        /// <summary>
        /// いま飛んでいる一発の強さ（発注時の段の値）。当たった瞬間の彼女の折れを
        /// 「打たれた回数」ではなく「その一発の強さ」に合わせるために持つ。
        /// </summary>
        float _orderPower = 1f;

        /// <summary>いま飛んでいる一発で拳を腹に押し込んでいる長さ（強さに連動する）。</summary>
        float _orderHold = 0.24f;

        /// <summary>コンボ開始時のカメラ位置が彼女の右側にあるか（顔面パンチを打つ腕を決める）。</summary>
        bool _cameraOnHerRight;

        /// <summary>顔面パンチが当たった瞬間のカメラ寄せ（1→0 で戻る）。</summary>
        float _impactPulse;
        int _bellyHits;
        float _nextPunchTimer;
        float _comboEndTimer;
        float _handOffTimer;    // HandOffWait のときだけ意味を持つ
        float _finisherHoistTimer;
        bool _finisherOrdered;
        float _collapseReleaseTimer;

        /// <summary>決めの全身ショットへ寄せた度合い（0 = コンボの寄り、1 = 全身）。</summary>
        float _finisherShotBlend;
        float _hoistProbeTimer;

        /// <summary>
        /// 抱え上げで体が実際に浮いているかを測る。目視で「浮いて見える」ではなく、
        /// 足首・つま先・腰がルート（＝床）から何 m 上にあるかを出す。
        /// </summary>
        void LogHoistProbe()
        {
            _hoistProbeTimer -= Time.deltaTime;
            if (_hoistProbeTimer > 0f)
                return;
            _hoistProbeTimer = 0.10f;

            var rig = _anim != null ? _anim.Rig : null;
            if (rig == null || rig.LFoot == null || rig.LToe == null)
                return;

            float rootY = _stats.transform.position.y;
            float ankle = Mathf.Min(rig.LFoot.position.y, rig.RFoot.position.y) - rootY;
            float toe = Mathf.Min(rig.LToe.position.y, rig.RToe.position.y) - rootY;
            float hips = rig.Hips != null ? rig.Hips.position.y - rootY : 0f;
            GameLog.Log($"[Hoist] w={FinisherHoist:F2} hips={hips:F3} ankle={ankle:F3} toe={toe:F3} t={Time.time:F2}");
        }

        bool _punchInFlight;
        float _hitTimeout;
        int _attempts;

        // 5 連撃のあいだ固定する立ち位置。殴り手が交代したときだけ決め直す。
        bool _comboSpotSet;
        Vector3 _comboSpot;
        Quaternion _comboFace;
        EnemyBrain _comboPuncher;

        float _cooldown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            // AfterSceneLoad は最初のシーンでしか走らない。タイトルからの再開や
            // PlayMode テストのシーン再読み込みでも付くように、sceneLoaded で毎回確認する。
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        static void Ensure()
        {
            if (FindFirstObjectByType<HeldBellyPunchDirector>() != null)
                return;

            // プレイヤーがいない検証シーンでは何もしない
            if (FindFirstObjectByType<PlayerStats>() == null)
                return;

            var go = new GameObject("Held Belly Punch Director");
            go.AddComponent<HeldBellyPunchDirector>();
        }

        void Start()
        {
            ApplyCameraOverrides();
            ResolveReferences();
            Subscribe();
        }

        /// <summary>
        /// 構図の撮り比べ用。<c>-heldpunchcamdist=</c> などの起動引数で、
        /// 腹パン中のカメラ値だけを差し替える（顔が入るかの確認を再ビルドなしで行う）。
        /// 引数が無ければインスペクタの値のまま。
        /// </summary>
        void OnDestroy()
        {
            Unsubscribe();
        }

        void OnDisable() => SetPunchGuard(null);

        void SetPunchGuard(EnemyBrain puncher)
        {
            if (_guardPuncher == puncher)
                return;

            if (_guardPuncher != null)
                _guardPuncher.HoldsBellyPunchGuard = false;
            _guardPuncher = puncher;
            if (_guardPuncher != null)
                _guardPuncher.HoldsBellyPunchGuard = true;
        }

        void ResolveReferences()
        {
            if (_stats == null)
                _stats = FindFirstObjectByType<PlayerStats>();

            if (_stats == null)
                return;

            if (_grapple == null)
                _grapple = _stats.GetComponent<PlayerGrapple>();

            if (_anim == null)
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();

            if (_camera == null)
            {
                var cam = Camera.main;
                _camera = cam != null
                    ? cam.GetComponent<ThirdPersonCamera>()
                    : FindFirstObjectByType<ThirdPersonCamera>();
            }
        }

        void Subscribe()
        {
            Unsubscribe();

            if (_grapple != null)
            {
                _grapple.Started += OnGrappleStarted;
                _grapple.Escaped += OnGrappleEnded;
                _grapple.Failed += OnGrappleEnded;
                _grapple.HeldThrowLanded += OnHeldThrowLanded;
            }

            if (_stats != null)
                _stats.HitReceived += OnHitReceived;
        }

        void Unsubscribe()
        {
            if (_grapple != null)
            {
                _grapple.Started -= OnGrappleStarted;
                _grapple.Escaped -= OnGrappleEnded;
                _grapple.Failed -= OnGrappleEnded;
                _grapple.HeldThrowLanded -= OnHeldThrowLanded;
            }

            if (_stats != null)
                _stats.HitReceived -= OnHitReceived;
        }

        void OnGrappleStarted(GrappleKind kind)
        {
            ResetHold();
            _hits = 0;
            _bellyHits = 0;

            // 前の拘束で残ったクールタイムを持ち越さない（持ち越すと、掴み直した直後の
            // 一発がコンボを始められず、拘束されているのに演出だけ出ない）
            _cooldown = 0f;

            // 腹パンのタッグ技は後ろ拘束のときだけ
            if (kind != GrappleKind.Back)
                return;

            _phase = Phase.WaitingThrow;
            _throwTimer = Mathf.Max(0.1f, ThrowDelaySeconds);
            _attempts = 0;

            GameLog.Log($"[HeldPunch] throw scheduled in {_throwTimer:F2}s");
        }

        void OnGrappleEnded()
        {
            // 連打で逃げた／時間切れで解放された。コンボ・トドメ中なら中止する
            if (IsCameraActive)
                GameLog.Log("[HeldPunch] cancel (hold ended)");

            ResetHold();
        }

        void ResetHold()
        {
            // 演出中に拘束が解けた（連打で逃げた等）なら、カメラの中央寄せも即座に戻す。
            // Phase を戻す前に判定する（IsCameraActive は Phase から導出している）。
            if (IsCameraActive && _camera != null)
                _camera.PushFocus(0f, 0f);

            _phase = Phase.Idle;
            _comboPuncher?.ClearPlanted();
            _thrower?.ClearPlanted();
            _throwTimer = 0f;
            _thrower = null;
            SetPunchGuard(null);
            _impactPulse = 0f;
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _handOffTimer = 0f;
            _finisherHoistTimer = 0f;
            _finisherOrdered = false;
            _collapseReleaseTimer = 0f;
            _punchInFlight = false;
            _hitTimeout = 0f;
            _attempts = 0;
            _comboSpotSet = false;
            _comboPuncher = null;
            HasAttacker = false;
            SetDreadTarget(null);
            _anim?.EndHeldThrowLead();
            _anim?.SetFinisherHoist(false);
        }

        void Update()
        {
            // 当たった瞬間の寄せを戻す（1 → 0、約 0.12 秒）。
            if (_impactPulse > 0f)
                _impactPulse = Mathf.MoveTowards(_impactPulse, 0f, Time.unscaledDeltaTime * 8f);

            if (_stats == null || _grapple == null)
            {
                ResolveReferences();
                if (_stats == null || _grapple == null)
                    return;

                Subscribe();
            }

            if (!_grapple.IsGrappled || _grapple.IsHandingOff || _grapple.Kind != GrappleKind.Back)
            {
                if (_phase != Phase.Idle || _thrower != null)
                    ResetHold();
                return;
            }

            // ポーズ・タイトル中は進めない
            if (Time.timeScale <= 0f)
            {
                // 命中で時間を止めている間も、実時間で減衰する寄りは保つ。
                if (IsCameraActive && HitStop.IsActive)
                    HoldFraming();
                return;
            }

            if (_cooldown > 0f)
                _cooldown = Mathf.Max(0f, _cooldown - Time.deltaTime);

            // 決めの画へ寄せる／戻す。グロッキーの 0.35 秒で引くので、カットせずに移る。
            float wideTarget = IsFinisherActive || (_phase == Phase.Combo && _hits >= ComboHitsTotal) ? 1f : 0f;
            _finisherShotBlend = Mathf.MoveTowards(_finisherShotBlend, wideTarget,
                Time.deltaTime / (wideTarget > 0.5f ? Mathf.Max(0.12f, ComboTailSeconds) : 0.25f));

            // 抱え上げの実測（足首・つま先・腰が床から何 m 浮いているか）。
            if (wideTarget > 0.5f)
                LogHoistProbe();

            switch (_phase)
            {
                case Phase.HandOffWait:
                    UpdateHandOff();
                    break;

                case Phase.Combo:
                    SuppressBystanders(0.9f);
                    _comboPuncher?.StayPlanted(0.4f);
                    UpdateCombo();
                    // 構図はコンボ中ずっと保つ（ヒット時の押し込みは 0.2 秒で抜けるため）
                    if (_phase == Phase.Combo)
                        HoldFraming();
                    break;

                case Phase.Finisher:
                    SuppressBystanders(0.9f);
                    _comboPuncher?.StayPlanted(0.4f);
                    UpdateFinisher();
                    if (IsCameraActive)
                        HoldFraming();
                    break;

                case Phase.Collapse:
                    UpdateCollapse();
                    if (_phase == Phase.Collapse)
                        HoldFraming();
                    break;

                case Phase.WaitingThrow:
                    UpdateOpeningThrow();
                    break;
            }
        }

        void UpdateOpeningThrow()
        {
            if (IsThrowIncoming)
                return;

            if (_thrower != null)
            {
                _thrower = null;
                _throwTimer = Mathf.Max(0.2f, RetrySeconds);
            }

            _throwTimer -= Time.deltaTime;
            if (_throwTimer > 0f)
                return;

            if (!TryOrderAttack(openingThrow: true))
                _throwTimer = 0.3f;
        }

        void UpdateCombo()
        {
            if (_hits >= ComboHitsTotal)
            {
                _comboEndTimer -= Time.deltaTime;
                if (_comboEndTimer <= 0f)
                    FinishCombo(completed: true);

                return;
            }

            if (_thrower != null)
            {
                var openingPuncher = _thrower;
                _comboPuncher = openingPuncher;
                _thrower = null;
                // 投げた距離のままだと顔面パンチが近すぎる。打つ前に一歩ぶん下がらせる。
                PlaceForOpeningPunch(openingPuncher, _comboZone);
                openingPuncher.StayPlanted(1.2f);
                bool openingLeft = _comboZone == PunchZone.Face
                    ? ChooseFacePunchSide(openingPuncher)
                    : ChooseBellyPunchSide();
                float openingPower = _comboZone == PunchZone.Face ? FacePunchPower(0) : BellyPunchPower(0);
                _orderPower = openingPower;
                _orderHold = FistHoldSeconds;
                if (openingPuncher.TryStartOpeningPunch(_comboZone, FistHoldSeconds, openingPower,
                        openingLeft))
                {
                    _punchInFlight = true;
                    _hitTimeout = 1.2f;
                    return;
                }
            }

            if (_punchInFlight)
            {
                _hitTimeout -= Time.deltaTime;
                if (_hitTimeout <= 0f)
                {
                    _punchInFlight = false;
                    _nextPunchTimer = Mathf.Max(0.2f, RetrySeconds);
                    _attempts++;
                    GameLog.Log("[HeldPunch] retry (miss)");
                }

                return;
            }

            _nextPunchTimer -= Time.deltaTime;
            if (_nextPunchTimer > 0f)
                return;

            if (TryOrderAttack(openingThrow: false))
            {
                _punchInFlight = true;
                _hitTimeout = 1.2f;
                return;
            }

            _attempts++;
            if (_attempts >= Mathf.Max(1, MaxAttempts))
            {
                // これ以上は当てられない。拘束を解放して詰みを防ぐ。
                GameLog.Log("[HeldPunch] give up (combo interrupted)");
                FinishCombo();
                return;
            }

            _nextPunchTimer = 0.25f;
        }

        /// <summary>
        /// 腹パン役を選び、プレイヤーの斜め前へ置いてジャブを指示する。
        /// コンボ中は相手の硬直を切って次を打たせる（5 連続をテンポよく見せるため）。
        /// </summary>
        void OnHitReceived(HitResult result)
        {
            if (!isActiveAndEnabled)
                return;

            if (_phase == Phase.Finisher && IsTagPunchHit(result))
            {
                OnFinisherHitReceived();
                return;
            }

            if (_phase != Phase.Combo || !IsTagPunchHit(result))
                return;

            if (_hits >= ComboHitsTotal)
                return;

            _hits++;
            _punchInFlight = false;
            _attempts = 0;

            PunchZone zone = _stats.LastHitZone ?? PunchZone.Body;
            // 顔面は接触だけ短く止め、拳を引く動きと首の反動へつなげる。
            HitStop.Play(zone == PunchZone.Face
                ? (_hits >= ComboHitsTotal ? 0.060f : 0.045f)
                : Mathf.Lerp(0.085f, 0.14f, Mathf.InverseLerp(1f, ComboHitsTotal, _hits)));
            _impactPulse = 1f;

            // 狙いで見た目を分ける。
            //   腹: 腰が押し戻され、その上で上体が前に折れる
            //   顔: 通常の顔面パンチと同じく、首が手前（カメラ側）へ振れる
            LastLandedZone = zone;
            var puncher = _stats.LastAttackerBrain;   // 被弾時に控えた本体（破棄済み Transform を触らない）
            if (puncher != null)
                _comboPuncher = puncher;
            if (zone == PunchZone.Body || puncher != _guardPuncher)
                SetPunchGuard(zone == PunchZone.Body ? puncher : null);

            float liftBeat = 0f;
            float bendLog = 0f;
            if (zone == PunchZone.Body)
                _bellyHits++;
            if (_anim != null)
            {
                if (zone == PunchZone.Face)
                {
                    _anim.EndHeldThrowLead();
                    // フックは打つ腕の外側から内側へ入る。首も拳が進む側へ振る。
                    Vector3 push = puncher != null
                        ? puncher.transform.forward * 0.55f - puncher.TagFaceOutside
                        : _stats.transform.forward;
                    bool finalHit = _hits >= ComboHitsTotal;
                    float faceFatigue = Mathf.InverseLerp(1f, 4f, _hits);
                    float faceSeconds = !finalHit
                        ? Mathf.Max(HitPoseSeconds, GapAfter(_hits, zone) + 0.70f)
                        : Mathf.Max(HitPoseSeconds, ComboTailSeconds + 0.25f);
                    _anim.PlayHeldFacePunch(faceSeconds, push,
                        FacePunchPower(Mathf.Max(0, _hits - 1)), faceFatigue, finalHit);
                    bendLog = FacePunchPower(Mathf.Max(0, _hits - 1));
                }
                else
                {
                    float fatigue = Mathf.InverseLerp(1f, 4f, _bellyHits);
                    float basePose = HitPoseSeconds + 0.32f * fatigue;
                    bool finalHit = _hits >= ComboHitsTotal;
                    // 間隔は段の表から取る。ここを固定値のままだと、強弱を付けても
                    // 等間隔の「作業」に見える（4 発目の前だけ休符になる）。
                    float gap = finalHit ? 0f : GapAfter(_hits, zone);
                    // 拘束役が腹を差し出す拍（liftBeat）が次の一発の間隔を決めている。
                    // 折れの長さを間隔から逆算して、拍と間隔を必ず一致させる。
                    float poseSeconds = finalHit
                        ? Mathf.Max(basePose, ComboTailSeconds + 0.25f)
                        : Mathf.Max(basePose, (gap + 0.26f) / 0.72f);
                    // 折れの深さも打たれた回数ではなく、その一発の強さに合わせる。
                    // 回数だけで深くすると、4 発目を浅く打っても彼女は一番深く折れたままになり、
                    // 殴る側の「弱」と食い違う（実測で確認する）。
                    float impact = Mathf.Lerp(0.80f, 1.60f,
                        Mathf.InverseLerp(0.74f, 1.50f, _orderPower));
                    float hold = _orderHold;
                    bendLog = impact;
                    _anim.PlayHeldBellyPunch(poseSeconds, fatigue, impact, hold, finalHit);
                    if (!finalHit)
                        liftBeat = poseSeconds * 0.72f - 0.26f;
                }
            }

            RefreshCamera(1.6f);
            // 殴られている間も、当たった瞬間の数十ミリ秒だけ入力を止める。
            // ここを長く取ると「5 発食らい終わるまで抜けられない」拘束になる（実測で 1.6 秒止めていた）。
            // 彼女は痛みに耐えながらも、合間を縫って抜けられるようにする。
            _grapple.SuppressStruggle(0.12f);

            if (_hits >= ComboHitsTotal)
            {
                _comboEndTimer = Mathf.Max(0.1f, ComboTailSeconds, liftBeat);
                _comboPuncher?.StayPlanted(_comboEndTimer + FinisherHoistSeconds + 1.2f);
                GameLog.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} ({zone}) → groggy " +
                          $"power={_orderPower:F2} hitT={Time.time:F2} bend={bendLog:F2}");
            }
            else
            {
                float interval = GapAfter(_hits, zone);
                _nextPunchTimer = Mathf.Max(0.1f, interval, liftBeat);
                // 長い間隔では、殴る側に一度体を起こして引き直させる（弱い一発の前の休符）。
                if (interval >= 0.5f)
                    _comboPuncher?.SetTagRest(Mathf.Max(0.1f, interval - 0.10f));
                GameLog.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} ({zone}) " +
                          $"power={_orderPower:F2} gap={interval:F2} " +
                          $"hitT={Time.time:F2} bend={bendLog:F2}");
            }

            if (_hits == 1)
                Performed?.Invoke();
        }

        /// <summary>「別の敵のパンチ（腹 / 顔）」として演出を始めてよいか。</summary>
        bool IsTagPunchHit(HitResult result)
        {
            if (!_stats.LastHitWasTagPunch || result.Damage <= 0f)
                return false;

            PunchZone? zone = _stats.LastHitZone;
            if (zone != _comboZone)
                return false;

            var captor = _grapple.Captor;
            var attacker = _stats.LastAttacker;
            if (captor == null || attacker == null || attacker == captor.transform)
                return false;

            // 腹パンは正面から。背中側（拘束者と同じ側）からの一撃は対象外。
            Vector3 to = attacker.position - _stats.transform.position;
            to.y = 0f;

            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;

            if (to.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f)
                return false;

            return Vector3.Dot(forward.normalized, to.normalized) > 0.1f;
        }

        void OnHeldThrowLanded(EnemyBrain attacker)
        {
            if (!isActiveAndEnabled || _phase == Phase.Combo || _phase == Phase.HandOffWait ||
                _cooldown > 0f ||
                !_grapple.IsGrappled || _grapple.IsHandingOff || _grapple.Kind != GrappleKind.Back ||
                _stats.IsDead)
                return;

            _phase = Phase.Combo;
            _comboZone = ChooseComboZone();
            // 最初の立ち位置は、このあと最初の 1 発を発注するときに決める。
            _comboSpotSet = false;
            _comboPuncher = null;
            // カメラはコンボ中に寄って動くので、「遠い腕」は開始時の構図で決めておく。
            _cameraOnHerRight = ComputeCameraOnHerRight();
            _hits = 0;
            _bellyHits = 0;
            LastLandedZone = PunchZone.Body;
            _throwTimer = 0f;
            _thrower = attacker;
            _punchInFlight = false;
            _attempts = 0;
            // 次の更新で同じ敵がそのまま一発目を打つ。
            _nextPunchTimer = 0f;

            // VRM の読み込みが遅れて CharacterAnimator が後から付くことがある。
            // 演出の瞬間に取り直す（Start で null のままでも姿勢を出せるように）。
            if (_anim == null)
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();

            // コンボ中ももがける。殴られている最中でも合間を縫って抜けられるようにする
            // （逃げたら演出はそこで終わる）。当たりの瞬間だけ 0.12 秒止める。
            _grapple.SuppressStruggle(0.12f);

            // 投げは“食らい”ではなく腹パン体勢への移行。折らずに腹を差し出す体勢へ入り、
            // コンボのあいだそのまま見せる（被弾姿勢はパンチが当たったときだけ）。
            // 顔面パンチのときは腹を差し出す必要がないので入れない（前傾したまま顔が隠れる）。
            _anim?.ClearHeldGroggy();
            if (_comboZone == PunchZone.Body)
                _anim?.PlayHeldThrowLead();

            GameLog.Log($"[HeldPunch] throw landed → combo start 0/{ComboHitsTotal} " +
                      $"captor={(_grapple.Captor != null ? _grapple.Captor.name : "none")} " +
                      $"attacker={attacker.name} " +
                      $"t={_grapple.HeldSeconds:F2}s");
        }

        void FinishCombo(bool completed = false)
        {
            int hits = _hits;

            _phase = Phase.Idle;
            _comboPuncher?.ClearPlanted();
            _comboPuncher?.SetTagRest(0f);
            SetDreadTarget(null);
            // 投げの崩れが残ったままコンボを終えない（崩れた姿勢のまま次の拘束へ入らない）。
            _anim?.EndHeldThrowLead();
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _punchInFlight = false;
            _hitTimeout = 0f;

            if (completed && _grapple != null && _grapple.IsGrappled
                && _stats != null && !_stats.IsDead)
            {
                var chain = FindFirstObjectByType<GrappleHandOff>();
                if (!_stats.HasHeldBellyDown && chain != null && chain.DecideAfterCombo(_comboPuncher))
                {
                    SetPunchGuard(null);
                    _grapple.ClearStruggleSuppression();
                    _phase = Phase.HandOffWait;
                    _handOffTimer = Mathf.Max(0f, HandOffDelaySeconds);
                    HasAttacker = false;
                    return;
                }

                BeginFinisher();
                return;
            }

            ContinueHold();
        }

        void BeginFinisher()
        {
            _phase = Phase.Finisher;
            _finisherHoistTimer = Mathf.Max(0.05f, FinisherHoistSeconds);
            _finisherOrdered = false;
            _punchInFlight = false;
            _attempts = 0;
            _grapple.ClearStruggleSuppression();
            _anim?.SetFinisherHoist(true);
            _comboPuncher?.StayPlanted(_finisherHoistTimer + 2.0f);
            HoldFraming();
            GameLog.Log($"[HeldPunch] groggy → hoist for finisher ({_comboZone})");
        }

        void UpdateFinisher()
        {
            if (_finisherHoistTimer > 0f)
            {
                _finisherHoistTimer -= Time.deltaTime;
                return;
            }

            if (_punchInFlight)
            {
                _hitTimeout -= Time.deltaTime;
                if (_hitTimeout <= 0f)
                {
                    _punchInFlight = false;
                    _attempts++;
                    if (_attempts >= 3)
                    {
                        GameLog.Log("[HeldPunch] finisher timeout → release down");
                        ReleaseWithFinisherDown();
                    }
                }
                return;
            }

            if (_finisherOrdered)
                return;

            if (TryOrderFinisherAttack())
            {
                _finisherOrdered = true;
                _punchInFlight = true;
                _hitTimeout = 1.6f;
                return;
            }

            _attempts++;
            if (_attempts >= 4)
            {
                GameLog.Log("[HeldPunch] no finisher attacker → release down");
                ReleaseWithFinisherDown();
            }
        }

        void OnFinisherHitReceived()
        {
            _punchInFlight = false;
            _anim?.SetFinisherHoist(false);

            PunchZone zone = _stats.LastHitZone ?? _comboZone;
            LastLandedZone = zone;
            var puncher = _stats.LastAttackerBrain != null ? _stats.LastAttackerBrain : _comboPuncher;
            if (puncher != null)
                _comboPuncher = puncher;
            if (zone == PunchZone.Body || puncher != _guardPuncher)
                SetPunchGuard(zone == PunchZone.Body ? puncher : null);

            HitStop.Play(zone == PunchZone.Face ? 0.115f : 0.22f);
            _impactPulse = zone == PunchZone.Face ? 1.35f : 1.55f;
            if (_camera != null && _camera.TryGetComponent<CameraShake>(out var shake))
                shake.Shake(zone == PunchZone.Body ? 0.065f : 0.048f, 0.34f);

            if (_anim != null)
            {
                _anim.EndHeldThrowLead();
                if (zone == PunchZone.Face)
                {
                    Vector3 push = puncher != null
                        ? puncher.transform.forward * 0.55f - puncher.TagFaceOutside
                        : _stats.transform.forward;
                    _anim.PlayHeldFacePunch(0.85f, push, 1.45f, 1f, finalHit: true, keepGroggy: true);
                }
                else
                {
                    _anim.PlayHeldBellyPunch(1.00f, 1f, 1.95f, 0.48f, finalHit: true, keepGroggy: true);
                }
            }

            _phase = Phase.Collapse;
            _collapseReleaseTimer = Mathf.Max(0.15f, CollapseHoldSeconds);
            RefreshCamera(1.2f);
            _grapple.SuppressStruggle(0.6f);
            GameLog.Log($"[HeldPunch] finisher landed ({zone}) → collapse hold hitT={Time.time:F2}");
        }

        void UpdateCollapse()
        {
            _collapseReleaseTimer -= Time.deltaTime;
            if (_collapseReleaseTimer > 0f)
                return;

            ReleaseWithFinisherDown();
        }

        void ReleaseWithFinisherDown()
        {
            var captor = _grapple != null ? _grapple.Captor : null;
            Transform attackerTf = _comboPuncher != null
                ? _comboPuncher.transform
                : (_stats != null ? _stats.LastAttacker : null);

            SetPunchGuard(null);
            _comboPuncher?.ClearPlanted();
            _comboPuncher?.SetTagRest(0f);
            _thrower?.ClearPlanted();
            SetDreadTarget(null);
            HasAttacker = false;
            _anim?.EndHeldThrowLead();
            _anim?.SetFinisherHoist(false);
            _finisherHoistTimer = 0f;
            _finisherOrdered = false;
            _collapseReleaseTimer = 0f;
            _punchInFlight = false;

            ApplyCollapseCamera();
            _phase = Phase.Idle;

            captor?.LockForHandOff(0.55f);
            _grapple?.Release();
            if (_stats != null && !_stats.IsDead)
            {
                _stats.ClearHeldBellyDamage();
                bool prone = _comboZone == PunchZone.Body;
                _stats.ForceDown(CombatConstants.DownDuration,
                    prone ? "held-belly-finisher" : "held-face-finisher",
                    attackerTf, launchDistance: 0f, launchHeight: 0f, forward: prone);
            }

            HoldOthers(Mathf.Max(ReleaseGraceSeconds, 1.1f));
            GameLog.Log($"[HeldPunch] combo end {_hits}/{ComboHitsTotal} ({_comboZone}) → finisher down");
        }

    }
}
