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

        [Tooltip("1 回の拘束で食らう腹パンの回数")]
        public int ComboHits = 5;

        [Range(0f, 1f)]
        [Tooltip("各パンチが顔面を狙う確率。残りは腹パン。0 で腹パンのみ、1 で顔面のみ")]
        public float FacePunchChance = 0.5f;

        [Tooltip("次の腹パンを指示するまでの秒数（前の一発が当たってから）")]
        public float ComboIntervalSeconds = 0.55f;

        [Tooltip("1 発ごとの「く」の字の長さ")]
        public float HitPoseSeconds = 0.55f;

        [Tooltip("当たった瞬間から拳を腹に押し込んでおく秒数。彼女の折れ（HitPoseSeconds）より十分短くする。長いと次の一発まで拳が伸びたままになり、打つ動作に見えない")]
        public float FistHoldSeconds = 0.28f;

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
        [Tooltip("腹パン中のカメラ距離倍率（1 未満で寄る）。0.28 で約 1.5m（基本距離 5.2m × 0.28）")]
        public float CameraDistanceScale = 0.28f;

        [Tooltip("腹パン中のピボット上下（正で上がる）。顔が画面に入る高さまで上げる")]
        public float CameraPivotOffset = 0.27f;

        [Tooltip("腹パン中の俯角オフセット（負で見上げる）")]
        public float CameraPitchOffset = -12f;

        [Tooltip("腹パン中のヨーオフセット（負でプレイヤーの正面側へ回り込む）")]
        public float CameraYawOffset = -40f;

        [Header("Aim")]
        [Tooltip("腹パン役を立たせる、プレイヤーの腹からの前方距離（m）")]
        public float AttackerDistance = 0.74f;

        [Tooltip("（未使用）ベルトでは敵の z が毎フレーム彼女に合わせられるため、側方オフセットは効かない")]
        public float AttackerSideOffset = 0.68f;

        [Tooltip("この距離より遠い敵は腹パン役に選ばない（m）。遠すぎると位置合わせがワープに見える")]
        public float AttackerSearchRange = 2.5f;

        /// <summary>腹パンのコンボ中か。</summary>
        public bool IsPerforming { get; private set; }

        public bool IsThrowIncoming => !_comboActive && _thrower != null && _thrower.isActiveAndEnabled &&
            (_thrower.IsPreparingHeldThrow || _thrower.IsHandOffLocked);

        /// <summary>腹パン役が見つかっているか（HUD の表示に使う）。</summary>
        public bool HasAttacker { get; private set; }

        /// <summary>いま当たった腹パンの数。</summary>
        public int ComboHitsLanded => _hits;

        /// <summary>最後に当たったパンチの狙い（顔 / 腹）。HUD と見た目の分岐に使う。</summary>
        public PunchZone LastLandedZone { get; private set; } = PunchZone.Body;

        /// <summary>この拘束で食らう腹パンの総数。</summary>
        public int ComboHitsTotal => Mathf.Max(1, ComboHits);

        /// <summary>次に投げを試すまでの秒数。予約していなければ 0。</summary>
        public float NextThrowRemaining => _waitingForThrow ? Mathf.Max(0f, _throwTimer) : 0f;

        /// <summary>最初の腹パンが当たった瞬間。デバッグ撮影が購読する。</summary>
        public event Action Performed;

        PlayerGrapple _grapple;
        PlayerStats _stats;
        CharacterAnimator _anim;
        ThirdPersonCamera _camera;

        bool _waitingForThrow;
        float _throwTimer;
        EnemyBrain _thrower;

        bool _comboActive;
        int _hits;
        float _nextPunchTimer;
        float _comboEndTimer;
        bool _waitingForHandOff;
        float _handOffTimer;

        bool _waitingForHit;
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

            // 前の拘束で残ったクールタイムを持ち越さない（持ち越すと、掴み直した直後の
            // 一発がコンボを始められず、拘束されているのに演出だけ出ない）
            _cooldown = 0f;

            // 腹パンのタッグ技は後ろ拘束のときだけ
            if (kind != GrappleKind.Back)
                return;

            _waitingForThrow = true;
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
            if (IsPerforming && _camera != null)
                _camera.PushFocus(0f, 0f);

            _waitingForThrow = false;
            _throwTimer = 0f;
            _thrower = null;
            _comboActive = false;
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _waitingForHandOff = false;
            _handOffTimer = 0f;
            _waitingForHit = false;
            _hitTimeout = 0f;
            _attempts = 0;
            HasAttacker = false;
            IsPerforming = false;
            _anim?.EndHeldThrowLead();
        }

        void Update()
        {
            if (_stats == null || _grapple == null)
            {
                ResolveReferences();
                if (_stats == null || _grapple == null)
                    return;

                Subscribe();
            }

            if (!_grapple.IsGrappled || _grapple.IsHandingOff || _grapple.Kind != GrappleKind.Back)
            {
                if (IsPerforming || _waitingForThrow || _thrower != null || _waitingForHandOff)
                    ResetHold();
                return;
            }

            // ポーズ・タイトル中は進めない
            if (Time.timeScale <= 0f)
                return;

            if (_waitingForHandOff)
            {
                UpdateHandOff();
                return;
            }

            if (_cooldown > 0f)
                _cooldown = Mathf.Max(0f, _cooldown - Time.deltaTime);

            if (_comboActive)
            {
                UpdateCombo();

                // 構図はコンボ中ずっと保つ（ヒット時の押し込みは 0.2 秒で抜けるため）
                if (_comboActive)
                    HoldFraming();

                return;
            }

            UpdateOpeningThrow();
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

            if (!_waitingForThrow)
                return;

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
                if (openingPuncher.TryStartOpeningBellyPunch(FistHoldSeconds))
                {
                    _waitingForHit = true;
                    _hitTimeout = 1.2f;
                    return;
                }
            }

            if (_waitingForHit)
            {
                _hitTimeout -= Time.deltaTime;
                if (_hitTimeout <= 0f)
                {
                    _waitingForHit = false;
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
                _waitingForHit = true;
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
            if (!_comboActive || !IsTagPunchHit(result))
                return;

            if (_hits >= ComboHitsTotal)
                return;

            _hits++;
            _waitingForHit = false;
            _attempts = 0;

            // 1 発を重く見せる。既存の被弾ヒットストップ（約 0.07 秒）より少し長く止める。
            HitStop.Play(0.09f);

            // 狙いで見た目を分ける。
            //   腹: 「く」の字に折れる（拘束されたまま腹を抱える）
            //   顔: 通常の顔面パンチと同じく、首が手前（カメラ側）へ振れる
            PunchZone zone = _stats.LastHitZone ?? PunchZone.Body;
            LastLandedZone = zone;

            if (_anim != null)
            {
                if (zone == PunchZone.Face)
                    _anim.PlayHeldFacePunch(HitPoseSeconds);
                else
                    _anim.PlayHeldBellyPunch(HitPoseSeconds);
            }

            RefreshCamera(1.6f);
            // 殴られている間も、当たった瞬間の数十ミリ秒だけ入力を止める。
            // ここを長く取ると「5 発食らい終わるまで抜けられない」拘束になる（実測で 1.6 秒止めていた）。
            // 彼女は痛みに耐えながらも、合間を縫って抜けられるようにする。
            _grapple.SuppressStruggle(0.12f);

            if (_hits >= ComboHitsTotal)
            {
                _comboEndTimer = Mathf.Max(0.1f, ComboTailSeconds);
                Debug.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} ({zone}) → finish");
            }
            else
            {
                _nextPunchTimer = Mathf.Max(0.1f, ComboIntervalSeconds);
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
            if (zone != PunchZone.Body && zone != PunchZone.Face)
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
            if (!isActiveAndEnabled || _comboActive || _waitingForHandOff || _cooldown > 0f ||
                !_grapple.IsGrappled || _grapple.IsHandingOff || _grapple.Kind != GrappleKind.Back ||
                _stats.IsDead)
                return;

            _comboActive = true;
            IsPerforming = true;
            _hits = 0;
            LastLandedZone = PunchZone.Body;
            _waitingForThrow = false;
            _thrower = attacker;
            _waitingForHit = false;
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
            // コンボのあいだそのまま見せる（「く」の字はパンチが当たったときだけ）。
            _anim?.PlayHeldThrowLead();

            Debug.Log($"[HeldPunch] throw landed → combo start 0/{ComboHitsTotal} " +
                      $"captor={(_grapple.Captor != null ? _grapple.Captor.name : "none")} " +
                      $"attacker={attacker.name} " +
                      $"t={_grapple.HeldSeconds:F2}s");
        }

        void FinishCombo(bool completed = false)
        {
            int hits = _hits;

            _comboActive = false;
            IsPerforming = false;
            // 投げの崩れが残ったままコンボを終えない（崩れた姿勢のまま次の拘束へ入らない）。
            _anim?.EndHeldThrowLead();
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _waitingForHit = false;
            _hitTimeout = 0f;

            if (completed && _grapple != null && _grapple.IsGrappled
                && _stats != null && !_stats.IsDead)
            {
                if (_stats.HasHeldBellyDown)
                {
                    _grapple.Release();
                    _camera?.PushFocus(0f, 0f);
                    _stats.ResolveHeldBellyDown();
                    Debug.Log($"[HeldPunch] combo end {hits}/{ComboHitsTotal} → down (prone)");
                    HoldOthers(ReleaseGraceSeconds);
                    HasAttacker = false;
                    return;
                }

                _grapple.ClearStruggleSuppression();
                _waitingForHandOff = true;
                _handOffTimer = Mathf.Max(0f, HandOffDelaySeconds);
                HasAttacker = false;
                return;
            }

            ContinueHold();
        }

    }
}
