using System.Collections;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>テストの待ち時間。</summary>
    internal static class TestTime
    {
        /// <summary>
        /// ゲーム内時間で <paramref name="seconds"/> 秒進める。
        ///
        /// 途中で <c>timeScale</c> が 0 になったら 1 に戻して進める
        /// （タイトルやポーズの残りで止まったままにしない）。
        /// それでも進まないときのために、実時間の上限（<c>seconds * 10 + 5</c> 秒）を置く。
        /// </summary>
        public static IEnumerator WaitGameSeconds(float seconds)
        {
            float end = Time.time + seconds;
            float deadline = Time.realtimeSinceStartup + seconds * 10f + 5f;
            while (Time.time < end && Time.realtimeSinceStartup < deadline)
            {
                if (Time.timeScale <= 0f)
                    Time.timeScale = 1f;

                yield return null;
            }
        }
    }
}
