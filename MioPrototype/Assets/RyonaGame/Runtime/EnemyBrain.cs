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
    public sealed class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>敵が選ぶ攻撃の種類。この 4 つを距離と乱数で抽選する。</summary>
        public enum AttackId
        {
            /// <summary>速い単発。予備動作が短く、間合いも短い。</summary>
            Jab = 0,

            /// <summary>遅いが重い。ひるみが大きい。</summary>
            Heavy = 1,

            /// <summary>ガード不能の崩し。予備動作は中くらい。</summary>
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

        [Tooltip("Break のダメージ。AttackKind.Break なのでガード不能")]
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

        /// <summary>攻撃トークンを保持しているか。<see cref="Group"/> が無ければ常に false。</summary>
        public bool HasAttackToken => Group != null && Group.HasAttackToken(this);

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
        const float BellyPunchBodyBias = 0.22f;

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

        /// <summary>
        /// この一振りがタッグ技（腹 / 顔）か。見た目側が片手打ちの姿勢を組むのに使う。
        /// 腹と顔が混ざるコンボでも腕の形が入れ替わらないよう、振り抜きまで true を保つ。
        /// </summary>
        public bool IsTagPunch => _tagPunchSwing;

        public float PunchPoseWeight
        {
            get
            {
                if (_currentAttack == AttackId.Grab) return 0f;

                // タッグ技の腹パンは、彼女が「く」の字に折れる間ずっと拳を腹に押し込む。
                // 通常の Recover（0.18 秒）だけで戻すと、折れる前に拳が引いて見える。
                // 予備動作・怯み・死亡では伸ばさない（振りかぶりが潰れる／死体が拳を突き出したままになる）。
                if (_punchPoseHoldSeconds > 0f &&
                    (Phase == EnemyPhase.Attack || Phase == EnemyPhase.Recover))
                    return 1f;

                if (Phase == EnemyPhase.Windup)
                {
                    // 当たりは「前フレームに描いた拳」で取る。通常の 0.7 から伸ばす ramp だと、
                    // 間合いの端（1.19m）で拳が伸びきる前に判定が来て空振りする。
                    // タッグ技（腹 / 顔）だけ早めに伸ばしきって、当てにいく。
                    bool tag = _tagPunchPending;
                    float start = tag ? 0.30f : 0.70f;
                    float span = tag ? 0.45f : 0.30f;
                    return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WindupProgress - start) / span));
                }
                if (Phase == EnemyPhase.Attack) return 1f;
                if (Phase == EnemyPhase.Recover) return Mathf.Clamp01(_punchVisualTimer / 0.18f);
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

        /// <summary>この一振りが押さえつけか。予備動作で決めて、当たった瞬間に使う。</summary>
        bool _pinPending;

        /// <summary>前回の押さえつけが始まった時刻。</summary>
        float _lastPinAt = -999f;

        /// <summary>この押さえつけで残っている膝蹴りの回数。</summary>
        int _pinKneesLeft;

        /// <summary>次の膝蹴りを入れる時刻（0 = 予定なし）。</summary>
        float _pinNextKneeAt;

        /// <summary>突き飛ばす側の腕を前に出す残り秒。</summary>
        float _pushUntil;

        /// <summary>引き渡しで彼女を押し出す瞬間、両腕を突き出す（0.2 秒）。</summary>
        public void PushAway()
        {
            _pushUntil = Time.time + 0.22f;
        }

        /// <summary>引き渡しが終わるまで、渡す側をその場に固定する残り時刻。</summary>
        float _handOffLockUntil;

        /// <summary>引き渡しのロック中か（歩かない・殴らない）。</summary>
        public bool IsHandOffLocked => Time.time < _handOffLockUntil;

        /// <summary>
        /// 引き渡しの間、渡す側をその場に固定する。腕を突き出したまま硬直（Recover）にし、
        /// 彼女が受け手の拘束位置へ移り終えるまで追いかけさせない。
        /// </summary>
        public void LockForHandOff(float seconds)
        {
            float length = Mathf.Max(0.1f, seconds);

            HideTelegraph();
            IsHolding = false;
            IsPinning = false;
            _pinKneesLeft = 0;
            ClearTransientAttackState();
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

        /// <summary>引き渡しで彼女を受け止める瞬間、腕を開いて構える（0.35 秒）。</summary>
        public void BeginCatch(float seconds = 0.35f)
        {
            _catchUntil = Time.time + Mathf.Max(0.1f, seconds);
        }

        /// <summary>いま受け止める構えの最中か（見た目側が腕を開くのに使う）。</summary>
        public bool IsCatching => Time.time < _catchUntil;

        /// <summary>押さえつけ中の相手か（掴んでいる本人）。見た目側が姿勢を組むのに使う。</summary>
        public bool IsPinning { get; private set; }

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
            _lastPinAt = -999f;   // 指示されたときはクールダウンを無視する
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
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (arg.Equals("-punchzone=face", System.StringComparison.OrdinalIgnoreCase)) ForceZone = PunchZone.Face;
                else if (arg.Equals("-punchzone=body", System.StringComparison.OrdinalIgnoreCase)) ForceZone = PunchZone.Body;
                else if (arg.Equals("-hitlog", System.StringComparison.OrdinalIgnoreCase)) _hitLog = true;
                else if (arg.Equals("-pinlog", System.StringComparison.OrdinalIgnoreCase)) _pinLog = true;
                else if (arg.Equals("-nofeint", System.StringComparison.OrdinalIgnoreCase))
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
            bool playerDown = Player.IsDead;

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
                    _punchVisualTimer = Mathf.Max(0f, _punchVisualTimer - dt);

                    // 押さえつけ中の膝蹴り（5 発）
                    if (IsPinning && _pinKneesLeft > 0 && Time.time >= _pinNextKneeAt)
                    {
                        _pinKneesLeft--;
                        _pinNextKneeAt = Time.time + Mathf.Max(0.1f, CombatConstants.PinKneeInterval);
                        DoPinKnee();
                    }

                    if (IsHolding && Player != null)
                    {
                        var hold = Player.GetComponent<PlayerGrapple>();
                        if (hold != null && hold.IsGrappled && hold.Captor == this)
                            break;
                        IsHolding = false;
                        IsPinning = false;
                        _pinKneesLeft = 0;
                        _timer = 0f;
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                        EndRecover();
                    break;

                case EnemyPhase.Stagger:
                    _timer -= dt;
                    StaggerProgress = Mathf.Clamp01(_timer / Mathf.Max(0.01f, _staggerTotal));
                    StaggerElapsed += dt;

                    // 姿勢だけでなく、実際に後ろへよろける。
                    // 位置が動かないと「その場で反っただけ」に見え、間合いも詰まらない。
                    {
                        float p = Mathf.Clamp01(StaggerProgress);
                        float speed = 1.15f * Mathf.Clamp(StaggerWeight, 1f, 2.2f) * p;
                        transform.position -= transform.forward * (speed * dt);
                    }

                    if (_timer <= 0f)
                        EndStagger();
                    break;
            }

            KeepSpacing();
        }

        void LateUpdate()
        {
            if (Target == null)
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

        void TickChase(Vector3 toTarget, float distance)
        {
            float ring = DesiredRing();

            // 攻撃権を持っていても、手前の仲間に塞がれて間合いまで入れないなら返す。
            // 持ったまま 1.7m で止まると、殴れる敵がいなくなる。
            if (distance > ring && Group != null && HasAttackToken && Group.IsBlockedByNearer(this))
            {
                ReleaseToken();
                EnterStrafe();
                return;
            }

            if (distance > ring)
            {
                // 遠いときだけ歩く。仲間と重ならないよう少し横へ逃げる。
                Vector3 dir = toTarget / Mathf.Max(0.001f, distance) + Separation();
                Move(dir.normalized);
                return;
            }

            // 間合いに入った。殴れるなら殴り、順番待ちなら回り込む。
            if (!TryBeginAttack(distance))
                EnterStrafe();
        }

        /// <summary>
        /// この敵が今いるべき距離。殴る番（トークン持ち）だけが間合いまで入り、
        /// 順番待ちは輪の外側で待つ。全員が同じ距離まで詰めると三人称カメラの画が敵で埋まる。
        /// </summary>
        float DesiredRing()
        {
            // グループが無ければ1体で戦うので、間合いまで普通に詰める
            if (HasAttackToken || Group == null)
                return EngageDistance;

            return EngageDistance * Mathf.Max(1.1f, WaitingRingScale) * _ringJitter;
        }

        /// <summary>仲間と近すぎるときに、横へ逃げる力。密集して1点に固まるのを防ぐ。</summary>
        Vector3 Separation()
        {
            if (Group == null)
                return Vector3.zero;

            const float radius = 1.7f;
            Vector3 push = Vector3.zero;

            var members = Group.Members;
            for (int i = 0; i < members.Count; i++)
            {
                var other = members[i];
                if (other == null || other == this)
                    continue;

                Vector3 d = transform.position - other.transform.position;
                d.y = 0f;
                float len = d.magnitude;
                if (len > 0.001f && len < radius)
                    push += d / len * (1f - len / radius);
            }

            return push * 0.9f;
        }

        // --- フェーズ: Strafe ----------------------------------------------------

        void TickStrafe(float dt, Vector3 toTarget, float distance)
        {
            _timer -= dt;

            // 間合いの中なら、トークンを取って殴る。取れなければ待つ。
            if (distance <= EngageDistance * 1.02f && TryBeginAttack(distance))
                return;

            // 順番が回ってきたら、殴る前にトークンを先取りして間合いまで歩く。
            // 近づいてよいのはトークンを持つ1体だけになるので、待機組が画面に詰め寄らない。
            if (!HasAttackToken && Time.time >= _attackReadyAt
                && (Group == null || Group.TryAcquireAttackToken(this)))
            {
                Phase = EnemyPhase.Chase;
                return;
            }

            // 間合いを維持するための前後補正。近すぎれば離れ、遠ければ詰める。
            float desired = DesiredRing();
            if (HasAttackToken && _strafeCloseIn)
                desired *= StrafeCloseInScale;

            // 引き離されすぎたら追い直す
            if (distance > desired * 1.45f)
            {
                Phase = EnemyPhase.Chase;
                return;
            }

            Vector3 radial = distance > 0.001f ? toTarget / distance : transform.forward;
            // 1 ラインでは回り込まず、前後の間合いだけ調整する。
            float error = distance - desired;
            Vector3 correction = radial * Mathf.Clamp(error * 2.5f, -1f, 1f);

            Vector3 direction = correction + Separation();
            if (Mathf.Abs(direction.x) > 0.06f)
                Move(direction.normalized, MoveSpeed * StrafeSpeedScale);

            if (_timer <= 0f)
                EnterStrafe();
        }

        void EnterStrafe()
        {
            Phase = EnemyPhase.Strafe;

            _timer = Mathf.Max(0.1f, StrafeSeconds) * UnityEngine.Random.Range(0.75f, 1.25f);
            _strafeCloseIn = UnityEngine.Random.value < StrafeCloseInChance;
        }

        // --- フェーズ: Windup / Attack / Recover ---------------------------------

        void TickWindup(float dt)
        {
            // 予備動作中は絶対に動かない（歩きながら殴らない）
            _timer -= dt;

            float total = Mathf.Max(0.05f, _run.Windup);
            float t = 1f - Mathf.Clamp01(_timer / total);
            WindupProgress = t;
            ApplyTelegraphVisual(t);

            if (_timer <= 0f)
            {
                // フェイント: 予備動作まで見せてから振らずに引く。
                // ガードを張らせて空振りさせるための揺さぶり。1 回の接近につき 1 度だけ。
                // タッグ技（拘束中の腹 / 顔）は必ず振る。引くと演出が空振りで止まる。
                if (!_tagPunchPending && !_feinted && FeintChance > 0f && UnityEngine.Random.value < FeintChance)
                {
                    _feinted = true;
                    HideTelegraph();
                    WindupProgress = 0f;
                    _attackReadyAt = Time.time + Mathf.Max(0.1f, FeintDelaySeconds);
                    ReleaseToken();
                    Phase = EnemyPhase.Chase;
                    Debug.Log($"[Enemy] feint {name}");
                    return;
                }

                Phase = EnemyPhase.Attack;
            }
        }

        /// <summary>判定を出す瞬間。1 フレームだけ <see cref="EnemyPhase.Attack"/> に滞在する。</summary>
        void Strike(float distance)
        {
            // 予備動作の最終フレームで別の敵が先に掴んだ場合も、二重に掴まない。
            if (IsRedundantGrab())
            {
                CancelWindup();
                return;
            }

            HideTelegraph();

            // ここで実際に振ったので、次の接近ではまたフェイントを選べる
            _feinted = false;

            // 振り抜きの風切り音。当たったかどうかとは別に、振ったことは必ず鳴らす
            PlaySfx(SfxId.Swing, 0.85f);

            // 倒れた相手は殴らない
            bool grabbed = false;

            if (Player != null && !Player.IsDead && _run.Data != null)
            {
                if (distance <= _run.Range + StrikeTolerance)
                {
                    if (_run.Id == AttackId.Grab)
                        grabbed = _pinPending ? TryPin() : TryGrab();
                    else if (PunchTouchesPlayer())
                    {
                        Player.LastHitZone = Zone;
                        float bellyDamage = _tagPunchPending && Zone == PunchZone.Body
                            ? CombatConstants.HeldBellyPunchDamage : 0f;
                        Player.ReceiveHit(State, _run.Data, transform, bellyDamage,
                            isTagPunch: _tagPunchPending);

                        // タッグ腹パンは、当たった瞬間から拳を腹に押し込む。
                        // 外れた／別の部位なら押し込まない（空振りで腕だけ残ると不自然）。
                        if (_bellyPunchPendingHold > 0f && Zone == PunchZone.Body)
                            _punchPoseHoldSeconds = _bellyPunchPendingHold;
                    }
                    else if (_hitLog || (_grapple != null && _grapple.IsGrappled))
                    {
                        Debug.Log($"[StrikeMiss] {name} tag={_tagPunchPending} zone={Zone} " +
                                  $"dist={distance:F2} range={_run.Range:F2}");
                    }
                }
                else if (_hitLog || (_grapple != null && _grapple.IsGrappled))
                {
                    Debug.Log($"[StrikeMiss] {name} out-of-range tag={_tagPunchPending} " +
                              $"dist={distance:F2} range={_run.Range:F2}+{StrikeTolerance:F2}");
                }
            }

            // この一振りで予約は使い切る。次の通常攻撃に持ち越さない。
            _bellyPunchPendingHold = 0f;
            _tagPunchPending = false;
            _pinPending = false;
            if (!grabbed)
                IsPinning = false;

            _punchVisualTimer = _run.Id == AttackId.Grab ? 0f : 0.18f;
            Phase = EnemyPhase.Recover;

            // 掴んだときは「相手がもがき終わるまで」硬直を伸ばす。
            // ここで上書きすると 0.7 秒で手を離してしまい、掴みポーズも消える。
            _timer = grabbed
                ? CombatConstants.GrappleLimitSeconds + 0.2f
                : Mathf.Max(0f, RecoverSeconds);
            IsHolding = grabbed;
            if (grabbed)
            {
                _forceGrab = false;
                _grabReadyAt = Time.time + CombatConstants.GrappleLimitSeconds + 3f;
                ReleaseToken();
            }
        }

        // Attack の前フレームで描いた拳の位置を使い、見えていない距離での命中を防ぐ。
        // 拳は狙いの座標（顔 / 腹）へ向けて振られているので、その座標への近さで当たりを取る。
        bool PunchTouchesPlayer()
        {
            var figure = GetComponent<EnemyFigure>();
            var fist = figure != null ? figure.Fist(false) : null;
            if (fist == null || Player == null) return false;
            Vector3 target = PunchTarget(Player, Zone, BodyTargetBias);
            Vector3 toPlayer = Player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (Vector3.Dot(transform.forward, toPlayer) <= 0f) return false;
            float gap = Vector3.Distance(fist.position, target);
            // 腹パンの距離は -hitlog のときだけ出す（常時出すとログが埋まる）。
            if (_hitLog && (_punchPoseHoldSeconds > 0f || _bellyPunchPendingHold > 0f || _tagPunchPending))
            {
                Transform wrist = figure.WristBone(false);
                float wristGap = wrist != null ? Vector3.Distance(wrist.position, target) : -1f;
                Debug.Log($"[BellyHit] att={name} pos=({transform.position.x:F2},{transform.position.z:F2}) " +
                          $"yaw={transform.eulerAngles.y:F0} " +
                          $"fist=({fist.position.x:F2},{fist.position.y:F2},{fist.position.z:F2}) " +
                          $"target=({target.x:F2},{target.y:F2},{target.z:F2}) gap={gap:F2} wristGap={wristGap:F2} " +
                          $"bias={BodyTargetBias:F2}");
            }

            return gap <= Mathf.Max(0.05f, PunchHitRadius);
        }

        /// <summary>
        /// 掴みが当たったとき。プレイヤーを掴んだまま硬直を伸ばし、
        /// 振りほどかれる（=こちらがよろける）か時間切れで解放されるまで待つ。
        /// 掴めるのは背後に回れているときだけ（正面掴みは廃止した）。
        /// </summary>
        bool TryGrab()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            // 判定は候補の抽選（<see cref="CollectCandidates"/>）と同じ IsBehindPlayer を使う。
            // ここだけ別の閾値にすると「選ばれたのに空振りする」角度ができる。
            if (!IsBehindPlayer())
                return false;

            grapple.Begin(this, GrappleKind.Back);
            return true;
        }

        /// <summary>
        /// 拘束中のプレイヤーへ、タッグ技のジャブを開始する（狙いは腹 or 顔）。
        /// 正面・間合い内・攻撃トークンありのときだけ開始する。
        /// 演出側（<see cref="HeldBellyPunchDirector"/>）が位置と向きを合わせてから呼ぶ。
        /// </summary>
        public bool TryStartTagPunch(PunchZone zone, bool allowRecover = false, float holdPoseSeconds = 0f)
        {
            if (Phase == EnemyPhase.Dead || Phase == EnemyPhase.Windup ||
                Phase == EnemyPhase.Attack || Phase == EnemyPhase.Stagger || IsRising)
                return false;

            // コンボ中は前の一発の硬直を切って次を打たせる（5 連続をテンポよく見せる）。
            if (Phase == EnemyPhase.Recover && !allowRecover)
                return false;

            if (Player == null || Player.IsDead)
                return false;

            var grapple = Player.GetComponent<PlayerGrapple>();
            if (grapple == null || !grapple.IsGrappled || grapple.Captor == this)
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > RangeOf(AttackId.Jab) + 0.25f)
                return false;

            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (to.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(facing.normalized, to.normalized) < 0.85f)
                return false;

            // 同時攻撃の上限は守る。取れなければ演出側が少し待って選び直す。
            if (Group != null && !Group.TryAcquireAttackToken(this))
                return false;

            ForceZone = zone;
            BeginWindup(AttackId.Jab);

            // 彼女が折れる間、拳を腹に押し込んだままにする。数え始めは「当たった瞬間」
            // （Strike）。ここで数えると予備動作の 0.35 秒を食って、折れている間に足りなくなる。
            // 顔を狙うときは押し込まない（通常の顔面パンチと同じ当たり方にする）。
            _bellyPunchPendingHold = zone == PunchZone.Body ? Mathf.Max(0f, holdPoseSeconds) : 0f;
            _tagPunchPending = true;
            _tagPunchSwing = true;
            ForceZone = null;

            Debug.Log($"[HeldPunch] order {name} zone={zone} dist={distance:F2} " +
                      $"pos=({transform.position.x:F2},{transform.position.z:F2})");
            return true;
        }

        /// <summary>
        /// 引き渡しで拘束を受け持つ。掴んだまま硬直（Recover）を維持し、
        /// 相手がもがき終えるまで離さない。
        /// </summary>
        public void TakeOverHold(float seconds)
        {
            IsHolding = true;
            HideTelegraph();
            _currentAttack = AttackId.Grab;
            _timer = Mathf.Max(0.5f, seconds);
            Phase = EnemyPhase.Recover;
            ReleaseToken();

            // 正面拘束（Pin）を引き継いだときは、そのまま膝蹴りまで続ける。
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            bool pin = grapple != null && grapple.Kind == GrappleKind.Pin;
            IsPinning = pin;
            _pinKneesLeft = pin ? Mathf.Max(1, CombatConstants.PinKneeCount) : 0;
            _pinNextKneeAt = pin ? Time.time + Mathf.Max(0.1f, CombatConstants.PinKneeDelay) : 0f;

            // 押し出されてくる彼女を受け止める構え（腕を開いて半歩かがむ）。
            // 彼女が届くのは突き飛ばし 0.3 秒 + 向き変更 0.25 秒後なので、0.9 秒保つ。
            BeginCatch(0.9f);

            // 受け手はその場を動かさず、届いた彼女をそのまま拘束する。
            // 向きだけは彼女へ合わせる（後ろ拘束なら背中、正面拘束なら顔の正面）。
            if (Player != null)
            {
                Vector3 look = Player.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            Debug.Log($"[HandOff] {name} が拘束を受け持つ {seconds:F1}s pin={pin}");
        }

        /// <summary>外部から怯ませる（振りほどかれた時など）。</summary>
        public void ForceStagger(float seconds)        {
            if (Phase == EnemyPhase.Dead)
                return;

            HideTelegraph();
            _riseTimer = 0f;
            IsHolding = false;
            IsPinning = false;
            _pinKneesLeft = 0;
            ClearTransientAttackState();
            ReleaseToken();
            _timer = Mathf.Max(0.05f, seconds);
            _staggerTotal = _timer;
            StaggerProgress = 1f;
            StaggerElapsed = 0f;
            StaggerWeight = 1.6f;
            Phase = EnemyPhase.Stagger;

            Debug.Log($"[Enemy] force stagger {name} {seconds:F2}s");
        }

        void EndRecover()
        {
            ReleaseToken();

            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            IsHolding = false;
            Phase = EnemyPhase.Chase;
        }

        bool IsRedundantGrab()
        {
            if (_run.Id != AttackId.Grab || Player == null)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            return _grapple != null && _grapple.IsGrappled;
        }

        /// <summary>プレイヤーが倒れた等で予備動作を中断する。硬直は挟まない。</summary>
        void CancelWindup()
        {
            HideTelegraph();
            ClearTransientAttackState();
            ReleaseToken();
            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            Phase = EnemyPhase.Chase;
        }

        /// <summary>
        /// 進行中の一振りに付随するフラグを落とす。
        /// 予備動作を中断したり怯んだりしたときに残すと、次の攻撃を始めるまで
        /// KeepSpacing（重なり回避）・フェイント・片手打ちの姿勢が効き続ける。
        /// 振り抜き（Strike）と硬直（Recover）では落とさないこと。
        /// </summary>
        void ClearTransientAttackState()
        {
            _tagPunchPending = false;
            _tagPunchSwing = false;
            _bellyPunchPendingHold = 0f;
            _punchPoseHoldSeconds = 0f;
            _pinPending = false;
        }

        // --- 攻撃の抽選 ----------------------------------------------------------

        bool TryBeginAttack(float distance)
        {
            if (Player == null || Player.IsDead)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            // 回り込み直後は反対を向いていることがある。予備動作中は旋回を
            // 固定するため、向き直る前に攻撃を始めると必ず空振りになる。
            Vector3 facingTarget = Player.transform.position - transform.position;
            facingTarget.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (facingTarget.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(facing.normalized, facingTarget.normalized) < 0.9f)
                return false;

            if (Time.time < _attackReadyAt)
                return false;

            AttackId id;
            bool force = _forceGrab;
            bool pin = false;

            bool fromBehind = Vector3.Dot(Player.transform.forward, transform.position - Player.transform.position) < -0.25f;
            if (_grapple != null && !_grapple.IsGrappled && fromBehind && distance <= RangeOf(AttackId.Grab)
                && Time.time >= _grabReadyAt && (!_hasLast || _lastAttack != AttackId.Grab)
                && UnityEngine.Random.value < 0.65f)
                id = AttackId.Grab;
            else if (CanStartPin(distance))
            {
                // 正面からの押さえつけ。当たると 4.2 秒 / 12 回のレバガチャになる。
                id = AttackId.Grab;
                pin = true;
            }
            else if (force && (_grapple == null || !_grapple.IsGrappled) && distance <= RangeOf(AttackId.Grab))
            {
                _forceGrab = false;
                id = AttackId.Grab;
            }
            else if (!TrySelectAttack(distance, out id))
                return false;

            // グループがあれば同時攻撃の順番を取る。取れなければ Strafe を続ける。
            if (Group != null && !Group.TryAcquireAttackToken(this))
                return false;

            BeginWindup(id);
            _pinPending = pin;

            // 押さえつけが選ばれなかった理由を 1 秒に 1 回だけ出す（-pinlog）
            if (_pinLog && !pin && CanPin && Time.time >= _pinLogNextAt)
            {
                _pinLogNextAt = Time.time + 1f;
                Debug.Log($"[PinCheck] {name} id={id} dist={distance:F2} " +
                          $"behind={IsBehindPlayer()} grappled={(_grapple != null && _grapple.IsGrappled)} " +
                          $"tok={HasAttackToken} sincePin={Time.time - _lastPinAt:F1} " +
                          $"range={CombatConstants.PinMinDistance:F2}-{CombatConstants.PinMaxDistance:F2}");
            }
            return true;
        }

        /// <summary>次の [PinCheck] ログまで（-pinlog）。</summary>
        float _pinLogNextAt;

        /// <summary>押さえつけが選ばれない理由をログに出す（-pinlog）。</summary>
        bool _pinLog;

        /// <summary>
        /// 押さえつけ（Pin）を始めてよいか。正面・間合い・クールダウンを満たすときだけ true。
        /// 攻撃トークン（同時攻撃の上限）は呼び出し側の <see cref="TryBeginAttack"/> が見る。
        /// 背後からは羽交い締め（Back）になるので出さない。
        /// </summary>
        public bool CanStartPin(float distance)
        {
            if (!CanPin && !_pinOrder)
                return false;

            if (Player == null || Player.IsDead)
                return false;

            if (distance < CombatConstants.PinMinDistance || distance > CombatConstants.PinMaxDistance)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();

            if (_grapple == null || _grapple.IsGrappled)
                return false;

            // 背後からは羽交い締め。押さえつけは正面からだけ。
            if (IsBehindPlayer())
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f)
                return false;

            if (Vector3.Dot(transform.forward, to.normalized) < 0.6f)
                return false;

            // 指示されたとき（-pintest）以外は、連発しないよう間を空ける。
            if (!_pinOrder && Time.time < _lastPinAt + CombatConstants.PinCooldown)
                return false;

            return true;
        }

        /// <summary>
        /// 押さえつけが当たったとき。正面から掴んで 4.2 秒 / 12 回のレバガチャへ移す。
        /// </summary>
        bool TryPin()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            // 判定は抽選（CanStartPin）と同じ向き・間合いで取る。
            // ここだけ別の条件にすると「選ばれたのに空振りする」角度ができる。
            if (IsBehindPlayer())
                return false;

            Vector3 to = Player.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude > RangeOf(AttackId.Grab) + StrikeTolerance)
                return false;

            grapple.Begin(this, GrappleKind.Pin);
            _lastPinAt = Time.time;
            _pinOrder = false;
            IsPinning = true;
            // 押さえつけている間に膝を 5 発入れる（何もできない 4.2 秒にしない）
            _pinKneesLeft = Mathf.Max(1, CombatConstants.PinKneeCount);
            _pinNextKneeAt = Time.time + Mathf.Max(0.1f, CombatConstants.PinKneeDelay);
            Debug.Log($"[Pin] begin {name} dist={to.magnitude:F2} knees={_pinKneesLeft}");
            return true;
        }

        /// <summary>
        /// 押さえつけ中の膝蹴り。彼女は掴まれたままなので、姿勢は崩れずダメージだけ入る
        /// （見た目は <see cref="PlayerGrapple.KneePulse"/> を両者が読んで作る）。
        /// </summary>
        void DoPinKnee()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || !grapple.IsGrappled || grapple.Captor != this)
                return;

            var knee = new AttackData
            {
                id = "pin_knee",
                kind = AttackKind.Light,
                damage = Mathf.Max(0f, CombatConstants.PinKneeDamage),
                partDamage = 0f,
                targetPart = BodyPart.Torso,
                flinch = 6f,
                startupSeconds = 0f,
                activeSeconds = 0f,
                recoverySeconds = 0f
            };

            // 腹への一撃。表情も腹ヒット用に切り替える（解放の直前で消す必要はない）。
            Player.LastHitZone = PunchZone.Body;
            Player.ReceiveHit(State, knee, transform, CombatConstants.HeldKneeBellyDamage);
            grapple.KickKnee();
            PlaySfx(SfxId.Impact, 0.85f);

            Debug.Log($"[Pin] knee {name} left={_pinKneesLeft} hp={Player.State.Hp:F0}");
        }

        /// <summary>
        /// 距離と乱数で攻撃を選ぶ。直前と同じ攻撃は避け、同じ攻撃の 3 連続は禁止する。
        /// </summary>
        bool TrySelectAttack(float distance, out AttackId id)
        {
            id = AttackId.Jab;

            // 1 回目: 直前と同じ攻撃を除いた候補
            int count = CollectCandidates(distance, excludeLast: true);
            if (count == 0)
            {
                // 2 回目: 2 連続までは許す（3 連続は絶対に作らない）
                if (_hasLast && _repeatCount >= 2)
                {
                    return false;   // 間合い外か、3 連続になる。攻めずに回り込む
                }

                count = CollectCandidates(distance, excludeLast: false);
                if (count == 0)
                    return false;
            }

            float total = 0f;
            for (int i = 0; i < count; i++)
                total += _candidateWeights[i];

            if (total <= 0f)
                return false;

            float roll = UnityEngine.Random.value * total;
            int pick = count - 1;
            for (int i = 0; i < count; i++)
            {
                roll -= _candidateWeights[i];
                if (roll <= 0f)
                {
                    pick = i;
                    break;
                }
            }

            id = _candidateIds[pick];
            return true;
        }

        /// <summary>
        /// 押さえつけの準備ができているとき、彼女の正面へ回り込む。
        /// 背後にいると <see cref="CanStartPin"/> が通らず、羽交い締め（Back）ばかりになって
        /// 「正面から掴まれない」状態になる（実測）。正面 1.0m まで歩いてから攻撃抽選に戻す。
        /// </summary>
        bool TryApproachForPin()
        {
            if (!CanPin && !_pinOrder)
                return false;

            if (IsPinning || Player == null || Player.IsDead)
                return false;

            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();
            if (_grapple == null || _grapple.IsGrappled)
                return false;

            // クールダウン中は回り込まない（ふだんどおり戦う）
            if (!_pinOrder && Time.time < _lastPinAt + CombatConstants.PinCooldown)
                return false;

            // すでに正面にいれば、通常の攻撃抽選（CanStartPin）に任せる
            Vector3 toEnemy = transform.position - Player.transform.position;
            toEnemy.y = 0f;
            Vector3 playerForward = Player.transform.forward;
            playerForward.y = 0f;
            if (toEnemy.sqrMagnitude > 1e-4f && playerForward.sqrMagnitude > 1e-4f &&
                Vector3.Dot(playerForward.normalized, toEnemy.normalized) > 0.9f)
                return false;

            Vector3 front = playerForward.sqrMagnitude > 1e-4f ? playerForward.normalized : Vector3.right;
            Vector3 target = Player.transform.position + front * 1.0f;
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.04f)
                return false;

            Move(dir.normalized, MoveSpeed * 0.9f);
            return true;
        }

        /// <summary>この敵が、プレイヤーの背中側にいるか。</summary>
        bool IsBehindPlayer()
        {
            if (Player == null)
                return false;

            return Vector3.Dot(Player.transform.forward,
                transform.position - Player.transform.position) < -0.25f;
        }

        int CollectCandidates(float distance, bool excludeLast)
        {
            int count = 0;

            // すでに誰かが掴んでいるなら、掴みは選ばない（二重に掴めない）
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            bool alreadyGrappled = grapple != null && grapple.IsGrappled;

            for (int i = 0; i < AttackCount; i++)
            {
                var id = (AttackId)i;

                if (distance > RangeOf(id))
                    continue;

                if (id == AttackId.Grab && (alreadyGrappled || Time.time < _grabReadyAt))
                    continue;

                // 掴みは背後からだけ（羽交い締め）。正面からの掴みは無い。
                if (id == AttackId.Grab && !IsBehindPlayer())
                    continue;

                if (excludeLast && _hasLast && _lastAttack == id)
                    continue;

                _candidateIds[count] = id;
                _candidateWeights[count] = WeightOf(id);
                count++;
            }

            return count;
        }

        /// <summary>重い攻撃ほど出しにくくして、軽い刻みが主体になるようにする。</summary>
        static float WeightOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return 1.10f;
                case AttackId.Heavy: return 0.85f;
                case AttackId.Break: return 0.70f;
                case AttackId.Grab: return 0.75f;
                default: return 0f;
            }
        }

        void BeginWindup(AttackId id)
        {
            _currentAttack = id;
            _punchPoseHoldSeconds = 0f;
            _bellyPunchPendingHold = 0f;
            _tagPunchPending = false;
            _tagPunchSwing = false;
            _pinPending = false;

            AttackData data = DataOf(id);
            if (data != null)
                data.damage = Mathf.Max(0f, DamageOf(id));   // インスペクタの値をそのまま反映する

            _run = new EnemyAttackRunner
            {
                Id = id,
                Data = data,
                Range = RangeOf(id),
                Windup = Mathf.Max(0.05f, WindupOf(id))
            };

            _timer = _run.Windup;

            // 狙いを決める。崩しは腹、素早い一撃は顔寄り、重い一撃は腹寄り。
            Zone = ForceZone ?? id switch
            {
                AttackId.Jab => UnityEngine.Random.value < JabFaceChance ? PunchZone.Face : PunchZone.Body,
                AttackId.Heavy => UnityEngine.Random.value < HeavyFaceChance ? PunchZone.Face : PunchZone.Body,
                AttackId.Break => PunchZone.Body,
                _ => PunchZone.Face
            };

            if (_hasLast && _lastAttack == id)
            {
                _repeatCount++;
            }
            else
            {
                _lastAttack = id;
                _repeatCount = 1;
                _hasLast = true;
            }

            Phase = EnemyPhase.Windup;
            ShowTelegraph();
            ApplyTelegraphVisual(0f);

            // 予備動作の合図。背中側から来ても音で分かるようにする
            PlaySfx(id == AttackId.Heavy ? SfxId.TellHeavy : SfxId.Tell, id == AttackId.Heavy ? 0.75f : 0.55f);
        }

        /// <summary>
        /// この敵の効果音。位置を持たせて「どこから鳴ったか」が分かるようにする。
        /// 2D の共通音源だと、後ろから来たのか前から来たのか耳で区別できない。
        /// </summary>
        void PlaySfx(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx == null)
                return;

            if (_voice == null)
            {
                // 胸の高さに置く。足元だと低く聞こえて方向が掴みにくい
                var voiceGo = new GameObject("Voice");
                voiceGo.transform.SetParent(transform, false);
                voiceGo.transform.localPosition = new Vector3(0f, 1.15f, 0f);

                _voice = voiceGo.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.spatialBlend = 1f;                  // 3D。左右と距離が付く
                _voice.dopplerLevel = 0f;                  // 動き回るので音程が揺れないようにする
                _voice.rolloffMode = AudioRolloffMode.Linear;
                _voice.minDistance = 1.6f;
                _voice.maxDistance = 22f;
                _voice.volume = 1f;

                Debug.Log($"[Sfx] 3D音源を作成 min={_voice.minDistance} max={_voice.maxDistance} " +
                          $"listener={(UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null)}");
            }

            sfx.PlayOn(_voice, id, volume);
        }

        AttackData DataOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return _jab;
                case AttackId.Heavy: return _heavy;
                case AttackId.Break: return _break;
                case AttackId.Grab: return _grab;
                default: return _jab;
            }
        }

        float RangeOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return JabRange > 0f ? JabRange : AttackRange;
                case AttackId.Heavy: return HeavyRange > 0f ? HeavyRange : AttackRange;
                case AttackId.Break: return BreakRange > 0f ? BreakRange : AttackRange;
                case AttackId.Grab:
                    // 掴みは「攻撃を始める間合い（EngageDistance）」で必ず選べるようにする。
                    // GrabRange が EngageDistance より狭いと、敵が間合いに入った瞬間に
                    // 掴みが候補から外れて出番がなくなる。
                    return Mathf.Max(GrabRange > 0f ? GrabRange : AttackRange, EngageDistance);
                default: return AttackRange;
            }
        }

        float WindupOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return JabSeconds > 0f ? JabSeconds : WindupSeconds;
                case AttackId.Heavy: return HeavySeconds > 0f ? HeavySeconds : WindupSeconds;
                case AttackId.Break: return BreakSeconds > 0f ? BreakSeconds : WindupSeconds;
                case AttackId.Grab: return GrabSeconds > 0f ? GrabSeconds : WindupSeconds;
                default: return WindupSeconds;
            }
        }

        float DamageOf(AttackId id)
        {
            switch (id)
            {
                case AttackId.Jab: return JabDamage;
                case AttackId.Heavy: return HeavyDamage;
                case AttackId.Break: return BreakDamage;
                case AttackId.Grab: return GrabDamage;
                default: return 0f;
            }
        }

        /// <summary>AttackLibrary の定義を元に、ダメージだけ持ち替えた 3 種を作る。</summary>
        void BuildAttacks()
        {
            _jab = AttackLibrary.Light();
            _jab.id = "enemy_jab";
            _jab.damage = Mathf.Max(0f, JabDamage);

            _heavy = AttackLibrary.Heavy();
            _heavy.id = "enemy_heavy";
            _heavy.damage = Mathf.Max(0f, HeavyDamage);

            _break = AttackLibrary.BreakAttack();
            _break.id = "enemy_break";
            _break.damage = Mathf.Max(0f, BreakDamage);

            // 掴み。ダメージは低く、当たったらレバガチャ（PlayerGrapple）に移る
            _grab = AttackLibrary.GrappleAttack();
            _grab.id = "enemy_grab";
            _grab.damage = Mathf.Max(0f, GrabDamage);
        }

        // --- 被弾・死亡 ----------------------------------------------------------

        /// <summary>
        /// ダメージを受ける。解決は <see cref="DamageResolver"/> に任せ、
        /// 怯みが出たら進行中の攻撃をキャンセルして <see cref="EnemyPhase.Stagger"/> へ移る。
        /// </summary>
        public HitResult TakeHit(CombatState attacker, AttackData attack)
        {
            if (Phase == EnemyPhase.Dead)
                return default;

            var result = DamageResolver.Resolve(attacker, attack, State, defenderBlocking: false);

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

        void EnterStagger(StaggerTier tier, BodyPart part = BodyPart.Torso)
        {
            if (IsHolding && Player != null)
            {
                var hold = Player.GetComponent<PlayerGrapple>();
                if (hold != null && hold.IsGrappled && hold.Captor == this)
                    hold.Release();
                IsHolding = false;
            }
            // 進行中の攻撃はキャンセルする。トークンも返して他の敵に順番を渡す。
            HideTelegraph();
            IsPinning = false;
            _pinKneesLeft = 0;
            ClearTransientAttackState();
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
            IsPinning = false;
            _pinKneesLeft = 0;
            _pinOrder = false;
            ClearTransientAttackState();
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
            IsPinning = false;
            _pinKneesLeft = 0;
            _pinNextKneeAt = 0f;
            _pinOrder = false;
            ClearTransientAttackState();
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

        void RegisterGroup()
        {
            if (_registered || Group == null)
                return;

            Group.Register(this);
            _registered = true;
        }

        void UnregisterGroup()
        {
            if (!_registered)
                return;

            _registered = false;
            if (Group != null)
                Group.Unregister(this);
        }

        void ReleaseToken()
        {
            if (Group != null)
                Group.ReleaseAttackToken(this);
        }

        // --- 移動 ----------------------------------------------------------------

        void Move(Vector3 direction)
        {
            Move(direction, MoveSpeed);
        }

        void Move(Vector3 direction, float speed)
        {
            // 脚の損傷で移動速度が落ちる（CombatState.MoveSpeedMultiplier）
            direction.z = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                direction.Normalize();
            float rate = speed * State.MoveSpeedMultiplier;
            if (rate <= 0f || direction.sqrMagnitude <= 0.0001f)
                return;

            transform.position += direction * (rate * Time.deltaTime);
        }

        /// <summary>
        /// 体の間隔を毎フレーム保つ。移動していないとき（予備動作中など）に
        /// 彼女が歩いて重なってくるのを防ぐ。
        /// </summary>
        void KeepSpacing()
        {
            if (Phase == EnemyPhase.Dead || IsHolding || IsHandOffLocked)
                return;

            // タッグ腹パンは演出側（HeldBellyPunchDirector）が当たる位置を決めている。
            // ここで仲間と押し分けると、置いた位置から横へずれて拳が届かなくなる
            // （実測: 0.33m ずれて gap 0.33 の空振りが続いた）。
            // 顔パンは _bellyPunchPendingHold が 0 なので、発注から当たるまでの
            // _tagPunchPending でも除外する（でないと顔パンだけ位置がずらされて外れる）。
            if (_bellyPunchPendingHold > 0f || _punchPoseHoldSeconds > 0f || _tagPunchPending)
                return;

            KeepOutOfPlayer();
            KeepOutOfOthers();
        }

        /// <summary>
        /// 彼女にめり込まないための下限。近づきすぎたら敵の側で距離を戻す。
        /// 真上に重なったときは後ろへ逃がす（0 除算を避ける）。
        /// </summary>
        void KeepOutOfPlayer()
        {
            if (Target == null || MinPlayerGap <= 0f)
                return;

            // 掴んでいる間は詰めた距離を保つ。
            // ここで 0.95 まで戻すと、伸ばした腕が彼女に届かず手が宙に浮く。
            if (IsHolding)
                return;

            Vector3 d = transform.position - Target.position;
            d.y = 0f;
            float len = d.magnitude;

            if (len >= MinPlayerGap)
                return;

            Vector3 dir = len > 0.001f ? d / len : -transform.forward;
            Vector3 wanted = Target.position + dir * MinPlayerGap;

            // 1 フレームで飛ばさず詰めて戻す。
            // 掴みを解いた瞬間は 0.60 → 0.95 を戻すので、硬く代入すると 35cm ワープする。
            transform.position = Vector3.MoveTowards(
                transform.position, wanted, 3f * Time.deltaTime);
        }

        /// <summary>
        /// 仲間と重ならないための下限。soft な <see cref="Separation"/> だけだと
        /// 二人羽織のように重なるので、硬い下限も置く（半分ずつ押し分ける）。
        /// </summary>
        void KeepOutOfOthers()
        {
            if (Group == null)
                return;

            const float min = 0.82f;
            var members = Group.Members;

            for (int i = 0; i < members.Count; i++)
            {
                var other = members[i];
                if (other == null || other == this || other.Phase == EnemyPhase.Dead || other.IsFlanking)
                    continue;

                Vector3 d = transform.position - other.transform.position;
                d.y = 0f;
                float len = d.magnitude;

                if (len > 0.001f && len < min)
                    transform.position = other.transform.position + d / len * (min * 0.5f + len * 0.5f);
            }
        }

        void Face(Vector3 forward, float dt)
        {
            Quaternion look = Quaternion.LookRotation(forward, Vector3.up);
            float t = Mathf.Clamp01(Mathf.Max(0f, TurnSpeed) * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, t);
        }

        // --- テレグラフ ----------------------------------------------------------

        void ShowTelegraph()
        {
            if (Telegraph == null)
                return;

            EnsureTelegraphMaterial();
            Telegraph.gameObject.SetActive(true);
        }

        void HideTelegraph()
        {
            if (Telegraph != null)
                Telegraph.gameObject.SetActive(false);
        }

        /// <summary>
        /// 予備動作の進捗（0〜1）に合わせて球を拡大し、色を変える。
        /// 最後の <see cref="TelegraphMaxHoldSeconds"/> は最大サイズで固定し、「ここで来る」を確定させる。
        /// </summary>
        void ApplyTelegraphVisual(float t)
        {
            if (Telegraph == null)
                return;

            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            float maxScale = Mathf.Max(0.01f, TelegraphMaxScale);
            float minScale = Mathf.Clamp(TelegraphStartScale, 0.01f, maxScale);
            float scale = Mathf.Lerp(minScale, maxScale, grow);

            if (_timer <= Mathf.Max(0f, TelegraphMaxHoldSeconds))
                scale = maxScale;

            Telegraph.localScale = _baseTelegraphScale * scale;
            ApplyTelegraphColor(Color.Lerp(TelegraphStartColor, TelegraphColor, grow));
        }

        void EnsureTelegraphMaterial()
        {
            if (_telegraphMaterial != null || Telegraph == null)
                return;

            if (_telegraphRenderer == null)
                _telegraphRenderer = Telegraph.GetComponentInChildren<Renderer>(true);

            if (_telegraphRenderer != null)
                _telegraphMaterial = _telegraphRenderer.material;   // 実行時インスタンス。アセットは汚さない
        }

        void ApplyTelegraphColor(Color color)
        {
            if (_telegraphMaterial == null)
                return;

            // Built-in の Standard は _Color、URP Lit は _BaseColor を見る。両対応しておく。
            if (_telegraphMaterial.HasProperty(ColorId))
                _telegraphMaterial.SetColor(ColorId, color);
            if (_telegraphMaterial.HasProperty(BaseColorId))
                _telegraphMaterial.SetColor(BaseColorId, color);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (TelegraphMaxScale < 0.01f)
                TelegraphMaxScale = 0.01f;
            if (StrafeSpeedScale < 0f)
                StrafeSpeedScale = 0f;
            if (MoveSpeed < 0f)
                MoveSpeed = 0f;
        }
#endif
    }
}
