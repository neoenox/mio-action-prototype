using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    /// <summary>
    /// 敵の見た目とモーション。プリミティブで組んだ人型を、関節の角度だけで動かす。
    ///
    /// カプセルが近づいて数値だけ減る状態をやめるのが目的。
    /// 「予備動作で拳を引く → 振る → 戻す」が読めれば、避ける判断もできるようになる。
    /// </summary>
    [AddComponentMenu("Ryona/Enemy Figure")]
    // 姿勢は LateUpdate で作る。順番が意味を持つ:
    //   Vrm10Instance(11000) が T ポーズへ戻す
    //   → EnemyFigure(11500) がプリミティブ骨格の姿勢を作る
    //   → 主人公の姿勢 (20000) → 背後拘束の腕 (21000)
    //   → EnemyVrm(22000) がそれを VRM へ写す
    //   → OcclusionFade(23000) が遮蔽を判定する

    [DefaultExecutionOrder(11500)]
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        public EnemyBrain Brain;

        public Material BodyMaterial;

        public Material ClothMaterial;

        /// <summary>髪。未指定なら服の色を暗くして使う。</summary>
        public Material HairMaterial;

        /// <summary>足元から頭まで。モデルの背丈に合わせる。</summary>
        public float Height = 1.75f;

        /// <summary>撮影用。true の間はその場から動かない。</summary>
        [System.NonSerialized] public bool Frozen;
        /// <summary>撮影用。0〜1 を入れると武器の発光量を固定する（-1 で通常）。</summary>
        [System.NonSerialized] public float DebugCharge = -1f;
        Transform _root;
        Transform _hips;
        Transform _chest;
        Transform _head;
        Transform _shoulderR, _elbowR, _shoulderL, _elbowL;
        Transform _wristR, _wristL;
        Transform _fistR, _fistL;
        Transform _hipR, _kneeR, _hipL, _kneeL;
        Transform _ankleR, _ankleL;
        Transform _footR, _footL;
        Transform _weapon;
        MeshRenderer[] _weaponRenderers;

        /// <summary>パイプの材質。予備動作中だけ光らせる。</summary>
        Material _weaponSteel;
        Color _weaponBase = Color.gray;
        float _charge;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        static readonly int ColorId = Shader.PropertyToID("_Color");

        static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");

        /// <summary>服の色から作った派生マテリアル。破棄のために持っておく。</summary>
        readonly List<Material> _ownedMaterials = new List<Material>();
        EnemyPhase _lastPhase = EnemyPhase.Chase;
        Vector3 _lastPosition;
        float _speed;

        /// <summary>実際に進んだ距離。足の運びはこれを基準にする（時間ではない）。</summary>
        float _walkDist;

        /// <summary>体から見た進行方向（x,z）。横歩きでも足を進行方向へ運ぶために持つ。</summary>
        Vector2 _walkDir = new Vector2(0f, 1f);

        /// <summary>立ち止まり中の呼吸の位相。個体ごとにずらして 3 体が同時に膨らまないようにする。</summary>
        float _idlePhase = -1f;

        /// <summary>腰の関節から見た足首の目標位置（左右）。</summary>
        Vector3 _legTargetR, _legTargetL;

        /// <summary>攻撃の踏み込みで root を前へ出している量（m）。</summary>
        float _lungeOffset;

        /// <summary>いま見た目で前へ出ている量。攻撃終了時に実際の位置へ移す。</summary>
        public float LungeOffset => _lungeOffset;
        public void ClearLungeOffset() => _lungeOffset = 0f;
        float _swingTime = -1f;
        // なめらかに寄せるための現在値
        float _hipsY;
        float _hipsZ;   // 踏み込み（前へ出る量）
        float _hipsX;   // 軸足側への体重移動（横歩きで足だけ滑って見えるのを防ぐ）
        float _torsoPitch, _torsoYaw, _torsoRoll, _headPitch;
        float _armRPitch, _armLPitch, _elbowRAngle, _elbowLAngle;
        Vector3 _shoulderRRest, _shoulderLRest, _shoulderROffset, _shoulderLOffset;

        /// <summary>手首の折り（+ で拳が前へ）とひねり（前腕の軸まわり）。</summary>
        float _wristRBend, _wristLBend, _wristRTwist, _wristLTwist;

        /// <summary>肩の開き（横方向）。0 だと腕が前後一直線にしか振れず、機械のレールに見える。</summary>
        float _armRSpread = -8f;
        float _armLSpread = 8f;
        float _legRPitch, _legLPitch, _kneeRAngle, _kneeLAngle;
        float _fallAngle;

        /// <summary>倒れ角（0 = 立ち、90 = 横倒し）。復帰の確認用。</summary>
        public float FallAngle => _fallAngle;

        // --- タッグ技（片手打ち）の構え ---------------------------------
        // 打たない側（左）の腕。脇を締めたまま拳を胸の前に構える。
        // 腰の横へ下ろすと、上体を前へ倒す腹パンの姿勢と重なって
        // 「腕がだらりと下がった」画になる（実測）。
        // 数値を変えて撮り比べられるよう、起動引数で差し替えられるようにする
        // （-tagarmL= / -tagelbowL= / -tagspreadL=）。
        float _tagArmL = 26f;
        float _tagElbowL = 83f;
        float _tagSpreadL = 10f;

        void Awake()
        {
            Build();
            ReadTagPoseArgs();
            if (GetComponent<BackHoldPose>() == null)
                gameObject.AddComponent<BackHoldPose>();
            if (GetComponent<PinHoldPose>() == null)
                gameObject.AddComponent<PinHoldPose>();
            if (GetComponent<BearHugPose>() == null)
                gameObject.AddComponent<BearHugPose>();
            if (GetComponent<OverheadHoldPose>() == null)
                gameObject.AddComponent<OverheadHoldPose>();
            if (GetComponent<WallAssistPose>() == null)
                gameObject.AddComponent<WallAssistPose>();
            // 最初のフレームから正しい立ち姿勢にしておく
            // （補間値が 0 のままだと 1 フレームだけ体が潰れて見える）
            Step(0f, true);
        }

        /// <summary>
        /// タッグ技の構えを起動引数で差し替える（撮り比べ用）。
        /// 例: <c>-tagarmL=-6 -tagelbowL=100 -tagspreadL=8</c>
        /// </summary>
        void ReadTagPoseArgs()
        {
            if (LaunchOptions.TryFloat("-tagarmL", out float armL)) _tagArmL = armL;
            if (LaunchOptions.TryFloat("-tagelbowL", out float elbowL)) _tagElbowL = elbowL;
            if (LaunchOptions.TryFloat("-tagspreadL", out float spreadL)) _tagSpreadL = spreadL;
        }
        // --- 静止画確認用の上書き ---------------------------------------
        // エディタから 1 コマだけ姿勢を見たいときに使う。実行中は常に null。
        bool _previewing;
        EnemyPhase _previewPhase;
        float _previewWindup;
        float _previewStaggerTime = -1f;
        float _previewRise = -1f;
        float _previewSwing;
        AttackId _previewAttack;
        float _previewSpeed;

        /// <summary>
        /// 検証用。左腕の (pitch, elbow, spread) を強制する（エディタの撮り比べ）。
        /// 実行中は常に null。
        /// </summary>
        [System.NonSerialized] public Vector3? ForceLeftArm;

        /// <summary>倒れる進み具合（0→1）。Dead 以外では毎フレーム 0 に戻す。</summary>
        float _deadProgress;

        /// <summary>
        /// ゲームを動かさずに姿勢を確認するための入口。
        /// <paramref name="swing"/> は振り抜きの経過秒（Recover の見た目）。
        /// </summary>
        public void Preview(EnemyPhase phase, float windup, AttackId attack, float speed, float swing = 0.1f)
        {
            Build();
            _previewing = true;
            _previewPhase = phase;
            _previewWindup = windup;
            _previewAttack = attack;
            _previewSpeed = speed;
            _previewSwing = swing;
            _walkDist = 0.15f;    // 歩きの姿勢は接地した途中を見せる
            Step(0f, true);
        }

        /// <summary>よろけの姿勢を任意の経過秒で確認する（撮影用）。</summary>
        public void PreviewStagger(float seconds)
        {
            Build();
            _previewing = true;
            _previewPhase = EnemyPhase.Stagger;
            _previewStaggerTime = seconds;
            _previewWindup = 0f;
            _previewAttack = AttackId.Jab;
            _previewSpeed = 0f;
            _previewSwing = 0f;
            Step(0f, true);
        }

        /// <summary>
        /// 検証用に時間を進める。<paramref name="advancePerSecond"/> ぶん体も動かすので、
        /// 「接地した足が地面に残っているか」を測れる。
        /// <paramref name="worldDirection"/> を渡すと横歩きも試せる。
        /// </summary>
        public void PreviewTick(float dt, float advancePerSecond, Vector3? worldDirection = null)
        {
            Build();
            _previewing = true;
            _previewPhase = EnemyPhase.Chase;
            _previewAttack = AttackId.Jab;
            _previewSpeed = advancePerSecond;
            _previewWindup = 0f;
            _previewSwing = 0.1f;
            Vector3 dir = worldDirection.HasValue ? worldDirection.Value : transform.forward;
            if (dir.sqrMagnitude < 1e-6f)
                dir = transform.forward;
            transform.position += dir.normalized * (advancePerSecond * dt);
            Step(dt, false);
        }

        /// <summary>立ち上がりの姿勢を任意の進み具合（0〜1）で確認する（撮影用）。</summary>
        public void PreviewRise(float rise)
        {
            Build();
            _previewing = true;
            _previewPhase = EnemyPhase.Chase;
            _previewRise = Mathf.Clamp01(rise);
            _previewStaggerTime = -1f;
            _previewWindup = 0f;
            _previewAttack = AttackId.Jab;
            _previewSpeed = 0f;
            _previewSwing = 0f;
            Step(0f, true);
        }

        /// <summary>
        /// 倒れる瞬間を任意の経過秒で確認する（撮影用）。
        /// 立っている姿勢から Dead へ切り替え、少しずつ時間を進めて
        /// <see cref="_fallAngle"/> の補間そのものを見せる。
        /// 一気に進めると補間が 1 フレームで終わってしまうので必ず刻む。
        /// </summary>
        public void PreviewDead(float seconds)
        {
            Build();
            _previewing = true;

            // まず「立っている」姿勢で初期化する（ここを Dead で初期化すると
            // instant で -84 まで寝てしまい、倒れる途中が出せない）。
            _previewPhase = EnemyPhase.Chase;
            _previewRise = -1f;
            _previewStaggerTime = -1f;
            _previewWindup = 0f;
            _previewAttack = AttackId.Jab;
            _previewSpeed = 0f;
            _previewSwing = 0f;
            Step(0f, true);

            _previewPhase = EnemyPhase.Dead;
            float t = 0f;
            const float dt = 1f / 60f;
            while (t < seconds - 1e-4f)
            {
                Step(dt, false);
                t += dt;
            }
        }

        /// <summary>検証用。歩行の位相（0〜1）。0〜0.62 が右足の接地。</summary>
        public float WalkPhase { get; private set; }

        /// <summary>片足が接地している割合。</summary>
        public const float WalkDuty = 0.62f;

        /// <summary>一歩で踏み出す幅。速いほど大きい。</summary>
        public static float WalkReach(float speed)
            => Mathf.Clamp(0.09f + speed * 0.055f, 0.10f, 0.215f);

        // --- 立ち上がりの姿勢（0 / 0.28 / 0.55 / 0.80 / 1.0 の 5 点を補間） -----
        // 仰向け → 上体を起こす → しゃがみ → 立ち上がる。
        // 終点は構え（Chase で速度 0 のときの姿勢）に合わせてある。合わせないと
        // 立ち上がり終わりの 1 フレームで腕と腰が跳ねる。
        static readonly float[] RiseFall = { -84f, -70f, -42f, -18f, -6f, 0f };
        static readonly float[] RiseHip = { 0.35f, 0.26f, 0.24f, 0.42f, 0.66f, 0.94f };
        static readonly float[] RiseTorso = { 0f, -28f, -44f, -36f, -18f, 3f };
        static readonly float[] RiseYaw = { 0f, 0f, 0f, -2f, -4f, -6f };
        static readonly float[] RiseHead = { 0f, -8f, -16f, -14f, -6f, 0f };
        static readonly float[] RiseArmR = { 12f, -14f, -38f, -30f, -24f, -20f };
        static readonly float[] RiseArmL = { 12f, -14f, -38f, -30f, -22f, -13f };
        static readonly float[] RiseElbowR = { 16f, 55f, 82f, 62f, 56f, 54f };
        static readonly float[] RiseElbowL = { 16f, 55f, 82f, 62f, 52f, 46f };
        static readonly float[] RiseKnee = { 12f, 60f, 105f, 100f, 55f, 10f };
        static readonly float[] RiseLeg = { -8f, -2f, 8f, 10f, 4f, 0f };

        static readonly float[] RiseKeys = { 0f, 0.20f, 0.45f, 0.65f, 0.82f, 1f };

        /// <summary>立ち上がりの表から、進み具合（0〜1）の値を読む。</summary>
        static float SampleRise(float[] values, float t)
        {
            for (int i = 1; i < RiseKeys.Length; i++)
            {
                if (t <= RiseKeys[i])
                {
                    float u = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(RiseKeys[i - 1], RiseKeys[i], t));
                    return Mathf.Lerp(values[i - 1], values[i], u);
                }
            }
            return values[values.Length - 1];
        }

        /// <summary>検証用。歩きの位相を指定した状態で固定する。<paramref name="localDirection"/> で横歩きも作れる。</summary>
        public void PreviewWalkAt(float u, float speed, Vector2? localDirection = null)
        {
            Build();
            _previewing = true;
            _previewPhase = EnemyPhase.Chase;
            _previewAttack = AttackId.Jab;
            _previewSpeed = speed;
            _previewWindup = 0f;
            _previewSwing = 0.1f;
            if (localDirection.HasValue)
            {
                Vector2 d = localDirection.Value;
                if (d.sqrMagnitude > 1e-6f)
                    _walkDir = d.normalized;
            }
            float cycle = 2f * WalkReach(speed) / WalkDuty;
            _walkDist = u * cycle;
            Step(0f, true);
        }

        /// <summary>接地の判定用。足（足首の箱）の Transform。<paramref name="left"/> が true で左足。</summary>
        public Transform Foot(bool left) => left ? _footL : _footR;

        /// <summary>予備動作の見え方を測るための拳。<paramref name="left"/> が true で左。</summary>
        public Transform Fist(bool left) => left ? _fistL : _fistR;
        // --- VRM へのリターゲット用（EnemyVrm から読む） -------------------
        public Transform HipsBone => _hips;

        // PinHoldPose から主人公の姿勢更新後に呼ぶ。腿の長さを変えず腹へ膝を合わせる。
        public void AlignPinKnee(bool left, Vector3 belly, float weight)
        {
            Transform hip = left ? _hipL : _hipR;
            Transform knee = left ? _kneeL : _kneeR;
            Transform ankle = left ? _ankleL : _ankleR;
            Transform supportHip = left ? _hipR : _hipL;
            Transform supportKnee = left ? _kneeR : _kneeL;
            Transform supportFoot = left ? _ankleR : _ankleL;
            if (hip == null || knee == null || supportFoot == null) return;
            if (weight <= 0.001f) return;

            Vector3 planted = supportFoot.position;
            Quaternion plantedRotation = supportFoot.rotation;
            float thighLength = Vector3.Distance(hip.position, knee.position);
            if (thighLength < 0.0001f) return;

            // 壁押さえつけは彼女との距離が近く、腿のほうが長い。回転だけでは膝が腹を突き抜け、
            // 骨盤を持ち上げると足が床を離れて彼が浮く。壁中だけ「回転＋平行移動」で
            // 膝関節を腹の表面へ正確に載せる（上下は制限し、しゃがみで高さを合わせる）。
            // レーンの押さえつけは距離があるので、従来どおり回転と小さな踏み込みで合わせる。
            bool wall = Brain != null && Brain.IsWallPinning;
            Vector3 toBelly = belly - hip.position;
            if (toBelly.sqrMagnitude < 0.0001f) return;

            if (wall)
            {
                Quaternion aimed = Quaternion.FromToRotation(knee.position - hip.position, toBelly) * hip.rotation;
                hip.rotation = Quaternion.Slerp(hip.rotation, aimed, weight);
                Vector3 moved = belly - knee.position;
                moved.y = Mathf.Clamp(moved.y, -0.10f, 0.06f);
                float flat = new Vector2(moved.x, moved.z).magnitude;
                if (flat > 0.40f)
                {
                    float scale = 0.40f / flat;
                    moved.x *= scale;
                    moved.z *= scale;
                }

                LastPinKneeReach = toBelly.magnitude - thighLength;
                LastPinKneeShift = moved.magnitude;
                _hips.position += moved;
            }
            else
            {
                Vector3 shift = toBelly.normalized * Mathf.Max(-0.065f, toBelly.magnitude - thighLength);
                LastPinKneeReach = toBelly.magnitude - thighLength;
                LastPinKneeShift = shift.magnitude;
                _hips.position += shift * weight;
                Quaternion aimed = Quaternion.FromToRotation(knee.position - hip.position, toBelly) * hip.rotation;
                hip.rotation = Quaternion.Slerp(hip.rotation, aimed, weight);
            }

            // 膝頭を当て、脛と足先は相手と反対側へ畳む。
            Vector3 shin = -transform.up - transform.forward * 0.65f;
            Quaternion tucked = Quaternion.FromToRotation(ankle.position - knee.position, shin) * knee.rotation;
            knee.rotation = Quaternion.Slerp(knee.rotation, tucked, weight);
            ankle.rotation = Quaternion.Slerp(ankle.rotation, transform.rotation * Quaternion.Euler(55f, 0f, 0f), weight);

            ProceduralRig.SolveTwoBone(supportHip, supportKnee, supportFoot, planted,
                supportHip.position + transform.forward * 0.5f + Vector3.up * 0.15f);
            supportFoot.rotation = plantedRotation;
        }

        public Transform ChestBone => _chest;

        /// <summary>直近の膝合わせで「腹までの距離 − 腿の長さ」。正なら踏み込み、負なら届きすぎ。</summary>
        public float LastPinKneeReach { get; private set; }

        /// <summary>直近の膝合わせで骨盤を動かした距離（m）。診断用。</summary>
        public float LastPinKneeShift { get; private set; }

        public Transform HeadBone => _head;

        public Transform ShoulderBone(bool left) => left ? _shoulderL : _shoulderR;

        public Transform ElbowBone(bool left) => left ? _elbowL : _elbowR;

        /// <summary>
        /// IK で置いた腕を、肘が「ちょうつがい」で曲がる形に収める。
        ///
        /// 敵の肘はローカル X 軸まわりにしか曲がらない（<c>Euler(-角度, 0, 0)</c>、正 = 前腕が前へ）。
        /// 2 ボーン IK は上腕のひねり（ロール）を決めないので、肘と手首の位置は合っていても
        /// 曲がる軸が X 軸と逆を向くことがある。その腕は可動域チェックで「逆方向に折れた」と
        /// 測られ、肘のローカル回転にも横ひねりが乗る。
        ///
        /// 上腕を自分の軸まわりに回して軸を合わせ、肘は純粋な曲げだけにする。
        /// 肘も手首も動かさない（上腕の軸まわりの回転は肘の位置を変えない）ので、見た目の形は同じ。
        /// <paramref name="upper"/> の子が <paramref name="elbow"/>、その子が <paramref name="wrist"/>。
        /// </summary>
        internal static void SettleElbowHinge(Transform upper, Transform elbow, Transform wrist)
        {
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 a = elbow.position - upper.position;
            Vector3 f = wrist.position - elbow.position;
            if (a.sqrMagnitude < 1e-8f || f.sqrMagnitude < 1e-8f)
                return;

            float bend = Vector3.Angle(a, f);
            // ほぼ伸びている（曲げ面が定まらない）、または畳みきり（面が不安定）は触らない。
            if (bend < 1f || bend > 175f)
                return;

            // 前腕は法線 n のまわりを +bend だけ振れている。
            // ローカル X まわりの -bend は「-X 軸まわりの +bend」なので、ローカル X が -n を向けばよい。
            Vector3 n = Vector3.Cross(a, f).normalized;
            Vector3 axisX = Vector3.ProjectOnPlane(upper.rotation * Vector3.right, a);
            if (axisX.sqrMagnitude < 1e-6f)
                return;

            float roll = Vector3.SignedAngle(axisX, -n, a);
            upper.rotation = Quaternion.AngleAxis(roll, a.normalized) * upper.rotation;
            elbow.localRotation = Quaternion.Euler(-bend, 0f, 0f);
        }

        /// <summary>
        /// 手首の骨。ここから拳までの向きが「手の向き」になる。
        /// 無いと VRM の手が前腕と一直線のままになり、棒の先に塊が付いたように見える。
        /// </summary>
        public Transform WristBone(bool left) => left ? _wristL : _wristR;

        public Transform HipBone(bool left) => left ? _hipL : _hipR;

        public Transform KneeBone(bool left) => left ? _kneeL : _kneeR;

        public Transform AnkleBone(bool left) => left ? _ankleL : _ankleR;

        /// <summary>
        /// VRM に差し替えるとき、プリミティブの見た目だけ消す。
        /// Transform は残すので、姿勢の計算・接地判定・攻撃の当たり判定はそのまま動く。
        /// </summary>
        public void HidePrimitives()
        {
            Build();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                r.enabled = false;
        }

        /// <summary>右手の武器。VRM に差し替えたあともここに描く。</summary>
        public Transform WeaponPivot => _weapon;

        /// <summary>
        /// 武器だけ見えるように戻す。
        /// 武器は「手に握られている」ので、VRM の手の位置とほぼ一致する。
        /// プリミティブの拳の位置に描いておけば、腕の向きは VRM と揃う。
        /// </summary>
        public void ShowWeapon()
        {
            SetWeaponVisible(true);
        }

        /// <summary>
        /// 武器の表示切り替え。掴みは両手を使うので、掴みかかっている間はパイプを隠す。
        /// </summary>
        public void SetWeaponVisible(bool visible)
        {
            if (_weapon == null)
                return;
            if (_weaponRenderers == null)
                _weaponRenderers = _weapon.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < _weaponRenderers.Length; i++)
            {
                if (_weaponRenderers[i] != null && _weaponRenderers[i].enabled != visible)
                    _weaponRenderers[i].enabled = visible;
            }
        }
        EnemyPhase PhaseNow => _previewing ? _previewPhase : (Brain != null ? Brain.Phase : EnemyPhase.Chase);
        float WindupNow => _previewing ? _previewWindup : (Brain != null ? Brain.WindupProgress : 0f);
        AttackId AttackNow => _previewing ? _previewAttack : (Brain != null ? Brain.CurrentAttack : AttackId.Jab);
        float SpeedNow => _previewing ? _previewSpeed : _speed;
        float RiseNow => _previewing
            ? (_previewRise >= 0f ? _previewRise : 0f)
            : (Brain != null ? Brain.RiseProgress : 0f);
        bool RisingNow => _previewing
            ? _previewRise >= 0f
            : (Brain != null && Brain.IsRising);
        void LateUpdate()
        {
            if (_previewing)
                return;   // 確認用に固定したまま
            if (Frozen)
            {
                // 撮影用。姿勢だけ毎フレーム作り直し、位置は動かさない
                Step(0f, true);
                return;
            }
            Step(Time.deltaTime, false);
        }
    }
}
