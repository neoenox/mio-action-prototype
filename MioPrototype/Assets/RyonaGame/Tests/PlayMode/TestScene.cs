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
    ///
    /// テスト中は <see cref="AudioListener.volume"/> を 0 にして音を出さない。
    /// 被弾ボイスが何度も鳴って耳障りなため。音量を 0 にしても
    /// <c>ProceduralSfx.HurtVoicePlayed</c> はそのまま発火するので、
    /// 声に合わせた表情のテストは影響を受けない。
    /// </summary>
    internal static class TestScene
    {
        /// <summary>テスト開始前の音量。テストが終わったら戻す。</summary>
        static float _volumeBeforeTests = 1f;
        static bool _silenced;

        /// <summary>
        /// Play に入るたびに音量を戻す。テストを回したあと volume が 0 のまま
        /// エディタの Play に残らないようにするため（ドメインリロードを切っていても安心）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAudioOnPlay()
        {
            AudioListener.volume = 1f;
            _silenced = false;
        }

        /// <summary>テストの間だけ消音する。イベントは止めない（表情のテストが壊れる）。</summary>
        public static void SilenceAudio()
        {
            if (_silenced)
            {
                AudioListener.volume = 0f;
                return;
            }

            _volumeBeforeTests = AudioListener.volume;
            _silenced = true;
            AudioListener.volume = 0f;
        }

        /// <summary>テスト前の音量へ戻す。</summary>
        public static void RestoreAudio()
        {
            if (!_silenced)
                return;

            AudioListener.volume = _volumeBeforeTests;
            _silenced = false;
        }

        public static IEnumerator LoadPrototype()
        {
            SilenceAudio();

            SceneManager.LoadScene("Prototype");
            yield return null;
            yield return null;

            var gate = Object.FindFirstObjectByType<Ryona.Game.TitleGate>();
            if (gate != null)
                gate.Skip();

            Time.timeScale = 1f;

            // シーン側の AudioListener が入れ替わっても音量はグローバルなので、
            // 念のためもう一度 0 にしておく。
            AudioListener.volume = 0f;

            yield return null;
        }
    }
}
