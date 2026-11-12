using UnityEngine;
using UnityEngine.UI;

namespace Ryona.Game
{
    /// <summary>
    /// 画面 HUD。体力と、ダウン・腹ダメージの蓄積を表示する。
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
        static readonly Color DownColor = new Color(0.76f, 0.65f, 1f);
        static readonly Color BellyColor = new Color(1f, 0.63f, 0.33f);
        static readonly Color BackColor = new Color(0f, 0f, 0f, 0.32f);
        static readonly Color TextColor = new Color(1f, 1f, 1f, 0.80f);
        static readonly float LightFlinch = AttackLibrary.Light().Flinch;
        static readonly float HeavyFlinch = AttackLibrary.Heavy().Flinch;

        const float BlockWidth = 320f;

        // 実行時に組み立てる部品
        Image _hpFill, _enemyHpFill;
        internal Image _downFill, _bellyFill;
        Text _statusText, _enemyText;
        internal Text _downHitsText, _bellyHitsText;
        internal Text _encounterText;
        EncounterDirector _encounter;

        // 画面ロック中の案内（右端）。見えない壁で止まったように感じさせない。
        Text _lockText;
        BeltScreenLock _screenLock;
        PlayerMotor _motor;

        /// <summary>ステージ終了の中央表示（通常は非表示）。</summary>
        internal RectTransform _bannerRoot;
        internal Text _bannerText;

        // 掴まれている間の表示
        RectTransform _grappleRoot;
        internal Text _grappleText;
        Image _grappleFill;
        PlayerGrapple _grapple;
        HeldBellyPunchDirector _bellyPunch;

        bool _built;
        bool _grappleSubscribed;

        void OnDestroy()
        {
            if (_grapple != null && _grappleSubscribed)
                _grapple.Failed -= OnGrappleReleased;

            _grappleSubscribed = false;
        }

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

