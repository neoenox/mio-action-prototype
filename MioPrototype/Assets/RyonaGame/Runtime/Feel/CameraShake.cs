using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// カメラの揺れ。ThirdPersonCamera が書いた位置・回転の上に、そのフレーム分だけ足す。
    ///
    /// 毎フレーム「基準位置 + 揺れ」になるので、位置は加算でよい。
    /// ただし前フレームで足した分を戻さずに足すと揺れが累積してカメラが飛ぶため、
    /// LateUpdate の先頭で必ず「前回足したオフセットを引く → 新しいオフセットを足す」を行う。
    /// 基準を書く ThirdPersonCamera より後に走る必要があるので実行順を大きく後ろに寄せてある。
    /// </summary>
    [AddComponentMenu("Ryona/Camera Shake")]
    [DefaultExecutionOrder(30000)]
    public sealed class CameraShake : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("揺れの大きさ 1m あたり何度傾けるか")]
        public float RotationPerMeter = 45f;

        [Tooltip("上下の揺れをどれだけ抑えるか（酔いにくくする）")]
        [Range(0f, 1f)] public float VerticalScale = 0.6f;

        [Tooltip("揺れの細かさ（1 秒あたりの振動数）")]
        public float Frequency = 34f;

        float _amplitude;
        float _remaining;
        float _duration;

        float _noiseSeed;

        // 前フレームで実際に足した分。戻すために覚えておく。
        Vector3 _appliedOffset;
        Quaternion _appliedRotation = Quaternion.identity;

        /// <summary>いま揺れているか。</summary>
        public bool IsShaking => _remaining > 0f;

        void Awake()
        {
            _noiseSeed = Random.value * 100f;
        }

        /// <summary>
        /// 揺れを要求する。<paramref name="amplitude"/> は振れ幅（m）、<paramref name="duration"/> は秒。
        /// 複数要求は「振幅は大きい方・残り時間は長い方」で合成する（足し算にすると連打で発散する）。
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
                return;

            // 残っている揺れは減衰済みの値で比べる。古い大きい揺れが尾を引くのを防ぐ。
            float live = _amplitude * Envelope();
            _amplitude = Mathf.Max(live, amplitude);

            _remaining = Mathf.Max(_remaining, duration);
            _duration = Mathf.Max(_duration, _remaining);   // 正規化の基準は常に残り時間以上
        }

        void LateUpdate()
        {
            // 1) 前フレームで足した分を必ず戻す。
            //    これで基準は ThirdPersonCamera が書いた「揺れ無し」の状態に戻る。
            Revert();

            if (_remaining <= 0f)
                return;

            // timeScale が 0（ヒットストップ中）でも揺れは実時間で進める
            _remaining = Mathf.Max(0f, _remaining - Time.unscaledDeltaTime);

            float strength = _amplitude * Envelope();
            if (strength <= 0f)
                return;

            // フレームレートに依存しないよう、乱数ではなくパーリンノイズで振る
            float n = Time.unscaledTime * Frequency;
            float nx = Mathf.PerlinNoise(_noiseSeed, n) * 2f - 1f;
            float ny = Mathf.PerlinNoise(_noiseSeed + 13.7f, n) * 2f - 1f;
            float nz = Mathf.PerlinNoise(_noiseSeed + 41.3f, n) * 2f - 1f;

            Vector3 offset = new Vector3(nx, ny * VerticalScale, nz) * strength;
            Quaternion rotation = Quaternion.Euler(
                nz * strength * RotationPerMeter,
                nx * strength * RotationPerMeter,
                ny * strength * RotationPerMeter);

            transform.position += offset;
            transform.rotation *= rotation;

            _appliedOffset = offset;
            _appliedRotation = rotation;
        }

        void OnDisable()
        {
            // 無効化されたまま揺れが残らないよう、足した分を戻してから寝る
            Revert();
            _amplitude = 0f;
            _remaining = 0f;
            _duration = 0f;
        }

        void Revert()
        {
            // 足したときの逆順で戻す（position は減算、rotation は逆クォータニオンの後掛け）
            transform.position -= _appliedOffset;
            transform.rotation *= Quaternion.Inverse(_appliedRotation);

            _appliedOffset = Vector3.zero;
            _appliedRotation = Quaternion.identity;
        }

        /// <summary>0..1 の減衰カーブ（2 乗）。</summary>
        float Envelope()
        {
            if (_duration <= 0f)
                return 0f;

            float t = Mathf.Clamp01(_remaining / _duration);
            return t * t;
        }
    }
}
