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
    public sealed class EncounterDirector : MonoBehaviour
    {
        [Header("Stage (belt)")]
        [Tooltip("ステージの基準。ここを中心に画面と敵の出現位置を決める")]
        public Transform Player;

        [Tooltip("画面の中心を知るためのカメラ")]
        public ThirdPersonCamera Camera;

        [Tooltip("次の一団が出るまでに進む距離（1 画面ぶん）")]
        public float ScreenSpan = 8f;

        [Tooltip("画面の右端の、これだけ外から入ってくる（m）")]
        public float SpawnAhead = 5.4f;

        [Tooltip("プレイヤーの目の前に出ないための最小距離（m）")]
        public float MinSpawnGap = 2.2f;

        /// <summary>
        /// 入場時に横へずらす間隔（m）。同じ位置に重ならないためだけでなく、
        /// 到着時刻をずらして「1 体ずつ来る」ようにするための値。
        /// 歩行速度 約 2m/s なので 1.6m で 0.8 秒ずつ遅れて来る。
        /// </summary>
        [System.NonSerialized] public float EntrySpacing = 1.6f;

        [Header("Timing")]
        /// <summary>倒れてから「倒れる音」を鳴らすまでの秒数。</summary>
        public float BodyDropDelay = 0.7f;

        /// <summary>
        /// 次の一団までの距離に届かないまま、これだけ動けなかったら次の一団を出す（秒）。
        /// ステージの端や障害物でゲームが止まって見えるのを防ぐ。
        /// </summary>
        [System.NonSerialized] public float StallSeconds = 2.5f;

        float _stallTimer;
        float _lastPlayerX;

        [Header("Sound")]
        [Range(0f, 1f)] public float BodyDropVolume = 0.95f;

        readonly List<EnemyBrain> _enemies = new List<EnemyBrain>();

        // 倒された敵の「着地待ち」（倒れる音を遅らせるため）
        readonly List<Vector3> _pendingDrops = new List<Vector3>();
        float _pendingTimer;

        float _collectRetry;
        float _nextTriggerX = float.NaN;
        float _lastWaveX;

        /// <summary>これまでに倒した数。</summary>
        public int Kills { get; private set; }

        /// <summary>生きている敵の数。</summary>
        public int Alive { get; private set; }

        /// <summary>いまの画面が片付いているか。</summary>
        public bool Cleared => Alive <= 0;

        /// <summary>これまでに出した一団の数。</summary>
        public int Wave { get; private set; }

        /// <summary>次の一団を出す X（計測用）。</summary>
        public float NextTriggerX => _nextTriggerX;

        /// <summary>決められた数の一団を片付け終えたか（もう湧かない）。</summary>
        public bool StageCleared { get; private set; }

        /// <summary>
        /// この数の一団を片付けたら終わり（0 なら無限）。1 波 = 敵 3 体。
        /// シーンに保存された値に影響されないよう NonSerialized。
        /// </summary>
        [System.NonSerialized] public int MaxWaves = 10;

        /// <summary>ゲーム開始からクリアまでの秒数（クリア時のみ）。</summary>
        public float ClearTimeSeconds { get; private set; }

        /// <summary>これまでの最速クリア（秒）。0 なら記録なし。</summary>
        public float BestClearSeconds { get; private set; }

        const string BestTimeKey = "Mio.BestClearSeconds.Normal10.v2";
        float _runSeconds;
        bool _recordEligible = true;

        void Start()
        {
            ApplyCommandLine();
            Collect();

            BestClearSeconds = PlayerPrefs.GetFloat(BestTimeKey, 0f);

            // 最初の一団は、始まった画面の右端から入ってくる
            if (_enemies.Count > 0)
                EnterWave(CenterX());
        }

        /// <summary>
        /// 比較用: <c>-screenspan=7</c> で「1 画面ぶんの距離」を差し替える。
        /// シーンに保存された値を起動時だけ上書きするので、7 / 9 / 11 を撮り比べられる。
        /// </summary>
        void ApplyCommandLine()
        {
            const string spanKey = "-screenspan=";
            const string wavesKey = "-maxwaves=";

            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                // 検証用のゲーム条件では通常プレイの記録を更新しない。
                if (a.StartsWith("-", System.StringComparison.Ordinal) &&
                    (a.StartsWith("-maxwaves", System.StringComparison.OrdinalIgnoreCase) ||
                     a.StartsWith("-screenspan", System.StringComparison.OrdinalIgnoreCase) ||
                     a.StartsWith("-hold", System.StringComparison.OrdinalIgnoreCase) ||
                     a.StartsWith("-encountertest", System.StringComparison.OrdinalIgnoreCase) ||
                     a.StartsWith("-grappletest", System.StringComparison.OrdinalIgnoreCase)))
                    _recordEligible = false;

                if (a.StartsWith(spanKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(a.Substring(spanKey.Length),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float value)
                        && value > 1f)
                    {
                        ScreenSpan = value;
                        Debug.Log($"[Encounter] ScreenSpan={ScreenSpan:F1}（起動引数で上書き）");
                    }

                    continue;
                }

                // 0 を渡すと無限（計測用）
                if (a.StartsWith(wavesKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a.Substring(wavesKey.Length), out int waves) && waves >= 0)
                    {
                        MaxWaves = waves;
                        Debug.Log($"[Encounter] MaxWaves={MaxWaves}（起動引数で上書き。0 は無限）");
                    }
                }
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

            // 押さえつけ（Pin）は本来「巨躯」の役割だが、巨躯の見た目はまだ無い。
            // 設計（docs/plans/2026-11-03-pin-grapple-design.md）の判断どおり、
            // 既存の敵 1 体に任せて数値と姿勢を先に固める。
            if (_enemies.Count > 0 && _enemies[_enemies.Count - 1] != null)
            {
                var pinner = _enemies[_enemies.Count - 1];
                pinner.CanPin = true;
                Debug.Log($"[Encounter] 押さえつけ担当: {pinner.name}");
            }

            // 関節技は 2 体目に任せる（押さえつけ役と分けて、拘束の種類が偏らないようにする）。
            if (_enemies.Count > 1 && _enemies[_enemies.Count - 2] != null)
            {
                var locker = _enemies[_enemies.Count - 2];
                locker.CanArmLock = true;
                Debug.Log($"[Encounter] 関節技担当: {locker.name}");
            }

            Debug.Log($"[Encounter] 敵 {_enemies.Count} 体を登録 1画面={ScreenSpan:F1}m " +
                $"（-screenspan=N で上書き可）");
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

            Debug.Log($"[Encounter] 撃破 {Kills} 残り {Alive} t={Time.time:F2}");

            if (Alive <= 0)
                Debug.Log($"[Encounter] 全滅 wave={Wave} 右へ進める t={Time.time:F2}");
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

            // 片付いていないあいだは次の一団を出さない
            if (Alive > 0)
            {
                _stallTimer = 0f;
                _lastPlayerX = PlayerX();
                return;
            }

            if (StageCleared)
                return;

            // 決められた数の一団を片付けたら終わり（0 なら無限）
            if (MaxWaves > 0 && Wave >= MaxWaves)
            {
                StageCleared = true;
                ClearTimeSeconds = _runSeconds;

                // 最速を更新したときだけ保存（毎回書くと無駄にディスクへ行く）
                if (_recordEligible && (BestClearSeconds <= 0f || ClearTimeSeconds < BestClearSeconds))
                {
                    BestClearSeconds = ClearTimeSeconds;
                    PlayerPrefs.SetFloat(BestTimeKey, ClearTimeSeconds);
                    PlayerPrefs.Save();
                }

                // 終わったことを音でも知らせる（画面のバナーだけだと見落とす）
                ProceduralSfx.Instance?.Play(SfxId.Clear, 1f);

                Debug.Log($"[Encounter] STAGE CLEAR {Wave} 波 / 撃破 {Kills} " +
                    $"time={ClearTimeSeconds:F1}s best={BestClearSeconds:F1}s t={Time.time:F2}");
                return;
            }

            float centerX = CenterX();

            if (float.IsNaN(_nextTriggerX))
                _nextTriggerX = _lastWaveX + ScreenSpan;

            // 1 画面ぶん進んだら、次の一団が右端から入ってくる
            if (centerX >= _nextTriggerX)
            {
                EnterWave(centerX);
                return;
            }

            // 進めないときの逃げ道。
            // ステージの端や障害物で「次の一団までの距離」に届かないと、
            // 画面がロックされないまま何も起きず、ゲームが止まって見える。
            // 一定時間まったく前に進めていなければ、待たせずに次の一団を出す。
            float playerX = PlayerX();
            if (playerX - _lastPlayerX > 0.05f)
            {
                _lastPlayerX = playerX;
                _stallTimer = 0f;
                return;
            }

            _lastPlayerX = playerX;
            _stallTimer += Time.deltaTime;

            if (_stallTimer >= StallSeconds)
            {
                Debug.Log($"[Encounter] {StallSeconds:F1} 秒進めないので次の一団を出します (x={playerX:F2})");
                EnterWave(centerX);
            }
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

        /// <summary>画面の右端の外に一団を並べて、歩いて入ってこさせる。</summary>
        void EnterWave(float centerX)
        {
            _lastWaveX = centerX;

            // 画面の右端の外。ただし彼女の目の前には出さない（いきなり現れない）。
            float playerX = Player != null ? Player.position.x : centerX;
            float edge = Mathf.Max(centerX + SpawnAhead, playerX + Mathf.Max(0f, MinSpawnGap));

            for (int i = 0; i < _enemies.Count; i++)
            {
                var brain = _enemies[i];
                if (brain == null)
                    continue;

                float z = Player != null ? Player.position.z : 0f;
                Vector3 pos = new Vector3(edge + i * EntrySpacing, 0f, z);

                brain.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, -90f, 0f));
                brain.Revive();
            }

            // 足音・予備動作の音は 1 体ごとに鳴らすと 3 重なって割れるので、一団につき 1 回。
            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
                sfx.PlayAt(new Vector3(edge, 0f, 0f), SfxId.TellHeavy, 0.7f);

            Alive = CountAlive();
            Wave++;
            _nextTriggerX = centerX + ScreenSpan;

            Debug.Log($"[Encounter] wave {Wave} 敵 {Alive} 体が右端から入場 (x={edge:F1}) t={Time.time:F2}");

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
                Debug.Log($"[Encounter] 起きあがり確認 {brain.name} phase={brain.Phase} fall={fall:F1} hp={brain.State.Hp:F0}");
            }
        }
    }
}
