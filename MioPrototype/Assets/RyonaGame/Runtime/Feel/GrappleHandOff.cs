using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 拘束の「引き渡し（たらい回し）」。
    ///
    /// 3 体で戦っているとき、拘束している敵が別の敵に彼女を引き渡す。
    /// 掴まれたまま相手だけが入れ替わるので、もがきの進捗はやり直しになり
    /// 「抜けても次の敵に捕まる」圧力になる。
    ///
    /// タッグ腹パンのコンボ中は邪魔をしない（演出が終わってから引き渡す）。
    /// </summary>
    [AddComponentMenu("Ryona/Grapple Hand Off")]
    public sealed class GrappleHandOff : MonoBehaviour
    {
        /// <summary>掴まれてから引き渡すまでの秒数。</summary>
        public float DelaySeconds = 2.6f;

        /// <summary>引き渡しの間隔（これ以上あける）。</summary>
        public float IntervalSeconds = 3.5f;

        /// <summary>引き渡せる距離（これより遠い敵には渡さない）。</summary>
        public float Range = 4.0f;

        PlayerGrapple _grapple;
        HeldBellyPunchDirector _belly;
        float _elapsed;
        float _nextAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GrappleHandOff>() != null)
                return;

            var go = new GameObject("Grapple Hand Off");
            go.AddComponent<GrappleHandOff>();
        }

        void Update()
        {
            if (_grapple == null)
                _grapple = FindFirstObjectByType<PlayerGrapple>();
            if (_grapple == null || !_grapple.IsGrappled)
            {
                _elapsed = 0f;
                return;
            }

            if (_belly == null)
                _belly = FindFirstObjectByType<HeldBellyPunchDirector>();

            // タッグ腹パンの最中は渡さない（殴っている敵が変わると演出が崩れる）
            if (_belly != null && _belly.IsPerforming)
            {
                _elapsed = 0f;
                return;
            }

            if (_grapple.Kind == GrappleKind.Pin)
            {
                // 押さえつけは時間制限つきの短い技なので渡さない
                _elapsed = 0f;
                return;
            }

            _elapsed += Time.deltaTime;
            if (_elapsed < DelaySeconds || Time.time < _nextAt)
                return;

            if (TryHandOff())
            {
                _nextAt = Time.time + IntervalSeconds;
                _elapsed = 0f;
            }
        }

        bool TryHandOff()
        {
            var captor = _grapple.Captor;
            if (captor == null)
                return false;

            EnemyBrain best = null;
            float bestDistance = Range;

            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive || enemy.IsRising)
                    continue;
                if (enemy.Phase == EnemyPhase.Windup || enemy.Phase == EnemyPhase.Attack ||
                    enemy.Phase == EnemyPhase.Stagger || enemy.IsHolding)
                    continue;

                float d = Vector3.Distance(enemy.transform.position, _grapple.transform.position);
                if (d >= bestDistance)
                    continue;

                best = enemy;
                bestDistance = d;
            }

            if (best == null)
                return false;

            // 抽選: 正面拘束（膝蹴り派生）か背面拘束（羽交い締め→腹パン）か。
            GrappleKind kind = Random.value < 0.5f ? GrappleKind.Pin : GrappleKind.Back;
            _grapple.HandOff(best, kind, shove: true);
            best.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
            Debug.Log($"[HandOff] {captor.name} → {best.name} kind={kind} dist={bestDistance:F2}");
            return true;
        }
    }
}
