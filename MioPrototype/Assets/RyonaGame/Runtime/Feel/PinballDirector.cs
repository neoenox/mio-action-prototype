using System.Collections;
using Ryona;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Game
{
    /// <summary>
    /// 挟み撃ちの突き飛ばし（ピンポン）。前後の敵が彼女を突き返し合い、
    /// 受け止めの瞬間の踏みとどまりで抜けられる。連打では抜けない。
    /// </summary>
    [AddComponentMenu("Ryona/Pinball Director")]
    public sealed class PinballDirector : MonoBehaviour
    {
        PinballRules _rules;
        PlayerStats _stats;
        PlayerGrapple _grapple;
        ThirdPersonCamera _camera;
        EnemyBrain _front;
        EnemyBrain _rear;
        float _nextAllowed;
        float _nextRollAt;
        bool _active;
        bool _inputUsed;
        float _catchAt;
        int _round;
        string _burstPrefix;

        const float CatchLeadSeconds = 0.63f;   // PushContactSeconds(0.13) + flight(0.33) + follow(0.17)
        const float RollCheckSeconds = 1f;      // 抽選は 1 秒に 1 回だけ（毎フレームの連続抽選を防ぐ）

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<PinballDirector>() != null)
                return;
            var go = new GameObject("Pinball Director");
            var director = go.AddComponent<PinballDirector>();
            director._burstPrefix = LaunchOptions.Value("-pinballburst");
        }
        internal bool IsActive => _active;
        internal int Round => _round;
        internal float NextCatchAt => _catchAt;

        internal void ForceStart()
        {
            if (TryFindPair(out var front, out var rear))
                StartPinball(front, rear);
        }

        internal PinballTiming Brace()
        {
            if (!_active || _inputUsed)
                return PinballTiming.RoundClosed;
            _inputUsed = true;
            var timing = _rules.Judge(Time.time, _catchAt, _round);
            if (timing == PinballTiming.InWindow)
                Escape();
            return timing;
        }


        void Start()
        {
            var cfg = RyonaConfig.Instance.Pinball;
            _rules = new PinballRules(cfg.Rounds, cfg.Distance, cfg.DistanceGain,
                cfg.Speed, cfg.SpeedGain, cfg.WindowHalfSeconds, cfg.WindowNarrowing);
            _stats = FindFirstObjectByType<PlayerStats>();
            if (_stats == null)
                return;
            _grapple = _stats.GetComponent<PlayerGrapple>();
            _camera = FindFirstObjectByType<ThirdPersonCamera>();
            _stats.HitReceived += OnHitReceived;
            _nextAllowed = Time.time + 4f;
        }

        void OnDestroy()
        {
            if (_stats != null)
                _stats.HitReceived -= OnHitReceived;
        }

        void Update()
        {
            if (_stats == null || _grapple == null)
                return;

            if (_active)
            {
                FrameCamera();
                if (!_inputUsed && (InputReader.LightPressed() || InputReader.HeavyPressed() || InputReader.BreakPressed()))
                {
                    _inputUsed = true;
                    var timing = _rules.Judge(Time.time, _catchAt, _round);
                    if (timing == PinballTiming.InWindow)
                        Escape();
                    else
                        GameLog.Log($"[Pinball] timing={timing} round={_round}");
                }
                return;
            }

            if (LaunchOptions.Has("-pinballtest") && Time.time >= _nextAllowed)
            {
                if (TryFindPair(out var front, out var rear))
                {
                    StartPinball(front, rear);
                    return;
                }
            }

            if (_grapple.IsGrappled)
                _lastBusyAt = Time.time;

            if (Time.time < _nextAllowed || IsOtherSequenceBusy())
                return;

            // 確率判定は 1 秒に 1 回だけ行う。毎フレームだとクールタイム明けに
            // ほぼ確実に発生し、「掴みが連鎖する」原因になる。
            if (Time.time < _nextRollAt)
                return;
            _nextRollAt = Time.time + RollCheckSeconds;
            if (Random.value > RyonaConfig.Instance.Pinball.Chance)
                return;
            if (TryFindPair(out var a, out var b))
                StartPinball(a, b);
        }

        /// <summary>
        /// 拘束が解けてから、ピンポンを始めてよいまでの間（秒）。
        /// 顔トドメの回避は「命中の直前に拘束を一瞬解く」演出なので、解けた瞬間に始めると
        /// 拘束役をもう一度掴ませてしまい、トドメが届かず顔パンの連撃が並行して走る（実測、テストが 180 秒で打ち切り）。
        /// </summary>
        const float AfterGrappleGraceSeconds = 1.5f;

        float _lastBusyAt = float.NegativeInfinity;
        HeldBellyPunchDirector _heldPunch;

        /// <summary>テスト用: 別の演出のせいで、いま自動では始まらないか。</summary>
        internal bool BlockedByOtherSequence => IsOtherSequenceBusy();

        /// <summary>別の演出（拘束・タッグ腹パンの連撃〜トドメ・マウント・ダウン）が走っているか。</summary>
        bool IsOtherSequenceBusy()
        {
            if (_heldPunch == null)
                _heldPunch = FindFirstObjectByType<HeldBellyPunchDirector>();

            bool busy = _grapple.IsGrappled || _grapple.IsHandingOff || _stats.IsDown || _stats.IsDead ||
                        (_heldPunch != null && _heldPunch.CurrentPhase != HeldBellyPunchDirector.Phase.Idle) ||
                        (_stats.GroundMount != null && _stats.GroundMount.Attacker != null);
            if (busy)
            {
                _lastBusyAt = Time.time;
                return true;
            }

            return Time.time - _lastBusyAt < AfterGrappleGraceSeconds;
        }

        bool TryFindPair(out EnemyBrain front, out EnemyBrain rear)
        {
            front = null;
            rear = null;
            var enemies = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None);
            float px = _stats.transform.position.x;
            EnemyBrain bestFront = null, bestRear = null;
            float bestGap = float.MaxValue;
            foreach (var e in enemies)
            {
                // 顔トドメで退いている拘束役は使わない（退いた直後にもう一度掴ませない）。
                if (e == null || !e.isActiveAndEnabled || !e.IsAlive || e.IsRising ||
                    e.IsHolding || e.IsEvading ||
                    e.Phase == EnemyPhase.Windup || e.Phase == EnemyPhase.Attack ||
                    e.Phase == EnemyPhase.Stagger)
                    continue;

                float dx = e.transform.position.x - px;
                float gap = Mathf.Abs(dx);
                if (gap < 0.9f || gap > 2.0f)
                    continue;

                if (dx > 0f && (bestFront == null || gap < bestGap))
                {
                    bestFront = e;
                    bestGap = gap;
                }
                else if (dx < 0f && (bestRear == null || gap < bestGap))
                    bestRear = e;
            }
            front = bestFront;
            rear = bestRear;
            return front != null && rear != null;
        }

        void StartPinball(EnemyBrain front, EnemyBrain rear)
        {
            _front = front;
            _rear = rear;
            _active = true;
            _round = 0;
            _inputUsed = false;
            _nextAllowed = Time.time + Mathf.Max(4f, RyonaConfig.Instance.Pinball.CooldownSeconds) + 3f;
            _grapple.SetPinball(true);
            _grapple.Begin(rear, GrappleKind.Back);

            foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (e != null && e != front && e != rear)
                    e.LockForHandOff(4f);
            }

            // 起点は前の敵の胸突き。通常攻撃と同じく予備動作の音が出る。
            front.transform.position = new Vector3(_stats.transform.position.x + 1.4f, 0f, _stats.transform.position.z);
            front.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            if (!front.TryStartPinballPunch())
            {
                Cancel();
                return;
            }
            _catchAt = Time.time + front.TagPunchSecondsToStrike + CatchLeadSeconds;
            GameLog.Log($"[Pinball] start front={front.name} rear={rear.name}");
            if (!string.IsNullOrEmpty(_burstPrefix))
                StartCoroutine(BurstShots());
        }

        void OnHitReceived(HitResult result)
        {
            if (!_active || _round != 0 || result.Damage <= 0f)
                return;
            if (_stats.LastAttackerBrain != _front)
                return;

            GameLog.Log("[Pinball] opening punch landed");
            StartCoroutine(RunRounds());
        }

        IEnumerator RunRounds()
        {
            for (_round = 0; _round < _rules.Rounds; _round++)
            {
                _inputUsed = false;
                _catchAt = Time.time + CatchLeadSeconds;
                yield return new WaitForSeconds(CatchLeadSeconds + 0.12f);
                if (!_active)
                    yield break;

                if (_rules.IsFinal(_round))
                {
                    Finish();
                    yield break;
                }

                EnemyBrain next = (_round % 2 == 0) ? _front : _rear;
                GrappleKind kind = (_round % 2 == 0) ? GrappleKind.Pin : GrappleKind.Back;
                _grapple.HandOff(next, kind, shove: true);
                GameLog.Log($"[Pinball] round={_round + 1} → {next.name}");
                yield return new WaitForSeconds(0.9f);
            }
        }

        void Finish()
        {
            var attack = AttackLibrary.Heavy();
            attack.id = "pinball_finish";
            attack.damage = 12f;
            attack.partDamage = 26f;
            _stats.ReceiveHit(_front.State, attack, _front.transform, heldBellyDamage: 26f, isTagPunch: false);
            _stats.ForceDown(CombatConstants.DownDuration, "pinball-finish", _front.transform,
                launchDistance: 0f, launchHeight: 0f, forward: true);
            GameLog.Log("[Pinball] finish → prone down");
            Cancel();
        }

        void Escape()
        {
            GameLog.Log($"[Pinball] brace out round={_round}");
            _grapple.Release();
            var light = AttackLibrary.Light();
            light.id = "pinball_escape";
            light.damage = 0f;
            light.flinch = 14f;
            _front.TakeHit(_stats.State, light);
            _rear.TakeHit(_stats.State, light);
            Cancel();
        }

        void Cancel()
        {
            _active = false;
            _stats?.GroundMount?.DelayNextSearch(4f);
            _grapple?.SetPinball(false);
            _grapple?.SetPinball(false);
            if (_camera != null)
                _camera.HoldFocus(1f, 0f, 0f, 0f);
        }

        void FrameCamera()
        {
            if (_camera == null || _front == null || _rear == null)
                return;
            float mid = (_front.transform.position.x + _rear.transform.position.x) * 0.5f;
            _camera.HoldFocus(0.46f, 0f, mid, 0.3f);
        }

        IEnumerator BurstShots()
        {
            int index = 0;
            float next = Time.unscaledTime;
            while (_active && index < 120)
            {
                while (Time.unscaledTime < next)
                    yield return null;
                string dir = System.IO.Path.GetDirectoryName(_burstPrefix);
                string name = System.IO.Path.GetFileNameWithoutExtension(_burstPrefix);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{index:000}.png"));
                next += 0.05f;
                index++;
            }
        }
    }
}
