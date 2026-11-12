using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>手触り用の効果音の種類。</summary>
    public enum SfxId
    {
        Swing = 0,
        Impact = 1,
        Guard = 2,
        Stagger = 3,
        Break = 4,

        /// <summary>敵の予備動作の合図（小）。踏み込み＋衣擦れ。</summary>
        Tell = 5,

        /// <summary>敵の予備動作の合図（大）。低く重い。</summary>
        TellHeavy = 6,

        /// <summary>鉄パイプで殴られた音。金属の余韻＋胴に来る低音。</summary>
        Pipe = 7,

        /// <summary>足音。歩行の接地に合わせて鳴らす。</summary>
        Step = 8,

        /// <summary>倒れる音。体が床に着くタイミングで鳴らす。</summary>
        BodyDrop = 9,

        /// <summary>ステージクリアの報せ。最後の一団を片付けた瞬間に鳴らす。</summary>
        Clear = 10,

        /// <summary>被弾の声（録音素材 hurt_small）。軽い被弾に使う。</summary>
        HurtSmall = 11,

        /// <summary>被弾の声（録音素材 hurt_large）。重い被弾に使う。</summary>
        HurtLarge = 12,

        /// <summary>被弾の声（録音素材 hurt_belly）。腹への一撃に使う。</summary>
        HurtBelly = 13,
        WallSlam = 14
    }

    /// <summary>
    /// 波形を実行時に合成する効果音。音声アセットを用意せずに打撃音を鳴らすための仕組み。
    ///
    /// クリップは生成コストを避けるため初回（Awake）に 1 度だけ作り、以後は使い回す。
    /// <see cref="AudioClip.Create"/> はメインスレッド専用なので、
    /// 再生時ではなく初期化時にまとめて生成している。
    /// </summary>
    [AddComponentMenu("Ryona/Procedural Sfx")]
    public sealed class ProceduralSfx : MonoBehaviour
    {
        const int SampleRate = 44100;

        // 生成した波形のピーク。ここを 1 にすると合成音が耳に痛いので少し下げる。
        const float ClipPeak = 0.9f;

        /// <summary>自動生成されたインスタンス。シーンに置かなくても動く。</summary>
        public static ProceduralSfx Instance { get; private set; }

        [Header("Volume")]
        [Range(0f, 1f)] public float MasterVolume = 0.4f;

        /// <summary>
        /// 音ごとの基準音量（ミックスの相対バランス）。
        /// 呼び出し側の volume は「同じ音を状況で強弱する」ための倍率なので、
        /// 音そのものの大小はここで決める。数値をいじるならこの表だけ。
        /// </summary>
        public static float Gain(SfxId id) => id switch
        {
            // 風切り音は広帯域ノイズなので、同じ RMS でも打撃音より大きく聞こえる。
            // 数値上も打撃（-24.2 dBFS）より 3 dB 下げて、当たった音が前に出るようにする。
            SfxId.Swing => 0.38f,
            SfxId.Impact => 0.95f,
            SfxId.WallSlam => 1.35f,
            SfxId.Guard => 0.80f,

            // のけぞり音は波形の RMS が小さく、0.45 では -32.8 dBFS になり
            // 「当たった音（-24.2）より 8dB 小さい」＝効いた感じが出なかった。
            // 測定に合わせて 1.00 にすると -25.8 dBFS（壊し音 -21.5 の次）になる。
            SfxId.Stagger => 1.00f,
            SfxId.Break => 1.00f,
            SfxId.Tell => 0.40f,
            SfxId.TellHeavy => 0.52f,
            SfxId.Pipe => 0.95f,
            SfxId.Step => 0.30f,
            SfxId.BodyDrop => 0.85f,

            // クリア音は「報せ」なので、殴り合いの音より少し小さく。耳に残りすぎない程度。
            SfxId.Clear => 0.60f,

            // 声は効果音より少し控えめに。打撃音に重なっても言葉として聞こえる程度。
            SfxId.HurtSmall => 0.70f,
            SfxId.HurtLarge => 0.75f,
            SfxId.HurtBelly => 0.80f,
            _ => 1f
        };

        /// <summary>
        /// 同じ音が続いたときに「同じサンプルの連打」に聞こえないよう、
        /// 音ごとに少しだけ高さを揺らす幅（±の比率）。0 なら揺らさない。
        /// </summary>
        static float PitchJitter(SfxId id) => id switch
        {
            SfxId.Step => 0.09f,
            SfxId.Swing => 0.07f,
            SfxId.Impact => 0.05f,
            SfxId.Guard => 0.05f,
            SfxId.Stagger => 0.04f,
            SfxId.HurtSmall => 0.05f,
            SfxId.HurtLarge => 0.05f,
            SfxId.HurtBelly => 0.05f,
            _ => 0f
        };

        /// <summary>鳴らした音をログに出す（-sfxlog）。二重再生や音量の確認用。</summary>
        public static bool LogPlays;

        readonly Dictionary<SfxId, AudioClip> _clips = new Dictionary<SfxId, AudioClip>();

        AudioSource[] _pool;
        int _next;
        const int Voices = 6;

        AudioSource[] _spatial;
        int _nextSpatial;
        const int SpatialVoices = 4;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 二重生成されたら自分を捨てる（音が二重に鳴るのを防ぐ）
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

            // 声をいくつか持つ。AudioSource.pitch は音源ごとの値なので、
            // 1 本だと「今鳴っている音の高さ」まで変わってしまう。
            _pool = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;   // 2D。位置に依存させない
                voice.volume = 1f;         // 音量は PlayOneShot の volumeScale で決める
                _pool[i] = voice;
            }

            BuildClips();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 効果音を鳴らす。<paramref name="volume"/> は 0..1 の倍率で、
        /// 最終的な音量は「音ごとの基準音量 × volume × MasterVolume」になる。
        /// </summary>
        public void Play(SfxId id, float volume = 1f)
        {
            Play(id, volume, 1f);
        }

        /// <summary>高さを指定して鳴らす（pitch は 1 が原音）。</summary>
        public void Play(SfxId id, float volume, float pitch)
        {
            if (_pool == null || _pool.Length == 0)
                return;

            if (!_clips.TryGetValue(id, out var clip) || clip == null)
                return;

            float jitter = PitchJitter(id);
            if (jitter > 0f)
                pitch *= 1f + Random.Range(-jitter, jitter);

            float gain = Mathf.Clamp01(volume) * Gain(id) * Mathf.Clamp01(MasterVolume);

            if (LogPlays)
                Debug.Log($"[Sfx] play {id} vol={gain:F2} pitch={pitch:F2}");

            var voice = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            voice.pitch = Mathf.Clamp(pitch, 0.5f, 1.6f);
            voice.PlayOneShot(clip, gain);

            if (id == SfxId.HurtSmall || id == SfxId.HurtLarge || id == SfxId.HurtBelly)
                HurtVoicePlayed?.Invoke(id, clip, voice.pitch);
        }

        /// <summary>
        /// 被弾の声を鳴らした瞬間（声の種類・素材・高さ）。表情が口の形と開きを声に合わせるのに使う。
        /// 声は timeScale に関係なく進むので、受け手は unscaled の時刻で追うこと。
        /// </summary>
        public static event System.Action<SfxId, AudioClip, float> HurtVoicePlayed;

        /// <summary>
        /// 指定した位置から鳴らす（足元で倒れる音など、場所が分かった方が良い音）。
        /// 位置を持たせた音源を使い回すので、鳴らすたびにオブジェクトを作らない。
        /// </summary>
        public void PlayAt(Vector3 position, SfxId id, float volume = 1f)
        {
            if (!_clips.TryGetValue(id, out var clip) || clip == null)
                return;

            if (_spatial == null)
            {
                _spatial = new AudioSource[SpatialVoices];
                for (int i = 0; i < SpatialVoices; i++)
                {
                    var go = new GameObject($"Spatial Voice {i + 1}");
                    go.transform.SetParent(transform, false);

                    var voice = go.AddComponent<AudioSource>();
                    voice.playOnAwake = false;
                    voice.spatialBlend = 1f;          // 3D
                    voice.dopplerLevel = 0f;          // 音程が動かないようにする
                    voice.rolloffMode = AudioRolloffMode.Linear;
                    voice.minDistance = 2f;
                    voice.maxDistance = 24f;
                    voice.volume = 1f;
                    _spatial[i] = voice;
                }
            }

            float jitter = PitchJitter(id);
            float pitch = jitter > 0f ? 1f + Random.Range(-jitter, jitter) : 1f;
            float gain = Mathf.Clamp01(volume) * Gain(id) * Mathf.Clamp01(MasterVolume);

            var source = _spatial[_nextSpatial];
            _nextSpatial = (_nextSpatial + 1) % _spatial.Length;

            source.transform.position = position;
            source.pitch = Mathf.Clamp(pitch, 0.5f, 1.6f);
            source.PlayOneShot(clip, gain);

            if (LogPlays)
                Debug.Log($"[Sfx] play {id} vol={gain:F2} at={position}");
        }

        /// <summary>
        /// 指定した音源で鳴らす。位置を持たせた音源（<see cref="AudioSource.spatialBlend"/> = 1）に
        /// 渡すと、どの方向から鳴ったかが分かるようになる。
        /// </summary>
        public void PlayOn(AudioSource source, SfxId id, float volume = 1f)
        {
            if (source == null)
                return;

            if (!_clips.TryGetValue(id, out var clip) || clip == null)
                return;

            float jitter = PitchJitter(id);
            float pitch = jitter > 0f ? 1f + Random.Range(-jitter, jitter) : 1f;
            float gain = Mathf.Clamp01(volume) * Gain(id) * Mathf.Clamp01(MasterVolume);

            if (LogPlays)
                Debug.Log($"[Sfx] play {id} vol={gain:F2} pitch={pitch:F2} 3D={source.name}");

            source.pitch = Mathf.Clamp(pitch, 0.5f, 1.6f);
            source.PlayOneShot(clip, gain);
        }

        void BuildClips()
        {
            _clips[SfxId.Swing] = MakeClip("Sfx_Swing", BuildSwing());
            _clips[SfxId.Impact] = MakeClip("Sfx_Impact", BuildImpact());
            _clips[SfxId.WallSlam] = MakeClip("Sfx_WallSlam", BuildWallSlam());
            _clips[SfxId.Guard] = MakeClip("Sfx_Guard", BuildGuard());
            _clips[SfxId.Stagger] = MakeClip("Sfx_Stagger", BuildStagger());
            _clips[SfxId.Break] = MakeClip("Sfx_Break", BuildBreak());
            _clips[SfxId.Tell] = MakeClip("Sfx_Tell", BuildTell(0.20f, 320f, 2300f, 70f, 0.55f, 6006));
            _clips[SfxId.TellHeavy] = MakeClip("Sfx_TellHeavy", BuildTell(0.30f, 220f, 1500f, 58f, 1.0f, 6007));
            _clips[SfxId.Pipe] = MakeClip("Sfx_Pipe", BuildPipe());
            _clips[SfxId.Step] = MakeClip("Sfx_Step", BuildStep());
            _clips[SfxId.BodyDrop] = MakeClip("Sfx_BodyDrop", BuildBodyDrop());
            _clips[SfxId.Clear] = MakeClip("Sfx_Clear", BuildClear());

            // 被弾の声は録音素材（Assets/RyonaGame/Resources/Voice）。無ければ鳴らさない。
            // 差し替え前の hurt_u / hurt_ku は VoiceBackup/ に置いてある（戻すときは README 参照）。
            LoadVoice(SfxId.HurtSmall, "Voice/hurt_small");
            LoadVoice(SfxId.HurtLarge, "Voice/hurt_large");
            LoadVoice(SfxId.HurtBelly, "Voice/hurt_belly");
        }

        void LoadVoice(SfxId id, string path)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[Sfx] 声の素材が見つからない: {path}");
                return;
            }

            // 表情が声の音量（口の開き）を波形から読むので、先に読み込んでおく
            // （インポート設定が Preload Audio Data オフのため）。
            clip.LoadAudioData();

            _clips[id] = clip;
            // どの素材がどの声に付いたかを 1 度だけ出す（差し替えの確認用）
            Debug.Log($"[Sfx] voice {id} <- {path} ({clip.length:F2}s)");
        }

        /// <summary>
        /// 検証用。生成した波形の長さ・ピーク・明るさ（ゼロ交差率）・実効値（RMS）を返す。
        /// 無音や、狙った音色になっているかを数値で確かめるために使う。
        /// </summary>
        public bool TryMeasure(SfxId id, out float seconds, out float peak, out float brightness, out float rms)
        {
            seconds = 0f;
            peak = 0f;
            brightness = 0f;
            rms = 0f;

            // EditMode（Awake が呼ばれない）でも測れるよう、必要ならここで作る
            if (_clips.Count == 0)
                BuildClips();

            if (!_clips.TryGetValue(id, out var clip) || clip == null)
                return false;

            var data = new float[Mathf.Max(1, clip.samples)];
            if (!clip.GetData(data, 0))
                return false;

            int crossings = 0;
            double sumSq = 0.0;

            for (int i = 0; i < data.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                sumSq += (double)data[i] * data[i];

                if (i > 0 && (data[i - 1] <= 0f) != (data[i] <= 0f))
                    crossings++;
            }

            seconds = clip.length;
            brightness = data.Length > 1 ? crossings / (float)(data.Length - 1) : 0f;
            rms = Mathf.Sqrt((float)(sumSq / data.Length));
            return true;
        }

        /// <summary>
        /// 検証用。2 つの音を同時に鳴らしたときの合成ピークと合成 RMS を返す。
        /// 別々の AudioSource から鳴るので出力では足し算になる。1.0 を超えると音割れするため、
        /// 「被弾」「ガード」「決着」のような重ね合わせを数値で確かめるのに使う。
        /// </summary>
        public bool TryMeasureMix(SfxId a, float volA, SfxId b, float volB,
            out float peak, out float rms)
        {
            peak = 0f;
            rms = 0f;

            if (_clips.Count == 0)
                BuildClips();

            if (!TrySamples(a, out float[] da) || !TrySamples(b, out float[] db))
                return false;

            float ga = Gain(a) * Mathf.Clamp01(volA) * Mathf.Clamp01(MasterVolume);
            float gb = Gain(b) * Mathf.Clamp01(volB) * Mathf.Clamp01(MasterVolume);

            int n = Mathf.Max(da.Length, db.Length);
            double sumSq = 0.0;

            for (int i = 0; i < n; i++)
            {
                float v = 0f;
                if (i < da.Length)
                    v += da[i] * ga;
                if (i < db.Length)
                    v += db[i] * gb;

                peak = Mathf.Max(peak, Mathf.Abs(v));
                sumSq += (double)v * v;
            }

            rms = Mathf.Sqrt((float)(sumSq / n));
            return true;
        }

        bool TrySamples(SfxId id, out float[] data)
        {
            data = null;
            if (!_clips.TryGetValue(id, out var clip) || clip == null)
                return false;

            data = new float[Mathf.Max(1, clip.samples)];
            return clip.GetData(data, 0);
        }

        static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // --- 波形の合成 -------------------------------------------------

        /// <summary>風切り音。ノイズに 1 次ローパスをかけて「ヒュッ」を作る。</summary>
        static float[] BuildSwing()
        {
            var data = NewBuffer(0.14f);
            var rng = new System.Random(1001);
            float low = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length;

                // カットオフを高い方から低い方へ動かすと、空気を切る感じになる
                float cutoff = Mathf.Lerp(4200f, 700f, t);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SampleRate);

                float noise = Noise(rng);
                low += a * (noise - low);

                // ローパス成分を差し引いた残り＝バンドパスっぽい成分を主役にする
                float band = noise - low * 0.75f;

                float env = Mathf.Sin(t * Mathf.PI);   // 中央で最大の山
                data[i] = band * env;
            }

            Finish(data, 0.004f);
            return data;
        }

        /// <summary>打撃音。80Hz 付近のサイン + 短いノイズバースト。</summary>
        static float[] BuildImpact()
        {
            var data = NewBuffer(0.16f);
            var rng = new System.Random(2002);
            float phase = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                // 当たった瞬間だけ高く、すぐ沈める（80Hz を通る）
                float freq = Mathf.Lerp(120f, 72f, Mathf.Clamp01(seconds / 0.05f));
                phase += 2f * Mathf.PI * freq / SampleRate;

                float body = Mathf.Sin(phase);
                float crack = Noise(rng) * Mathf.Exp(-seconds * 90f);   // 短いバースト
                float env = Mathf.Exp(-seconds * 30f);                  // 急峻な減衰

                data[i] = body * 0.9f + crack * 0.7f;
                data[i] *= env;
            }

            Finish(data, 0.003f);
            return data;
        }

        static float[] BuildWallSlam()
        {
            // 壁へ叩きつける音。以前は 87Hz の胴鳴り＋速い減衰で「軽い」と感じたので、
            // 低い胴鳴り（62Hz）とサブ（41Hz）を足し、余韻を長くして重さを出す。
            var data = NewBuffer(0.44f);
            var rng = new System.Random(2014);
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / SampleRate;
                float thud = Mathf.Sin(2f * Mathf.PI * (62f * t - 70f * t * t)) * Mathf.Exp(-16f * t);
                float sub = Mathf.Sin(2f * Mathf.PI * 41f * t) * Mathf.Exp(-9f * t) * 0.55f;
                float panel = (0.26f * Mathf.Sin(2f * Mathf.PI * 118f * t) +
                               0.14f * Mathf.Sin(2f * Mathf.PI * 197f * t)) * Mathf.Exp(-13f * t);
                float crack = 0.75f * Noise(rng) * Mathf.Exp(-90f * t);
                data[i] = thud + sub + panel + crack;
            }
            Finish(data, 0.002f);
            return data;
        }

        /// <summary>素手で受け止める短い、金属成分のないガード音。</summary>
        static float[] BuildGuard()
        {
            var data = NewBuffer(0.11f);
            var rng = new System.Random(3003);
            float low = 0f;
            float filter = 1f - Mathf.Exp(-2f * Mathf.PI * 1100f / SampleRate);
            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;
                low += filter * (Noise(rng) - low);
                float body = Mathf.Sin(2f * Mathf.PI * 150f * seconds);
                data[i] = (low * 0.65f + body * 0.35f) * Mathf.Exp(-seconds * 38f);
            }
            Finish(data, 0.002f);
            return data;
        }

        /// <summary>怯み音。低めのノイズバーストで、少し長めに残す。</summary>
        static float[] BuildStagger()
        {
            var data = NewBuffer(0.22f);
            var rng = new System.Random(4004);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 650f / SampleRate);
            float low = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                float noise = Noise(rng);
                low += a * (noise - low);   // 650Hz の 1 次ローパス＝こもった「ドッ」

                float attack = Mathf.Clamp01(seconds / 0.012f);
                float env = attack * Mathf.Exp(-seconds * 13f);

                data[i] = low * env;
            }

            Finish(data, 0.004f);
            return data;
        }

        /// <summary>崩し音。60Hz のサイン + ノイズで、一番長く低い。</summary>
        static float[] BuildBreak()
        {
            var data = NewBuffer(0.25f);
            var rng = new System.Random(5005);
            float phase = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                float freq = Mathf.Lerp(90f, 48f, Mathf.Clamp01(seconds / 0.12f));
                phase += 2f * Mathf.PI * freq / SampleRate;

                float body = Mathf.Sin(phase);
                float noise = Noise(rng) * 0.5f;

                float attack = Mathf.Clamp01(seconds / 0.004f);
                float env = attack * Mathf.Exp(-seconds * 11f);

                data[i] = (body + noise) * env;
            }

            Finish(data, 0.003f);
            return data;
        }

        /// <summary>
        /// 鉄パイプで殴られた音。
        ///
        /// 素手の <see cref="BuildImpact"/>（80Hz の胴鳴り＋ノイズ）と区別するため、
        /// 金属の余韻を足す。ただし整数倍音にすると鐘や楽器に聞こえるので、
        /// あえて非整数比の倍音を混ぜ、高い倍音ほど速く減衰させている。
        /// 低音（胴に来る音）は残す。金属の余韻だけだと「金属板が鳴った」に聞こえ、
        /// 人を殴った重さが出ない。
        /// </summary>
        static float[] BuildPipe()
        {
            var data = NewBuffer(0.42f);
            var rng = new System.Random(7007);

            // パイプの断面で決まる非整数倍音（周波数, 音量, 減衰の速さ）
            // 高い倍音を厚めにして、金属が「キン」と鳴る成分を残す
            float[] freq = { 486f, 1147f, 1723f, 2419f, 3187f };
            float[] gain = { 0.55f, 0.55f, 0.45f, 0.33f, 0.22f };
            float[] decay = { 8f, 9f, 11f, 13f, 16f };

            float lowPhase = 0f;
            float prevNoise = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                float metal = 0f;
                for (int p = 0; p < freq.Length; p++)
                {
                    metal += Mathf.Sin(2f * Mathf.PI * freq[p] * seconds)
                             * gain[p] * Mathf.Exp(-seconds * decay[p]);
                }

                // 当たった瞬間の「カキン」。直前との差を取ると高域だけ残る（簡易ハイパス）。
                // これが無いと立ち上がりが甘く、金属に聞こえない
                float noise = Noise(rng);
                float tick = (noise - prevNoise) * Mathf.Exp(-seconds * 150f) * 0.5f;
                prevNoise = noise;

                // 胴に来る低音。叩かれた重さはここで出る（鳴りすぎるとただの鈍い音になる）
                float freqLow = Mathf.Lerp(105f, 78f, Mathf.Clamp01(seconds / 0.06f));
                lowPhase += 2f * Mathf.PI * freqLow / SampleRate;
                float body = Mathf.Sin(lowPhase) * Mathf.Exp(-seconds * 30f) * 0.55f;

                data[i] = metal * 0.9f + tick + body;
            }

            Finish(data, 0.004f);
            return data;
        }

        /// <summary>
        /// 敵の予備動作の合図。踏み込みの低い音＋衣擦れを上へ掃引する。
        /// 「これから来る」と分かればいいので、打撃音（Swing）とは明るさを変えて区別できるようにする。
        /// </summary>
        /// <summary>
        /// クリア音。ドミソ（C5-E5-G5）を少しずつ重ねて上がる 3 音。
        /// 勝利のファンファーレほど大げさにせず、「終わった」と分かる報せに留める。
        /// </summary>
        static float[] BuildClear()
        {
            var data = NewBuffer(0.95f);
            float[] notes = { 523.25f, 659.25f, 783.99f };
            const float noteSeconds = 0.95f / 3f;
            float phase = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float time = (float)i / SampleRate;
                int index = Mathf.Clamp((int)(time / noteSeconds), 0, notes.Length - 1);
                float local = time - index * noteSeconds;

                phase += 2f * Mathf.PI * notes[index] / SampleRate;

                // 立ち上がりを 3ms で切り、減衰は指数。倍音を少し混ぜて硬くしない
                float attack = Mathf.Clamp01(local * 300f);
                float env = attack * Mathf.Exp(-local * 3.2f);
                float tone = Mathf.Sin(phase) * 0.62f + Mathf.Sin(phase * 2f) * 0.20f;

                data[i] = tone * env * (index == notes.Length - 1 ? 1f : 0.72f);
            }

            Finish(data, 0.02f);
            return data;
        }

        static float[] BuildTell(float seconds, float fromCutoff, float toCutoff,
            float thumpHz, float thumpGain, int seed)
        {
            var data = NewBuffer(seconds);
            var rng = new System.Random(seed);
            float low = 0f;
            float band = 0f;
            float phase = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / data.Length;
                float time = (float)i / SampleRate;

                // 帯域を上へ動かす（息を吸う感じ）。打撃音より暗くして、耳で区別できるようにする
                float cutoff = Mathf.Lerp(fromCutoff, toCutoff, t * t);
                float aHigh = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SampleRate);
                float aLow = 1f - Mathf.Exp(-2f * Mathf.PI * (cutoff * 0.35f) / SampleRate);

                float noise = Noise(rng);
                low += aHigh * (noise - low);
                band += aLow * (low - band);

                float hiss = (low - band) * Mathf.Sin(Mathf.Pow(t, 1.7f) * Mathf.PI);

                // 踏み込み。最初だけ低く鳴らす
                phase += 2f * Mathf.PI * thumpHz / SampleRate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-time * 20f) * thumpGain;

                data[i] = hiss + thump;
            }

            Finish(data, 0.006f);
            return data;
        }

        // --- 合成の補助 -------------------------------------------------

        /// <summary>
        /// 足音。靴底が床に当たる「コツ」。
        /// 打撃音（80Hz の胴鳴り）と混ざらないよう、胴鳴りは入れずに
        /// 短いノイズ＋軽い低音だけにして、長さも 60ms に切る。
        /// </summary>
        static float[] BuildStep()
        {
            var data = NewBuffer(0.07f);
            var rng = new System.Random(8008);
            float low = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                float noise = Noise(rng);
                low += 0.35f * (noise - low);   // 靴底の「ザッ」を残すため軽くだけ鈍らせる

                float attack = Mathf.Clamp01(seconds / 0.002f);
                float env = attack * Mathf.Exp(-seconds * 60f);

                float thump = Mathf.Sin(2f * Mathf.PI * 105f * seconds) * 0.35f;

                data[i] = (noise * 0.55f + low * 0.6f + thump) * env;
            }

            Finish(data, 0.003f);
            return data;
        }

        /// <summary>
        /// 倒れる音。体が床に着く「ドサッ」。
        /// 打撃音より低く長くして、当たった音と区別できるようにする。
        /// </summary>
        static float[] BuildBodyDrop()
        {
            var data = NewBuffer(0.45f);
            var rng = new System.Random(9009);
            float low = 0f;
            float phase = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                float seconds = (float)i / SampleRate;

                float noise = Noise(rng);
                low += 0.045f * (noise - low);   // 衣擦れと床の広がり（こもった成分）

                float freq = Mathf.Lerp(88f, 42f, Mathf.Clamp01(seconds / 0.10f));
                phase += 2f * Mathf.PI * freq / SampleRate;

                float attack = Mathf.Clamp01(seconds / 0.006f);
                float env = attack * Mathf.Exp(-seconds * 8f);

                data[i] = (Mathf.Sin(phase) * 0.8f + low * 1.6f) * env;
            }

            Finish(data, 0.004f);
            return data;
        }

        static float[] NewBuffer(float seconds)
        {
            int length = Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate));
            return new float[length];
        }

        static float Noise(System.Random rng)
        {
            return (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        /// <summary>前後のクリックを消し、ピークを揃える。</summary>
        static void Finish(float[] data, float fadeSeconds)
        {
            FadeEdges(data, fadeSeconds);
            Normalize(data, ClipPeak);
        }

        static void FadeEdges(float[] data, float fadeSeconds)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(fadeSeconds * SampleRate), 1, data.Length / 2);
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                data[i] *= k;
                data[data.Length - 1 - i] *= k;
            }
        }

        static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            for (int i = 0; i < data.Length; i++)
                max = Mathf.Max(max, Mathf.Abs(data[i]));

            if (max < 1e-5f)
                return;

            float scale = peak / max;
            if (scale > 8f)
                scale = 8f;   // ほぼ無音のときに過大なゲインを掛けない

            for (int i = 0; i < data.Length; i++)
                data[i] *= scale;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadArgs()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-sfxlog", System.StringComparison.OrdinalIgnoreCase))
                    LogPlays = true;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null)
                return;

            var go = new GameObject("[ProceduralSfx]");
            DontDestroyOnLoad(go);
            go.AddComponent<ProceduralSfx>();
        }
    }
}
