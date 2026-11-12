using UnityEngine;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// 起動直後にタイトルを出し、何か押されたら消して開始する。
    /// 既定では出さない（すぐ遊べるようにするため）。<c>-title</c> を付けたときだけ出す。
    /// 操作の説明はプレイ画面の右下（<see cref="MatchHud"/>）に常時出している。
    /// </summary>
    public sealed class TitleGate : MonoBehaviour
    {
        /// <summary>このどれかが付いていたらタイトルを出さない（計測・撮影用）。</summary>
        static readonly string[] SkipFlags =
        {
            "-sfxaudition", "-encountertest", "-grappletest",
            "-holdlight", "-holdright", "-holdattack", "-holdmove", "-holdmash",
            "-hudshot", "-statlog", "-occlog", "-camlog", "-vrmlog"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!LaunchOptions.Has("-title"))
                return;

            foreach (string flag in SkipFlags)
            {
                if (LaunchOptions.HasPrefix(flag))
                    return;
            }

            var go = new GameObject("Title Gate");
            go.AddComponent<TitleGate>();
        }

        Canvas _canvas;
        CanvasGroup _group;
        float _fade = -1f;   // -1 = 待機、0 以上 = フェード中

        string _shotPath;    // -titleshot=... のときだけ、1 秒後に自分を撮って自動開始する
        float _age;
        bool _shot;

        void Awake()
        {
            // 押されるまで時間を止める（アニメーションも止まる）
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _shotPath = LaunchOptions.Value("-titleshot");

            Build();

            Debug.Log("[Title] 表示（何かキー / パッドのボタンで開始）");
        }

        void Update()
        {
            if (_group == null)
                return;

            if (_fade < 0f)
            {
                // カメラ側がカーソルを掴み直すことがあるので、待機中は毎フレーム戻す
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                _age += Time.unscaledDeltaTime;

                // 見た目の確認用: 撮ってから自分で開始する（押せない環境でも検証できる）
                if (_shotPath != null)
                {
                    if (!_shot && _age > 1.0f)
                    {
                        _shot = true;
                        ScreenCapture.CaptureScreenshot(_shotPath);
                        Debug.Log($"[Title] 撮影 {_shotPath}");
                    }

                    if (_age > 2.2f)
                    {
                        _fade = 0f;
                        Debug.Log("[Title] 自動で開始");
                    }

                    return;
                }

                if (!AnyPressed())
                    return;

                _fade = 0f;
                Debug.Log("[Title] 開始");
            }

            // timeScale = 0 なので unscaled で進める
            _fade += Time.unscaledDeltaTime / 0.3f;

            if (_fade >= 1f)
            {
                Finish();
                return;
            }

            _group.alpha = 1f - _fade;
        }

        static bool AnyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame)
                return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame
                || pad.buttonEast.wasPressedThisFrame
                || pad.buttonWest.wasPressedThisFrame
                || pad.buttonNorth.wasPressedThisFrame
                || pad.startButton.wasPressedThisFrame))
                return true;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.anyKeyDown;
#else
            return false;
#endif
        }

        void Finish()
        {
            Time.timeScale = 1f;
            // マウスを奪わない（タイトルから始めてもカーソルは表示したまま）。
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_canvas != null)
                Destroy(_canvas.gameObject);

            Destroy(gameObject);
        }

        /// <summary>
        /// 自動テストからタイトルを飛ばす。
        /// タイトルは <c>Time.timeScale = 0</c> で止めるので、
        /// これを通さないと PlayMode テストではゲームが 1 フレームも進まない。
        /// </summary>
        public void Skip()
        {
            Finish();
        }

        // --- UI -----------------------------------------------------------------

        void Build()
        {
            var canvasGo = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 背景（ゲーム画面は見せない。暗幕で完全に覆う）
            var back = NewRect("Back", canvasGo.transform);
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = Vector2.zero;
            back.offsetMax = Vector2.zero;

            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.055f, 0.06f, 0.085f, 1f);
            backImage.raycastTarget = false;

            NewLabel(canvasGo.transform, "MIO", 132, 150f, new Color(1f, 0.95f, 0.86f, 1f), FontStyle.Normal);
            NewLabel(canvasGo.transform, "何かキーを押して開始", 30, 10f, new Color(0.92f, 0.92f, 0.95f, 0.88f), FontStyle.Normal);
            NewLabel(canvasGo.transform, "A / D または ← / →：移動　J 弱　K 強　L 崩し",
                19, -70f, new Color(0.78f, 0.79f, 0.85f, 0.75f), FontStyle.Normal);

            // クリア記録があれば出す（PlayerPrefs に保存済み）
            float best = PlayerPrefs.GetFloat("Mio.BestClearSeconds.Normal10.v2", 0f);
            if (best > 0f)
            {
                int minutes = Mathf.FloorToInt(best / 60f);
                NewLabel(canvasGo.transform, $"ベスト {minutes}:{best - minutes * 60f:00.0}",
                    22, -125f, new Color(1f, 0.92f, 0.60f, 0.9f), FontStyle.Normal);
            }
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        static void NewLabel(Transform parent, string text, int size, float y, Color color, FontStyle style)
        {
            var rt = NewRect("Label", parent);
            rt.sizeDelta = new Vector2(1400f, size * 2f);
            rt.anchoredPosition = new Vector2(0f, y);

            var label = rt.gameObject.AddComponent<Text>();
            label.font = MatchHud.JapaneseFont;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
