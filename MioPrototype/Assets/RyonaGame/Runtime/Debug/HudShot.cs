using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// HUD の見た目確認用（<c>-hudshot &lt;png&gt; -hudshotdelay &lt;秒&gt;</c>）。
    ///
    /// デスクトップのキャプチャだと別ウィンドウが写ってしまうため、
    /// ゲームの中から <see cref="ScreenCapture.CaptureScreenshot(string)"/> で撮る。
    /// 指定秒だけ待ってから撮り、そのまま終了する（通常プレイでは動かない）。
    ///
    /// <c>-hudshotdelays 1.0,1.3,1.6</c> と書くと、その時刻ごとに連番で撮る
    /// （起き上がりのように動きを追いたいとき用。この場合は時間を止めない）。
    /// </summary>
    [AddComponentMenu("Ryona/Hud Shot")]
    public sealed class HudShot : MonoBehaviour
    {
        string _path;
        readonly List<float> _times = new List<float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            string path = Value(args, "-hudshot");
            if (string.IsNullOrEmpty(path))
                return;

            var go = new GameObject("Hud Shot");
            var shot = go.AddComponent<HudShot>();
            shot._path = path;

            string list = Value(args, "-hudshotdelays");
            if (!string.IsNullOrEmpty(list))
            {
                foreach (string part in list.Split(','))
                {
                    if (float.TryParse(part, out float t))
                        shot._times.Add(t);
                }

                shot._times.Sort();
            }

            if (shot._times.Count == 0)
            {
                float delay = 6f;
                string single = Value(args, "-hudshotdelay");
                if (float.TryParse(single, out float seconds))
                    delay = seconds;

                shot._times.Add(delay);
            }
        }

        IEnumerator Start()
        {
            bool many = _times.Count > 1;

            Debug.Log($"[HudShot] {_times.Count} 枚 撮ります path={System.IO.Path.GetFullPath(_path)} " +
                      $"times={string.Join("/", _times)}");

            float elapsed = 0f;

            for (int i = 0; i < _times.Count; i++)
            {
                // 撮影時刻まで待つ（連写のときはゲーム内時間を進めたまま）
                while (elapsed < _times[i])
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!many)
                {
                    // 1 枚だけなら、文字が読めるよう時間を止めてから撮る
                    Time.timeScale = 0f;
                    yield return new WaitForSecondsRealtime(0.2f);
                }

                string full = FileName(i, many);
                ScreenCapture.CaptureScreenshot(full);
                Debug.Log($"[HudShot] 撮影 {i + 1}/{_times.Count} t={elapsed:F2} path={full}");

                yield return new WaitForSecondsRealtime(0.15f);
            }

            yield return new WaitForSecondsRealtime(1.2f);
            Application.Quit();
        }

        /// <summary>連番のときは rise_1.png, rise_2.png … にする。</summary>
        string FileName(int index, bool numbered)
        {
            string full = System.IO.Path.GetFullPath(_path);
            if (!numbered)
                return full;

            string dir = System.IO.Path.GetDirectoryName(full);
            string name = System.IO.Path.GetFileNameWithoutExtension(full);
            string ext = System.IO.Path.GetExtension(full);

            return System.IO.Path.Combine(dir, $"{name}_{index + 1}{ext}");
        }

        static string Value(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, System.StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
