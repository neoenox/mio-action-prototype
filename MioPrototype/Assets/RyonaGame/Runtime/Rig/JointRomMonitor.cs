using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 人型リグ（プレイヤー / 敵）の関節角度を一定間隔で測り、
    /// 人体の可動域を超えた「曲がり設定」に警告を出す。
    ///
    /// 姿勢を作っているコードの角度を読むのではなく、**実際に曲がったあとの骨**を
    /// 測る。手続きアニメは IK と補間で最終的な角度が決まるので、設定値ではなく
    /// 結果を見ないと「人体に有り得ない折れ方」を取り逃すため。
    ///
    /// 警告は <see cref="Debug.LogWarning"/>（プレイヤーのログ・Editor のコンソール）。
    /// 同じ関節は <see cref="WarnInterval"/> 秒に 1 回だけ出し、ログが埋まらないようにする。
    /// 計測と集計は警告を止めていても続く（<see cref="Tally"/> を見れば現状が分かる）。
    ///
    /// <see cref="JointRomWatch"/> がシーン内のプレイヤーと敵へ自動で付ける。
    /// </summary>
    [AddComponentMenu("Ryona/Joint ROM Monitor")]
    [DefaultExecutionOrder(25000)]
    public sealed class JointRomMonitor : MonoBehaviour
    {
        [Header("表示")]
        [Tooltip("警告に出す名前（例: プレイヤー / 敵2）")]
        public string Subject = "キャラクター";

        [Tooltip("警告を出すか。オフでも計測と集計は続く")]
        public bool LogWarnings = true;

        [Tooltip("計測する間隔（秒）")]
        [Min(0.01f)]
        public float SampleInterval = 0.05f;

        [Tooltip("同じ関節の警告を出し直すまでの間隔（秒）")]
        [Min(0.1f)]
        public float WarnInterval = 2f;

        [Tooltip("1 回の計測で出す警告の上限（姿勢が大きく崩れたときの一斉出力を抑える）")]
        [Min(1)]
        public int MaxWarningsPerSample = 4;

        [Header("まとめログ（-jointlog）")]
        [Tooltip("n 秒ごとに逸脱のまとめを 1 行出す")]
        public bool LogSummary;

        [Tooltip("まとめログの間隔（秒）")]
        [Min(0.5f)]
        public float SummaryInterval = 5f;

        [Header("参照（自動で入る）")]
        public ProceduralRig Rig;
        public CharacterAnimator Anim;
        public EnemyFigure Figure;
        public EnemyBrain Brain;

        readonly List<JointRomReading> _readings = new List<JointRomReading>();
        readonly JointRomTally _tally = new JointRomTally();
        readonly Dictionary<int, float> _lastWarnTime = new Dictionary<int, float>();
        readonly List<JointRomReading> _summaryBuffer = new List<JointRomReading>();

        JointSkeleton _skeleton;
        float _sampleTimer;
        float _summaryTimer;
        float _lastSampleTime = -1f;
        int _topWarnings;
        int _warnings;
        int _samples;

        /// <summary>直近の計測結果（全関節ぶん）。</summary>
        public IReadOnlyList<JointRomReading> Readings => _readings;

        /// <summary>逸脱の集計（関節ごとの最悪値と回数）。</summary>
        public JointRomTally Tally => _tally;

        /// <summary>これまでに出した警告の数。</summary>
        public int WarningCount => _warnings;

        /// <summary>計測した回数。</summary>
        public int SampleCount => _samples;

        /// <summary>直近の計測時刻（<see cref="Time.unscaledTime"/>）。未計測なら負。</summary>
        public float LastSampleTime => _lastSampleTime;

        /// <summary>骨格が組めているか（VRM の読み込み前は false）。</summary>
        public bool HasSkeleton => _skeleton.IsValid;

        /// <summary>直近のまとめログ（テスト・診断用）。</summary>
        public string LastSummary { get; private set; }

        public void Bind(ProceduralRig rig, CharacterAnimator anim, string subject)
        {
            Rig = rig;
            Anim = anim;
            Figure = null;
            Brain = null;
            Subject = subject;
            _skeleton = default;
        }

        public void Bind(EnemyFigure figure, EnemyBrain brain, string subject)
        {
            Figure = figure;
            Brain = brain;
            Rig = null;
            Anim = null;
            Subject = subject;
            _skeleton = default;
        }

        /// <summary>集計と警告回数を消す（シーン遷移やテストの前後で使う）。</summary>
        public void ResetReport()
        {
            _tally.Clear();
            _lastWarnTime.Clear();
            _warnings = 0;
            _samples = 0;
            _topWarnings = 0;
            LastSummary = null;
        }

        /// <summary>
        /// いま 1 回だけ測る。エディタの診断・PlayMode テストから直接呼べる。
        /// <paramref name="emitWarnings"/> を false にすると集計だけする。
        /// </summary>
        public bool SampleNow(bool emitWarnings = true)
        {
            if (!TryBuildSkeleton(out JointSkeleton skeleton))
                return false;

            _skeleton = skeleton;
            JointRomProbe.Measure(_skeleton, _readings);
            if (_readings.Count == 0)
                return false;

            _samples++;
            _lastSampleTime = Time.unscaledTime;

            int emitted = 0;
            for (int i = 0; i < _readings.Count; i++)
            {
                JointRomReading reading = _readings[i];
                if (!reading.IsViolation)
                    continue;

                _tally.Add(reading);

                if (!emitWarnings || !LogWarnings)
                    continue;
                if (emitted >= MaxWarningsPerSample)
                    continue;
                if (!CanWarn(reading))
                    continue;

                Warn(reading);
                emitted++;
            }

            return true;
        }

        /// <summary>集計を 1 行にまとめてログに出す（<see cref="LogSummary"/> 用）。</summary>
        public void LogSummaryNow()
        {
            if (_tally.TrackedKeys == 0)
            {
                LastSummary = $"[JointROM] {Subject}: 可動域の逸脱なし（{_samples} 回計測）";
                return;
            }

            _summaryBuffer.Clear();
            foreach (JointRomReading reading in _tally.WorstReadings)
                _summaryBuffer.Add(reading);
            _summaryBuffer.Sort((a, b) => b.Excess.CompareTo(a.Excess));

            int shown = Mathf.Min(3, _summaryBuffer.Count);
            var text = new System.Text.StringBuilder(160);
            text.Append("[JointROM] ").Append(Subject)
                .Append(": 逸脱 ").Append(_tally.TrackedKeys).Append(" 種 / ")
                .Append(_tally.ViolationSamples).Append(" 回。");
            for (int i = 0; i < shown; i++)
            {
                JointRomReading reading = _summaryBuffer[i];
                if (i > 0)
                    text.Append(" / ");
                text.Append(reading.Describe())
                    .Append(' ').Append(reading.Excess.ToString("0")).Append("°超過");
            }

            LastSummary = text.ToString();
            Debug.Log(LastSummary);
        }

        void LateUpdate()
        {
            // ポーズ中（タイトル・ポーズメニュー・ヒットストップ停止）は姿勢が
            // 変わらないので測り直さない。timeScale = 0 のときに deltaTime が 0 になり、
            // タイマーが進まないだけで実害は無いが、明示的に抜けておく。
            if (Time.timeScale <= 0f)
                return;

            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer <= 0f)
            {
                _sampleTimer = Mathf.Max(0.01f, SampleInterval);
                SampleNow();
            }

            if (!LogSummary)
                return;

            _summaryTimer -= Time.deltaTime;
            if (_summaryTimer > 0f)
                return;

            _summaryTimer = Mathf.Max(0.5f, SummaryInterval);
            if (_tally.ViolationSamples > _topWarnings)
            {
                _topWarnings = _tally.ViolationSamples;
                LogSummaryNow();
            }
        }

        /// <summary>このフレームの警告に添える状態（プレイヤーは状態名、敵はフェーズ）。</summary>
        public string ContextText
        {
            get
            {
                if (Anim != null)
                    return $" [{Anim.Current}]";
                if (Brain != null)
                    return $" [{Brain.Phase}]";
                return string.Empty;
            }
        }

        bool TryBuildSkeleton(out JointSkeleton skeleton)
        {
            skeleton = default;

            if (Rig == null)
            {
                Rig = GetComponentInChildren<ProceduralRig>(true);
                if (Rig == null)
                    Rig = GetComponentInParent<ProceduralRig>();
            }
            if (Rig != null && JointSkeletonBuilder.TryBuild(Rig, out skeleton))
            {
                if (Anim == null)
                    Anim = Rig.GetComponent<CharacterAnimator>();
                return true;
            }

            if (Figure == null)
            {
                Figure = GetComponent<EnemyFigure>();
                if (Figure == null)
                    Figure = GetComponentInParent<EnemyFigure>();
            }
            if (Figure != null && JointSkeletonBuilder.TryBuild(Figure, out skeleton))
            {
                if (Brain == null)
                    Brain = Figure.Brain;
                return true;
            }

            return false;
        }

        bool CanWarn(JointRomReading reading)
        {
            int key = (int)reading.Joint * 3 + (int)reading.Side;
            float now = Time.unscaledTime;
            if (_lastWarnTime.TryGetValue(key, out float last) && now - last < WarnInterval)
                return false;

            _lastWarnTime[key] = now;
            return true;
        }

        void Warn(JointRomReading reading)
        {
            _warnings++;
            string excess = reading.Verdict == JointRomVerdict.OverFlexed
                ? $"{reading.Excess:0}° 超過"
                : $"{reading.Excess:0}° 反対方向";
            Debug.LogWarning($"[JointROM] {Subject} {reading.Describe()}{excess}{ContextText}");
        }
    }
}
