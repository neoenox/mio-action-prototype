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
    public sealed class PlayerGrapple : MonoBehaviour
    {
        public PlayerStats Stats;

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
        float _remaining;
        float _heldSeconds;
        float _suppressRemaining;

        /// <summary>押さえつけ（Pin）/ ベアハッグで掴まれている間の、相手との距離（m）。</summary>
        /// <remarks>
        /// 敵カプセル 0.40 + 彼女 0.30 = 0.70 が体の重ならない下限。
        /// 0.72 にすると敵が <see cref="EnemyBrain"/> 側の下限（0.95）まで押し戻され、
        /// 伸ばした腕（肩から 0.62m）が彼女の 50cm 手前で止まって「宙に手を出す」画になる。
        /// </remarks>
        const float GrabDistance = 0.60f;

        /// <summary>
        /// ベアハッグの基準距離（離れ気味のとき）。締め付けでここから 0.16m 詰めて密着する。
        /// </summary>
        const float BearHugDistance = 0.54f;
        // 羽交い締めは胸を相手の背中に付けて組む。離れていると腕が伸びきって
        // 「首へ手を伸ばしている」画になる（0.85m だった）。肘を畳んで脇の下へ入れる距離にする。
        internal const float BackGrabDistance = 0.34f;

        /// <summary>
        /// 壁からの投げ捨てで、うつ伏せ（true）／仰向け（false）を固定する。
        /// null ならランダム。撮影とテスト用。<c>-walldown=prone|supine</c> でも指定できる。
        /// </summary>
        public static bool? ForcedWallTossForward { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ParseWallDownFlag()
        {
            ForcedWallTossForward = null;
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (!a.StartsWith("-walldown=", StringComparison.OrdinalIgnoreCase))
                    continue;

                string v = a.Substring("-walldown=".Length);
                if (v.Equals("prone", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("forward", StringComparison.OrdinalIgnoreCase))
                    ForcedWallTossForward = true;
                else if (v.Equals("supine", StringComparison.OrdinalIgnoreCase) ||
                         v.Equals("back", StringComparison.OrdinalIgnoreCase))
                    ForcedWallTossForward = false;
            }
        }

        /// <summary>壁押さえつけ中か。押さえつけ（Pin）の一種だが、レーンの外の壁へ押し出される。</summary>
        public bool IsWallPin { get; private set; }
        public enum WallStage { None, Grab, Rise, Throw, Slam, Lean, Regrip, Restrain, Held, TossWindup, TossAir, TossLand }
        public WallStage WallPhase { get; private set; }
        public float WallPhaseProgress { get; private set; }
        public bool IsWallTransition => IsWallPin && WallPhase != WallStage.Held;
        public float WallRestraintWeight => WallPhase == WallStage.Restrain
            ? Mathf.SmoothStep(0f, 1f, WallPhaseProgress)
            : WallPhase == WallStage.Held || WallPhase == WallStage.TossWindup ? 1f : 0f;
        internal bool IsWallToss => IsWallPin && WallPhase >= WallStage.TossWindup;
        internal float WallTossLower => Mathf.SmoothStep(0f, 1f, WallPhaseProgress);

        // --- 壁・二体がかり（Y 字拘束） -------------------------------
        /// <summary>もう一人の拘束役（アシスト）。居なければ null。</summary>
        public EnemyBrain WallAssist { get; private set; }

        /// <summary>Y 字拘束の見た目 0..1（手首の開き・二人の腕の持ち方に使う）。</summary>
        internal float WallSplitWeight { get; private set; }

        /// <summary>二体目が横に着いて、二人で押さえ始めたか。</summary>
        public bool IsWallSplitActive => IsWallPin && WallPhase == WallStage.Held &&
            WallAssist != null && _wallSplitReady && WallAssist.IsAlive && WallAssist.isActiveAndEnabled;

        /// <summary>いま殴る側（拘束役かアシスト）。null なら待ち。</summary>
        internal EnemyBrain WallSplitPuncher { get; private set; }

        /// <summary>いまの 1 発の経過秒（0 = 振りかぶり開始、負 = 待ち）。</summary>
        internal float WallSplitPunchAge { get; private set; } = -1f;

        /// <summary>二人がかりで入った腹パンの数（HUD 用）。</summary>
        internal int WallSplitHits => _wallSplitHits;

        /// <summary>打つ手が左か（アシスト=右、拘束役=左）。ポーズ側が空き手を選ぶ。</summary>
        public bool WallSplitPunchLeft { get; private set; }

        bool _wallSplitStarted;
        bool _wallSplitReady;
        int _wallSplitHits;
        float _wallSplitAssistAge;

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
        internal EnemyBrain WallAssistCandidate { get; private set; }

        /// <summary>
        /// 投げ捨てる向き（X の符号）。<see cref="BeginWallToss"/> が実際の投げ方向から決める。
        /// 拘束役ののけぞり（<see cref="EnemyFigure"/>）と彼女の手の逃がし先
        /// （<see cref="CharacterAnimator"/>）がこれを見る。
        /// </summary>
        internal float WallTossSide => _wallTossSide;

        internal float WallGripWeight => WallPhase == WallStage.Regrip
            ? Mathf.SmoothStep(0f, 1f, WallPhaseProgress)
            : WallRestraintWeight > 0f || WallPhase == WallStage.Restrain ? 1f : 0f;

        internal float WallSlideWeight => WallPhase switch
        {
            WallStage.Lean => Mathf.SmoothStep(0f, 1f, WallPhaseProgress),
            WallStage.Regrip => 1f,
            WallStage.Restrain => 1f - WallRestraintWeight,
            _ => 0f
        };

        internal float WallCaptorTravel => WallPhase switch
        {
            WallStage.Throw => 0.55f * WallPhaseProgress,
            WallStage.Slam or WallStage.Lean => 0.55f,
            WallStage.Regrip => Mathf.Lerp(0.55f, 0.90f, WallGripWeight),
            WallStage.Restrain => Mathf.Lerp(0.90f, 1f, WallRestraintWeight),
            WallStage.Held or WallStage.TossWindup or WallStage.TossAir or WallStage.TossLand => 1f,
            _ => 0f
        };
        WallPinSite _wallSite;
        float _wallPhaseTime;
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
            HasTimeLimit ? Mathf.Max(0f, _remaining) : 0f;

        /// <summary>
        /// 時間切れで解ける拘束か。後ろ拘束（Back）だけが false。
        /// </summary>
        public bool HasTimeLimit => Kind != GrappleKind.Back;

        /// <summary>掴まれてからの経過秒。ログとテスト用。</summary>
        public float HeldSeconds => _heldSeconds;

        public float FrontEscapeRemaining => IsGrappled && !IsHandingOff && HasTimeLimit
            ? Mathf.Max(0f, CombatConstants.FrontGrappleEscapeSeconds - _heldSeconds)
            : 0f;

        internal float FrontEscapePoseWeight => Mathf.SmoothStep(0f, 1f, Mathf.Min(
            _heldSeconds / 0.18f, FrontEscapeRemaining / 0.20f));

        internal float FrontEscapeEffort { get; private set; }
        float _frontEffortHold;

        /// <summary>腹パン演出などで、もがき入力を一時的に止めているか。</summary>
        public bool IsStruggleSuppressed => IsHandingOff || _suppressRemaining > 0f ||
            IsWallToss || (IsWallTransition && WallPhase >= WallStage.Throw && WallPhase <= WallStage.Lean);

        /// <summary>もがき入力を止めている残り秒。</summary>
        public float SuppressRemaining => Mathf.Max(0f, _suppressRemaining);

        /// <summary>
        /// 指定秒だけ、もがき入力を受け付けない（腹パンで「く」の字になっている間）。
        /// 拘束そのものは続くので、演出が終わればまた連打できる。
        /// </summary>
        public void SuppressStruggle(float seconds)
        {
            if (seconds > 0f)
                _suppressRemaining = Mathf.Max(_suppressRemaining, seconds);
        }

        /// <summary>
        /// もがきの抑制をすぐ解除する。腹パンを撃ち終えて「拘束だけが残る」ときに使う
        /// （抑制が残ったままだと、5 発のあと数秒間は連打しても抜けられない）。
        /// </summary>
        public void ClearStruggleSuppression()
        {
            _suppressRemaining = 0f;
        }

        /// <summary>掴んだ相手。倒されたら掴みは解ける。</summary>
        public EnemyBrain Captor { get; private set; }

        /// <summary>もがきの進み具合（0..1）。HUD のバーに出す。</summary>
        public float Progress => _resistance != null ? _resistance.Progress : 0f;

        /// <summary>もがいた回数。</summary>
        public int InputCount => _resistance != null ? _resistance.InputCount : 0;

        /// <summary>必要な入力回数（腕の損傷で増える）。</summary>
        public float RequiredInputs { get; private set; }

        public GrappleKind Kind => _resistance != null ? _resistance.Kind : GrappleKind.Back;

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

            _heldSeconds += Time.deltaTime;
            _frontEffortHold = Mathf.Max(0f, _frontEffortHold - Time.deltaTime);
            bool pressing = _frontEffortHold > 0f && FrontEscapeRemaining > 0f;
            FrontEscapeEffort = Mathf.MoveTowards(FrontEscapeEffort, pressing ? 1f : 0f,
                Time.deltaTime / (pressing ? 0.30f : 0.45f));

            // 腹パンで「く」の字になっている間は、もがき入力を受け付けない。
            // 拘束は続くので、演出が終わればまた連打できる。
            if (_suppressRemaining > 0f)
            {
                _suppressRemaining = Mathf.Max(0f, _suppressRemaining - Time.deltaTime);
                StrugglePulse = Mathf.Max(0f, StrugglePulse - Time.deltaTime * 6f);
                return;
            }

            // 後ろ拘束は時間切れで解けない。
            if (HasTimeLimit)
            {
                _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
                if (_remaining <= 0f && _pinReceiver == null)
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

        /// <summary>
        /// 壁へ押し出しながら押さえつける。掴み方は <see cref="GrappleKind.Pin"/> と同じで、
        /// 立つ位置だけ壁の前に差し替える（もがき・膝蹴り・制限時間はそのまま）。
        /// </summary>
        internal void BeginWallPin(EnemyBrain captor, Vector3 spot, Quaternion rotation,
            Vector3 wallNormal, float faceZ, WallPinSite site)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            _wallSite = site;
            _wallStartPosition = transform.position;
            _wallStartRotation = transform.rotation;
            _wallPinSpot = spot;
            _wallPinRotation = rotation;
            WallFaceZ = faceZ;
            _wallPinNormal = wallNormal.sqrMagnitude > 0.001f
                ? wallNormal.normalized
                : Vector3.back;
            IsWallPin = true;
            WallPinWeight = 0f;
            _wallExitRemaining = 0f;

            // 押し出しの初速は今のレーン。接触のブレンドに合わせて少しずつ壁へ寄せる。
            var motor = Motor();
            if (motor != null)
                motor.SetLaneZOverride(transform.position.z);

            BeginHold(captor, GrappleKind.Pin, blendContact: true);
            _grabContactRemaining = 0f;
            _wallSite.BeginRise();
            SetWallStage(WallStage.Grab);

            // 壁拘束の間、二体目は「退避」ではなく横で待たせる（二人がかりの準備）。
            PickWallAssistCandidate();
        }

        /// <summary>壁拘束に加わる二体目を、拘束が始まった時点で選ぶ。</summary>
        void PickWallAssistCandidate()
        {
            WallAssistCandidate = null;
            float bestDistance = 5f;
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == Captor || !enemy.isActiveAndEnabled || !enemy.IsAlive ||
                    enemy.IsRising || enemy.IsHolding || enemy.IsPinning || enemy.MoveSpeed <= 0f)
                    continue;

                float distance = Vector3.Distance(enemy.transform.position, transform.position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                WallAssistCandidate = enemy;
            }

            if (WallAssistCandidate != null)
            {
                WallAssistCandidate.BeginWallAssist(this);
                Debug.Log($"[WallSplit] candidate {WallAssistCandidate.name} dist={bestDistance:F2}");
            }
        }

        void SetWallStage(WallStage stage)
        {
            WallPhase = stage;
            WallPhaseProgress = 0f;
            _wallPhaseTime = 0f;
            Debug.Log($"[WallSequence] {stage}");
        }

        void TickWallTransition()
        {
            _wallPhaseTime += Time.deltaTime;
            float seconds = WallPhase switch
            {
                WallStage.Grab => 0.22f,
                WallStage.Rise => 0.46f,
                WallStage.Throw => 0.30f,
                WallStage.Slam => 0.13f,
                WallStage.Lean => 0.57f,
                WallStage.Regrip => 0.28f,
                _ => 0.52f
            };
            WallPhaseProgress = Mathf.Clamp01(_wallPhaseTime / seconds);
            float progress = Mathf.SmoothStep(0f, 1f, WallPhaseProgress);
            if (_wallSite == null)
            {
                Release();
                return;
            }
            if (WallPhase == WallStage.Rise)
                _wallSite.SetRise(progress);

            const float wallContactDepth = 0.10f;
            Vector3 contactSpot = _wallPinSpot + Vector3.forward * wallContactDepth;
            if (WallPhase == WallStage.Throw)
            {
                float flight = WallPhaseProgress * (0.40f + 0.60f * WallPhaseProgress);
                transform.position = Vector3.Lerp(_wallStartPosition, contactSpot, flight)
                    + Vector3.up * (0.10f * Mathf.Sin(Mathf.PI * WallPhaseProgress));
                transform.rotation = Quaternion.Slerp(_wallStartRotation, _wallPinRotation, progress);
            }
            else if (WallPhase >= WallStage.Slam)
            {
                Vector3 spot = Vector3.Lerp(contactSpot, _wallPinSpot, WallRestraintWeight);
                transform.SetPositionAndRotation(spot, _wallPinRotation);
            }

            Motor()?.SetLaneZOverride(transform.position.z);
            if (WallPhaseProgress < 1f)
                return;

            if (WallPhase == WallStage.Restrain)
            {
                SetWallStage(WallStage.Held);
                _heldSeconds = 0f;
                Captor.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
                return;
            }

            SetWallStage((WallStage)((int)WallPhase + 1));
            if (WallPhase == WallStage.Slam)
            {
                WallPinWeight = 1f;
                PlaySfx(SfxId.WallSlam, 1f);
                FindFirstObjectByType<CombatFeel>()?.OnWallSlam();
            }
        }

        void BeginWallToss()
        {
            // 投げる向きは「彼女が元いた向き（進行方向）」＝アリーナの内側。
            // 壁の左右へ投げると、画面外やステージ端へ飛んでいく（実測）。
            // 拘束役ののけぞりもこの向きに合わせる（逆だと投げた側と反対へ倒れて見える）。
            float interior = (_wallStartRotation * Vector3.forward).x;
            // 壁へ正面から向かっていて X 成分が無いときは向きが決められないので、
            // +X をアリーナ内側とみなす（0 にすると投げ方向がゼロベクトルになる）。
            _wallTossSide = Mathf.Abs(interior) > 0.1f ? Mathf.Sign(interior) : 1f;
            SetWallStage(WallStage.TossWindup);
        }

        /// <summary>
        /// 壁押さえつけ中に二体目が横へ入り、二人がかり（Y 字）になる。
        /// 拘束役の EnemyBrain から、最初の膝蹴りのタイミングで呼ばれる。
        /// 二体目が見つからなければ false（呼び出し側は従来の膝蹴りを続ける）。
        /// </summary>
        internal bool TryBeginWallSplit(EnemyBrain captor)
        {
            if (!IsWallPin || WallPhase != WallStage.Held || _wallSplitStarted || IsHandingOff)
                return false;
            if (Captor != captor || Stats == null || Stats.IsDown || Stats.IsDead)
                return false;

            // 壁拘束の開始時に選んでおいた二体目を優先する（すでに横で待っている）。
            EnemyBrain best = WallAssistCandidate;
            if (best != null && (!best.isActiveAndEnabled || !best.IsAlive || best.IsHolding ||
                best.IsHandOffLocked || best.IsPinning))
                best = null;

            float bestDistance = best != null
                ? Vector3.Distance(best.transform.position, transform.position) : 5f;
            if (best == null)
            {
                foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (enemy == null || enemy == captor || !enemy.isActiveAndEnabled || !enemy.IsAlive ||
                        enemy.IsRising || enemy.IsHolding || enemy.IsHandOffLocked || enemy.IsPinning ||
                        enemy.MoveSpeed <= 0f)
                        continue;

                    float distance = Vector3.Distance(enemy.transform.position, transform.position);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = enemy;
                }
            }
            if (best == null)
                return false;

            WallAssistCandidate = best;
            WallAssist = best;
            _wallSplitStarted = true;
            _wallSplitReady = best.WallAssistReady;
            _wallSplitHits = 0;
            _wallSplitAssistAge = 0f;
            WallSplitWeight = 0f;
            WallSplitPunchAge = -1f;
            // 二人がかりの間は拘束時間を延ばす（膝 5 発ぶんの時間を使うため）。
            _remaining = Mathf.Max(_remaining, 4.6f);
            RequiredInputs = CombatConstants.WallSplitEscapeInputs;
            if (!best.IsWallAssisting)
                best.BeginWallAssist(this);
            StartCoroutine(WallSplitRoutine());
            Debug.Log($"[WallSplit] begin captor={captor.name} assist={best.name} " +
                      $"dist={bestDistance:F2} ready={_wallSplitReady} need={RequiredInputs:F0}");
            return true;
        }

        System.Collections.IEnumerator WallSplitRoutine()
        {
            // アシストが横へ着くまで待つ（来なければ通常の膝蹴りへ戻す）。
            while (_wallSplitAssistAge < 1.2f && !_wallSplitReady &&
                   WallAssist != null && WallAssist.IsAlive && WallAssist.isActiveAndEnabled)
            {
                _wallSplitAssistAge += Time.deltaTime;
                if (WallAssist.WallAssistReady)
                {
                    _wallSplitReady = true;
                    break;
                }
                yield return null;
            }
            if (!_wallSplitReady)
            {
                Debug.Log("[WallSplit] assist could not arrive - fallback to knees");
                var captor = Captor;
                EndWallSplit();
                captor?.ResumePinKnees();
                yield break;
            }

            // 手首を Y 字へ広げ、二人の持ち方へ移る（姿勢側は WallSplitWeight を見る）。
            while (WallSplitWeight < 0.99f && IsWallSplitActive && Stats != null && !Stats.IsDown)
                yield return null;
            if (!IsWallSplitActive || Stats == null || Stats.IsDown)
            {
                EndWallSplit();
                yield break;
            }

            for (int i = 0; i < CombatConstants.WallSplitPunchCount; i++)
            {
                if (!IsWallSplitActive || WallAssist == null || Stats == null || Stats.IsDown)
                {
                    EndWallSplit();
                    yield break;
                }

                EnemyBrain puncher = i % 2 == 0 ? Captor : WallAssist;
                WallSplitPuncher = puncher;
                // 拘束役は右で彼女の左腕を持つので空き手は左。アシストは左で右腕を持つので空き手は右。
                WallSplitPunchLeft = puncher == Captor;
                WallSplitPunchAge = 0f;

                // 振りかぶり（0.22 秒）
                float t = 0f;
                while (t < 0.22f)
                {
                    if (!IsWallSplitActive)
                    {
                        EndWallSplit();
                        yield break;
                    }
                    t += Time.deltaTime;
                    WallSplitPunchAge = t;
                    yield return null;
                }

                puncher?.DoWallSplitPunch();

                // 壁に両腕を固定されたまま、腹で折れる被弾姿勢を重ねる。
                // 疲労（打った数）を渡すと、後半ほど戻りが遅く、折れが深くなる。
                float fatigue = (float)_wallSplitHits / CombatConstants.WallSplitPunchCount;
                Animator()?.PlayHeldBellyPunch(CombatConstants.WallSplitPunchInterval, fatigue);
                _wallSplitHits++;

                // 戻り（合計 0.55 秒間隔）
                while (t < CombatConstants.WallSplitPunchInterval)
                {
                    if (!IsWallSplitActive)
                    {
                        EndWallSplit();
                        yield break;
                    }
                    t += Time.deltaTime;
                    WallSplitPunchAge = t;
                    yield return null;
                }
                WallSplitPunchAge = -1f;
            }

            // 6 発を打ち終えたらアシストを離し、既存の投げ捨てへ。
            EndWallSplit();
            if (IsWallPin && WallPhase == WallStage.Held && Captor != null &&
                Stats != null && !Stats.IsDown)
                BeginWallToss();
        }

        /// <summary>二人がかりを終える。アシストには退避を指示する。</summary>
        void EndWallSplit()
        {
            var assist = WallAssist;
            WallAssist = null;
            WallSplitPuncher = null;
            WallSplitPunchAge = -1f;
            _wallSplitStarted = false;
            _wallSplitReady = false;
            _wallSplitHits = 0;
            if (assist != null)
                assist.ReleaseWallAssist();
            if (WallAssistCandidate != null && WallAssistCandidate != assist)
                WallAssistCandidate.ReleaseWallAssist();
            WallAssistCandidate = null;

            // もがき必要数を通常へ戻す（腕の負傷の扱いは BeginHold と同じ）。
            if (IsWallPin && Stats != null)
                RequiredInputs = ResistanceMath.RequiredInputs(Kind, 1f,
                    Stats.State.Body[BodyPart.Arms]);
        }

        void TickWallSplitWeight(float dt)
        {
            float target = IsWallSplitActive ? 1f : 0f;
            WallSplitWeight = Mathf.MoveTowards(WallSplitWeight, target, dt / 0.22f);
        }

        void TickWallToss()
        {
            const float seconds = 0.26f;
            _wallPhaseTime += Time.deltaTime;
            WallPhaseProgress = Mathf.Clamp01(_wallPhaseTime / seconds);

            // 壁から 0.10m 手前まで引き上げる。ここから放物線で投げ捨てる。
            Vector3 launch = _wallPinSpot + (_wallPinRotation * Vector3.forward) * 0.10f;
            transform.position = Vector3.Lerp(_wallPinSpot, launch, WallTossLower);
            Motor()?.SetLaneZOverride(transform.position.z);

            if (WallPhaseProgress < 1f)
                return;

            // 引き上げてから投げ捨てる。空中の弧・着地・倒れはダウンの仕組み
            // （PlayerStats.ForceDown → PlayerMotor.LaunchBack）に任せる。
            // ここで台本どおりに運ぶと、レーンへ戻ってから急に倒れるように見えた（実測）。
            var attacker = Captor != null ? Captor.transform : null;
            Vector3 throwDirection = Vector3.right * _wallTossSide;
            PlaySfx(SfxId.Swing, 0.95f);

            // 飛んでいる間に z をレーンへ戻す。壁の真上を飛ぶと壁の見た目を
            // すり抜けるので、離脱は速め（着地より先にレーンへ出る）。
            _wallThrowExitSeconds = ThrowExitSeconds;
            Fail();

            // うつ伏せ着地でも投げ飛ばす（壁の投げ捨ては「崩れ落ち」ではない）。
            bool forward = ForcedWallTossForward ?? (UnityEngine.Random.value < 0.5f);
            Stats?.ForceDown(CombatConstants.DownDuration, "wall-toss", attacker,
                1.7f, 0.95f, forward, throwDirection, launchWhenForward: true);
            StartCoroutine(PlayBodyDropAfter(ThrowFlightSeconds));
        }

        /// <summary>投げ捨ての滞空時間（秒）。着地音の待ちに使う。</summary>
        const float ThrowFlightSeconds = 0.62f;

        /// <summary>投げ捨てで壁からレーンへ抜けるまでの秒数（滞空より短くする）。</summary>
        const float ThrowExitSeconds = 0.34f;

        IEnumerator PlayBodyDropAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);

            // 待つ間に別の敵へ掴み直されたら、拘束中の効果音になるので鳴らさない。
            // ダウンが解けている（着地ではない）ときも鳴らさない。
            if (IsGrappled || Stats == null || !Stats.IsDown)
                yield break;

            PlaySfx(SfxId.BodyDrop, 0.8f);
        }

        void BeginHold(EnemyBrain captor, GrappleKind kind, bool blendContact)
        {
            if (IsGrappled || Stats == null || Stats.IsDead)
                return;

            // 前の壁押さえつけから戻っている途中なら、ここでレーンへ戻しきる。
            if (!IsWallPin && _wallExitRemaining > 0f)
                ClearWallExit();

            Captor = captor;

            if (blendContact)
            {
                _grabContactFrom = transform.position;
                _grabContactRotation = transform.rotation;
                _grabContactRemaining = GrabContactSeconds;
            }
            else
                SnapToCaptor(captor, kind);

            // 腕が壊れているほど逃げにくい（仕様書 §4.3）
            float armInjury = Stats.State.Body[BodyPart.Arms];
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, armInjury);

            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = HoldDuration(kind);
            _heldSeconds = 0f;
            _suppressRemaining = 0f;
            IsGrappled = true;
            StrugglePulse = 0f;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            KneePulse = 0f;
            if (kind == GrappleKind.BearHug)
            {
                _hugStage = HugStage.Rest;
                _hugTime = 0f;
                _hugPhase = 0f;
                SqueezePulse = 0f;
                PullPulse = 0f;
            }

            // 掴まれている間は移動できない
            Stats.IsMoving = false;

            PlaySfx(SfxId.Impact, 0.9f);

            string limit = HasTimeLimit ? $"{_remaining:F2}s" : "none";
            Debug.Log($"[Grapple] begin kind={kind} need={RequiredInputs:F1} " +
                      $"limit={limit} arm={armInjury:F0} captor={(captor != null ? captor.name : "none")}");

            Started?.Invoke(kind);
        }

        /// <summary>
        /// 拘束を別の敵へ引き渡す（たらい回し）。掴まれたまま相手だけが入れ替わる。
        /// もがきの進捗と制限時間はやり直し（抜けても次の敵に捕まる）。
        /// </summary>
        public void HandOff(EnemyBrain next, GrappleKind kind = GrappleKind.Back, bool shove = false)
        {
            if (!IsGrappled || IsHandingOff || next == null || !next.IsAlive ||
                next == Captor || Stats == null || Stats.IsDead)
                return;

            EndWallPin();

            var previous = Captor;
            if (shove && (previous == null || !previous.IsAlive))
                return;

            Captor = next;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            _grabContactRemaining = 0f;

            if (shove)
            {
                // 接触待ち・二歩・受け止めの間は両者を固定する。
                const float lockSeconds = 1.30f;
                IsHandingOff = true;
                _shoveSender = previous;
                previous.LockForHandOff(lockSeconds);
                previous.PushAway();
                next.LockForHandOff(lockSeconds);
                StartCoroutine(Shove(next, kind));
                return;
            }

            // 押し出しの経路は戻り補間へ任せる（壁から剥がれながら受け手の前へ運ばれる）。
            // 即座に寄せる経路は SnapToCaptor が位置を決めるので、戻り補間が残ると
            // 置いた位置から壁際へ引き戻される。ここで打ち切る。
            ClearWallExit();

            SnapToCaptor(next, kind);
            ApplyHandOffHold(kind);
            Started?.Invoke(kind);
        }

        EnemyBrain _pinReceiver;
        float _pinReceiverGrip;

        internal bool IsPreparingPinHandOff => _pinReceiver != null;

        internal float PinHandOffGrip(EnemyBrain enemy) => _pinReceiver == enemy ? _pinReceiverGrip : 0f;

        internal bool TryStartPinHandOff(EnemyBrain receiver)
        {
            if (_pinReceiver != null || IsHandingOff || Captor == null)
                return false;

            _pinReceiver = receiver;
            receiver.LockForHandOff(1.2f);
            StartCoroutine(ReceiveFromPin(Captor, receiver));
            return true;
        }

        System.Collections.IEnumerator ReceiveFromPin(EnemyBrain sender, EnemyBrain receiver)
        {
            // 受け手が後ろから腕を取るまで、正面の押さえつけともがき入力を維持する。
            float elapsed = 0f;
            float grip = 0f;
            while (_pinReceiver == receiver && IsGrappled && Kind == GrappleKind.Pin &&
                   Captor == sender && sender != null && sender.isActiveAndEnabled && sender.IsAlive && sender.IsPinning &&
                   receiver != null && receiver.isActiveAndEnabled && receiver.IsAlive &&
                   receiver.Phase != EnemyPhase.Stagger && receiver.IsHandOffLocked &&
                   Stats != null && !Stats.IsDead && !Stats.IsDown && !Stats.HasHeldBellyDown && elapsed < 1.05f)
            {
                Vector3 forward = transform.forward;
                Vector3 spot = transform.position - forward * BackGrabDistance;
                spot.y = receiver.transform.position.y;
                receiver.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                receiver.transform.position = Vector3.MoveTowards(receiver.transform.position, spot,
                    receiver.MoveSpeed * receiver.State.MoveSpeedMultiplier * Time.deltaTime);

                if (elapsed >= KneeHoldSeconds && (receiver.transform.position - spot).sqrMagnitude < 0.0025f)
                {
                    grip += Time.deltaTime;
                    _pinReceiverGrip = Mathf.SmoothStep(0f, 1f, grip / 0.16f);
                    if (grip >= 0.23f)
                    {
                        // 両側から腕を取る短い間を置き、受け手だけの拘束へ切り替える。
                        receiver.transform.position = spot;
                        ClearPinHandOff();
                        Captor = receiver;
                        ApplyHandOffHold(GrappleKind.Back);
                        _heldSeconds = 0f;
                        FrontEscapeEffort = 0f;
                        _frontEffortHold = 0f;
                        _grabContactRemaining = 0f;
                        receiver.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
                        sender.LockForHandOff(0.24f);
                        Debug.Log($"[HandOff] pin → back {sender.name} → {receiver.name}");
                        Started?.Invoke(Kind);
                        yield break;
                    }
                }

                yield return null;
                elapsed += Time.deltaTime;
            }

            if (_pinReceiver == receiver)
                ClearPinHandOff();
        }

        void ClearPinHandOff()
        {
            _pinReceiver = null;
            _pinReceiverGrip = 0f;
        }

        void ApplyHandOffHold(GrappleKind kind)
        {
            RequiredInputs = ResistanceMath.RequiredInputs(kind, 1f, Stats.State.Body[BodyPart.Arms]);
            _resistance = new GrappleResistance(kind, RequiredInputs);
            _remaining = HoldDuration(kind);
            _suppressRemaining = 0f;
        }

        static float HoldDuration(GrappleKind kind) => kind switch
        {
            GrappleKind.Back => float.PositiveInfinity,
            GrappleKind.BearHug => CombatConstants.BearHugLimitSeconds,
            _ => CombatConstants.GrappleLimitSeconds
        };

        /// <summary>
        /// 掴み位置まで一続きに移動する。突き飛ばしの距離と掴み位置を別々に扱うと、
        /// 行き過ぎた後に戻る動きが「ワープ」に見えるので、最初から終点へ運ぶ。
        /// </summary>
        System.Collections.IEnumerator Shove(EnemyBrain next, GrappleKind kind)
        {
            Vector3 fromPos = transform.position;
            Vector3 toPos = GrabSpot(next, kind);
            Vector3 toNext = next.transform.position - toPos;
            toNext.y = 0f;
            // どちらの拘束でも、受け手に正面から当たってから掴みに移る。
            Vector3 towardReceiver = toNext.normalized;
            bool behindHold = kind == GrappleKind.Back;
            Vector3 catchPos = toPos + towardReceiver * (behindHold ? -0.015f : 0.24f);
            Vector3 lookDir = behindHold ? -toNext : toNext;

            Quaternion fromRot = transform.rotation;
            Quaternion targetRot = lookDir.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                : fromRot;

            float distance = Vector3.Distance(fromPos, catchPos);
            const float pushSeconds = 0.18f;
            const float impactSeconds = 0.13f;
            const float supportedSeconds = 0.22f;
            float catchSeconds = behindHold ? 0.45f : 0.32f;
            if (toNext.sqrMagnitude > 1e-4f)
                next.transform.rotation = Quaternion.LookRotation(-toNext, Vector3.up);

            // 送り手が両手を背に付けるまでは、その場で踏ん張る。
            float contactAt = Time.time + EnemyBrain.PushContactSeconds;
            while (Time.time < contactAt)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;
                if (_shoveSender == null || !_shoveSender.IsAlive || !_shoveSender.IsPushing)
                {
                    Release();
                    yield break;
                }
            }

            PushDirection = Vector3.ProjectOnPlane(catchPos - fromPos, Vector3.up).normalized;
            Quaternion travelRot = PushDirection.sqrMagnitude > 0f
                ? Quaternion.LookRotation(PushDirection, Vector3.up)
                : fromRot;
            PushPulse = 1f;
            RecoveryDistance = distance;
            const float flightSeconds = 0.33f;
            const float followThroughSeconds = 0.17f;
            const float recoverySeconds = flightSeconds + followThroughSeconds;
            float t = 0f;
            while (t < recoverySeconds)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;

                t += Time.deltaTime;
                float pushProgress = Mathf.Clamp01(t / pushSeconds);
                PushPulse = 1f - pushProgress;
                float flight = Mathf.Clamp01(t / flightSeconds);
                float followThrough = Mathf.InverseLerp(flightSeconds, recoverySeconds, t);
                RecoveryStep = flight + followThrough;
                RecoveryPoseWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.18f, RecoveryStep));
                // 着地の前後で水平速度が急に変わらないようにする。
                float drivenFlight = flight * (1.50f - 0.50f * flight);
                float travel = ShoveLandingFraction * drivenFlight + (1f - ShoveLandingFraction) * followThrough;
                // 押された体が先に動き、足が遅れて床を離れる。
                float airborne = Mathf.InverseLerp(0.12f, 1f, flight);
                RecoveryLift = 0.075f * (4f * airborne * (1f - airborne)) * Mathf.InverseLerp(0.1f, 0.8f, distance);
                RecoveryTravel = distance * travel;
                transform.position = Vector3.Lerp(fromPos, catchPos, travel) + Vector3.up * RecoveryLift;
                transform.rotation = Quaternion.Slerp(fromRot, travelRot, Mathf.SmoothStep(0f, 1f, pushProgress));
            }

            next.BeginCatch(catchSeconds);
            Debug.Log($"[HandOff] catch {next.name} kind={kind}");
            t = 0f;
            Quaternion catchRot = transform.rotation;
            Vector3 receiverStart = next.transform.position;
            Quaternion receiverRot = next.transform.rotation;
            // 受け手の後退は、接触までに残った速度に合わせる。
            Vector3 recoil = towardReceiver * Mathf.Min(0.16f, distance * 0.073f);
            while (t < catchSeconds)
            {
                yield return null;
                if (!CanContinueHandOff(next))
                    yield break;

                t += Time.deltaTime;
                float recoilTime = Mathf.Clamp01(t / impactSeconds);
                float recoilProgress = recoilTime * (2f - recoilTime);
                Vector3 drift = recoil * recoilProgress;
                next.transform.position = receiverStart + drift;

                // 衝突後はまず踏ん張って支える。後ろ拘束への回転は、それから二人で行う。
                float settle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(supportedSeconds, catchSeconds, t));
                HandOffTurn = behindHold ? settle : 0f;
                Quaternion pivot = Quaternion.AngleAxis(18f * HandOffTurn, Vector3.up);
                next.transform.rotation = pivot * receiverRot;
                Vector3 offset = Vector3.Lerp(catchPos, toPos, settle) - receiverStart;
                transform.position = next.transform.position + pivot * offset;
                transform.rotation = Quaternion.Slerp(catchRot, pivot * targetRot, settle);
                RecoveryPoseWeight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(impactSeconds, catchSeconds, t));
            }

            EndShove();
            ApplyHandOffHold(kind);
            _heldSeconds = 0f;
            next.TakeOverHold(CombatConstants.GrappleLimitSeconds + 4f);
            IsHandingOff = false;
            Started?.Invoke(kind);
        }

        void EndShove()
        {
            if (_shoveSender != null)
                _shoveSender.EndPush();
            _shoveSender = null;
            PushPulse = 0f;
            PushDirection = Vector3.zero;
            RecoveryStep = 0f;
            RecoveryTravel = 0f;
            RecoveryLift = 0f;
            RecoveryPoseWeight = 0f;
            HandOffTurn = 0f;
            RecoveryDistance = 0f;
        }

        bool CanContinueHandOff(EnemyBrain next)
        {
            if (!IsHandingOff || Captor != next)
                return false;

            if (next != null && next.IsAlive && next.Phase != EnemyPhase.Stagger &&
                Stats != null && !Stats.IsDead && !Stats.IsDown)
                return true;

            Release();
            return false;
        }

        /// <summary>
        /// 罰なしで解放する（相手が倒れた・プレイヤーが倒れた・腹パン演出が終わった）。
        /// </summary>
        public void Release()
        {
            EndWallSplit();
            EndWallPin();
            bool wasGrappled = IsGrappled;
            ClearPinHandOff();

            if (IsHandingOff)
            {
                if (Captor != null)
                    Captor.EndCatch();
                IsHandingOff = false;
                EndShove();
            }

            IsGrappled = false;
            FrontEscapeEffort = 0f;
            _frontEffortHold = 0f;
            _grabContactRemaining = 0f;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;
            PullPulse = 0f;

            if (wasGrappled)
                Released?.Invoke();
        }

        /// <summary>
        /// 掴まれた瞬間の引き寄せ。相手の体の前に、掴み方に応じた距離まで詰める。
        /// これをしないと、殴り合い用の間合い（1.4m）のまま腕だけ伸びて手が届かない。
        /// </summary>
        void SnapToCaptor(EnemyBrain captor, GrappleKind kind)
        {
            if (captor == null)
                return;

            // 壁押さえつけは「捕まえた相手の前」ではなく「壁の前」に立つ。
            // 向きも -Z（室内側）で固定する。レーンへ戻すときに PlayerMotor が ±X へ直す。
            if (IsWallPin)
            {
                transform.position = _wallPinSpot;
                transform.rotation = _wallPinRotation;
                return;
            }

            Vector3 spot = GrabSpot(captor, kind);
            transform.position = spot;

            // 羽交い締め・頭上拘束は背中を向けたまま掴まれる（向きは変えない）。
            // 押さえつけ / ベアハッグは正面から掴まれるので、相手の方を向かせる。
            if (kind != GrappleKind.Back)
            {
                Vector3 look = captor.transform.position - spot;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        /// <summary>
        /// 掴まれる位置（相手から見て、掴み方に応じた距離）。
        /// 突き飛ばしと瞬間的な寄せの両方で同じ計算を使う。
        /// </summary>
        Vector3 GrabSpot(EnemyBrain captor, GrappleKind kind)
        {
            Vector3 dir = transform.position - captor.transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = -captor.transform.forward;
                dir.y = 0f;
            }

            if (dir.sqrMagnitude < 1e-4f)
                return transform.position;

            dir.Normalize();

            // 羽交い締めは密着させる。押さえつけは腕を伸ばす余地を残し、
            // ベアハッグは胴を抱えるので少しだけ近い。
            float distance = kind switch
            {
                // 頭上拘束も背後から密着して掴む（腕を上げる余地を残し、羽交い締めより少し離す）。
                GrappleKind.Back => BackGrabDistance,
                GrappleKind.BearHug => BearHugDistance,
                _ => GrabDistance
            };
            Vector3 spot = captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            return spot;
        }

        /// <summary>
        /// ベアハッグ中、掴み位置を「密着 → 少し離れる → 密着」と動かす。
        /// ダメージは密着した瞬間（<see cref="IsBearHugContact"/>）だけに入れる。
        /// </summary>
        void TickBearHugBody(float dt)
        {
            if (Captor == null)
                return;

            switch (_hugStage)
            {
                case HugStage.Pull:
                    _hugTime += dt;
                    {
                        float x = Mathf.Clamp01(_hugTime / HugPullSeconds);

                        // 最初の 35% はほとんど動かず（相手が体重を預けて粘る）、
                        // 残りで一気に引き込む。等速で寄せると「滑っている」ように見える。
                        _hugPhase = x < 0.35f
                            ? 0.05f * (x / 0.35f)
                            : 0.05f + 0.95f * Mathf.SmoothStep(
                                0f, 1f, (x - 0.35f) / 0.65f);

                        // 引き寄せの勢いは中盤で最大。密着の瞬間には 0 へ戻る。
                        PullPulse = Mathf.Sin(Mathf.PI * x);

                        if (_hugTime >= HugPullSeconds)
                        {
                            // 引き込みの勢いで少し行き過ぎてから戻る（ぶつかる感じ）。
                            _hugPhase = 1.08f;
                            PullPulse = 0f;
                            _hugStage = HugStage.Contact;
                            _hugTime = 0f;
                        }
                    }
                    break;

                case HugStage.Contact: // 密着（ここでダメージ）
                    _hugTime += dt;
                    _hugPhase = Mathf.MoveTowards(_hugPhase, 1f, dt * 0.8f);
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 12f);
                    if (_hugTime >= HugContactSeconds)
                    {
                        _hugStage = HugStage.Release;
                        _hugTime = 0f;
                    }
                    break;

                case HugStage.Release: // 少し離す
                    _hugTime += dt;
                    _hugPhase = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01(_hugTime / HugReleaseSeconds));
                    PullPulse = Mathf.MoveTowards(PullPulse, 0f, dt * 10f);
                    if (_hugTime >= HugReleaseSeconds)
                    {
                        _hugPhase = 0f;
                        _hugStage = HugStage.Rest;
                        _hugTime = 0f;
                    }
                    break;
            }

            SqueezePulse = Mathf.Clamp01(_hugPhase);

            // 掴み位置の基準（BearHugDistance）から、密着度に応じて前後させる。
            // 方向は「相手から見た彼女」の向きを使い、横へは流れないようにする。
            Vector3 dir = transform.position - Captor.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                dir = -Captor.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f)
                return;
            dir.Normalize();

            float distance = BearHugDistance - HugTightInset * _hugPhase;
            Vector3 spot = Captor.transform.position + dir * distance;
            spot.y = transform.position.y;
            transform.position = spot;
        }

        void Escape()
        {
            EndWallPin();
            var captor = Captor;
            int inputs = InputCount;
            float spent = _heldSeconds;

            ClearPinHandOff();
            IsGrappled = false;
            _remaining = 0f;
            _suppressRemaining = 0f;
            Captor = null;
            _hugStage = HugStage.Rest;
            _hugTime = 0f;
            _hugPhase = 0f;

            PlaySfx(SfxId.Guard, 0.9f);

            // 振りほどいた側に反撃の隙を作る（掴んでいた側がよろける）
            if (captor != null)
                captor.ForceStagger(CombatConstants.GrappleEscapeStun);

            Debug.Log($"[Grapple] escape inputs={inputs} need={RequiredInputs:F1} t={spent:F2}s");
            Escaped?.Invoke();
        }

        /// <summary>
        /// 押さえつけ（Pin）の時間切れ。後ろ拘束では呼ばない。
        /// 壁では投げてから解放する。腹ダメージが上限に達していればうつ伏せに倒れる。
        /// </summary>
        void Fail()
        {
            if (IsWallPin && WallPhase == WallStage.Held && !Stats.IsDown)
            {
                EndWallSplit();
                BeginWallToss();
                return;
            }

            var captor = Captor;

            int inputs = InputCount;
            Release();
            Stats.ResolveHeldBellyDown();

            // 拘束を解いた直後に、別の敵から即座に追撃されないようにする。
            foreach (var enemy in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (enemy == null || enemy == captor || !enemy.IsAlive)
                    continue;

                if (Vector3.Distance(enemy.transform.position, transform.position) > 6f)
                    continue;

                enemy.HoldFire(CombatConstants.GrappleReleaseGraceSeconds);
            }

            Debug.Log($"[Grapple] release kind={Kind} inputs={inputs} need={RequiredInputs:F1}");

            Failed?.Invoke();
        }

        /// <summary>
        /// 壁押さえつけを終える。すぐにレーンへ戻さず、戻りの秒数
        /// （通常 <see cref="WallExitSeconds"/>、投げ捨ては <see cref="ThrowExitSeconds"/>）かけて
        /// z を 0 へ戻す（戻りの姿勢は <see cref="CharacterAnimator"/> が壁の重みで作る）。
        /// </summary>
        void EndWallPin()
        {
            if (!IsWallPin)
                return;

            _wallSite?.RetractAfterRelease();
            _wallSite = null;
            WallPhase = WallStage.None;
            WallPhaseProgress = 0f;
            IsWallPin = false;
            _wallExitFromZ = transform.position.z;

            // 二人がかりの途中で壁拘束が終わったら、二体目も持ち場から離す。
            if (WallAssistCandidate != null)
                WallAssistCandidate.ReleaseWallAssist();
            WallAssistCandidate = null;
            WallAssist = null;
            _wallSplitStarted = false;
            _wallSplitReady = false;
            WallSplitPunchAge = -1f;
            WallSplitPuncher = null;

            // 投げ捨てのときは、飛んでいる間に z をレーンへ戻す（壁の上をすり抜けない）。
            _wallExitSeconds = _wallThrowExitSeconds > 0f ? _wallThrowExitSeconds : WallExitSeconds;
            _wallThrowExitSeconds = 0f;
            _wallExitRemaining = _wallExitSeconds;

            var motor = Motor();
            if (motor != null)
            {
                motor.SetLaneZOverride(_wallExitFromZ);
                motor.SnapToLaneFacing();
            }
        }

        /// <summary>
        /// 壁からレーンへの戻りを打ち切る。引き渡しのように、掴まれたまま
        /// 別の移動（押し出し・寄せ）が始まるときに使う。
        /// </summary>
        void ClearWallExit()
        {
            _wallExitRemaining = 0f;
            Motor()?.ClearLaneZOverride();
        }

        PlayerMotor Motor()
        {
            if (_motor == null)
                _motor = GetComponent<PlayerMotor>();
            return _motor;
        }

        static void PlaySfx(SfxId id, float volume)
        {
            var sfx = ProceduralSfx.Instance;
            if (sfx != null)
                sfx.Play(id, volume);
        }
    }
}
