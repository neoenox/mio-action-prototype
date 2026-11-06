using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ヒットストップ。当たった瞬間だけ時計を止めて、打撃の「重さ」を作る。
    ///
    /// <see cref="Time.timeScale"/> を 0 にするため、戻し忘れるとゲームが二度と動かない。
    /// そこで
    /// ・復帰は unscaled の実時間で測る（timeScale に依存させない）
    /// ・連打されても復帰コルーチンは常に 1 本だけ
    /// ・止める前の timeScale を覚えておき、必ずその値へ戻す
    /// の 3 点で壊れないようにしてある。
    /// シーンに何も置かなくても動くよう、ドライバーは実行時に自動生成する。
    /// </summary>
    public static class HitStop
    {
        static HitStopRunner _runner;

        // 残り時間（unscaled 秒）と、止める前の timeScale（復帰先）
        static float _remaining;
        static float _restoreScale = 1f;

        /// <summary>いま時間が止まっているか。</summary>
        public static bool IsActive => _remaining > 0f;

        /// <summary>
        /// <paramref name="seconds"/> 秒だけ時間を止める。
        /// すでに止まっている最中に呼ばれた場合は、残り時間の長い方を採用する
        /// （短い追撃で止めが切れて、かえって軽く見えるのを防ぐ）。
        /// </summary>
        public static void Play(float seconds)
        {
            if (seconds <= 0f)
                return;

            EnsureRunner();
            if (_runner == null)
                return;

            // timeScale の保存は「止まり始めの 1 回」だけ。
            // 他システムがすでに 0 にしていた場合まで 0 を保存すると復帰できなくなるので 1 に倒す。
            if (_remaining <= 0f)
                _restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;

            _remaining = Mathf.Max(_remaining, seconds);
            Time.timeScale = 0f;
            _runner.EnsureRunning();
        }

        /// <summary>ドライバーから呼ばれる計時。戻り値が false になったら復帰させる。</summary>
        internal static bool Tick(float unscaledDeltaSeconds)
        {
            if (_remaining <= 0f)
                return false;

            _remaining = Mathf.Max(0f, _remaining - unscaledDeltaSeconds);
            return _remaining > 0f;
        }

        /// <summary>timeScale を保存しておいた値へ戻す。何度呼んでも安全。</summary>
        internal static void RestoreNow()
        {
            _remaining = 0f;

            // ポーズ／タイトルが timeScale を握っている間は触らない。
            // ここで保存値（通常 1）を書き戻すと、ヒットストップ中に Esc を押したときに
            // メニューを開いたままゲームが再開してしまう。
            if (PauseMenu.IsPaused)
                return;

            if (UnityEngine.Object.FindFirstObjectByType<TitleGate>() != null)
                return;

            Time.timeScale = _restoreScale;
        }

        static void EnsureRunner()
        {
            if (_runner != null)
                return;

            var go = new GameObject("[HitStopRunner]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<HitStopRunner>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            EnsureRunner();
        }
    }

    /// <summary>
    /// <see cref="HitStop"/> の復帰だけを担当する隠しドライバー。
    /// シーンには置かず、HitStop が自動生成する。
    /// </summary>
    public sealed class HitStopRunner : MonoBehaviour
    {
        Coroutine _routine;

        /// <summary>復帰コルーチンを 1 本だけ走らせる。すでに走っていれば何もしない。</summary>
        internal void EnsureRunning()
        {
            if (!isActiveAndEnabled)
                return;

            if (_routine != null)
                return;

            _routine = StartCoroutine(ResumeRoutine());
        }

        IEnumerator ResumeRoutine()
        {
            // timeScale が 0 でも yield return null は毎フレーム帰ってくるので、
            // 計時は unscaledDeltaTime で行う。
            while (HitStop.Tick(Time.unscaledDeltaTime))
                yield return null;

            HitStop.RestoreNow();
            _routine = null;
        }

        void OnDisable()
        {
            // 無効化・破棄・再生終了で止まったままになるのを防ぐ
            StopAllCoroutines();
            _routine = null;
            HitStop.RestoreNow();
        }

        void OnApplicationQuit()
        {
            HitStop.RestoreNow();
        }
    }
}
