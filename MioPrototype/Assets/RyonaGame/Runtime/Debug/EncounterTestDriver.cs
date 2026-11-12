using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 「決着」の通し確認用（<c>-encountertest</c>）。
    ///
    /// 2 秒ごとに生きている敵を 1 体だけ即死させ、撃破数 → 全滅 → 次の波、が
    /// 一通り回るかを実機で確かめる。通常プレイでは動かない。
    /// </summary>
    [AddComponentMenu("Ryona/Encounter Test Driver")]
    public sealed class EncounterTestDriver : MonoBehaviour
    {
        /// <summary>何秒ごとに 1 体倒すか。</summary>
        public float Interval = 2f;

        /// <summary>この体数を倒したら止める（0 = 止めない）。</summary>
        public int MaxKills;

        float _timer = 1.5f;
        int _kills;
        float _logTimer = 2f;
        EncounterDirector _director;
        BeltScreenLock _lock;
        CombatState _attacker;
        AttackData _lethal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            bool wanted = LaunchOptions.Has("-encountertest");

            if (!wanted)
                return;

            var go = new GameObject("Encounter Test Driver");
            var driver = go.AddComponent<EncounterTestDriver>();

            string limit = LaunchOptions.Value("-encounterkills");
            if (int.TryParse(limit, out int kills))
                driver.MaxKills = kills;
        }

        void Awake()
        {
            _attacker = new CombatState();

            _lethal = AttackLibrary.Heavy();
            _lethal.id = "test_lethal";
            _lethal.damage = 9999f;

            _director = FindFirstObjectByType<EncounterDirector>();
            _lock = FindFirstObjectByType<BeltScreenLock>();

            Debug.Log("[Encounter] test 開始（2 秒ごとに 1 体倒します）");
        }

        void Update()
        {
            // 計測用: 波が進まないときに「どこで止まっているか」を出す
            _logTimer -= Time.deltaTime;
            if (_logTimer <= 0f)
            {
                _logTimer = 2f;

                if (_director != null)
                {
                    float px = _director.Player != null ? _director.Player.position.x : float.NaN;
                    float cx = _director.Camera != null ? _director.Camera.CurrentX : float.NaN;
                    bool locked = _lock != null && _lock.IsLocked;

                    Debug.Log($"[EncTest] t={Time.time:F1} playerX={px:F2} camX={cx:F2} " +
                        $"next={_director.NextTriggerX:F2} alive={_director.Alive} wave={_director.Wave} locked={locked}");

                    // 進めない原因の切り分け: 彼女の周囲に何があるか
                    if (_director.Player != null && !locked && _director.Alive <= 0 && px < _director.NextTriggerX - 1f)
                    {
                        var pos = _director.Player.position;
                        var hits = Physics.OverlapCapsule(pos + Vector3.up * 0.4f, pos + Vector3.up * 1.4f, 0.35f);
                        string names = "";

                        foreach (var h in hits)
                            names += $"{h.name}@{h.bounds.center.x:F2} ";

                        Debug.Log($"[EncTest] 停止中 y={pos.y:F2} 周囲=[{names}]");
                    }
                }
            }

            if (MaxKills > 0 && _kills >= MaxKills)
                return;

            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;

            _timer = Mathf.Max(0.2f, Interval);

            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain == null || !brain.IsAlive)
                    continue;

                brain.TakeHit(_attacker, _lethal);
                _kills++;
                return;   // 1 回に 1 体だけ
            }
        }
    }
}
