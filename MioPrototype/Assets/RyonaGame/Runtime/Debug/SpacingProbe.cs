using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 間合いの実測用。<c>-statlog</c> 付きで起動したときだけ動く。
    /// 彼女と各敵、敵同士の最小距離を 1 秒ごとに出す。
    /// 「敵が彼女にめり込む」を目視ではなく数値で確かめるために置いてある。
    /// </summary>
    public sealed class SpacingProbe : MonoBehaviour
    {
        float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!LaunchOptions.Has("-statlog"))
                return;

            var go = new GameObject("Spacing Probe");
            go.AddComponent<SpacingProbe>();
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;

            _timer = 1f;

            var brains = FindObjectsByType<EnemyBrain>(FindObjectsInactive.Exclude);
            if (brains.Length == 0)
                return;

            Transform player = null;
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i].Target != null)
                {
                    player = brains[i].Target;
                    break;
                }
            }

            float minPlayer = float.PositiveInfinity;
            if (player != null)
            {
                for (int i = 0; i < brains.Length; i++)
                {
                    if (brains[i].Phase == EnemyPhase.Dead)
                        continue;

                    float d = Flat(brains[i].transform.position - player.position).magnitude;
                    if (d < minPlayer)
                        minPlayer = d;
                }
            }

            float minPair = float.PositiveInfinity;
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i].Phase == EnemyPhase.Dead)
                    continue;

                for (int j = i + 1; j < brains.Length; j++)
                {
                    if (brains[j].Phase == EnemyPhase.Dead)
                        continue;

                    float d = Flat(brains[i].transform.position - brains[j].transform.position).magnitude;
                    if (d < minPair)
                        minPair = d;
                }
            }

            Debug.Log($"[Spacing] player={minPlayer:0.00} pair={minPair:0.00}");

            // 膠着の切り分け用: 敵ごとの状態。距離・トークン・クールダウン・向き。
            if (player != null)
            {
                for (int i = 0; i < brains.Length; i++)
                {
                    var b = brains[i];
                    if (b.Phase == EnemyPhase.Dead)
                        continue;

                    Vector3 to = Flat(player.position - b.transform.position);
                    float facing = to.sqrMagnitude > 0.0001f ? Vector3.Dot(Flat(b.transform.forward).normalized, to.normalized) : 0f;
                    Debug.Log($"[SpacingEnemy] {b.name} phase={b.Phase} dist={to.magnitude:0.00} " +
                        $"token={b.HasAttackToken} cd={b.AttackCooldownRemaining:0.0} facing={facing:0.00}");
                }
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
