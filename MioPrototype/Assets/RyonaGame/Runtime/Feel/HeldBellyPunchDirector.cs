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
    /// <item>拘束者以外の敵をプレイヤーの正面へ置き、Body 狙いのジャブを指示する</item>
    /// <item>腹パンチが当たったら「く」の字・カメラ寄せ・もがき停止を <see cref="PerformanceSeconds"/> 秒再生</item>
    /// <item>演出が終わったら拘束を解放し、他の敵は <see cref="ReleaseGraceSeconds"/> 秒待つ</item>
    /// </list>
    ///
    /// 後ろ拘束は時間切れで解けないので、プレイヤーは
    /// 「連打で逃げる」か「腹パンを 1 回食らって解放される」かを選ぶことになる。
    /// 判定（<see cref="PlayerStats.ReceiveHit"/>）には干渉せず、イベントを見て演出だけを足す。
    /// </summary>
    [AddComponentMenu("Ryona/Held Belly Punch Director")]
    public sealed class HeldBellyPunchDirector : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("後ろ拘束が始まってから、最初の腹パンを指示するまでの秒数")]
        public float PunchDelaySeconds = 1.6f;

        [Tooltip("腹パンが外れた／中断されたときに、次を試すまでの秒数")]
        public float RetrySeconds = 1.0f;

        [Tooltip("「く」の字・カメラ寄せ・もがき停止を続ける秒数")]
        public float PerformanceSeconds = 0.55f;

        [Tooltip("演出後に他の敵が追撃を控える秒数")]
        public float ReleaseGraceSeconds = 0.6f;

        [Tooltip("1 回の拘束で腹パンを試す最大回数。超えたら連打で逃げるしかない")]
        public int MaxAttempts = 3;

        [Header("Camera")]
        [Tooltip("腹パン中のカメラ距離倍率（1 未満で寄る）")]
        public float CameraDistanceScale = 0.48f;

        [Tooltip("腹パン中のピボット上下（正で上がる）。腹と顔を同じ画面に入れる")]
        public float CameraPivotOffset = 0.25f;

        [Header("Aim")]
        [Tooltip("腹パン役を立たせる、プレイヤーからの前方距離（m）")]
        public float AttackerDistance = 1.32f;

        [Tooltip("この距離より遠い敵は腹パン役に選ばない（m）")]
        public float AttackerSearchRange = 8f;

        /// <summary>腹パンの演出中か。</summary>
        public bool IsPerforming { get; private set; }

        /// <summary>腹パンを指示して、当たるのを待っているか。</summary>
        public bool IsPunchIncoming => _waitingForHit;

        /// <summary>腹パン役が見つかっているか（HUD の表示に使う）。</summary>
        public bool HasAttacker { get; private set; }

        /// <summary>次の腹パンまでの残り秒。予約していなければ 0。</summary>
        public float NextPunchRemaining => _punchPending ? Mathf.Max(0f, _punchTimer) : 0f;

        /// <summary>演出が始まった瞬間。デバッグ撮影が購読する。</summary>
        public event Action Performed;

        PlayerGrapple _grapple;
        PlayerStats _stats;
        CharacterAnimator _anim;
        ThirdPersonCamera _camera;

        float _punchTimer;
        bool _punchPending;
        bool _waitingForHit;
        float _hitTimeout;
        int _attempts;

        float _performanceRemaining;
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
            ResolveReferences();
            Subscribe();
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

            // 腹パンのタッグ技は後ろ拘束のときだけ
            if (kind != GrappleKind.Back)
                return;

            _punchPending = true;
            _punchTimer = Mathf.Max(0.1f, PunchDelaySeconds);
            _attempts = 0;

            Debug.Log($"[HeldPunch] schedule in {_punchTimer:F2}s");
        }

        void OnGrappleEnded()
        {
            // 連打で逃げた／投げられた。演出中なら中止する（カメラは自然に戻る）
            if (IsPerforming)
                Debug.Log("[HeldPunch] cancel (hold ended)");

            ResetHold();
        }

        void ResetHold()
        {
            // 演出中に拘束が解けた（連打で逃げた等）なら、カメラの中央寄せも即座に戻す。
            // 通常終了（FinishPerformance）は余韻を残すため、ここでは触らない。
            if (IsPerforming && _camera != null)
                _camera.PushFocus(0f, 0f);

            _punchPending = false;
            _waitingForHit = false;
            _hitTimeout = 0f;
            _attempts = 0;
            HasAttacker = false;
            IsPerforming = false;
            _performanceRemaining = 0f;
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
                if (IsPerforming || _punchPending || _waitingForHit)
                    ResetHold();
                return;
            }

            // ポーズ・タイトル中は進めない
            if (Time.timeScale <= 0f)
                return;

            if (IsPerforming)
            {
                _performanceRemaining -= Time.deltaTime;
                if (_performanceRemaining <= 0f)
                    FinishPerformance();
                return;
            }

            if (_cooldown > 0f)
                _cooldown = Mathf.Max(0f, _cooldown - Time.deltaTime);

            if (_waitingForHit)
            {
                _hitTimeout -= Time.deltaTime;
                if (_hitTimeout <= 0f)
                {
                    // 指示した腹パンが外れた／中断された。次の機会を作る
                    _waitingForHit = false;
                    ScheduleRetry("miss");
                }

                return;
            }

            if (!_punchPending)
                return;

            _punchTimer -= Time.deltaTime;
            if (_punchTimer <= 0f)
                TryOrderPunch();
        }

        /// <summary>
        /// 腹パン役を選び、正面に置いてジャブを指示する。
        /// 見つからない／指示できないときは少し待って選び直す。
        /// </summary>
        void TryOrderPunch()
        {
            var captor = _grapple.Captor;
            EnemyBrain best = null;
            float bestDistance = Mathf.Max(1f, AttackerSearchRange);

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive || enemy.IsRising || enemy == captor)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, _stats.transform.position);
                if (distance >= bestDistance)
                    continue;

                best = enemy;
                bestDistance = distance;
            }

            if (best == null)
            {
                HasAttacker = false;
                _punchPending = true;
                _punchTimer = 0.5f;
                return;
            }

            HasAttacker = true;

            Vector3 forward = _stats.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.right;
            forward.Normalize();

            Vector3 spot = _stats.transform.position + forward * Mathf.Max(0.8f, AttackerDistance);
            spot.y = best.transform.position.y;
            best.transform.position = spot;
            best.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);

            _attempts++;

            if (best.TryStartBellyPunch())
            {
                _punchPending = false;
                _waitingForHit = true;
                _hitTimeout = 2.0f;
                return;
            }

            if (_attempts >= Mathf.Max(1, MaxAttempts))
            {
                // 3 回続けて指示できなければ、この拘束では諦める（連打で逃げるしかない）
                _punchPending = false;
                HasAttacker = false;
                Debug.Log("[HeldPunch] give up (no clean opening)");
                return;
            }

            _punchPending = true;
            _punchTimer = 0.3f;
        }

        void ScheduleRetry(string reason)
        {
            _attempts++;

            if (_attempts >= Mathf.Max(1, MaxAttempts))
            {
                _punchPending = false;
                HasAttacker = false;
                Debug.Log($"[HeldPunch] give up ({reason})");
                return;
            }

            _punchPending = true;
            _punchTimer = Mathf.Max(0.2f, RetrySeconds);
            Debug.Log($"[HeldPunch] retry in {_punchTimer:F2}s ({reason})");
        }

        void OnHitReceived(HitResult result)
        {
            if (!ShouldPerform(result))
                return;

            BeginPerformance();
        }

        bool ShouldPerform(HitResult result)
        {
            if (_grapple == null || _stats == null)
                return false;

            if (!_grapple.IsGrappled || _grapple.Kind != GrappleKind.Back)
                return false;

            if (IsPerforming || _cooldown > 0f)
                return false;

            // ヒットストップ中は Time.timeScale が 0 になるが、それは「当たった瞬間」なので
            // ここでは弾かない（ポーズ・タイトル中は当たり判定自体が進まない）。
            if (_stats.IsDead)
                return false;

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

        void BeginPerformance()
        {
            IsPerforming = true;
            _performanceRemaining = Mathf.Max(0.1f, PerformanceSeconds);
            _punchPending = false;
            _waitingForHit = false;
            _hitTimeout = 0f;

            // VRM の読み込みが遅れて CharacterAnimator が後から付くことがある。
            // 演出の瞬間に取り直す（Start で null のままでも姿勢を出せるように）。
            if (_anim == null)
                _anim = _stats.GetComponentInChildren<CharacterAnimator>();
            if (_camera == null)
            {
                var cam = Camera.main;
                _camera = cam != null
                    ? cam.GetComponent<ThirdPersonCamera>()
                    : FindFirstObjectByType<ThirdPersonCamera>();
            }

            // 演出中はもがきを受け付けない（拘束は続く）
            _grapple.SuppressStruggle(_performanceRemaining + 0.05f);

            if (_anim != null)
                _anim.PlayHeldBellyPunch(_performanceRemaining);

            if (_camera != null)
            {
                // ヒットストップや読み込みの引っかかりで実時間が飛んでも、
                // 演出が終わる前にカメラが戻らないよう少し長めに押す。
                float cameraSeconds = _performanceRemaining + 0.25f;
                _camera.PushDistance(CameraDistanceScale, cameraSeconds);
                _camera.PushPivot(CameraPivotOffset, cameraSeconds);
                // 画面ロック中は彼女が端にいることがあるので、中央へ寄せる。
                // 俯角も少し弱めて、折れて下を向いた顔を画面に入れる。
                _camera.PushFocus(_stats.transform.position.x, cameraSeconds, -8f);
            }

            HoldOthers(_performanceRemaining + 0.2f);

            Debug.Log($"[HeldPunch] trigger captor={(_grapple.Captor != null ? _grapple.Captor.name : "none")} " +
                      $"attacker={(_stats.LastAttacker != null ? _stats.LastAttacker.name : "none")} " +
                      $"t={_grapple.HeldSeconds:F2}s");

            Performed?.Invoke();
        }

        void FinishPerformance()
        {
            IsPerforming = false;
            _performanceRemaining = 0f;

            if (_grapple != null && _grapple.IsGrappled)
            {
                _grapple.Release();
                Debug.Log("[HeldPunch] release after belly punch");
            }

            HoldOthers(ReleaseGraceSeconds);
            _cooldown = 0.6f;
            HasAttacker = false;
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
