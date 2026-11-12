using System;
using System.Collections;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 掴まれた状態（仕様書 §4.3 の抵抗システム）。
    ///
    /// 数値の本体はエンジン非依存の <see cref="GrappleResistance"/> が持っていて、
    /// ここは「入力を受け取る・時間切れで解放する・演出に知らせる」だけを担当する。
    /// 掴まれたら J / K / L / Space の連打で必要回数ぶんもがくと振りほどける。
    ///
    /// 後ろ拘束（<see cref="GrappleKind.Back"/>）は時間切れで解けない。
    /// 連打で振りほどくか、タッグ腹パンを 5 発食らうかになる。
    /// 食らい切ったときに腹ダメージが上限ならうつ伏せにダウンし、
    /// 未満なら引き渡しを抽選する（<see cref="GrappleHandOff.TryChain"/>）。
    /// 押さえつけ（<see cref="GrappleKind.Pin"/>）は
    /// <see cref="CombatConstants.GrappleLimitSeconds"/> 秒の制限時間つきで、
    /// 時間切れになると解放される（<see cref="Fail"/>）。
    /// </summary>
    [AddComponentMenu("Ryona/Player Grapple")]
    public sealed partial class PlayerGrapple : MonoBehaviour
    {
        public PlayerStats Stats;

        /// <summary>この拘束の状態（誰が・どの種類で・どの段階か）。</summary>
        readonly GrappleSession _session = new GrappleSession();

        /// <summary>拘束の状態。カメラ・ポーズ・HUD はここだけを見ればよい。</summary>
        internal GrappleSession Session => _session;

        /// <summary>掴まれた瞬間。</summary>
        public event Action<GrappleKind> Started;

        internal event Action<EnemyBrain> HeldThrowLanded;

        internal void NotifyHeldThrowLanded(EnemyBrain attacker) => HeldThrowLanded?.Invoke(attacker);

        /// <summary>振りほどけた瞬間。</summary>
        public event Action Escaped;

        /// <summary>押さえつけが時間切れになった瞬間（後ろ拘束では起きない）。</summary>
        public event Action Failed;

        /// <summary>拘束が解けた瞬間（もがいて逃げた場合を除く）。姿勢の突き放しに使う。</summary>
        public event Action Released;

        GrappleResistance _resistance;




        // 拘束中の間合い（GrabStandoff / BearHugStandoff / BackGrabStandoff）は
        // CombatConstants に置いた。EnemyBrain 側の間合い下限や腕の長さと同じ根拠で
        // 決まるため、片方だけ動かすと「伸ばした腕が宙に浮く」画になる。
        // 体が重ならない下限は CombatConstants.BodyGap（敵 0.40 + 彼女 0.30）。

        /// <summary>
        /// 壁からの投げ捨てで、うつ伏せ（true）／仰向け（false）を固定する。
        /// null ならランダム。撮影とテスト用。<c>-walldown=prone|supine</c> でも指定できる。
        /// </summary>
        public static bool? ForcedWallTossForward { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ParseWallDownFlag()
        {
            ForcedWallTossForward = null;

            string v = LaunchOptions.Value("-walldown");
            if (v == null)
                return;

            if (v.Equals("prone", StringComparison.OrdinalIgnoreCase) ||
                v.Equals("forward", StringComparison.OrdinalIgnoreCase))
                ForcedWallTossForward = true;
            else if (v.Equals("supine", StringComparison.OrdinalIgnoreCase) ||
                     v.Equals("back", StringComparison.OrdinalIgnoreCase))
                ForcedWallTossForward = false;
        }

        /// <summary>壁押さえつけ中か。押さえつけ（Pin）の一種だが、レーンの外の壁へ押し出される。</summary>
        public bool IsWallPin => _session.IsWall;
        public WallStage WallPhase => _session.Stage;
        public float WallPhaseProgress => _session.StageProgress;
        public bool IsWallTransition => _session.IsWallTransition;
        public float WallRestraintWeight => _session.WallRestraintWeight;
        internal bool IsWallToss => _session.IsWallToss;
        internal float WallTossLower => _session.WallTossLower;

        // --- 壁・二体がかり（Y 字拘束） -------------------------------
        /// <summary>もう一人の拘束役（アシスト）。居なければ null。</summary>
        public EnemyBrain WallAssist => _session.Assist;

        /// <summary>Y 字拘束の見た目 0..1（手首の開き・二人の腕の持ち方に使う）。</summary>
        internal float WallSplitWeight => _session.SplitWeight;

        /// <summary>二体目が横に着いて、二人で押さえ始めたか。</summary>
        public bool IsWallSplitActive => _session.IsSplitActive;

        /// <summary>いま殴る側（拘束役かアシスト）。null なら待ち。</summary>
        public EnemyBrain WallSplitPuncher => _session.SplitPuncher;

        /// <summary>いまの 1 発の経過秒（0 = 振りかぶり開始、負 = 待ち）。</summary>
        internal float WallSplitPunchAge => _session.SplitPunchAge;

        /// <summary>二人がかりで入った腹パンの数（HUD 用）。</summary>
        internal int WallSplitHits => _session.SplitHits;

        /// <summary>打つ手が左か（アシスト=右、拘束役=左）。ポーズ側が空き手を選ぶ。</summary>
        public bool WallSplitPunchLeft => _session.SplitPunchLeft;

        /// <summary>
        /// 彼女のアニメータ。二体がかりの腹パンで被弾姿勢を重ねるのに使う
        /// （VRM の読み込みが遅れて後から付くことがあるので、都度取り直す）。
        /// </summary>
        CharacterAnimator _animator;

        CharacterAnimator Animator()
        {
            if (_animator == null && Stats != null)
                _animator = Stats.GetComponentInChildren<CharacterAnimator>();
            return _animator;
        }

        /// <summary>壁拘束が始まった時点で横に残しておく二体目（いなければ null）。</summary>
        internal EnemyBrain WallAssistCandidate
        {
            get => _session.AssistCandidate;
            private set => _session.AssistCandidate = value;
        }

        /// <summary>
        /// 投げ捨てる向き（X の符号）。<see cref="BeginWallToss"/> が実際の投げ方向から決める。
        /// 拘束役ののけぞり（<see cref="EnemyFigure"/>）と彼女の手の逃がし先
        /// （<see cref="CharacterAnimator"/>）がこれを見る。
        /// </summary>
        internal float WallTossSide => _wallTossSide;

        internal float WallGripWeight => _session.WallGripWeight;

        internal float WallSlideWeight => _session.WallSlideWeight;

        internal float WallCaptorTravel => _session.WallCaptorTravel;
        WallPinSite _wallSite;
        Vector3 _wallStartPosition;
        Quaternion _wallStartRotation;
        float _wallTossSide = 1f;


        /// <summary>壁押さえつけの見た目 0..1。姿勢のブレンドに使う。</summary>
        public float WallPinWeight { get; private set; }

        /// <summary>
        /// 両手首を壁面へ貼り付けた見た目 0..1。<see cref="WallStage.Held"/> に入ってから
        /// <see cref="WallAdhereSeconds"/> かけて立ち上がる。腕の IK と、押さえ役の手の置き先
        /// （手首 → 胸）がこれで入れ替わる。
        /// </summary>
        public float WallAdhereWeight { get; private set; }

        /// <summary>壁の表面から室内側へ向く法線（world）。姿勢の反らせ方向に使う。</summary>
        public Vector3 WallPinNormal => _wallPinNormal;

        /// <summary>壁の表面 Z（基準位置）。髪の押し戻しに使う。</summary>
        public float WallFaceZ { get; private set; }

        /// <summary>壁の表面が有効か（押し出し中・戻り中の見た目が残っている間）。</summary>
        public bool HasWallFace => IsWallPin || WallPinWeight > 0.001f;

        Vector3 _wallPinSpot;
        Quaternion _wallPinRotation;
        Vector3 _wallPinNormal = Vector3.back;

        /// <summary>壁からレーンへ戻るまでの秒数。</summary>
        const float WallExitSeconds = 0.25f;

        /// <summary>今回の z 戻しにかける秒数（投げ捨てのときは飛んでいる間）。</summary>
        float _wallExitSeconds = WallExitSeconds;

        /// <summary>投げ捨てで使う z 戻しの秒数（<see cref="EndWallPin"/> が拾う）。</summary>
        float _wallThrowExitSeconds;

        /// <summary>両手首が壁面へ貼り付くまでの秒数。</summary>
        const float WallAdhereSeconds = 0.40f;
        float _wallExitRemaining;
        float _wallExitFromZ;
        PlayerMotor _motor;

        /// <summary>掴まれているか。</summary>
        public bool IsGrappled { get; private set; }

        /// <summary>突き飛ばしから受け止めまで。新しい拘束はまだ始まっていない。</summary>
        public bool IsHandingOff { get; private set; }

        /// <summary>
        /// 掴まれている残り時間。後ろ拘束は時間切れが無いので 0 を返す
        /// （表示するときは <see cref="HasTimeLimit"/> で分岐する）。
        /// </summary>
        public float Remaining =>
            HasTimeLimit ? Mathf.Max(0f, _session.LimitRemaining) : 0f;

        /// <summary>
        /// 時間切れで解ける拘束か。後ろ拘束（Back）だけが false。
        /// </summary>
        public bool HasTimeLimit => Kind != GrappleKind.Back;

        /// <summary>掴まれてからの経過秒。ログとテスト用。</summary>
        public float HeldSeconds => _session.HeldSeconds;

        public float FrontEscapeRemaining => IsGrappled && !IsHandingOff && HasTimeLimit
            ? Mathf.Max(0f, CombatConstants.FrontGrappleEscapeSeconds - _session.HeldSeconds)
            : 0f;

        internal float FrontEscapePoseWeight => Mathf.SmoothStep(0f, 1f, Mathf.Min(
            _session.HeldSeconds / 0.18f, FrontEscapeRemaining / 0.20f));

        internal float FrontEscapeEffort { get; private set; }
        float _frontEffortHold;

        /// <summary>腹パン演出などで、もがき入力を一時的に止めているか。</summary>
        public bool IsStruggleSuppressed => IsHandingOff || _session.SuppressRemaining > 0f ||
            IsWallToss || (IsWallTransition && WallPhase >= WallStage.Throw && WallPhase <= WallStage.Lean);

        /// <summary>もがき入力を止めている残り秒。</summary>
        public float SuppressRemaining => Mathf.Max(0f, _session.SuppressRemaining);

        /// <summary>
        /// 指定秒だけ、もがき入力を受け付けない（腹パンで「く」の字になっている間）。
        /// 拘束そのものは続くので、演出が終わればまた連打できる。
        /// </summary>
        public void SuppressStruggle(float seconds)
        {
            if (seconds > 0f)
                _session.SuppressRemaining = Mathf.Max(_session.SuppressRemaining, seconds);
        }

        /// <summary>
        /// もがきの抑制をすぐ解除する。腹パンを撃ち終えて「拘束だけが残る」ときに使う
        /// （抑制が残ったままだと、5 発のあと数秒間は連打しても抜けられない）。
        /// </summary>
        public void ClearStruggleSuppression()
        {
            _session.SuppressRemaining = 0f;
        }

        /// <summary>掴んだ相手。倒されたら掴みは解ける。</summary>
        public EnemyBrain Captor => _session.Captor;

        /// <summary>もがきの進み具合（0..1）。HUD のバーに出す。</summary>
        public float Progress => _resistance != null ? _resistance.Progress : 0f;

        /// <summary>もがいた回数。</summary>
        public int InputCount => _resistance != null ? _resistance.InputCount : 0;

        /// <summary>必要な入力回数（腕の損傷で増える）。</summary>
        public float RequiredInputs { get; private set; }

        public GrappleKind Kind => _session.Kind;

        /// <summary>もがいた瞬間の 0..1。見た目の震えに使う。</summary>
        public float StrugglePulse { get; private set; }

        /// <summary>
        /// 押さえつけ中に入る膝蹴りの 0..1。1 から 0 へ減衰する。
        /// 押さえる側（膝）と彼女（崩れ）の両方が見る。
        /// </summary>
        public float KneePulse { get; private set; }

        /// <summary>押さえつけ中に膝蹴りの見た目を立てる（押さえる側から呼ぶ）。</summary>
        public void KickKnee()
        {
            _kneeHold = KneeHoldSeconds;
            KneePulse = 1f;
        }

        /// <summary>膝蹴りの形を保つ秒。姿勢の補間（約 0.1 秒）が追いついてから減衰させる。</summary>
        const float KneeHoldSeconds = 0.18f;
        float _kneeHold;

        /// <summary>
        /// ベアハッグの密着度 0..1。0 = 少し離れている / 1 = 体が密着している。
        /// 締め付けのたびに 0 → 1 → 0 を繰り返す。
        /// </summary>
        public float SqueezePulse { get; private set; }

        /// <summary>いま「密着して締め付けている」瞬間か（ダメージはこの瞬間に入れる）。</summary>
        public bool IsBearHugContact =>
            IsGrappled && Kind == GrappleKind.BearHug && _hugStage == HugStage.Contact;

        /// <summary>
        /// 引き寄せられている勢い 0..1。引き寄せの中盤で最大になり、密着で 0 に戻る。
        /// 彼女の上体が前へ持っていかれ、腕と足が遅れて残る表現に使う。
        /// </summary>
        public float PullPulse { get; private set; }

        /// <summary>抱え上げられている度合い 0..1。ベアハッグ中に上がり、解放で下がる。</summary>
        public float LiftPulse { get; private set; }

        /// <summary>
        /// 締め付けを始める（掴んでいる側から呼ぶ）。体を引き寄せ、密着した瞬間に
        /// <see cref="IsBearHugContact"/> が true になる。
        /// </summary>
        public void Squeeze()
        {
            if (!IsGrappled || Kind != GrappleKind.BearHug || _hugStage != HugStage.Rest)
                return;

            _hugStage = HugStage.Pull;
            _hugTime = 0f;
        }

        // 密着 → 少し離れる → 密着 の周期。
        const float HugPullSeconds = 0.12f;
        const float HugContactSeconds = 0.10f;
        const float HugReleaseSeconds = 0.30f;

        /// <summary>ベアハッグの密着サイクル。</summary>
        enum HugStage
        {
            Rest,
            Pull,
            Contact,
            Release
        }

        HugStage _hugStage;
        float _hugTime;
        float _hugPhase;

        /// <summary>密着しているときの距離の詰め（m）。胸が触れる距離まで寄せる。</summary>
        const float HugTightInset = 0.16f;

        /// <summary>
        /// 突き飛ばされた瞬間の 0..1。押されて上体が前へ流れ、腕が後ろへ残る。
        /// 引き渡し（<see cref="HandOff"/>）で立てる。
        /// </summary>
        public float PushPulse { get; private set; }
        internal Vector3 PushDirection { get; private set; }
        internal float RecoveryStep { get; private set; }
        internal float RecoveryTravel { get; private set; }
        internal float RecoveryLift { get; private set; }
        internal float RecoveryPoseWeight { get; private set; }
        internal float HandOffTurn { get; private set; }
        internal float RecoveryDistance { get; private set; }
        internal const float ShoveLandingFraction = 0.8f;
        const float GrabContactSeconds = 0.18f;
        float _grabContactRemaining;
        Vector3 _grabContactFrom;
        Quaternion _grabContactRotation;
        internal float GrabContactWeight => Mathf.SmoothStep(0f, 1f, _grabContactRemaining / GrabContactSeconds);
        EnemyBrain _shoveSender;

        /// <summary>突き飛ばしの見た目を立てる。</summary>
        public void PushOff()
        {
            PushPulse = 1f;
        }

        void Awake()
        {
            if (Stats == null)
                Stats = GetComponent<PlayerStats>();
        }

        /// <summary>
        /// 既存のシーンにも自動で付ける（シーンを作り直さなくても遊べるように）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (stats == null)
                return;

            if (stats.GetComponent<PlayerGrapple>() == null)
                stats.gameObject.AddComponent<PlayerGrapple>();
        }

        void Update()
        {
            // 壁押さえつけの見た目は、掴まれていなくても抜けきるまで減衰させる。
            // 減衰は戻り（WallExitSeconds）と同じ速さにする。姿勢だけ先に抜けると、
            // 位置が壁際に残っているのに立ち姿で横滑りして見える（実測）。
            float wallTarget = IsGrappled && IsWallPin &&
                WallPhase >= WallStage.Slam ? 1f : 0f;
            WallPinWeight = Mathf.MoveTowards(WallPinWeight, wallTarget,
                Time.deltaTime / WallExitSeconds);

            // 両手首を壁面へ貼り付ける見た目。引き上げ（Restrain）が終わってから
            // ゆっくり立ち上げる。彼の手が彼女の手首を離れて胸へ移るのと同時に、
            // 彼女の腕が頭上から壁面へ滑る（瞬間移動させない）。
            float adhereTarget = IsWallPin && WallPhase == WallStage.Held ? 1f : 0f;
            WallAdhereWeight = Mathf.MoveTowards(WallAdhereWeight, adhereTarget,
                Time.deltaTime / WallAdhereSeconds);

            // 二体がかり（Y 字）の見た目。二人そろってから手首を広げる。
            TickWallSplitWeight(Time.deltaTime);

            // 壁からレーンへ戻る。掴みが解けたあとも z を滑らかに戻さないと、
            // 1.4m 横へ瞬間移動したように見える。
            // 引き渡し（HandOff）は掴まれたままなので、IsGrappled では切らない。
            // 切ると戻りが一度も走らず、壁際の z を固定したまま次の拘束へ入る。
            if (_wallExitRemaining > 0f)
            {
                _wallExitRemaining = Mathf.Max(0f, _wallExitRemaining - Time.deltaTime);
                float t = Mathf.Clamp01(_wallExitRemaining / _wallExitSeconds);
                var motor = Motor();
                if (motor != null)
                {
                    if (_wallExitRemaining > 0f)
                    {
                        // 戻り先はレーン（PlayerMotor が持つ本来の z）。0 を決め打ちすると、
                        // レーンが 0 以外になったときに壁際から別の場所へ滑る。
                        float lane = motor.LaneZ;
                        motor.SetLaneZOverride(Mathf.Lerp(lane, _wallExitFromZ,
                            Mathf.SmoothStep(0f, 1f, t)));
                    }
                    else
                        motor.ClearLaneZOverride();
                }
            }

            if (StrugglePulse > 0f)
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 4f);

            // 膝蹴りの見た目は掴まれていなくても抜けきるまで減衰させる（解放された直後に残らない）
            if (_kneeHold > 0f)
            {
                // 姿勢の補間が追いつくまで形を保つ。すぐ減衰させると腿が上がりきらない。
                _kneeHold -= Time.deltaTime;
                KneePulse = 1f;
            }
            else if (KneePulse > 0f)
                KneePulse = Mathf.Max(0f, KneePulse - Time.deltaTime * 4f);

            // 引き渡し中は、移動と同じ時計で押された姿勢を進める。
            if (!IsHandingOff && PushPulse > 0f)
                PushPulse = Mathf.Max(0f, PushPulse - Time.deltaTime * 4f);

            // ベアハッグ以外では密着度・引き寄せを抜く（演出の取りこぼし防止）。
            if (!IsGrappled || Kind != GrappleKind.BearHug)
            {
                SqueezePulse = Mathf.MoveTowards(SqueezePulse, 0f, Time.deltaTime * 4f);
                PullPulse = Mathf.MoveTowards(PullPulse, 0f, Time.deltaTime * 10f);
            }

            // 抱え上げ。掴まれてベアハッグの間だけ上がり、解放されると下がる。
            float liftTarget = IsGrappled && Kind == GrappleKind.BearHug &&
                (FrontEscapeRemaining <= 0f || _hugStage != HugStage.Rest) ? 1f : 0f;
            LiftPulse = Mathf.MoveTowards(LiftPulse, liftTarget,
                Time.deltaTime * (liftTarget > LiftPulse ? 2.5f : 4.5f));

            if (!IsGrappled)
                return;

            // 掴んだ相手が倒れた／消えたら、そのまま解放する（罰は与えない）
            if (Captor == null || !Captor.IsAlive)
            {
                Release();
                Debug.Log("[Grapple] 掴んでいた相手が倒れたので解放");
                return;
            }

            if (Stats == null || Stats.IsDead)
            {
                Release();
                return;
            }

            if (IsHandingOff)
                return;

            if (IsWallToss)
            {
                TickWallToss();
                return;
            }

            if (IsWallTransition)
            {
                TickWallTransition();
                if (!IsStruggleSuppressed && InputReader.StrugglePressed())
                    Struggle();
                return;
            }

            // ベアハッグ: 体を引き寄せて密着させ、少し離してからまた締める。
            if (_grabContactRemaining > 0f)
            {
                _grabContactRemaining = Mathf.Max(0f, _grabContactRemaining - Time.deltaTime);
                float weight = GrabContactWeight;
                SnapToCaptor(Captor, Kind);
                transform.position = Vector3.Lerp(transform.position, _grabContactFrom, weight);
                transform.rotation = Quaternion.Slerp(transform.rotation, _grabContactRotation, weight);
            }
            else if (Kind == GrappleKind.BearHug)
                TickBearHugBody(Time.deltaTime);

            // 押し出しのブレンドで動いた z を、レーン固定（PlayerMotor.LateUpdate）にも渡す。
            if (IsWallPin)
            {
                var motor = Motor();
                if (motor != null)
                    motor.SetLaneZOverride(transform.position.z);
            }

            _session.HeldSeconds += Time.deltaTime;
            _frontEffortHold = Mathf.Max(0f, _frontEffortHold - Time.deltaTime);
            bool pressing = _frontEffortHold > 0f && FrontEscapeRemaining > 0f;
            FrontEscapeEffort = Mathf.MoveTowards(FrontEscapeEffort, pressing ? 1f : 0f,
                Time.deltaTime / (pressing ? 0.30f : 0.45f));

            // 腹パンで「く」の字になっている間は、もがき入力を受け付けない。
            // 拘束は続くので、演出が終わればまた連打できる。
            if (_session.SuppressRemaining > 0f)
            {
                _session.SuppressRemaining = Mathf.Max(0f, _session.SuppressRemaining - Time.deltaTime);
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 6f);
                return;
            }

            // 後ろ拘束は時間切れで解けない。
            if (HasTimeLimit)
            {
                _session.LimitRemaining = Mathf.Max(0f, _session.LimitRemaining - Time.deltaTime);
                if (_session.LimitRemaining <= 0f && _pinReceiver == null)
                {
                    Fail();
                    return;
                }
            }

            if (InputReader.StrugglePressed())
                Struggle();
        }

        /// <summary>
        /// もがく。入力を待たずに呼べるようにしておく（テストと自動操作のため）。
        /// 必要回数に届いたらその場で振りほどく。
        /// </summary>
        public void Struggle()
        {
            if (!IsGrappled || IsStruggleSuppressed)
                return;

            StrugglePulse = 1f;
            if (FrontEscapeRemaining > 0f)
                _frontEffortHold = 0.36f;

            if (_resistance.RegisterInput())
                Escape();
        }

        /// <summary>掴まれる。外部（敵の掴み攻撃）から呼ぶ。掴みは背後からだけ。</summary>
        public void Begin(EnemyBrain captor, GrappleKind kind = GrappleKind.Back) =>
            BeginHold(captor, kind, blendContact: false);

        internal void BeginContact(EnemyBrain captor, GrappleKind kind) =>
            BeginHold(captor, kind, blendContact: true);

    }
}
