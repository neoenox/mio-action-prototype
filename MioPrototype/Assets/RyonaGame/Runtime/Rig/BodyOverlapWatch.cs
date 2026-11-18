using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// キャラクター同士（プレイヤーと敵、敵同士）の体が重なっていないかを一定間隔で測り、
    /// 許容を超えるめり込みに警告を出す見張り番。
    ///
    /// <see cref="JointRomWatch"/> と同じく、シーン・プレハブの編集は要らない。
    /// 有効/無効は起動引数で切り替える:
    /// <list type="bullet">
    /// <item><c>-bodyoverlap</c> … 実行版でも警告を出す（Editor / Development Build では既定で有効）</item>
    /// <item><c>-nobodyoverlap</c> … 警告を止める</item>
    /// <item><c>-overlaplog</c> … 5 秒ごとに重なりのまとめを 1 行出す（計測も有効になる）</item>
    /// </list>
    /// </summary>
    [AddComponentMenu("Ryona/Body Overlap Watch")]
    [DefaultExecutionOrder(25100)]
    public sealed class BodyOverlapWatch : MonoBehaviour
    {
        const float ScanInterval = 0.5f;

        struct ActorEntry
        {
            public string Subject;
            public string Context;
            public JointSkeleton Skeleton;
            public float HeightHint;
            public List<BodyCapsule> Capsules;
        }

        static bool _enabled;
        static bool _logSummary;

        /// <summary>自動起動したインスタンス（テスト・診断用）。</summary>
        public static BodyOverlapWatch Instance { get; private set; }

        /// <summary>重なり監視（<c>-bodyoverlap</c> または Editor）が有効か。</summary>
        public static bool Enabled => _enabled;

        /// <summary>まとめログ（<c>-overlaplog</c>）が有効か。</summary>
        public static bool SummaryEnabled => _logSummary;

        [Header("計測")]
        [Tooltip("警告を出すか。オフでも計測と集計は続く")]
        public bool LogWarnings = true;

        [Tooltip("計測する間隔（秒）")]
        [Min(0.01f)]
        public float SampleInterval = 0.05f;

        [Tooltip("同じ部位ペアの警告を出し直すまでの間隔（秒）")]
        [Min(0.1f)]
        public float WarnInterval = 2f;

        [Tooltip("1 回の計測で出す警告の上限（深いめり込みから順に出す）")]
        [Min(1)]
        public int MaxWarningsPerSample = 4;

        [Tooltip("接触として許容するめり込みの余裕（m）")]
        [Min(0f)]
        public float SlackMeters = BodyOverlap.DefaultSlackMeters;

        [Header("まとめログ（-overlaplog）")]
        [Tooltip("n 秒ごとに重なりのまとめを 1 行出す")]
        public bool LogSummary;

        [Tooltip("まとめログの間隔（秒）")]
        [Min(0.5f)]
        public float SummaryInterval = 5f;

        readonly List<ActorEntry> _actors = new List<ActorEntry>();
        readonly List<List<BodyCapsule>> _capsulePool = new List<List<BodyCapsule>>();
        readonly List<BodyOverlapReading> _violations = new List<BodyOverlapReading>();
        readonly List<string> _violationContexts = new List<string>();
        readonly List<int> _sortedIndices = new List<int>();
        readonly BodyOverlapTally _tally = new BodyOverlapTally();
        readonly Dictionary<string, float> _lastWarnTime = new Dictionary<string, float>();
        readonly Dictionary<string, string> _worstContext = new Dictionary<string, string>();
        readonly List<BodyOverlapReading> _summaryBuffer = new List<BodyOverlapReading>();

        float _scanTimer;
        float _sampleTimer;
        float _summaryTimer;
        int _topViolations;
        int _warnings;
        int _samples;

        /// <summary>直近の計測で許容を超えた重なり一覧。</summary>
        public IReadOnlyList<BodyOverlapReading> LastViolations => _violations;

        /// <summary>重なりの集計（ペアごとの最悪値と回数）。</summary>
        public BodyOverlapTally Tally => _tally;

        /// <summary>これまでに出した警告の数。</summary>
        public int WarningCount => _warnings;

        /// <summary>計測した回数。</summary>
        public int SampleCount => _samples;

        /// <summary>直近の計測で対象にしたキャラクター数。</summary>
        public int ActorCount => _actors.Count;

        /// <summary>直近のまとめログ（テスト・診断用）。</summary>
        public string LastSummary { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            _enabled = false;
            _logSummary = false;
            Instance = null;

            bool forcedOff = LaunchOptions.Has("-nobodyoverlap");
            bool forcedOn = LaunchOptions.Has("-bodyoverlap") || LaunchOptions.Has("-overlaplog");
            _logSummary = LaunchOptions.Has("-overlaplog");

            _enabled = !forcedOff && (forcedOn || DefaultEnabled());
            if (!_enabled)
                return;

            var go = new GameObject("Body Overlap Watch");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BodyOverlapWatch>();
            Instance.LogSummary = _logSummary;
        }

        static bool DefaultEnabled()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }

        /// <summary>集計と警告回数を消す（シーン遷移やテストの前後で使う）。</summary>
        public void ResetReport()
        {
            _tally.Clear();
            _worstContext.Clear();
            _lastWarnTime.Clear();
            _warnings = 0;
            _samples = 0;
            _topViolations = 0;
            LastSummary = null;
        }

        /// <summary>
        /// シーン内のアクティブなプレイヤーと敵を集め直す。
        /// </summary>
        public void RefreshActors()
        {
            _actors.Clear();

            PlayerStats stats = FindFirstObjectByType<PlayerStats>();
            if (stats != null && stats.isActiveAndEnabled)
            {
                ProceduralRig rig = stats.GetComponentInChildren<ProceduralRig>(true);
                if (rig != null && rig.Ready && JointSkeletonBuilder.TryBuild(rig, out JointSkeleton playerSkeleton))
                {
                    CharacterAnimator anim = rig.GetComponent<CharacterAnimator>();
                    string ctx = anim != null ? anim.Current.ToString() : string.Empty;
                    AddActor("プレイヤー", ctx, in playerSkeleton, 0f);
                }
            }

            EnemyFigure[] figures = FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
            for (int i = 0; i < figures.Length; i++)
            {
                EnemyFigure figure = figures[i];
                if (figure == null || !figure.isActiveAndEnabled)
                    continue;
                if (!JointSkeletonBuilder.TryBuild(figure, out JointSkeleton enemySkeleton))
                    continue;

                EnemyBrain brain = figure.Brain;
                string ctx = brain != null ? brain.Phase.ToString() : string.Empty;
                AddActor($"敵{i + 1}", ctx, in enemySkeleton, figure.Height);
            }
        }

        /// <summary>
        /// いま 1 回だけ全キャラクターペアの重なりを測る（キャラクター一覧も取り直す）。
        /// <paramref name="emitWarnings"/> を false にすると集計だけする。
        /// </summary>
        public bool SampleNow(bool emitWarnings = true)
        {
            RefreshActors();
            return MeasureActors(emitWarnings);
        }

        bool MeasureActors(bool emitWarnings)
        {
            if (_actors.Count < 2)
                return false;

            for (int i = 0; i < _actors.Count; i++)
            {
                ActorEntry actor = _actors[i];
                if (actor.Skeleton.Root == null || !actor.Skeleton.Root.gameObject.activeInHierarchy)
                {
                    RefreshActors();
                    if (_actors.Count < 2)
                        return false;
                    break;
                }
            }

            for (int i = 0; i < _actors.Count; i++)
            {
                ActorEntry actor = _actors[i];
                BodyOverlapProbe.BuildCapsules(in actor.Skeleton, actor.Capsules, actor.HeightHint);
            }

            _violations.Clear();
            _violationContexts.Clear();
            _samples++;

            for (int i = 0; i < _actors.Count; i++)
            {
                ActorEntry a = _actors[i];
                for (int j = i + 1; j < _actors.Count; j++)
                {
                    ActorEntry b = _actors[j];
                    int beforeCount = _violations.Count;
                    BodyOverlapProbe.MeasureViolations(
                        a.Subject, a.Capsules,
                        b.Subject, b.Capsules,
                        _violations,
                        SlackMeters);

                    if (_violations.Count > beforeCount)
                    {
                        string pairContext = FormatPairContext(a.Context, b.Context);
                        for (int k = beforeCount; k < _violations.Count; k++)
                            _violationContexts.Add(pairContext);
                    }
                }
            }

            if (_violations.Count == 0)
                return true;

            _sortedIndices.Clear();
            for (int i = 0; i < _violations.Count; i++)
            {
                BodyOverlapReading reading = _violations[i];
                _tally.Add(reading);
                string key = BodyOverlapTally.KeyOf(reading);
                if (!_worstContext.ContainsKey(key)
                    || (_tally.WorstOverall.HasValue && reading.ExcessMeters >= _tally.WorstOverall.Value.ExcessMeters))
                {
                    _worstContext[key] = _violationContexts[i];
                }
                _sortedIndices.Add(i);
            }

            if (!emitWarnings || !LogWarnings)
                return true;

            // 深いめり込みから優先して警告する。
            _sortedIndices.Sort((ia, ib) =>
                _violations[ib].ExcessMeters.CompareTo(_violations[ia].ExcessMeters));

            int emitted = 0;
            for (int i = 0; i < _sortedIndices.Count && emitted < MaxWarningsPerSample; i++)
            {
                int idx = _sortedIndices[i];
                BodyOverlapReading reading = _violations[idx];
                if (!CanWarn(reading))
                    continue;

                Warn(reading, _violationContexts[idx]);
                emitted++;
            }

            return true;
        }

        /// <summary>集計を 1 行にまとめてログに出す（<see cref="LogSummary"/> 用）。</summary>
        public void LogSummaryNow()
        {
            if (_tally.TrackedPairs == 0)
            {
                LastSummary = $"[BodyOverlap] 重なり超過なし（{_samples} 回計測）";
                return;
            }

            _summaryBuffer.Clear();
            foreach (BodyOverlapReading reading in _tally.WorstReadings)
                _summaryBuffer.Add(reading);
            _summaryBuffer.Sort((a, b) => b.ExcessMeters.CompareTo(a.ExcessMeters));

            int shown = Mathf.Min(3, _summaryBuffer.Count);
            var text = new System.Text.StringBuilder(160);
            text.Append("[BodyOverlap] 超過 ").Append(_tally.TrackedPairs).Append(" 組 / ")
                .Append(_tally.ViolationSamples).Append(" 回。");
            for (int i = 0; i < shown; i++)
            {
                BodyOverlapReading reading = _summaryBuffer[i];
                if (i > 0)
                    text.Append(" / ");
                text.Append(reading.Describe());
            }

            LastSummary = text.ToString();
            GameLog.Log(LastSummary);
            LogWorstNow();
        }

        /// <summary>重なりを超過した部位ペアごとに最悪値と延べ回数を出す（<c>[BodyOverlapWorst]</c>）。</summary>
        public void LogWorstNow()
        {
            foreach (BodyOverlapReading reading in _tally.WorstReadings)
            {
                _worstContext.TryGetValue(BodyOverlapTally.KeyOf(reading), out string context);
                int count = _tally.CountOf(reading);
                GameLog.Log($"[BodyOverlapWorst] {reading.Describe()}{context} ×{count}");
            }
        }

        void OnApplicationQuit()
        {
            if (LogSummary && _tally.TrackedPairs > 0)
                LogWorstNow();
        }

        void LateUpdate()
        {
            if (Time.timeScale <= 0f)
                return;

            _scanTimer -= Time.deltaTime;
            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = Mathf.Max(0.01f, SampleInterval);
                if (_scanTimer <= 0f || _actors.Count < 2)
                {
                    _scanTimer = ScanInterval;
                    SampleNow();
                }
                else
                {
                    MeasureActors(true);
                }
            }

            if (!LogSummary)
                return;

            _summaryTimer -= Time.deltaTime;
            if (_summaryTimer > 0f)
                return;

            _summaryTimer = Mathf.Max(0.5f, SummaryInterval);
            if (_tally.ViolationSamples > _topViolations)
            {
                _topViolations = _tally.ViolationSamples;
                LogSummaryNow();
            }
        }

        void AddActor(string subject, string context, in JointSkeleton skeleton, float heightHint)
        {
            int index = _actors.Count;
            while (_capsulePool.Count <= index)
                _capsulePool.Add(new List<BodyCapsule>(10));

            _actors.Add(new ActorEntry
            {
                Subject = subject,
                Context = context,
                Skeleton = skeleton,
                HeightHint = heightHint,
                Capsules = _capsulePool[index]
            });
        }

        bool CanWarn(BodyOverlapReading reading)
        {
            string key = BodyOverlapTally.KeyOf(reading);
            float now = Time.unscaledTime;
            if (_lastWarnTime.TryGetValue(key, out float last) && now - last < WarnInterval)
                return false;

            _lastWarnTime[key] = now;
            return true;
        }

        void Warn(BodyOverlapReading reading, string context)
        {
            _warnings++;
            Debug.LogWarning($"[BodyOverlap] {reading.Describe()}{context}");
        }

        static string FormatPairContext(string a, string b)
        {
            bool hasA = !string.IsNullOrEmpty(a);
            bool hasB = !string.IsNullOrEmpty(b);
            if (hasA && hasB)
                return $" [{a} / {b}]";
            if (hasA)
                return $" [{a}]";
            if (hasB)
                return $" [{b}]";
            return string.Empty;
        }
    }
}
