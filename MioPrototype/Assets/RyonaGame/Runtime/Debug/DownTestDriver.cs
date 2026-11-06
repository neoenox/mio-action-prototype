using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ダウン（ひるみの蓄積 → 放物線で吹き飛ばされ → 倒れる）を撮るための入口。
    ///
    /// <c>-downburst=&lt;prefix&gt;</c> を付けて起動すると、1.5 秒後から一定間隔で
    /// 彼女に攻撃を当て続け、ダウンした瞬間から 0.05 秒ごとに 40 枚（2 秒ぶん）撮る。
    /// <c>-downhits=heavy</c> で重い攻撃に切り替え（4 発でダウン）。
    /// <c>-downhits=guard</c> でガードさせたまま殴る（ガードブレイクの確認）。
    /// </summary>
    [AddComponentMenu("Ryona/Down Test Driver")]
    public sealed class DownTestDriver : MonoBehaviour
    {
        /// <summary>撮影先（<c>.../d.png</c> なら d_00.png 〜 d_39.png）。</summary>
        public string BurstPrefix;

        /// <summary>当てる攻撃（light / heavy / break / guard）。</summary>
        public string Attack = "light";

        /// <summary>攻撃の間隔（秒）。</summary>
        public float IntervalSeconds = 0.30f;

        /// <summary>最初の一撃までの秒。</summary>
        public float FirstHitSeconds = 1.5f;

        PlayerStats _stats;
        float _nextHitAt = -1f;
        int _hits;

        bool _capturing;
        bool _captured;
        int _shots;
        float _nextShotAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<DownTestDriver>() != null)
                return;

            string prefix = null;
            string attack = "light";
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("-downburst=", System.StringComparison.OrdinalIgnoreCase))
                    prefix = a.Substring("-downburst=".Length);
                else if (a.StartsWith("-downhits=", System.StringComparison.OrdinalIgnoreCase))
                    attack = a.Substring("-downhits=".Length).Trim().ToLowerInvariant();
            }

            if (prefix == null)
                return;

            var go = new GameObject("Down Test Driver");
            var driver = go.AddComponent<DownTestDriver>();
            driver.BurstPrefix = prefix;
            driver.Attack = attack;

            Debug.Log($"[Down] -downburst: {driver.FirstHitSeconds:F1} 秒後から {attack} を " +
                      $"{driver.IntervalSeconds:F2} 秒間隔で当てます");
        }

        void Update()
        {
            if (_stats == null)
            {
                _stats = FindFirstObjectByType<PlayerStats>();
                if (_stats == null)
                    return;

                _nextHitAt = Time.time + FirstHitSeconds;

                // 撮影中は AI に殴らせない（当てるのはこのドライバだけ）。
                // 混ざると「どの一撃で倒れたか」が分からず、狙った絵も撮れない。
                int away = 0;
                foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (enemy == null)
                        continue;

                    enemy.HoldFire(9999f);

                    // 彼女の真横に立たれると絵が隠れるので、横へ離す
                    Vector3 p = enemy.transform.position;
                    p.x = _stats.transform.position.x + (away % 2 == 0 ? 3.2f : -3.2f) * (1 + away / 2);
                    away++;
                    enemy.transform.position = p;
                }
            }

            if (_capturing)
            {
                CaptureTick();
                return;
            }

            if (_captured)
                return;

            if (_stats.State != null && _stats.State.IsDown)
            {
                _capturing = true;
                _captured = true;
                _shots = 0;
                _nextShotAt = Time.unscaledTime;
                return;
            }

            if (_stats.IsDead || Time.time < _nextHitAt)
                return;

            _nextHitAt = Time.time + IntervalSeconds;
            Hit();
        }

        void Hit()
        {
            var attack = AttackLibrary.Light();
            if (Attack == "heavy")
                attack = AttackLibrary.Heavy();
            else if (Attack == "break")
                attack = AttackLibrary.BreakAttack();

            // 一番近い敵に殴らせる（吹き飛ぶ向きが「殴った相手の反対側」になる）
            EnemyBrain from = null;
            float best = float.MaxValue;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                float d = Vector3.Distance(enemy.transform.position, _stats.transform.position);
                if (d >= best)
                    continue;

                from = enemy;
                best = d;
            }

            // 腹を殴られた扱いにする（前へ崩れるダウンの確認用）
            if (Attack == "belly")
                _stats.LastHitZone = PunchZone.Body;

            _stats.IsBlocking = Attack == "guard";
            _stats.ReceiveHit(from != null ? from.State : null, attack,
                from != null ? from.transform : null);
            _stats.IsBlocking = false;

            _hits++;
            Debug.Log($"[Down] hit #{_hits} attack={Attack} flinch={_stats.State.Flinch:F0}");
        }

        void CaptureTick()
        {
            const int count = 40;
            const float interval = 0.05f;

            if (Time.unscaledTime < _nextShotAt)
                return;

            string dir = System.IO.Path.GetDirectoryName(BurstPrefix);
            string name = System.IO.Path.GetFileNameWithoutExtension(BurstPrefix);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"{name}_{_shots:00}.png"));

            _shots++;
            _nextShotAt += interval;

            if (_shots >= count)
            {
                _capturing = false;
                Debug.Log($"[Down] burst {count} 枚 {BurstPrefix}_XX.png");
            }
        }
    }
}
