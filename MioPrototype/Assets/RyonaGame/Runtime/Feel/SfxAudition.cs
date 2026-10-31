using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 効果音を 1 つずつ試聴する開発用モード（<c>-sfxaudition</c>）。
    ///
    /// 戦闘中は複数の音が重なるので、音量バランスの良し悪しが判断しづらい。
    /// 時間を止めて 1 音ずつ順番に鳴らし、画面に名前と実測値を出す。
    /// 番号は <see cref="SfxId"/> と 1 対 1（1 が Swing、11 が Clear）にしてある。
    /// 報告するときに「3 が大きい」と言えば、そのまま SfxId.Guard のことになる。
    /// </summary>
    [AddComponentMenu("Ryona/Sfx Audition")]
    public sealed class SfxAudition : MonoBehaviour
    {
        /// <summary>1 音あたりの間隔（秒）。耳で余韻を確かめられる程度に空ける。</summary>
        public float Interval = 1.6f;

        struct Entry
        {
            public SfxId Id;
            public string Name;
            public string Level;   // 実測ピーク（dBFS）。0 に近いほど大きい
        }

        // 並びは SfxId の値順。番号 = Id + 1。
        static readonly Entry[] Entries =
        {
            new Entry { Id = SfxId.Swing,     Name = "Swing 振り抜き",          Level = "-27.2" },
            new Entry { Id = SfxId.Impact,    Name = "Impact 素手の打撃",        Level = "-24.2" },
            new Entry { Id = SfxId.Guard,     Name = "Guard ガード",             Level = "-26.4" },
            new Entry { Id = SfxId.Stagger,   Name = "Stagger のけぞり",         Level = "-25.8" },
            new Entry { Id = SfxId.Break,     Name = "Break 崩し",               Level = "-21.5" },
            new Entry { Id = SfxId.Tell,      Name = "Tell 予備動作・小",         Level = "-26.1" },
            new Entry { Id = SfxId.TellHeavy, Name = "TellHeavy 予備動作・大",    Level = "-26.2" },
            new Entry { Id = SfxId.Pipe,      Name = "Pipe 鉄パイプ被弾",         Level = "-25.6" },
            new Entry { Id = SfxId.Step,      Name = "Step 足音",                Level = "-35.0" },
            new Entry { Id = SfxId.BodyDrop,  Name = "BodyDrop 倒れる",           Level = "-24.0" },
            new Entry { Id = SfxId.Clear,     Name = "Clear クリア",              Level = "-22.6" },
            new Entry { Id = SfxId.HurtU,     Name = "HurtU 被弾の声（う）",     Level = "未測定" },
            new Entry { Id = SfxId.HurtKu,     Name = "HurtKu 被弾の声（く）",     Level = "未測定" }
        };

        struct Combo
        {
            public string Name;
            public SfxId A;
            public float VolA;
            public SfxId B;
            public float VolB;
        }

        // 実際に重ねて鳴る組み合わせ。単発で良くても、重なると埋もれる／割れることがある。
        static readonly Combo[] Combos =
        {
            new Combo { Name = "被弾（Pipe + Stagger）",       A = SfxId.Pipe,  VolA = 1.00f, B = SfxId.Stagger, VolB = 0.70f },
            new Combo { Name = "ガード（Guard + Stagger）",    A = SfxId.Guard, VolA = 0.90f, B = SfxId.Stagger, VolB = 0.50f },
            new Combo { Name = "決着（Break + BodyDrop）",     A = SfxId.Break, VolA = 1.00f, B = SfxId.BodyDrop, VolB = 1.00f }
        };

        int _index;
        int _combo = -1;
        bool _started;
        string _lastAction = "";
        float _timer;
        bool _auto = true;
        GUIStyle _row;
        GUIStyle _head;
        GUIStyle _hint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            bool wanted = false;
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-sfxaudition", System.StringComparison.OrdinalIgnoreCase))
                    wanted = true;
            }

            if (!wanted)
                return;

            var go = new GameObject("Sfx Audition");
            go.AddComponent<SfxAudition>();
        }

        void Start()
        {
            // 時間を止める。戦闘の音が混ざらず、画面も静止して聞き比べやすい。
            // （AudioSource は timeScale の影響を受けないので音は鳴る）
            Time.timeScale = 0f;

            Debug.Log($"[Sfx] audition 開始 音源数={Entries.Length} 間隔={Interval:F1}s " +
                      "（timeScale=0。番号を指定して報告してください）");
        }

        void Update()
        {
            // 番号キーで好きな音だけを鳴らせる。
            // 1〜9 が 1〜9 番、0 が 10 番、- が 11 番（11 個になっても全部押せる）。
            for (int i = 0; i < 10; i++)
            {
                UnityEngine.InputSystem.Key key = i == 9
                    ? UnityEngine.InputSystem.Key.Digit0
                    : (UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i);

                if (KeyPressed(key))
                {
                    PlayEntry(i);
                    return;
                }
            }

            if (KeyPressed(UnityEngine.InputSystem.Key.Minus))
            {
                PlayEntry(Entries.Length - 1);
                return;
            }

            // ←→ で 1 つずつ送る（自動送りを止めたまま聞き比べるのに使う）
            if (KeyPressed(UnityEngine.InputSystem.Key.LeftArrow))
            {
                PlayEntry((_index - 1 + Entries.Length) % Entries.Length);
                return;
            }

            if (KeyPressed(UnityEngine.InputSystem.Key.RightArrow))
            {
                PlayEntry((_index + 1) % Entries.Length);
                return;
            }

            // Enter で重ね合わせを順に試す
            if (KeyPressed(UnityEngine.InputSystem.Key.Enter))
            {
                PlayNextCombo();
                return;
            }

            // T で左右に振って鳴らす（定位・方向の確認）
            if (KeyPressed(UnityEngine.InputSystem.Key.T))
            {
                StartCoroutine(PanTest());
                return;
            }

            // Space で自動送りの停止／再開
            if (KeyPressed(UnityEngine.InputSystem.Key.Space))
            {
                _auto = !_auto;
                Debug.Log($"[Sfx] 自動送り {(_auto ? "再開" : "停止")}");
            }

            if (!_auto)
                return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f)
                return;

            _timer = Interval;

            // 最初の 1 回は 1 番から鳴らす（いきなり 2 番から始まると 1 番を聞き逃す）
            if (_started)
                _index = (_index + 1) % Entries.Length;
            _started = true;

            var entry = Entries[_index];
            Play(entry.Id, 1f);
            _lastAction = "自動送り";

            Debug.Log($"[Sfx] audition {_index + 1}/{Entries.Length} {entry.Name}");
        }

        void PlayEntry(int index)
        {
            _index = index;
            _timer = Interval;
            var entry = Entries[index];
            Play(entry.Id, 1f);
            _lastAction = "単発";
            Debug.Log($"[Sfx] audition 手動 {index + 1}/{Entries.Length} {entry.Name} ({entry.Level} dBFS)");
        }

        void PlayNextCombo()
        {
            _combo = (_combo + 1) % Combos.Length;
            _timer = Interval;

            var c = Combos[_combo];
            Play(c.A, c.VolA);
            Play(c.B, c.VolB);
            _lastAction = $"重ね {c.Name}";

            Debug.Log($"[Sfx] audition 重ね {c.Name} " +
                      $"（{c.A} x{c.VolA:F2} + {c.B} x{c.VolB:F2}）");
        }

        /// <summary>同じ音を左・正面・右の 3 方向で鳴らす。定位の確認用。</summary>
        System.Collections.IEnumerator PanTest()
        {
            var cam = Camera.main;
            if (cam == null)
                yield break;

            var entry = Entries[_index];
            _timer = Interval;
            _lastAction = "左右に振って再生";

            Vector3 p = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            Vector3 right = cam.transform.right;

            Vector3[] spots =
            {
                p + fwd * 3f + right * -2.2f,
                p + fwd * 3f,
                p + fwd * 3f + right * 2.2f
            };
            string[] names = { "左", "正面", "右" };

            for (int i = 0; i < spots.Length; i++)
            {
                var sfx = ProceduralSfx.Instance;
                if (sfx != null)
                    sfx.PlayAt(spots[i], entry.Id, 1f);

                Debug.Log($"[Sfx] 定位 {names[i]} {entry.Name}");
                yield return new WaitForSecondsRealtime(0.55f);
            }
        }

        static bool KeyPressed(UnityEngine.InputSystem.Key key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
#else
            return false;
#endif
        }

        void OnGUI()
        {
            if (_row == null)
            {
                _row = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleLeft };
                _head = new GUIStyle(GUI.skin.label) { fontSize = 23, alignment = TextAnchor.MiddleLeft };
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            }

            const float x = 20f;
            const float y = 18f;
            const float w = 560f;
            const float lineH = 24f;

            var panel = new Rect(x - 10f, y - 8f, w, lineH * (Entries.Length + 5) + 16f);
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y, w, lineH), "効果音の試聴（番号はそのまま音の番号）", _head);

            for (int i = 0; i < Entries.Length; i++)
            {
                bool current = i == _index;
                GUI.color = current ? new Color(1f, 0.85f, 0.35f) : new Color(0.86f, 0.86f, 0.9f);

                string mark = current ? "▶" : "  ";
                string text = $"{mark} {i + 1,2}  {Entries[i].Name,-24} {Entries[i].Level} dBFS";
                GUI.Label(new Rect(x, y + lineH * (i + 1), w, lineH), text, _row);
            }

            GUI.color = new Color(0.75f, 0.78f, 0.85f);
            float hy = y + lineH * (Entries.Length + 1) + 6f;
            GUI.Label(new Rect(x, hy, w, lineH),
                $"1-9 / 0=10 / - =11：単発　←→：前後　Enter：重ね　T：左右　Space：自動{(_auto ? "停止" : "再開")}", _hint);

            GUI.color = new Color(0.62f, 0.86f, 0.68f);
            GUI.Label(new Rect(x, hy + lineH, w, lineH), string.IsNullOrEmpty(_lastAction) ? " " : _lastAction, _hint);
            GUI.color = Color.white;
        }

        static void Play(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx == null)
                return;

            sfx.Play(id, volume, 1f);
        }
    }
}
