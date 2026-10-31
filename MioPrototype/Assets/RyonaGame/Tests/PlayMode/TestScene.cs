using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ryona.Tests
{
    /// <summary>
    /// テスト用のシーン読み込み。
    ///
    /// タイトル（<see cref="Ryona.Game.TitleGate"/>）は起動時に
    /// <c>Time.timeScale = 0</c> にして入力を待つ。タイトル自体は
    /// シーンを読み直すと消えるが <c>timeScale</c> は 0 のまま残るので、
    /// そのままでは PlayMode テストが 1 フレームも進まない
    /// （予備動作もヒットストップも観測できず失敗していた）。
    /// 読み込んだら必ず時間を戻す。
    /// </summary>
    internal static class TestScene
    {
        public static IEnumerator LoadPrototype()
        {
            SceneManager.LoadScene("Prototype");
            yield return null;
            yield return null;

            var gate = Object.FindFirstObjectByType<Ryona.Game.TitleGate>();
            if (gate != null)
                gate.Skip();

            Time.timeScale = 1f;

            yield return null;
        }
    }
}
