using System;
using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// 被弾したときの苦悶の表情。
    ///
    /// 痛みを「眉を寄せる（angry）＋眉を上げる（sad）＋口を開ける（aa）＋目を細める（blink）」
    /// の重ね合わせで作る。1 つのプリセットだけだと、怒っているのか悲しんでいるのか
    /// はっきりしない表情になる。怯みの強さに合わせて濃さを変え、怯みが終わると戻す。
    ///
    /// 顔ヒットと腹パン（ボディーブロー）では配合を分ける。
    /// 腹パンは息が詰まるので、眉を寄せたまま口を開けて息を漏らし、目を強くつぶる。
    /// 顔ヒット（口を結んで歯を食いしばる）より「息ができない」顔になる。
    ///
    /// 平常時はまばたきだけを動かす（止まった顔は人形に見える）。
    /// 倒れている間は苦悶を保ち、立ち上がるときにゆっくり戻す。
    /// </summary>
    [AddComponentMenu("Ryona/Player Face")]
    public sealed class PlayerFace : MonoBehaviour
    {
        /// <summary>
        /// 苦悶の表情の配合（重み 0-1）。
        /// 眉・口・目の重ね方を 1 組にして、顔ヒットと腹パンで差し替える。
        /// </summary>
        [Serializable]
        public struct PainShape
        {
            [Tooltip("眉を寄せる。痛みの中心。")]
            public float Angry;

            [Tooltip("眉の内側を上げる。怒りだけだと「怒っている」に見えるので足す。")]
            public float Sad;

            [Tooltip("口を開ける（aa）。食いしばりに足す「痛っ」の息。開けすぎると口を尖らせた顔になる。")]
            public float Mouth;

            [Tooltip("歯を食いしばる（ih）。口を横に広げて薄く結ぶ。")]
            public float Grit;

            [Tooltip("目を細める。0 にすると見開いたままになる。")]
            public float Blink;

            public PainShape(float angry, float sad, float mouth, float grit, float blink)
            {
                Angry = angry;
                Sad = sad;
                Mouth = mouth;
                Grit = grit;
                Blink = blink;
            }

            /// <summary>2 つの配合を混ぜる（腹パンの表情へ寄せるのに使う）。</summary>
            public static PainShape Lerp(in PainShape a, in PainShape b, float t)
            {
                t = Mathf.Clamp01(t);
                return new PainShape(
                    Mathf.Lerp(a.Angry, b.Angry, t),
                    Mathf.Lerp(a.Sad, b.Sad, t),
                    Mathf.Lerp(a.Mouth, b.Mouth, t),
                    Mathf.Lerp(a.Grit, b.Grit, t),
                    Mathf.Lerp(a.Blink, b.Blink, t));
            }
        }

        /// <summary>顔を殴られたときの既定の配合。Editor の表情シートも同じ値を使う。</summary>
        public static PainShape DefaultFacePain => new PainShape(0.82f, 0.50f, 0.12f, 0.95f, 0.32f);

        /// <summary>
        /// 腹パンの既定の配合。眉を寄せたまま口を開け、目を強くつぶって息を漏らす。
        /// 顔ヒット（口を結んで耐える）より「息ができない」顔にする。
        /// 目を完全に閉じると眠って見えるので、まぶたの線が残る濃さに留める。
        /// </summary>
        public static PainShape DefaultBellyPain => new PainShape(0.93f, 0.24f, 0.52f, 0.40f, 0.80f);

        [Header("References")]
        public PlayerStats Stats;
        public Vrm10Instance Vrm;

        [Tooltip("倒れている間も顔を歪ませるために見る")]
        public CharacterAnimator Anim;

        [Header("苦悶の作り（重み 0-1）")]
        [Tooltip("顔を殴られたときの配合。")]
        public PainShape FacePain = DefaultFacePain;

        [Header("腹パン（ボディーブロー）の苦悶")]
        [Tooltip("腹を殴られたときの配合。同じ段の顔ヒットより「堪えている」顔にする。")]
        public PainShape BellyPain = DefaultBellyPain;

        [Tooltip("腹ヒットで表情の濃さを底上げする量。弱い腹パンでも苦悶を見せる。")]
        public float BellyPeakBonus = 0.10f;

        [Tooltip("腹ヒットで苦悶を保つ追加時間（秒）。連続の腹パンで表情が戻りきらないようにする。")]
        public float BellyHoldExtraSeconds = 0.18f;

        [Header("速さ（秒）")]
        [Tooltip("歪むまでの時間。短いほど「叩かれた瞬間に顔が歪む」。")]
        public float RaiseSeconds = 0.05f;

        [Tooltip("怯みが終わってから表情が戻るまでの時間。")]
        public float ReleaseSeconds = 0.35f;

        [Tooltip("怯みの硬直が終わったあとも苦悶を保つ追加時間。")]
        public float HoldExtraSeconds = 0.12f;

        [Header("倒れている間")]
        [Tooltip("ダウン中に保つ濃さ。0 にすると怯みが終わった時点で戻る。")]
        public float DownPain = 0.80f;

        [Tooltip("立ち上がり中に残す濃さ。")]
        public float GetUpPain = 0.45f;

        [Header("倒れている間の目")]
        [Tooltip("ダウン中は目を閉じる。倒れて気を失っているように見せる。")]
        public bool CloseEyesWhenDown = true;

        [Tooltip("ダウンの何割まで進んだら目を閉じるか（0=倒れ始め、1=倒れ終わり）。")]
        public float EyeCloseAtDown = 0.38f;

        [Tooltip("立ち上がりの何割まで閉じたままにするか。過ぎたら開ける。")]
        public float EyeOpenAtGetUp = 0.55f;

        [Tooltip("目を閉じる／開くのにかける時間（秒）。")]
        public float EyeCloseSeconds = 0.22f;

        [Header("まばたき")]
        public bool BlinkEnabled = true;

        [Tooltip("まばたきの間隔（秒）。この範囲で毎回ばらつかせる。")]
        public float BlinkIntervalMin = 2.6f;
        public float BlinkIntervalMax = 5.2f;

        [Tooltip("1 回のまばたきの長さ（閉じ始め〜開き終わり）。")]
        public float BlinkSeconds = 0.16f;

        static readonly ExpressionKey AngryKey = ExpressionKey.Angry;
        static readonly ExpressionKey SadKey = ExpressionKey.Sad;
        static readonly ExpressionKey MouthKey = ExpressionKey.Aa;
        static readonly ExpressionKey GritKey = ExpressionKey.Ih;
        static readonly ExpressionKey BlinkKey = ExpressionKey.Blink;

        float _pain;        // いまの濃さ（0-1）
        float _peak;        // このヒットでの最大の濃さ
        float _holdUntil;   // この時刻まではピークへ寄せる（unscaled）

        float _bellyBlend;  // 0=顔ヒットの配合 1=腹パンの配合
        float _bellyTarget; // 直近のヒットが腹なら 1

        float _blinkTimer = 2f;   // 次のまばたきまで
        float _blinkT = -1f;      // まばたき中だけ 0 以上
        int _blinkCount;          // 動作確認用（最初の数回だけログに出す）
        float _statsRetry;        // PlayerStats の再探索タイマー
        float _eyeClose;          // 0=開いている 1=閉じている（ダウン中）
        bool _eyesShut;           // ログ用
        float _eyeCheckAt = -1f;  // 反映後のブレンドシェイプを読む時刻

        /// <summary>
        /// 0 以上にすると、その濃さで固定する（撮影・確認用）。負で通常動作。
        /// </summary>
        public float PreviewAmount = -1f;

        /// <summary>
        /// 0-1 で腹パンの配合へ寄せる（撮影・確認用）。負なら通常動作。
        /// </summary>
        public float PreviewBelly = -1f;

        /// <summary>腹パンの配合へ寄っている度合い（0=顔ヒット用、1=腹パン用）。</summary>
        public float BellyBlend => _bellyBlend;

        /// <summary>いま使っている苦悶の配合（テスト・ログ用）。</summary>
        public PainShape CurrentShape => PainShape.Lerp(FacePain, BellyPain, _bellyBlend);

        void OnEnable()
        {
            if (Stats != null)
                Stats.HitReceived += OnHitReceived;
        }

        void OnDisable()
        {
            if (Stats != null)
                Stats.HitReceived -= OnHitReceived;
        }

        void Start()
        {
            // Start で購読すると VrmCharacterSlot が実行時に足した場合に
            // OnEnable より後に来ることがあるので、二重購読を避けてここで張り直す。
            if (Stats != null)
            {
                Stats.HitReceived -= OnHitReceived;
                Stats.HitReceived += OnHitReceived;
            }

            Debug.Log($"[Face] start stats={(Stats != null)} vrm={(Vrm != null)} anim={(Anim != null)}");
        }

        void OnHitReceived(HitResult result)
        {
            // 腹パンは顔ヒットと別の配合にする。殴られた場所は ReceiveHit の前に
            // EnemyBrain が立てている（投げなどでは null）。
            bool belly = Stats != null && Stats.LastHitZone == PunchZone.Body;
            _bellyTarget = belly ? 1f : 0f;

            // ガードで受け止めただけなら、痛みはほぼ無い（少しだけ顔を歪める）
            float peak;
            float hold;
            if (result.Blocked && !result.GuardBroken)
            {
                peak = 0.34f;
                hold = 0.10f;
            }
            else
            {
                peak = result.Stagger switch
                {
                    StaggerTier.Small => 0.66f,
                    StaggerTier.Medium => 0.88f,
                    StaggerTier.Large => 1f,
                    StaggerTier.GuardBreak => 1f,
                    _ => 0.45f
                };
                hold = result.StunSeconds + HoldExtraSeconds;

                // 腹パンは同じ段でも重く見せる。弱い一撃で表情が薄いと、
                // 5 連続の腹パンが「軽く小突かれている」ように見える。
                if (belly)
                {
                    peak += BellyPeakBonus;
                    hold += BellyHoldExtraSeconds;
                }
            }

            // 連続で殴られたときは、強いほうを残す（弱い一撃で上書きしない）
            _peak = Mathf.Max(_peak, Mathf.Clamp01(peak));
            _holdUntil = Mathf.Max(_holdUntil, Time.unscaledTime + hold);

            Debug.Log($"[Face] hit tier={result.Stagger} blocked={result.Blocked} " +
                      $"zone={(belly ? "belly" : "face")} peak={peak:F2} hold={hold:F2}");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 顔ヒット ⇔ 腹パンの配合は、痛みが引くのと同じ速さで戻す。
            // 一瞬で切り替えると、連続で殴られたときに顔がパタパタ変わる。
            // Vrm が無くても進める（テストと状態ログが配合を読めるように）。
            float blendSeconds = _bellyTarget > _bellyBlend ? RaiseSeconds : ReleaseSeconds;
            _bellyBlend = Mathf.MoveTowards(_bellyBlend, _bellyTarget, dt / Mathf.Max(0.01f, blendSeconds));

            if (Vrm == null)
                return;

            if (Stats == null)
                LinkStats();

            if (PreviewAmount >= 0f)
            {
                _pain = Mathf.Clamp01(PreviewAmount);
                if (PreviewBelly >= 0f)
                    _bellyBlend = Mathf.Clamp01(PreviewBelly);
                Apply(_pain, 0f);
                return;
            }

            UpdateBlink(dt);
            UpdateEyesClosed(dt);
            HoldWhileDown();

            if (Time.unscaledTime < _holdUntil)
                _pain = Mathf.MoveTowards(_pain, _peak, dt / Mathf.Max(0.01f, RaiseSeconds));
            else
                _pain = Mathf.MoveTowards(_pain, 0f, dt / Mathf.Max(0.01f, ReleaseSeconds));

            // 溜め終わったら次のヒットに備えてピークを戻す
            if (_pain <= 0.001f)
                _peak = 0f;

            // 苦悶が強いあいだはまばたきを止める（目を細めているので二重になる）
            Apply(_pain, Mathf.Clamp01(1f - _pain * 2f));

            // 「閉じた」と言い切る前に、実際にメッシュへ入った重みを読む。
            if (_eyeCheckAt > 0f && Time.unscaledTime >= _eyeCheckAt)
            {
                _eyeCheckAt = -1f;
                Debug.Log($"[Face] eye close blend={EyeCloseBlend():F0} (0=open 100=shut) eyeClose={_eyeClose:F2}");
            }
        }

        /// <summary>
        /// 実際にメッシュへ書き込まれた「目を閉じる」ブレンドシェイプの重み（0-100）。
        /// Expression の重みは LateUpdate で反映されるので、要求値ではなく結果を読む。
        /// </summary>
        float EyeCloseBlend()
        {
            float best = -1f;

            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                int idx = mesh.GetBlendShapeIndex("Fcl_EYE_Close");
                if (idx >= 0)
                    best = Mathf.Max(best, smr.GetBlendShapeWeight(idx));
            }

            return best;
        }

        /// <summary>
        /// PlayerStats は親（キャラのルート）に付いていることがあるので、
        /// 取れていなければ親まで探しに行く。取れた時点で購読する。
        /// </summary>
        void LinkStats()
        {
            _statsRetry -= Time.unscaledDeltaTime;
            if (_statsRetry > 0f)
                return;

            _statsRetry = 0.5f;

            var found = GetComponentInParent<PlayerStats>();
            if (found == null)
                return;

            Stats = found;
            Stats.HitReceived += OnHitReceived;
            Debug.Log($"[Face] stats linked: {found.name}");
        }

        /// <summary>倒れている／立ち上がっている間は、怯みが切れても顔を歪ませておく。</summary>
        void HoldWhileDown()
        {
            if (Anim == null)
                return;

            float floor = Anim.Current switch
            {
                CharacterAnimator.State.Down => DownPain,
                CharacterAnimator.State.GetUp => GetUpPain,
                _ => 0f
            };

            if (floor <= 0f)
                return;

            _peak = Mathf.Max(_peak, floor);
            _holdUntil = Mathf.Max(_holdUntil, Time.unscaledTime + 0.08f);
        }

        void UpdateBlink(float dt)
        {
            if (!BlinkEnabled)
            {
                _blinkT = -1f;
                return;
            }

            if (_blinkT >= 0f)
            {
                _blinkT += dt;
                if (_blinkT >= BlinkSeconds)
                {
                    _blinkT = -1f;
                    _blinkTimer = UnityEngine.Random.Range(BlinkIntervalMin, BlinkIntervalMax);
                }
                return;
            }

            _blinkTimer -= dt;
            if (_blinkTimer <= 0f)
            {
                _blinkT = 0f;
                if (_blinkCount++ < 3)
                    Debug.Log($"[Face] blink ({_blinkCount})");
            }
        }

        /// <summary>まばたきの閉じ具合（0=開いている、1=閉じている）。</summary>
        float BlinkAmount()
        {
            if (_blinkT < 0f)
                return 0f;

            float u = Mathf.Clamp01(_blinkT / Mathf.Max(0.01f, BlinkSeconds));
            if (u < 0.35f)
                return u / 0.35f;
            if (u < 0.55f)
                return 1f;
            return 1f - (u - 0.55f) / 0.45f;
        }

        /// <summary>
        /// 倒れたら目を閉じる。倒れきる手前で閉じ、立ち上がりの途中で開ける。
        /// </summary>
        void UpdateEyesClosed(float dt)
        {
            float target = 0f;

            if (CloseEyesWhenDown && Anim != null)
            {
                if (Anim.Current == CharacterAnimator.State.Down)
                    target = Progress() > EyeCloseAtDown ? 1f : 0f;
                else if (Anim.Current == CharacterAnimator.State.GetUp)
                    target = Progress() < EyeOpenAtGetUp ? 1f : 0f;
            }

            _eyeClose = Mathf.MoveTowards(_eyeClose, target, dt / Mathf.Max(0.01f, EyeCloseSeconds));

            if (target > 0.5f && !_eyesShut)
            {
                _eyesShut = true;
                _eyeCheckAt = Time.unscaledTime + 0.40f;
                Debug.Log($"[Face] eyes shut (state={Anim.Current} t={Progress():F2})");
            }
            else if (target < 0.5f && _eyesShut)
            {
                _eyesShut = false;
                Debug.Log($"[Face] eyes open (state={Anim.Current} t={Progress():F2})");
            }
        }

        /// <summary>いまの状態の進み具合（0-1）。分からなければ 0。</summary>
        float Progress()
        {
            if (Anim == null || Anim.StateDuration <= 0.01f)
                return 0f;

            return Mathf.Clamp01(Anim.StateTime / Anim.StateDuration);
        }

        /// <summary>濃さ 0-1 を表情に流し込む。エディタのプレビューからも呼べる。</summary>
        /// <param name="blinkScale">まばたきを混ぜる割合。撮影時は 0 にして固定する。</param>
        public void Apply(float amount, float blinkScale = 1f)
        {
            if (Vrm == null)
                return;

            amount = Mathf.Clamp01(amount);
            var expression = Vrm.Runtime.Expression;
            var shape = PainShape.Lerp(FacePain, BellyPain, _bellyBlend);

            expression.SetWeight(AngryKey, shape.Angry * amount);
            expression.SetWeight(SadKey, shape.Sad * amount);
            expression.SetWeight(MouthKey, shape.Mouth * amount);
            expression.SetWeight(GritKey, shape.Grit * amount);
            // 倒れている間は目を閉じる（閉じた目でまばたきが混ざらないよう最大値で取る）
            float blink = Mathf.Clamp01(shape.Blink * amount + BlinkAmount() * blinkScale);
            expression.SetWeight(BlinkKey, Mathf.Max(blink, _eyeClose));
        }
    }
}
