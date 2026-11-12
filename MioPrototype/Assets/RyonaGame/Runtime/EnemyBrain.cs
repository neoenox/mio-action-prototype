using System;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 敵の行動フェーズ。既存の 4 値（Chase=0 / Windup=1 / Recover=2 / Dead=3）は
    /// <see cref="MatchHud"/> などが参照しているため、数値を変えずに末尾へ追加する。
    /// </summary>
    public enum EnemyPhase
    {
        /// <summary>間合いの外から歩いて詰める。</summary>
        Chase = 0,

        /// <summary>予備動作。テレグラフを出して「来るぞ」を見せる。</summary>
        Windup = 1,

        /// <summary>攻撃後の硬直。移動も攻撃もしない。</summary>
        Recover = 2,

        /// <summary>死亡。以後なにもしない。</summary>
        Dead = 3,

        /// <summary>判定を出す瞬間。1 フレームだけ滞在する。</summary>
        Attack = 4,

        /// <summary>被弾で中断され、怯んでいる。</summary>
        Stagger = 5,

        /// <summary>間合いの内側で横に回り込む（攻撃順番待ちもここで過ごす）。</summary>
        Strafe = 6
    }

    /// <summary>
    /// 敵の頭脳。仕様書 SPEC.md §5 のフェアネス規定を守るため、
    /// <list type="bullet">
    /// <item>攻撃は必ず予備動作（<see cref="Telegraph"/> の拡大と色変化）を挟む</item>
    /// <item>予備動作中は必ず立ち止まる（歩きながら殴らない）</item>
    /// <item>同時攻撃は <see cref="EnemyGroup"/> のトークンで制限する</item>
    /// </list>
    /// を守る。移動はキャラクターコントローラを使わず素の transform 移動で行い、
    /// 床は y=0 平面、当たり判定はシーン側のカプセルコライダーが持つ。
    /// </summary>
    /// <summary>敵のパンチが狙う場所。顔を殴るか、腹（ボディーブロー）を殴るか。</summary>
    public enum PunchZone
    {
        Face = 0,
        Body = 1
    }

    [AddComponentMenu("Ryona/Enemy Brain")]
    public sealed partial class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>敵が選ぶ攻撃の種類。この 4 つを距離と乱数で抽選する。</summary>
        public enum AttackId
        {
            /// <summary>速い単発。予備動作が短く、間合いも短い。</summary>
            Jab = 0,

            /// <summary>遅いが重い。ひるみが大きい。</summary>
            Heavy = 1,

            /// <summary>崩し。予備動作は中くらい。</summary>
            Break = 2,

            /// <summary>掴み。至近距離でだけ出て、当たるとレバガチャになる。</summary>
            Grab = 3
        }

        const int AttackCount = 4;

        [Header("References")]
        [Tooltip("追いかける相手（プレイヤー）の Transform")]
        public Transform Target;

        [Tooltip("攻撃の着弾先。ダメージ解決は Player.ReceiveHit を通す")]
        public PlayerStats Player;

        [Tooltip("予備動作を知らせる球。拡大と色変化で「来るタイミング」を見せる")]
        public Transform Telegraph;

        [Tooltip("同時攻撃を調停するグループ。未設定なら Start でシーンから探す")]
        public EnemyGroup Group;

        [Header("Tuning")]
        [Tooltip("歩き（追跡）の速度 m/s")]
        public float MoveSpeed = 2.6f;

        [Tooltip("攻撃間合いの基準値。実間合いは AttackRange * 0.85 になる")]
        public float AttackRange = 1.45f;

        [Tooltip("攻撃別の予備動作が 0 以下のときのフォールバック秒数")]
        public float WindupSeconds = 0.45f;

        [Tooltip("攻撃後の硬直秒数")]
        public float RecoverSeconds = 0.70f;

        [Tooltip("予備動作まで見せてから振らずに引く確率（0 で無効）。1 回の接近につき 1 度だけ")]
        [System.NonSerialized] public float FeintChance = 0.22f;

        [Tooltip("フェイント後に再び踏み込むまでの秒数。短いほど「二度撃ち」に見える")]
        [System.NonSerialized] public float FeintDelaySeconds = 0.45f;

        [Tooltip("旋回の速さ（Slerp 係数）")]
        public float TurnSpeed = 8f;

        [Header("Attack: Jab")]
        [Tooltip("Jab の予備動作秒数")]
        public float JabSeconds = 0.35f;

        [Tooltip("Jab が届く距離 m")]
        public float JabRange = 1.45f;

        [Tooltip("Jab のダメージ")]
        [System.NonSerialized] public float JabDamage = 3f;

        [Header("Attack: Heavy")]
        [Tooltip("Heavy の予備動作秒数")]
        public float HeavySeconds = 0.75f;

        [Tooltip("Heavy が届く距離 m")]
        public float HeavyRange = 1.70f;

        [Tooltip("Heavy のダメージ。ひるみは AttackLibrary.Heavy の値を使う")]
        [System.NonSerialized] public float HeavyDamage = 7f;

        [Header("Attack: Break")]
        [Tooltip("Break の予備動作秒数")]
        public float BreakSeconds = 0.55f;

        [Tooltip("Break が届く距離 m")]
        public float BreakRange = 1.55f;

        [Tooltip("Break のダメージ")]
        [System.NonSerialized] public float BreakDamage = 5f;

        [Header("Attack: Grab")]
        [Tooltip("掴みの予備動作秒数。長めにして「来る」と分かるようにする")]
        public float GrabSeconds = 0.55f;

        [Tooltip("掴みが届く距離 m。至近距離でだけ出る")]
        public float GrabRange = 1.15f;

        [Tooltip("掴み自体のダメージ。本命は掴んだ後のレバガチャ")]
        public float GrabDamage = 3f;


        [Tooltip("着弾判定の余裕 m。見た目と当たり判定のズレを吸収する")]
        public float StrikeTolerance = 0.35f;

        /// <summary>
        /// 彼女の体との最小距離。これより近づいたら敵の側が下がる。0 以下なら押し出さない。
        /// 彼女は敵をすり抜けられるので、既定では 0（押し出さない）。
        /// </summary>
        [System.NonSerialized] public float MinPlayerGap = 0f;

        [Header("Strafe")]
        [Tooltip("横回りを続ける秒数の基準値。実際は 0.75〜1.25 倍でばらつく")]
        public float StrafeSeconds = 1.10f;

        [Tooltip("横回り中の移動速度倍率")]
        public float StrafeSpeedScale = 0.75f;

        [Tooltip("横回りに入るたび、間合いを詰め直す確率")]
        [Range(0f, 1f)]
        public float StrafeCloseInChance = 0.35f;

        [Tooltip("間合いを詰め直すときの距離倍率（1 未満で近づく）")]
        public float StrafeCloseInScale = 0.85f;

        [Tooltip("順番待ちの敵が立つ距離（間合いの何倍か）。全員が同じ距離まで詰めると画面が敵で埋まる")]
        public float WaitingRingScale = 2.0f;

        [Header("Timing")]
        [Tooltip("硬直・怯みが明けてから次の予備動作に入るまでの間隔")]
        [System.NonSerialized]
        public float AttackCooldown = 1.4f;

        [Tooltip("怯み 小 の秒数（0 以下なら CombatConstants.StunSmall）")]
        public float StaggerSecondsSmall = 0.20f;

        [Tooltip("怯み 中 の秒数（0 以下なら CombatConstants.StunMedium）")]
        public float StaggerSecondsMedium = 0.40f;

        [Tooltip("怯み 大 の秒数（0 以下なら CombatConstants.StunLarge）")]
        public float StaggerSecondsLarge = 0.80f;

        [Header("Telegraph")]
        [Tooltip("予備動作開始時の球の倍率（元の localScale に対する倍率）")]
        public float TelegraphStartScale = 0.35f;

        [Tooltip("予備動作完了時の球の倍率（元の localScale に対する倍率）")]
        public float TelegraphMaxScale = 3.5f;

        [Tooltip("予備動作の最後、この秒数のあいだ球を最大サイズで固定する")]
        public float TelegraphMaxHoldSeconds = 0.10f;

        [Tooltip("予備動作開始時の球の色")]
        public Color TelegraphStartColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        [Tooltip("予備動作完了時の球の色。ここまで変化すれば「来る」と分かる")]
        public Color TelegraphColor = new Color(1f, 0.28f, 0.18f, 1f);

        /// <summary>HP / 部位ダメージ / ひるみを持つ戦闘状態。攻撃側としても渡す。</summary>
        public CombatState State { get; } = new CombatState();

        /// <summary>この個体の最大 HP。3 体 × 100 では倒しきれないため、雑魚は低めに。</summary>
        [System.NonSerialized]
        public float MaxHp = 48f;

        /// <summary>現在の行動フェーズ。</summary>
        public EnemyPhase Phase { get; private set; } = EnemyPhase.Chase;

        /// <summary>生きているか。</summary>
        public bool IsAlive => State.IsAlive;

        /// <summary>最後に選んだ攻撃の種類。抽選結果の確認用。</summary>
        public AttackId CurrentAttack => _currentAttack;

        /// <summary>
        /// 掴みが決まって、相手がもがき終わるまで掴んだまま硬直している。
        /// 見た目側はこの間ずっと腕を前に出したまま（掴んだ形）にする。
        /// </summary>
        public bool IsHolding { get; private set; }

        /// <summary>
        /// 予備動作の進み具合（0 = 構え始め、1 = 殴る直前）。
        /// 見た目側が「拳を引く」ポーズを作るのに使う。判定には影響しない。
        /// </summary>
        public float WindupProgress { get; private set; }

        /// <summary>よろけの残り割合。1 = 殴られた瞬間、0 = 立て直し完了。姿勢の戻りに使う。</summary>
        public float StaggerProgress { get; private set; }

        /// <summary>よろけの重さ（1 = 小、1.4 = 中、1.8 = 大）。揺れ幅を tier で変える。</summary>
        public float StaggerWeight { get; private set; } = 1f;

        /// <summary>よろけが始まってからの経過秒。頭と腕を体より遅らせるのに使う。</summary>
        public float StaggerElapsed { get; private set; }

        /// <summary>怯んだ一撃が当たった部位。頭なら首、胴なら上体を優先して反応させる。</summary>
        public BodyPart StaggerPart { get; private set; } = BodyPart.Torso;

        float _staggerTotal;

        /// <summary>攻撃を始めてよい距離（<see cref="AttackRange"/> の 85%）。</summary>
        public float EngageDistance => Mathf.Clamp(AttackRange * 0.85f, 0.1f, 1.03f);

        /// <summary>
        /// 拳が実際に届く上限（m）。実測で Jab 1.25 / Heavy 1.45 / Break 1.25 まで。
        /// これより遠いと拳が狙いから外れて空振りするので、その距離では攻撃を始めない。
        /// </summary>
        const float MaxJabReach = 1.25f;
        const float MaxHeavyReach = 1.45f;
        const float MaxBreakReach = 1.25f;

        /// <summary>攻撃トークンを保持しているか。<see cref="Group"/> が無ければ常に false。</summary>
        public bool HasAttackToken => Group != null && Group.HasAttackToken(this);

        /// <summary>
        /// タッグ腹パン用に攻撃トークンを先に確保する。位置合わせの間に
        /// 他の敵に取られて「歩いたのに打てない」になるのを防ぐ。
        /// </summary>
        public bool TryReserveTagPunchToken() =>
            Group == null || Group.TryAcquireAttackToken(this);

        /// <summary>次の予備動作まで残っている秒数（0 なら攻撃可能）。</summary>
        public float AttackCooldownRemaining => Mathf.Max(0f, _attackReadyAt - Time.time);

        /// <summary>被弾したときに発火する。<see cref="EnemyReaction"/> が購読している。</summary>
        public event Action<HitResult> Hit;

        /// <summary>倒れたときに発火する（決着の演出・再出現が購読する）。</summary>
        public event Action<EnemyBrain> Died;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary><c>-nofeint</c> の通知を 1 回だけ出すためのフラグ（敵 3 体が同じ引数を読む）。</summary>
        static bool _feintDisabledLogged;

        // --- 攻撃テーブル（AttackLibrary を元にダメージだけ差し替えた実体） ---------
        AttackData _jab;
        AttackData _heavy;
        AttackData _break;
        AttackData _grab;

        // 抽選バッファ（毎フレームの GC を避けるため使い回す）
        readonly AttackId[] _candidateIds = new AttackId[AttackCount];
        readonly float[] _candidateWeights = new float[AttackCount];

        // --- 実行時状態 -----------------------------------------------------------
        /// <summary>予備動作〜攻撃で使う 1 回分のパラメータ。</summary>
        struct EnemyAttackRunner
        {
            public AttackId Id;
            public AttackData Data;
            public float Range;
            public float Windup;
            public bool FromHeldThrow;
        }

        EnemyAttackRunner _run;

        /// <summary>
        /// 今回のパンチの狙い（顔 / 腹）。予備動作に入るとき決める。
        /// 拳はこの場所の座標へ向けて振られ、届いた距離で当たり判定を取る。
        /// </summary>
        public PunchZone Zone { get; private set; } = PunchZone.Face;

        [Header("Punch Zone")]
        [Tooltip("素早い一撃（Jab）が顔を狙う確率。残りは腹")]
        [Range(0f, 1f)] public float JabFaceChance = 0.65f;

        [Tooltip("重い一撃（Heavy）が顔を狙う確率。残りは腹")]
        [Range(0f, 1f)] public float HeavyFaceChance = 0.35f;

        /// <summary>検証用。設定すると、狙いを抽選せずこの場所に固定する。</summary>
        [System.NonSerialized] public PunchZone? ForceZone;

        [Tooltip("拳が狙いの座標にこの距離まで届いたら当たり（m）")]
        public float PunchHitRadius = 0.30f;

        /// <summary>プレイヤーの顔・腹の中心（ワールド座標）。拳の狙いと当たり判定が共有する。</summary>
        public static Vector3 PunchTarget(PlayerStats player, PunchZone zone, float bodyBias = 0.40f)
        {
            if (player == null)
                return Vector3.zero;

            var rig = player.GetComponentInChildren<ProceduralRig>();
            if (rig != null && rig.Ready && rig.Head != null && rig.Hips != null && rig.Chest != null)
            {
                // 顔は頭の中心、腹は腰と胸のあいだ（へその少し上）
                return zone == PunchZone.Face
                    ? rig.Head.position
                    : Vector3.Lerp(rig.Hips.position, rig.Chest.position, Mathf.Clamp01(bodyBias));
            }

            return player.transform.position + Vector3.up * (zone == PunchZone.Face ? 1.35f : 0.95f);
        }

        /// <summary>現在フェーズの残り秒数（予備動作・硬直・怯みで共用）。</summary>
        float _timer;
        float _punchVisualTimer;

        /// <summary>腹パンが当たった瞬間から、拳を腹に押し込んでおく残り秒。</summary>
        float _punchPoseHoldSeconds;

        /// <summary>
        /// タッグ腹パンの発注時に受け取った保持秒。
        /// 発注時ではなく<b>当たった瞬間</b>に <see cref="_punchPoseHoldSeconds"/> へ移す
        /// （発注時から伸ばすと、振りかぶる前から拳が腹に着いて見える）。
        /// </summary>
        float _bellyPunchPendingHold;

        /// <summary>
        /// タッグ技（腹 / 顔）の発注から当たるまでの間。振りかぶり中の拳の伸ばし方に使う。
        /// 近距離から当てるので、通常のジャブより早く伸ばしきらないと空振りする。
        /// </summary>
        bool _tagPunchPending;
        EnemyBrain _heldThrowCaptor;
        float _heldThrowPullUntil;

        /// <summary>投げで引き込んで、彼女を腹パン体勢へ崩すまでの秒数。</summary>
        const float HeldThrowPullSeconds = 0.30f;
        const float HeldThrowReach = 0.62f;
        const float HeldThrowContactTolerance = 0.12f;
        const float GrabReach = 0.72f;
        const float GrabContactRange = 0.88f;

        internal bool IsPreparingHeldThrow => !ReferenceEquals(_heldThrowCaptor, null);
        internal bool IsPreparingGrab => _run.Id == AttackId.Grab &&
            (Phase == EnemyPhase.Windup || Phase == EnemyPhase.Attack);
        internal bool IsPreparingFrontGrab => IsPreparingHeldThrow || _pinPending || _bearHugPending;
        internal float GrabAdvance { get; private set; }
        internal float HeldThrowPull => Mathf.Clamp01((_heldThrowPullUntil - Time.time) / HeldThrowPullSeconds);

        /// <summary>
        /// この一振りがタッグ技（腹 / 顔）か。予備動作から振り抜きまで true のままにする。
        /// 見た目側が「片手で打つ」姿勢（反対の腕を腰の横へ引く）を組むのに使う。
        /// <see cref="_tagPunchPending"/> は当たった瞬間に落とすので、振り抜きの間はこちらを見る。
        /// </summary>
        bool _tagPunchSwing;

        /// <summary>拳と狙いの距離をログる（`-hitlog`）。</summary>
        bool _hitLog;

        /// <summary>通常の Body 狙い（腰と胸のあいだ）。</summary>
        const float DefaultBodyBias = 0.40f;

        /// <summary>腹パンだけは低め（へそ寄り）を狙う。</summary>
        const float BellyPunchBodyBias = 0.34f;

        /// <summary>
        /// Body 狙いのとき、腰から胸のどこを狙うか（0=腰, 1=胸）。
        /// 腹パンのあいだだけ低くし、終わったら通常へ戻す。
        /// フィールドにすると 0.28 がその敵に残り、以後の腹ジャブまで低いままになる。
        /// </summary>
        public float BodyTargetBias =>
            _punchPoseHoldSeconds > 0f || _bellyPunchPendingHold > 0f
                ? BellyPunchBodyBias
                : DefaultBodyBias;

        /// <summary>タッグ腹パンの最中（発注から拳を押し込んでいる間）。見た目側がしゃがみ込むのに使う。</summary>
        public bool IsBellyPunch => _bellyPunchPendingHold > 0f || _punchPoseHoldSeconds > 0f;
        internal float BellyPunchDrive => Phase == EnemyPhase.Windup
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.86f, WindupProgress))
            : 1f;
        internal bool HoldsBellyPunchGuard { get; set; }
        internal bool KeepsBellyPunchArm => HoldsBellyPunchGuard && !IsTagFacePunch
            && (IsBellyPunch || Phase == EnemyPhase.Recover || Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe);

        /// <summary>
        /// この一振りがタッグ技（腹 / 顔）か。見た目側が片手打ちの姿勢を組むのに使う。
        /// 腹と顔が混ざるコンボでも腕の形が入れ替わらないよう、振り抜きまで true を保つ。
        /// </summary>
        public bool IsTagPunch => _tagPunchSwing;

        /// <summary>テスト用: タッグパンチの構えを直接立てる（反射を使わないため）。</summary>
        internal void SetTagPunchSwingForTest(bool value) => _tagPunchSwing = value;
        internal bool IsTagFacePunch => _tagPunchSwing && Zone == PunchZone.Face;
        public bool UsesLeftPunch => IsTagFacePunch ? _tagFaceLeft
            : (IsTagPunch || KeepsBellyPunchArm) && Player != null && Player.transform.forward.x < -0.1f;
        internal Vector3 TagFaceOutside => transform.right * (UsesLeftPunch ? -1f : 1f);
        bool _tagFaceLeft;

        /// <summary>この顔面パンチの強さ（1 = 普通、1.25 = 決めの一発）。体の踏み込みに掛ける。</summary>
        internal float TagFacePower => _tagFacePower;
        float _tagFacePower = 1f;
        internal bool IsOpeningBellyPunch => _run.FromHeldThrow && _tagPunchSwing;

        public float PunchPoseWeight
        {
            get
            {
                if (_currentAttack == AttackId.Grab) return 0f;

                // 腹に当たった拳は反動の初動だけ追い、彼女が戻り始める前に引く。
                if (_punchPoseHoldSeconds > 0f &&
                    (Phase == EnemyPhase.Attack || Phase == EnemyPhase.Recover))
                    return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_punchPoseHoldSeconds / 0.11f));

                if (Phase == EnemyPhase.Windup)
                {
                    if (IsTagFacePunch)
                    {
                        // 拳は「溜め」から一気に振り抜く。当たる瞬間まで速度を落とさないよう、
                        // 予備動作の終わり（1.0）まで等速で伸ばす（SmoothStep で止めると、
                        // 当たる直前に減速して「やる気のないパンチ」になる）。
                        return Mathf.Clamp01((WindupProgress - 0.60f) / 0.395f);
                    }
                    if (IsBellyPunch)
                        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.34f, WindupProgress));

                    // 当たりは「前フレームに描いた拳」で取る。通常の 0.7 から伸ばす ramp だと、
                    // 間合いの端（1.19m）で拳が伸びきる前に判定が来て空振りする。
                    // タッグ技（腹 / 顔）だけ早めに伸ばしきって、当てにいく。
                    bool tag = _tagPunchPending;
                    float start = tag ? 0.30f : 0.70f;
                    float span = tag ? 0.45f : 0.30f;
                    return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WindupProgress - start) / span));
                }
                if (Phase == EnemyPhase.Attack) return 1f;
                if (Phase == EnemyPhase.Recover)
                {
                    // 顔面パンチは当たった拳を一瞬（0.06 秒）押し込んでから引く。
                    // 即座に戻すと「触れただけ」に見える。
                    if (IsTagFacePunch)
                    {
                        float elapsed = 0.18f - _punchVisualTimer;
                        if (elapsed <= 0.06f)
                            return 1f;
                        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 0.06f) / 0.12f));
                    }

                    float weight = Mathf.Clamp01(_punchVisualTimer / 0.18f);
                    return weight;
                }
                return 0f;
            }
        }

        /// <summary>
        /// タッグ顔面パンチの「溜め」。腰と胸を拳側へひねって体重を後ろへ預ける量（0〜1）。
        /// 振り抜き始めると 0 へ戻る。
        /// </summary>
        internal float TagFaceCoil
        {
            get
            {
                if (!IsTagFacePunch || Phase != EnemyPhase.Windup)
                    return 0f;

                float wind = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.48f, WindupProgress));
                float release = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.78f, 0.985f, WindupProgress));
                return wind * release;
            }
        }

        /// <summary>
        /// タッグ顔面パンチの踏み込み（0 = 構え、1 = 振り切ったところ）。
        /// 腰→胸→肩の順に回して拳を送り、当たったあとも少し残して振り抜きを見せる。
        /// </summary>
        internal float TagFaceDrive
        {
            get
            {
                if (!IsTagFacePunch)
                    return 0f;

                if (Phase == EnemyPhase.Windup)
                    return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.985f, WindupProgress));
                if (Phase == EnemyPhase.Attack)
                    return 1f;
                if (Phase == EnemyPhase.Recover)
                {
                    float elapsed = 0.18f - _punchVisualTimer;
                    return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.30f, elapsed));
                }
                return 0f;
            }
        }

        /// <summary>今回の接近でフェイントを使ったか（連続フェイントを防ぐ）。</summary>
        bool _feinted;

        /// <summary>立ち上がりにかける秒数。シーンに保存された値で上書きされないよう NonSerialized。</summary>
        [System.NonSerialized] public float RiseSeconds = 1.6f;

        float _riseTimer;

        /// <summary>
        /// 立ち上がりの進み具合（0 = 倒れたまま、1 = 立ちきった）。
        /// 立ち上がっていないときは 0。見た目側が起き上がりの姿勢を作るのに使う。
        /// </summary>
        public float RiseProgress =>
            RiseSeconds <= 0f || _riseTimer <= 0f ? 0f : 1f - Mathf.Clamp01(_riseTimer / RiseSeconds);

        /// <summary>立ち上がりの最中か（この間は動かない・殴らない）。</summary>
        public bool IsRising => _riseTimer > 0f;

        /// <summary>この時刻を過ぎたら次の予備動作に入れる。</summary>
        float _attackReadyAt;
        float _grabReadyAt;
        float _ringJitter = 1f;

        /// <summary>デバッグ用: 次の攻撃を掴みに固定する（-grappletest）。</summary>
        bool _forceGrab;

        // --- 押さえつけ（GrappleKind.Pin） ------------------------------------
        [Header("押さえつけ（Pin）")]
        [Tooltip("正面から押さえつける（巨躯用）。既定は false。テストは -pintest")]
        public bool CanPin;

        // 間合い・クールダウン・膝蹴りの回数は CombatConstants に置いてある。
        // ここを public フィールドにすると、シーン（Prototype.unity）に保存された
        // 古い既定値が読み込まれてコードの値を上書きしてしまう（実測でそうなった）。

        /// <summary>次の予備動作を押さえつけにする（デバッグの指示用）。</summary>
        bool _pinOrder;

        /// <summary>次の予備動作をベアハッグにする（デバッグの指示用）。</summary>
        bool _bearHugOrder;

        /// <summary>この一振りが押さえつけか。予備動作で決めて、当たった瞬間に使う。</summary>
        bool _pinPending;

        /// <summary>この一振りがベアハッグか。</summary>
        bool _bearHugPending;

        /// <summary>いまベアハッグで締め付けているか。見た目と締め付けに使う。</summary>
        public bool IsBearHugging { get; private set; }

        /// <summary>次の締め付けまでの時刻。</summary>
        float _squeezeNextAt;

        /// <summary>引き寄せ中で、まだ密着（ダメージ）していないか。</summary>
        bool _squeezeActive;

        /// <summary>前回の押さえつけが始まった時刻。</summary>
        float _lastPinAt = -999f;

        /// <summary>この押さえつけで残っている膝蹴りの回数。</summary>
        int _pinKneesLeft;

        /// <summary>次の膝蹴りを入れる時刻（0 = 予定なし）。</summary>
        float _pinNextKneeAt;

        // --- 壁・二体がかり（Y 字）のアシスト役 -------------------------
        PlayerGrapple _wallAssistHold;
        float _wallAssistSide = 1f;
        bool _wallAssistReady;
        float _wallSplitLogAt;

        /// <summary>二体目として壁押さえつけに加わっているか。</summary>
        public bool IsWallAssisting => _wallAssistHold != null;

        /// <summary>横の持ち場に着いたか（PlayerGrapple が Y 字へ移る合図）。</summary>
        internal bool WallAssistReady => _wallAssistReady;

        internal const float PushContactSeconds = 0.13f;
        internal const float PushDuration = 0.36f;
        float _pushUntil;

        public void PushAway() => _pushUntil = Time.time + PushDuration;

        internal void EndPush() => _pushUntil = 0f;

        internal float PushElapsed => PushDuration - (_pushUntil - Time.time);

        /// <summary>引き渡しが終わるまで、渡す側をその場に固定する残り時刻。</summary>
        float _handOffLockUntil;

        /// <summary>引き渡しのロック中か（歩かない・殴らない）。</summary>
        public bool IsHandOffLocked => Time.time < _handOffLockUntil;

        /// <summary>
        /// 引き渡しの間、渡す側をその場に固定する。腕を突き出したまま硬直（Recover）にし、
        /// 彼女が受け手の拘束位置へ移り終えるまで追いかけさせない。
        /// </summary>
        public void LockForHandOff(float seconds, bool releaseToken = true)
        {
            float length = Mathf.Max(0.1f, seconds);

            AbsorbLungeOffset();
            HideTelegraph();
            IsHolding = false;
            ClearGrappleState(clearAttackState: true);
            if (releaseToken)
                ReleaseToken();

            _timer = length;
            Phase = EnemyPhase.Recover;
            _handOffLockUntil = Time.time + length;
            _attackReadyAt = Time.time + length + 0.2f;
        }

        /// <summary>いま押し出している最中か（見た目側が腕を突き出すのに使う）。</summary>
        public bool IsPushing => Time.time < _pushUntil;

        /// <summary>受け止める構えの残り秒。</summary>
        float _catchUntil;
        float _catchStartedAt;

        /// <summary>引き渡しで彼女を受け止める瞬間、腕を開いて構える。</summary>
        public void BeginCatch(float seconds)
        {
            _catchStartedAt = Time.time;
            _catchUntil = Time.time + Mathf.Max(0.1f, seconds);
        }

        public void EndCatch() => _catchUntil = 0f;

        /// <summary>いま受け止める構えの最中か（見た目側が腕を開くのに使う）。</summary>
        public bool IsCatching => Time.time < _catchUntil;

        internal float CatchImpact
        {
            get
            {
                if (!IsCatching)
                    return 0f;

                float time = Time.time - _catchStartedAt;
                float compression = Mathf.Lerp(0.65f, 1f, Mathf.InverseLerp(0f, 0.035f, time));
                float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.035f, 0.20f, time));
                return compression * (1f - settle);
            }
        }

        /// <summary>押さえつけ中の相手か（掴んでいる本人）。見た目側が姿勢を組むのに使う。</summary>
        public bool IsPinning { get; private set; }

        /// <summary>壁へ押し出して押さえつけている最中か。</summary>
        public bool IsWallPinning { get; private set; }

        // 壁押さえつけの押し出し（両者を 0.18 秒で壁際へ移す）。
        Vector3 _wallFromPos;
        Vector3 _wallToPos;
        Quaternion _wallFromRot;
        Quaternion _wallToRot;

        // 壁からレーンへ戻る（掴みが解けたあと）。
        const float WallReturnSeconds = 0.25f;
        float _wallReturn;
        float _wallReturnX;
        float _wallReturnZ;

        /// <summary>掴まれ状態を見るためだけに持つプレイヤー側の参照。</summary>
        PlayerGrapple _grapple;

        /// <summary>次の攻撃を掴みにする。デバッグの駆動用。</summary>
        public void QueueGrab()
        {
            _forceGrab = true;
            _attackReadyAt = Time.time;
        }

        /// <summary>次の掴みを押さえつけ（Pin）にする。デバッグの駆動用（-pintest）。</summary>
        public void QueuePin()
        {
            _pinOrder = true;
            _bearHugOrder = false;
            _lastPinAt = -999f;   // 指示されたときはクールダウンを無視する
            _attackReadyAt = Time.time;
        }

        /// <summary>次の掴みをベアハッグにする。デバッグとテストの駆動用。</summary>
        public void QueueBearHug()
        {
            _bearHugOrder = true;
            _pinOrder = false;
            _lastPinAt = -999f;
            _attackReadyAt = Time.time;
        }

        /// <summary>
        /// しばらく次の攻撃を始めない。掴みに失敗した直後、他の敵が一斉に
        /// 追撃すると 1 回の失敗で HP が 3 割飛ぶため、一拍だけ様子を見させる。
        /// </summary>
        public void HoldFire(float seconds)
        {
            _attackReadyAt = Mathf.Max(_attackReadyAt, Time.time + Mathf.Max(0f, seconds));
            if (seconds > 0f && (Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe))
                ReleaseToken();
        }

        internal bool CanMountDownedPlayer => isActiveAndEnabled && IsAlive && !IsRising &&
            !IsHolding && !IsHandOffLocked && !IsFlanking && Time.time >= _attackReadyAt &&
            (Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe);

        float _flankOffset;
        float _blockedFlankSeconds;
        Collider _bodyCollider;
        readonly System.Collections.Generic.List<Collider> _flankIgnored = new System.Collections.Generic.List<Collider>();
        public bool IsFlanking => _flankOffset != 0f;

        void StopFlanking()
        {
            if (!IsFlanking) return;
            _flankOffset = 0f;
            if (_bodyCollider != null)
                foreach (var other in _flankIgnored)
                    if (other != null) Physics.IgnoreCollision(_bodyCollider, other, false);
            _flankIgnored.Clear();

            // 回り込みが終わっても、彼女とは通り抜けられるまま
            IgnorePlayerCollision();
        }

        /// <summary>彼女の体（CharacterController）とこの敵の当たりを無視して、すり抜けられるようにする。</summary>
        void IgnorePlayerCollision()
        {
            if (Player == null)
                return;

            var own = GetComponent<Collider>();
            var player = Player.GetComponent<Collider>();
            if (own != null && player != null)
                Physics.IgnoreCollision(own, player, true);
        }

        bool TryFlank(float dt)
        {
            if (Group == null || Target == null || Player == null || Player.IsDead)
                return false;
            if (!IsFlanking)
            {
                float own = transform.position.x - Target.position.x;
                if (Mathf.Abs(own) < 1f || HasAttackToken || AttackCooldownRemaining > 0f)
                {
                    _blockedFlankSeconds = 0f;
                    return false;
                }

                float side = Mathf.Sign(own);
                EnemyBrain lead = null, candidate = null;
                float leadDistance = float.PositiveInfinity, candidateDistance = float.PositiveInfinity;
                bool oppositeOccupied = false;
                foreach (var other in Group.Members)
                {
                    if (other == null || !other.IsAlive) continue;
                    if (other != this && other.IsFlanking) return false;
                    float offset = (other.transform.position.x - Target.position.x) * side;
                    if (offset < 0f && offset >= -2.2f)
                        oppositeOccupied = true;
                    if (offset > 0f && offset < leadDistance)
                    {
                        lead = other;
                        leadDistance = offset;
                    }
                }
                if (lead == null || lead == this || leadDistance > 2.2f)
                {
                    _blockedFlankSeconds = 0f;
                    return false;
                }
                foreach (var other in Group.Members)
                {
                    if (other == null || other == lead || !other.IsAlive || other.HasAttackToken
                        || other.IsRising || other.AttackCooldownRemaining > 0f
                        || (other.Phase != EnemyPhase.Chase && other.Phase != EnemyPhase.Strafe))
                        continue;
                    float offset = (other.transform.position.x - Target.position.x) * side;
                    if (offset > leadDistance + 0.1f && offset < candidateDistance)
                    {
                        candidate = other;
                        candidateDistance = offset;
                    }
                }
                if (candidate != this) return false;

                // 両側が埋まり、しばらく進めないときだけ同じ側の空きに入る。
                _blockedFlankSeconds = oppositeOccupied ? _blockedFlankSeconds + dt : 0f;
                float engage = Mathf.Max(MinPlayerGap, EngageDistance * 0.92f);
                if (oppositeOccupied && (_blockedFlankSeconds < 1.25f || lead.IsHolding
                    || lead.AttackCooldownRemaining < 0.3f
                    || leadDistance <= engage + 0.05f || !Group.HasFreeSlot))
                    return false;

                _blockedFlankSeconds = 0f;
                _flankOffset = oppositeOccupied ? side * engage : -side * 1.5f;
                _bodyCollider = GetComponent<Collider>();
                if (_bodyCollider != null)
                {
                    var playerCollider = Player.GetComponent<Collider>();
                    if (playerCollider != null)
                    {
                        Physics.IgnoreCollision(_bodyCollider, playerCollider, true);
                        _flankIgnored.Add(playerCollider);
                    }
                    foreach (var other in Group.Members)
                    {
                        if (other == null || other == this) continue;
                        var otherCollider = other.GetComponent<Collider>();
                        if (otherCollider == null) continue;
                        Physics.IgnoreCollision(_bodyCollider, otherCollider, true);
                        _flankIgnored.Add(otherCollider);
                    }
                }
                ReleaseToken();
            }
            float destination = Target.position.x + _flankOffset;
            Vector3 position = transform.position;
            position.x = Mathf.MoveTowards(position.x, destination, MoveSpeed * dt);
            transform.position = position;
            if (Mathf.Abs(position.x - destination) < 0.05f)
                StopFlanking();
            return true;
        }

        /// <summary>この敵専用の 3D 音源。最初に鳴らすときに作る。</summary>
        AudioSource _voice;

        /// <summary>今回の横回りで間合いを詰め直すか。</summary>
        bool _strafeCloseIn;

        AttackId _currentAttack;
        AttackId _lastAttack;
        int _repeatCount;
        bool _hasLast;

        Renderer _telegraphRenderer;
        Material _telegraphMaterial;
        Vector3 _baseTelegraphScale = Vector3.one;
        bool _registered;

        void Awake()
        {
            BuildAttacks();

            // 個体ごとの最大 HP。3 体 × 100 では彼女の火力（軽 8）で倒しきれず、
            // 殴られ続けて終わってしまうため、雑魚は低めにする。
            State.SetMaxHp(MaxHp);

            // 検証用: -punchzone=face / body で、パンチの狙いを固定する
            string zone = LaunchOptions.Value("-punchzone");
            if (string.Equals(zone, "face", System.StringComparison.OrdinalIgnoreCase))
                ForceZone = PunchZone.Face;
            else if (string.Equals(zone, "body", System.StringComparison.OrdinalIgnoreCase))
                ForceZone = PunchZone.Body;

            _hitLog = LaunchOptions.Has("-hitlog");
            _pinLog = LaunchOptions.Has("-pinlog");

            if (LaunchOptions.Has("-nofeint"))
            {
                // 掴みもフェイント対象なので、22% で「掴みの予備動作だけ見せて引く」。
                // 数秒の検証では掴めたのかフェイントしたのか区別できないため、切れるようにする。
                FeintChance = 0f;
                if (!_feintDisabledLogged)
                {
                    _feintDisabledLogged = true;
                    Debug.Log("[Enemy] フェイント無効（-nofeint）");
                }
            }

            // 順番待ちの輪を少しずつずらす。全員が同じ半径に並ぶと機械的に見える。
            _ringJitter = UnityEngine.Random.Range(0.92f, 1.12f);

            if (Telegraph != null)
            {
                // 頭上に浮く球は、武器（鉄パイプ）を振りかぶる姿勢と二重になって
                // 「宙に赤い玉が浮いている」ようにしか見えないので廃止した。
                // 予備動作は「パイプが光る」で示す（EnemyFigure.ApplyWeaponCharge）。
                Destroy(Telegraph.gameObject);
                Telegraph = null;
                Debug.Log("[Ryona] 頭上に浮く予告球を廃止しました（予備動作はパイプの発光で示します）");
            }
        }

        void Start()
        {
            if (Group == null)
                Group = FindFirstObjectByType<EnemyGroup>();

            RegisterGroup();
            IgnorePlayerCollision();
        }

        void OnEnable()
        {
            // 無効化 → 有効化では Start が走り直さないので、ここでも登録を試みる。
            // 死亡済みの個体はグループに戻さない。
            if (Phase == EnemyPhase.Dead)
                return;

            if (Group == null)
                Group = FindFirstObjectByType<EnemyGroup>();

            RegisterGroup();
        }

        void OnDisable()
        {
            if (IsPreparingHeldThrow)
                CancelWindup();
            StopFlanking();
            ReleaseToken();
            UnregisterGroup();
            HideTelegraph();
        }

        void OnDestroy()
        {
            // OnDisable と重複するが、破棄順によって取りこぼさないよう明示的にも掃除する
            StopFlanking();
            ReleaseToken();
            UnregisterGroup();
            HideTelegraph();
        }

        void Update()
        {
            if (Phase == EnemyPhase.Dead)
                return;

            float dt = Time.deltaTime;
            _punchPoseHoldSeconds = Mathf.Max(0f, _punchPoseHoldSeconds - dt);

            State.Tick(dt);

            if (!State.IsAlive)
            {
                Die();
                return;
            }

            // 立ち上がりの間は動かない・殴らない。
            // 見た目側（EnemyFigure）が起き上がりの姿勢を作るので、
            // ここで動かすと足が滑って見える。
            if (_riseTimer > 0f)
            {
                _riseTimer -= dt;

                if (Target != null)
                {
                    Vector3 toPlayer = Target.position - transform.position;
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude > 0.0001f)
                        Face(toPlayer.normalized, dt);
                }

                return;
            }

            if (Target == null || Player == null)
                return;

            if (Player.GroundMount.Attacker == this)
                return;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            // 壁の二体がかりで、自分がアシスト役なら通常 AI を止めて持ち場へ。
            if (_wallAssistHold != null)
            {
                if (Phase == EnemyPhase.Windup || Phase == EnemyPhase.Attack)
                    CancelWindup();
                UpdateWallAssist(dt);
                return;
            }

            // 二体がかり中は、拘束役も正面から彼女の左側（+X）へ回り込む。
            // 正面に残ると、正面のカメラが彼の背中になる。
            if (IsPinning && _grapple != null && _grapple.IsWallSplitActive)
            {
                UpdateWallSplitCaptor(dt, _grapple);
                return;
            }

            bool waitingForWallPin = _grapple != null && _grapple.IsWallPin && _grapple.Captor != this;
            if (waitingForWallPin)
            {
                if (Phase == EnemyPhase.Windup || Phase == EnemyPhase.Attack)
                    CancelWindup();

                if (Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe)
                {
                    StopFlanking();
                    MoveAsideForWallPin();
                    return;
                }
            }

            if ((Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe) && !IsFlanking && TryApproachForPin())
                return;
            if ((Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe) && TryFlank(dt))
                return;
            if (Phase != EnemyPhase.Chase && Phase != EnemyPhase.Strafe)
            {
                _blockedFlankSeconds = 0f;
                StopFlanking();
            }

            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // 予備動作・判定・怯みのあいだは向きを固定する。
            // タメが読めることと、判定と見た目がズレないことを優先する。
            if (distance > 0.001f && CanTurn())
                Face(toTarget / distance, dt);

            // 倒れた相手には攻撃を出さない（フェアネス規定）。
            bool playerDown = Player.IsDead || Player.IsDown;

            switch (Phase)
            {
                case EnemyPhase.Chase:
                    if (!playerDown)
                        TickChase(toTarget, distance);
                    break;

                case EnemyPhase.Strafe:
                    if (!playerDown)
                        TickStrafe(dt, toTarget, distance);
                    break;

                case EnemyPhase.Windup:
                    if (playerDown || IsRedundantGrab())
                        CancelWindup();
                    else
                        TickWindup(dt);
                    break;

                case EnemyPhase.Attack:
                    Strike(distance);
                    break;

                case EnemyPhase.Recover:
                    TickHeldRecover(dt);
                    break;

                case EnemyPhase.Stagger:
                    TickStagger(dt);
                    break;
            }

            if (!waitingForWallPin)
                KeepSpacing();
        }

        /// <summary>
        /// 攻撃後の硬直。掴んだままの相手への追い打ち（膝蹴り・ベアハッグの締め）と、
        /// 拘束が解けていた場合の後始末を進める。
        /// </summary>
        void TickHeldRecover(float dt)
        {
            _punchVisualTimer = Mathf.Max(0f, _punchVisualTimer - dt);
            var frontHold = IsPinning || IsBearHugging ? Player.GetComponent<PlayerGrapple>() : null;
            bool canHitHeld = frontHold != null && frontHold.IsGrappled &&
                !frontHold.IsHandingOff && !frontHold.IsWallTransition && frontHold.Captor == this && frontHold.FrontEscapeRemaining <= 0f;

            // 押さえつけ中の膝蹴り（5 発）。壁で二体目が居るときは膝を入れず、
            // 二人がかり（Y 字）へ切り替える。
            if (canHitHeld && IsPinning && _pinKneesLeft > 0 && Time.time >= _pinNextKneeAt)
            {
                if (IsWallPinning && _pinKneesLeft == CombatConstants.PinKneeCount &&
                    frontHold != null && frontHold.TryBeginWallSplit(this))
                {
                    _pinKneesLeft = 0;
                }
                else
                {
                    _pinKneesLeft--;
                    _pinNextKneeAt = Time.time + Mathf.Max(0.1f, CombatConstants.PinKneeInterval);
                    DoPinKnee();
                }
            }

            // ベアハッグの締め付け。体を引き寄せ、密着した瞬間に HP を削る。
            if (canHitHeld && IsBearHugging && !_squeezeActive && Time.time >= _squeezeNextAt)
            {
                if (frontHold.Kind == GrappleKind.BearHug)
                {
                    _squeezeActive = true;
                    frontHold.Squeeze();
                }
            }
            if (canHitHeld && IsBearHugging && _squeezeActive && frontHold.IsBearHugContact)
            {
                _squeezeActive = false;
                _squeezeNextAt = Time.time + Mathf.Max(0.1f, CombatConstants.BearHugSqueezeInterval);
                DoBearHugSqueeze();
            }

            // まだ掴めているなら硬直を続ける（拘束側が時間を管理する）。
            if (IsHolding && Player != null)
            {
                var hold = Player.GetComponent<PlayerGrapple>();
                if (hold != null && hold.IsGrappled && hold.Captor == this)
                    return;
                IsHolding = false;
                ClearGrappleState();
                _timer = 0f;
            }

            _timer -= dt;
            if (_timer <= 0f)
                EndRecover();
        }

        /// <summary>
        /// 怯み。姿勢だけでなく実際に後ろへよろけさせる。
        /// 位置が動かないと「その場で反っただけ」に見え、間合いも詰まらない。
        /// </summary>
        void TickStagger(float dt)
        {
            _timer -= dt;
            StaggerProgress = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _staggerTotal));
            StaggerElapsed += dt;

            float p = Mathf.Clamp01(StaggerProgress);
            float speed = 1.15f * Mathf.Clamp(StaggerWeight, 1f, 2.2f) * p;
            transform.position -= transform.forward * (speed * dt);

            if (_timer <= 0f)
                EndStagger();
        }

        void MoveAsideForWallPin()
        {
            float center = _grapple.transform.position.x;
            float offset = transform.position.x - center;
            float side = offset < 0f ? -1f : 1f;

            // 壁カメラは -X 側にある。その側の敵は画角の外まで下がり、
            // +X 側の敵は奥で待つ。どちらも二人の前を横切らない。
            float clearance = side < 0f ? 4f : 1.85f;
            if (Group != null)
            {
                foreach (var other in Group.Members)
                {
                    if (other == null || other == this || other == _grapple.Captor || !other.IsAlive)
                        continue;

                    float otherOffset = (other.transform.position.x - center) * side;
                    if (otherOffset >= 0f && (otherOffset < Mathf.Abs(offset) ||
                        (otherOffset == Mathf.Abs(offset) && other.GetInstanceID() < GetInstanceID())))
                        clearance += 0.85f;
                }
            }

            if (Mathf.Abs(offset) < clearance)
            {
                var position = transform.position;
                float speed = MoveSpeed * State.MoveSpeedMultiplier;
                position.x = Mathf.MoveTowards(position.x, center + side * clearance, speed * Time.deltaTime);
                transform.position = position;
            }

            Vector3 toPlayer = _grapple.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
                Face(toPlayer.normalized, Time.deltaTime);
        }

        void LateUpdate()
        {
            // 壁押さえつけ: 相手は「彼女の手前（-Z）」に立つ。彼女はレーンの外へ
            // 押し出されているので、通常の「彼女の z に合わせる」処理は使わない。
            if (IsWallPinning)
            {
                var wallHold = Player.GetComponent<PlayerGrapple>();
                // 二体がかり（Y 字）中は、Update 側が彼女の横へ回り込む位置を決める。
                // ここで正面の定位置へ戻すと、彼が正面に残ってカメラが背中になる。
                if (wallHold != null && wallHold.IsWallSplitActive)
                    return;

                float t = wallHold != null ? wallHold.WallCaptorTravel : 1f;
                transform.position = Vector3.Lerp(_wallFromPos, _wallToPos, t);
                transform.rotation = Quaternion.Slerp(_wallFromRot, _wallToRot, t);
                return;
            }

            // 拘束が解けたあと、壁際からレーンへ戻る。
            if (_wallReturn > 0f)
            {
                _wallReturn = Mathf.Max(0f, _wallReturn - Time.deltaTime);
                float targetZ = Target != null ? Target.position.z : 0f;
                float t = Mathf.Clamp01(_wallReturn / WallReturnSeconds);
                float eased = Mathf.SmoothStep(0f, 1f, t);

                // X も正面の間合いへ戻す。壁の前では彼女と同じ x に立っているので、
                // そのままレーンへ戻すと体が重なる。
                float facing = Target != null && Mathf.Abs(Target.forward.x) > 0.01f
                    ? Mathf.Sign(Target.forward.x)
                    : 1f;
                float stopX = Target != null
                    ? Target.position.x + facing * CombatConstants.PinMinDistance
                    : transform.position.x;

                var returned = transform.position;
                returned.x = Mathf.Lerp(stopX, _wallReturnX, eased);
                returned.z = Mathf.Lerp(targetZ, _wallReturnZ, eased);
                transform.position = returned;
                return;
            }

            // 誰かが壁押さえつけしている間、他の敵はレーン（z=0）に留める。
            // 彼女の z に合わせると、全員が壁の前に押し寄せて壁へめり込む。
            // ただし二体がかりのアシストは、彼女の横（壁際）に付くので除外する。
            if (_grapple == null && Player != null)
                _grapple = Player.GetComponent<PlayerGrapple>();
            if (_grapple != null && _grapple.IsGrappled && _grapple.IsWallPin &&
                _grapple.Captor != this && !IsWallAssisting)
            {
                var lane = transform.position;
                lane.z = 0f;
                transform.position = lane;
                return;
            }

            // 二体がかりのアシストは、Update 側が彼女の正面側の持ち場を決める。
            // ここで彼女の z に合わせると、持ち場（正面）へ寄れない（実測: z が彼女の位置へ戻る）。
            if (IsWallAssisting)
                return;

            if (Target == null || (Player != null && Player.GroundMount.Attacker == this))
                return;
            var position = transform.position;
            position.z = Target.position.z;
            transform.position = position;
        }

        bool CanTurn()
        {
            return Phase != EnemyPhase.Windup
                && Phase != EnemyPhase.Attack
                && Phase != EnemyPhase.Stagger;
        }

        // --- フェーズ: Chase ------------------------------------------------------

    }
}
