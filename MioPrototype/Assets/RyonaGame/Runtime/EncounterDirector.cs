using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ベルト（横スクロール）の敵の出し方。ファイナルファイト式。
    /// <para>
    /// 敵は画面の右端の少し外から入ってくる。倒しているあいだは画面がロックされ、
    /// 全滅させるとロックが外れて右へ進める。1 画面ぶん進むと次の一団が入ってくる。
    /// </para>
    /// <para>
    /// 敵は作り置き（<see cref="EnemyBrain.Revive"/>）で、位置だけ画面外へ運んでいる。
    /// 倒された敵をそのまま次の一団に使い回すので、個体数は増えない。
    /// </para>
    /// </summary>
    [AddComponentMenu("Ryona/Encounter Director")]
    public sealed class EncounterDirector : ConfigurableMonoBehaviour
    {
        [Header("Stage (belt)")]
        [Tooltip("ステージの基準。ここを中心に画面と敵の出現位置を決める")]
        public Transform Player;

        [Tooltip("画面の中心を知るためのカメラ")]
        public ThirdPersonCamera Camera;

        /// <summary>最初のアリーナ中心。壁の並びと同じ X。</summary>
        public const float StartX = -6f;

        [Tooltip("アリーナ中心の間隔（m）。-screenspan=N で上書き")]
        [System.NonSerialized] public float ArenaPitch = 10f;

        [System.NonSerialized] public int WavesPerArena = 2;
        [System.NonSerialized] public int ArenaCount = 5;

        [Tooltip("この X に近づいたら次のアリーナをロックする（中心との差 m）")]
        [System.NonSerialized] public float EntryOffset = 2f;

        [Tooltip("同じアリーナで次の波が出るまでの秒")]
        [System.NonSerialized] public float WaveGap = 1.2f;

        [Tooltip("プレイヤーの目の前に出ないための最小距離（m）")]
        [System.NonSerialized] public float MinSpawnGap = 2.2f;

        /// <summary>
        /// 入場時に横へずらす間隔（m）。同じ位置に重ならないためだけでなく、
        /// 到着時刻をずらして「1 体ずつ来る」ようにするための値。
        /// 歩行速度 約 2m/s なので 1.6m で 0.8 秒ずつ遅れて来る。
        /// </summary>
        [System.NonSerialized] public float EntrySpacing = 1.6f;

        [Header("Timing")]
        [System.NonSerialized] public float BodyDropDelay = 0.7f;
        [System.NonSerialized] public float BodyDropVolume = 0.95f;

        readonly List<EnemyBrain> _enemies = new List<EnemyBrain>();
        readonly List<Vector3> _pendingDrops = new List<Vector3>();
        float _pendingTimer;
        float _collectRetry;
        float _nextWaveAt;
        int _currentArena;
        BeltScreenLock _lock;

        /// <summary>現在の設定から作るアリーナ配置。</summary>
        public ArenaPlan Plan => new ArenaPlan(StartX, ArenaPitch, WavesPerArena, ArenaCount);

        /// <summary>これまでに倒した数。</summary>
        public int Kills { get; private set; }

        /// <summary>生きている敵の数。</summary>
        public int Alive { get; private set; }

        /// <summary>いまのアリーナが片付いているか。</summary>
        public bool Cleared => Alive <= 0;

        /// <summary>これまでに出した波の数。</summary>
        public int Wave { get; private set; }

        /// <summary>次にロックするアリーナの入場 X（計測用）。</summary>
        public float NextTriggerX => Plan.CenterOf(Plan.ArenaOf(Wave + 1)) - EntryOffset;

        /// <summary>同一アリーナの次の波までの残り秒（HUD 用）。</summary>
        public float NextWaveRemaining => Mathf.Max(0f, _nextWaveAt - Time.time);

        /// <summary>次の波が別アリーナなら true（HUD の案内出し分け用）。</summary>
        public bool ArenaMovingNext => Plan.ArenaOf(Wave + 1) != _currentArena;

        /// <summary>直近の波が入場した X（検証用）。</summary>
        public float LastEntryX { get; private set; }

        /// <summary>決められた数の波を片付けたか（0 なら無限）。</summary>
        public bool StageCleared { get; private set; }

        [System.NonSerialized] public int MaxWaves = 10;
        public float ClearTimeSeconds { get; private set; }
        public float BestClearSeconds { get; private set; }

        const string BestTimeKey = "Mio.BestClearSeconds.Normal10.v3";
        float _runSeconds;
        internal float RunSeconds => _runSeconds;
        bool _recordEligible = true;

        /// <summary>調整値は <see cref="RyonaConfig.Encounter"/> が真実源。シーンの保存値は使わない。</summary>
        void ApplySettings(RyonaConfig.EncounterSettings s)
        {
            ArenaPitch = s.ArenaPitch;
            WavesPerArena = s.WavesPerArena;
            ArenaCount = s.ArenaCount;
            EntryOffset = s.EntryOffset;
            WaveGap = s.WaveGap;
            MinSpawnGap = s.MinSpawnGap;
            BodyDropDelay = s.BodyDropDelay;
            BodyDropVolume = s.BodyDropVolume;
        }

        protected override void Awake()
        {
            ApplySettings(RyonaConfig.Instance.Encounter);
        }

        public override void OnConfigChanged(RyonaConfig config)
        {
            ApplySettings(config.Encounter);
            GameLog.Log("[EncounterDirector] Config hot-reloaded");
        }

        void Start()
        {
            ApplyCommandLine();
            Collect();
            _lock = FindFirstObjectByType<BeltScreenLock>();

            BestClearSeconds = PlayerPrefs.GetFloat(BestTimeKey, 0f);

            // 最初のアリーナは開始位置で即ロックし、右端の外から第一波が入る。
            if (_enemies.Count > 0)
            {
                _currentArena = 0;
                _lock?.LockAt(Plan.CenterOf(0), initial: true);
                EnterWave(0);
            }
        }

        /// <summary>
        /// 比較用: <c>-screenspan=7</c> でアリーナ間隔を差し替える。
        /// </summary>
        void ApplyCommandLine()
        {
            // 検証用のゲーム条件では通常プレイの記録を更新しない。
            if (LaunchOptions.HasPrefix("-maxwaves") ||
                LaunchOptions.HasPrefix("-screenspan") ||
                LaunchOptions.HasPrefix("-hold") ||
                LaunchOptions.HasPrefix("-encountertest") ||
                LaunchOptions.HasPrefix("-grappletest"))
                _recordEligible = false;

            if (LaunchOptions.TryFloat("-screenspan", out float value) && value > 1f)
            {
                ArenaPitch = value;
                GameLog.Log($"[Encounter] ArenaPitch={ArenaPitch:F1}（-screenspan から）");
            }

            // 0 を渡すと無限（計測用）。最後のアリーナに留まる。
            if (LaunchOptions.TryInt("-maxwaves", out int waves) && waves >= 0)
            {
                MaxWaves = waves;
                GameLog.Log($"[Encounter] MaxWaves={MaxWaves}（0 は無限）");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<EncounterDirector>() != null)
                return;

            // 敵がいないシーン（検証用）では何もしない
            if (FindFirstObjectByType<EnemyBrain>() == null)
                return;

            var go = new GameObject("Encounter Director");
            go.AddComponent<EncounterDirector>();
        }

        /// <summary>シーンに置かれた敵を 1 度だけ集める。</summary>
        void Collect()
        {
            if (_enemies.Count > 0)
                return;

            _collectRetry -= Time.deltaTime;
            if (_collectRetry > 0f)
                return;

            _collectRetry = 1f;

            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain == null)
                    continue;

                _enemies.Add(brain);
                brain.Died += OnDied;
            }

            Alive = CountAlive();

            AssignRoles();
            GameLog.Log($"[Encounter] 敵 {_enemies.Count} 体を登録 arena={ArenaCount} × {WavesPerArena} 波 pitch={ArenaPitch:F1}m " +
                          $"（-screenspan=N で上書き可）");
        }

        /// <summary>
        /// 背丈（同点は名前）で掴み役・道具役・打撃役を決める。CanPin は掴み役から導く。
        /// </summary>
        void AssignRoles()
        {
            var valid = new List<EnemyBrain>(_enemies.Count);
            var candidates = new List<RoleCandidate>(_enemies.Count);
            foreach (var brain in _enemies)
            {
                if (brain == null)
                    continue;
                valid.Add(brain);
                candidates.Add(new RoleCandidate(brain.name, HeightOf(brain), brain.CanPin));
            }

            for (int i = 0; i < valid.Count; i++)
            {
                var role = EnemyRoleRules.RoleOf(candidates, i);
                valid[i].Role = role;
                valid[i].CanPin = role == EnemyRole.Grappler;
                GameLog.Log($"[Encounter] role {valid[i].name}={role} height={HeightOf(valid[i]):F2}");
            }

        }

        /// <summary>敵の背丈（m）。<see cref="EnemyFigure"/> が無ければ 0。</summary>
        static float HeightOf(EnemyBrain brain)
        {
            var figure = brain != null ? brain.GetComponent<EnemyFigure>() : null;
            return figure != null ? figure.Height : 0f;
        }

        void OnDestroy()
        {
            foreach (var brain in _enemies)
            {
                if (brain != null)
                    brain.Died -= OnDied;
            }
        }

        void OnDied(EnemyBrain brain)
        {
            Kills++;
            Alive = CountAlive();

            // 倒れた瞬間に鳴らすと軽いので、少し遅らせる
            _pendingDrops.Add(brain != null ? brain.transform.position : transform.position);
            _pendingTimer = BodyDropDelay;

            GameLog.Log($"[Encounter] 撃破 {Kills} 残り {Alive} t={Time.time:F2}");

            if (Alive <= 0)
            {
                _nextWaveAt = Time.time + WaveGap;
                if (Plan.ArenaOf(Wave + 1) != _currentArena)
                    _lock?.Release();
                GameLog.Log($"[Encounter] 全滅 wave={Wave} arena={_currentArena} t={Time.time:F2}");
            }
        }

        int CountAlive()
        {
            int alive = 0;
            foreach (var brain in _enemies)
            {
                if (brain != null && brain.IsAlive)
                    alive++;
            }

            return alive;
        }

        float CenterX()
        {
            if (Camera != null)
                return Camera.CurrentX;

            if (Player != null)
                return Player.position.x;

            return transform.position.x;
        }

        void Update()
        {
            Collect();
            if (!StageCleared)
                _runSeconds += Time.deltaTime;

            // 着地音
            if (_pendingDrops.Count > 0)
            {
                _pendingTimer -= Time.deltaTime;
                if (_pendingTimer <= 0f)
                {
                    foreach (Vector3 point in _pendingDrops)
                        PlayDrop(point);

                    _pendingDrops.Clear();
                }
            }

            // 片付いていないあいだは次の波を出さない
            if (Alive > 0)
                return;

            if (StageCleared)
                return;

            // 決められた数の波を片付けたら終わり（0 なら無限）
            if (MaxWaves > 0 && Wave >= MaxWaves)
            {
                StageCleared = true;
                ClearTimeSeconds = _runSeconds;

                if (_recordEligible && (BestClearSeconds <= 0f || ClearTimeSeconds < BestClearSeconds))
                {
                    BestClearSeconds = ClearTimeSeconds;
                    PlayerPrefs.SetFloat(BestTimeKey, ClearTimeSeconds);
                    PlayerPrefs.Save();
                }

                ProceduralSfx.Instance?.Play(SfxId.Clear, 1f);
                _lock?.Release();

                GameLog.Log($"[Encounter] STAGE CLEAR {Wave} 波 / 撃破 {Kills} " +
                            $"time={ClearTimeSeconds:F1}s best={BestClearSeconds:F1}s t={Time.time:F2}");
                return;
            }

            int nextArena = Plan.ArenaOf(Wave + 1);
            bool sameArena = nextArena == _currentArena && (_lock == null || _lock.IsLocked);
            if (sameArena)
            {
                // 同じアリーナに波が残っていれば、歩かせず WaveGap で次を出す。
                if (Time.time < _nextWaveAt)
                    return;
            }
            else
            {
                // 最後の波を片付けたらロックは外れている。次のアリーナの入場地点まで歩かせる。
                float entryX = Plan.CenterOf(nextArena) - EntryOffset;
                if (PlayerX() < entryX)
                    return;

                _lock?.LockAt(Plan.CenterOf(nextArena));
                _currentArena = nextArena;
            }

            EnterWave(nextArena);
        }

        float PlayerX()
        {
            if (Player != null)
                return Player.position.x;

            return CenterX();
        }

        void PlayDrop(Vector3 position)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx == null)
                return;

            sfx.PlayAt(position, SfxId.BodyDrop, BodyDropVolume);
        }

        /// <summary>アリーナ右端の外に一団を並べて、歩いて入ってこさせる。</summary>
        void EnterWave(int arena)
        {
            float playerX = PlayerX();
            float z = Player != null ? Player.position.z : 0f;
            float half = Camera != null ? Camera.ScreenHalfWidth(z) : 5f;
            float edge = Mathf.Max(Plan.EntryX(arena, half, 1f),
                                   playerX + Mathf.Max(0f, MinSpawnGap));

            for (int i = 0; i < _enemies.Count; i++)
            {
                var brain = _enemies[i];
                if (brain == null)
                    continue;

                Vector3 pos = new Vector3(edge + i * EntrySpacing, 0f, z);
                brain.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -90f, 0f));
                brain.Revive();
            }

            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
                sfx.PlayAt(new Vector3(edge, 0f, 0f), SfxId.TellHeavy, 0.7f);

            Alive = CountAlive();
            Wave++;

            LastEntryX = edge;
            GameLog.Log($"[Encounter] wave {Wave} arena {arena} center={Plan.CenterOf(arena):F1} " +
                        $"敵 {Alive} 体が右端から入場 (x={edge:F1}) t={Time.time:F2}");

            StartCoroutine(VerifyStanding());
        }

        System.Collections.IEnumerator VerifyStanding()
        {
            // 立ち上がりは最大 RiseSeconds × 1.4 秒かかるので、余裕をもって待つ
            yield return new WaitForSecondsRealtime(3.0f);

            foreach (var brain in _enemies)
            {
                if (brain == null)
                    continue;

                var figure = brain.GetComponent<EnemyFigure>();
                float fall = figure != null ? figure.FallAngle : -1f;
                GameLog.Log($"[Encounter] 起きあがり確認 {brain.name} phase={brain.Phase} fall={fall:F1} hp={brain.State.Hp:F0}");
            }
        }
    }
}
