using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 戦闘の「手触り」をまとめる演出ディレクター。
    ///
    /// 判定（<see cref="PlayerCombat"/> / <see cref="PlayerStats"/>）には一切干渉せず、
    /// イベントを見てヒットストップ・カメラ揺れ・SE を再生するだけに留める。
    /// 数値を変えたいときはここだけ触ればよい。
    /// </summary>
    [AddComponentMenu("Ryona/Combat Feel")]
    public sealed class CombatFeel : MonoBehaviour
    {
        [Header("References")]
        public PlayerCombat Combat;
        public PlayerStats Stats;
        public ThirdPersonCamera CameraRig;

        [Header("Hit Stop (seconds)")]
        public float HitHitStop = 0.065f;
        public float ReceivedHitStop = 0.07f;

        [Header("Camera Shake (meter, seconds)")]
        public float HitShake = 0.06f;
        public float HitShakeSeconds = 0.16f;
        public float ReceivedShake = 0.12f;
        public float ReceivedShakeSeconds = 0.26f;

        [Header("Heavy Attack")]
        [Tooltip("Heavy のときの揺れ倍率")]
        public float HeavyShakeScale = 1.4f;

        [Tooltip("Heavy のときのヒットストップ倍率")]
        public float HeavyHitStopScale = 1.3f;

        [Header("Voice")]
        [Tooltip("被弾の声を続けて出さない間隔（秒）。連続で殴られても「あっあっあっ」にしない")]
        public float HurtVoiceInterval = 0.55f;

        CameraShake _shake;
        ProceduralSfx _sfx;
        float _nextHurtVoiceAt;

        // HitLanded には攻撃データが乗ってこないので、直近の攻撃種別を覚えておく
        AttackKind _lastKind = AttackKind.Light;

        void Start()
        {
            if (Combat == null)
                Combat = GetComponent<PlayerCombat>();
            if (Stats == null)
                Stats = GetComponent<PlayerStats>();

            ResolveCameraRig();
            ResolveShake();

            if (Combat != null)
            {
                Combat.AttackStarted += OnAttackStarted;
                Combat.HitLanded += OnHitLanded;
            }

            if (Stats != null)
                Stats.HitReceived += OnHitReceived;
        }

        void OnDestroy()
        {
            if (Combat != null)
            {
                Combat.AttackStarted -= OnAttackStarted;
                Combat.HitLanded -= OnHitLanded;
            }

            if (Stats != null)
                Stats.HitReceived -= OnHitReceived;
        }

        // --- 購読ハンドラ -----------------------------------------------

        void OnAttackStarted(AttackData attack)
        {
            if (attack != null)
                _lastKind = attack.kind;

            PlaySfx(SfxId.Swing);
        }

        void OnHitLanded(HitResult result, IDamageable target)
        {
            // 重い一撃は少しだけ強くする
            bool heavy = _lastKind == AttackKind.Heavy;
            PlaySfx(SfxId.Impact);
            Shake(HitShake * (heavy ? HeavyShakeScale : 1f), HitShakeSeconds);
            HitStop.Play(HitHitStop * (heavy ? HeavyHitStopScale : 1f));
        }

        void OnHitReceived(HitResult result)
        {
            // 強い一撃ほど重く止める。弱い一撃まで長く止めると、殴られるたびに操作が重くなる。
            float weight = result.Stagger switch
            {
                StaggerTier.Small => 0.75f,
                StaggerTier.Large => 1.35f,
                _ => 1f
            };

            bool heldFace = result.Damage > 0f && Stats != null &&
                Stats.LastHitWasTagPunch && Stats.LastHitZone == PunchZone.Face;
            // 顔の寄り画では、細かく長く揺らすより命中の短い衝撃を見せる。
            // 顔面パンチは「当たった」ことが分かるよう、揺れを強め・短くする。
            Shake(ReceivedShake * (heldFace ? 1.55f : weight),
                heldFace ? ReceivedShakeSeconds * 0.60f : ReceivedShakeSeconds);
            float stopSeconds = Stats != null && Stats.GroundMount.IsApplyingPunch ? 0.025f : ReceivedHitStop;
            // タッグ顔面は演出側の短いストップを汎用被弾の長さで上書きしない。
            HitStop.Play(heldFace ? 0.035f : stopSeconds * weight);

            // 敵は素手。重い被弾も金属音ではなく打撃音を使う。
            PlaySfx(SfxId.Impact, heldFace || weight > 1.1f ? 1f : 0.8f);
            PlaySfx(SfxId.Stagger, 0.7f);

            // 拘束中の腹パン・膝蹴りは、腹用と強い被弾用の声を半々で鳴らす。
            if (Stats != null && Stats.LastHitZone == PunchZone.Body)
            {
                SfxId bellyVoice = Stats.LastHitWasHeldBelly && Random.value < 0.5f
                    ? SfxId.HurtLarge : SfxId.HurtBelly;
                PlayHurtVoice(bellyVoice);
                return;
            }

            // 軽い被弾は hurt_small、重い被弾は hurt_large。中くらいは半々。
            SfxId voice = result.Stagger switch
            {
                StaggerTier.Small => SfxId.HurtSmall,
                StaggerTier.Large => SfxId.HurtLarge,
                StaggerTier.GuardBreak => SfxId.HurtLarge,
                _ => Random.value < 0.5f ? SfxId.HurtSmall : SfxId.HurtLarge
            };
            PlayHurtVoice(voice);
        }

        // --- 参照の解決 -------------------------------------------------

        void ResolveCameraRig()
        {
            if (CameraRig != null)
                return;

            var cam = Camera.main;
            if (cam != null)
                CameraRig = cam.GetComponent<ThirdPersonCamera>();
        }

        /// <summary>
        /// 揺れの実体を探す。カメラに付いていなければ、その場で足して有効化する。
        /// 参照が無くても落ちないようにするのが目的。
        /// </summary>
        void ResolveShake()
        {
            if (_shake != null)
                return;

            if (CameraRig != null)
            {
                _shake = CameraRig.GetComponent<CameraShake>();
                if (_shake == null)
                    _shake = CameraRig.gameObject.AddComponent<CameraShake>();
            }

            if (_shake == null)
            {
                var cam = Camera.main;
                if (cam != null)
                    _shake = cam.GetComponent<CameraShake>();
            }
        }

        // --- 再生の補助 -------------------------------------------------

        internal void OnWallSlam() => Shake(0.22f, 0.30f);

        void Shake(float amplitude, float duration)
        {
            ResolveShake();
            if (_shake == null)
                return;

            _shake.Shake(amplitude, duration);
        }

        void PlayHurtVoice(SfxId id)
        {
            if (Time.time < _nextHurtVoiceAt)
                return;

            _nextHurtVoiceAt = Time.time + Mathf.Max(0f, HurtVoiceInterval);
            PlaySfx(id);
        }

        void PlaySfx(SfxId id, float volume = 1f)
        {
            if (_sfx == null)
                _sfx = ProceduralSfx.Instance;

            if (_sfx == null)
                return;

            _sfx.Play(id, volume);
        }
    }
}
