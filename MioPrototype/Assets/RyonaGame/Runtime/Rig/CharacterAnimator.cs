using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 手続き的モーション。アニメーションクリップを使わず、ボーンを直接動かす。
    ///
    /// 状態: 立ち / 歩き / 怯み(4段) / ダウン / 攻撃
    /// すべて Rig の「向き指定」と「2ボーンIK」だけで組んである。
    /// </summary>
    [AddComponentMenu("Ryona/Character Animator")]
    [DefaultExecutionOrder(20000)]
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        public enum State
        {
            Idle = 0,
            Walk = 1,
            Stagger = 2,
            Down = 3,
            Attack = 4,
            Guard = 5,
            GetUp = 6,

            /// <summary>敵に掴まれてもがいている。移動も攻撃もできない。</summary>
            Grabbed = 7
        }

        [Header("References")]
        public ProceduralRig Rig;
        public PlayerStats Stats;
        public PlayerCombat Combat;
        public PlayerMotor Motor;

        /// <summary>掴まれているかを見る。未設定なら Bind で探す。</summary>
        public PlayerGrapple Grapple;

        /// <summary>
        /// 歩行で足が接地した瞬間。(0=左, 1=右)
        /// 足音をアニメーションに合わせるために使う（速度を変えても音がずれない）。
        /// </summary>
        public event System.Action<int> Footstep;

        /// <summary>実際の移動速度（足音の強さに使う）。</summary>
        public float PlanarSpeed => _planarSpeed;

        [Tooltip("Combat が無いとき（プレビュー）に使う攻撃データ")]
        public AttackData AttackOverride;

        /// <summary>
        /// もがきの脈（0〜1）をプレビューで直接入れる。0 未満で無効（実際の Grapple を見る）。
        /// 本番ではレバガチャの入力で 1 まで上がり、時間で減る。
        /// </summary>
        [System.NonSerialized] public float StrugglePulseOverride = -1f;

        [Header("Walk")]
        public float StrideLength = 0.42f;
        public float StepHeight = 0.075f;
        public float ArmSwing = 0.85f;

        [Header("Blend")]
        [Tooltip("状態が切り替わったとき、前の姿勢から寄せる秒数")]
        public float IdleBlendSeconds = 0.16f;
        public float AttackBlendSeconds = 0.06f;
        public float DownBlendSeconds = 0.10f;

        [Header("Guard")]
        [Tooltip("ガードを出してから構え終わるまで")]
        public float GuardBlendSeconds = 0.09f;

        [Tooltip("ガード中に殴られたとき、押し返される演出の長さ")]
        public float GuardRecoilSeconds = 0.18f;

        [Header("Down Pose (倒れたときの腕)")]
        [Tooltip("仰向けに倒れたときの左腕の向き。立ち姿勢のローカル方向（-Y が下）")]
        public Vector3 DownArmLUpper = new Vector3(0.85f, -0.50f, -0.25f);
        public Vector3 DownArmLFore = new Vector3(0.60f, -0.30f, -0.55f);
        public Vector3 DownArmRUpper = new Vector3(-0.85f, -0.50f, 0.25f);
        public Vector3 DownArmRFore = new Vector3(-0.60f, -0.30f, 0.55f);

        [Header("Stagger")]
        public float LightBend = 22f;
        public float MediumBend = 42f;
        public float HeavyBend = 60f;

        [Header("Get Up")]
        [Tooltip("倒れてから立ち上がり終わるまでの秒数")]
        public float GetUpSeconds = 0.80f;

        public State Current => _state;
        public float StateTime => _stateTime;
        public float StateDuration => _stateDuration;

        // 腹パン演出（後ろ拘束中に別の敵の腹パンチを受けたとき）の残り時間。
        float _heldBellyPunchRemaining;
        float _heldBellyFatigue;
        float _heldBellyCarry;
        float _heldBellyPunchTotal;

        float _heldFacePunchRemaining;
        float _heldFacePunchTotal;

        // 投げられて、正面の敵に腹を差し出す体勢への移行（0..1）。
        // パンチの「く」の字とは別物で、折れはしない。コンボのあいだ保持する。
        bool _heldThrowLeadOn;
        float _heldThrowLead;

        /// <summary>いま腹パンの「く」の字オーバーレイ中か。</summary>
        public bool IsHeldBellyPunch => _heldBellyPunchRemaining > 0f;

        /// <summary>いま拘束中の顔面パンチ（首が手前へ振れる）オーバーレイ中か。</summary>
        public bool IsHeldFacePunch => _heldFacePunchRemaining > 0f;

        /// <summary>
        /// 投げられて、腹を差し出す体勢に入っている最中か（コンボのあいだ保持）。
        /// 食らいの姿勢ではないので、折れ・怯み・痛み表情・ヒットストップを伴わない。
        /// </summary>
        public bool IsHeldThrowLead => _heldThrowLeadOn || _heldThrowLead > 0.001f;

        /// <summary>いま投げの体勢移行がどこまで入っているか（0..1）。</summary>
        public float HeldThrowLead => _heldThrowLead;

        /// <summary>
        /// 拘束中に顔面を殴られたときの首の振れ（0..1）。入り 0.05 秒、保持、戻り 0.22 秒。
        /// 通常の顔面パンチ（<see cref="ApplyFaceFlick"/>）と同じ向き＝カメラ側へ顔を背ける。
        /// テストと Editor 診断が数値で確認できるように公開している。
        /// </summary>
        public float HeldFacePunchFlick
        {
            get
            {
                if (_heldFacePunchTotal <= 0f || _heldFacePunchRemaining <= 0f)
                    return 0f;

                float t = _heldFacePunchTotal - _heldFacePunchRemaining;
                float inT = Mathf.SmoothStep(0f, 1f, t / 0.05f);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.20f) / 0.22f);
                return Mathf.Clamp01(inT * outT);
            }
        }

        /// <summary>
        /// 拘束中に顔面を殴られた反応。腹パンの「く」の字とは別で、上体は折らずに
        /// 首だけ手前（カメラ側）へ振る。呼んでも状態は Grabbed のまま。
        /// </summary>
        public void PlayHeldFacePunch(float seconds)
        {
            if (seconds <= 0f)
                return;

            _heldFacePunchTotal = seconds;
            _heldFacePunchRemaining = seconds;
        }

        /// <summary>
        /// 正面の敵に投げられ、腹を差し出す体勢へ移る。上体は折らず、腰を引いて腹を前へ出す。
        /// <see cref="EndHeldThrowLead"/> を呼ぶまで保持する。
        /// ダメージの反応ではないので、表情は <see cref="PlayerFace"/> に任せる。
        /// </summary>
        public void PlayHeldThrowLead()
        {
            _heldThrowLeadOn = true;
        }

        /// <summary>投げの体勢移行を解除する（コンボ終了／拘束が解けた）。</summary>
        public void EndHeldThrowLead()
        {
            _heldThrowLeadOn = false;
        }

        /// <summary>
        /// 腹パンの被弾姿勢を再生する。拘束（Grabbed）の上に重ねるので、
        /// 呼んでも状態は Grabbed のまま。
        /// </summary>
        public void PlayHeldBellyPunch(float seconds, float fatigue = 0f)
        {
            if (seconds <= 0f)
                return;

            _heldBellyCarry = HeldBellyPunchFold;
            _heldBellyFatigue = Mathf.Clamp01(fatigue);
            _heldBellyPunchTotal = seconds;
            _heldBellyPunchRemaining = seconds;
        }

        /// <summary>
        /// 腹パンの反動の強さ（0..1）。素早く押し戻され、時間をかけて立て直す。
        /// テストと Editor 診断が姿勢を数値で確認するために公開している。
        /// </summary>
        public float HeldBellyPunchFold
        {
            get
            {
                if (_heldBellyPunchTotal <= 0f || _heldBellyPunchRemaining <= 0f)
                    return 0f;

                float t = _heldBellyPunchTotal - _heldBellyPunchRemaining;
                float inT = Mathf.SmoothStep(_heldBellyCarry, 1f, t / 0.055f);
                float recoverAt = _heldBellyPunchTotal * Mathf.Lerp(0.26f, 0.72f, _heldBellyFatigue);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recoverAt, _heldBellyPunchTotal, t));
                return Mathf.Clamp01(inT * outT);
            }
        }

        internal float HeldBellyCaptorLift
        {
            get
            {
                if (_heldBellyPunchRemaining <= 0f || _heldBellyFatigue < 0.6f)
                    return 0f;
                float progress = 1f - _heldBellyPunchRemaining / _heldBellyPunchTotal;
                float pull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.49f, 0.72f, progress));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.81f, 1f, progress));
                return pull * (1f - release);
            }
        }

        /// <summary>倒れている（立ち上がり中を含む）。移動できない区間。</summary>
        public bool IsDowned => _state == State.Down || _state == State.GetUp;

        State _state = State.Idle;
        float _stateTime;
        float _stateDuration = 1f;
        StaggerTier _tier = StaggerTier.None;

        // どこから殴られたか（0=正面, ±1=左右, 2=背後）。怯みの向きを変える。
        int _hurtSide;

        // 顔を殴られた時刻。怯みの長さ（軽い一撃は 0.2 秒）に関係なく、
        // 顔がカメラ側へ弾かれて、少し止まり、ゆっくり戻る動きを最後まで見せるために持つ。
        float _faceFlickAt = -10f;

        // この振れについて、効き始めの角度をまだログっていないか（1 回だけ測る）。
        bool _faceFlickLogged = true;

        // 怯み姿勢を出し終えたスタンは、もう一度怯みに入れない。
        // ヒットストップ中はコア側のスタン残りが止まり、姿勢だけ先に終わる。
        // 残りスタンでフォールバックが再突入すると、1 回の被弾で 2 回のけぞって見える。
        bool _stunPlayed;

        // 疲労度（0=平気, 1=息が上がりきっている）。待機の呼吸と構えに効く。
        float _tired;

        // 検証シートで疲労度を固定している間は true（勝手に戻さない）。
        bool _tiredHold;

        // 倒れきったときの腰の位置。立ち上がりの開始位置をここから取る
        // （固定値で始めると、倒れた位置と立ち上がりの初動がズレて 1 フレーム跳ねる）。
        float _downEndHipsY = -0.84f;
        float _downEndHipsZ = -0.87f;

        // 振りほどいた直後に相手を突き放す残り時間。
        const float ShoveSeconds = 0.28f;

        // 倒れきるまでの秒数。ダウンも死亡も同じ速さで倒す
        // （以前は死亡だけ 0.1 秒のクロスフェードで瞬間的に倒れていた）。
        const float DownFallSeconds = 0.95f;

        /// <summary>このダウンで倒れきるまでの秒（吹き飛ばされるときは短くなる）。</summary>
        float _downFallSeconds = DownFallSeconds;

        /// <summary>このダウンが「吹き飛ばされ」か（空中の姿勢を足すのに使う）。</summary>
        bool _downLaunched;

        /// <summary>このダウンが「前へ崩れる」か（腹を殴られてうつ伏せになる）。</summary>
        bool _downForward;

        /// <summary>倒れきったときの体の角度（起き上がりはここから始める）。</summary>
        float _downEndLay = -92f;

        /// <summary>
        /// 空中にいる度合い（1=空中）。着地後に 0 へ戻す。
        /// 空中で体を倒しきると「板のように飛んでいく」画になるので、
        /// 空中は傾きを抑え、着地してから水平にする。
        /// </summary>
        float _airBlend;

        float _shoveTime;
        float _walkPhase;

        /// <summary>体から見た進行方向（x=横、y=前）。横歩き・後退りでも足を進行方向へ運ぶために使う。</summary>
        Vector2 _walkDir = new Vector2(0f, 1f);

        /// <summary>
        /// 検証用。歩く向きを固定する（<c>(0,1)</c>=前進、<c>(1,0)</c>=右への横歩き）。
        /// null なら実際の移動から作る。プレビューは動かないので、これで向きを指定する。
        /// </summary>
        public static Vector2? WalkDirectionOverride;

        /// <summary>いま足を運ぶ向き。検証用の上書きがあればそちらを使う。</summary>
        Vector2 WalkDir => WalkDirectionOverride ?? _walkDir;
        float _blend = 1f;
        float _blendSeconds = 0.16f;
        Vector3 _lastPosition;
        float _planarSpeed;
        float _lastWalkU = -1f;
        bool _bound;
        float _guardRecoil;
        bool _hasFaceYaw;
        Quaternion _faceYaw;

        // 攻撃の出始めに相手へ向き直るための状態（ソフトオートエイム）
        bool _hasAim;
        Quaternion _aimStart;
        Quaternion _aimYaw;
        float _aimTime;

        static readonly Vector3 ArmIdleUpper = new Vector3(0.30f, -1f, 0.02f);
        static readonly Vector3 ArmIdleFore = new Vector3(0.10f, -1f, 0.30f);

        // 構えの腕。Idle と、そこから崩れる Stagger で共有する。
        // 構えの腕。拳を顔の前に上げるファイティングポーズではなく、
        // 肘を軽く曲げて腕を下ろしただけの、力の抜けた構え（拳は腰〜腹の高さ）。
        public static Vector3 StanceArmLUpper = new Vector3(0.18f, -0.90f, 0.26f);
        public static Vector3 StanceArmLFore = new Vector3(0.00f, -0.10f, 0.99f);
        public static Vector3 StanceArmRUpper = new Vector3(-0.18f, -0.92f, 0.18f);
        public static Vector3 StanceArmRFore = new Vector3(0.00f, -0.05f, 0.99f);
        /// <summary>
        /// 構えの手首のひねり。0 = 前腕と手がまっすぐ繋がる向き。
        /// 0 のままだと掌が外向き（左手は後ろ、右手は前）で左右が揃わず、拳が「猫の手」に見える。
        /// 掌が体の中心を向く角度を、構えの姿勢から実測して決めた値。
        /// </summary>
        public static float StanceHandRollL = 45f;
        public static float StanceHandRollR = -48f;

        /// <summary>
        /// 3段コンボの何段目か（0 = 右で打つ, 1 = 左で打つ, 2 = 強）。
        /// 攻撃開始時に <see cref="PlayerCombat"/> が書く。姿勢の左右反転にだけ使う。
        /// </summary>
        [System.NonSerialized] public int ComboStep;

        /// <summary>腕のベクトルは X が左右なので、反転は X の符号だけ落とせばよい。</summary>
        static Vector3 MirrorX(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        // 構えの「その他の値」。当たりモーションはここから崩す（0 から作ると
        // ヒットの瞬間に足がそろって立ち位置が飛ぶ）。
        const float StanceHipsY = -0.085f;
        const float StanceHipsZ = 0.005f;
        const float StanceTorsoPitch = 5f;
        const float StanceTorsoYaw = -12f;
        const float StanceHeadYaw = 10f;
        const float StanceHeadPitch = 1f;
        static readonly Vector3 StanceFootL = new Vector3(-0.05f, 0f, 0.11f);
        static readonly Vector3 StanceFootR = new Vector3(0.05f, 0f, -0.14f);

        Vector3 _leftOut = new Vector3(-1f, 0f, 0f);
        Vector3 _rightOut = new Vector3(1f, 0f, 0f);
        bool _axesReady;

        /// <summary>
        /// 「腕を外へ開く向き」を骨の実際の位置から求める。
        /// モデルによって左右が +X のことも -X のこともあるので、決め打ちにしない。
        /// </summary>
        void CacheArmAxes()
        {
            if (Rig == null || Rig.Hips == null || Rig.LUpperArm == null || Rig.RUpperArm == null)
                return;

            // ワールドの向きで覚えると、被弾で体が回ったときに「外へ」が付いてこず、
            // 腕だけ元の向きを向いたままになる。キャラクターのローカルに直して覚える。
            _leftOut = Horizontal(
                transform.InverseTransformDirection(Rig.LUpperArm.position - Rig.Hips.position), Vector3.left);
            _rightOut = Horizontal(
                transform.InverseTransformDirection(Rig.RUpperArm.position - Rig.Hips.position), Vector3.right);
            _axesReady = true;
        }

        static Vector3 Horizontal(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? fallback : v.normalized;
        }

        /// <summary>
        /// 仕様 (開き, 高さ, 前後) を、このモデルの実際の向きに変換する。
        /// x は必ず「外へ」の大きさとして扱う。
        /// </summary>
        Vector3 ArmDirection(bool left, Vector3 spec)
        {
            if (!_axesReady)
                CacheArmAxes();

            Vector3 side = transform.TransformDirection(left ? _leftOut : _rightOut);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;

            Vector3 dir = side * Mathf.Abs(spec.x) + Vector3.up * spec.y + forward * spec.z;

            // 腕は肩の面に付いている。体幹がねじれる・傾くと肩も一緒に回るので、
            // 腕の狙いも同じだけ回す。これをやらないと、強くねじる姿勢（のけぞり・
            // 振りかぶり）で「肩から先だけ別の場所にある」画になる。
            // BendTorso は Spine に yaw/pitch/roll を、Chest にその 0.4 倍を掛けるので、
            // 肩の面は合わせて 1.4 倍回っている。
            const float shoulderShare = 1.4f;

            // 攻撃だけ追従を弱める。攻撃の腕は「踏み込んで拳を相手へ出す」形として
            // すでに体のひねり込みで作ってあるので、肩の面に合わせて回すと
            // 拳が体の前へ回り込んで伸びが減る（当てにいっている画にならない）。
            float share = _state == State.Attack ? 0.35f : 1f;

            Quaternion torso =
                Quaternion.AngleAxis(_armFollowYaw * shoulderShare * share, Vector3.up) *
                Quaternion.AngleAxis(_armFollowPitch * shoulderShare * share, Vector3.right) *
                Quaternion.AngleAxis(_armFollowRoll * shoulderShare * share, Vector3.forward);

            // BendTorso と同じく、軸はキャラクターの向きを基準に取る。
            Quaternion basis = transform.rotation;
            dir = basis * torso * Quaternion.Inverse(basis) * dir;

            return dir;
        }

        // このフレームの体幹角度。腕の狙いを肩の面に合わせるのに使う。
        float _armFollowYaw;
        float _armFollowPitch;
        float _armFollowRoll;

        /// <summary>検証用: 直近の ApplyPose で SetArm に渡した向き（ワールド）。</summary>
        [System.NonSerialized] public Vector3 DebugArmLUpper;
        [System.NonSerialized] public Vector3 DebugArmLFore;
        [System.NonSerialized] public Vector3 DebugArmRUpper;
        [System.NonSerialized] public Vector3 DebugArmRFore;
        [System.NonSerialized] public float DebugTorsoPitch;
        [System.NonSerialized] public float DebugHeadPitch;
        [System.NonSerialized] public float DebugTorsoYaw;
        [System.NonSerialized] public float DebugHeadYaw;

        /// <summary>
        /// プレビュー専用: 被弾の場所（顔 / 腹）。設定すると <see cref="Stats"/> より優先する。
        /// Editor の診断や PlayMode テストはこの経路で顔 / 腹の分岐を確かめる
        /// （実機では常に null なので、ふだんは <see cref="PlayerStats.LastHitZone"/> を見る）。
        /// </summary>
        [System.NonSerialized] public PunchZone? PreviewHitZone;

        /// <summary>
        /// いま使っている被弾の場所。プレビュー / テストで指定があればそれを優先し、
        /// 無ければ実機の <see cref="PlayerStats.LastHitZone"/>（投げなどでは null）。
        /// </summary>
        public PunchZone? CurrentHitZone =>
            PreviewHitZone ?? (Stats != null ? Stats.LastHitZone : null);

        void Awake()
        {
            if (Rig == null)
                Rig = GetComponent<ProceduralRig>();
            if (Rig == null)
                Rig = gameObject.AddComponent<ProceduralRig>();
        }

        void Start()
        {
            if (Rig != null && !Rig.Ready)
                Rig.Capture();

            Bind();

            // 読み込み直後の 1 フレームだけ T ポーズが見えるのを防ぐ
            ApplyPose();
        }

        void OnDestroy()
        {
            if (_bound && Stats != null)
            {
                Stats.Staggered -= OnStaggered;
                Stats.HitReceived -= OnHitReceived;
                Stats.Revived -= OnRevived;

                if (Grapple != null)
                {
                    Grapple.Escaped -= OnGrappleEscaped;
                    Grapple.Released -= OnGrappleReleased;
                }
            }
        }

        void Bind()
        {
            if (_bound || Stats == null)
                return;

            // 踏み込みで root が前へ出るので、その分だけ足を残すために必要。
            if (Motor == null)
                Motor = GetComponentInParent<PlayerMotor>();

            if (Grapple == null)
                Grapple = GetComponentInParent<PlayerGrapple>();

            // VRM 側の animator は VrmCharacterSlot が追加するので、Combat が未設定のまま。
            // ここで拾わないと Combat.IsAttacking が常に false になり、
            // State.Attack に入らず「攻撃の判定だけ出て拳が動かない」状態になる。
            if (Combat == null)
                Combat = GetComponentInParent<PlayerCombat>();

            Stats.Staggered += OnStaggered;
            Stats.HitReceived += OnHitReceived;
            Stats.Revived += OnRevived;

            if (Grapple != null)
            {
                Grapple.Escaped += OnGrappleEscaped;
                Grapple.Released += OnGrappleReleased;
            }

            _bound = true;
        }

        void OnRevived()
        {
            // HP 0 の倒れ姿勢を切り上げ、次のフレームで通常の起き上がりに入る。
            if (_state == State.Down)
                _stateDuration = 0f;
        }

        /// <summary>
        /// もがいて振りほどけた瞬間、両手で相手を突き放す。
        /// 振りほどいた直後に構えへ戻るだけだと「するりと抜けた」ように見える。
        /// </summary>
        void OnGrappleEscaped()
        {
            _shoveTime = ShoveSeconds;
        }

        /// <summary>
        /// 時間切れなどで拘束が解けた瞬間、両手で押し返してから構えへ戻る。
        /// いきなり Idle へ戻すと「するりと抜けた」ように見える。
        /// </summary>
        void OnGrappleReleased()
        {
            _shoveTime = ShoveSeconds;
        }

        /// <summary>直前フレームの拘束の種類。引き渡しで姿勢が切り替わった瞬間を検出する。</summary>
        GrappleKind? _lastGrappleKind;
        bool _wasHandingOff;

        /// <summary>
        /// ガード中に殴られたら、腕ごと押し戻される。
        /// 姿勢が変わらないと「受け止めた」感触が出ないため、姿勢側で少しだけ動かす。
        /// </summary>
        void OnHitReceived(HitResult result)
        {
            if (_state == State.Guard)
                _guardRecoil = Mathf.Max(_guardRecoil, GuardRecoilSeconds);
        }

        void OnStaggered(HitResult result)
        {
            // 拘束中もダメージは受けるが、倒れるまでは拘束姿勢を維持する。
            if (Grapple != null && Grapple.IsGrappled && Stats != null && !Stats.IsDead)
                return;
            _tier = result.Stagger;
            _stunPlayed = true;
            CaptureFaceYaw();
            CaptureHurtSide();

            if (CurrentHitZone == PunchZone.Face && _hurtSide != 2)
            {
                _faceFlickAt = Time.time;
                _faceFlickLogged = false;
            }

            // どの向きから・どの場所を殴られたか。見た目の分岐が効いているかを数字で確かめる
            // （<c>-statlog</c> のときだけ。既定は黙る）。
            if (PlayerStats.StatLogging)
            {
                string zone = CurrentHitZone?.ToString() ?? "none";
                Debug.Log($"[Hurt] zone={zone} tier={result.Stagger} " +
                    $"side={_hurtSide} t={Time.time:F2}");
            }

            // ひるみの蓄積はダウン（倒れる）。
            // 倒れている時間は「ダウンの合計 − 起き上がり」で、合計が DownDuration と揃う。
            if (Stats != null && Stats.State != null && Stats.IsDown)
            {
                SetState(State.Down, Mathf.Max(0.4f, Stats.DownRemaining - GetUpSeconds));
                return;
            }

            SetState(State.Stagger, Mathf.Max(0.12f, result.StunSeconds));
        }

        /// <summary>
        /// 殴ってきた相手の方向を覚えておく。
        /// 攻撃はキャラクターの forward へ出るので、VRM の見た目だけ回すと
        /// 「前を向いているのに攻撃が後ろへ飛ぶ」ことになる。体（root）ごと回す。
        /// </summary>
        /// <summary>
        /// どこから殴られたかを覚えておく（0=正面, ±1=左右, 2=背後）。
        /// 正面からの一撃だけを想定すると、後ろから殴られても「前へ反る」不自然な画になる。
        /// </summary>
        void CaptureHurtSide()
        {
            _hurtSide = 0;

            Transform src = Stats != null ? Stats.LastAttacker : null;
            if (src == null)
                return;

            Vector3 d = src.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f)
                return;

            Vector3 local = transform.InverseTransformDirection(d.normalized);

            // 後方 40° 以内は「背中から」とみなす
            if (local.z < -0.75f)
                _hurtSide = 2;
            else if (Mathf.Abs(local.x) > 0.35f)
                _hurtSide = local.x > 0f ? 1 : -1;
        }

        /// <summary>
        /// 顔を殴られたあとの首の動き。カメラのほうへ顔が弾かれて（0.05 秒）、
        /// しばらく横を向いたまま耐え、ゆっくり正面へ戻る（合計約 0.95 秒）。
        /// 怯みの長さとは別に時間を持つので、軽い一撃でも動きが最後まで見える。
        /// </summary>
        void ApplyFaceFlick(ref float pitch, ref float yaw, ref float roll)
        {
            float t = Time.time - _faceFlickAt;
            if (t < 0f || t > 0.95f)
                return;

            float env = Mathf.SmoothStep(0f, 1f, t / 0.05f)
                        * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.50f));

            float toCamera = CameraYawToMe();

            yaw = Mathf.Lerp(yaw, StanceHeadYaw + toCamera * 0.95f, env);
            roll += -Mathf.Sign(toCamera == 0f ? 1f : toCamera) * 10f * env;
            pitch -= 6f * env;

            // 振れきった瞬間の角度を 1 回だけ残す（見た目が「横を向いている」かを数字で確かめる）。
            if (!_faceFlickLogged && t >= 0.10f)
            {
                _faceFlickLogged = true;
                if (PlayerStats.StatLogging)
                    Debug.Log($"[FaceFlick] toCam={toCamera:F0} yaw={yaw:F0} " +
                        $"pitch={pitch:F0} roll={roll:F0} t={t:F2}");
            }
        }

        // --- 後ろ拘束で、殴りに来る敵を不安そうに見る -------------------------------

        HeldBellyPunchDirector _punchDirector;

        /// <summary>殴りに来る敵を見る重み（0〜1）。拘束中に正面の敵が選ばれると上がる。</summary>
        float _dreadLook;

        EnemyBrain _dreadEnemy;
        float _dreadYaw;
        float _dreadPitch;
        bool _hasDreadAngles;

        /// <summary>前のフレームで姿勢を組んだ後の頭の位置（ResetPose 前の値）。</summary>
        Vector3 _posedHeadPosition;
        bool _hasPosedHead;

        /// <summary>殴りに来る敵を見ている重み（検証用）。</summary>
        public float DreadLook => _dreadLook;

        void UpdateDreadLook(float dt)
        {
            if (_punchDirector == null)
                _punchDirector = FindFirstObjectByType<HeldBellyPunchDirector>();

            EnemyBrain target = _punchDirector != null && _punchDirector.isActiveAndEnabled
                && Grapple != null && Grapple.IsGrappled && !Grapple.IsHandingOff
                && Grapple.Kind == GrappleKind.Back
                ? _punchDirector.DreadEnemy
                : null;

            if (target != _dreadEnemy)
            {
                _dreadEnemy = target;
                _hasDreadAngles = false;
            }

            if (target != null && !_hasDreadAngles && Rig != null && Rig.Head != null)
                CaptureDreadAngles(_punchDirector.DreadStartPoint);

            bool bracing = target != null && target.IsTagPunch &&
                (target.Phase == EnemyPhase.Attack ||
                 (target.Phase == EnemyPhase.Windup &&
                  target.WindupProgress >= (target.IsOpeningBellyPunch ? 0.15f : 0.55f)));
            bool look = target != null && _hasDreadAngles && !bracing;
            // 拳が出る前だけ視線を外す。被弾姿勢から戻るときは最初の角度へ戻す。
            float seconds = look ? 0.18f : bracing ? 0.085f : 0.35f;
            _dreadLook = Mathf.MoveTowards(_dreadLook, look ? 1f : 0f, Mathf.Max(0f, dt) / seconds);
        }

        /// <summary>
        /// 殴りに来る敵の顔へ向く首の角度（体のローカル）。yaw は右 +、pitch は上 −。
        /// 上体の前傾ぶんは呼び出し側で差し引く。
        /// </summary>
        void CaptureDreadAngles(Vector3 point)
        {
            Vector3 to = point - (_hasPosedHead ? _posedHeadPosition : Rig.Head.position);
            Vector3 local = transform.InverseTransformDirection(to);
            float flat = new Vector2(local.x, local.z).magnitude;
            if (flat < 0.05f)
                return;

            _dreadYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -70f, 70f);
            _dreadPitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -45f, 25f);
            _hasDreadAngles = true;
        }

        /// <summary>
        /// カメラがどちら側にいるか（度）。正面 0、右 +、左 −。
        /// 顔を「手前（カメラ側）へ背ける」向きを出すのに使う。
        /// </summary>
        float CameraYawToMe()
        {
            var cam = Camera.main;
            if (cam == null)
                return 0f;

            Vector3 to = cam.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude <= 1e-4f)
                return 0f;

            Vector3 local = transform.InverseTransformDirection(to.normalized);
            return Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -80f, 80f);
        }

        void CaptureFaceYaw()
        {
            _hasFaceYaw = false;

            Transform src = Stats != null ? Stats.LastAttacker : null;
            if (src == null)
                return;

            Vector3 d = src.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f)
                return;

            if (Motor != null && Motor.BeltFacing)
                d = Vector3.right * (Mathf.Abs(d.x) > 0.01f ? Mathf.Sign(d.x) : Motor.LaneFacingSign);
            _faceYaw = Quaternion.LookRotation(d.normalized, Vector3.up);
            _hasFaceYaw = true;

            Debug.Log($"[Turn] hit angle={Vector3.Angle(transform.forward, d):F0} " +
                      $"src={src.name}");
        }

        void SetState(State state, float duration)
        {
            _state = state;
            _stateTime = 0f;
            _stateDuration = duration;

            if (state == State.Down)
            {
                // 吹き飛ばされて倒れるときは、倒れきる時刻を着地に合わせる。
                // 空中にいるのに「その場で崩れる」速さで倒れると、足が地面を滑って見える。
                _downForward = Stats != null && Stats.DownIsForward;
                _downLaunched = !_downForward && Motor != null && Motor.IsAirborne;

                // 吹き飛ばされるときは速く倒す。0.52 秒かけて回すと「ゆっくり傾いていく板」に見え、
                // 倒れるというより「倒されている途中を眺めている」画になる。0.32 秒で着地と同時に倒れきる。
                // 前へ崩れるときは腹を抱えて膝から落ちるので、少しだけ長く取る。
                _downFallSeconds = _downForward
                    ? DownFallSeconds * 0.79f
                    : (_downLaunched ? DownFallSeconds * 0.34f : DownFallSeconds);

                _airBlend = _downLaunched ? 1f : 0f;
                _downEndLay = _downForward ? 92f : -92f;
            }
        }

        /// <summary>
        /// 攻撃の出始めに体を相手へ向ける（ソフトオートエイム）。
        /// キーボードだけだと、立ち止まったままでは向きを変えられず、
        /// 「前を向いたまま横の敵を殴って空振り」になるため。
        /// 振り向く角度は maxDegrees で制限する（真後ろまでは回らない）。
        /// </summary>
        public void CancelAim()
        {
            _hasAim = false;
            _hasFaceYaw = false;
        }

        public void AimAt(Vector3 worldPoint, float maxDegrees)
        {
            Vector3 d = worldPoint - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0004f)
                return;

            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            Transform body = transform.root;

            _aimStart = body.rotation;
            _aimYaw = Quaternion.RotateTowards(body.rotation, want, Mathf.Max(0f, maxDegrees));
            _aimTime = 0f;

            float turn = Quaternion.Angle(_aimStart, _aimYaw);
            _hasAim = turn > 2f;

            if (_hasAim)
                Debug.Log($"[Aim] turn={turn:F0} angle={Vector3.Angle(transform.forward, d):F0}");
        }

        void LateUpdate()
        {
            if (Rig == null || !Rig.Ready)
                return;

            if (!_bound)
                Bind();

            float dt = Time.deltaTime;

            // ヒットストップ（timeScale=0）中でも、当たり・ダウンの姿勢だけは進める。
            // 止めている間に「当たる前の姿勢」のまま固まると、衝撃が 1 拍遅れて見える。
            // ただしポーズ画面・タイトルの timeScale=0 は「止める」場所なので進めない
            // （進めるとポーズ中に勝手に倒れて起き上がってしまう）。
            if (dt <= 0f && HitStop.IsActive
                && (_state == State.Stagger || _state == State.Down))
                dt = Time.unscaledDeltaTime;

            _stateTime += dt;

            if (_guardRecoil > 0f)
                _guardRecoil = Mathf.Max(0f, _guardRecoil - dt);

            if (_shoveTime > 0f)
                _shoveTime = Mathf.Max(0f, _shoveTime - dt);

            // 腹パンと顔パンの反動。ヒットストップ中は dt=0 なので、当たった瞬間の姿勢で止まる。
            _heldBellyPunchRemaining = Mathf.Max(0f, _heldBellyPunchRemaining - dt);
            if (Grapple != null && (!Grapple.IsGrappled || Grapple.IsHandingOff))
                _heldBellyPunchRemaining = 0f;
            _heldFacePunchRemaining = Mathf.Max(0f, _heldFacePunchRemaining - dt);

            // 投げの崩れ。引き込まれる動きなので、パンチの折れ（0.07 秒）よりゆっくり入れる。
            if (Grapple == null || !Grapple.IsGrappled)
                _heldThrowLeadOn = false;
            _heldThrowLead = Mathf.MoveTowards(_heldThrowLead, _heldThrowLeadOn ? 1f : 0f,
                dt / (_heldThrowLeadOn ? 0.10f : 0.16f));

            UpdateDreadLook(dt);

            // 実際の移動速度。足を地面に固定する（滑らせない）ために使う。
            Vector3 move = transform.position - _lastPosition;
            _lastPosition = transform.position;
            if (dt > 1e-4f)
            {
                float instant = new Vector3(move.x, 0f, move.z).magnitude / dt;

                // 0.3 を直接掛けるとフレームレートで平滑の強さが変わり、歩行周期そのものが
                // 変わる（60fps と 144fps で足の回る速さが違う）。経過時間から係数を作る。
                _planarSpeed = Mathf.Lerp(_planarSpeed, instant, 1f - Mathf.Exp(-18f * dt));

                // 体から見た進行方向。横歩きでも足を進行方向へ運ぶため（敵の EnemyFigure と同じ考え方）。
                // 向きは体の回転で変わるので、世界の移動量を体のローカルへ直してから混ぜる。
                Vector3 flatMove = new Vector3(move.x, 0f, move.z);
                if (WalkDirectionOverride.HasValue)
                {
                    _walkDir = WalkDirectionOverride.Value;
                }
                else if (flatMove.sqrMagnitude > 1e-8f)
                {
                    Vector3 local = transform.InverseTransformDirection(flatMove);
                    Vector2 wanted = new Vector2(local.x, local.z).normalized;
                    Vector2 blended = Vector2.Lerp(_walkDir, wanted, 1f - Mathf.Exp(-12f * dt));
                    if (blended.sqrMagnitude > 1e-6f)
                        _walkDir = blended.normalized;
                }
            }

            State before = _state;
            bool held = Grapple != null && Grapple.IsGrappled && (Stats == null || !Stats.IsDead);

            if (held)
            {
                // 掴まれる前に始まった被弾の振り向きや攻撃の照準を拘束へ持ち越さない。
                _hasFaceYaw = false;
                _hasAim = false;
                if (_state != State.Grabbed)
                {
                    _faceFlickAt = -10f;
                    SetState(State.Grabbed, 0f);
                }
            }

            if (_stunPlayed && Stats != null && !Stats.IsStunned)
                _stunPlayed = false;

            // 被弾イベント以外から倒れる経路（掴みが解けて崩れる等）でもダウンに入る。
            // `Stats.IsDown` は立つのに、姿勢が Idle のままという取りこぼしを防ぐ。
            // 残りが起き上がり時間ぶんしか無いときは入らない（GetUp 後にまた Down へ戻るのを防ぐ）。
            if (Stats != null && Stats.State != null && Stats.State.IsDown && !Stats.IsDead
                && Stats.DownRemaining > GetUpSeconds + 0.05f
                && _state != State.Down && _state != State.GetUp
                && !(Grapple != null && Grapple.IsGrappled))
            {
                SetState(State.Down, Mathf.Max(0.4f, Stats.DownRemaining - GetUpSeconds));
            }

            switch (_state)
            {
                case State.Stagger:
                    if (_stateTime >= _stateDuration)
                        _state = State.Idle;
                    break;

                case State.Down:
                    // 時間で終わるのが基本。ただしコア状態（残り秒）が先に尽きたら、
                    // 倒れたまま動ける状態にならないよう、こちらも合わせて起き上がる。
                    if (_stateTime >= _stateDuration
                        || (Stats != null && Stats.DownRemaining > 0f
                            && Stats.DownRemaining <= GetUpSeconds))
                    {
                        // 倒れたまま起き上がる。起き上がり中は動けない
                        SetState(State.GetUp, Mathf.Max(0.2f, GetUpSeconds));
                        Debug.Log($"[Anim] ダウン終了 → 立ち上がり {GetUpSeconds:F2}s");
                        if (Stats != null)
                            Stats.AddStun(Mathf.Max(0.05f, GetUpSeconds - 0.02f));
                    }
                    break;

                case State.GetUp:
                    if (_stateTime >= _stateDuration)
                    {
                        Debug.Log("[Anim] 立ち上がり完了 → 構え");
                        _state = State.Idle;
                    }
                    break;

                case State.Attack:
                    if (Combat == null || !Combat.IsAttacking)
                        _state = State.Idle;
                    break;

                case State.Grabbed:
                    if (Grapple == null || !Grapple.IsGrappled)
                    {
                        if (Stats != null && Stats.IsDead)
                            SetState(State.Down, 999f);
                        else if (Stats != null && Stats.IsStunned)
                            SetState(State.Stagger, Mathf.Max(0.1f, Stats.StunRemaining));
                        else
                            _state = State.Idle;
                    }
                    break;

                default:
                    if (Stats != null && Stats.IsDead)
                    {
                        SetState(State.Down, 999f);
                    }
                    else if (Combat != null && Combat.IsAttacking)
                    {
                        SetState(State.Attack, 0f);
                    }
                    else if (Stats != null && Stats.IsStunned && !_stunPlayed)
                    {
                        // イベントを取りこぼしても怯み姿勢は出す
                        SetState(State.Stagger, Mathf.Max(0.1f, Stats.StunRemaining));
                    }
                    else
                    {
                        _state = Stats != null && Stats.IsMoving ? State.Walk : State.Idle;
                    }
                    break;
            }

            // 状態が変わった瞬間に前の姿勢を控え、そこから寄せる（カクつき防止）
            if (_state != before)
            {
                Rig.SnapshotPose();
                _blend = 0f;
                _blendSeconds = _state switch
                {
                    State.Attack => AttackBlendSeconds,
                    State.Stagger => 0.04f,
                    State.Down => DownBlendSeconds,
                    State.GetUp => 0.05f,
                    State.Guard => GuardBlendSeconds,
                    State.Grabbed => 0.08f,
                    _ => IdleBlendSeconds
                };
            }

            // 引き渡しで拘束の種類（Back / Pin / BearHug）が変わると、姿勢が一瞬で
            // 切り替わって見える。今の姿勢から 0.14 秒かけて新しい形へ寄せる。
            if (_state == State.Grabbed && Grapple != null)
            {
                if (_wasHandingOff != Grapple.IsHandingOff ||
                    (_lastGrappleKind.HasValue && _lastGrappleKind.Value != Grapple.Kind))
                {
                    Rig.SnapshotPose();
                    _blend = 0f;
                    _blendSeconds = 0.14f;
                }
                _lastGrappleKind = Grapple.Kind;
                _wasHandingOff = Grapple.IsHandingOff;
            }
            else
            {
                _lastGrappleKind = null;
                _wasHandingOff = false;
            }

            if (_state == State.Walk)
            {
                // 横歩きは歩幅が短い。前と同じ幅で横へ運ぶと引き足が軸足を追い越して
                // 足が交差する。歩幅と 1 周期を同じだけ詰めるので、接地は滑らない。
                float walkStride = Mathf.Lerp(StepLength, StepLength * 0.45f, Mathf.Abs(WalkDir.x));
                _walkPhase += dt * (Mathf.PI * Mathf.Max(_planarSpeed, 0.15f) / walkStride);
            }

            // 殴ってきた相手の方を向き直る。
            // 当たった瞬間に回すと「殴られた」感じが消えるので、反ってから遅れて回る。
            // 脚の IK は world 座標で解くので、向きを変えるのは ApplyPose より前。
            if (_hasFaceYaw)
            {
                Transform body = transform.root;

                if (_state == State.Stagger)
                {
                    float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.14f, _stateTime));
                    if (turn > 0.001f)
                    {
                        if (Motor != null && Motor.BeltFacing)
                            body.rotation = _faceYaw;
                        else
                            body.rotation = Quaternion.RotateTowards(
                                body.rotation, _faceYaw, 1100f * turn * Mathf.Max(0f, dt));
                    }
                }
                else
                {
                    // 怯みが終わったら結果を記録して終了（毎ヒット2行だとログが埋まる）
                    Vector3 f = body.forward;
                    f.y = 0f;
                    Debug.Log($"[Turn] rest angle={Vector3.Angle(f, _faceYaw * Vector3.forward):F0}");
                    _hasFaceYaw = false;
                }
            }

            // 攻撃の出始めに相手へ向く（ソフトオートエイム）。
            // 判定は「発生の瞬間」の向きで決まるので、0.045 秒で向き終える。
            if (_hasAim)
            {
                if (_state == State.Attack)
                {
                    const float aimSeconds = 0.07f;
                    _aimTime += Mathf.Max(0f, dt);

                    float total = Quaternion.Angle(_aimStart, _aimYaw);
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_aimTime / aimSeconds));
                    transform.root.rotation = Quaternion.RotateTowards(
                        transform.root.rotation, _aimYaw, total / aimSeconds * k * Mathf.Max(0f, dt));

                    // 時間だけで打ち切ると、大きく振り向くとき（70°）に 3 割しか回らず、
                    // 拳が相手の横を向いたまま当たらない。目標角に着くまで続ける。
                    if (Quaternion.Angle(transform.root.rotation, _aimYaw) <= 2f
                        || _aimTime >= aimSeconds * 1.8f)
                        _hasAim = false;
                }
                else
                {
                    _hasAim = false;
                }
            }

            ApplyPose();

            // 突き飛ばされた瞬間: 姿勢を組んだ後に足す（上体が前へ・腕が後ろへ・顔は起こす）。
            // ApplyPose より前に書くと上書きされるので、必ずこの位置で。
            if (Grapple != null && !Grapple.IsHandingOff && Grapple.PushPulse > 0f && Rig.Ready)
            {
                float p = Mathf.Clamp01(Grapple.PushPulse);
                Rig.Chest.localRotation *= Quaternion.Euler(20f * p, 0f, 0f);
                Rig.Head.localRotation *= Quaternion.Euler(-16f * p, 0f, 0f);
                Rig.LUpperArm.localRotation *= Quaternion.Euler(45f * p, 0f, 0f);
                Rig.RUpperArm.localRotation *= Quaternion.Euler(45f * p, 0f, 0f);
            }
        }

        float MoveSpeed()
        {
            if (Stats == null)
                return 1f;
            float t = Stats.State.MoveSpeedMultiplier;
            return Mathf.Max(0.4f, t);
        }

        /// <summary>
        /// 1 歩の長さ。速度に比例させることで「歩幅」と「実際に進む距離」が一致し、
        /// 足が地面を滑らなくなる。
        /// </summary>
        float StepLength => Mathf.Clamp(_planarSpeed * StrideLength, 0.30f, 1.10f);

        /// <summary>
        /// 歩行の位相から「足が着いた瞬間」を拾う。
        /// 左足は u=0.5→0 で接地、右足は u=0→0.5 の切り替わりで接地する。
        /// </summary>
        void DetectFootstep(float u)
        {
            float last = _lastWalkU;
            _lastWalkU = u;

            if (last < 0f || _planarSpeed < 0.2f)
                return;

            if (u < last)
                Footstep?.Invoke(0);          // 1 周して戻った＝左足の接地
            else if (last < 0.5f && u >= 0.5f)
                Footstep?.Invoke(1);          // 右足の接地
        }

        static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

    }
}
