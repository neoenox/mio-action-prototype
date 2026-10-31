using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// Esc（パッドはスタート）で一時停止。音量・カメラ感度・やり直し・終了をここにまとめる。
    /// タイトル表示中は動かない（あちらが timeScale を握っているため）。
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject("Pause Menu");
            go.AddComponent<PauseMenu>();
        }

        Canvas _canvas;
        Text _volumeValue;
        Text _sensValue;
        bool _paused;

        // タイトルの有無は毎フレーム探すと重いので 0.25 秒ごとに確認する
        float _titleCheckAt;
        bool _titleAlive;

        // 見た目の確認用（-pauseshot=...）: 2 秒後に自分で開いて撮り、0.4 秒後に閉じる
        string _shotPath;
        float _resumeAt;

        void Awake()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                const string key = "-pauseshot=";

                if (a.StartsWith(key, System.StringComparison.OrdinalIgnoreCase))
                    _shotPath = a.Substring(key.Length);
            }

            Build();
            SetVisible(false);
        }

        void Update()
        {
            // タイトルが出ている間は触らない
            if (Time.unscaledTime >= _titleCheckAt)
            {
                _titleCheckAt = Time.unscaledTime + 0.25f;
                _titleAlive = FindFirstObjectByType<TitleGate>() != null;
            }

            if (_titleAlive)
                return;

            // 見た目の確認: 開く → 撮る → 自動で閉じる
            if (_shotPath != null && Time.unscaledTime >= 2f)
            {
                Pause();
                ScreenCapture.CaptureScreenshot(_shotPath);
                Debug.Log($"[Pause] 撮影 {_shotPath}");
                _shotPath = null;
                _resumeAt = Time.unscaledTime + 0.4f;
            }

            if (_resumeAt > 0f && Time.unscaledTime >= _resumeAt)
            {
                _resumeAt = 0f;
                Resume();
            }

            if (TogglePressed())
            {
                if (_paused)
                    Resume();
                else
                    Pause();

                return;
            }

            if (!_paused)
                return;

            if (LeftPressed())
                StepVolume(-0.1f);

            if (RightPressed())
                StepVolume(0.1f);

            if (UpPressed())
                StepSensitivity(0.5f);

            if (DownPressed())
                StepSensitivity(-0.5f);

            if (InputReader.RestartPressed())
                Restart();

            if (QuitPressed())
                Application.Quit();
        }

        void Pause()
        {
            _paused = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetVisible(true);
            RefreshStatus();
            Debug.Log("[Pause] 一時停止");
        }

        void Resume()
        {
            _paused = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetVisible(false);
            Debug.Log("[Pause] 再開");
        }

        void Restart()
        {
            Time.timeScale = 1f;
            Debug.Log("[Pause] やり直し");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // --- 設定 ---------------------------------------------------------------

        static ProceduralSfx Sfx => ProceduralSfx.Instance;

        static ThirdPersonCamera Belt
        {
            get
            {
                var cam = UnityEngine.Camera.main;
                return cam != null ? cam.GetComponent<ThirdPersonCamera>() : null;
            }
        }

        void StepVolume(float delta)
        {
            if (Sfx == null)
                return;

            Sfx.MasterVolume = Mathf.Clamp01(Sfx.MasterVolume + delta);
            Sfx.Play(SfxId.Step, 1f);   // 変わったことを耳で確認できるように
            RefreshStatus();
        }

        void StepSensitivity(float delta)
        {
            var cam = Belt;
            if (cam == null)
                return;

            cam.MouseSensitivity = Mathf.Clamp(cam.MouseSensitivity + delta, 0.5f, 8f);
            RefreshStatus();
        }

        void RefreshStatus()
        {
            float volume = Sfx != null ? Sfx.MasterVolume : 0.4f;
            var cam = Belt;
            float sensitivity = cam != null ? cam.MouseSensitivity : 2.2f;

            if (_volumeValue != null)
                _volumeValue.text = $"{Mathf.RoundToInt(volume * 100f)} %";

            if (_sensValue != null)
                _sensValue.text = $"{sensitivity:F1}";
        }

        // --- 入力 ---------------------------------------------------------------

        static bool TogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame)
                return true;
