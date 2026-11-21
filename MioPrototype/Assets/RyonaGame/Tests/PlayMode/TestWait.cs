using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace Ryona.Tests
{
    /// <summary>
    /// テストの待機。
    ///
    /// <c>WaitForSeconds</c> は「その秒数だけ待つ」ので、状態が先に変わっても待ち続け、
    /// 遅い環境では足りない。ここでは「条件が成立するまでフレームを進め、上限を超えたら
    /// テストを落とす」形にして、待ち時間を**上限（タイムアウト）**として扱う。
    ///
    /// 物理の滞空・アニメーションの再生位置・音声のように、時間の経過そのものが
    /// 対象の箇所は <c>WaitForSeconds</c> のままにする（条件では置き換えない）。
    ///
    /// 使い方:
    /// <code>
    /// yield return TestWait.UntilOrTimeout(() => !_stats.IsDown, _anim.GetUpSeconds + 0.1f);
    /// </code>
    /// </summary>
    internal static class TestWait
    {
        /// <summary>タイムアウト時の既定のメッセージ。</summary>
        public const string TimeoutMessage = "条件が成立しないままタイムアウトした";

        /// <summary>
        /// <paramref name="condition"/> が true になるまでフレームを進める。
        /// <paramref name="timeoutSeconds"/> を過ぎても成立しなければテストを落とす。
        ///
        /// 時間の尺度は <c>WaitForSeconds</c> と同じゲーム内時間（<see cref="Time.time"/>）で数える。
        /// <c>timeScale</c> が 0 のまま止まったときに無限に待たないよう、実時間の上限
        /// （<c>timeoutSeconds * 10 + 5</c> 秒）も置く（<see cref="TestTime.WaitGameSeconds"/> と同じ扱い）。
        /// </summary>
        /// <param name="condition">成立を待つ条件。1 フレームごとに評価する。</param>
        /// <param name="timeoutSeconds">待つ上限（秒）。短くしない。</param>
        /// <param name="message">タイムアウトしたときのメッセージ。</param>
        public static IEnumerator UntilOrTimeout(
            Func<bool> condition, float timeoutSeconds, string message = null)
        {
            Assert.IsNotNull(condition, "待つ条件が null");

            float deadline = Time.time + timeoutSeconds;
            float hardDeadline = Time.realtimeSinceStartup + timeoutSeconds * 10f + 5f;
            while (!condition() && Time.time < deadline && Time.realtimeSinceStartup < hardDeadline)
                yield return null;

            Assert.IsTrue(condition(), message ?? $"{TimeoutMessage}（{timeoutSeconds:F2} 秒）");
        }

        /// <summary>
        /// <paramref name="count"/> フレーム進める。
        ///
        /// 状態を持たない「1〜2 フレームで反映される」待ちに使う。秒数で書くと
        /// フレームレートで意味が変わる箇所（入力の反映、Animator への適用）向け。
        /// </summary>
        public static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }
    }
}
