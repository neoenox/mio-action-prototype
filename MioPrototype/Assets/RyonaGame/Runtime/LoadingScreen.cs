using UnityEngine;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// 読み込み中の暗幕と「読み込み中…」。
    /// プレイヤーの VRM を読むあいだ（数秒）、何も無いステージを見せないために出す。
    /// 時間が止まっている間も動くよう、フェードは unscaled で進める。
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        const float FadeSeconds = 0.25f;

        static LoadingScreen _current;

        CanvasGroup _group;
        bool _closing;

        /// <summary>表示する。すでに出ていれば何もしない。</summary>
        public static void Show()
        {
            if (_current != null)
                return;

            var go = new GameObject("Loading Screen");
            _current = go.AddComponent<LoadingScreen>();
            _current.Build();
        }

        /// <summary>フェードして消す。出ていなければ何もしない。</summary>
        public static void Hide()
        {
            if (_current != null)
                _current._closing = true;
        }

        void Update()
        {
            if (!_closing || _group == null)
                return;

            _group.alpha -= Time.unscaledDeltaTime / FadeSeconds;
            if (_group.alpha <= 0f)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_current == this)
                _current = null;
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;   // HUD（100）より上、タイトル（500）より下

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(canvasGo.transform, false);
            var backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = Vector2.zero;
            backRect.anchorMax = Vector2.one;
            backRect.offsetMin = Vector2.zero;
            backRect.offsetMax = Vector2.zero;
            var image = back.GetComponent<Image>();
            image.color = new Color(0.055f, 0.06f, 0.085f, 1f);   // タイトルと同じ暗幕
            image.raycastTarget = false;

            var text = new GameObject("Label", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(canvasGo.transform, false);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(800f, 80f);

            var label = text.GetComponent<Text>();
            label.font = MatchHud.JapaneseFont;
            label.fontSize = 32;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.92f, 0.92f, 0.95f, 0.88f);
            label.text = "読み込み中…";
            label.raycastTarget = false;
        }
    }
}
