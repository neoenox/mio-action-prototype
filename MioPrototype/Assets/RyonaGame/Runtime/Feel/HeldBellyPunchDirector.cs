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
    /// <item>後ろ拘束が始まったら <see cref="PunchDelaySeconds"/> 秒待つ</item>
    /// <item>拘束者以外の敵をプレイヤーの斜め前へ置き、Body 狙いのジャブを指示する</item>
    /// <item>最初の一発が当たったら、<see cref="ComboHits"/> 発まで連続で腹パン</item>
    /// <item>「く」の字・もがき停止・斜め前カメラをコンボ中ずっと維持し、終わったら拘束を解放</item>
    /// </list>
    ///
    /// 後ろ拘束は時間切れで解けないので、プレイヤーは
    /// 「連打で逃げる」か「腹パンを 5 発食らって解放される」かを選ぶことになる。
    /// 判定（<see cref="PlayerStats.ReceiveHit"/>）には干渉せず、イベントを見て演出だけを足す。
    /// </summary>
    [AddComponentMenu("Ryona/Held Belly Punch Director")]
    public sealed class HeldBellyPunchDirector : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("後ろ拘束が始まってから、最初の腹パンを指示するまでの秒数")]
        public float PunchDelaySeconds = 1.6f;

        [Tooltip("1 回の拘束で食らう腹パンの回数")]
        public int ComboHits = 5;

        [Tooltip("次の腹パンを指示するまでの秒数（前の一発が当たってから）")]
        public float ComboIntervalSeconds = 0.55f;

        [Tooltip("1 発ごとの「く」の字の長さ")]
        public float HitPoseSeconds = 0.55f;

        [Tooltip("当たった瞬間から拳を腹に押し込んでおく秒数。彼女の折れ（HitPoseSeconds）より十分短くする。長いと次の一発まで拳が伸びたままになり、打つ動作に見えない")]
        public float FistHoldSeconds = 0.28f;

        [Tooltip("最後の一発から拘束を解放するまでの余韻")]
        public float ComboTailSeconds = 0.55f;

        [Tooltip("演出後に他の敵が追撃を控える秒数")]
        public float ReleaseGraceSeconds = 0.6f;

        [Tooltip("腹パンが外れた／中断されたときに、次を試すまでの秒数")]
        public float RetrySeconds = 0.6f;

        [Tooltip("1 回の拘束で腹パンを指示する最大試行回数")]
        public int MaxAttempts = 8;

        [Header("Camera")]
        [Tooltip("腹パン中のカメラ距離倍率（1 未満で寄る）")]
        public float CameraDistanceScale = 0.34f;

        [Tooltip("腹パン中のピボット上下（正で上がる）。顔が画面に入る高さまで上げる")]
        public float CameraPivotOffset = 0.27f;

        [Tooltip("腹パン中の俯角オフセット（負で見上げる）")]
        public float CameraPitchOffset = -12f;

        [Tooltip("腹パン中のヨーオフセット（負でプレイヤーの正面側へ回り込む）")]
        public float CameraYawOffset = -40f;

        [Header("Aim")]
        [Tooltip("腹パン役を立たせる、プレイヤーからの前方距離（m）")]
        public float AttackerDistance = 0.98f;

        [Tooltip("（未使用）ベルトでは敵の z が毎フレーム彼女に合わせられるため、側方オフセットは効かない")]
        public float AttackerSideOffset = 0.68f;

        [Tooltip("この距離より遠い敵は腹パン役に選ばない（m）")]
        public float AttackerSearchRange = 8f;

        /// <summary>腹パンのコンボ中か。</summary>
        public bool IsPerforming { get; private set; }

        /// <summary>次の腹パンを指示して、当たるのを待っているか。</summary>
        public bool IsPunchIncoming => _waitingForHit;

        /// <summary>腹パン役が見つかっているか（HUD の表示に使う）。</summary>
        public bool HasAttacker { get; private set; }

        /// <summary>いま当たった腹パンの数。</summary>
        public int ComboHitsLanded => _hits;

        /// <summary>この拘束で食らう腹パンの総数。</summary>
        public int ComboHitsTotal => Mathf.Max(1, ComboHits);

        /// <summary>次の腹パンまでの残り秒。予約していなければ 0。</summary>
        public float NextPunchRemaining =>
            _waitingForFirst ? Mathf.Max(0f, _punchTimer)
            : (_comboActive && _hits < ComboHitsTotal ? Mathf.Max(0f, _nextPunchTimer) : 0f);

        /// <summary>最初の腹パンが当たった瞬間。デバッグ撮影が購読する。</summary>
        public event Action Performed;

        PlayerGrapple _grapple;
        PlayerStats _stats;
        CharacterAnimator _anim;
        ThirdPersonCamera _camera;

        bool _waitingForFirst;
        float _punchTimer;

        bool _comboActive;
        int _hits;
        float _nextPunchTimer;
        float _comboEndTimer;

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
        void ApplyCameraOverrides()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (TryRead(a, "-heldpunchcamdist=", out float dist))
                    CameraDistanceScale = dist;
                else if (TryRead(a, "-heldpunchcampivot=", out float pivot))
                    CameraPivotOffset = pivot;
                else if (TryRead(a, "-heldpunchcampitch=", out float pitch))
                    CameraPitchOffset = pitch;
                else if (TryRead(a, "-heldpunchcamyaw=", out float yaw))
                    CameraYawOffset = yaw;
            }
        }

        static bool TryRead(string arg, string key, out float value)
        {
            value = 0f;

            return arg.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)
                && float.TryParse(arg.Substring(key.Length),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }

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
            }

            if (_stats != null)
                _stats.HitReceived -= OnHitReceived;
        }

        void OnGrappleStarted(GrappleKind kind)
        {
            ResetHold();
            _hits = 0;

            // 腹パンのタッグ技は後ろ拘束のときだけ
            if (kind != GrappleKind.Back)
                return;

            _waitingForFirst = true;
            _punchTimer = Mathf.Max(0.1f, PunchDelaySeconds);
            _attempts = 0;

            Debug.Log($"[HeldPunch] schedule in {_punchTimer:F2}s");
        }

        void OnGrappleEnded()
        {
            // 連打で逃げた／投げられた。コンボ中なら中止する
            if (IsPerforming)
                Debug.Log("[HeldPunch] cancel (hold ended)");

            ResetHold();
        }

        void ResetHold()
        {
            // 演出中に拘束が解けた（連打で逃げた等）なら、カメラの中央寄せも即座に戻す。
            if (IsPerforming && _camera != null)
                _camera.PushFocus(0f, 0f);

            _waitingForFirst = false;
            _punchTimer = 0f;
            _comboActive = false;
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _waitingForHit = false;
            _hitTimeout = 0f;
            _attempts = 0;
            HasAttacker = false;
            IsPerforming = false;
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

            if (!_grapple.IsGrappled || _grapple.Kind != GrappleKind.Back)
            {
                if (IsPerforming || _waitingForFirst || _comboActive || _waitingForHit)
                    ResetHold();
                return;
            }

            // ポーズ・タイトル中は進めない
            if (Time.timeScale <= 0f)
                return;

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

            UpdateFirstPunch();
        }

        void UpdateFirstPunch()
        {
            if (_waitingForHit)
            {
                _hitTimeout -= Time.deltaTime;
                if (_hitTimeout <= 0f)
                {
                    _waitingForHit = false;
                    _waitingForFirst = true;
                    _punchTimer = Mathf.Max(0.2f, RetrySeconds);
                    _attempts++;
                    Debug.Log("[HeldPunch] retry (first punch missed)");
                }

                return;
            }

            if (!_waitingForFirst)
                return;

            _punchTimer -= Time.deltaTime;
            if (_punchTimer > 0f)
                return;

            if (TryOrderPunch())
            {
                _waitingForFirst = false;
                _waitingForHit = true;
                _hitTimeout = 2.0f;
                return;
            }

            _attempts++;
            if (_attempts >= Mathf.Max(1, MaxAttempts))
            {
                _waitingForFirst = false;
                HasAttacker = false;
                Debug.Log("[HeldPunch] give up (no opening for the first punch)");
                return;
            }

            _punchTimer = 0.3f;
        }

        void UpdateCombo()
        {
            if (_hits >= ComboHitsTotal)
            {
                _comboEndTimer -= Time.deltaTime;
                if (_comboEndTimer <= 0f)
                    FinishCombo();

                return;
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

            if (TryOrderPunch())
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
        bool TryOrderPunch()
        {
            var captor = _grapple.Captor;
            EnemyBrain best = null;
            float bestDistance = Mathf.Max(1f, AttackerSearchRange);

            // 立たせる基準は「拳が狙う腹の座標」にする。root を基準にすると、
            // 腹とのずれのぶんだけ向きが狂い、拳が 0.3m 外れる（実測: gap 0.33）。
            Vector3 anchor = BodyAnchor();

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive || enemy.IsRising || enemy == captor)
                    continue;

                // 予備動作・攻撃・怯みの途中なら、位置を動かすと攻撃が壊れる。次の機会を待つ。
                if (enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack ||
                    enemy.Phase == EnemyPhase.Stagger)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, anchor);
                if (distance >= bestDistance)
                    continue;

                best = enemy;
                bestDistance = distance;
            }

            if (best == null)
            {
                HasAttacker = false;
                return false;
            }

            HasAttacker = true;

            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.right;
            forward.Normalize();

            // ベルトなので奥行き（z）は EnemyBrain.LateUpdate が毎フレーム彼女に合わせる。
            // ここで側方（z）へずらしても次のフレームで戻され、「ずらした前提の向き」だけが
            // 残って腹から 35° 外れる（実測: 拳が 0.33m 届かず空振り）。
            // 立ち位置は前後（x）だけで決め、向きは実際に立つ位置から腹へ向ける。
            Vector3 spot = anchor + forward * Mathf.Max(0.8f, AttackerDistance);
            spot.z = _stats.transform.position.z;
            spot.y = best.transform.position.y;

            Vector3 toPlayer = anchor - spot;
            toPlayer.y = 0f;

            best.transform.position = spot;
            if (toPlayer.sqrMagnitude > 1e-4f)
                best.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

            if (!best.TryStartBellyPunch(allowRecover: true, holdPoseSeconds: FistHoldSeconds))
                return false;

            return true;
        }

        /// <summary>
        /// 立ち位置と向きの基準。拳が狙うのと同じ「腹の座標」を使う。
        /// root を基準にすると、腹とのずれのぶんだけ腹パン役の向きが狂う。
        /// </summary>
        Vector3 BodyAnchor()
        {
            if (_stats == null)
                return Vector3.zero;

            Vector3 belly = EnemyBrain.PunchTarget(_stats, PunchZone.Body, 0.28f);
            return new Vector3(belly.x, _stats.transform.position.y, belly.z);
        }

        void OnHitReceived(HitResult result)
        {
            if (!_comboActive)
            {
                if (ShouldStartCombo(result))
                    BeginCombo();

                return;
            }

            if (!IsBellyHit(result))
                return;

            if (_hits >= ComboHitsTotal)
                return;

            _hits++;
            _waitingForHit = false;
            _attempts = 0;

            // 1 発を重く見せる。既存の被弾ヒットストップ（約 0.07 秒）より少し長く止める。
            HitStop.Play(0.09f);

            // 1 発ごとに「く」の字をやり直す。カメラはコンボ中ずっと同じ構図。
            if (_anim != null)
                _anim.PlayHeldBellyPunch(HitPoseSeconds);
            RefreshCamera(1.6f);
            _grapple.SuppressStruggle(1.6f);

            if (_hits >= ComboHitsTotal)
            {
                _comboEndTimer = Mathf.Max(0.1f, ComboTailSeconds);
                Debug.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal} → release");
            }
            else
            {
                _nextPunchTimer = Mathf.Max(0.1f, ComboIntervalSeconds);
                Debug.Log($"[HeldPunch] combo {_hits}/{ComboHitsTotal}");
            }
        }

        bool ShouldStartCombo(HitResult result)
        {
            if (_grapple == null || _stats == null)
                return false;

            if (!_grapple.IsGrappled || _grapple.Kind != GrappleKind.Back)
                return false;

            if (_comboActive || _cooldown > 0f)
                return false;

            if (_stats.IsDead)
                return false;

            return IsBellyHit(result);
        }

        /// <summary>「別の敵の腹パンチ」として演出を始めてよいか。</summary>
        bool IsBellyHit(HitResult result)
        {
            if (result.Damage <= 0f || result.Blocked)
                return false;

            if (_stats.LastHitZone != PunchZone.Body)
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

        void BeginCombo()
        {
            if (_comboActive || _cooldown > 0f)
                return;

            _comboActive = true;
            IsPerforming = true;
            _hits = 1;
            _waitingForFirst = false;
            _waitingForHit = false;
            _attempts = 0;
            _nextPunchTimer = Mathf.Max(0.1f, ComboIntervalSeconds);

            // VRM の読み込みが遅れて CharacterAnimator が後から付くことがある。
            // 演出の瞬間に取り直す（Start で null のままでも姿勢を出せるように）。
            if (_anim == null)
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();

            // コンボ中はもがきを受け付けない。最後の一発の後に解放される。
            float suppress = ComboHitsTotal * (ComboIntervalSeconds + 0.5f) + ComboTailSeconds + 0.5f;
            _grapple.SuppressStruggle(suppress);

            if (_anim != null)
                _anim.PlayHeldBellyPunch(HitPoseSeconds);

            RefreshCamera(2.0f);
            HoldOthers(ComboHitsTotal * 0.7f + 1f);

            Debug.Log($"[HeldPunch] combo start 1/{ComboHitsTotal} " +
                      $"captor={(_grapple.Captor != null ? _grapple.Captor.name : "none")} " +
                      $"attacker={(_stats.LastAttacker != null ? _stats.LastAttacker.name : "none")} " +
                      $"t={_grapple.HeldSeconds:F2}s");

            Performed?.Invoke();
        }

        void FinishCombo()
        {
            int hits = _hits;

            _comboActive = false;
            IsPerforming = false;
            _nextPunchTimer = 0f;
            _comboEndTimer = 0f;
            _waitingForHit = false;
            _hitTimeout = 0f;

            if (_grapple != null && _grapple.IsGrappled)
            {
                _grapple.Release();
                Debug.Log($"[HeldPunch] release after {hits} hits");
            }

            HoldOthers(ReleaseGraceSeconds);
            _cooldown = 0.6f;
            HasAttacker = false;
        }

        /// <summary>コンボ中、カメラを斜め前の構図に保つ。ヒットごとに押し直す。</summary>
        void RefreshCamera(float seconds)
        {
            if (!ResolveCamera())
                return;

            float hold = Mathf.Max(0.2f, seconds);
            _camera.PushDistance(CameraDistanceScale, hold);
            _camera.PushPivot(CameraPivotOffset, hold);

            float yaw = FocusYaw();
            _camera.PushFocus(_stats.transform.position.x, hold, CameraPitchOffset, yaw);

            // ヒット時に押した値だけ出す（毎フレームの保持はログに出さない）
            if (hold >= 1.5f)
                Debug.Log($"[HeldPunchCam] scale={CameraDistanceScale:F2} pivot={CameraPivotOffset:F2} " +
                          $"pitch={CameraPitchOffset:F0} yaw={yaw:F0} " +
                          $"liveScale={_camera.EffectDistanceScale:F2} livePivot={_camera.EffectPivotOffset:F2}");
        }

        /// <summary>
        /// コンボ中、同じ構図を毎フレーム保つ。
        /// <see cref="RefreshCamera"/> の押し込みは 0.2 秒で抜けるので、
        /// 2 発目以降は通常の距離に戻って顔が小さくなる。ここでは保持用の API で固定する。
        /// </summary>
        void HoldFraming()
        {
            if (!ResolveCamera())
                return;

            _camera.HoldFocus(CameraDistanceScale, CameraPivotOffset,
                _stats.transform.position.x, 0.5f, CameraPitchOffset, FocusYaw());
        }

        /// <summary>腹パン中に回り込む向き。左向きのときに背中側へ回らないよう符号を合わせる。</summary>
        float FocusYaw()
        {
            float facing = _stats.transform.forward.x;
            float sign = Mathf.Abs(facing) > 0.1f ? Mathf.Sign(facing) : 1f;
            return CameraYawOffset * sign;
        }

        bool ResolveCamera()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                _camera = cam != null
                    ? cam.GetComponent<ThirdPersonCamera>()
                    : FindFirstObjectByType<ThirdPersonCamera>();
            }

            return _camera != null && _stats != null;
        }

        /// <summary>演出の間、周囲の敵が追撃しないようにする。</summary>
        void HoldOthers(float seconds)
        {
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, _stats.transform.position) > 8f)
                    continue;

                enemy.HoldFire(seconds);
            }
        }
    }
}