#endif
            return false;
        }

        static bool QuitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.qKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        static bool LeftPressed() => AxisPressed(-1);
        static bool RightPressed() => AxisPressed(1);

        static bool UpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame))
                return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            return pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame);
#else
            return false;
#endif
        }

        static bool DownPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame))
                return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            return pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame);
#else
            return false;
#endif
        }

        static bool AxisPressed(int sign)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (sign < 0 && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame))
                    return true;

                if (sign > 0 && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame))
                    return true;
            }

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                var control = sign < 0 ? pad.dpad.left : pad.dpad.right;
                if (control.wasPressedThisFrame)
                    return true;
            }
#endif
            return false;
        }

        // --- UI -----------------------------------------------------------------

        void Build()
        {
            var canvasGo = new GameObject("Pause Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;   // タイトル(500)より下

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var back = NewRect("Back", canvasGo.transform);
            back.anchorMin = Vector2.zero;
            back.anchorMax = Vector2.one;
            back.offsetMin = Vector2.zero;
            back.offsetMax = Vector2.zero;

            var image = back.gameObject.AddComponent<Image>();
            image.color = new Color(0.03f, 0.035f, 0.05f, 0.88f);
            image.raycastTarget = false;

            NewLabel(canvasGo.transform, "PAUSE", 64, 170f, new Color(1f, 0.95f, 0.86f, 1f));

            // 2 行の設定。値は中央で桁を揃え、矢印は右へ固定する
            NewLabel(canvasGo.transform, "音量", 30, 50f, LabelColor, TextAnchor.MiddleRight,
                new Vector2(-260f, 50f), new Vector2(320f, 46f));
            _volumeValue = NewLabel(canvasGo.transform, "40 %", 30, 50f, ValueColor,
                TextAnchor.MiddleCenter, new Vector2(0f, 50f), new Vector2(220f, 46f));
            NewLabel(canvasGo.transform, "← →", 26, 50f, HintColor, TextAnchor.MiddleLeft,
                new Vector2(150f, 50f), new Vector2(160f, 46f));

            NewLabel(canvasGo.transform, "カメラ感度", 30, -10f, LabelColor, TextAnchor.MiddleRight,
                new Vector2(-260f, -10f), new Vector2(320f, 46f));
            _sensValue = NewLabel(canvasGo.transform, "2.2", 30, -10f, ValueColor,
                TextAnchor.MiddleCenter, new Vector2(0f, -10f), new Vector2(220f, 46f));
            NewLabel(canvasGo.transform, "↑ ↓", 26, -10f, HintColor, TextAnchor.MiddleLeft,
                new Vector2(150f, -10f), new Vector2(160f, 46f));

            NewLabel(canvasGo.transform, "R：はじめから　　Q：終了　　Esc：再開", 24, -130f, HintColor);
        }

        void SetVisible(bool visible)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(visible);
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

        static readonly Color LabelColor = new Color(0.93f, 0.93f, 0.96f, 0.95f);
        static readonly Color ValueColor = new Color(1f, 0.95f, 0.72f, 1f);
        static readonly Color HintColor = new Color(0.78f, 0.79f, 0.85f, 0.80f);

        static Text NewLabel(Transform parent, string text, int size, float y, Color color)
        {
            return NewLabel(parent, text, size, y, color, TextAnchor.MiddleCenter,
                new Vector2(0f, y), new Vector2(1400f, size * 2f));
        }

        static Text NewLabel(Transform parent, string text, int size, float y, Color color,
            TextAnchor alignment, Vector2 position, Vector2 sizeDelta)
        {
            var rt = NewRect("Label", parent);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;

            var label = rt.gameObject.AddComponent<Text>();
            label.font = MatchHud.JapaneseFont;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
    }
}
