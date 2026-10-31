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
    [AddComponentMenu("Ryona/Enemy Brain")]
    public sealed class EnemyBrain : MonoBehaviour, IDamageable
    {
        /// <summary>敵が選ぶ攻撃の種類。この 4 つを距離と乱数で抽選する。</summary>
        public enum AttackId
        {
            /// <summary>速い単発。予備動作が短く、間合いも短い。</summary>
            Jab = 0,

            /// <summary>遅いが重い。体勢値を大きく削る。</summary>
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

        [Tooltip("Heavy のダメージ。体勢削りは AttackLibrary.Heavy の値を使う")]
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

        [Tooltip("ガードブレイク怯みの秒数（0 以下なら CombatConstants.StunGuardBreak）")]
        public float StaggerSecondsGuardBreak = 1.20f;

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

        /// <summary>HP / スタミナ / 体勢を持つ戦闘状態。攻撃側としても渡す。</summary>
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

        /// <summary>現在フェーズの残り秒数（予備動作・硬直・怯みで共用）。</summary>
        float _timer;
        float _punchVisualTimer;
        public float PunchPoseWeight
        {
            get
            {
                if (_currentAttack == AttackId.Grab) return 0f;
                if (Phase == EnemyPhase.Windup)
                    return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WindupProgress - 0.7f) / 0.3f));
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
        int _swingsWithToken;

        /// <summary>デバッグ用: 次の攻撃を掴みに固定する（-grappletest）。</summary>
        bool _forceGrab;

        /// <summary>掴まれ状態を見るためだけに持つプレイヤー側の参照。</summary>
        PlayerGrapple _grapple;

        /// <summary>次の攻撃を掴みにする。デバッグの駆動用。</summary>
        public void QueueGrab()
        {
            _forceGrab = true;
            _attackReadyAt = Time.time;
        }

        /// <summary>
        /// しばらく次の攻撃を始めない。掴みに失敗した直後、他の敵が一斉に
        /// 追撃すると 1 回の失敗で HP が 3 割飛ぶため、一拍だけ様子を見させる。
        /// </summary>
        public void HoldFire(float seconds)
        {
            _attackReadyAt = Mathf.Max(_attackReadyAt, Time.time + Mathf.Max(0f, seconds));
        }

        /// <summary>横回りの向き（+1 / -1）。</summary>
        float _strafeSign = 1f;
        float _flankSide;
        Collider _bodyCollider;
        readonly System.Collections.Generic.List<Collider> _flankIgnored = new System.Collections.Generic.List<Collider>();
        public bool IsFlanking => _flankSide != 0f;

        void StopFlanking()
        {
            if (!IsFlanking) return;
            _flankSide = 0f;
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
            var grapple = Player.GetComponent<PlayerGrapple>();
            if (grapple != null && grapple.IsGrappled)
            {
                StopFlanking();
                return false;
            }
            if (!IsFlanking)
            {
                float facing = Mathf.Sign(Target.forward.x);
                float own = (transform.position.x - Target.position.x) * facing;
                if (own < 1f || HasAttackToken) return false;
                EnemyBrain lead = null, candidate = null;
                float leadDistance = float.PositiveInfinity, candidateDistance = float.PositiveInfinity;
                foreach (var other in Group.Members)
                {
                    if (other == null || !other.IsAlive) continue;
                    if (other != this && other.IsFlanking) return false;
                    float offset = (other.transform.position.x - Target.position.x) * facing;
                    if (offset > 0f && offset < leadDistance)
                    {
                        lead = other;
                        leadDistance = offset;
                    }
                }
                if (lead == null || lead == this || leadDistance > 2.2f) return false;
                foreach (var other in Group.Members)
                {
                    if (other == null || other == lead || !other.IsAlive || other.HasAttackToken) continue;
                    float offset = (other.transform.position.x - Target.position.x) * facing;
                    if (offset > leadDistance + 0.1f && offset < candidateDistance)
                    {
                        candidate = other;
                        candidateDistance = offset;
                    }
                }
                if (candidate != this) return false;
                _flankSide = -facing;
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
            float destination = Target.position.x + _flankSide * 1.5f;
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

            State.Tick(dt, moving: Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe);

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

            if ((Phase == EnemyPhase.Chase || Phase == EnemyPhase.Strafe) && TryFlank(dt))
                return;
            if (Phase != EnemyPhase.Chase && Phase != EnemyPhase.Strafe)
                StopFlanking();

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
                    if (playerDown)
                        CancelWindup();
                    else
                        TickWindup(dt);
                    break;

                case EnemyPhase.Attack:
                    Strike(distance);
                    break;

                case EnemyPhase.Recover:
                    _punchVisualTimer = Mathf.Max(0f, _punchVisualTimer - dt);
                    if (IsHolding && Player != null)
                    {
                        var hold = Player.GetComponent<PlayerGrapple>();
                        if (hold != null && hold.IsGrappled && hold.Captor == this)
                            break;
                        IsHolding = false;
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
            Vector3 tangent = Vector3.zero;

            float error = distance - desired;
            Vector3 correction = radial * Mathf.Clamp(error * 2.5f, -1f, 1f);

            Vector3 direction = tangent + correction + Separation();
            if (direction.sqrMagnitude > 0.0001f)
                Move(direction.normalized, MoveSpeed * StrafeSpeedScale);

            if (_timer <= 0f)
                EnterStrafe();
        }

        void EnterStrafe()
        {
            Phase = EnemyPhase.Strafe;

            // 待機中は左右に分かれて立つ（全員が同じ側に固まらないように）
            int index = Group != null ? Group.IndexOf(this) : -1;
            if (index >= 0)
                _strafeSign = index % 2 == 0 ? 1f : -1f;
            else
                _strafeSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;

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
                if (!_feinted && FeintChance > 0f && UnityEngine.Random.value < FeintChance)
                {
                    _feinted = true;
                    HideTelegraph();
                    WindupProgress = 0f;
                    _attackReadyAt = Time.time + Mathf.Max(0.1f, FeintDelaySeconds);
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
                        grabbed = TryGrab();
                    else if (PunchTouchesPlayer())
                        Player.ReceiveHit(State, _run.Data, transform);
                }
            }

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
        bool PunchTouchesPlayer()
        {
            var figure = GetComponent<EnemyFigure>();
            var fist = figure != null ? figure.Fist(false) : null;
            if (fist == null || Player == null) return false;
            var rig = Player.GetComponentInChildren<ProceduralRig>();
            Vector3 center = rig != null && rig.Ready && rig.Chest != null
                ? rig.Chest.position : Player.transform.position + Vector3.up * 1.05f;
            Vector3 toPlayer = Player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (Vector3.Dot(transform.forward, toPlayer) <= 0f) return false;
            float gap = Vector3.Distance(fist.position, center);
            return gap <= 0.38f;
        }

        /// <summary>
        /// 掴みが当たったとき。プレイヤーを掴んだまま硬直を伸ばし、
        /// 振りほどかれる（=こちらがよろける）か投げる（=相手が怯む）まで待つ。
        /// </summary>
        bool TryGrab()
        {
            var grapple = Player != null ? Player.GetComponent<PlayerGrapple>() : null;
            if (grapple == null || grapple.IsGrappled)
                return false;

            // 正面から掴めば振りほどきやすいが、背中側から掴まれると力が入らず逃げにくい。
            // 見た目は同じ掴みなので、向きだけで難易度が変わる（HUD に「後ろから」と出す）。
            GrappleKind kind = GrappleKind.Front;

            if (Player != null)
            {
                Vector3 toCaptor = transform.position - Player.transform.position;
                toCaptor.y = 0f;

                if (toCaptor.sqrMagnitude > 0.0001f
                    && Vector3.Dot(Player.transform.forward, toCaptor.normalized) < -0.35f)
                {
                    kind = GrappleKind.Back;
                }
            }

            grapple.Begin(this, kind);
            return true;
        }

        /// <summary>外部から怯ませる（振りほどかれた時など）。</summary>
        public void ForceStagger(float seconds)
        {
            if (Phase == EnemyPhase.Dead)
                return;

            HideTelegraph();
            _riseTimer = 0f;
            _swingsWithToken = 0;
            IsHolding = false;
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
            // 1回殴っただけで番を離れると、毎回「下がって出直す」動きになって落ち着かない。
            // 2回続けて振ってから交代する。
            _swingsWithToken++;
            if (_swingsWithToken >= 2)
            {
                _swingsWithToken = 0;
                ReleaseToken();
            }

            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            IsHolding = false;
            Phase = EnemyPhase.Chase;
        }

        /// <summary>プレイヤーが倒れた等で予備動作を中断する。硬直は挟まない。</summary>
        void CancelWindup()
        {
            HideTelegraph();
            _swingsWithToken = 0;
            ReleaseToken();
            _attackReadyAt = Time.time + Mathf.Max(0f, AttackCooldown);
            Phase = EnemyPhase.Chase;
        }

        // --- 攻撃の抽選 ----------------------------------------------------------

        bool TryBeginAttack(float distance)
        {
            if (Player == null || Player.IsDead)
                return false;

            // 回り込み直後は反対を向いていることがある。予備動作中は旋回を
            // 固定するため、向き直る前に攻撃を始めると必ず空振りになる。
            Vector3 facingTarget = Player.transform.position - transform.position;
            facingTarget.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (facingTarget.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(facing.normalized, facingTarget.normalized) < 0.9f)
                return false;

            // 掴まれている間は、掴んでいる本人以外は新しい攻撃を始めない。
            // 3 秒間で 3 人に袋叩きにされると、1 回の掴みで HP が半分飛んでしまう。
            if (_grapple == null)
                _grapple = Player.GetComponent<PlayerGrapple>();


            if (Time.time < _attackReadyAt)
                return false;

            AttackId id;
            bool force = _forceGrab;

            bool fromBehind = Vector3.Dot(Player.transform.forward, transform.position - Player.transform.position) < -0.25f;
            if (_grapple != null && !_grapple.IsGrappled && fromBehind && distance <= RangeOf(AttackId.Grab)
                && Time.time >= _grabReadyAt && (!_hasLast || _lastAttack != AttackId.Grab)
                && UnityEngine.Random.value < 0.65f)
                id = AttackId.Grab;
            else if (force && distance <= RangeOf(AttackId.Grab))
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
            return true;
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
            _swingsWithToken = 0;
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
                StaggerTier.GuardBreak => 2.2f,
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
                case StaggerTier.GuardBreak:
                    return StaggerSecondsGuardBreak > 0f ? StaggerSecondsGuardBreak : CombatConstants.StunGuardBreak;
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
            _swingsWithToken = 0;
            _run = default;
            _feinted = false;
            WindupProgress = 0f;
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
            if (Phase == EnemyPhase.Dead || IsHolding)
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
