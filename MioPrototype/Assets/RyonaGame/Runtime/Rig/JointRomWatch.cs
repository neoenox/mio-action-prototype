using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 可動域の見張り番。シーン内のプレイヤーと敵へ <see cref="JointRomMonitor"/> を
    /// 自動で付ける。シーン・プレハブの編集は要らない（プレイヤーの VRM は
    /// 読み込み後に <see cref="ProceduralRig"/> が付くので、見つかるまで探し続ける）。
    ///
    /// 有効/無効は起動引数で切り替える:
    /// <list type="bullet">
    /// <item><c>-jointrom</c> … 実行版でも警告を出す（Editor では既定で有効）</item>
    /// <item><c>-nojointrom</c> … 警告を止める</item>
    /// <item><c>-jointlog</c> … 5 秒ごとに逸脱のまとめを 1 行出す（計測も有効になる）</item>
    /// </list>
    /// </summary>
    [AddComponentMenu("Ryona/Joint ROM Watch")]
    public sealed class JointRomWatch : MonoBehaviour
    {
        const float ScanInterval = 0.5f;

        static bool _enabled;
        static bool _logSummary;

        float _timer = ScanInterval;

        /// <summary>警告（<c>-jointrom</c> または Editor）が有効か。</summary>
        public static bool Enabled => _enabled;

        /// <summary>まとめログ（<c>-jointlog</c>）が有効か。</summary>
        public static bool SummaryEnabled => _logSummary;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            _enabled = false;
            _logSummary = false;

            bool forcedOn = false;
            bool forcedOff = false;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-jointrom", StringComparison.OrdinalIgnoreCase))
                    forcedOn = true;
                else if (string.Equals(a, "-nojointrom", StringComparison.OrdinalIgnoreCase))
                    forcedOff = true;
                else if (string.Equals(a, "-jointlog", StringComparison.OrdinalIgnoreCase))
                {
                    _logSummary = true;
                    forcedOn = true;
                }
            }

            _enabled = !forcedOff && (forcedOn || DefaultEnabled());
            if (!_enabled)
                return;

            // シーンを読み直しても（R ではじめから）見張り続けたいので残す。
            // 個々のモニターはシーンと一緒に消えるので、付け直しは次の走査で行う。
            var go = new GameObject("Joint ROM Watch");
            DontDestroyOnLoad(go);
            go.AddComponent<JointRomWatch>();
        }

        static bool DefaultEnabled()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
                return;

            _timer = ScanInterval;
            Scan();
        }

        void Scan()
        {
            AttachPlayer();
            AttachEnemies();
        }

        void AttachPlayer()
        {
            PlayerStats stats = FindFirstObjectByType<PlayerStats>();
            if (stats == null)
                return;

            ProceduralRig rig = stats.GetComponentInChildren<ProceduralRig>(true);
            if (rig == null || !rig.Ready)
                return;

            Attach(rig.gameObject, monitor =>
            {
                monitor.Bind(rig, rig.GetComponent<CharacterAnimator>(), "プレイヤー");
            });
        }

        void AttachEnemies()
        {
            EnemyFigure[] figures = FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
            for (int i = 0; i < figures.Length; i++)
            {
                EnemyFigure figure = figures[i];
                if (figure == null)
                    continue;

                string subject = $"敵{i + 1}";
                Attach(figure.gameObject, monitor =>
                {
                    monitor.Bind(figure, figure.Brain, subject);
                });
            }
        }

        static void Attach(GameObject host, Action<JointRomMonitor> configure)
        {
            if (host == null)
                return;

            JointRomMonitor monitor = host.GetComponent<JointRomMonitor>();
            if (monitor == null)
            {
                monitor = host.AddComponent<JointRomMonitor>();
                configure(monitor);
            }

            monitor.LogSummary = _logSummary;
        }
    }
}
