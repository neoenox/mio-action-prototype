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
    public sealed class CharacterAnimator : MonoBehaviour
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
        float _heldBellyPunchTotal;

        float _heldFacePunchRemaining;
        float _heldFacePunchTotal;

        /// <summary>いま腹パンの「く」の字オーバーレイ中か。</summary>
        public bool IsHeldBellyPunch => _heldBellyPunchRemaining > 0f;

        /// <summary>いま拘束中の顔面パンチ（首が手前へ振れる）オーバーレイ中か。</summary>
        public bool IsHeldFacePunch => _heldFacePunchRemaining > 0f;

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
        /// 腹パンの「く」の字を再生する。拘束（Grabbed）の上に重ねるので、
        /// 呼んでも状態は Grabbed のまま。
        /// </summary>
        public void PlayHeldBellyPunch(float seconds)
        {
            if (seconds <= 0f)
                return;

            _heldBellyPunchTotal = seconds;
            _heldBellyPunchRemaining = seconds;
        }

        /// <summary>
        /// 腹パンの折れ具合（0..1）。入り 0.07 秒、保持、戻り 0.18 秒。
        /// テストと Editor 診断が姿勢を数値で確認するために公開している。
        /// </summary>
        public float HeldBellyPunchFold
        {
            get
            {
                if (_heldBellyPunchTotal <= 0f || _heldBellyPunchRemaining <= 0f)
                    return 0f;

                float t = _heldBellyPunchTotal - _heldBellyPunchRemaining;
                float inT = Mathf.SmoothStep(0f, 1f, t / 0.07f);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.37f) / 0.18f);
                return Mathf.Clamp01(inT * outT);
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

                if (Grapple != null)
                    Grapple.Escaped -= OnGrappleEscaped;
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

            if (Grapple != null)
                Grapple.Escaped += OnGrappleEscaped;

            _bound = true;
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

            // ガードブレイクだけは膝をつかせる
            if (result.Stagger == StaggerTier.GuardBreak)
            {
                SetState(State.Down, Mathf.Max(0.8f, result.StunSeconds));
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
        /// <summary>
        /// 検証シート用。実際の被弾を通さずに「どこから殴られたか」を固定する。
        /// 0=正面 / ±1=左右 / 2=背後。
        /// </summary>
        public void PreviewHurtSide(int side)
        {
            _hurtSide = side;
        }

        /// <summary>検証シート用。疲労度を固定する（0=平気 / 1=限界）。</summary>
        public void PreviewTired(float value)
        {
            _tired = Mathf.Clamp01(value);
            _tiredHold = true;
        }

        /// <summary>検証シート用。振りほどき直後の突き放しを任意の残り時間で出す。</summary>
        public void PreviewShove(float seconds)
        {
            _shoveTime = Mathf.Max(0f, seconds);
        }

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

            // 腹パンの「く」の字。ヒットストップ中は dt=0 なので、当たった瞬間の姿勢で止まる。
            _heldBellyPunchRemaining = Mathf.Max(0f, _heldBellyPunchRemaining - dt);
            _heldFacePunchRemaining = Mathf.Max(0f, _heldFacePunchRemaining - dt);

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

            switch (_state)
            {
                case State.Stagger:
                    if (_stateTime >= _stateDuration)
                        _state = State.Idle;
                    break;

                case State.Down:
                    if (_stateTime >= _stateDuration)
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
                    else if (Grapple != null && Grapple.IsGrappled)
                    {
                        SetState(State.Grabbed, 0f);
                    }
                    else if (Combat != null && Combat.IsAttacking)
                    {
                        SetState(State.Attack, 0f);
                    }
                    else if (Stats != null && Stats.IsStunned)
                    {
                        // イベントを取りこぼしても怯み姿勢は出す
                        SetState(State.Stagger, Mathf.Max(0.1f, Stats.StunRemaining));
                    }
                    else if (Stats != null && Stats.IsBlocking)
                    {
                        _state = State.Guard;
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

        public void ApplyPose()
        {
            if (Rig == null || !Rig.Ready)
                return;

            Rig.ResetPose();

            Vector3 pole = transform.position + transform.forward * 2f + Vector3.up * 0.55f;
            Vector3 footL = Rig.RestFootL;
            Vector3 footR = Rig.RestFootR;

            Vector3 hipsOffset = Vector3.zero;
            float torsoPitch = 0f;
            float torsoRoll = 0f;
            float torsoYaw = 0f;
            float headPitch = 0f;
            float headYaw = 0f;
            float headRoll = 0f;
            float layAngle = 0f;
            float laySide = 0f;
            float legTuck = 0f;   // 立ち上がり中、膝を畳む量
            float footPitchL = 0f; // 左足首の角度（正＝つま先が下がる／かかとが上がる）
            float footPitchR = 0f; // 右足首の角度（正＝つま先が下がる／かかとが上がる）

            Vector3 armLUpper = ArmIdleUpper;
            Vector3 armLFore = ArmIdleFore;
            Vector3 armRUpper = Mirror(ArmIdleUpper);
            Vector3 armRFore = Mirror(ArmIdleFore);
            float grip = 0.30f;   // 指の握り。0=開いた手、1=拳
            float handRollL = 0f;  // 手のひらの向き（前腕軸まわりのひねり）
            float handRollR = 0f;

            // 歩行以外では位相の追跡を切る（次に歩き出した 1 歩目を鳴らし直す）
            if (_state != State.Walk)
                _lastWalkU = -1f;

            switch (_state)
            {
                case State.Idle:
                {
                    // ファイティングスタンス。左足を半歩前、右足を後ろに引いた半身。
                    // 呼吸・体重移動・膝の細かい上下だけを足して、止まって見えないようにする。
                    float t = Time.time;
                    float shift = Mathf.Sin(t * 0.62f);
                    float bounce = Mathf.Sin(t * 3.4f) * 0.004f;

                    // スタミナが減るほど呼吸が荒く、構えが下がる。
                    // 「疲れている」を数値ではなく姿勢で見せる（HP バーだけでは伝わらない）。
                    float tiredTarget = Stats != null
                        ? Mathf.Clamp01((45f - Stats.State.Stamina) / 45f)
                        : 0f;
                    if (!_tiredHold)
                        _tired = Mathf.MoveTowards(_tired, tiredTarget, Time.deltaTime * 0.8f);

                    float breathAmp = 1f + 2.2f * _tired;
                    float breathRate = 1.7f + 1.3f * _tired;
                    float slowBreath = Mathf.Sin(t * breathRate);

                    // 呼吸の位相を部位ごとにずらす。全身が同じ位相で膨らむと
                    // 「袋が膨らんでいる」ように見える。胸が先、腰と頭が遅れて付いてくる。
                    float breathHips = Mathf.Sin(t * breathRate - 0.30f);
                    float breathHead = Mathf.Sin(t * breathRate - 0.85f);

                    // 腰は沈める（膝を曲げる）。腰だけ後ろへ引くと「お尻を突き出した」画になるので、
                    // 前後はほぼ真下に置き、沈み込みだけで「構えている」を作る。
                    hipsOffset.y = StanceHipsY + breathHips * 0.006f * breathAmp + bounce - 0.02f * _tired;
                    hipsOffset.x = shift * 0.010f;
                    hipsOffset.z = StanceHipsZ;

                    torsoPitch = StanceTorsoPitch + slowBreath * 0.7f * breathAmp + 5f * _tired;
                    torsoYaw = StanceTorsoYaw + shift * 2.0f;   // 半身（左肩が前）
                    torsoRoll = shift * 1.0f;
                    headYaw = StanceHeadYaw - shift * 2.0f;     // 半身の分だけ顔を正面へ戻す
                    headPitch = StanceHeadPitch + breathHead * 0.5f * breathAmp + 4f * _tired;

                    // 前の手（左）は顎の高さから前へ差し出し、後の手（右）は頬の横に引く。
                    // 左右で高さと前後を変えると「構え」に見える（揃えるとガードと区別が付かない）。
                    // 疲れてくると腕が下がり、拳も開き気味になる。
                    float droop = 0.55f * _tired;
                    armLUpper = Vector3.Lerp(StanceArmLUpper, new Vector3(0.30f, -0.98f, 0.08f), droop);
                    armLFore = Vector3.Lerp(StanceArmLFore, new Vector3(0.10f, -0.92f, 0.30f), droop);
                    armRUpper = Vector3.Lerp(StanceArmRUpper, new Vector3(-0.30f, -0.98f, 0.08f), droop);
                    armRFore = Vector3.Lerp(StanceArmRFore, new Vector3(0.06f, -0.90f, 0.34f), droop);

                    // 呼吸で拳がわずかに上下する。腕を完全に止めると「止め絵」に見える。
                    float armBreath = slowBreath * 0.013f * breathAmp;
                    armLUpper += new Vector3(0f, armBreath, 0f);
                    armLFore += new Vector3(0f, armBreath * 0.6f, 0f);
                    armRUpper += new Vector3(0f, armBreath * 0.8f, 0f);
                    armRFore += new Vector3(0f, armBreath * 0.5f, 0f);

                    // 掌が前を向くと「両手を上げた」に見えるので、前腕軸まわりにひねって
                    // 掌を内側（自分の顔）へ向ける。拳は小指側を前に。
                    // 手首も完全には止めない（ゆっくり左右で違う周期で揺らす）。
                    handRollL = StanceHandRollL + Mathf.Sin(t * 0.83f) * 2.5f;
                    handRollR = StanceHandRollR + Mathf.Sin(t * 0.71f + 1.7f) * 2.5f;

                    grip = 0.95f - 0.35f * _tired;

                    // 振りほどいた直後は両手で突き放す（構えへ戻る前に 1 拍だけ入れる）。
                    if (_shoveTime > 0f)
                    {
                        // 突き放しは「押し出して、戻る」の 2 段。残り時間をそのまま使うと
                        // 始まった瞬間に最大まで飛び、戻りが等速になる（人が腕を出す形にならない）。
                        float since = ShoveSeconds - _shoveTime;
                        float pushIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.07f, since));
                        float pushOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.22f, _shoveTime));
                        float s = Mathf.Min(pushIn, pushOut);

                        Vector3 pushUpper = new Vector3(0.16f, -0.26f, 0.95f);
                        Vector3 pushFore = new Vector3(0.04f, 0.10f, 0.99f);

                        armLUpper = Vector3.Lerp(armLUpper, pushUpper, s);
                        armLFore = Vector3.Lerp(armLFore, pushFore, s);
                        armRUpper = Vector3.Lerp(armRUpper, new Vector3(-pushUpper.x, pushUpper.y, pushUpper.z), s);
                        armRFore = Vector3.Lerp(armRFore, pushFore, s);

                        torsoPitch += 7f * s;
                        hipsOffset.z -= 0.02f * s;
                        grip = Mathf.Lerp(grip, 0.35f, s);
                    }

                    // 足は左を前、右を後ろへ、肩幅より少し広く。位置だけで半身を作る。
                    footL += StanceFootL;
                    footR += StanceFootR;
                    break;
                }

                case State.Guard:
                {
                    // 両腕を顔の前に構えて顎を引く。
                    // 腕の指定は「外へ開く量」しか持てないので、肘は体の横に置いたまま
                    // 前腕だけを立てて拳を顔の前に出す。
                    // 残り時間をそのまま使うと等速で戻り「バネ」に見えないので、
                    // 減衰の最後ほどゆっくり戻す。
                    float recoil = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(_guardRecoil / Mathf.Max(0.01f, GuardRecoilSeconds)));
                    float sway = Mathf.Sin(Time.time * 2.1f) * 0.004f;

                    armLUpper = new Vector3(0.16f, -0.55f, 0.62f + recoil * 0.08f);
                    armLFore = new Vector3(0.03f, 0.85f - recoil * 0.10f, 0.30f - recoil * 0.14f);
                    armRUpper = armLUpper;
                    armRFore = armLFore;

                    // ガードも拳を握る。握りが緩いと指が開いて「猫の手」に見える。
                    grip = 0.92f;

                    // 殴られたら腰が引いて、腕が体にめり込む
                    // 構えは「踏ん張り」。腰を沈めて膝を曲げ、足を広げて半歩引く。
                    // 沈みは構え（StanceHipsY）から数える。絶対値で書くと
                    // ガードに入った瞬間にかえって腰が上がる（以前そうなっていた）。
                    hipsOffset.y = StanceHipsY - 0.045f - recoil * 0.012f + sway;
                    hipsOffset.z = StanceHipsZ - 0.035f * recoil;

                    torsoPitch = 8f - recoil * 12f;
                    torsoYaw = -3f;
                    headPitch = 4f + recoil * 7f;

                    // 右脚を引いた半歩の構え。足を固定したまま腰だけ沈める。
                    // 左右にも広げる（狭いと上から押されたときに耐えられない形になる）。
                    footR += new Vector3(0.030f, 0f, -0.100f);
                    footL += new Vector3(-0.030f, 0f, 0.050f);
                    break;
                }

                case State.Walk:
                {
                    float p = _walkPhase;
                    // 横歩きは歩幅を詰める。位相の進みと同じ値を使う（ずれると接地が滑る）
                    float lat = Mathf.Abs(WalkDir.x);
                    float stepLen = Mathf.Lerp(StepLength, StepLength * 0.45f, lat);
                    float half = stepLen * 0.5f;
                    // 足を運ぶ向き。前進なら前後、横歩きなら横
                    Vector2 dir = WalkDir;

                    // 位相 0〜0.5 が接地、0.5〜1 が遊脚。
                    // 接地中は「体が進むぶん足が後ろへ流れる」ので、地面に対して止まって見える。
                    float u = Mathf.Repeat(p / (2f * Mathf.PI), 1f);

                    // 足音のタイミング。u=0 で左、u=0.5 で右が接地する。
                    DetectFootstep(u);

                    Vector3 FootOffset(float uu)
                    {
                        float sweep;
                        float lift = 0f;

                        if (uu < 0.5f)
                        {
                            // 接地。前から後ろへ一定の速さで流れる
                            sweep = half - uu * 2f * stepLen;
                        }
                        else
                        {
                            // 遊脚。弧を描いて前へ戻る
                            float s = (uu - 0.5f) * 2f;
                            sweep = -half + s * stepLen;
                            lift = Mathf.Sin(s * Mathf.PI) * StepHeight;
                        }

                        // 進行方向へ運ぶ。横歩きでは足が横に出る（前後だけだと足が滑る）
                        return new Vector3(dir.x * sweep, lift, dir.y * sweep);
                    }

                    // 構え（アイドル）の足の置き方を基準にして、そこへ歩行の踏み出しを足す。
                    // 基準を外すと歩き出した瞬間に足の幅・腰の高さ・上体の向きが変わり、
                    // 「構えていた人が急に別の人になった」ように見える。
                    // 横歩きは足の間隔を広げる（守りながら横へ動く形）。狭いままだと足が交差する
                    Vector3 stanceL = StanceFootL + new Vector3(-0.035f * lat, 0f, 0f);
                    Vector3 stanceR = StanceFootR + new Vector3(0.035f * lat, 0f, 0f);
                    footL += stanceL + FootOffset(u);
                    footR += stanceR + FootOffset(Mathf.Repeat(u + 0.5f, 1f));

                    // 腰は構えの沈みを保ったまま、1 歩ごとに少し沈む（1 周期に 2 回）
                    // ＋ 体重移動で左右に振れる
                    hipsOffset.y = StanceHipsY - Mathf.Abs(Mathf.Cos(p)) * 0.018f;
                    hipsOffset.x = Mathf.Sin(p) * 0.012f;
                    hipsOffset.z = StanceHipsZ;

                    torsoPitch = StanceTorsoPitch + 0.5f * dir.y;
                    // 横歩きでは進行方向へ少し傾ける。まっすぐ立ったまま横へ滑ると
                    // 「台車に乗っている」ように見える
                    torsoRoll = -dir.x * 3f;
                    torsoYaw = StanceTorsoYaw + Mathf.Sin(p) * 3f;
                    headYaw = StanceHeadYaw - Mathf.Sin(p) * 2f;
                    headPitch = StanceHeadPitch;

                    // 構えを崩さない。歩いても拳は顔の前に残し、肩と肘だけ小さく揺らす。
                    // 腕を下ろすと「構えていた人が急に歩き出した」ように見えてしまう。
                    // ただし振り幅が小さすぎると「腕を固定したまま歩く」人形になるので、
                    // 肩の前後動が目で追える程度まで上げてある。
                    float swing = Mathf.Cos(p) * ArmSwing * 0.55f;

                    armLUpper = StanceArmLUpper + new Vector3(0.02f, 0f, -swing * 0.40f);
                    armLFore = StanceArmLFore + new Vector3(0f, 0f, -swing * 0.28f);
                    armRUpper = StanceArmRUpper + new Vector3(-0.02f, 0f, swing * 0.40f);
                    armRFore = StanceArmRFore + new Vector3(0f, 0f, swing * 0.28f);

                    handRollL = StanceHandRollL;
                    handRollR = StanceHandRollR;
                    grip = 0.95f;
                    break;
                }

                case State.Stagger:
                {
                    // 構えから崩れる。0 から作ると、当たった瞬間に足がそろって立ち位置が飛ぶ。

                    // 30ms で一気に当たりの形へ入り、そこから減衰しながら戻る。
                    // 減衰の途中で 1 回だけ前へ振れる（のけぞりの余韻）。
                    //
                    // 減衰と揺れは「当たってからの秒数」で決める。状態の長さで正規化すると、
                    // 弱い一撃（0.2 秒）は 0.2 秒で 0.7 往復して痙攣し、
                    // 強い一撃（0.8 秒）は同じ 0.7 往復を 0.8 秒かけて行うので
                    // 「軽い一撃の方が激しく揺れる」という逆転が起きていた。
                    float snap = Mathf.Clamp01(_stateTime / 0.03f);

                    // 余韻の長さは当たりの強さで変える。同じ減衰だと、軽い一撃は
                    // 一瞬で戻って「弾いた」ように見え、重い一撃も同じ速さで戻って
                    // 「軽いのに長く止まっている」ように見える。
                    // 重い一撃ほどゆっくり・低い周期で揺れて、戻りきるのは状態の終わり際。
                    float decay = _tier switch
                    {
                        StaggerTier.Small => 9.0f,
                        StaggerTier.Medium => 5.5f,
                        StaggerTier.Large => 3.2f,
                        StaggerTier.GuardBreak => 3.0f,
                        _ => 9.0f
                    };

                    float sway = _tier switch
                    {
                        StaggerTier.Small => 13.0f,
                        StaggerTier.Medium => 10.0f,
                        StaggerTier.Large => 8.0f,
                        StaggerTier.GuardBreak => 7.5f,
                        _ => 13.0f
                    };

                    float k = snap * 1.22f * Mathf.Exp(-decay * _stateTime) * Mathf.Cos(sway * _stateTime);

                    // 上体も腰も脚も同じ値で動かす。以前は上体だけ生の k（負値もそのまま）で、
                    // 他は 0 で止めていたため、戻りの途中で上体だけ前へ出て腰が置いていかれていた。
                    float kv = Mathf.Clamp(k, -0.30f, 1f);

                    float bend = _tier switch
                    {
                        StaggerTier.Small => LightBend,
                        StaggerTier.Medium => MediumBend,
                        StaggerTier.Large => HeavyBend,
                        StaggerTier.GuardBreak => HeavyBend,
                        _ => LightBend
                    };

                    // 弱いヒットで腕を大きく振り回すと大げさに見えるので、強さに比例させる
                    float flail = Mathf.Clamp01(bend / 46f);

                    // 横からならその側へ、背中からなら前に折れる（どちらも「押された方向」へ崩れる）。
                    bool fromBehind = _hurtSide == 2;
                    float side = _hurtSide == 0 ? 1f : (_hurtSide == 2 ? 0f : -_hurtSide);

                    // 本当に横から殴られたときの符号（0 = 正面 / 背後）。
                    // side は正面ヒットでも 1 になる（揺れに方向を与えるため）ので、
                    // 「横からの一撃か」はこちらで判断する。
                    float lateralSide = _hurtSide == 1 ? -1f : (_hurtSide == -1 ? 1f : 0f);

                    // 殴られた場所で崩れ方を変える。
                    //   顔: 頭が弾かれて後ろへ振れる（上体は少し反る）
                    //   腹: 上体が前へ折れて腹を抱える（息が詰まる）
                    // 場所が分からないとき（投げなど）は、どちらにも倒さない。
                    bool bodyBlow = !fromBehind && CurrentHitZone == PunchZone.Body;
                    bool faceBlow = !fromBehind && CurrentHitZone == PunchZone.Face;

                    // 顔ヒットは上体を大きく反らせない（のけぞりすぎると「吹き飛ばされた」画になる）。
                    // 顔は首が弾かれる（+ 少しだけ上体が付く）で「殴られた」に見える。
                    // 腹と背後は押された方向へ崩れるので、これまでどおり深く倒す。
                    float torsoBack = faceBlow ? 0.16f : 0.38f;
                    torsoPitch = StanceTorsoPitch + (fromBehind ? bend * 0.80f
                        : (bodyBlow ? bend * 0.95f : -bend * torsoBack)) * kv;
                    // 上体は真後ろへ反るだけでなく、殴られた方向へ少しねじれる。
                    // ねじれが無いと「自分で反った」ようにしか見えない。
                    torsoYaw = StanceTorsoYaw + 20f * kv * side;
                    // 揺れは当たった直後だけ。長く続けると震えているように見える。
                    torsoRoll = Mathf.Sin(_stateTime * 20f) * 2.4f * kv
                                * Mathf.Exp(-_stateTime * 9f);

                    // 頭は殴られた所なので、上体より先に振れる（20ms）。
                    // 上体が先に反ると「自分で反った」ように見える（実測でそうなっていた）。
                    // 上体の反りは抑え、その分を頭に持たせると「殴られた」に見える。
                    float headFollow = Mathf.Clamp01(_stateTime / 0.02f);
                    headPitch = StanceHeadPitch - bend * 0.62f * kv * headFollow * (fromBehind ? -0.7f : 1f);
                    if (bodyBlow)
                        headPitch = StanceHeadPitch + bend * 0.45f * kv * headFollow;   // 腹を抱えて頭が落ちる
                    else if (faceBlow)
                        headPitch -= bend * 0.25f * kv * headFollow;                    // 顔を打たれて頭が跳ねる
                    headYaw = StanceHeadYaw + 14f * kv * headFollow * side;
                    headRoll = 15f * kv * headFollow * side;   // 横から来た一撃で首が傾く

                    // 効いた直後、首が小さく揺れる（頭がガクッと残る感じ）。
                    // 減衰を速くしないと、いつまでも震えて「寒い」に見える。
                    float shakeT = Mathf.Max(0f, _stateTime - 0.05f);
                    float shakeAmp = 9f * flail * Mathf.Exp(-shakeT * 5f);
                    headRoll += Mathf.Sin(shakeT * 24f) * shakeAmp;
                    headYaw += Mathf.Cos(shakeT * 18f) * shakeAmp * 0.5f;
                    headPitch += Mathf.Sin(shakeT * 30f) * shakeAmp * 0.35f;

                    // 腰ごと後ろへ押され、同時に沈む（膝が抜ける）。
                    // 腰より足を大きく動かすと、脚が突っ張って「自分から下がった」画になる。
                    // 顔ヒットは腰を引かずに首だけで受ける（引くと上体ごと逃げて見える）。
                    hipsOffset.y = StanceHipsY - 0.075f * kv;
                    hipsOffset.z = StanceHipsZ - (faceBlow ? 0.05f : 0.13f) * kv;

                    // 前の足（左）は引きずられて浮き、後ろの足（右）で踏ん張る。
                    float drag = Mathf.Clamp01(kv * 1.4f);
                    footL += StanceFootL + new Vector3(0f, 0.035f * Mathf.Sin(Mathf.PI * drag), -0.09f * kv);
                    footR += StanceFootR + new Vector3(0f, 0f, -0.05f * kv);

                    // 構えが崩れる。前の手（左）は外へ弾け、後の手（右）は顔の前に残る
                    // （とっさに顔を守ろうとしている＝痛みと抵抗が同時に見える）。
                    armLUpper = Vector3.Lerp(StanceArmLUpper,
                        new Vector3(0.86f, -0.34f, -0.22f), kv * flail);
                    armLFore = Vector3.Lerp(StanceArmLFore,
                        new Vector3(0.40f, 0.42f, 0.22f), kv * flail);
                    armRUpper = Vector3.Lerp(StanceArmRUpper,
                        new Vector3(-0.62f, -0.52f, 0.06f), kv * flail * 0.6f);
                    armRFore = Vector3.Lerp(StanceArmRFore,
                        new Vector3(-0.24f, 0.72f, 0.36f), kv * flail * 0.6f);

                    // --- 殴られた場所ごとの崩れ方（上の共通の動きを上書きする）-----------------
                    float hit = _stateTime;
                    float hold = _tier switch
                    {
                        StaggerTier.Small => 0.10f,
                        StaggerTier.Medium => 0.18f,
                        _ => 0.32f
                    };

                    if (faceBlow)
                    {
                        // 顔を殴られたら、顔がカメラ側へ弾かれるように横を向く。
                        // 一瞬で振られ、少し止まり、ゆっくり戻る（往復して痙攣しない）。
                        float turn = Mathf.SmoothStep(0f, 1f, hit / 0.07f)
                                     * (1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.35f));

                        float toCamera = 0f;
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            Vector3 to = cam.transform.position - transform.position;
                            to.y = 0f;
                            Vector3 local = transform.InverseTransformDirection(to.normalized);
                            toCamera = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
                        }

                        headYaw = StanceHeadYaw + toCamera * 0.9f * turn;
                        headRoll = -Mathf.Sign(toCamera == 0f ? 1f : toCamera) * 9f * turn;
                        headPitch = StanceHeadPitch - 6f * turn;
                        // 首だけでなく肩も少し付いていく。ただし「殴られた方向への 20° のねじれ」は
                        // 消さずに足す（上書きすると、横から殴られたときだけ上体が正面を向く）。
                        // side が 0 の正面ヒットでは 20*kv*0 ＝ 0 なので、従来と同じ値になる。
                        torsoYaw = StanceTorsoYaw
                            + Mathf.Clamp(20f * kv * side + toCamera * 0.22f * turn, -34f, 34f);
                    }
                    else if (bodyBlow)
                    {
                        // 腹を殴られたら、腰を引いて上体を「く」の字に折る。腹を抱えて頭が落ちる。
                        // 当たった瞬間に折れ、少し耐えてから起き上がる。
                        float fold = Mathf.SmoothStep(0f, 1f, hit / 0.09f)
                                     * (1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.45f));

                        torsoPitch = StanceTorsoPitch + Mathf.Min(bend * 1.25f, 66f) * fold;
                        // 横から殴られたときは、折れながら殴られた方向へ少しねじれる。
                        // 前へ折れる動きが主役なので、顔（20°）より控えめの 12°。
                        // 正面ヒットでは lateralSide が 0 なので、従来どおりまっすぐ折れる。
                        torsoYaw = StanceTorsoYaw + 12f * kv * lateralSide;
                        torsoRoll = 0f;
                        headPitch = StanceHeadPitch + bend * 0.55f * fold;
                        headYaw = StanceHeadYaw;
                        headRoll = 0f;

                        // 腰は後ろへ引いて沈む。上体が前、腰が後ろで「く」の形になる。
                        hipsOffset.y = StanceHipsY - 0.13f * fold;
                        hipsOffset.z = StanceHipsZ - 0.20f * fold;

                        // 両手は腹へ。
                        var bellyUpper = new Vector3(0.30f, -0.88f, 0.25f);
                        var bellyFore = new Vector3(-0.70f, -0.20f, 0.68f);
                        armLUpper = Vector3.Lerp(StanceArmLUpper, bellyUpper, fold);
                        armLFore = Vector3.Lerp(StanceArmLFore, bellyFore, fold);
                        armRUpper = Vector3.Lerp(StanceArmRUpper, bellyUpper, fold);
                        armRFore = Vector3.Lerp(StanceArmRFore, bellyFore, fold);
                    }

                    handRollL = StanceHandRollL;
                    handRollR = StanceHandRollR;
                    grip = 0.55f;
                    break;
                }

                case State.Grabbed:
                {
                    // 相手に掴まれ、引かれている。上体は反り、両手で相手の腕を掴もうとする。
                    // もがくたびに肘が開き、掴もうとする手が跳ねる（レバガチャの手応えを体で返す）。
                    float pull = 1f - Mathf.Exp(-_stateTime * 10f);
                    float pulse = StrugglePulseOverride >= 0f
                        ? StrugglePulseOverride
                        : (Grapple != null ? Grapple.StrugglePulse : 0f);
                    // 細かく震えるのではなく、「逃げようと体を引く → 掴まれて止められる → 戻される」を
                    // ゆっくり繰り返す。逃げたいのに逃げられない、が体の動きで見えるようにする。
                    // 連打すると、引く力が強くなって、止められる衝撃も大きくなる。
                    float cycle = Mathf.Repeat(_stateTime * 0.9f, 1f);
                    float ramp = cycle < 0.62f
                        ? Mathf.SmoothStep(0f, 1f, cycle / 0.62f)                       // じわじわ引く
                        : 1f - Mathf.SmoothStep(0f, 1f, (cycle - 0.62f) / 0.12f);       // 止められて引き戻される
                    float strain = Mathf.Clamp01(ramp * 0.6f + pulse * 0.55f) * pull;

                    // 掴まれた向きで姿勢を作り替える。背後は羽交い締め（Back）、
                    // それ以外は正面からの押さえつけ（Pin）。
                    if (Grapple != null && Grapple.Kind == GrappleKind.Back)
                    {
                        // 羽交い締め。相手の腕が首に回り、上体を後ろへ反らされる。
                        // 腰は相手側（後ろ）へ引かれ、膝を折って踏ん張る。
                        // 腰を沈めないと脚が突っ張って「吊られている」画になる。
                        hipsOffset.y = StanceHipsY - 0.075f * pull;
                        hipsOffset.z = StanceHipsZ - 0.02f * pull;

                        // 羽交い締め: 相手の腕が脇の下をくぐって首の後ろで組まれる。
                        // 彼女の腕は、その腕に押し上げられて肘が横へ大きく開き、
                        // 前腕は力なく体の前へ垂れる（抗って、相手の腕を掴もうとする）。
                        // 頭と上体は後ろから押されて前へ折れ、顎が引ける。苦悶の形。
                        torsoPitch = 6f * pull;
                        torsoYaw = Mathf.Sin(Time.time * 2.6f) * 4f;
                        torsoRoll = 0f;

                        headPitch = 16f * pull;
                        headYaw = Mathf.Sin(Time.time * 3.8f) * 5f;
                        headRoll = 0f;

                        // 上腕は肩の高さで真横へ、前腕は体の前へ向けて下へ垂らす。
                        // もがくと肘が上がり、前腕が相手の腕を掴みに跳ね上がる。
                        var nelsonUpper = new Vector3(0.98f, 0.18f, 0.02f);
                        var nelsonFore = new Vector3(-0.35f, -0.45f, 0.82f);
                        float claw = 0.14f * pulse;

                        armLUpper = nelsonUpper + new Vector3(0f, claw, 0f);
                        armRUpper = nelsonUpper + new Vector3(0f, claw * 0.6f, 0f);
                        armLFore = nelsonFore + new Vector3(0f, claw * 1.6f, claw);
                        armRFore = nelsonFore + new Vector3(0f, claw * 1.2f, claw * 0.8f);

                        // 逃げようと前へ体を投げ出す（腕は押さえられたまま、体だけが相手から離れようとする）。
                        torsoPitch += 9f * strain;
                        headPitch += 5f * strain;
                        hipsOffset.z += 0.05f * strain;
                        armLUpper += new Vector3(0f, 0.12f * strain, 0f);
                        armRUpper += new Vector3(0f, 0.12f * strain, 0f);

                        handRollL = StanceHandRollL;
                        handRollR = StanceHandRollR;
                        grip = 1f;

                        // 前の足（左）は引かれてかかとが浮き、つま先で踏ん張る。
                        // 後ろの足（右）は残して全体重を支える。
                        footL += StanceFootL;
                        footR += StanceFootR;
                        footL += new Vector3(0f, 0f, 0.05f);
                        footPitchL = 30f * pull;

                        // 腹パン: 羽交い締めのまま上体を折り、腰を引いて膝を曲げる（くの字）。
                        // もがきの脈（strain / claw）は止めて、抵抗していないことを見せる。
                        float toCamera = CameraYawToMe();
                        float fold = HeldBellyPunchFold;
                        if (fold > 0f)
                        {
                            strain = 0f;
                            claw = 0f;

                            torsoPitch = Mathf.Lerp(torsoPitch, 50f, fold);
                            torsoYaw = Mathf.Lerp(torsoYaw, 0f, fold);

                            // 顔は下を向かせない。上体は折れても、顔だけ上げてカメラ側へ向ける。
                            // 下を向くとポニーテールが前に落ちて顔が完全に隠れる。
                            float foldYaw = Mathf.Clamp(toCamera, -60f, 60f);
                            headPitch = Mathf.Lerp(headPitch, -40f, fold);
                            headYaw = Mathf.Lerp(headYaw, foldYaw * 0.90f, fold);

                            hipsOffset.y = Mathf.Lerp(hipsOffset.y, StanceHipsY - 0.15f, fold);
                            hipsOffset.z = Mathf.Lerp(hipsOffset.z, StanceHipsZ - 0.13f, fold);
                        }

                        // 顔面パンチ: 上体は折らず、首だけ手前（カメラ側）へ振る。
                        // 通常の顔面パンチ（ApplyFaceFlick）と同じ向きにしないと、
                        // 拘束中だけ後ろへ仰け反って「別の反応」に見える。
                        float faceFlick = HeldFacePunchFlick;
                        if (faceFlick > 0f)
                        {
                            float side = toCamera >= 0f ? 1f : -1f;
                            torsoPitch = Mathf.Lerp(torsoPitch, torsoPitch - 8f, faceFlick);
                            torsoYaw = Mathf.Lerp(torsoYaw, torsoYaw + 10f * side, faceFlick);
                            headPitch = Mathf.Lerp(headPitch, headPitch - 14f, faceFlick);
                            headYaw = Mathf.Lerp(headYaw, toCamera * 0.95f, faceFlick);
                            headRoll = Mathf.Lerp(headRoll, -side * 9f, faceFlick);
                        }

                        break;
                    }

                    // 押さえつけ（Pin）。正面から両肩を押され、膝が折れかけて耐える。
                    // 上体は前へ折り、顔は上げて相手を見る（下を向くと前髪で顔が隠れる）。
                    hipsOffset.y = StanceHipsY - 0.18f * pull;
                    hipsOffset.z = StanceHipsZ - 0.02f * pull;

                    torsoPitch = 18f * pull + 6f * strain;
                    torsoYaw = Mathf.Sin(Time.time * 2.2f) * 3f;
                    torsoRoll = 0f;

                    headPitch = -30f * pull;
                    headYaw = Mathf.Sin(Time.time * 3.4f) * 4f;
                    headRoll = 0f;

                    // 相手の腕を掴んで剥がそうとする。胸の高さで前へ（肘は肩より上げない）。
                    Vector3 gripUpper = new Vector3(0.20f, -0.42f, 0.88f);
                    Vector3 gripFore = new Vector3(0.05f, 0.10f, 0.99f);

                    // もがくと肘が開き、また締まる
                    float spread = 0.10f * pulse;
                    armLUpper = gripUpper + new Vector3(spread, -spread * 0.4f, 0f);
                    armRUpper = gripUpper + new Vector3(spread * 0.6f, -spread * 0.3f, 0f);
                    armLFore = gripFore + new Vector3(0f, spread * 0.5f, 0f);
                    armRFore = gripFore + new Vector3(0f, spread * 0.4f, 0f);

                    // 残り時間が少ないのに押し返せていないと、腕が落ちて力尽きて見える。
                    // 逆に逃げられそう（進捗が高い）なときは腕が上がったまま。
                    float progress = Grapple != null ? Mathf.Clamp01(Grapple.Progress) : 0f;
                    float timeLeft = Grapple != null && Grapple.HasTimeLimit
                        ? Mathf.Clamp01(Grapple.Remaining) : 1f;
                    float give = (1f - timeLeft) * (1f - progress);
                    armLUpper += new Vector3(0f, -0.16f * give, 0f);
                    armRUpper += new Vector3(0f, -0.16f * give, 0f);
                    armLFore += new Vector3(0f, -0.12f * give, 0f);
                    armRFore += new Vector3(0f, -0.12f * give, 0f);

                    grip = 1f;

                    // 構えの足の位置から始める。Rest（T ポーズ）のままだと脚が伸びきって
                    // 爪先立ちになり、「踏ん張り」ではなく「吊られている」画になる。
                    footL += StanceFootL;
                    footR += StanceFootR;

                    // 前後に足を割って踏ん張る。両足を揃えると「座っている」画になる。
                    footL += new Vector3(0f, 0f, 0.04f);
                    footR += new Vector3(0f, 0f, -0.04f);
                    break;
                }

                case State.Down:
                {
                    // 倒れる速さは状態の長さに依存させない（ダウン 2.0s / 気絶 1.2s / 死亡 ∞ の
                    // どれでも同じ 0.95 秒で倒れきる）。以前は状態の長さで正規化していたため、
                    // 死亡だけが 0.1 秒で瞬間的に倒れ、気絶は 1.2 秒かけてゆっくり倒れる逆転があった。
                    float rise = Mathf.Clamp01(_stateTime / DownFallSeconds);

                    // 崩れ落ちる: 最初に速く落ちて、最後にゆっくり収まる
                    float settle = 1f - Mathf.Pow(1f - Mathf.Clamp01(rise), 2.2f);

                    // 膝をついたあと、そのまま後ろへ倒れる
                    float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.65f, rise));

                    // 腰が落ちる（片膝 → 仰向け）。最初の 0.2 秒で目に見えて沈むようにする
                    // 仰向けに倒す（-90 で完全に水平）。
                    // 真上を向いたままだと、重力で前髪が顔の上に落ちてくる。
                    // 体ごと横向きに倒すと、前髪は顔の横（床）へ落ちるので顔が見える。
                    layAngle = -92f * fall;
                    laySide = -30f * fall;

                    // 崩れ落ちる: 足首を支点に腰が弧を描いて後ろへ倒れる。
                    // 「膝をついてから倒れる」形は腰が低すぎて膝が床下へ潜り、
                    // 回転後に膝が背中側へ跳ね上がる（人体に有り得ない形）ので使わない。
                    // 倒れる途中は膝を少し曲げる。棒のように真っ直ぐ倒れると
                    // 「板が倒れた」ように見えるので、足を少しだけ腰へ寄せる。
                    // 倒れきると 0 に戻るので、寝た姿勢は脚を伸ばしたまま。
                    float buckle = Mathf.Sin(Mathf.Clamp01(fall) * Mathf.PI) * 0.09f;
                    footL += (Rig.RestHips - footL).normalized * buckle;
                    footR += (Rig.RestHips - footR).normalized * buckle;

                    Vector3 pivot = (footL + footR) * 0.5f;
                    Vector3 arcHips = pivot +
                        Quaternion.AngleAxis(layAngle, Vector3.right) * (Rig.RestHips - pivot);
                    hipsOffset = arcHips - Rig.RestHips;

                    // 倒れ始めに一度沈む（膝が抜ける感じ）。倒れきる前に戻す。
                    hipsOffset.y -= 0.09f * settle * (1f - fall);

                    // 上体は前へ潰れてから、倒れるに従って脱力する
                    torsoPitch = Mathf.Lerp(20f * settle, 3f, fall);
                    torsoRoll = 6f * settle;
                    headPitch = Mathf.Lerp(8f * settle, -16f, fall);

                    // 首を横に倒す。仰向けのまま正面を向くと、重力で前髪が顔の上に落ちてくる。
                    // 横に倒すと重力が顔の面から横へ逃げるので、前髪が顔を覆わない。
                    headYaw = Mathf.Lerp(7f * settle, 15f, fall);

                    // 左手は膝に添え、右腕は力なく垂れる。
                    // 補間の出発点は「構えの腕」にする（T ポーズ由来の垂れ腕から始めると、
                    // 殴られた瞬間に腕だけ別の場所へ飛ぶ）。
                    armLUpper = Vector3.Lerp(StanceArmLUpper, DownArmLUpper, settle);
                    armLFore = Vector3.Lerp(StanceArmLFore, DownArmLFore, settle);
                    armRUpper = Vector3.Lerp(StanceArmRUpper, DownArmRUpper, settle);
                    armRFore = Vector3.Lerp(StanceArmRFore, DownArmRFore, settle);

                    grip = 0.18f;

                    // 倒れきったあと、膝を少し立てる。棒のように真っ直ぐ伸びていると
                    // 「置かれた人形」に見えるので、脱力して曲がった脚を残す。
                    legTuck = 0.22f * fall;

                    // 背中が床に着いた瞬間の「ドサッ」。ここが無いと、倒れ終わりが
                    // ふわっと止まって「自分で寝た」ように見える。着地後に一度だけ沈む。
                    // 着地の「ドサッ」は、体が床に着いた瞬間（fall=1）に合わせる。
                    // rise 基準にすると、体がもう平らになった後に沈みが来て「二度落ちた」ように見える。
                    float landT = Mathf.Clamp01((fall - 0.86f) / 0.14f);
                    float land = Mathf.Sin(landT * Mathf.PI);
                    hipsOffset.y -= 0.018f * land;
                    torsoRoll += Mathf.Sin(landT * 26f) * 2.6f * land;
                    headPitch -= 4f * land;

                    // 倒れきったあとは浅い息。完全に止めると「止め絵」に見える。
                    // 痛みで胸が小刻みに動く程度に留める（大きくすると寝返りに見える）。
                    if (rise > 0.97f)
                        hipsOffset.y += Mathf.Sin(_stateTime * 2.6f) * 0.0035f;

                    // 床下がりのクランプは最後に 1 回だけ。先に掛けると、着地の沈みと呼吸で
                    // 背中が床を突き抜けたり、クランプ位置で腰の軌道が折れたりする。
                    hipsOffset.y = Mathf.Max(hipsOffset.y, 0.11f - Rig.RestHips.y);

                    // 立ち上がりはこの位置から始める（固定値だと倒れた位置と合わない）。
                    _downEndHipsY = hipsOffset.y;
                    _downEndHipsZ = hipsOffset.z;
                    break;
                }

                case State.GetUp:
                {
                    // 倒れた姿勢から、横に転がって上体を起こし、立ち上がる。
                    float u = _stateDuration > 100f
                        ? 1f
                        : Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _stateDuration));

                    float sit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.60f, u));
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1.00f, u));

                    // 仰向け(-88) → 上体を起こす(-40) → 立ち(0)
                    // 横への倒れ込みは増やさない（増やすと体がひねれて側転に見える）
                    layAngle = -92f + 52f * sit + 40f * rise;
                    laySide = -30f * (1f - rise);

                    hipsOffset.y = Mathf.Lerp(_downEndHipsY, StanceHipsY, rise) + 0.08f * sit * (1f - rise);

                    // 腰は rise で一気に寄せない。寝た姿勢では腰が 0.87m 後ろにあり、
                    // 最後にまとめて寄せると立ち上がる瞬間に腰が滑って「ワープ」に見える。
                    // 座っている間に少しずつ足元へ寄せると、股関節と足が近づいて
                    // 膝が自然に立ってくる（かかとを引き寄せる動き）。
                    float slide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.88f, u));
                    hipsOffset.z = Mathf.Lerp(_downEndHipsZ, StanceHipsZ, Mathf.Max(slide, rise));

                    // 起き上がるときの「腹筋」。rise で戻す
                    torsoPitch = 3f * (1f - rise) + 26f * sit * (1f - rise);
                    torsoRoll = 6f * (1f - rise);
                    headPitch = -9f * (1f - rise);
                    headYaw = 15f * (1f - rise);

                    // 手を地面について押し上げる。上体が起きたら素早く体の前へ戻す。
                    // ただし「寝た姿勢 → 構え」を一気に補間すると、途中で腕が
                    // だらんと垂れて「気絶から起きた人形」に見える。腰の横に手を突いて
                    // 押す形を一度挟み、そこから構えへ戻す。
                    Vector3 pushLUpper = new Vector3(0.42f, -0.80f, -0.42f);
                    Vector3 pushLFore = new Vector3(0.10f, -0.86f, -0.50f);

                    float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.32f, u));
                    float leave = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.84f, u));

                    Vector3 armLUpperPush = Vector3.Lerp(DownArmLUpper, pushLUpper, plant);
                    Vector3 armLForePush = Vector3.Lerp(DownArmLFore, pushLFore, plant);
                    Vector3 armRUpperPush = Vector3.Lerp(DownArmRUpper, Mirror(pushLUpper), plant);
                    Vector3 armRForePush = Vector3.Lerp(DownArmRFore, Mirror(pushLFore), plant);

                    // 腕の行き先は「構え」の腕。Idle（腕が下がった形）へ戻すと、
                    // 立ち上がりきった瞬間に構えへ跳ねる。
                    armLUpper = Vector3.Lerp(armLUpperPush, StanceArmLUpper, leave);
                    armLFore = Vector3.Lerp(armLForePush, StanceArmLFore, leave);
                    armRUpper = Vector3.Lerp(armRUpperPush, StanceArmRUpper, leave);
                    armRFore = Vector3.Lerp(armRForePush, StanceArmRFore, leave);

                    grip = 0.18f * (1f - rise);

                    // 途中で膝を畳む（脚が棒のまま回らないように）
                    legTuck = Mathf.Sin(Mathf.Clamp01((u - 0.10f) / 0.72f) * Mathf.PI) * 0.95f;
                    hipsOffset.y += 0.06f * legTuck;   // 畳んだ分だけ腰を浮かせて足のめり込みを防ぐ

                    // 上体を起こす間は足を前へ出す。真下に置いたままだと
                    // 腰が低いぶん膝が限界まで畳まれ（160°超）、すねが腿に刺さる。
                    // ただし出しすぎると脚が伸びきって「体育座り」に見えるので、
                    // 膝が曲がって見える範囲に留め、rise が始まる前に戻す
                    // （戻すのが遅いと、立ち上がる瞬間に足が体の前から下へ飛ぶ）。
                    float feetOut = Mathf.Sin(Mathf.Clamp01((u - 0.14f) / 0.44f) * Mathf.PI);
                    footL += new Vector3(0f, 0f, 0.22f) * feetOut;
                    footR += new Vector3(0f, 0f, 0.25f) * feetOut;

                    // 立ち上がりきる直前、少し沈んでから伸びる（地面を踏む感触）
                    float settle2 = Mathf.Sin(Mathf.Clamp01((u - 0.78f) / 0.22f) * Mathf.PI);

                    // 手で床を押す山場。上体が起きる前に腕を突っ張る（押し上げている感じ）。
                    float push = Mathf.Sin(Mathf.Clamp01((u - 0.30f) / 0.28f) * Mathf.PI);

                    // 最後は構えの数値へ着地させる。状態が切り替わる瞬間に
                    // 腰や上体が数センチ跳ぶのを消す。
                    float toStance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.95f, u));
                    hipsOffset.y = Mathf.Lerp(hipsOffset.y, StanceHipsY, toStance);
                    hipsOffset.z = Mathf.Lerp(hipsOffset.z, StanceHipsZ, toStance);
                    torsoPitch = Mathf.Lerp(torsoPitch, StanceTorsoPitch, toStance);
                    torsoYaw = Mathf.Lerp(torsoYaw, StanceTorsoYaw, toStance);
                    torsoRoll = Mathf.Lerp(torsoRoll, 0f, toStance);
                    headPitch = Mathf.Lerp(headPitch, StanceHeadPitch, toStance);
                    headYaw = Mathf.Lerp(headYaw, StanceHeadYaw, toStance);
                    grip = Mathf.Lerp(grip, 0.95f, toStance);

                    // 沈み込みと踏ん張りは、着地した姿勢の上に乗せる（最後は 0 に戻る）
                    hipsOffset.y -= 0.045f * settle2;
                    hipsOffset.y += 0.018f * push;

                    // 立ち上がった直後、足元が定まらず小さくよろける。
                    // 最後は 0 に戻すので、構えへは継ぎ目なく繋がる。
                    float wobble = Mathf.Clamp01((u - 0.84f) / 0.16f);
                    float wob = Mathf.Sin(wobble * 15f) * 3.2f * wobble * (1f - wobble) * 2f;
                    torsoRoll += wob;
                    headRoll += wob * 0.8f;
                    headPitch += Mathf.Sin(wobble * 19f) * 2.0f * wobble * (1f - wobble) * 2f;
                    break;
                }

                case State.Attack:
                {
                    // 攻撃データの発生 / 持続 / 硬直に合わせて、
                    // 「振りかぶり → 打ち込み → 戻し」の3段で動かす。
                    AttackData attack = Combat != null && Combat.CurrentAttack != null
                        ? Combat.CurrentAttack
                        : AttackOverride;

                    float startup = 0.05f;
                    float total = 0.30f;
                    AttackKind kind = AttackKind.Light;
                    float power = 1f;

                    if (attack != null)
                    {
                        startup = Mathf.Max(0.01f, attack.startupSeconds);
                        total = Mathf.Max(startup + 0.02f, attack.TotalSeconds);
                        kind = attack.kind;

                        power = kind switch
                        {
                            AttackKind.Heavy => 1.25f,
                            AttackKind.Break => 1.45f,
                            _ => 1f
                        };
                    }

                    float cockEnd = Mathf.Max(0.015f, startup * 0.40f);

                    // 手が伸びきる時刻。既定では「当たり判定の瞬間＝腕が伸びきった瞬間」に揃える。
                    // 溜めと伸びが短すぎると腕が瞬間移動して見えるので、最低 60ms は伸びに使う。
                    float strikeEnd = Mathf.Max(startup, cockEnd + 0.060f);
                    float recover = Mathf.Max(0.02f, total - strikeEnd);

                    // c = 振りかぶり量（0 = 構え, 1 = 引き切った）
                    // p = 打ち込み量（0 = 引き切った, 1 = 当たった）
                    // 2 つの量を別々に持つことで、戻しの最後にちゃんと構えへ帰る。
                    float c;
                    float p;

                    // 打ち込みの速さ。ease-out（速く出て、伸び切る直前に減速）＝鞭のように振る。
                    float strikeSpan = Mathf.Max(0.001f, strikeEnd - cockEnd);

                    if (_stateTime < cockEnd)
                    {
                        float x = Mathf.Clamp01(_stateTime / cockEnd);
                        c = 1f - Mathf.Pow(1f - x, 2f);
                        p = 0f;
                    }
                    else if (_stateTime < strikeEnd)
                    {
                        c = 1f;
                        float x = Mathf.Clamp01((_stateTime - cockEnd) / strikeSpan);
                        p = 1f - Mathf.Pow(1f - x, 3f);
                    }
                    else
                    {
                        float u = Mathf.Clamp01((_stateTime - strikeEnd) / recover);
                        c = 1f - Mathf.SmoothStep(0.55f, 1f, u);
                        p = 1f - Mathf.SmoothStep(0.40f, 1f, u);
                    }

                    // 上体は腕より 12ms 先行させる。体が先に回ってから腕が出ると、
                    // 「腕だけ振っている」ではなく体重が乗った打撃に見える。
                    float pBody;
                    if (_stateTime < cockEnd)
                    {
                        pBody = 0f;
                    }
                    else if (_stateTime < strikeEnd)
                    {
                        float x = Mathf.Clamp01((_stateTime - cockEnd + 0.012f) / strikeSpan);
                        pBody = 1f - Mathf.Pow(1f - x, 3f);
                    }
                    else
                    {
                        float u = Mathf.Clamp01((_stateTime - strikeEnd) / recover);
                        pBody = 1f - Mathf.SmoothStep(0.40f, 1f, u);
                    }

                    // 伸び切ったあと、拳と胸がわずかに行き過ぎてから戻る（follow-through）。
                    // 打ち終わりで「その場で止まった」感じを消す。戻りの前半で 0 に戻る。
                    float followThrough = 0f;
                    if (_stateTime >= strikeEnd)
                    {
                        float fu = Mathf.Clamp01((_stateTime - strikeEnd) /
                            Mathf.Max(0.06f, recover * 0.55f));
                        followThrough = Mathf.Sin(fu * Mathf.PI);
                    }

                    // 攻撃の種類で「振りかぶり」と「打ち込み」の形を変える。
                    // どれも 構え → 振りかぶり(c) → 打ち込み(p) → 構え の同じ骨組みを使う。
                    Vector3 cockUpper;
                    Vector3 cockFore;
                    Vector3 punchUpper;
                    Vector3 punchFore;
                    float yawCock;
                    float yawPunch;
                    float pitchCock;
                    float pitchPunch;
                    float lungeDistance;
                    float rollPunch;      // 伸び切ったときの手首のひねり（0 = 掌が下・拳頭が前）
                    float shoulderYaw;    // 打つ腕の肩を前へ送る胸郭の回旋
                    float shoulderPitch;  // 打つ腕の肩を押し込む胸郭の前傾

                    // 左腕（引き手）。胸の前で守るが、手は顔まで上げない（上げると「降参」に見える）。
                    var cockUpperL = new Vector3(0.34f, -0.90f, 0.06f);
                    var cockForeL = new Vector3(-0.02f, -0.55f, 0.78f);
                    var punchUpperL = new Vector3(0.30f, -0.86f, 0.22f);
                    var punchForeL = new Vector3(-0.04f, -0.35f, 0.92f);

                    switch (kind)
                    {
                        case AttackKind.Heavy:
                            // 体重を乗せたフック。腕を横に広げると T ポーズに見えるので、
                            // 肘を横へ上げて拳を胸の前へ引き、振り抜きは腰の回転で見せる。
                            // 前腕を背中側へ振ると肘が畳まれて「腕が折れる」ので、前腕は前向きのまま。
                            cockUpper = new Vector3(0.70f, -0.58f, -0.18f);
                            cockFore = new Vector3(0.30f, 0.34f, 0.89f);
                            punchUpper = new Vector3(0.22f, 0.10f, 0.95f);
                            punchFore = new Vector3(0.10f, -0.05f, 1.00f);
                            yawCock = 34f;
                            yawPunch = -30f;
                            pitchCock = -3f;
                            pitchPunch = 10f;
                            lungeDistance = 0.075f;
                            rollPunch = -22f;      // フックは拳を立て気味に残す
                            shoulderYaw = 7f;
                            shoulderPitch = 2f;
                            break;

                        case AttackKind.Break:
                            // 片手の掌底。肘は下げて脇を締めたまま、胸の前から突き出す。
                            // 溜めは「拳を胸へ引きつける」形。前腕を立てたままなので肘が畳まれない。
                            // 両手で押すと拳が 2 つ横に並び、真横からは「両腕が前に出た」画になる
                            // （実測で左右とも z=0.51 に揃っていた）ので、押すのは前の手だけにする。
                            cockUpper = new Vector3(0.22f, -0.86f, -0.28f);
                            cockFore = new Vector3(0.06f, 0.42f, 0.90f);
                            punchUpper = new Vector3(0.18f, 0.05f, 0.98f);
                            punchFore = new Vector3(0.10f, 0.00f, 1.00f);
                            yawCock = 16f;
                            yawPunch = -14f;
                            pitchCock = -4f;
                            pitchPunch = 13f;
                            lungeDistance = 0.060f;
                            // 掌底なので掌は前向きのまま残し、肩を胸ごと押し込む。
                            rollPunch = -12f;
                            shoulderYaw = 0f;
                            shoulderPitch = 6f;
                            break;

                        default:
                            // ストレート。拳を肋の横まで引いてから前へ突き出す。
                            // 溜めを顔の高さにすると「手を振っている」に見えるので、必ず胸より下に置く。
                            // 前腕まで後ろへ振ると拳が背中に回って「両手を広げた」画になるので、
                            // 肘だけ後ろへ引き、前腕は前向きのままにして拳を肋の横へ落とす。
                            cockUpper = new Vector3(0.30f, -0.90f, -0.30f);
                            cockFore = new Vector3(0.05f, -0.20f, 0.98f);
                            punchUpper = new Vector3(0.15f, 0.08f, 0.98f);
                            punchFore = new Vector3(0.03f, 0.02f, 1.00f);
                            yawCock = 26f;
                            yawPunch = -24f;
                            pitchCock = 0f;
                            pitchPunch = 6f;
                            lungeDistance = 0.045f;
                            // ストレートは掌を下へ返しきる（＝拳頭が前へ出る）。
                            rollPunch = -3f;
                            shoulderYaw = 6f;
                            shoulderPitch = 3f;
                            break;
                    }

                    // 相手が近いのに腕を伸ばし切ると、拳が体にめり込んで見える。
                    // 間合いに合わせて拳の止まる位置を手前へ寄せる（当たり判定は変えない）。
                    if (Combat != null && Combat.AimDistance > 0.01f)
                    {
                        // 0.45m で最も短く、1.00m 以上で伸び切る
                        float s = Mathf.Clamp01((Combat.AimDistance - 0.45f) / 0.55f);
                        float k = Mathf.Lerp(0.80f, 1f, s);
                        punchUpper = new Vector3(punchUpper.x * k, punchUpper.y, punchUpper.z * k);
                        punchFore = new Vector3(punchFore.x * k, punchFore.y, punchFore.z * k);

                    }

                    // 右腕。基準は構え（アイドルと同じ姿勢）。
                    // 腕を下ろした姿勢から補間すると、攻撃の 1 フレーム目で
                    // 拳が下りて足がそろい、腰が浮く「姿勢の飛び」が出る。
                    armRUpper = Vector3.Lerp(
                        Vector3.Lerp(StanceArmRUpper, cockUpper, c), punchUpper, p);
                    armRFore = Vector3.Lerp(
                        Vector3.Lerp(StanceArmRFore, cockFore, c), punchFore, p);

                    // 左腕: 崩しは両手、それ以外は顔の前の構えを保つ（拳を引いたまま振らない）
                    // 突きで左のガード手まで下ろすと「両手を広げた」画になるので、少ししか動かさない。
                    // 下ろしすぎると「打つたびにガードを落とす」ように見えるので、
                    // 構えから 1 割前後しか動かさない（拳は顎の前に残る）。
                    float leftAmount = Mathf.Max(c * 0.16f, p * 0.10f);
                    Vector3 leftUpper = Vector3.Lerp(
                        Vector3.Lerp(StanceArmLUpper, cockUpperL, c), punchUpperL, p);
                    Vector3 leftFore = Vector3.Lerp(
                        Vector3.Lerp(StanceArmLFore, cockForeL, c), punchForeL, p);
                    armLUpper = Vector3.Lerp(StanceArmLUpper, leftUpper, leftAmount);
                    armLFore = Vector3.Lerp(StanceArmLFore, leftFore, leftAmount);

                    // 打っていない側の手を「肩の横」へ引く。
                    // 構えのままだと拳が胸の 27cm 前・肩の高さに残り、真横から見ると
                    // 打つ手と高さが揃って「両腕が前に出ている」画になる（実測で 2 本とも
                    // z=0.2〜0.7 の帯に並んでいた）。
                    // このリグは肩 1.15m・腕 0.50m なので、肘を下げたまま拳を顎まで
                    // 上げると 1.12m が上限（顎は 1.31m）。高さでは離せない。
                    // なので前ではなく「外・わずかに後ろ」へ逃がす。手は肩の外側に付き、
                    // 真横カメラからは体の陰に入る。肘は肋に付いたまま。
                    var tuckUpperL = new Vector3(0.40f, -0.90f, -0.04f);
                    var tuckForeL = new Vector3(0.08f, 0.97f, -0.10f);
                    float tuck = Mathf.Max(c * 0.70f, p * 1.00f);
                    armLUpper = Vector3.Lerp(armLUpper, tuckUpperL, tuck);
                    armLFore = Vector3.Lerp(armLFore, tuckForeL, tuck);

                    // 手首のひねりも構えから続ける（0 に戻すと毎回拳がくるっと反転する）。
                    // 伸び切る瞬間は「ナックルが前に出る」向きへ返す。前腕軸まわりの 0 が
                    // 掌下（＝拳頭が前）なので、構えの -48 のままだと掌が横向きに残り、
                    // 「手のひらを押し出す」形に見える。行き過ぎだけ少しだけ余計に返す。
                    handRollL = StanceHandRollL;
                    handRollR = Mathf.Lerp(StanceHandRollR, rollPunch - followThrough * 3f, p);

                    // 当たる瞬間は握り切る。握りが緩いと指が伸びて「手を押し出す」形に見える。
                    grip = 0.92f + 0.06f * p;

                    // 3段コンボの 2 段目は左で打つ。右腕で組んだ姿勢を X 反転して左腕へ移し、
                    // 引き手と入れ替える（左右で同じ形のパンチになる）。
                    bool leftPunch = ComboStep == 1;
                    if (leftPunch)
                    {
                        Vector3 punchU = armRUpper;
                        Vector3 punchF = armRFore;
                        Vector3 guardU = armLUpper;
                        Vector3 guardF = armLFore;

                        armLUpper = MirrorX(punchU);
                        armLFore = MirrorX(punchF);
                        armRUpper = MirrorX(guardU);
                        armRFore = MirrorX(guardF);

                        handRollL = Mathf.Lerp(StanceHandRollL,
                            -rollPunch + followThrough * 3f, p);
                        handRollR = StanceHandRollR;
                    }

                    // 腰の捻りと体重移動。振りかぶりは構えの半身から捻り込み、
                    // 打ち込みの到達角度は今までの見た目（構え基準ではない絶対角）を保つ。
                    // ただし伸び切る瞬間だけ胸郭を shoulderYaw / shoulderPitch ぶん余計に回し、
                    // 打つ腕の肩が前へ出ているように見せる（リグに肩甲骨を動かす API は無いので、
                    // BendTorso が配る胸の回旋・前傾で代用する）。
                    float cockYaw = StanceTorsoYaw + c * yawCock * power;
                    torsoYaw = Mathf.Lerp(cockYaw, yawPunch * power - shoulderYaw, pBody)
                        - followThrough * shoulderYaw * 0.30f;
                    float cockPitch = StanceTorsoPitch + pitchCock * c;
                    torsoPitch = Mathf.Lerp(cockPitch, pitchPunch + shoulderPitch, pBody)
                        + followThrough * shoulderPitch * 0.35f;
                    // 肩を送った分だけ胸も回るので、顔はその分だけ戻して相手を見続ける。
                    headYaw = Mathf.Lerp(StanceHeadYaw + c * 4f, -4f + shoulderYaw, p);
                    // 胸を押し込んだ分だけ顔が落ちるので、その分だけ顔を戻して相手を見る。
                    headPitch = Mathf.Lerp(StanceHeadPitch, pitchPunch * 0.35f - shoulderPitch * 0.35f, p);

                    // 左で打つときは体の向きも左右反転させる（右で打つ形のまま左腕だけ
                    // 動かすと、胸は右へ捻ったまま左腕が出て「ねじ切れた」画になる）。
                    if (leftPunch)
                    {
                        torsoYaw = -torsoYaw;
                        headYaw = -headYaw;
                    }

                    // 踏み込み（足は構えの位置に固定したまま、腰だけ前へ出る）
                    footL += StanceFootL;
                    footR += StanceFootR;

                    // 体重が後ろ足に残らないよう、打ち込みに合わせてかかとを上げる。
                    // つま先を床に残す処理は脚の IK のあと（RaiseHeel）で行う。
                    // このリグは足首→ボールが 12cm しかないので、26° では 1.5cm しか
                    // 浮かない（実測）。振り抜きで体重が乗ったと分かる角度まで上げる。
                    float heel = 44f * p * power;
                    footPitchR = heel;

                    // 前足に体重を乗せる。足は床に固定したまま腰だけ前へ送るので滑らない
                    // （脚の IK が伸びを吸収し、後ろ足はかかとが上がって付いてくる）。
                    // 伸び切ったあとは拳に引かれて少しだけ行き過ぎてから戻る。
                    hipsOffset.z = StanceHipsZ
                        + Mathf.Lerp(-0.012f * c, lungeDistance * power, pBody)
                        + 0.016f * pBody * power
                        + followThrough * 0.008f;

                    // 前足が体重を受ける分だけ腰を沈める（前膝が伸びきって見えないように）。
                    // 打ち終わりに少しだけ深く沈んでから戻る＝前足で踏ん張った感触。
                    hipsOffset.y = StanceHipsY
                        - 0.030f * pBody
                        - 0.010f * pBody * power
                        - followThrough * 0.004f;
                    break;
                }
            }

            // 倒れる途中の足。IK は「倒す前」の座標で解き、そのあと LayBody で
            // 体ごと倒れるので、狙った床の位置に足を置くには倒れるぶんを打ち消す。
            // さらに倒れきる直前で、足を「脚を伸ばした先」へ滑らせる（膝の二つ折り防止）。
            if (layAngle < -1f)
            {
                Vector3 hipsLocal = Rig.RestHips + hipsOffset;
                float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-70f, -88f, layAngle));

                Vector3 extendedL = hipsLocal +
                    Quaternion.AngleAxis(layAngle, Vector3.right) * (Rig.RestFootL - Rig.RestHips);
                Vector3 extendedR = hipsLocal +
                    Quaternion.AngleAxis(layAngle, Vector3.right) * (Rig.RestFootR - Rig.RestHips);

                footL = Vector3.Lerp(footL, extendedL, follow);
                footR = Vector3.Lerp(footR, extendedR, follow);

                // IK は「倒す前」の姿勢で解き、そのあと LayBody で体ごと倒れる。
                // 狙った床の位置に足を着けるには、倒れるぶんを打ち消しておく。
                Quaternion undo = Quaternion.AngleAxis(-layAngle, Vector3.right);
                footL = hipsLocal + undo * (footL - hipsLocal);
                footR = hipsLocal + undo * (footR - hipsLocal);
            }

            // 腰のずれはキャラクター基準。ワールドのまま足すと、体が回ったときに
            // 「後ろへ押される」が横や前へずれる。
            // 踏み込みで root が前へ出た分、足を後ろへ残す（足が床を滑らない）。
            // 踏み込みが終わると 0 に戻るので、戻る分だけ足が体の下へ寄る。
            if (Motor != null && Motor.LungeTravel > 0.0005f)
            {
                Vector3 back = transform.InverseTransformDirection(Motor.transform.forward);
                back.y = 0f;
                footL -= back * Motor.LungeTravel;
                footR -= back * Motor.LungeTravel;
            }

            // 腕の狙いを体幹に追従させるための、このフレームの体幹角度。
            // ArmDirection が「肩の面」を基準に腕の向きを作るのに使う。
            _armFollowYaw = torsoYaw;
            _armFollowPitch = torsoPitch;
            _armFollowRoll = torsoRoll;

            Rig.OffsetHips(transform.TransformDirection(hipsOffset));
            Rig.BendTorso(torsoPitch, torsoRoll, torsoYaw);
            ApplyFaceFlick(ref headPitch, ref headYaw, ref headRoll);
            Rig.TurnHead(headPitch, headYaw, headRoll);

            Rig.SetArm(true, ArmDirection(true, armLUpper).normalized,
                ArmDirection(true, armLFore).normalized, handRollL);
            Rig.SetArm(false, ArmDirection(false, armRUpper).normalized,
                ArmDirection(false, armRFore).normalized, handRollR);

            DebugArmLUpper = ArmDirection(true, armLUpper).normalized;
            DebugArmLFore = ArmDirection(true, armLFore).normalized;
            DebugArmRUpper = ArmDirection(false, armRUpper).normalized;
            DebugArmRFore = ArmDirection(false, armRFore).normalized;
            DebugTorsoPitch = torsoPitch;
            DebugHeadPitch = headPitch;
            DebugTorsoYaw = torsoYaw;
            DebugHeadYaw = headYaw;

            // 指を握る。開いたままだと攻撃が「手を差し出す」に見える。
            // 曲がる向きは左右で鏡像なので、リグ側が左右別々の軸を使う。
            Rig.CurlFingers(true, grip);
            Rig.CurlFingers(false, grip);

            // 足を動かす必要が無いときは IK を掛けない（無駄な計算と微妙な揺れを避ける）。
            float hipMove = Mathf.Abs(hipsOffset.x) + Mathf.Abs(hipsOffset.y) + Mathf.Abs(hipsOffset.z);
            bool needLegSolve = hipMove > 0.02f
                || (footL - Rig.RestFootL).sqrMagnitude > 0.0004f
                || (footR - Rig.RestFootR).sqrMagnitude > 0.0004f;

            if (needLegSolve)
            {
                // 足を狙った位置へ置く（接地）。このとき脚の形は IK が決めるので、
                // しゃがみ姿勢を上書きしない（上書きすると足が地面から外れる）。
                Rig.SolveLeg(true, transform.TransformPoint(footL), pole);
                Rig.SolveLeg(false, transform.TransformPoint(footR), pole);
            }
            else if (legTuck > 0.001f)
            {
                // 倒れるときは脚を伸ばす（曲がったまま倒れると体育座りになる）
                Rig.BlendLegsToCrouch(legTuck);
            }
            else if (layAngle < -1f)
            {
                Rig.BlendLegsToRest(Mathf.Clamp01(-layAngle / 88f) * 0.96f);
            }

            // 倒れるのは最後。立った姿勢を丸ごと倒すので、IK の結果も一緒に倒れる。
            if (Mathf.Abs(layAngle) > 0.01f)
                Rig.LayBody(layAngle, laySide);

            // 状態が切り替わった直後は、切り替え前の姿勢から寄せる（カクつき防止）
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + Time.deltaTime / Mathf.Max(0.01f, _blendSeconds));
                Rig.BlendFromSnapshot(Mathf.SmoothStep(0f, 1f, _blend));
            }

            // かかと上げは最後。つま先を床に残したまま足首を回す（IK の解き直しを含む）。
            if (footPitchL > 0.01f)
                Rig.RaiseHeel(true, footPitchL, pole);

            if (footPitchR > 0.01f)
                Rig.RaiseHeel(false, footPitchR, pole);
        }

        static Vector3 Mirror(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        /// <summary>エディタのプレビュー用。ガードで押し返されている度合い（0〜1）を直接入れる。</summary>
        public void ForceGuardRecoil(float normalized)
        {
            _guardRecoil = Mathf.Clamp01(normalized) * GuardRecoilSeconds;
        }

        /// <summary>エディタのプレビュー用。状態を固定して即座に姿勢を作る。</summary>
        /// <param name="log">毎フレーム呼ぶときは false にしてログを止める。</param>
        public void ForceState(State state, float time, float duration = 1f, StaggerTier tier = StaggerTier.Medium, bool log = true)
        {
            _state = state;
            _stateTime = time;
            _stateDuration = duration;
            _tier = tier;
            _walkPhase = time * Mathf.PI * 2f;
            _planarSpeed = 1.4f;
            _blend = 1f;
            ApplyPose();

            if (log && Rig != null && Rig.Ready)
            {
                Debug.Log($"[Pose] {state} t={time:F2} hipsY={Rig.Hips.position.y:F3} " +
                          $"footLz={Rig.LFoot.position.z:F3} footLx={Rig.LFoot.position.x:F3} " +
                          $"handRy={Rig.RHand.position.y:F3} handRz={Rig.RHand.position.z:F3}");
            }
        }
    }
}