            UpdateDownGauges(state);

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
            else if (Player.GodMode)
            {
                _statusText.text = "GOD MODE ─ 体力自動回復";
                _statusText.color = new Color(1f, 0.92f, 0.55f, 0.98f);
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
        /// <summary>押さえつけが時間切れで解放された直後、そのことを 1.5 秒だけ出す残り秒。</summary>
        float _releasedRemaining;

        void UpdateGrapple()
        {
            if (_grappleRoot == null)
                return;

            if (_grapple == null)
            {
                _grapple = FindFirstObjectByType<PlayerGrapple>();
                if (_grapple != null && !_grappleSubscribed)
                {
                    _grapple.Failed += OnGrappleReleased;
                    _grappleSubscribed = true;
                }
            }

            if (_bellyPunch == null)
                _bellyPunch = FindFirstObjectByType<HeldBellyPunchDirector>();

            if (_releasedRemaining > 0f)
                _releasedRemaining = Mathf.Max(0f, _releasedRemaining - Time.unscaledDeltaTime);

            bool grappled = _grapple != null && _grapple.IsGrappled && !Player.IsDead;
            var mount = Player.GroundMount;
            bool mounted = mount != null && mount.BlocksGetUp && !Player.IsDead;
            bool show = grappled || mounted || _releasedRemaining > 0f;

            // 腹パンのコンボ中は腹が画面の中心に来る。中央下のままだと
            // 腹パン表示が当たっている場所に重なるので、コンボ中だけ下へ退ける。
            bool combo = _bellyPunch != null && (_bellyPunch.IsPerforming || _bellyPunch.IsThrowIncoming);
            Vector2 blockPos = _grappleRoot.anchoredPosition;
            blockPos.y = combo || mounted ? 96f : 232f;
            if ((_grappleRoot.anchoredPosition - blockPos).sqrMagnitude > 0.01f)
                _grappleRoot.anchoredPosition = blockPos;

            if (_grappleRoot.gameObject.activeSelf != show)
                _grappleRoot.gameObject.SetActive(show);

            if (!show)
                return;

            if (mounted)
            {
                _grappleText.text = mount.Stage switch
                {
                    GroundMountAttack.Beat.Roll => "仰向けに返される",
                    GroundMountAttack.Beat.Dismount => "押さえ込みが解ける",
                    _ => "マウント ─ 連打で脱出"
                };
                _grappleText.color = new Color(1f, 0.62f, 0.48f, 0.98f);
                _grappleFill.fillAmount = mount.EscapeProgress;
                _grappleFill.color = new Color(0.95f, 0.45f, 0.35f, 0.95f);
                return;
            }

            if (!grappled)
            {
                // 時間切れで解放された直後
                _grappleText.text = "押さえつけから解放された";
                _grappleText.color = new Color(0.72f, 1f, 0.70f, 0.98f);
                _grappleFill.fillAmount = 0f;
                _grappleFill.color = new Color(0.95f, 0.45f, 0.35f, 0.95f);
                return;
            }

            if (_grapple.IsHandingOff)
            {
                _grappleText.text = "受け渡し中";
                _grappleText.color = TextColor;
                _grappleFill.fillAmount = 0f;
                return;
            }

            float progress = Mathf.Clamp01(_grapple.Progress);
            int need = Mathf.CeilToInt(_grapple.RequiredInputs);

            // 後ろから掴まれたときは振りほどきにくい（背中側は力が入らない）。
            // 見た目は同じ掴みなので、せめて文字で分かるようにする。
            string where = _grapple.Kind switch
            {
                GrappleKind.Back => "（後ろから）",
                GrappleKind.Pin => _grapple.IsWallPin ? "（壁に押さえつけ）" : "（押さえつけ）",
                GrappleKind.BearHug => "（ベアハッグ）",
                _ => string.Empty
            };

            // 後ろ拘束は時間切れが無い。代わりに「次の腹パンまで」を出す。
            string tail;
            bool danger = false;

            if (_grapple.IsWallTransition)
            {
                tail = _grapple.WallPhase switch
                {
                    WallStage.Grab => "掴まれた",
                    WallStage.Rise => "壁がせり上がる",
                    WallStage.Throw => "壁へ投げられる",
                    WallStage.Slam => "壁に叩きつけられた",
                    WallStage.Lean => "壁をずり落ちる",
                    WallStage.Regrip => "両腕を掴まれる",
                    WallStage.Restrain => "引き起こされる",
                    WallStage.TossWindup or WallStage.TossAir => "レーンへ投げられる",
                    WallStage.TossLand => "着地",
                    _ => "両手首を押さえられる"
                };
                danger = true;
            }
            else if (_grapple.FrontEscapeRemaining > 0f)
            {
                string attack = _grapple.Kind == GrappleKind.BearHug ? "締め付け" : "膝蹴り";
                tail = $"{attack}まで {_grapple.FrontEscapeRemaining:F1}s";
                danger = _grapple.FrontEscapeRemaining <= 0.5f;
            }
            else if (_grapple.IsPreparingPinHandOff)
            {
                tail = "背後から敵が来る";
                danger = true;
            }
            else if (_grapple.IsWallSplitActive)
            {
                tail = $"2人がかり {_grapple.WallSplitHits}/{CombatConstants.WallSplitPunchCount}";
                danger = true;
            }
            else if (_grapple.HasTimeLimit)
            {
                tail = $"あと {_grapple.Remaining:F1}s で解放";
                // 時間切れは解放なので、危険色にはしない
            }
            else if (_bellyPunch == null)
            {
                tail = "連打で逃げる";
            }
            else if (_bellyPunch.IsPerforming)
            {
                string what = _bellyPunch.IsFaceCombo ? "顔面！" : "腹パン！";
                tail = $"{what} {_bellyPunch.ComboHitsLanded}/{_bellyPunch.ComboHitsTotal}";
                danger = true;
            }
            else if (_bellyPunch.IsThrowIncoming)
            {
                tail = "正面から投げが来る！";
                danger = true;
            }
            else if (_bellyPunch.NextThrowRemaining > 0f)
            {
                float remaining = _bellyPunch.NextThrowRemaining;
                tail = $"投げまで {remaining:F1}s";
                danger = remaining <= 0.5f;
            }
            else
            {
                // 撃ち終えて次の予約も無い（敵がいない等）。抜ける手段は連打だけ。
                tail = "連打で逃げる";
            }

            string escapeHint = _grapple.IsStruggleSuppressed
                ? "連打は一時停止中"
                : "J / K / L を連打して脱出";
            _grappleText.text =
                $"拘束中{where}　{tail}\n{escapeHint}　{_grapple.InputCount}/{need}";

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

        /// <summary>押さえつけが時間切れで解放された。そのことを 1.5 秒だけ出す。</summary>
        void OnGrappleReleased()
        {
            _releasedRemaining = 1.5f;
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

        void UpdateDownGauges(CombatState state)
        {
            SetFill(_downFill, state.Flinch / CombatConstants.MaxFlinch);
            SetFill(_bellyFill, Player.HeldBellyDamage / CombatConstants.MaxHeldBellyDamage);

            float downLeft = Mathf.Max(0f, CombatConstants.MaxFlinch - state.Flinch);
            _downHitsText.text = Player.IsDown ? "ダウン中"
                : downLeft <= 0f ? "上限到達"
                : $"弱あと{Mathf.CeilToInt(downLeft / LightFlinch)}発 / 強あと{Mathf.CeilToInt(downLeft / HeavyFlinch)}発";

            float bellyLeft = Mathf.Max(0f, CombatConstants.MaxHeldBellyDamage - Player.HeldBellyDamage);
            _bellyHitsText.text = Player.HasHeldBellyDown ? "上限到達"
                : $"腹パンあと{Mathf.CeilToInt(bellyLeft / CombatConstants.HeldBellyPunchDamage)}発 / " +
                  $"膝あと{Mathf.CeilToInt(bellyLeft / CombatConstants.HeldKneeBellyDamage)}発";
        }

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
            _grappleRoot = NewRect("GrappleBlock", root, 0f, 232f, 560f, 84f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

            // 文字がキャラクターに重なると読みにくいので、薄い下地を敷く
            var grappleBack = NewRect("Back", _grappleRoot, 0f, 0f, 560f, 84f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var grappleBackImage = grappleBack.gameObject.AddComponent<Image>();
            grappleBackImage.sprite = WhiteSprite;
            grappleBackImage.color = new Color(0f, 0f, 0f, 0.65f);
            grappleBackImage.raycastTarget = false;

            _grappleText = NewText(_grappleRoot, 0f, 4f, 560f, 60f, string.Empty, 24, TextAnchor.UpperCenter);
            _grappleFill = NewBar(_grappleRoot, 0f, 66f, 560f, 10f, new Color(0.95f, 0.45f, 0.35f));
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
            help.text = "A / D　移動\nJ 弱　K 強　L 崩し\nG ゴッドモード\nホイール ズーム\nEsc 一時停止";

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
            var playerBlock = NewRect("PlayerBlock", root, 26f, 22f, BlockWidth, 132f,
                new Vector2(0f, 0f), new Vector2(0f, 0f));

            _statusText = NewText(playerBlock, 0f, 0f, BlockWidth, 22f, string.Empty, 16, TextAnchor.UpperLeft);

            NewText(playerBlock, 0f, 25f, 95f, 20f, "体力", 16, TextAnchor.UpperLeft);
            _hpFill = NewBar(playerBlock, 100f, 29f, BlockWidth - 100f, 12f, HpColor);

            NewText(playerBlock, 0f, 52f, 100f, 20f, "ダウンゲージ", 16, TextAnchor.UpperLeft);
            _downHitsText = NewText(playerBlock, 105f, 52f, BlockWidth - 105f, 20f,
                string.Empty, 15, TextAnchor.UpperRight);
            _downFill = NewBar(playerBlock, 0f, 72f, BlockWidth, 7f, DownColor);

            NewText(playerBlock, 0f, 85f, 100f, 20f, "腹ダメージ", 16, TextAnchor.UpperLeft);
            _bellyHitsText = NewText(playerBlock, 105f, 85f, BlockWidth - 105f, 20f,
                string.Empty, 15, TextAnchor.UpperRight);
            _bellyFill = NewBar(playerBlock, 0f, 105f, BlockWidth, 7f, BellyColor);

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
