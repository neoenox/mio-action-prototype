using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 放置プレイ（入力なし）の検証用。<c>-idleburst=&lt;prefix&gt;</c> で起動すると、
    /// 何も仕込まずに通常の対戦を流したまま、一定間隔で画面を撮る。
    /// 「放っておくと拘束から抜けられずにリョナられ続ける」が成り立っているかを、
    /// 拘束の連鎖・途切れ（自由に立っている時間）として見るためのもの。
    /// <c>-idleburstframes</c>（既定 240）と <c>-idleburstinterval</c>（既定 0.5 秒）。撮り終えたら終了する。
    /// </summary>
    [AddComponentMenu("Ryona/Idle Capture Driver")]
    public sealed class IdleCaptureDriver : MonoBehaviour
    {
        string _prefix;
        int _frames = 240;
        float _interval = 0.5f;
        int _index;
        float _next;
        PlayerStats _stats;
        PlayerGrapple _grapple;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string prefix = LaunchOptions.Value("-idleburst");
            if (string.IsNullOrEmpty(prefix) || FindFirstObjectByType<IdleCaptureDriver>() != null)
                return;

            var driver = new GameObject("Idle Capture Driver").AddComponent<IdleCaptureDriver>();
            DontDestroyOnLoad(driver.gameObject);
            driver._prefix = prefix;
            if (LaunchOptions.TryInt("-idleburstframes", out int frames) && frames > 0)
                driver._frames = frames;
            if (LaunchOptions.TryFloat("-idleburstinterval", out float interval) && interval > 0f)
                driver._interval = interval;
            driver._next = Time.unscaledTime + 1f;
        }

        void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next += _interval;

            if (_stats == null)
            {
                _stats = FindFirstObjectByType<PlayerStats>();
                _grapple = _stats != null ? _stats.GetComponent<PlayerGrapple>() : null;
            }

            string dir = System.IO.Path.GetDirectoryName(_prefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(_prefix);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{_index:000}.png"));

            // 1 枚ごとに状態を 1 行残す（拘束の連鎖と途切れを時系列で読むため）。
            if (_stats != null)
            {
                string hold = _grapple != null && _grapple.IsGrappled
                    ? $"{_grapple.Kind}{(_grapple.IsWallPin ? "(wall)" : "")}"
                    : "free";
                GameLog.Log($"[Idle] #{_index:000} t={Time.time:F1} hp={_stats.State.Hp:F0} " +
                            $"down={(_stats.IsDown ? 1 : 0)} dead={(_stats.IsDead ? 1 : 0)} hold={hold}");
            }

            _index++;
            if (_index >= _frames)
                Application.Quit();
        }
    }
}
