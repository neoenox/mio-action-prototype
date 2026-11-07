using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 戦闘の瞬間にカメラの「寄り引き」と「見下ろし」を付ける演出。
    ///
    /// 判定（<see cref="PlayerCombat"/> / <see cref="PlayerStats"/>）には一切干渉せず、
    /// イベントを見て <see cref="ThirdPersonCamera.PushDistance"/> /
    /// <see cref="ThirdPersonCamera.PushPivot"/> を押し込むだけに留める。
    /// <see cref="ThirdPersonCamera.Distance"/> そのものは触らないので、
    /// プレイヤーのホイール操作は常にそのまま効く。
    ///
    /// 空振り（<see cref="PlayerCombat.AttackStarted"/> から 0.25 秒以内に
    /// <see cref="PlayerCombat.HitLanded"/> が来ない場合）では何もしない。
    /// 演出はすべて「当たった／殴られた」の結果だけを起点にしている。
    /// </summary>
    [AddComponentMenu("Ryona/Camera Director")]
    public sealed class CameraDirector : MonoBehaviour
    {
        [Header("References")]
        public ThirdPersonCamera Rig;
        public PlayerStats Stats;
        public PlayerCombat Combat;

        [Header("Push In (自分が当てた)")]
        [Tooltip("通常ヒットで寄る倍率")]
        public float PushInScale = 0.90f;

        [Tooltip("通常ヒットで寄せておく秒数")]
        public float PushInSeconds = 0.20f;

        [Header("Hit Pull (自分が殴られた)")]
        [Tooltip("被弾で引く倍率（ノックバック感）")]
        public float HitPullScale = 1.10f;

        [Tooltip("被弾で引いておく秒数")]
        public float HitPullSeconds = 0.30f;

        [Tooltip("被弾でピボットを下げる量（m）")]
        public float HitPivotDrop = -0.10f;

        [Tooltip("被弾でピボットを下げておく秒数")]
        public float HitPivotSeconds = 0.30f;

        [Header("Stagger (Large 以上の怯み)")]
        [Tooltip("怯みで引く倍率")]
        public float StaggerPullScale = 1.16f;

        [Tooltip("怯みで引いておく秒数")]
        public float StaggerPullSeconds = 0.45f;

        [Tooltip("怯みでピボットを下げる量（m）")]
        public float StaggerPivotDrop = -0.18f;

        [Tooltip("怯みでピボットを下げておく秒数")]
        public float StaggerPivotSeconds = 0.45f;

        [Header("Death (倒れたら戻さない)")]
        [Tooltip("死亡時に引く倍率（寄りではなく引きで見下ろす）")]
        public float DeathPullScale = 1.25f;

        [Tooltip("死亡時にピボットを下げる量（m）")]
        public float DeathPivotDrop = -0.85f;

        [Tooltip("死亡演出を効かせ切るまでの秒数")]
        public float DeathPivotSeconds = 0.5f;

        [Tooltip("死亡演出を保持するために押し直す秒数（毎フレーム更新する）")]
        public float DeathHoldSeconds = 0.5f;

        bool _started;
        bool _dead;
        float _deathElapsed;

        void Start()
        {
            ResolveReferences();

            _started = true;
            Subscribe();
        }

        void OnEnable()
        {
            // 2 回目以降の有効化（タイトルから戻ったときなど）はここで購読し直す。
            // 初回は Start より先に呼ばれるので、_started で弾く。
            if (_started)
                Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();

            // 無効化中は死亡演出の監視も止める（再有効化で最初からやり直す）
            _dead = false;
            _deathElapsed = 0f;
        }

        void OnDestroy()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (Stats == null)
                return;

            if (!Stats.IsDead)
            {
                _dead = false;
                _deathElapsed = 0f;
                return;
            }

            if (!_dead)
            {
                _dead = true;
                _deathElapsed = 0f;
            }

            _deathElapsed += Time.unscaledDeltaTime;
            ApplyDeathFraming();
        }

        // --- 購読 -------------------------------------------------------

        void Subscribe()
        {
            Unsubscribe();

            if (Combat != null)
                Combat.HitLanded += OnHitLanded;

            if (Stats != null)
            {
                Stats.HitReceived += OnHitReceived;
                Stats.Staggered += OnStaggered;
            }
        }

        void Unsubscribe()
        {
            if (Combat != null)
                Combat.HitLanded -= OnHitLanded;

            if (Stats != null)
            {
                Stats.HitReceived -= OnHitReceived;
                Stats.Staggered -= OnStaggered;
            }
        }

        // --- 購読ハンドラ -----------------------------------------------

        void OnHitLanded(HitResult result, IDamageable target)
        {
            if (Rig == null)
                return;

            Rig.PushDistance(PushInScale, PushInSeconds);
        }

        void OnHitReceived(HitResult result)
        {
            if (Rig == null)
                return;

            // Large 以上の怯みは Staggered 側でより強く演出する。
            // ここで弱い引きも積むと、倍率の合成で弱い方が残ってしまう。
            if (result.Stagger >= StaggerTier.Large)
                return;

            Rig.PushDistance(HitPullScale, HitPullSeconds);
            Rig.PushPivot(HitPivotDrop, HitPivotSeconds);
        }

        void OnStaggered(HitResult result)
        {
            if (Rig == null)
                return;

            if (result.Stagger < StaggerTier.Large)
                return;

            Rig.PushDistance(StaggerPullScale, StaggerPullSeconds);
            Rig.PushPivot(StaggerPivotDrop, StaggerPivotSeconds);
        }

        // --- 死亡演出 ---------------------------------------------------

        /// <summary>
        /// 倒れた瞬間から <see cref="DeathPivotSeconds"/> かけて引き＋見下ろしへ持っていき、
        /// 戦闘終了なのでそのまま保持する（戻さない）。
        ///
        /// 演出は時間で減衰する前提なので、死んでいる間は毎フレーム押し直して保持する。
        /// 寄る要求が同時に来ても、引き側は「強い方」を採用するので押し負けない。
        /// </summary>
        void ApplyDeathFraming()
        {
            if (Rig == null)
                return;

            float k = DeathPivotSeconds > 0f
                ? Mathf.Clamp01(_deathElapsed / DeathPivotSeconds)
                : 1f;

            // 等速で動かすと機械的に見えるので smoothstep で効かせる
            float ease = k * k * (3f - 2f * k);

            // 押し直しの窓は最低 1 フレームぶん確保する（0 を渡すと「即座に戻す」の意味になる）
            float hold = Mathf.Max(DeathHoldSeconds, Time.unscaledDeltaTime);

            Rig.PushDistance(Mathf.Lerp(1f, DeathPullScale, ease), hold);
            Rig.PushPivot(Mathf.Lerp(0f, DeathPivotDrop, ease), hold);
        }

        // --- 参照の解決 -------------------------------------------------

        void ResolveReferences()
        {
            if (Combat == null)
                Combat = GetComponent<PlayerCombat>();

            if (Stats == null)
                Stats = GetComponent<PlayerStats>();

            if (Stats == null && Combat != null)
                Stats = Combat.Stats;

            if (Rig == null)
            {
                var cam = Camera.main;
                if (cam != null)
                    Rig = cam.GetComponent<ThirdPersonCamera>();
            }
        }
    }
}
