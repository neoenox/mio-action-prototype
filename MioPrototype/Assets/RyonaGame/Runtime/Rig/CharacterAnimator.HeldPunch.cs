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
        public void PlayHeldFacePunch(float seconds, Vector3 punchDirection = default, float strength = 1f)
        {
            if (seconds <= 0f)
                return;

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

        /// <summary>
        /// 腹パンの疲労度 0..1（呼び出し側が渡した値）。壁の二体がかりでは、
        /// 後半の一発ほど深く折って戻りを遅くするのに使う。
        /// </summary>
        public float HeldBellyPunchFatigue => _heldBellyFatigue;

        /// <summary>
        /// いまの腹パンが当たってからの秒。当たった瞬間の揺れの位相に使う。
        /// 反応が終わっていれば 0。
        /// </summary>
        public float HeldBellyPunchAge =>
            _heldBellyPunchTotal > 0f && _heldBellyPunchRemaining > 0f
                ? _heldBellyPunchTotal - _heldBellyPunchRemaining
                : 0f;

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
