using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 被弾した瞬間、画面の縁が赤くにじむ。
    ///
    /// 三人称だと自分の体は画面の中央に小さく映るだけで、カメラの揺れだけでは
    /// 「殴られた」が伝わりにくい。画面の外周を一瞬染めると、視線を動かさずに
    /// 「痛い」が分かる。中央は空けておく（キャラと敵が見えなくなるため）。
    ///
    /// HUD と同じ IMGUI で描く。uGUI の Canvas を足すとシーンを作り直す必要があり、
    /// ゲーム側の見た目に影響が出るため、独立した 1 枚の板として重ねている。
    /// </summary>
    [AddComponentMenu("Ryona/Hit Flash")]
    [DefaultExecutionOrder(100)]
    public sealed class HitFlash : MonoBehaviour
    {
        public PlayerStats Stats;

        [Header("濃さ")]
        [Tooltip("通常被弾")]
        public float HitAlpha = 0.40f;

        [Tooltip("ガードで受け止めたとき")]
        public float GuardedAlpha = 0.14f;

        [Header("時間")]
        [Tooltip("消えるまでの秒数")]
        public float FadeSeconds = 0.40f;

        [Tooltip("赤の色。暗すぎると汚れに見える")]
        public Color Tint = new Color(0.62f, 0.05f, 0.05f, 1f);

        Texture2D _tex;
        float _peak;
        float _remaining;
        bool _subscribed;

        void Awake()
        {
            _tex = BuildVignette(128);
        }

        void Start()
        {
            Subscribe();
            Debug.Log($"[Feel] 被弾フラッシュ準備完了 stats={(Stats != null)}");
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();

            if (_tex != null)
                Destroy(_tex);
        }

        void Subscribe()
        {
            if (_subscribed)
                return;

            if (Stats == null)
                Stats = GetComponent<PlayerStats>();

            if (Stats == null)
                return;

            Stats.HitReceived += OnHitReceived;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (Stats != null)
                Stats.HitReceived -= OnHitReceived;

            _subscribed = false;
        }

        /// <summary>検証用。いま最大の濃さで光らせて、撮影に写す。</summary>
        public void FlashNow(float alpha)
        {
            _peak = Mathf.Clamp01(alpha);
            _remaining = FadeSeconds;
        }

        void OnHitReceived(HitResult result)
        {
            float amount = result.Blocked ? GuardedAlpha : HitAlpha;

            // 連続で殴られるときは「濃い方」を採用する（足し算にすると真っ赤になる）
            float live = _remaining > 0f ? _peak * Mathf.Clamp01(_remaining / FadeSeconds) : 0f;
            _peak = Mathf.Max(live, amount);
            _remaining = FadeSeconds;
        }

        void Update()
        {
            if (_remaining <= 0f)
                return;

            // ヒットストップ中（timeScale 0）でも消えていくようにする
            _remaining = Mathf.Max(0f, _remaining - Time.unscaledDeltaTime);
        }

        void OnGUI()
        {
            if (_tex == null || _remaining <= 0f)
                return;

            float t = Mathf.Clamp01(_remaining / FadeSeconds);
            float alpha = _peak * t * t;
            if (alpha <= 0.001f)
                return;

            Color before = GUI.color;
            GUI.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex,
                ScaleMode.StretchToFill, true);
            GUI.color = before;
        }

        /// <summary>外周だけ濃い放射状のテクスチャを作る（中央は透明）。</summary>
        static Texture2D BuildVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "HitFlashVignette"
            };

            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;

                    // 円ではなく「四角い枠」で濃くする。
                    // 円にすると画面の左右が薄くなり、枠として目に入らない。
                    float r = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

                    // 中央 55% は完全に透明（キャラと敵を見せる）
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.02f, r));
                    a *= a;   // 縁だけを際立たせる

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }

    /// <summary>シーンを作り直さなくても効くように、起動時に被弾フラッシュを差し込む。</summary>
    static class HitFlashBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallSceneHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode) => Boot();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var stats = Object.FindAnyObjectByType<PlayerStats>();
            if (stats == null)
                return;

            if (Object.FindAnyObjectByType<HitFlash>() != null)
                return;

            var go = new GameObject("[HitFlash]");
            var flash = go.AddComponent<HitFlash>();
            flash.Stats = stats;
        }
    }
}
