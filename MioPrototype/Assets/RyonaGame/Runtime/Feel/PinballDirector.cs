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
        bool _roundsRunning;
        Coroutine _rounds;
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

            // 締めのあと少しの間は、倒れる彼女を寄りで見せる（すぐ引きに戻すと倒れる姿が小さくなる）。
            if (!_active && Time.time < _holdFrameUntil && _camera != null)
                _camera.HoldFocus(0.55f, 0f, _stats.transform.position.x, 0.3f);

            if (_active)
            {
                FrameCamera();

                // 起点の胸突きが当たらないまま待ち続けない（背後に掴まれたまま固まる）。
                if (_round == 0 && !_roundsRunning)
                {
                    if (Time.unscaledTime >= _nextWaitLog)
                    {
                        _nextWaitLog = Time.unscaledTime + 0.25f;
                        GameLog.Log($"[Pinball] wait opening front={(_front != null ? _front.name : "none")} " +
                                    $"phase={(_front != null ? _front.Phase.ToString() : "-")} " +
                                    $"dist={(_front != null ? Mathf.Abs(_front.transform.position.x - _stats.transform.position.x) : 0f):F2}");
                    }
                    if (Time.time >= _openingDeadline)
                    {
                        GameLog.Log("[Pinball] cancel: 起点の胸突きが当たらない（時間切れ）");
                        _grapple.Release();
                        Cancel();
                        return;
                    }
                }
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

            if (LaunchOptions.Has("-pinballtest"))
            {
                // 検証用: 敵が前後に揃うのを待たず、自分で並べて必ず始める。
                // 以前は「たまたま前後に揃ったら」だけで、25 秒走らせても始まらないことがあった（実測）。
                // 開始直後に敵がまだ湧いていないこともあるので、成功するまで再試行する。
                if (!_testArranged && Time.time >= 2f && !_grapple.IsGrappled && !_stats.IsDown)
                {
                    if (ArrangeTestPair(out var front, out var rear))
                    {
                        _testArranged = true;
                        StartPinball(front, rear);
                    }
                    else if (Time.unscaledTime >= _nextTestArrangeLog)
                    {
                        _nextTestArrangeLog = Time.unscaledTime + 1f;
                        GameLog.Log("[Pinball] 検証用の2体がまだ揃っていない（再試行します）");
                    }
                }
                return;
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

        bool _testArranged;
        float _openingDeadline;
        float _nextWaitLog;
        float _nextTestArrangeLog;

        /// <summary>
        /// 起点の胸突きが当たるまでの待ちの上限（秒）。当たらないまま待つと、彼女は背後の敵に
        /// 掴まれたまま（連打も止められて）動けなくなる（実測: 前の敵が胸に手を当てたまま固まった）。
        /// </summary>
        const float OpeningTimeoutSeconds = 2.5f;

        /// <summary>起点の胸突きを打つ前の敵の立ち位置（彼女からの距離、m）。ジャブの届く範囲（1.25m）の内側。</summary>
        const float OpeningStandOff = 1.0f;

        /// <summary>検証用: 2 体を彼女の前後 1.4m に並べ、残りは遠ざけて止める。</summary>
        bool ArrangeTestPair(out EnemyBrain front, out EnemyBrain rear)
        {
            front = null;
            rear = null;
            var list = new System.Collections.Generic.List<EnemyBrain>();
            foreach (var e in EnemyRegistry.All)
            {
                if (e != null && e.isActiveAndEnabled && e.IsAlive)
                    list.Add(e);
            }
            if (list.Count < 2)
                return false;

            Vector3 p = _stats.transform.position;
            front = list[0];
            rear = list[1];
            // 自分の攻撃の予備動作中だと胸突きを始められない（実測 phase=Windup で中止）。
            front.CancelBystanderWindup(0f);
            rear.CancelBystanderWindup(0f);
            front.transform.SetPositionAndRotation(new Vector3(p.x + 1.4f, 0f, p.z), Quaternion.Euler(0f, -90f, 0f));
            rear.transform.SetPositionAndRotation(new Vector3(p.x - 1.4f, 0f, p.z), Quaternion.Euler(0f, 90f, 0f));
            for (int i = 2; i < list.Count; i++)
            {
                list[i].transform.position = new Vector3(p.x + 8f + i, 0f, p.z);
                list[i].LockForHandOff(12f);
            }
            GameLog.Log($"[Pinball] test arranged front={front.name} rear={rear.name}");
            return true;
        }

        bool TryFindPair(out EnemyBrain front, out EnemyBrain rear)
        {
            front = null;
            rear = null;
            var enemies = EnemyRegistry.All;
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
            StopRounds();
            _front = front;
            _rear = rear;
            _active = true;
            _roundsRunning = false;
            _round = 0;
            _inputUsed = false;
            _nextAllowed = Time.time + Mathf.Max(4f, RyonaConfig.Instance.Pinball.CooldownSeconds) + 3f;
            _grapple.SetPinball(true);
            _grapple.Begin(rear, GrappleKind.Back);

            foreach (var e in EnemyRegistry.All)
            {
                if (e != null && e != front && e != rear)
                    e.LockForHandOff(4f);
            }

            // 起点は前の敵の胸突き。通常攻撃と同じく予備動作の音が出る。
            // 前の敵は彼女の正面（前の敵がいる側）に立たせ、彼女の方を向かせる。
            // 以前は 1.4m に置いており、胸突き（ジャブ）の届く 1.25m より遠く、毎回空振りした
            // （実測 StrikeMiss dist=1.40 range=1.25）。往復が始まらず、背後に掴まれたまま固まっていた。
            // 回転も Euler(0,180,0) 固定で、奥行き方向（カメラの方）を向いていた。
            Vector3 her = _stats.transform.position;
            float side = Mathf.Sign(front.transform.position.x - her.x);
            if (Mathf.Abs(side) < 0.5f)
                side = 1f;
            front.CancelBystanderWindup(0f);
            front.transform.SetPositionAndRotation(new Vector3(her.x + OpeningStandOff * side, 0f, her.z),
                Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up));
            if (!front.TryStartPinballPunch())
            {
                GameLog.Log($"[Pinball] cancel: {front.name} が胸突きを始められない phase={front.Phase}");
                _grapple.Release();
                Cancel();
                return;
            }
            _catchAt = Time.time + front.TagPunchSecondsToStrike + CatchLeadSeconds;
            _openingDeadline = Time.time + OpeningTimeoutSeconds;
            GameLog.Log($"[Pinball] start front={front.name} rear={rear.name}");
            if (!string.IsNullOrEmpty(_burstPrefix))
                StartCoroutine(BurstShots());
        }

        void OnHitReceived(HitResult result)
        {
            if (_active && _sandwichHitter != null && result.Damage > 0f &&
                _stats.LastAttackerBrain == _sandwichHitter)
            {
                GameLog.Log($"[Pinball] sandwich blow landed by {_sandwichHitter.name}");
                _sandwichHitter = null;
                return;
            }

            if (_active && _finishPending && result.Damage > 0f && _stats.LastAttackerBrain == _front)
            {
                FinishOnHit();
                return;
            }

            if (!_active || _round != 0 || _roundsRunning || result.Damage <= 0f)
                return;
            if (_stats.LastAttackerBrain != _front)
                return;

            GameLog.Log("[Pinball] opening punch landed");
            _roundsRunning = true;
            _rounds = StartCoroutine(RunRounds());
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
                    // 最後の受け止めで踏みとどまれなかったら、吹き飛ばす前に前後から挟んで殴る。
                    yield return Sandwich();
                    if (!_active)
                        yield break;
                    yield return FinishPunch();
                    yield break;
                }

                EnemyBrain next = (_round % 2 == 0) ? _front : _rear;
                GrappleKind kind = (_round % 2 == 0) ? GrappleKind.Pin : GrappleKind.Back;
                _grapple.HandOff(next, kind, shove: true);
                GameLog.Log($"[Pinball] round={_round + 1} → {next.name}");
                yield return new WaitForSeconds(0.9f);
            }
        }

        bool _finishPending;

        /// <summary>挟み撃ちの腹パンで、いま命中を待っている敵（null なら待っていない）。</summary>
        EnemyBrain _sandwichHitter;

        /// <summary>挟み撃ちの打撃の数。前→後ろ→前→後ろの順に交互に入る。</summary>
        internal const int SandwichBlows = 4;

        /// <summary>テスト用: 挟み撃ちで命中した打撃の数。</summary>
        internal int SandwichLanded { get; private set; }

        /// <summary>テスト用: 挟み撃ちの最中か。</summary>
        internal bool InSandwich { get; private set; }

        /// <summary>
        /// 挟み撃ちの腹パン（ピンポンの締めの派生）。
        /// 掴んでいない方の敵が詰めて一発入れ、その敵へ彼女を突き渡す。殴られた勢いで
        /// 反対側の敵の胸へぶつかり、そこで受け止められて次の一発をもらう。
        /// 倒れずに「く」の字と反りを繰り返し、最後は前の敵の締めの一発で崩れる。
        /// この間は踏みとどまりも連打も効かない（逃げ場がない）。
        /// </summary>
        IEnumerator Sandwich()
        {
            _inputUsed = true;
            InSandwich = true;
            SandwichLanded = 0;
            GameLog.Log("[Pinball] sandwich begin");

            // 開始時の固定（4 秒）は挟み撃ちの途中で切れ、残りの敵が歩いて来て後ろの敵に
            // めり込んだ（実測 pinball #186〜）。締めまで固定し直す。
            foreach (var e in EnemyRegistry.All)
            {
                if (e != null && e != _front && e != _rear)
                    e.LockForHandOff(SandwichBlows * 1.8f + 3f);
            }

            for (int i = 0; i < SandwichBlows && _active; i++)
            {
                // 掴んでいない方が殴る。後ろに掴まれていれば前が腹を、前に掴まれていれば後ろが背を打つ。
                EnemyBrain holder = _grapple.Captor;
                EnemyBrain hitter = holder == _rear ? _front : _rear;
                if (hitter == null || !hitter.IsAlive || holder == null)
                    break;

                // 突き渡しの固定が明けるまで待つ（固定中は構えに入れない）。
                float lockDeadline = Time.time + 1.5f;
                while (_active && hitter.IsHandOffLocked && Time.time < lockDeadline)
                    yield return null;
                if (!_active)
                    break;

                yield return StepIn(hitter);
                if (!_active)
                    break;

                _sandwichHitter = hitter;
                bool started = false;
                float startDeadline = Time.time + 0.6f;
                while (_active && !(started = TryStartBlow(hitter, 0.8f)) && Time.time < startDeadline)
                    yield return null;
                if (!started)
                {
                    GameLog.Log($"[Pinball] sandwich: {hitter.name} が構えられない phase={hitter.Phase}");
                    _sandwichHitter = null;
                    break;
                }

                float deadline = Time.time + 1.4f;
                while (_active && _sandwichHitter != null && Time.time < deadline)
                    yield return null;
                if (_sandwichHitter == null)
                    SandwichLanded++;
                else
                    GameLog.Log($"[Pinball] sandwich blow missed by {hitter.name}");
                _sandwichHitter = null;

                // 折れる（反る）のを見せてから、殴った側へ突き渡す。
                yield return new WaitForSeconds(0.30f);
                if (!_active)
                    break;
                if (i < SandwichBlows - 1)
                {
                    _grapple.HandOff(hitter, hitter == _front ? GrappleKind.Pin : GrappleKind.Back, shove: true);
                    yield return new WaitForSeconds(0.75f);
                }
            }

            // 締めは前の敵が打つので、後ろに掴まれた形へ戻しておく。
            if (_active && _grapple.Captor != _rear && _rear != null && _rear.IsAlive)
            {
                _grapple.HandOff(_rear, GrappleKind.Back, shove: true);
                yield return new WaitForSeconds(0.9f);
            }

            InSandwich = false;
            GameLog.Log($"[Pinball] sandwich end landed={SandwichLanded}/{SandwichBlows}");
        }

        /// <summary>
        /// 挟み撃ちの一発（と締め）を構えさせる。攻撃権は並びや上限を見ずに取らせる
        /// （通常の抽選に任せると、3 発目と締めが「構えられない」で抜けた。実測 landed=2/4）。
        /// </summary>
        bool TryStartBlow(EnemyBrain hitter, float windupScale) =>
            hitter.TryStartPinballPunch(windupScale, scripted: true);

        /// <summary>打てる距離（OpeningStandOff）まで短く詰めて、彼女を向く。瞬間移動にしない。</summary>
        IEnumerator StepIn(EnemyBrain who)
        {
            Vector3 her = _stats.transform.position;
            float side = Mathf.Sign(who.transform.position.x - her.x);
            if (Mathf.Abs(side) < 0.5f)
                side = who == _front ? 1f : -1f;
            Vector3 from = who.transform.position;
            Vector3 to = new Vector3(her.x + OpeningStandOff * side, from.y, her.z);
            Quaternion fromRot = who.transform.rotation;
            Quaternion face = Quaternion.LookRotation(new Vector3(-side, 0f, 0f), Vector3.up);
            who.CancelBystanderWindup(0f);
            const float stepSeconds = 0.25f;
            for (float t = 0f; t < stepSeconds; t += Time.deltaTime)
            {
                if (!_active)
                    yield break;
                // 位置と向きを同じ s で補間する。毎フレームの現在値を起点にすると
                // 向きが指数的にしか追従せず、詰め終わっても彼女を向いていない。
                float s = Mathf.SmoothStep(0f, 1f, t / stepSeconds);
                who.transform.SetPositionAndRotation(
                    Vector3.Lerp(from, to, s),
                    Quaternion.Slerp(fromRot, face, s));
                yield return null;
            }
            who.transform.SetPositionAndRotation(to, face);
        }
        float _shotsUntil;
        float _holdFrameUntil;

        /// <summary>締めのあと寄りを保つ秒数。</summary>
        const float FinishHoldSeconds = 1.2f;

        /// <summary>
        /// 締め: 前の敵が彼女の正面へ詰めて、腹へ実際に一発入れる。当たった瞬間に前へ崩れてダウン。
        /// 以前は離れた位置の前の敵の名義でダメージだけ与えていて、殴る動きがないまま倒れた。
        /// </summary>
        IEnumerator FinishPunch()
        {
            if (_front == null || !_front.IsAlive)
            {
                FinishFallback();
                yield break;
            }

            yield return StepIn(_front);
            if (!_active)
                yield break;

            _finishPending = true;
            if (!TryStartBlow(_front, 1.25f))
            {
                FinishFallback();
                yield break;
            }
            GameLog.Log($"[Pinball] finish punch windup {_front.name}");

            // 命中は OnHitReceived が受ける。来なければ保険で倒す（詰み防止）。
            float deadline = Time.time + 1.6f;
            while (_active && _finishPending && Time.time < deadline)
                yield return null;
            if (_active && _finishPending)
                FinishFallback();
        }

        /// <summary>締めの一発が当たった瞬間。拘束を離して、腹を抱えて前へ崩れる。</summary>
        void FinishOnHit()
        {
            _finishPending = false;
            _grapple.Release();
            _stats.ForceDown(CombatConstants.DownDuration, "pinball-finish", _front.transform,
                launchDistance: 0f, launchHeight: 0f, forward: true);
            GameLog.Log("[Pinball] finish punch landed → prone down");
            Cancel();
            _holdFrameUntil = Time.time + FinishHoldSeconds;
        }

        /// <summary>締めの一発が出せない・当たらないときの保険（ダメージだけ与えて倒す）。</summary>
        void FinishFallback()
        {
            _finishPending = false;
            var attack = AttackLibrary.Heavy();
            attack.id = "pinball_finish";
            attack.damage = 12f;
            attack.partDamage = 26f;
            _grapple.Release();
            if (_front != null)
                _stats.ReceiveHit(_front.State, attack, _front.transform, heldBellyDamage: 26f, isTagPunch: false);
            _stats.ForceDown(CombatConstants.DownDuration, "pinball-finish",
                _front != null ? _front.transform : null, launchDistance: 0f, launchHeight: 0f, forward: true);
            GameLog.Log("[Pinball] finish fallback → prone down");
            Cancel();
            _holdFrameUntil = Time.time + FinishHoldSeconds;
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

        void StopRounds()
        {
            if (_rounds != null)
            {
                StopCoroutine(_rounds);
                _rounds = null;
            }
            _roundsRunning = false;
        }

        void Cancel()
        {
            _active = false;
            _sandwichHitter = null;
            InSandwich = false;
            StopRounds();
            _finishPending = false;
            _shotsUntil = Time.unscaledTime + 1.5f;
            _stats?.GroundMount?.DelayNextSearch(4f);
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
            // 枚数は preview.py が -pinballburstframes で渡す（挟み撃ちの分だけ長くなった）。
            int frames = LaunchOptions.TryInt("-pinballburstframes", out int n) && n > 0 ? n : 120;
            // 終わった後も 1.5 秒撮る（締めで倒れるところまで見る）。
            while (index < frames && (_active || Time.unscaledTime < _shotsUntil))
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
