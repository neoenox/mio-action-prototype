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

        /// <summary>
        /// 顔を殴られたときの既定の配合。Editor の表情シートも同じ値を使う。
        /// 目は強くつぶる（殴られた瞬間に反射で閉じる）。口をわずかに開けて歯を食いしばる。
        /// まぶたが完全に閉じても、眉を寄せ・歯を食いしばったままなら眠っては見えない。
        /// </summary>
        public static PainShape DefaultFacePain => new PainShape(0.85f, 0.62f, 0.20f, 0.95f, 1.00f);

        /// <summary>
        /// 腹パンの既定の配合。眉を寄せたまま口を開け、目を強くつぶって息を漏らす。
        /// 顔ヒット（口を結んで耐える）より「息ができない」顔にする。
        /// 目はギュッと閉じる（痛みで反射的に閉じるので、弱い一撃でも半開きにしない）。
        /// </summary>
        public static PainShape DefaultBellyPain => new PainShape(0.93f, 0.24f, 0.52f, 0.40f, 1.00f);

        /// <summary>被弾の顔で直接動かすブレンドシェイプ（表情シートも同じ名前を使う）。</summary>
        public static readonly string[] HurtShapeNames =
        {
            "Fcl_EYE_Close", "Fcl_EYE_Angry", "Fcl_BRW_Angry", "Fcl_BRW_Sorrow",
            "Fcl_MTH_A", "Fcl_MTH_U", "Fcl_MTH_O",
        };

        /// <summary>
        /// どの Expression にも割り当てられていないので、直接書いても上書きされないシェイプ。
        /// 目を閉じる・母音は Expression（Blink / Aa / Ou / Oh）で動かす（瞳を隠すシェイプも一緒に動くため）。
        /// </summary>
        public static readonly string[] DirectHurtShapes =
        {
            "Fcl_EYE_Angry", "Fcl_BRW_Angry", "Fcl_BRW_Sorrow",
        };

        /// <summary>見比べ用の配合（<c>voice_faces.png</c>）。</summary>
        public static System.Collections.Generic.List<(string, System.Collections.Generic.Dictionary<string, float>)> HurtFaceSheet()
        {
            static System.Collections.Generic.Dictionary<string, float> F(
                float squeeze, float browAngry, float browSorrow, float a, float u, float o) =>
                new System.Collections.Generic.Dictionary<string, float>
                {
                    ["Fcl_EYE_Close"] = 1f,
                    ["Fcl_EYE_Angry"] = squeeze,
                    ["Fcl_BRW_Angry"] = browAngry,
                    ["Fcl_BRW_Sorrow"] = browSorrow,
                    ["Fcl_MTH_A"] = a,
                    ["Fcl_MTH_U"] = u,
                    ["Fcl_MTH_O"] = o,
                };

            // 実機の既定値（インスペクタの初期値）と同じ配合。
            const float sq = 0.8f, ba = 0.7f, bs = 0.45f;
            return new System.Collections.Generic.List<(string, System.Collections.Generic.Dictionary<string, float>)>
            {
                ("閉じるだけ（以前）", F(0f, 0f, 0f, 0f, 0f, 0f)),
                ("強く閉じる", F(sq, ba, bs, 0f, 0f, 0f)),
                ("う（hurt_small）", F(sq, ba, bs, 0f, 0.8f, 0f)),
                ("あ（hurt_large）", F(sq, ba, bs, 0.85f, 0f, 0f)),
                ("お（hurt_belly）", F(sq, ba, bs, 0f, 0f, 0.8f)),
                ("ふ（hurt_belly 後半）", F(sq, ba, bs, 0f, 0.45f, 0f)),
                ("拘束中 お", F(sq, ba * 0.5f, 0.65f, 0f, 0f, 0.8f)),
                ("拘束中 あ", F(sq, ba * 0.5f, 0.65f, 0.85f, 0f, 0f)),
            };
        }

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

        [Header("被弾の瞬間（目を強く閉じる・声に合わせて口を開ける）")]
        [Tooltip("被弾したら最低この秒数は目を強く閉じる。声が鳴っている間はその長さまで延ばす。")]
        public float SqueezeSeconds = 0.45f;

        [Tooltip("拘束中の被弾で目を強く閉じる秒数。連撃（0.55 秒おき）の合間に目を開けて、次の一撃を見せる。声が続いても延ばさない。")]
        public float HeldSqueezeSeconds = 0.32f;

        [Tooltip("閉じた目に力を入れる（Fcl_EYE_Angry）。0 だと眠っているような閉じ方になる。")]
        [Range(0f, 1f)] public float EyeSqueeze = 0.8f;

        [Tooltip("目を閉じている間、眉を寄せる（Fcl_BRW_Angry）。")]
        [Range(0f, 1f)] public float SqueezeBrowAngry = 0.7f;

        [Tooltip("目を閉じている間、眉の内側を上げる（Fcl_BRW_Sorrow）。痛みの眉にする。")]
        [Range(0f, 1f)] public float SqueezeBrowSorrow = 0.45f;

        [Tooltip("声「あ」（hurt_large）の口の最大の開き。")]
        [Range(0f, 1f)] public float VoiceMouthA = 0.85f;

        [Tooltip("声「う」（hurt_small、hurt_belly の「ふ」）の口の最大の開き。")]
        [Range(0f, 1f)] public float VoiceMouthU = 0.8f;

        [Tooltip("声「お」（hurt_belly の「お」）の口の最大の開き。")]
        [Range(0f, 1f)] public float VoiceMouthO = 0.8f;

        static readonly ExpressionKey AngryKey = ExpressionKey.Angry;
        static readonly ExpressionKey SadKey = ExpressionKey.Sad;
        static readonly ExpressionKey MouthKey = ExpressionKey.Aa;
        static readonly ExpressionKey GritKey = ExpressionKey.Ih;
        static readonly ExpressionKey BlinkKey = ExpressionKey.Blink;
        static readonly ExpressionKey VowelUKey = ExpressionKey.Ou;
        static readonly ExpressionKey VowelOKey = ExpressionKey.Oh;

        /// <summary>声の母音（口の形）。</summary>
        public enum Vowel { None, A, U, O }

        // --- 被弾の瞬間 ---
        float _squeeze;          // 目を強く閉じる重み（0-1）
        float _squeezeUntil;     // この時刻まで閉じる（unscaled）

        // --- 声に合わせた口 ---
        AudioClip _voiceClip;
        SfxId _voiceId;
        float _voiceStart;       // 鳴らした時刻（unscaled）
        float _voicePitch = 1f;
        VoiceEnvelope _voiceEnv;
        float _voiceOpen;        // いまの口の開き（0-1、声の音量から）
        float _voiceWeight;      // 声の口が苦悶の口を置き換える割合（0-1）
        Vowel _voiceVowel;

        // 直接書くシェイプ（Expression に割り当ての無いもの）
        SkinnedMeshRenderer _eyeAngrySmr;
        int _eyeAngryIndex = -1;
        SkinnedMeshRenderer _browAngrySmr;
        int _browAngryIndex = -1;
        bool _wroteSorrow;

        /// <summary>目を強く閉じている重み（テスト用）。</summary>
        public float Squeeze => _squeeze;

        /// <summary>声に合わせた口の開き（テスト用）。</summary>
        public float VoiceOpen => _voiceOpen * _voiceWeight;

        /// <summary>いまの声の母音（テスト用）。</summary>
        public Vowel VoiceVowel => _voiceWeight > 0.001f ? _voiceVowel : Vowel.None;

        /// <summary>
        /// 声の音量の推移（20ms ごと、最大 1 に正規化）。
        /// 「お」→「ふ」のように 2 つの山がある声は、2 つ目の山から母音を切り替える。
        /// </summary>
        sealed class VoiceEnvelope
        {
            public const float Step = 0.02f;
            public float[] Level;
            public float SoundStart;   // 声が出始める秒（冒頭の無音を飛ばす）
            public float SoundEnd;     // 声が消える秒
            public float SecondPeak = -1f;   // 2 つ目の山の始まり（無ければ負）

            public float At(float t)
            {
                if (Level == null || Level.Length == 0 || t < 0f)
                    return 0f;
                float f = t / Step;
                int i = Mathf.FloorToInt(f);
                if (i >= Level.Length - 1)
                    return i == Level.Length - 1 ? Level[i] : 0f;
                return Mathf.Lerp(Level[i], Level[i + 1], f - i);
            }
        }

        static readonly System.Collections.Generic.Dictionary<AudioClip, VoiceEnvelope> s_envelopes =
            new System.Collections.Generic.Dictionary<AudioClip, VoiceEnvelope>();

        static VoiceEnvelope EnvelopeOf(AudioClip clip)
        {
            if (clip == null)
                return null;
            if (s_envelopes.TryGetValue(clip, out var cached))
                return cached;

            var env = new VoiceEnvelope();
            int channels = Mathf.Max(1, clip.channels);
            var data = new float[clip.samples * channels];
            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
            if (!clip.GetData(data, 0))
            {
                // 読めなければ「鳴っている間ずっと半開き」で代用する
                env.Level = new[] { 0.6f, 0.6f };
                env.SoundStart = 0f;
                env.SoundEnd = clip.length;
                s_envelopes[clip] = env;
                return env;
            }

            int window = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * VoiceEnvelope.Step));
            int frames = clip.samples;
            int count = Mathf.CeilToInt(frames / (float)window);
            env.Level = new float[count];
            float peak = 1e-5f;
            for (int w = 0; w < count; w++)
            {
                double sum = 0;
                int n = 0;
                int end = Mathf.Min(frames, (w + 1) * window);
                for (int f = w * window; f < end; f++)
                {
                    float v = data[f * channels];
                    sum += v * v;
                    n++;
                }
                float rms = n > 0 ? Mathf.Sqrt((float)(sum / n)) : 0f;
                env.Level[w] = rms;
                peak = Mathf.Max(peak, rms);
            }

            // 最大で 1 に揃え、小さい雑音は 0 に落とす
            for (int w = 0; w < count; w++)
                env.Level[w] = Mathf.Clamp01((env.Level[w] / peak - 0.08f) / 0.92f);

            int first = System.Array.FindIndex(env.Level, v => v > 0.12f);
            int last = System.Array.FindLastIndex(env.Level, v => v > 0.12f);
            env.SoundStart = first >= 0 ? first * VoiceEnvelope.Step : 0f;
            env.SoundEnd = last >= 0 ? (last + 1) * VoiceEnvelope.Step : clip.length;

            // 2 つ目の山: 1 つ目の山のあと、いったん 0.15 未満まで落ちてから 0.15 を超えた所
            if (first >= 0)
            {
                bool dipped = false;
                for (int w = first + 1; w < count; w++)
                {
                    if (!dipped && env.Level[w] < 0.15f)
                        dipped = true;
                    else if (dipped && env.Level[w] > 0.15f)
                    {
                        env.SecondPeak = w * VoiceEnvelope.Step;
                        break;
                    }
                }
            }

            s_envelopes[clip] = env;
            Debug.Log($"[Face] voice envelope {clip.name}: sound {env.SoundStart:F2}-{env.SoundEnd:F2}s " +
                      $"second={env.SecondPeak:F2}s");
            return env;
        }

        float _pain;        // いまの濃さ（0-1）
        float _peak;        // このヒットでの最大の濃さ
        float _holdUntil;   // この時刻まではピークへ寄せる（unscaled）

        float _bellyBlend;  // 0=顔ヒットの配合 1=腹パンの配合
        float _bellyTarget; // 直近のヒットが腹なら 1

        PlayerGrapple _grapple;
        SkinnedMeshRenderer _worriedBrow;
        int _worriedBrowIndex;
        bool _mountFearActive;
        float _originalBrowWeight;
        bool _heldFaceActive;
        float _heldImpact;
        float _heldImpactUntil;
        bool _heldGenericHit;

        bool UsesHeldFace => Stats != null && !Stats.IsDead && _grapple != null &&
            _grapple.IsGrappled && !_grapple.IsHandingOff;

        float _blinkTimer = 2f;   // 次のまばたきまで
        float _blinkT = -1f;      // まばたき中だけ 0 以上
        int _blinkCount;          // 動作確認用（最初の数回だけログに出す）
        float _statsRetry;        // PlayerStats の再探索タイマー
        float _eyeClose;          // 0=開いている 1=閉じている（ダウン中）
        bool _eyesShut;           // ログ用
        float _eyeCheckAt = -1f;  // 反映後のブレンドシェイプを読む時刻

        /// <summary>実際に購読している Stats。差し替え時に古い購読を確実に外すために持つ。</summary>
        PlayerStats _subscribed;

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
            Subscribe();
            ProceduralSfx.HurtVoicePlayed -= OnHurtVoice;
            ProceduralSfx.HurtVoicePlayed += OnHurtVoice;
        }

        void OnDisable()
        {
            ProceduralSfx.HurtVoicePlayed -= OnHurtVoice;
            ClearMountFear();
            ClearHeldFace();
            Unsubscribe();
        }

        /// <summary>被弾の声が鳴った。口の形をその声の母音にし、開きを音量に合わせる。</summary>
        void OnHurtVoice(SfxId id, AudioClip clip, float pitch)
        {
            if (clip == null || Stats == null || Stats.IsDead)
                return;

            _voiceClip = clip;
            _voiceId = id;
            _voiceStart = Time.unscaledTime;
            _voicePitch = Mathf.Max(0.1f, pitch);
            _voiceEnv = EnvelopeOf(clip);

            // 声が出ている間は目を開けない。ただし拘束中は連撃の合間に目を開けるので延ばさない。
            if (_voiceEnv != null && !UsesHeldFace)
                _squeezeUntil = Mathf.Max(_squeezeUntil, _voiceStart + _voiceEnv.SoundEnd / _voicePitch);
        }

        /// <summary>この声のこの時刻の母音。「おふっ」は 2 つ目の山から「う」にする。</summary>
        Vowel VowelAt(float t)
        {
            switch (_voiceId)
            {
                case SfxId.HurtSmall: return Vowel.U;
                case SfxId.HurtLarge: return Vowel.A;
                case SfxId.HurtBelly:
                    return _voiceEnv != null && _voiceEnv.SecondPeak > 0f && t >= _voiceEnv.SecondPeak - 0.02f
                        ? Vowel.U : Vowel.O;
                default: return Vowel.A;
            }
        }

        /// <summary>目を強く閉じる重みと、声に合わせた口を毎フレーム進める。</summary>
        void UpdateHurtFace(float dt)
        {
            bool squeezing = Time.unscaledTime < _squeezeUntil && Stats != null && !Stats.IsDead;
            // 閉じるのは一瞬（反射）、開けるのはゆっくり
            _squeeze = Mathf.MoveTowards(_squeeze, squeezing ? 1f : 0f, dt / (squeezing ? 0.03f : 0.15f));

            float targetOpen = 0f;
            float targetWeight = 0f;
            if (_voiceClip != null && _voiceEnv != null)
            {
                float t = (Time.unscaledTime - _voiceStart) * _voicePitch;   // 声の中の秒
                if (t <= _voiceEnv.SoundEnd + 0.05f)
                {
                    // 声が出る少し前から口の形を作る（冒頭の無音のあいだは苦悶の口のまま）
                    if (t >= _voiceEnv.SoundStart - 0.04f)
                    {
                        targetWeight = 1f;
                        targetOpen = _voiceEnv.At(t + 0.02f);   // 口は音より少しだけ先に開く
                        _voiceVowel = VowelAt(t);
                    }
                }
                else
                {
                    _voiceClip = null;
                }
            }

            // 開きは音量どおり速く追い、閉じはわずかに遅らせる（パクパクしない）
            _voiceOpen = Mathf.MoveTowards(_voiceOpen, targetOpen,
                dt / (targetOpen > _voiceOpen ? 0.03f : 0.07f));
            _voiceWeight = Mathf.MoveTowards(_voiceWeight, targetWeight, dt / (targetWeight > 0f ? 0.04f : 0.12f));
            if (_voiceWeight <= 0.001f)
                _voiceVowel = Vowel.None;
        }

        void Start()
        {
            // Start で購読すると VrmCharacterSlot が実行時に足した場合に
            // OnEnable より後に来ることがあるので、二重購読を避けてここで張り直す。
            Subscribe();
            FindWorriedBrow();

            Debug.Log($"[Face] start stats={(Stats != null)} vrm={(Vrm != null)} anim={(Anim != null)}");
        }

        /// <summary>Stats の被弾イベントを購読する（二重購読しない）。</summary>
        void Subscribe()
        {
            if (Stats == null || _subscribed == Stats)
                return;

            Unsubscribe();
            Stats.HitReceived += OnHitReceived;
            _subscribed = Stats;
            _grapple = Stats.GetComponent<PlayerGrapple>();
        }

        void Unsubscribe()
        {
            if (_subscribed != null)
                _subscribed.HitReceived -= OnHitReceived;

            _subscribed = null;
            _grapple = null;
        }

        void OnHitReceived(HitResult result)
        {
            bool heldFace = UsesHeldFace;
            // 締め付けは密着のタイミングで表情を作る。
            if (heldFace && _grapple.IsBearHugContact &&
                _grapple.Captor != null && Stats.LastAttacker == _grapple.Captor.transform)
                return;

            // 殴られたら反射で目を強く閉じる（拘束中も、腹でも顔でも）。
            // 声が続けて鳴れば、その間は閉じたまま延びる（OnHurtVoice、拘束中は延ばさない）。
            if (result.Damage > 0f)
                _squeezeUntil = Mathf.Max(_squeezeUntil,
                    Time.unscaledTime + (heldFace ? HeldSqueezeSeconds : SqueezeSeconds));

            bool belly = Stats != null && Stats.LastHitZone == PunchZone.Body;
            if (heldFace && _grapple.Kind != GrappleKind.BearHug && result.Damage > 0f)
            {
                // 拘束中の腹への一撃は短く反応し、次の一撃までに目を開ける。
                _heldGenericHit = !belly;
                _heldImpactUntil = belly ? Time.unscaledTime + 0.12f : 0f;
                if (belly)
                    return;
            }

            // 腹パンは顔ヒットと別の配合にする。殴られた場所は ReceiveHit の前に
            // EnemyBrain が立てている（投げなどでは null）。
            _bellyTarget = belly ? 1f : 0f;

            float peak = result.Stagger switch
            {
                StaggerTier.Small => 0.66f,
                StaggerTier.Medium => 0.88f,
                StaggerTier.Large => 1f,
                _ => 0.45f
            };
            float hold = result.StunSeconds + HoldExtraSeconds;

            // 腹パンは同じ段でも重く見せる。弱い一撃で表情が薄いと、
            // 5 連続の腹パンが「軽く小突かれている」ように見える。
            if (belly)
            {
                peak += BellyPeakBonus;
                hold += BellyHoldExtraSeconds;
            }

            // 連続で殴られたときは、強いほうを残す（弱い一撃で上書きしない）
            _peak = Mathf.Max(_peak, Mathf.Clamp01(peak));
            _holdUntil = Mathf.Max(_holdUntil, Time.unscaledTime + hold);

            Debug.Log($"[Face] hit tier={result.Stagger} " +
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

            // 目を強く閉じる・声に合わせた口も、Vrm が無くても進める（テストが読めるように）。
            UpdateHurtFace(dt);

            if (Vrm == null)
                return;

            if (Stats == null)
                LinkStats();

            if (PreviewAmount >= 0f)
            {
                ClearMountFear();
                ClearHeldFace();
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

            var mount = Stats != null ? Stats.GroundMount : null;
            if (mount != null && mount.Fear > 0f)
            {
                ApplyMountFear(mount.Fear, mount.HeadRecoil);
                return;
            }
            ClearMountFear();

            bool heldFace = UsesHeldFace;
            if (!heldFace)
            {
                _heldImpactUntil = 0f;
                _heldGenericHit = false;
            }

            if (heldFace && (!_heldGenericHit || _pain <= 0.001f))
            {
                ApplyHeldFace(dt);
            }
            else
            {
                ClearHeldFace();
                // 苦悶が強いあいだはまばたきを止める（目を細めているので二重になる）。
                Apply(_pain, Mathf.Clamp01(1f - _pain * 2f));
            }

            // 「閉じた」と言い切る前に、実際にメッシュへ入った重みを読む。
            if (_eyeCheckAt > 0f && Time.unscaledTime >= _eyeCheckAt)
            {
                _eyeCheckAt = -1f;
                Debug.Log($"[Face] eye close blend={EyeCloseBlend():F0} (0=open 100=shut) eyeClose={_eyeClose:F2}");
            }
        }

        void FindWorriedBrow()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                    continue;

                int index = mesh.GetBlendShapeIndex("Fcl_BRW_Sorrow");
                if (index >= 0 && _worriedBrow == null)
                {
                    _worriedBrow = renderer;
                    _worriedBrowIndex = index;
                }

                // 被弾で目に力を入れる・眉を寄せる（Expression に割り当てが無いので直接書く）
                int eye = mesh.GetBlendShapeIndex("Fcl_EYE_Angry");
                if (eye >= 0 && _eyeAngrySmr == null)
                {
                    _eyeAngrySmr = renderer;
                    _eyeAngryIndex = eye;
                }

                int brow = mesh.GetBlendShapeIndex("Fcl_BRW_Angry");
                if (brow >= 0 && _browAngrySmr == null)
                {
                    _browAngrySmr = renderer;
                    _browAngryIndex = brow;
                }
            }
        }

        void ApplyHeldFace(float dt)
        {
            float worry = _grapple.FrontEscapePoseWeight;
            float effort = _grapple.FrontEscapeEffort * worry;
            // 拘束中の顔面パンチ: 当たった直後に目を閉じ、そのあとは歯を食いしばって睨む。
            float impulse = Anim != null ? Anim.HeldFacePunchImpulse : 0f;
            float brace = Anim != null ? Anim.HeldFacePunchBrace : 0f;
            // 澪の眉専用シェイプなら、困り眉にしても目や口の形を変えずに済む。
            if (_worriedBrow != null)
            {
                if (!_heldFaceActive)
                    _originalBrowWeight = _worriedBrow.GetBlendShapeWeight(_worriedBrowIndex);
                _worriedBrow.SetBlendShapeWeight(_worriedBrowIndex,
                    Mathf.Lerp(65f + 17f * worry + 6f * effort, 34f, brace));
            }
            _heldFaceActive = true;

            bool reacting = _grapple.IsBearHugContact || Time.unscaledTime < _heldImpactUntil
                || impulse > 0.02f || brace > 0.02f;
            _heldImpact = Mathf.MoveTowards(_heldImpact, reacting ? 1f : 0f,
                dt / (reacting ? 0.04f : 0.12f));
            float mouth = Mathf.Lerp(0.14f + 0.035f * worry + 0.035f * effort, 0.30f, _heldImpact);
            float sad = _worriedBrow != null ? 0f : 0.60f + 0.18f * worry;
            // 踏ん張っている間は眉と目に力を入れ、口を食いしばる（やられっぱなしに見せない）。
            float angry = 0.62f * brace;
            float grit = 0.70f * brace;
            WriteShape(new PainShape(angry, sad, mouth + 0.10f * brace, grit, _heldImpact), 1f, _heldImpact);
        }

        void ApplyMountFear(float fear, float hit)
        {
            if (!_mountFearActive && _worriedBrow != null)
                _originalBrowWeight = _worriedBrow.GetBlendShapeWeight(_worriedBrowIndex);
            _mountFearActive = true;
            float blink = Mathf.Lerp(_eyeClose, Mathf.Clamp01(hit * 1.7f), fear);
            float sad = _worriedBrow != null ? 0f : 0.70f * fear;
            WriteShape(new PainShape(0f, sad, 0.10f + 0.22f * fear + 0.11f * hit, 0f, 0f),
                1f, blink, squeezeOverride: hit * fear);
            if (_worriedBrow != null)
                _worriedBrow.SetBlendShapeWeight(_worriedBrowIndex,
                    Mathf.Lerp(_originalBrowWeight, 82f, fear));
            Vrm.Runtime.Expression.SetWeight(ExpressionKey.Surprised, 0.22f * fear * (1f - blink));
        }

        void ClearMountFear()
        {
            if (!_mountFearActive)
                return;

            if (Vrm != null)
                Vrm.Runtime.Expression.SetWeight(ExpressionKey.Surprised, 0f);
            if (_worriedBrow != null)
                _worriedBrow.SetBlendShapeWeight(_worriedBrowIndex, _originalBrowWeight);
            _mountFearActive = false;
        }

        void ClearHeldFace()
        {
            if (!_heldFaceActive)
                return;

            if (_worriedBrow != null)
                _worriedBrow.SetBlendShapeWeight(_worriedBrowIndex, _originalBrowWeight);
            _heldFaceActive = false;
            _heldImpact = 0f;
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
            Subscribe();
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
            var shape = CurrentShape;
            // 倒れている間は目を閉じる（閉じた目でまばたきが混ざらないよう最大値で取る）
            // 痛みの濃さに比例させると、弱い一撃（濃さ 0.66）で 3 分の 2 しか閉じず
            // 「半開きで痛がっている」ように見える。ダメージを受けたらギュッと閉じる。
            float blink = Mathf.Clamp01(shape.Blink * Mathf.Clamp01(amount * 1.45f)
                                        + BlinkAmount() * blinkScale);
            WriteShape(shape, amount, Mathf.Max(blink, _eyeClose));
        }

        void WriteShape(PainShape shape, float amount, float blink, float? squeezeOverride = null)
        {
            // VRM の読み込みが Start より遅れることがあるので、見つかるまで探し直す
            if (_eyeAngrySmr == null || _browAngrySmr == null || _worriedBrow == null)
                FindWorriedBrow();

            // 声が出ている間は、口を声の母音に置き換える。
            // Angry / Sad の表情（Fcl_ALL_*）は口の形も含むので、重ねると「う」の口が消える。
            // その間は眉を直接のシェイプ（下）で作る。
            float voice = _voiceWeight;
            float open = _voiceOpen;
            float a = 0f, u = 0f, o = 0f;
            switch (_voiceVowel)
            {
                case Vowel.A: a = open * VoiceMouthA; break;
                case Vowel.U: u = open * VoiceMouthU; break;
                case Vowel.O: o = open * VoiceMouthO; break;
            }

            var expression = Vrm.Runtime.Expression;
            expression.SetWeight(AngryKey, shape.Angry * amount * (1f - voice));
            expression.SetWeight(SadKey, shape.Sad * amount * (1f - voice));
            expression.SetWeight(MouthKey, Mathf.Lerp(shape.Mouth * amount, a, voice));
            expression.SetWeight(GritKey, shape.Grit * amount * (1f - voice));
            expression.SetWeight(VowelUKey, u * voice);
            expression.SetWeight(VowelOKey, o * voice);

            // 殴られたら目を強く閉じる。まぶたを閉じるだけだと眠っているように見えるので、
            // 目と眉に力を入れる（ギュッとつぶる）。
            float squeeze = squeezeOverride ?? _squeeze;
            expression.SetWeight(BlinkKey, Mathf.Max(blink, squeeze));

            if (_eyeAngrySmr != null)
                _eyeAngrySmr.SetBlendShapeWeight(_eyeAngryIndex, 100f * EyeSqueeze * squeeze);
            if (_browAngrySmr != null)
                _browAngrySmr.SetBlendShapeWeight(_browAngryIndex,
                    100f * SqueezeBrowAngry * squeeze * (_heldFaceActive ? 0.5f : 1f));

            // 眉の内側を上げる。拘束中の困り眉（65）があればそれより弱めない。
            if (_worriedBrow != null && (squeeze > 0.001f || _wroteSorrow))
            {
                float baseSorrow = _heldFaceActive ? 65f : _originalBrowWeight;
                _worriedBrow.SetBlendShapeWeight(_worriedBrowIndex,
                    Mathf.Max(baseSorrow, 100f * SqueezeBrowSorrow * squeeze));
                _wroteSorrow = squeeze > 0.001f;
            }
        }
    }
}
