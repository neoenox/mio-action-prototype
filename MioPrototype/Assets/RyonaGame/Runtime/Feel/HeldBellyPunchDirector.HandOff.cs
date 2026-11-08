using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 拘束の継続・引き渡し・クールタイム。
    /// </summary>
    public sealed partial class HeldBellyPunchDirector
    {
        void UpdateHandOff()
        {
            if (ResolveCamera() && (_camera.IsFocusing ||
                !Mathf.Approximately(_camera.EffectDistanceScale, 1f) ||
                !Mathf.Approximately(_camera.EffectPivotOffset, 0f)))
            {
                _handOffTimer = Mathf.Max(0f, HandOffDelaySeconds);
                return;
            }

            SetPunchGuard(null);
            _handOffTimer -= Time.deltaTime;
            if (_handOffTimer > 0f)
                return;

            _phase = Phase.Idle;
            int hits = _hits;
            var chain = FindFirstObjectByType<GrappleHandOff>();
            if (chain != null && chain.TryChain())
            {
                Debug.Log($"[HeldPunch] combo end {hits}/{ComboHitsTotal} → hand off");
                HoldOthers(ReleaseGraceSeconds);
                return;
            }

            ContinueHold();
        }

        void ContinueHold()
        {
            SetPunchGuard(null);
            if (_grapple != null && _grapple.IsGrappled)
            {
                _grapple.ClearStruggleSuppression();
                Debug.Log($"[HeldPunch] combo end {_hits}/{ComboHitsTotal} → hold continues");
            }

            HoldOthers(ReleaseGraceSeconds);

            // 数秒のクールタイムを置いて、また 5 発のコンボを始められる状態に戻す。
            // 抜けるまで「撃たれる → 少し休む → また撃たれる」が続く。
            StartComboCooldown();
            HasAttacker = false;
        }

        /// <summary>次の連撃は再び正面の投げから始める。</summary>
        void StartComboCooldown()
        {
            _attempts = 0;
            _cooldown = Mathf.Max(0.1f, RestartCooldownSeconds);

            if (_grapple == null || !_grapple.IsGrappled || _grapple.Kind != GrappleKind.Back)
            {
                _phase = Phase.Idle;
                return;
            }

            _phase = Phase.WaitingThrow;
            _throwTimer = _cooldown;
        }

        /// <summary>コンボ中、カメラを斜め前の構図に保つ。ヒットごとに押し直す。</summary>
        void HoldOthers(float seconds)
        {
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, _stats.transform.position) > 8f)
                    continue;

                enemy.HoldFire(seconds);
            }
        }
    }
}
