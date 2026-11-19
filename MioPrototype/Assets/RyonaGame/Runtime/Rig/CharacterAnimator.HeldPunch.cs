using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        /// <summary>
        /// 拘束中に顔面を殴られた反応。頭が手前へ振れ、続いて肩が短く揺れる。
        /// 呼んでも状態は Grabbed のまま。
        /// </summary>
        /// <param name="punchDirection">
        /// 拳が彼女の頭を押す向き（ワールド）。省くと従来どおりカメラ側へ背ける。
        /// 渡すと、殴られた側と逆へ頭が振れて「どちらから来たか」が分かる。
        /// </param>
        /// <param name="strength">衝撃の強さ（0.5〜1.2 程度）。</param>
        public void PlayHeldFacePunch(float seconds, Vector3 punchDirection = default, float strength = 1f,
            float fatigue = 0f, bool finalHit = false, bool keepGroggy = false)
        {
            if (seconds <= 0f)
                return;

            if (!keepGroggy)
            {
                ClearHeldGroggy();
            }
            else
            {
                _heldBellyFinalHit = false;
                _heldFaceFinalHit = false;
            }
            _heldFaceFinalHit = finalHit;
            _heldFaceFatigue = Mathf.Clamp01(fatigue);
            _heldFacePunchTotal = seconds;
            _heldFacePunchRemaining = seconds;
            _heldFacePunchStartFrame = Time.frameCount;
            _heldFacePunchStrength = Mathf.Clamp(strength, 0.2f, 1.5f);

            Vector3 local = transform.InverseTransformDirection(punchDirection);
            local.y = 0f;
            _hasFacePunchDirection = local.sqrMagnitude > 1e-4f;
            if (_hasFacePunchDirection)
            {
                local.Normalize();
                _facePunchLocalX = local.x;
                _facePunchLocalZ = local.z;
            }
        }

        /// <summary>
        /// 正面の敵に投げられ、腹を差し出す体勢へ移る。上体は折らず、腰を引いて腹を前へ出す。
        /// <see cref="EndHeldThrowLead"/> を呼ぶまで保持する。
        /// ダメージの反応ではないので、表情は <see cref="PlayerFace"/> に任せる。
        /// </summary>
        public void PlayHeldThrowLead()
        {
            ClearHeldGroggy();
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
        public void PlayHeldBellyPunch(float seconds, float fatigue = 0f, float impact = 1f,
            float holdSeconds = 0f, bool finalHit = false, bool keepGroggy = false)
        {
            if (seconds <= 0f)
                return;

            float carry = HeldBellyPunchFold;
            // 腹パンの反動の途中から次を受けたときは、一度真っ直ぐへ戻さず折れた位置を継ぐ。
            if (!keepGroggy)
            {
                ClearHeldGroggy();
            }
            else
            {
                _heldBellyFinalHit = false;
                _heldFaceFinalHit = false;
            }
            _heldBellyCarry = carry;
            _heldBellyFatigue = Mathf.Clamp01(fatigue);
            _heldBellyImpact = Mathf.Clamp(impact, 0.5f, 2f);
            _heldBellyHoldSeconds = Mathf.Max(0f, holdSeconds);
            _heldBellyFinalHit = finalHit;
            _heldBellyPunchTotal = seconds;
            _heldBellyPunchRemaining = seconds;
        }

        public void ClearHeldGroggy()
        {
            _heldBellyFinalHit = false;
            _heldFaceFinalHit = false;
            _rearHoldGroggy = 0f;
        }

        internal void SetFinisherHoist(bool active) => _finisherHoistTarget = active;

        internal void PlayHeldRegularHit(HitResult result)
        {
            PunchZone zone = CurrentHitZone ?? PunchZone.Body;
            (float duration, float impact) = result.Stagger switch
            {
                StaggerTier.Large => (0.62f, 1.25f),
                StaggerTier.Medium => (0.54f, 1.08f),
                _ => (0.46f, 0.92f),
            };

            bool groggy = _rearHoldGroggy > 0.2f;
            if (zone == PunchZone.Face)
            {
                Transform src = Stats != null ? Stats.LastAttacker : null;
                Vector3 push;
                if (src != null && src.TryGetComponent<EnemyBrain>(out var attackerBrain))
                    push = src.forward * 0.60f + src.right * (attackerBrain.UsesLeftPunch ? 0.65f : -0.65f);
                else if (src != null)
                    push = transform.position - src.position;
                else
                    push = -transform.forward;
                push.y = 0f;
                PlayHeldFacePunch(duration, push, impact, groggy ? 1f : 0.4f, finalHit: false, keepGroggy: groggy);
            }
            else
            {
                PlayHeldBellyPunch(duration, groggy ? 0.85f : 0.35f, impact, holdSeconds: 0.14f,
                    finalHit: false, keepGroggy: groggy);
            }
        }

        float _heldBellyHoldSeconds;
        bool _heldBellyFinalHit;
        float _heldFaceFatigue;
        bool _heldFaceFinalHit;
        float _rearHoldGroggy;
        bool _finisherHoistTarget;
        public float FinisherHoistWeight { get; private set; }

        /// <summary>
        /// 決めの抱え上げで、腰を構えより何 m 持ち上げるか。
        /// 足先の IK 目標も一緒に上げないと、脚が伸びるだけで床から離れない。
        /// </summary>
        [Tooltip("決めの抱え上げで腰を持ち上げる量（m）")]
        public float HoistHipRise = 0.095f;

        /// <summary>
        /// 決めの抱え上げで、足先の IK 目標を持ち上げる量（m）。
        /// 腰より少し大きくすると膝が曲がって「ぶら下がっている」形になる。
        /// </summary>
        [Tooltip("決めの抱え上げで足先を持ち上げる量（m）。腰より大きくすると膝が曲がる")]
        public float HoistFootRise = 0.125f;

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
                // 締めの膝入れ（重さ 1.2 以上）は、膝がまだ腹にある間に素早く折れると膝へ食い込むので、
                // 膝が引くまでの間に膝を包むようにゆっくり折れる。
                float inSeconds = Mathf.Lerp(0.055f, 0.14f, Mathf.InverseLerp(1.2f, 1.7f, _heldBellyImpact));
                float startFold = Mathf.Max(0.52f, _heldBellyCarry);
                float inT = Mathf.SmoothStep(startFold, 1f, t / inSeconds);
                float recoverAt = _heldBellyHoldSeconds > 0f
                    ? _heldBellyHoldSeconds
                    : _heldBellyPunchTotal * Mathf.Lerp(0.26f, 0.72f, _heldBellyFatigue);
                float outT = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recoverAt, _heldBellyPunchTotal, t));
                return Mathf.Clamp01(inT * outT);
            }
        }

        internal float HeldBellyCaptorLift
        {
            get
            {
                if (_heldBellyPunchRemaining <= 0f || _heldBellyFinalHit || _rearHoldGroggy > 0.01f
                    || _heldBellyPunchTotal <= 0f || _punchDirector == null || !_punchDirector.IsBodySequence)
                    return 0f;
                float progress = 1f - _heldBellyPunchRemaining / _heldBellyPunchTotal;
                // 前半は「く」の字に折れ、後半で拘束役が体勢を引き起こして腹を前へ突き出させる。
                float pull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.68f, progress));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1f, progress)) * 0.12f;
                return pull * (1f - release);
            }
        }

        internal float HeldBellyDangle
        {
            get
            {
                if (!_heldBellyFinalHit || _heldBellyPunchTotal <= 0f)
                    return 0f;
                if (Grapple == null || !Grapple.IsGrappled || Grapple.IsHandingOff || Grapple.Kind != GrappleKind.Back)
                    return 0f;
                float progress = 1f - _heldBellyPunchRemaining / _heldBellyPunchTotal;
                // 最終打の深い折れのあと、羽交い締めの腕の中で意識朦朧と脱力して項垂れる余韻を維持する。
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.58f, progress));
            }
        }

        public float HeldFacePunchFatigue => _heldFaceFatigue;

        internal float HeldFaceDangle
        {
            get
            {
                if (!_heldFaceFinalHit || _heldFacePunchTotal <= 0f)
                    return 0f;
                if (Grapple == null || !Grapple.IsGrappled || Grapple.IsHandingOff || Grapple.Kind != GrappleKind.Back)
                    return 0f;
                float progress = 1f - _heldFacePunchRemaining / _heldFacePunchTotal;
                // 5発目の反動と腕検査（0.55s）が終わった後半（0.42..0.76）で意識が飛んで項垂れる。
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.76f, progress));
            }
        }

        internal float HeldComboDangle =>
            Grapple != null && Grapple.IsGrappled && !Grapple.IsHandingOff && Grapple.Kind == GrappleKind.Back
                ? Mathf.Max(_rearHoldGroggy, Mathf.Max(HeldBellyDangle, HeldFaceDangle))
                : 0f;

        /// <summary>
        /// 腹パンの疲労度 0..1（呼び出し側が渡した値）。壁の二体がかりでは、
        /// 後半の一発ほど深く折って戻りを遅くするのに使う。
        /// </summary>
        public float HeldBellyPunchFatigue => _heldBellyFatigue;

        /// <summary>
        /// 腹パンの重さ（1 = 標準）。壁の二体がかりで、序盤は浅く（&lt;1）、締めは深く（&gt;1）折るのに使う。
        /// </summary>
        public float HeldBellyPunchImpact => _heldBellyImpact;

        /// <summary>
        /// いまの腹パンが当たってからの秒。当たった瞬間の揺れの位相に使う。
        /// 反応が終わっていれば 0。
        /// </summary>
        public float HeldBellyPunchAge =>
            _heldBellyPunchTotal > 0f && _heldBellyPunchRemaining > 0f
                ? _heldBellyPunchTotal - _heldBellyPunchRemaining
                : 0f;

        /// <summary>
        /// 顔面パンのコンボ中に顔を上げておく度合い（0..1）。うつむいたままだと
        /// 前髪が落ちて表情が読めないため、コンボの間だけ顎を上げる。
        /// </summary>
        float _faceComboLift;

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
        bool _groundRolled;

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

    }
}
