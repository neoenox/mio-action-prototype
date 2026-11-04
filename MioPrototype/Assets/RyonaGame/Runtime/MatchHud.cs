using UnityEngine;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// 画面 HUD。仕様書 §9 の情報（HP・スタミナ・姿勢・部位）を表示する。
    ///
    /// IMGUI から uGUI に置き換えた。Canvas を実行時に組み立てるので、
    /// シーンの参照（Player / Enemy）はそのまま使える。
    /// 画面の中央には常にキャラクターが立つので、HUD は左下と左上の隅に寄せる。
    /// </summary>
    [AddComponentMenu("Ryona/Match Hud")]
    public sealed class MatchHud : MonoBehaviour
    {
        public PlayerStats Player;
        public EnemyBrain Enemy;

        static readonly Color HpColor = new Color(0.78f, 0.28f, 0.28f);
        static readonly Color StaminaColor = new Color(0.85f, 0.80f, 0.45f);
        static readonly Color PostureColor = new Color(0.55f, 0.70f, 0.90f);
        static readonly Color PartColor = new Color(0.90f, 0.55f, 0.35f);
        static readonly Color BackColor = new Color(0f, 0f, 0f, 0.32f);
        static readonly Color TextColor = new Color(1f, 1f, 1f, 0.80f);

        const float BlockWidth = 320f;

        // 実行時に組み立てる部品
        readonly Image[] _partFills = new Image[3];
        Image _hpFill, _staminaFill, _postureFill, _enemyHpFill;
        Text _statusText, _enemyText;
        Text _encounterText;
        EncounterDirector _encounter;

        // 画面ロック中の案内（右端）。見えない壁で止まったように感じさせない。
        Text _lockText;
        BeltScreenLock _screenLock;
        PlayerMotor _motor;

        /// <summary>ステージ終了の中央表示（通常は非表示）。</summary>
        RectTransform _bannerRoot;
        Text _bannerText;

        // 掴まれている間の表示
        RectTransform _grappleRoot;
        Text _grappleText;
        Image _grappleFill;
        PlayerGrapple _grapple;
        HeldBellyPunchDirector _bellyPunch;

        bool _built;

        void Awake()
        {
            Build();
        }

        void Update()
        {
            if (!_built)
                Build();

            if (Player == null || Player.State == null)
                return;

            var state = Player.State;

            SetFill(_hpFill, state.Hp / state.MaxHp);
            SetFill(_staminaFill, state.Stamina / CombatConstants.MaxStamina);
            SetFill(_postureFill, state.Posture / CombatConstants.MaxPosture);

            _partFills[0].fillAmount = Health(state.Body[BodyPart.Arms]);
            _partFills[1].fillAmount = Health(state.Body[BodyPart.Legs]);
            _partFills[2].fillAmount = Health(state.Body[BodyPart.Torso]);

            if (Enemy != null && Enemy.State != null)
            {
                SetFill(_enemyHpFill, Enemy.State.Hp / Enemy.State.MaxHp);
                _enemyText.text = Enemy.Phase == EnemyPhase.Windup ? "▲ 来るぞ" : "敵";
                _enemyText.color = Enemy.Phase == EnemyPhase.Windup
                    ? new Color(1f, 0.55f, 0.35f, 0.95f)
                    : TextColor;
            }

            if (Player.IsDead)
            {
                _statusText.text = "倒れた ─ R で復帰";
                _statusText.color = new Color(1f, 0.45f, 0.45f, 0.95f);

                if (InputReader.RestartPressed())
                    Player.RestoreAll();
            }
            else if (Player.IsStunned)
            {
                _statusText.text = "怯み中";
                _statusText.color = new Color(1f, 0.85f, 0.45f, 0.95f);
            }
            else
            {
                _statusText.text = string.Empty;
            }

            UpdateEncounter();
            UpdateLockHint();
            UpdateGrapple();
        }

        /// <summary>
        /// 掴まれている間だけ、画面下中央に「連打しろ」とバーを出す。
        /// 数値は PlayerGrapple が持っているので、ここは見た目だけ。
        /// </summary>
        void UpdateGrapple()
        {
            if (_grappleRoot == null)
                return;

            if (_grapple == null)
                _grapple = FindFirstObjectByType<PlayerGrapple>();

            if (_bellyPunch == null)
                _bellyPunch = FindFirstObjectByType<HeldBellyPunchDirector>();

            bool show = _grapple != null && _grapple.IsGrappled && !Player.IsDead;

            if (_grappleRoot.gameObject.activeSelf != show)
                _grappleRoot.gameObject.SetActive(show);

            if (!show)
                return;

            float progress = Mathf.Clamp01(_grapple.Progress);
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);

            // 後ろから掴まれたときは振りほどきにくい（背中側は力が入らない）。
            // 見た目は同じ掴みなので、せめて文字で分かるようにする。
            string where = _grapple.Kind == GrappleKind.Back ? "（後ろから）" : string.Empty;

            // 後ろ拘束は時間切れが無い。代わりに「次の腹パンまで」を出す。
            string tail;
            bool danger = false;

            if (_grapple.HasTimeLimit)
            {
                tail = $"あと {_grapple.Remaining:F1}s";
            }
            else if (_bellyPunch == null)
            {
                tail = "連打で逃げる";
            }
            else if (_bellyPunch.IsPerforming)
            {
                string what = _bellyPunch.LastLandedZone == PunchZone.Face ? "顔面！" : "腹パン！";
                tail = $"{what} {_bellyPunch.ComboHitsLanded}/{_bellyPunch.ComboHitsTotal}";
                danger = true;
            }
            else if (_bellyPunch.IsPunchIncoming)
            {
                tail = "パンチが来る！";
                danger = true;
            }
            else if (_bellyPunch.HasAttacker)
            {
                tail = $"パンチまで {_bellyPunch.NextPunchRemaining:F1}s";
                danger = _bellyPunch.NextPunchRemaining <= 0.5f;
            }
            else
            {
                tail = "別の敵なし";
            }

            _grappleText.text =
                $"掴まれている{where} ─ 連打！  {_grapple.InputCount}/{need}　{tail}";

            _grappleText.color = danger
                ? new Color(1f, 0.40f, 0.32f, 0.98f)
                : Color.Lerp(
                    new Color(1f, 0.62f, 0.48f, 0.98f),
                    new Color(0.72f, 1f, 0.70f, 0.98f),
                    progress);

            _grappleFill.fillAmount = progress;
            _grappleFill.color = Color.Lerp(
                new Color(0.95f, 0.45f, 0.35f, 0.95f),
                new Color(0.55f, 0.95f, 0.55f, 0.95f),
                progress);
        }

        /// <summary>
        /// 画面ロック中の案内。ロックの間は右端に薄く出し、右の端に近づいたら明るく点滅させる。
        /// </summary>
        void UpdateLockHint()
        {
            if (_lockText == null)
                return;

            if (_screenLock == null)
                _screenLock = FindFirstObjectByType<BeltScreenLock>();
            if (_motor == null)
                _motor = FindFirstObjectByType<PlayerMotor>();

            bool locked = _screenLock != null && _screenLock.IsLocked && _motor != null
                && (_encounter == null || !_encounter.StageCleared);

            if (_lockText.gameObject.activeSelf != locked)
                _lockText.gameObject.SetActive(locked);

            if (!locked)
                return;

            // 右の端までの残り。近いほど明るく、端では点滅する。
            float room = _motor.ForwardLimitX - _motor.transform.position.x;
            float near = Mathf.Clamp01(1f - room / 1.5f);
            float pulse = room < 0.35f ? 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            float alpha = Mathf.Lerp(0.45f, 1f, near) * pulse;
            _lockText.color = new Color(1f, 0.85f, 0.45f, alpha);
        }

        /// <summary>撃破数と「全滅 → 次の波」のカウントダウン。</summary>
        void UpdateEncounter()
        {
            if (_encounterText == null)
                return;

            if (_encounter == null)
                _encounter = FindFirstObjectByType<EncounterDirector>();

            if (_encounter == null)
            {
                _encounterText.text = string.Empty;
                return;
            }

            if (_encounter.Cleared)
            {
                _encounterText.text = "全滅 → 右へ進め";
                _encounterText.color = new Color(1f, 0.85f, 0.45f, 0.95f);
            }
            else
            {
                _encounterText.text = $"撃破 {_encounter.Kills}　残り {_encounter.Alive}";
                _encounterText.color = TextColor;
            }

            // ステージ終了（決められた数の一団を片付けた）
            if (_bannerRoot != null)
            {
                bool done = _encounter.StageCleared;

                if (_bannerRoot.gameObject.activeSelf != done)
                    _bannerRoot.gameObject.SetActive(done);

                if (done && _bannerText != null)
                {
                    _bannerText.text =
                        "STAGE CLEAR\n" +
                        $"<size=26>タイム {Clock(_encounter.ClearTimeSeconds)}　" +
                        $"ベスト {Clock(_encounter.BestClearSeconds)}</size>";
                    _bannerText.color = new Color(1f, 0.92f, 0.60f, 1f);
                    _encounterText.text = $"撃破 {_encounter.Kills}　全 {_encounter.Wave} 波";
                    _encounterText.color = new Color(1f, 0.92f, 0.60f, 0.95f);
                }
            }
        }

        /// <summary>秒を 0:00.0 の形にする（クリアタイム表示用）。</summary>
        static string Clock(float seconds)
        {
            if (seconds <= 0f)
                return "--:--";

            int minutes = Mathf.FloorToInt(seconds / 60f);
            return $"{minutes}:{seconds - minutes * 60f:00.0}";
        }

        static float Health(float damage) => 1f - Mathf.Clamp01(damage / CombatConstants.PartMax);

        static void SetFill(Image image, float normalized)
        {
            if (image != null)
                image.fillAmount = Mathf.Clamp01(normalized);
        }

        void Build()
        {
            if (_built)
                return;

            _built = true;

            // Canvas
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasGo.transform;

            // 敵のブロック（左上）
            var enemyBlock = NewRect("EnemyBlock", root, 26f, 16f, BlockWidth, 48f,
                new Vector2(0f, 1f));

            _enemyHpFill = NewBar(enemyBlock, 0f, 0f, BlockWidth, 12f, HpColor);
            _enemyText = NewText(enemyBlock, 0f, -18f, 220f, 20f, "敵", 16, TextAnchor.UpperLeft);

            // 撃破数（右上）
            _encounterText = NewTextRight(root, 26f, 16f, 460f, 24f, string.Empty, 17);

            // ステージ終了の表示（中央）。通常は非表示
            _bannerRoot = NewRect("BannerBlock", root, 0f, 0f, 900f, 150f,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            var bannerBack = NewRect("Back", _bannerRoot, 0f, 0f, 900f, 150f,
                new Vector2(0f, 1f), new Vector2(0f, 1f));
            var bannerBackImage = bannerBack.gameObject.AddComponent<Image>();
            bannerBackImage.sprite = WhiteSprite;
            bannerBackImage.color = new Color(0f, 0f, 0f, 0.45f);
            bannerBackImage.raycastTarget = false;

            _bannerText = NewText(_bannerRoot, 0f, 0f, 900f, 150f, string.Empty, 56, TextAnchor.MiddleCenter);
            _bannerRoot.gameObject.SetActive(false);

            // 掴まれている間の表示（中央下）。通常は非表示
            _grappleRoot = NewRect("GrappleBlock", root, 0f, 232f, 520f, 56f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            // 文字がキャラクターに重なると読みにくいので、薄い下地を敷く
            var grappleBack = NewRect("Back", _grappleRoot, 0f, 0f, 520f, 56f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var grappleBackImage = grappleBack.gameObject.AddComponent<Image>();
            grappleBackImage.sprite = WhiteSprite;
            grappleBackImage.color = new Color(0f, 0f, 0f, 0.35f);
            grappleBackImage.raycastTarget = false;

            _grappleText = NewText(_grappleRoot, 0f, 6f, 520f, 26f, string.Empty, 20, TextAnchor.UpperCenter);
            _grappleFill = NewBar(_grappleRoot, 0f, 34f, 520f, 10f, new Color(0.95f, 0.45f, 0.35f));
            _grappleRoot.gameObject.SetActive(false);

            // 操作の説明（右下）。タイトルを出さないので、プレイ画面に常に置く。
            var helpRect = NewRect("ControlsHelp", root, 26f, 22f, 420f, 150f,
                new Vector2(1f, 0f), new Vector2(1f, 0f));
            var help = helpRect.gameObject.AddComponent<Text>();
            help.font = JapaneseFont;
            help.fontSize = 16;
            help.alignment = TextAnchor.LowerRight;
            help.color = new Color(TextColor.r, TextColor.g, TextColor.b, 0.72f);
            help.raycastTarget = false;
            help.horizontalOverflow = HorizontalWrapMode.Overflow;
            help.verticalOverflow = VerticalWrapMode.Overflow;
            help.text = "A / D　移動\nJ 弱　K 強　L 崩し\nShift ガード\n掴まれたら J / K / L 連打\nEsc 一時停止";

            // 画面ロック中の案内（右端）。通常は非表示
            var lockRect = NewRect("LockHint", root, 26f, 0f, 460f, 36f,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            _lockText = lockRect.gameObject.AddComponent<Text>();
            _lockText.font = JapaneseFont;
            _lockText.fontSize = 24;
            _lockText.alignment = TextAnchor.MiddleRight;
            _lockText.raycastTarget = false;
            _lockText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _lockText.verticalOverflow = VerticalWrapMode.Overflow;
            _lockText.text = "敵を全員倒すと進める ▶";
            lockRect.gameObject.SetActive(false);

            // 自キャラのブロック（左下）
            var playerBlock = NewRect("PlayerBlock", root, 26f, 22f, BlockWidth, 150f,
                new Vector2(0f, 0f), new Vector2(0f, 0f));

            _statusText = NewText(playerBlock, 0f, 0f, BlockWidth, 22f, string.Empty, 16, TextAnchor.UpperLeft);

            NewText(playerBlock, 0f, 25f, 95f, 20f, "体力", 16, TextAnchor.UpperLeft);
            _hpFill = NewBar(playerBlock, 100f, 29f, BlockWidth - 100f, 12f, HpColor);
            NewText(playerBlock, 0f, 52f, 95f, 20f, "スタミナ", 16, TextAnchor.UpperLeft);
            _staminaFill = NewBar(playerBlock, 100f, 56f, BlockWidth - 100f, 10f, StaminaColor);
            NewText(playerBlock, 0f, 79f, 95f, 20f, "体勢", 16, TextAnchor.UpperLeft);
            _postureFill = NewBar(playerBlock, 100f, 83f, BlockWidth - 100f, 10f, PostureColor);

            float y = 110f;
            float labelWidth = 24f;
            float gap = 6f;
            float barWidth = (BlockWidth - labelWidth * 3f - gap * 2f) / 3f;
            string[] names = { "腕", "脚", "胴" };
            for (int i = 0; i < 3; i++)
            {
                float x = (labelWidth + barWidth + gap) * i;
                NewText(playerBlock, x, y, labelWidth, 20f, names[i], 16, TextAnchor.UpperLeft);
                _partFills[i] = NewBar(playerBlock, x + labelWidth, y + 5f, barWidth - 2f, 9f, PartColor);
            }

            Debug.Log($"[Hud] uGUI 構築完了 font={(JapaneseFont != null ? JapaneseFont.name : "none")} " +
                      $"あ={JapaneseFont != null && JapaneseFont.HasCharacter('あ')} " +
                      $"encounter={(_encounterText != null)}");
        }

        static RectTransform NewRect(string name, Transform parent, float x, float y,
            float width, float height, Vector2 anchor)
        {
            return NewRect(name, parent, x, y, width, height, anchor, anchor);
        }

        static RectTransform NewRect(string name, Transform parent, float x, float y,
            float width, float height, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(
                anchorMin.x < 0.5f ? x : -x,
                anchorMax.y > 0.5f ? -y : y);
            return rt;
        }

        static Image NewBar(Transform parent, float x, float y, float width, float height, Color color)
        {
            // 下地（暗い帯）
            var back = NewRect("Back", parent, x, y, width, height, new Vector2(0f, 1f));
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.sprite = WhiteSprite;
            backImage.color = BackColor;
            backImage.raycastTarget = false;

            // 中身（減るほう）
            var fill = NewRect("Fill", back, 0f, 0f, width, height, new Vector2(0f, 1f));
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = WhiteSprite;
            fillImage.color = new Color(color.r, color.g, color.b, 0.90f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            return fillImage;
        }

        /// <summary>右上に置く右寄せのテキスト。</summary>
        static Text NewTextRight(Transform parent, float x, float y,
            float width, float height, string text, int size)
        {
            var rt = NewRect("Text", parent, x, y, width, height, new Vector2(1f, 1f));

            var label = rt.gameObject.AddComponent<Text>();
            label.font = JapaneseFont;
            label.fontSize = size;
            label.alignment = TextAnchor.UpperRight;
            label.color = TextColor;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        static Text NewText(Transform parent, float x, float y, float width, float height,
            string text, int size, TextAnchor anchor)
        {
            var rt = NewRect("Text", parent, x, y, width, height, new Vector2(0f, 1f));

            var label = rt.gameObject.AddComponent<Text>();
            label.font = JapaneseFont;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = TextColor;
            label.text = text;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        static Sprite _white;

        static Sprite WhiteSprite
        {
            get
            {
                if (_white == null)
                {
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave
                    };

                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(255, 255, 255, 255);

                    texture.SetPixels32(pixels);
                    texture.Apply();

                    _white = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }

                return _white;
            }
        }

        static Font _jpFont;

        /// <summary>
        /// uGUI の既定フォントは日本語のグリフを持たないため、OS の日本語フォントを掴む。
        /// 同梱しないのでビルドも太らず、ライセンス的にも安全。
        /// </summary>
        public static Font JapaneseFont
        {
            get
            {
                if (_jpFont == null)
                {
                    _jpFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic", "Noto Sans JP" },
                        14);
                }

                return _jpFont;
            }
        }
    }
}
