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

        [Tooltip("次の腹パンを指示するまでの秒数（前の一発が当たってから）")]
        public float ComboIntervalSeconds = 0.55f;

        [Tooltip("1 発ごとの被弾姿勢の長さ")]
        public float HitPoseSeconds = 0.55f;

        [Tooltip("腹に当ててから拳を引き切るまでの秒数")]
        public float FistHoldSeconds = 0.17f;

        [Tooltip("最後の一発のあと、カメラの寄りを保つ秒数")]
        public float ComboTailSeconds = 1.4f;

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
        public float CameraDistanceScale = 0.30f;

        [Tooltip("腹パン中のピボット上下（正で上がる）。脚を切って腹と顔を入れる高さ")]
        public float CameraPivotOffset = 0.34f;

        [Tooltip("腹パン中の俯角オフセット（負で見上げる）")]
        public float CameraPitchOffset = -12f;

        [Tooltip("腹パン中のヨーオフセット（負で彼女の正面側へ回り込み、顔が見える）。殴る腕が体の奥に隠れない角度にする")]
        public float CameraYawOffset = -35f;

        [Header("Camera (face)")]
        [Tooltip("顔面パンチ中のカメラ距離倍率（1 未満で寄る）")]
        public float FaceCameraDistanceScale = 0.28f;

        [Tooltip("顔面パンチ中のピボット上下。顔と拳が同じ画に入る高さ")]
        public float FaceCameraPivotOffset = 0.50f;

        [Tooltip("顔面パンチ中の俯角オフセット。見上げ気味にすると顔が見える")]
        public float FaceCameraPitchOffset = -6f;

        [Tooltip("顔面パンチ中のヨーオフセット。腹パンと同程度に回り込んで、彼女の顔と拳を同時に見せる")]
        public float FaceCameraYawOffset = -40f;

        [Header("Aim")]
        [Tooltip("腹パン役を立たせる、プレイヤーの腹からの前方距離（m）。離して踏み込ませると拳の移動が見える")]
        public float AttackerDistance = 0.82f;

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
        }

        /// <summary>いまの段階（テスト・デバッグ表示用）。</summary>
        public Phase CurrentPhase => _phase;

        /// <summary>腹パンのコンボ中か（投げが決まってから打数を数え終わるまで）。</summary>
        public bool IsPerforming => _phase == Phase.Combo;
        internal bool IsFaceCombo => IsPerforming && _comboZone == PunchZone.Face;

        public bool IsThrowIncoming => _phase != Phase.Combo && _thrower != null &&
            _thrower.isActiveAndEnabled &&
            (_thrower.IsPreparingHeldThrow || _thrower.IsHandOffLocked);

        /// <summary>腹パン役が見つかっているか（HUD の表示に使う）。</summary>
        public bool HasAttacker { get; private set; }

        /// <summary>
        /// 構え始めに彼女が目を向ける相手（投げ役 → 腹パン役）。
        /// </summary>
        public Transform DreadTarget => DreadEnemy != null ? DreadEnemy.transform : null;
        internal EnemyBrain DreadEnemy { get; private set; }
        internal Vector3 DreadStartPoint { get; private set; }

        void SetDreadTarget(EnemyBrain enemy)
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

        /// <summary>コンボ開始時のカメラ位置が彼女の右側にあるか（顔面パンチを打つ腕を決める）。</summary>
        bool _cameraOnHerRight;

        /// <summary>顔面パンチが当たった瞬間のカメラ寄せ（1→0 で戻る）。</summary>
        float _impactPulse;
        int _bellyHits;
        float _nextPunchTimer;
        float _comboEndTimer;
        float _handOffTimer;    // HandOffWait のときだけ意味を持つ

        bool _punchInFlight;
        float _hitTimeout;
        int _attempts;

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

            Debug.Log($"[HeldPunch] throw scheduled in {_throwTimer:F2}s");
        }

        void OnGrappleEnded()
        {
            // 連打で逃げた／時間切れで解放された。コンボ中なら中止する
            if (IsPerforming)
                Debug.Log("[HeldPunch] cancel (hold ended)");

            ResetHold();
        }

        void ResetHold()
        {
            // 演出中に拘束が解けた（連打で逃げた等）なら、カメラの中央寄せも即座に戻す。
            // Phase を戻す前に判定する（IsPerforming は Phase から導出している）。
            if (IsPerforming && _camera != null)
                _camera.PushFocus(0f, 0f);

            _phase = Phase.Idle;
            _throwTimer = 0f;
            _thrower = null;
            SetPunchGuard(null);
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _handOffTimer = 0f;
            _punchInFlight = false;
            _hitTimeout = 0f;
            _attempts = 0;
            HasAttacker = false;
            SetDreadTarget(null);
            _anim?.EndHeldThrowLead();
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
                if (_phase == Phase.Combo && _comboZone == PunchZone.Face && HitStop.IsActive)
                    HoldFraming();
                return;
            }

            if (_cooldown > 0f)
                _cooldown = Mathf.Max(0f, _cooldown - Time.deltaTime);

            switch (_phase)
            {
                case Phase.HandOffWait:
                    UpdateHandOff();
                    break;

                case Phase.Combo:
                    UpdateCombo();
                    // 構図はコンボ中ずっと保つ（ヒット時の押し込みは 0.2 秒で抜けるため）
                    if (_phase == Phase.Combo)
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
                _thrower = null;
                // 投げた距離のままだと顔面パンチが近すぎる。打つ前に一歩ぶん下がらせる。
                PlaceForOpeningPunch(openingPuncher, _comboZone);
                if (openingPuncher.TryStartOpeningPunch(_comboZone, FistHoldSeconds,
                        FacePunchPower(0)))
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
                    Debug.Log("[HeldPunch] retry (miss)");
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
                Debug.Log("[HeldPunch] give up (combo interrupted)");
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
            if (!isActiveAndEnabled || _phase != Phase.Combo || !IsTagPunchHit(result))
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
                : 0.09f);
            if (zone == PunchZone.Face)
                _impactPulse = 1f;

            // 狙いで見た目を分ける。
            //   腹: 腰が押し戻され、その上で上体が前に折れる
            //   顔: 通常の顔面パンチと同じく、首が手前（カメラ側）へ振れる
            LastLandedZone = zone;
            var puncher = _stats.LastAttacker.GetComponent<EnemyBrain>();
            if (zone == PunchZone.Body || puncher != _guardPuncher)
                SetPunchGuard(zone == PunchZone.Body ? puncher : null);

            float liftBeat = 0f;
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
                    _anim.PlayHeldFacePunch(HitPoseSeconds, push,
                        FacePunchPower(Mathf.Max(0, _hits - 1)));
                }
                else
                {
                    float fatigue = Mathf.InverseLerp(1f, 4f, _bellyHits);
                    float poseSeconds = HitPoseSeconds + (ComboIntervalSeconds + 0.12f) * fatigue;
                    _anim.PlayHeldBellyPunch(poseSeconds, fatigue);
                    if (_bellyHits >= 3)
                        liftBeat = poseSeconds * 0.90f;
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
                Debug.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} ({zone}) → finish");
            }
            else
            {
                _nextPunchTimer = Mathf.Max(0.1f, ComboIntervalSeconds, liftBeat);
                Debug.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} ({zone})");
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
            if (_comboZone == PunchZone.Body)
                _anim?.PlayHeldThrowLead();

            Debug.Log($"[HeldPunch] throw landed → combo start 0/{ComboHitsTotal} " +
                      $"captor={(_grapple.Captor != null ? _grapple.Captor.name : "none")} " +
                      $"attacker={attacker.name} " +
                      $"t={_grapple.HeldSeconds:F2}s");
        }

        void FinishCombo(bool completed = false)
        {
            int hits = _hits;

            _phase = Phase.Idle;
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
                if (_stats.HasHeldBellyDown)
                {
                    SetPunchGuard(null);
                    _grapple.Release();
                    _camera?.PushFocus(0f, 0f);
                    _stats.ResolveHeldBellyDown();
                    Debug.Log($"[HeldPunch] combo end {hits}/{ComboHitsTotal} → down (prone)");
                    HoldOthers(ReleaseGraceSeconds);
                    HasAttacker = false;
                    return;
                }

                _grapple.ClearStruggleSuppression();
                _phase = Phase.HandOffWait;
                _handOffTimer = Mathf.Max(0f, HandOffDelaySeconds);
                HasAttacker = false;
                return;
            }

            ContinueHold();
        }

    }
}
