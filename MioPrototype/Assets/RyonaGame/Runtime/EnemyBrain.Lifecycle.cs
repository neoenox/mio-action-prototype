using System;
using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// ダメージを受ける。解決は <see cref="DamageResolver"/> に任せ、
        /// 怯みが出たら進行中の攻撃をキャンセルして <see cref="EnemyPhase.Stagger"/> へ移る。
        /// </summary>
        public HitResult TakeHit(CombatState attacker, AttackData attack)
        {
            if (Phase == EnemyPhase.Dead)
                return default;

            var result = DamageResolver.Resolve(attacker, attack, State);

            // 演出側（EnemyReaction）はこのイベントだけを見ているので必ず呼ぶ
            Hit?.Invoke(result);

            if (!State.IsAlive)
            {
                Die();
                return result;
            }

            if (result.Stagger != StaggerTier.None)
                EnterStagger(result.Stagger, attack.targetPart);

            return result;
        }

        internal void EnterStagger(StaggerTier tier, BodyPart part = BodyPart.Torso)
        {
            if (IsHolding && Player != null)
            {
                var hold = Player.GetComponent<PlayerGrapple>();
                if (hold != null && hold.IsGrappled && hold.Captor == this)
                    hold.Release();
                IsHolding = false;
            }
            // 進行中の攻撃はキャンセルする。トークンも返して他の敵に順番を渡す。
            AbsorbLungeOffset();
            HideTelegraph();
            ClearGrappleState(clearAttackState: true);
            ReleaseToken();

            Phase = EnemyPhase.Stagger;
            _timer = Mathf.Max(0f, StaggerSecondsOf(tier));
            _staggerTotal = Mathf.Max(0.01f, _timer);
            StaggerProgress = 1f;
            StaggerElapsed = 0f;
            StaggerPart = part;
            StaggerWeight = tier switch
            {
                StaggerTier.Large => 1.8f,
                StaggerTier.Medium => 1.4f,
                _ => 1f
            };
        }

        void EndStagger()
        {
            StaggerProgress = 0f;
            StaggerWeight = 1f;
            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            Phase = EnemyPhase.Chase;
        }

        float StaggerSecondsOf(StaggerTier tier)
        {
            switch (tier)
            {
                case StaggerTier.Small:
                    return StaggerSecondsSmall > 0f ? StaggerSecondsSmall : CombatConstants.StunSmall;
                case StaggerTier.Medium:
                    return StaggerSecondsMedium > 0f ? StaggerSecondsMedium : CombatConstants.StunMedium;
                case StaggerTier.Large:
                    return StaggerSecondsLarge > 0f ? StaggerSecondsLarge : CombatConstants.StunLarge;
                default:
                    return 0f;
            }
        }

        void Die()
        {
            if (Phase == EnemyPhase.Dead)
                return;

            Phase = EnemyPhase.Dead;
            HideTelegraph();
            IsHolding = false;
            ClearGrappleState(clearAttackState: true);
            _pinOrder = false;
            _bearHugOrder = false;
            ReleaseToken();
            UnregisterGroup();
            enabled = false;

            // 死体を障害物にしない。
            // 当たり判定を残すと、片付けたあとで彼女が死体に詰まって次の画面へ歩けなくなる
            // （実測: x=8.46 で停止、0.29m 先に倒れた敵のカプセル）。
            // 生きている敵の判定は残すので、すり抜けは起きない。
            var body = GetComponent<Collider>();
            if (body != null)
                body.enabled = false;

            Debug.Log($"[Enemy] die {name}");
            Died?.Invoke(this);
        }

        /// <summary>
        /// 倒れた敵を立て直す（波ごとの再出現）。
        /// 同じオブジェクトを使い回すので、生成し直すより軽く、
        /// HUD や他の敵が持っている参照も切れない。
        /// </summary>
        public void Revive()
        {
            if (Phase != EnemyPhase.Dead)
                return;

            State.RestoreAll();
            _timer = 0f;
            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            _run = default;
            _feinted = false;
            WindupProgress = 0f;

            // 死亡時に取りこぼした遷移状態を必ず戻す（再出現後に正面回り込みや
            // 重なり回避が効かない、という残り方を防ぐ）。
            IsHolding = false;
            ClearGrappleState(clearAttackState: true, resetKneeTimer: true);
            _pinOrder = false;
            _bearHugOrder = false;
            _riseTimer = RiseSeconds * UnityEngine.Random.Range(1f, 1.4f);   // 見た目はここから起き上がる（少しずつずらす）

            Phase = EnemyPhase.Chase;
            enabled = true;

            // 死んだときに切った当たり判定を戻す（生きている敵はすり抜けさせない）
            var body = GetComponent<Collider>();
            if (body != null)
                body.enabled = true;

            RegisterGroup();

            Debug.Log($"[Enemy] revive {name} hp={State.Hp:F0} rise={RiseSeconds:F2}s");
        }

        // --- グループ連携 --------------------------------------------------------

    }
}
