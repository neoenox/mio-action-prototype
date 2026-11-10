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
    public sealed class EnemyFigure : MonoBehaviour
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
        float _tagArmL = -12f;
        float _tagElbowL = 126f;
        float _tagSpreadL = 2f;

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
            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (TryReadFloat(arg, "-tagarmL=", out float armL)) _tagArmL = armL;
                else if (TryReadFloat(arg, "-tagelbowL=", out float elbowL)) _tagElbowL = elbowL;
                else if (TryReadFloat(arg, "-tagspreadL=", out float spreadL)) _tagSpreadL = spreadL;
            }
        }

        static bool TryReadFloat(string arg, string key, out float value)
        {
            value = 0f;
            if (!arg.StartsWith(key, System.StringComparison.OrdinalIgnoreCase))
                return false;
            return float.TryParse(arg.Substring(key.Length),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
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

            Vector3 planted = supportFoot.position;
            Quaternion plantedRotation = supportFoot.rotation;
            float thighLength = Vector3.Distance(hip.position, knee.position);
            Vector3 toBelly = belly - hip.position;
            if (toBelly.sqrMagnitude < 0.0001f) return;

            // 骨盤も合わせて動かし、腿の長さを保ったまま膝頭を腹の表面で止める。
            Vector3 shift = toBelly.normalized * Mathf.Max(-0.065f, toBelly.magnitude - thighLength);
            _hips.position += shift * weight;
            Quaternion aimed = Quaternion.FromToRotation(knee.position - hip.position, belly - hip.position) * hip.rotation;
            hip.rotation = Quaternion.Slerp(hip.rotation, aimed, weight);

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
        void Step(float dt, bool instant)
        {
            if (!instant && dt <= 0f)
                return;
            if (!instant)
            {
                Vector3 delta = transform.position - _lastPosition;
                _lastPosition = transform.position;
                Vector3 flat = new Vector3(delta.x, 0f, delta.z);
                // 係数を固定値で掛けるとフレームレートで平滑の強さが変わる（歩く速さが FPS 依存になる）
                float speedBlend = 1f - Mathf.Exp(-15f * dt);
                _speed = Mathf.Lerp(_speed, flat.magnitude / dt, speedBlend);
                // 進んだ距離をそのまま足の運びに使う。時間で位相を回すと速度とズレて滑る。
                _walkDist += flat.magnitude;
                // 体から見た進行方向。横歩きでも足を進行方向へ運ぶため
                if (flat.sqrMagnitude > 1e-8f)
                {
                    Vector3 localDir = transform.InverseTransformDirection(flat);
                    Vector2 wanted = new Vector2(localDir.x, localDir.z).normalized;
                    Vector2 blended = Vector2.Lerp(_walkDir, wanted, 1f - Mathf.Exp(-12f * dt));
                    if (blended.sqrMagnitude > 1e-6f)
                        _walkDir = blended.normalized;
                }
            }
            float speed = SpeedNow;
            EnemyPhase phase = PhaseNow;
            // 倒れの進み具合は Dead のときだけ進める（他の状態では 0）。
            if (phase != EnemyPhase.Dead)
                _deadProgress = 0f;
            if (!instant && phase != _lastPhase)
            {
                // 攻撃の瞬間は 1 フレームしかないので、振り抜きはここから自分で再生する
                if (phase == EnemyPhase.Recover && _lastPhase == EnemyPhase.Attack)
                    _swingTime = 0.20f;
                _lastPhase = phase;
            }
            if (instant)
                _swingTime = phase == EnemyPhase.Recover ? _previewSwing : -1f;
            else if (phase != EnemyPhase.Recover)
                _swingTime = -1f;
            else if (_swingTime >= 0f)
                _swingTime += dt;
            // --- 目標姿勢を組み立てる ---------------------------------
            float restY = Height * 0.515f;
            float hipsY = restY;
            float hipsZ = 0f;
         // + で前へ踏み込む
            float hipsX = 0f;         // + で右へ（軸足へ体重を移す）
            float torsoPitch = 0f;    // - で前傾（胸の X 回転には符号を反転して適用する）
            float torsoYaw = 0f;      // + で左へひねる
            float torsoRoll = 0f;     // + で左へ傾く
            float headPitch = 0f;
            float armRPitch = -8f;    // - で前、+ で後ろ
            float armLPitch = -8f;
            float armRSpread = -8f;   // 肩の開き（右は負が基準）
            float armLSpread = 8f;
            float elbowR = 22f;
            float elbowL = 22f;
            float legRPitch = 0f;
            float legLPitch = 0f;
            float kneeR = 10f;
            float kneeL = 10f;
            float fall = 0f;
            bool legsExact = false;   // 歩行中は脚を補間しない（接地を保つ）
            switch (phase)
            {
                case EnemyPhase.Chase:
                case EnemyPhase.Strafe:
                {
                    float amp = Mathf.Clamp01(speed / 1.2f);
                    // 一歩で踏み出す幅。速いほど大きく、届く範囲まで
                    float reach = WalkReach(speed);
                    // 横歩きは歩幅が短い。前と同じ幅で横へ運ぶと、引き足が軸足を
                    // 追い越して足が交差してしまう。横成分が増えるほど詰める。
                    Vector2 dir = _walkDir;
                    float latness = Mathf.Abs(dir.x);
                    float fwdness = Mathf.Abs(dir.y);
                    float reachEff = Mathf.Lerp(reach, reach * 0.55f, latness);
                    float cycle = 2f * reachEff / WalkDuty;
          // 1往復（2歩）で進む距離
                    float u = cycle > 0.01f ? Mathf.Repeat(_walkDist / cycle, 1f) : 0f;
                    // 止まったときに遊脚が宙に浮いたままにならないよう、着地まで進める
                    if (speed < 0.15f && cycle > 0.01f && u > WalkDuty)
                    {
                        _walkDist += 0.35f * dt;
                        u = Mathf.Repeat(_walkDist / cycle, 1f);
                    }
                    // 距離を溜め続けると精度が落ちるので、1周期分に丸めておく
                    if (cycle > 0.01f)
                        _walkDist = Mathf.Repeat(_walkDist, cycle);
                    WalkPhase = u;
                    // 腰は軽く落とす。脚を伸ばしきらずに済むので、踏み出しても届く
                    float hipBase = Height * 0.455f + 0.008f * Mathf.Cos(4f * Mathf.PI * u);
                    hipsY = hipBase + Height * 0.030f;
                    // 足首の目標。進行方向（横歩きなら横）へ運ぶ
                    FootPath(u, reachEff, WalkDuty, out float sweepR, out float liftR);
                    FootPath(Mathf.Repeat(u + 0.5f, 1f), reachEff, WalkDuty, out float sweepL, out float liftL);
                    // 体重を軸足へ預ける。これが無いと足だけが横へ滑って「跳び箱」に見える。
                    // 軸足は接地中の側なので、右足が着地する u=0 から 0.30 あたりが右、
                    // 0.80 あたりが左。
                    hipsX = Mathf.Cos(2f * Mathf.PI * (u - 0.30f)) * Height * 0.016f * amp;
                    // 進行方向へ上体を傾ける。横歩きは横へ、後退りは傾けない
                    torsoRoll = -dir.x * 4.5f * amp;
                    _legTargetR = new Vector3(dir.x * sweepR, liftR - hipBase, dir.y * sweepR);
                    _legTargetL = new Vector3(dir.x * sweepL, liftL - hipBase, dir.y * sweepL);
                    // 接地中の足は地面に固定したいので、脚の角度は補間せずそのまま使う
                    legsExact = true;
                    float s = -Mathf.Cos(2f * Mathf.PI * u);
                    // 横歩きでは前後の腕振りを減らし、代わりに腕を横へ小さく振る。
                    // 前後だけだと、横へ動いているのに「その場足踏み」に見える。
                    armRPitch = -s * 22f * amp * fwdness;
                    armLPitch = s * 22f * amp * fwdness;
                    armRSpread = -8f - s * 6f * amp * latness;
                    armLSpread = 8f - s * 6f * amp * latness;
                    elbowR = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    elbowL = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    torsoPitch = 5f * amp * (dir.y >= 0f ? dir.y : dir.y * 0.45f);
                    torsoYaw = s * 4f * amp;
                    // 止まっているときは腕を下ろしたままにせず、拳（武器）を軽く構える。
                    // 歩いている最中に混ぜると腕振りが消えるので、速度が落ちた分だけ効かせる。
                    float guard = Mathf.Clamp01(1f - amp * 2f);
                    if (guard > 0f)
                    {
                        armRPitch += -20f * guard;
                        elbowR += 30f * guard;
                        armLPitch += -13f * guard;
                        elbowL += 24f * guard;
                        torsoPitch += 3f * guard;
                        torsoYaw += -6f * guard;      // 半身になって相手の正面を外す
                        // 立ち止まっている間も呼吸と重心移動を入れる。
                        // 完全に静止すると「置物」に見え、こちらを狙っている感じが消える。
                        // 個体ごとに位相をずらして、3 体が同時に同じ呼吸をしないようにする。
                        if (_idlePhase < 0f)
                            _idlePhase = Random.value * 6.283f;
                        float idle = Time.time * 1.6f + _idlePhase;
                        float breath = Mathf.Sin(idle) * guard;
                        torsoPitch += breath * 0.9f;
                        torsoYaw += Mathf.Sin(idle * 0.37f) * 1.6f * guard;
                        hipsY += breath * 0.004f;
                        armRPitch += breath * 1.2f;
                        armLPitch += breath * 1.2f;
                        elbowR += breath * 1.8f;
                        elbowL += breath * 1.8f;
                    }
                    break;
                }
                case EnemyPhase.Windup:
                {
                    float t = WindupNow;
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    // 引きは素早く見せ、引き切ったら姿勢を止めて溜める。
                    // 溜めの間ずっと動き続けると「いつ振るか」が読めないので、止める時間を作る。
                    const float pull = 0.55f;
                    float x = Mathf.Clamp01(t / pull);
                    float k = 1f - (1f - x) * (1f - x);
                    // 拳は腰の後ろへ。肘を曲げたまま高く上げると「ガード」に見えるので、
                    // 腕ごと後ろへ引いて「振りかぶっている」形にする。
                    armRPitch = Mathf.Lerp(-8f, CockArm(attack), k);
                    elbowR = Mathf.Lerp(22f, CockElbow(attack), k);
                    // 振りかぶるほど肩を開く。前後（ピッチ）だけで振ると「蝶番」に見え、
                    // 肩が入っていない拳になる。開きは振り切る直前に戻す。
                    armRSpread = Mathf.Lerp(-8f, -21f, k);
                    armLSpread = Mathf.Lerp(8f, breakAttack ? 17f : 11f, k);
                    // 引き切ったあとも上体だけ僅かにひねり続ける（固まって見えないように）
                    float creep = Mathf.Clamp01((t - pull) / Mathf.Max(0.01f, 1f - pull));
                    torsoYaw = -Mathf.Lerp(0f, CockYaw(attack), k) - 3f * creep;
                    torsoPitch = Mathf.Lerp(4f, CockPitch(attack), k);
                    hipsY = restY - 0.05f * k;
                    // 振りかぶるほど体重を後ろへ預ける（振り始めの踏み込みが効く）
                    hipsZ = -0.035f * k;
                    // 崩しは両手を引く（見てから避けにくい代わりに、両腕が動く）
                    if (breakAttack)
                    {
                        armLPitch = Mathf.Lerp(-8f, 58f, k);
                        elbowL = Mathf.Lerp(22f, 46f, k);
                    }
                    else
                    {
                        armLPitch = Mathf.Lerp(-8f, -26f, k);
                        elbowL = Mathf.Lerp(22f, 70f, k);
                    }
                    legRPitch = Mathf.Lerp(0f, 8f, k);
                    legLPitch = Mathf.Lerp(0f, -10f, k);
                    kneeR = Mathf.Lerp(10f, 24f, k);
                    kneeL = Mathf.Lerp(10f, 26f, k);
                    // 振り切る直前。予備動作の最後で腕を打ち込み始める。
                    // ここを入れないと「当たった瞬間はまだ振りかぶったまま」に見える。
                    float release = Mathf.Clamp01((t - 0.88f) / 0.12f);
                    if (release > 0f)
                    {
                        armRPitch = Mathf.Lerp(armRPitch, StrikeArm(attack), release);
                        elbowR = Mathf.Lerp(elbowR, StrikeElbow(attack), release);
                        armRSpread = Mathf.Lerp(armRSpread, -12f, release);
                        armLSpread = Mathf.Lerp(armLSpread, breakAttack ? 9f : 7f, release);
                        torsoYaw = Mathf.Lerp(torsoYaw, StrikeYaw(attack), release);
                        torsoPitch = Mathf.Lerp(torsoPitch, StrikePitch(attack), release);
                        hipsZ = Mathf.Lerp(hipsZ, StrikeHips(attack), release);
                        headPitch = Mathf.Lerp(headPitch, 7f, release);
                    }
                    break;
                }
                case EnemyPhase.Attack:
                {
                    // 判定のフレーム。ここが「当たった瞬間」なので、腕を伸ばし切った形を出す。
                    // 以前は case が無く、当たりの 1 フレームだけ立ち姿に戻ってしまっていた。
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    armRPitch = StrikeArm(attack);
                    elbowR = StrikeElbow(attack);
                    armRSpread = -12f;
                    armLSpread = breakAttack ? 9f : 7f;
                    torsoYaw = StrikeYaw(attack);
                    torsoPitch = StrikePitch(attack);
                    headPitch = 7f;
                    hipsY = restY - 0.10f;
                    hipsZ = StrikeHips(attack);
                    if (breakAttack)
                    {
                        armLPitch = -70f;
                        elbowL = 10f;
                    }
                    else
                    {
                        armLPitch = -34f;
                        elbowL = 86f;
                    }
                    legRPitch = -6f;
                    legLPitch = 12f;
                    kneeR = 14f;
                    kneeL = 12f;
                    break;
                }
                case EnemyPhase.Recover:
                {
                    // 振り抜き。0〜1 で伸びきり、その後ゆっくり戻す。
                    // 速く振って長く止める（0.055 で振り切り、0.24 まで伸びたまま）と
                    // 「重いものを振った」感触が出る。
                    float s = 0f;
                    if (_swingTime >= 0f)
                    {
                        float t = _swingTime;
                        if (Brain != null && Brain.IsTagPunch && Brain.Zone == PunchZone.Body)
                            s = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.20f) / 0.17f);
                        else if (t < 0.055f)
                            s = Mathf.SmoothStep(0f, 1f, t / 0.055f);
                        else if (t < 0.24f)
                            s = 1f;
                        else
                            s = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.24f) / 0.18f);
                    }
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    // 予備動作の終わりと同じ姿勢から振り始める（切り替わりで飛ばないように）
                    armRPitch = Mathf.Lerp(CockArm(attack), StrikeArm(attack), s);
                    elbowR = Mathf.Lerp(CockElbow(attack), StrikeElbow(attack), s);
                    torsoYaw = Mathf.Lerp(-CockYaw(attack), StrikeYaw(attack), s);
                    torsoPitch = Mathf.Lerp(CockPitch(attack), StrikePitch(attack), s);
                    hipsY = restY - Mathf.Lerp(0.05f, 0.10f, s);
                    headPitch = Mathf.Lerp(0f, 7f, s);
                    // 振り抜きに合わせて踏み込む（腰ごと前へ出ると打撃の重さが出る）
                    hipsZ = Mathf.Lerp(-0.02f, StrikeHips(attack), s);
                    if (breakAttack)
                    {
                        armLPitch = Mathf.Lerp(58f, -70f, s);
                        elbowL = Mathf.Lerp(46f, 10f, s);
                    }
                    else
                    {
                        armLPitch = Mathf.Lerp(-26f, -34f, s);
                        elbowL = Mathf.Lerp(70f, 86f, s);
                    }
                    legRPitch = Mathf.Lerp(8f, -6f, s);
                    legLPitch = Mathf.Lerp(-10f, 12f, s);
                    kneeR = Mathf.Lerp(24f, 14f, s);
                    kneeL = Mathf.Lerp(26f, 12f, s);
                    break;
                }
                case EnemyPhase.Stagger:
                {
                    // 一定の姿勢を出すと「止め絵」に見えるので、殴られた瞬間を山として減衰させる。
                    float e;
                    float w;
                    float heaviness;
                    if (_previewing && _previewStaggerTime >= 0f)
                    {
                        e = _previewStaggerTime;
                        w = Mathf.Clamp01(1f - e / 0.32f);
                        // シートは「重い一撃」を見たいので、こらえる2段目まで出す
                        heaviness = 1.8f;
                    }
                    else
                    {
                        e = _previewing ? 0f : (Brain != null ? Brain.StaggerElapsed : 0f);
                        w = _previewing ? 1f : (Brain != null ? Brain.StaggerProgress : 1f);
                        heaviness = _previewing ? 1.4f : (Brain != null ? Brain.StaggerWeight : 1.4f);
                    }
                    w = Mathf.Clamp01(w);
                    // 頭と腕は体より 50〜90ms 遅れて付いてくる。
                    // 全部が同じフレームで振り切れると「棒が倒れた」ように見える。
                    float lagHead = Mathf.Clamp01((e - 0.05f) / 0.05f);
                    float lagArm = Mathf.Clamp01((e - 0.08f) / 0.09f);
                    float back = 31f * w * heaviness;
                    float settle = Mathf.Sin(e * 16f) * Mathf.Exp(-e * 7f);
                    // 上体は真後ろへ反るだけにせず、殴られた側へひねる。
                    // ひねりが無いと「自分で反った」ようにしか見えない。
                    torsoPitch = 4f - back - 4f * settle;
                    torsoYaw = (26f * w + 7f * settle) * heaviness * 0.8f;
                    // 首は体のあとから落ち、戻りで一度だけ前に残る。
                    headPitch = 2f - back * 0.72f * lagHead + 4f * settle;
                    // 腕は振り遅れて外へ流れる。殴られた側（右）ほど大きく。
                    armRPitch = 24f + 34f * w * lagArm * heaviness * 0.7f;
                    armLPitch = 24f + 16f * w * lagArm;
                    elbowR = 46f - 18f * w;
                    elbowL = 40f - 8f * w;
                    // 膝が抜けて腰が落ち、同時に後ろへ押される。
                    hipsY = restY - 0.03f - 0.06f * w * heaviness;
                    hipsZ = -0.03f - 0.12f * w * heaviness;
                    // 前の足が突っ張り、後ろの足が引いて体重を逃がす。
                    legLPitch = -11f * w;
                    legRPitch = 13f * w;
                    kneeL = 10f + 8f * w;
                    kneeR = 10f + 20f * w * heaviness * 0.7f;
                    // 重い一撃は「のけぞる → こらえる」の2段にする。
                    // 反り切ったあと前へ折れて腹を抱えるので、単調な往復に見えない。
                    // （小さい一撃でこれをやると、いちいち痛がりすぎて戦闘が止まる）
                    if (heaviness >= 1.6f)
                    {
                        float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - 0.14f) / 0.14f))
                            * Mathf.Clamp01(1f - (e - 0.34f) / 0.26f);
                        torsoPitch += 24f * fold;
                        torsoYaw += -10f * fold;
                        headPitch += 8f * fold;       // 上体が折れた分、顔も落ちる
                        hipsY -= 0.05f * fold;
                        kneeR += 12f * fold;
                        // 腕は前へ伸ばさず、腹の前へ引き寄せる（伸ばすと「掴もうとしている」に見える）
                        armRPitch += -12f * fold;
                        armLPitch += -10f * fold;
                        elbowR += 8f * fold;
                        elbowL += 6f * fold;
                    }
                    break;
                }
                case EnemyPhase.Dead:
                {
                    // 倒れる進み具合。腰・膝・腕を「崩れる順」に動かすために持つ。
                    _deadProgress = instant
                        ? 1f
                        : Mathf.Clamp01(_deadProgress + dt / 0.5f);
                    float p = Mathf.SmoothStep(0f, 1f, _deadProgress);

                    // 膝が抜ける。途中で一番深く畳み、寝たら伸びる。
                    // 脚を伸ばしたまま根元を回すと、脚が床を突き抜けて
                    // 「板が倒れる」ように見える（実測でそうなっていた）。
                    float buckle = Mathf.Sin(_deadProgress * Mathf.PI);

                    fall = -84f;
                    hipsY = Mathf.Lerp(restY, restY * 0.35f, p);
                    armRPitch = 12f - 26f * buckle;
                    armLPitch = 12f - 24f * buckle;
                    elbowR = 16f + 26f * buckle;
                    elbowL = 16f + 24f * buckle;
                    legRPitch = -8f - 14f * buckle;
                    legLPitch = -8f - 12f * buckle;
                    kneeR = 12f + 66f * buckle;
                    kneeL = 12f + 60f * buckle;
                    break;
                }
            }
            // --- 掴み: 両腕を前へ伸ばして掴みかかる ----------------------
            if (AttackNow == AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))
            {
                if (phase == EnemyPhase.Windup)
                {
                    // 予備動作: まず両腕を横へ大きく開く（武器を下ろした合図）。
                    // いきなり前へ出すと、突きの構えと見分けが付きにくい。
                    // 開き切ってから掴む直前に前へ寄せる、の2段にする。
                    float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(WindupNow / 0.62f));
                    float close = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((WindupNow - 0.72f) / 0.28f));

                    // 腕は前後（ピッチ）でしか振れないリグなので「横に開く」は使えない。
                    // 掴みは「両腕を低く前へ出して腰を落とす」で読ませる。
                    armRPitch = Mathf.Lerp(-8f, -30f, open);
                    armLPitch = Mathf.Lerp(-8f, -30f, open);
                    elbowR = Mathf.Lerp(22f, 58f, open);
                    elbowL = Mathf.Lerp(22f, 58f, open);
                    armRSpread = Mathf.Lerp(-8f, -14f, open);
                    armLSpread = Mathf.Lerp(8f, 14f, open);

                    armRPitch = Mathf.Lerp(armRPitch, -76f, close);
                    armLPitch = Mathf.Lerp(armLPitch, -72f, close);
                    elbowR = Mathf.Lerp(elbowR, 18f, close);
                    elbowL = Mathf.Lerp(elbowL, 24f, close);
                    armRSpread = Mathf.Lerp(armRSpread, -14f, close);
                    armLSpread = Mathf.Lerp(armLSpread, 14f, close);

                    torsoPitch = Mathf.Lerp(4f * open, -18f, close);
                    torsoYaw = 0f;
                    hipsY = restY - 0.045f * open - 0.055f * close;
                    hipsZ = Mathf.Lerp(-0.035f * open, 0.08f, close);
                    kneeR = Mathf.Lerp(10f, 26f, open);
                    kneeL = Mathf.Lerp(10f, 30f, open);
                    legRPitch = 0f;
                    legLPitch = 0f;
                }
                else
                {
                    // 掴んだ後: 腕を伸ばしたまま。掴めていなければ引く。
                    bool holding = _previewing || (Brain != null && Brain.IsHolding);
                    float retract = holding
                        ? 0f
                        : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_swingTime / 0.22f));

                    // 掴んだ後: 上体を前へ倒し、腕を下ろして彼女の肩・上腕に手を乗せる。
                    // 彼（肩 1.45m）と彼女（構えで肩 1.15m）は 30cm 差があるので、
                    // 水平に伸ばすと手が頭の高さで止まる。前傾＋腕を下ろすで合わせる。
                    armRPitch = Mathf.Lerp(-76f, -8f, retract);
                    armLPitch = Mathf.Lerp(-74f, -8f, retract);
                    elbowR = Mathf.Lerp(14f, 22f, retract);
                    elbowL = Mathf.Lerp(18f, 22f, retract);
                    armRSpread = Mathf.Lerp(-14f, -8f, retract);
                    armLSpread = Mathf.Lerp(14f, 8f, retract);
                    torsoPitch = Mathf.Lerp(-12f, 4f, retract);
                    torsoYaw = 0f;
                    headPitch = Mathf.Lerp(-12f, 2f, retract);   // 掴んでいる間は相手を見下ろす（正=上向き）
                    hipsY = restY - Mathf.Lerp(0.07f, 0.03f, retract);
                    hipsZ = Mathf.Lerp(0.06f, 0f, retract);
                    kneeR = Mathf.Lerp(26f, 10f, retract);
                    kneeL = Mathf.Lerp(22f, 10f, retract);
                    legRPitch = 0f;
                    legLPitch = 0f;
                    var hold = Brain != null && Brain.Player != null
                        ? Brain.Player.GetComponent<PlayerGrapple>() : null;
                    if (holding && hold != null && hold.Kind == GrappleKind.Back)
                    {
                        armRPitch = -78f;
                        armLPitch = -78f;
                        elbowR = 28f;
                        elbowL = 28f;
                        armRSpread = -28f;
                        armLSpread = 28f;
                        torsoPitch = -16f;
                        hipsY = restY - 0.17f;
                        // 前後に足を割って踏ん張る（両足を揃えて沈むと「座っている」画になる）。
                        // 彼女に近い足を前へ、反対の足を後ろへ引く。
                        kneeR = 62f;
                        kneeL = 40f;
                        legRPitch = -16f;
                        legLPitch = 14f;
                        headPitch = 0f;
                    }
                }

                if (Brain != null && Brain.IsPreparingGrab)
                {
                    float step = Mathf.InverseLerp(0.44f, 0.90f, WindupNow);
                    float advance = Brain.GrabAdvance;
                    float floor = Height * 0.030f - hipsY;
                    float forward = Mathf.SmoothStep(0f, 0.21f, step);
                    _legTargetR = new Vector3(0f, floor + 0.085f * Mathf.Sin(Mathf.PI * step), forward - hipsZ);
                    _legTargetL = new Vector3(0f, floor, -0.07f - Mathf.Min(advance, 0.30f) - hipsZ);
                    legsExact = true;
                }
            }
            if (Brain != null && Brain.HeldThrowPull > 0f)
            {
                float pull = Brain.HeldThrowPull;
                torsoPitch += 18f * pull;
                hipsY -= 0.08f * pull;
                kneeR += 16f * pull;
                kneeL += 10f * pull;
            }

            // ボディーブロー: 腹へ届くよう、腰を落として上体を前へ倒す。
            // 顔を殴るときは立ったまま、腹を殴るときは沈む。構えを見れば狙いが読める。
            if (Brain != null && Brain.Zone == PunchZone.Body && AttackNow != AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))
            {
                float bw = Brain.PunchPoseWeight;
                if (Brain.IsBellyPunch)
                {
                    // 先に膝を曲げて拳を脇に溜め、前足に体重を移しながら腰を返す。
                    float bodyDrive = Brain.BellyPunchDrive;
                    torsoYaw = Mathf.Lerp(24f, -31f, bodyDrive) * bw;
                    torsoPitch = Mathf.Lerp(torsoPitch, Mathf.Lerp(-14f, -33f, bodyDrive), bw);
                    hipsY = Mathf.Lerp(hipsY, restY - Mathf.Lerp(0.27f, 0.23f, bodyDrive), bw);
                    hipsZ = Mathf.Lerp(hipsZ, 0.11f * bodyDrive, bw);
                    kneeR = Mathf.Lerp(kneeR, Mathf.Lerp(48f, 67f, bodyDrive), bw);
                    kneeL = Mathf.Lerp(kneeL, Mathf.Lerp(61f, 34f, bodyDrive), bw);
                    legRPitch = Mathf.Lerp(legRPitch, Mathf.Lerp(-20f, -29f, bodyDrive), bw);
                    legLPitch = Mathf.Lerp(legLPitch, Mathf.Lerp(-4f, 18f, bodyDrive), bw);
                }
                else
                {
                    torsoPitch -= 16f * bw;
                    hipsY -= 0.10f * bw;
                    kneeR += 14f * bw;
                    kneeL += 14f * bw;
                }
            }
            // 拳を引いても次の打撃までは腰を上げない。顔を打つ瞬間だけ必要な高さを戻す。
            if (Brain != null && Brain.HoldsBellyPunchGuard &&
                (phase == EnemyPhase.Recover || phase == EnemyPhase.Chase || phase == EnemyPhase.Strafe ||
                 (Brain.IsTagPunch && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack))))
            {
                float guard = Brain.IsTagPunch && Brain.Zone == PunchZone.Face ? 1f - Brain.PunchPoseWeight : 1f;
                float lowHips = Mathf.Min(hipsY, restY - 0.20f * guard);
                if (legsExact)
                {
                    _legTargetR.y += hipsY - lowHips;
                    _legTargetL.y += hipsY - lowHips;
                }
                hipsY = lowHips;
                torsoPitch = Mathf.Min(torsoPitch, -13f * guard);
                kneeR = Mathf.Max(kneeR, 35f * guard);
                kneeL = Mathf.Max(kneeL, 23f * guard);
            }
            // タッグ技（腹 / 顔）は片手で打つ。通常のジャブは左前腕を胸の前へ水平に構えるので、
            // 腹パンの体勢（上体を前へ倒す）と重なると「両手を前に出して押している」画になる。
            // 打たない腕は脇を締めたまま拳を胸の前に構える。顔は奥側の腕で打つ。
            if (Brain != null && Brain.IsTagPunch && AttackNow != AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))
            {
                if (Brain.IsTagFacePunch)
                {
                    float side = Brain.UsesLeftPunch ? -1f : 1f;
                    // 決めの一発（5 発目）は踏み込みを大きくして、連打の締めに見せる。
                    float power = Brain.TagFacePower;
                    float faceCoil = Brain.TagFaceCoil * power;
                    float faceDrive = Brain.TagFaceDrive * power;

                    // 腰 → 胸 → 肩の順にひねる。溜めは拳側へ、振り抜きは逆へ回す。
                    // 上体も前へ入れて、腕だけでなく体重が拳に乗っていることを見せる。
                    torsoYaw = side * (24f * faceCoil - 34f * faceDrive);
                    torsoPitch = -5f * faceCoil - 16f * faceDrive;
                    torsoRoll = side * (-4f * faceCoil + 8f * faceDrive);

                    // 溜めで後ろへ預けた体重を、踏み込みで拳へ乗せる。
                    // 決めの一発でも腰を出しすぎない（出しすぎると拳が彼女の顔に近づきすぎ、
                    // 肘が折れて「押している」画になる）。強さは上体のひねりと反りに乗せる。
                    float bodyDrive = Mathf.Min(faceDrive, 1.05f);
                    hipsZ = 0.18f * bodyDrive - 0.05f * faceCoil;
                    hipsY = restY - 0.06f * bodyDrive + 0.008f * faceCoil;

                    // 拳側（後ろ足）で踏ん張り、前足が体重を受けて曲がる。
                    bool punchRight = side > 0f;
                    legRPitch = punchRight ? (-3f - 12f * faceDrive) : (8f + 4f * faceDrive);
                    legLPitch = punchRight ? (8f + 4f * faceDrive) : (-3f - 12f * faceDrive);
                    kneeR = punchRight
                        ? (16f + 6f * faceCoil - 8f * faceDrive)
                        : (16f + 14f * faceDrive);
                    kneeL = punchRight
                        ? (16f + 14f * faceDrive)
                        : (16f + 6f * faceCoil - 8f * faceDrive);

                    // 打つ拳は頬の横へ引いて構える（振り抜きは BackHoldPose が実際の腕を解く）。
                    // 反対の腕は顎の前に構え、振り抜きで引いて反動をつくる。
                    // 構えの拳は体の中心ではなく「自分の頬の外側」に置く（戻すときに
                    // 彼女の顔の前を横切らない）。
                    float chamber = -30f - 12f * faceDrive;
                    if (punchRight)
                    {
                        armRPitch = chamber;
                        elbowR = 132f;
                        armRSpread = -18f;
                        armLPitch = -22f - 14f * faceDrive;
                        elbowL = 128f;
                        armLSpread = 10f;
                    }
                    else
                    {
                        armLPitch = chamber;
                        elbowL = 132f;
                        armLSpread = 18f;
                        armRPitch = -22f - 14f * faceDrive;
                        elbowR = 128f;
                        armRSpread = -10f;
                    }
                }
                else
                {
                    armLPitch = _tagArmL;
                    elbowL = _tagElbowL;
                    armLSpread = _tagSpreadL;
                }
            }
            // 検証用の上書き（エディタで姿勢を撮り比べる）。実行中は null なので影響しない。
            if (ForceLeftArm.HasValue)            {
                armLPitch = ForceLeftArm.Value.x;
                elbowL = ForceLeftArm.Value.y;
                armLSpread = ForceLeftArm.Value.z;
            }
            // 腰を落として手を届かせ、前足に体重を乗せて押し切る。
            if (Brain != null && Brain.IsPushing)
            {
                float time = Brain.PushElapsed;
                float reach = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.03f, EnemyBrain.PushContactSeconds, time));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.19f, EnemyBrain.PushDuration, time));
                float shoveDrive = reach * (1f - release);
                float load = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / EnemyBrain.PushContactSeconds));
                armRPitch = armLPitch = Mathf.Lerp(-36f, -77f, shoveDrive);
                elbowR = elbowL = Mathf.Lerp(70f, 12f, shoveDrive);
                armRSpread = -10f;
                armLSpread = 10f;
                torsoPitch = 5f + 6f * load - 26f * shoveDrive;
                hipsY = restY - 0.09f * load - 0.055f * shoveDrive;
                hipsZ += 0.14f * shoveDrive - 0.06f * load;
                legRPitch = Mathf.Lerp(4f, -28f, shoveDrive);
                legLPitch = Mathf.Lerp(12f, 23f, shoveDrive);
                kneeR = 17f + 20f * shoveDrive + 15f * load;
                kneeL = 18f + 18f * load;
            }
            var holdState = Brain != null && Brain.Player != null ? Brain.Player.GetComponent<PlayerGrapple>() : null;
            // 体で衝撃を受けてから腕で支え、背面拘束では片腕で向きを誘導する。
            if (Brain != null && Brain.IsCatching)
            {
                float impact = Brain.CatchImpact;
                armRPitch = armLPitch = Mathf.Lerp(-58f, -44f, impact);
                elbowR = elbowL = Mathf.Lerp(60f, 78f, impact);
                armRSpread = -Mathf.Lerp(24f, 11f, impact);
                armLSpread = -armRSpread;
                torsoPitch += 24f * impact - 6f;
                hipsZ -= 0.04f + 0.045f * impact;
                hipsY -= 0.03f + 0.06f * impact;
                legLPitch += 13f * impact;
                kneeR += 8f + 16f * impact;
                kneeL += 8f + 10f * impact;
                if (holdState != null)
                {
                    float guide = Mathf.Sin(Mathf.PI * holdState.HandOffTurn);
                    armRPitch -= 18f * guide;
                    elbowR += 16f * guide;
                    armLPitch += 14f * guide;
                    legRPitch -= 12f * guide;
                }
            }
            else if (holdState != null && holdState.IsHandingOff && holdState.Captor == Brain)
            {
                float ready = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.20f, 1.85f, holdState.RecoveryStep));
                armRPitch = armLPitch = Mathf.Lerp(-8f, -52f, ready);
                elbowR = elbowL = Mathf.Lerp(18f, 40f, ready);
                armRSpread = -Mathf.Lerp(8f, 23f, ready);
                armLSpread = -armRSpread;
            }
            else if (holdState != null && holdState.IsHandingOff && Brain.IsHandOffLocked && !Brain.IsPushing)
            {
                // 送り手は押し切った腕を戻し、受け渡しを見送る。
                armRPitch = armLPitch = -20f;
                elbowR = elbowL = 42f;
                torsoPitch = 0f;
                hipsZ = 0f;
            }
            // ベアハッグ: 胴を抱え込むので上体を前へ寄せ、膝を落として締める。
            if (Brain != null && Brain.IsBearHugging)
            {
                var hug = Brain.Player.GetComponent<PlayerGrapple>();
                float progress = hug != null ? Mathf.Clamp01(hug.Progress) : 0f;
                float lift = hug != null ? hug.LiftPulse : 0f;
                float squeeze = hug != null ? hug.SqueezePulse : 0f;
                float yank = hug != null ? hug.PullPulse : 0f;

                // 抱え上げるほど上体を起こし、腰を彼女へ寄せる。締め付けでは少し沈む。
                // 引き寄せでは体重を後ろへ預けて、腕で彼女を引きずり込む。
                torsoPitch += Mathf.Lerp(8f, -4f, progress)
                    - 4f * lift + 2f * squeeze - 7f * yank;
                hipsZ += 0.05f + 0.02f * squeeze - 0.03f * yank;
                hipsY += 0.03f * lift - 0.03f + 0.02f * yank;
                kneeR += 8f - 4f * lift + 5f * yank;
                kneeL += 8f - 4f * lift + 5f * yank;
            }
            // 押さえつけ（Pin）: 肩を押し込むので上体を前へ倒し、膝を落として踏ん張る。            // 腕は PinHoldPose が彼女の肩へ向けて毎フレーム引き直す。
            // もがかれるほど押し返されて上体が起こされる（振りほどきが効いているのが見える）。
            if (Brain != null && Brain.IsPinning)
            {
                var pin = Brain.Player.GetComponent<PlayerGrapple>();
                float progress = pin != null ? Mathf.Clamp01(pin.Progress) : 0f;
                torsoPitch += Mathf.Lerp(12f, -6f, progress);
                hipsY -= 0.06f;
                kneeR += 10f;
                kneeL += 10f;

                // 膝蹴り。カメラ側の脚で入れる（奥の脚だと彼の体に隠れて見えない）。
                float knee = pin != null ? pin.KneePulse : 0f;
                if (knee > 0f)
                {
                    var cam = Camera.main;
                    bool leftKnee = false;
                    if (cam != null)
                    {
                        Vector3 toCam = cam.transform.position - transform.position;
                        toCam.y = 0f;
                        leftKnee = Vector3.Dot(toCam, transform.right) < 0f;
                    }

                    hipsY += 0.02f * knee;
                    hipsZ += 0.08f * knee;
                    if (leftKnee)
                    {
                        // 骨盤を右へずらして、左膝を彼女の腹の中心線へ入れる
                        // （そのまま上げると膝が彼女の脇腹の横を通って「当たっていない」画になる）
                        hipsX += 0.10f * knee;
                        kneeL += 105f * knee;
                        legLPitch -= 115f * knee;
                        kneeR += 8f * knee;
                    }
                    else
                    {
                        hipsX -= 0.10f * knee;
                        kneeR += 105f * knee;
                        legRPitch -= 115f * knee;
                        kneeL += 8f * knee;
                    }
                }
            }
            // --- 立ち上がり（倒れた姿勢から起こす） ---------------------
            // 4 つの形を順に通す: 仰向け → 上体を起こす → しゃがみ（膝を深く折る）
            // → 立ち上がる。途中で必ず膝を折って足を体の下へ入れる。
            // これが無いと、脚を伸ばしたまま足首を支点に板が起きるように見える。
            float rise = RiseNow;
            bool rising = RisingNow && (phase == EnemyPhase.Chase || phase == EnemyPhase.Strafe);
            if (rising)
            {
                // 倒れている間も頭は前を見る。上体と一緒に垂れると
                // 「気を失ったまま体だけ起きた」ように見える。
                fall = SampleRise(RiseFall, rise);
                hipsY = restY * SampleRise(RiseHip, rise);
                hipsZ = 0f;
                torsoPitch = SampleRise(RiseTorso, rise);
                torsoYaw = SampleRise(RiseYaw, rise);
                headPitch = SampleRise(RiseHead, rise);
                armRPitch = SampleRise(RiseArmR, rise);
                armLPitch = SampleRise(RiseArmL, rise);
                elbowR = SampleRise(RiseElbowR, rise);
                elbowL = SampleRise(RiseElbowL, rise);
                kneeR = SampleRise(RiseKnee, rise);
                kneeL = SampleRise(RiseKnee, rise);
                legRPitch = SampleRise(RiseLeg, rise);
                legLPitch = SampleRise(RiseLeg, rise);
                legsExact = false;   // 脚は角度で作る（歩行の接地 IK は使わない）
            }
            // 掴みは両手を使うので、パイプは手元から隠す。
            // 立ち上がりの途中も隠す（床に手を突いている間、手にパイプがあると刺さる）。
            bool grabbing = AttackNow == AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover);
            bool weaponHidden = grabbing || (rising && rise < 0.9f);
            SetWeaponVisible(!weaponHidden);
            // --- 手首 ---------------------------------------------------
            // 前腕と拳が一直線のままだと「棒の先に塊」に見える。
            // 当てる瞬間だけ伸ばし、それ以外は少し折って力を抜く。
            float wristR = 8f;
            float wristL = 8f;
            float twistR = -12f;   // 前腕の軸まわり（手のひらを内へ）
            float twistL = 12f;
            switch (phase)
            {
                case EnemyPhase.Windup:
                    wristR = -16f;   // 拳を立てて振りかぶる
                    wristL = 4f;
                    twistR = -22f;
                    break;
                case EnemyPhase.Attack:
                    wristR = 2f;     // 当たる瞬間は前腕と一直線
                    twistR = -4f;
                    break;
                case EnemyPhase.Recover:
                    wristR = 12f;    // 振り抜いたあとは脱力
                    twistR = -8f;
                    break;
                case EnemyPhase.Stagger:
                    wristR = 18f;    // 力が抜れて手が遅れる
                    wristL = 18f;
                    break;
                case EnemyPhase.Dead:
                    wristR = 4f;
                    wristL = 4f;
                    break;
            }
            if (Brain != null && Brain.IsTagFacePunch)
                wristR = wristL = 3f;
            // 掴みは手を開いて伸ばすので、拳を立てない
            if (grabbing)
            {
                wristR = 0f;
                wristL = 0f;
            }
            // --- なめらかに適用 ---------------------------------------
            float k2 = instant ? 1f : 1f - Mathf.Exp(-14f * dt);
            _hipsY = Mathf.Lerp(_hipsY, hipsY, k2);
            _hipsZ = Mathf.Lerp(_hipsZ, hipsZ, k2);
            _hipsX = Mathf.Lerp(_hipsX, hipsX, k2);
            _torsoPitch = Mathf.Lerp(_torsoPitch, torsoPitch, k2);
            _torsoRoll = Mathf.Lerp(_torsoRoll, torsoRoll, k2);
            _torsoYaw = Mathf.Lerp(_torsoYaw, torsoYaw, k2);
            _headPitch = Mathf.Lerp(_headPitch, headPitch, k2);
            _armRPitch = Mathf.Lerp(_armRPitch, armRPitch, k2);
            _armLPitch = Mathf.Lerp(_armLPitch, armLPitch, k2);
            _armRSpread = Mathf.Lerp(_armRSpread, armRSpread, k2);
            _armLSpread = Mathf.Lerp(_armLSpread, armLSpread, k2);
            _elbowRAngle = Mathf.Lerp(_elbowRAngle, elbowR, k2);
            _elbowLAngle = Mathf.Lerp(_elbowLAngle, elbowL, k2);
            _wristRBend = Mathf.Lerp(_wristRBend, wristR, k2);
            _wristLBend = Mathf.Lerp(_wristLBend, wristL, k2);
            _wristRTwist = Mathf.Lerp(_wristRTwist, twistR, k2);
            _wristLTwist = Mathf.Lerp(_wristLTwist, twistL, k2);
            if (!legsExact)
            {
                _legRPitch = Mathf.Lerp(_legRPitch, legRPitch, k2);
                _legLPitch = Mathf.Lerp(_legLPitch, legLPitch, k2);
                _kneeRAngle = Mathf.Lerp(_kneeRAngle, kneeR, k2);
                _kneeLAngle = Mathf.Lerp(_kneeLAngle, kneeL, k2);
            }
            _fallAngle = Mathf.Lerp(_fallAngle, fall, instant ? 1f : (phase == EnemyPhase.Dead ? 1f - Mathf.Exp(-4f * dt) : k2));
            // 予備動作の間、構えたパイプを光らせる。
            // 以前は頭の上に赤い球を出していたが、振りかぶった武器と二重になって
            // 「宙に玉が浮いている」ようにしか見えなかったため、武器そのもので示す。
            float chargeTarget = phase == EnemyPhase.Windup ? Mathf.Clamp01(WindupNow) : 0f;
            if (DebugCharge >= 0f)
                chargeTarget = DebugCharge;   // 検証用（PhotoShot -shotcharge）
            _charge = instant ? chargeTarget : Mathf.Lerp(_charge, chargeTarget, 1f - Mathf.Exp(-16f * dt));
            ApplyWeaponCharge();
            _hips.localPosition = new Vector3(_hipsX, _hipsY, _hipsZ);
            _hips.localRotation = Quaternion.Euler(0f, _torsoYaw * 0.45f, 0f);
            _chest.localRotation = Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f, _torsoRoll);
            float headCounterYaw = Brain != null && Brain.IsTagFacePunch ? 0.90f : 0.25f;
            _head.localRotation = Quaternion.Euler(-_headPitch * 0.5f, -_torsoYaw * headCounterYaw, 0f);
            _shoulderR.localRotation = Quaternion.Euler(_armRPitch, 0f, _armRSpread);
            _shoulderL.localRotation = Quaternion.Euler(_armLPitch, 0f, _armLSpread);
            _elbowR.localRotation = Quaternion.Euler(-_elbowRAngle, 0f, 0f);
            _elbowL.localRotation = Quaternion.Euler(-_elbowLAngle, 0f, 0f);
            if (_wristR != null)
                _wristR.localRotation = Quaternion.Euler(_wristRBend, _wristRTwist, 0f);
            if (_wristL != null)
                _wristL.localRotation = Quaternion.Euler(_wristLBend, _wristLTwist, 0f);
            if (legsExact)
            {
                // 進行方向へ踏み出す。前後だけでなく横にも運べるよう、位置から IK で解く
                // 腰の横移動は足首の目標も一緒に動かすので、同じ量だけ引いて接地を保つ
                Vector3 targetR = _legTargetR;
                Vector3 targetL = _legTargetL;
                targetR.x -= _hipsX;
                targetL.x -= _hipsX;
                Vector3 poleR = _hipR.position + transform.forward * 0.5f + Vector3.up * 0.15f;
                Vector3 poleL = _hipL.position + transform.forward * 0.5f + Vector3.up * 0.15f;
                ProceduralRig.SolveTwoBone(_hipR, _kneeR, _ankleR,
                    _hipR.position + transform.rotation * targetR, poleR);
                ProceduralRig.SolveTwoBone(_hipL, _kneeL, _ankleL,
                    _hipL.position + transform.rotation * targetL, poleL);
                // 足は脛と一緒に回すとつま先立ちに見える。地面と平行に保つ
                Quaternion flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _ankleR.rotation = flat;
                _ankleL.rotation = flat;
                // 歩き終わりに姿勢が飛ばないよう、実際の角度を覚えておく
                _legRPitch = Mathf.DeltaAngle(0f, _hipR.localEulerAngles.x);
                _legLPitch = Mathf.DeltaAngle(0f, _hipL.localEulerAngles.x);
                _kneeRAngle = Mathf.DeltaAngle(0f, _kneeR.localEulerAngles.x);
                _kneeLAngle = Mathf.DeltaAngle(0f, _kneeL.localEulerAngles.x);
            }
            else
            {
                _hipR.localRotation = Quaternion.Euler(_legRPitch, 0f, 0f);
                _hipL.localRotation = Quaternion.Euler(_legLPitch, 0f, 0f);
                _kneeR.localRotation = Quaternion.Euler(_kneeRAngle, 0f, 0f);
                _kneeL.localRotation = Quaternion.Euler(_kneeLAngle, 0f, 0f);
                _ankleR.localRotation = Quaternion.identity;
                _ankleL.localRotation = Quaternion.identity;
            }
            // 拳側の足はかかとを上げて踏ん張る（体重が拳へ乗ったことが足元で分かる）。
            if (Brain != null && Brain.IsTagFacePunch)
            {
                float heel = 34f * Brain.TagFaceDrive * Brain.TagFacePower;
                if (heel > 0.1f)
                {
                    if (Brain.UsesLeftPunch)
                        _ankleL.localRotation = Quaternion.Euler(heel, 0f, 0f);
                    else
                        _ankleR.localRotation = Quaternion.Euler(heel, 0f, 0f);
                }
            }
            // 前フレームの持ち上げを戻してから測る（残したまま測ると、
            // 沈みが小さく見えて補正が足りず、毎フレーム少しずつずれる）。
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.Euler(_fallAngle, 0f, 0f);

            // 倒れている間は、体の一番低いところが床に着くように持ち上げる。
            // 根元を回すだけだと脚や背中が床を突き抜ける（実測で足首が -25cm、
            // 胸が -6cm まで潜っていた）。倒れ角に沿って滑らかに増える。
            if (_fallAngle < -5f)
            {
                float lowest = Mathf.Min(_ankleL.position.y, _ankleR.position.y);
                lowest = Mathf.Min(lowest, _hips.position.y - 0.14f);
                lowest = Mathf.Min(lowest, _chest.position.y - 0.12f);
                lowest = Mathf.Min(lowest, _head.position.y - 0.10f);
                _root.localPosition = new Vector3(0f, Mathf.Max(0f, -lowest), 0f);
            }
            else
            {
                _root.localPosition = Vector3.zero;
            }

            // 拳が届くまで全身を小さく踏み込み、判定と見た目の距離を揃える。
            float drive = 0f;
            if (AttackNow != AttackId.Grab)
            {
                if (phase == EnemyPhase.Windup)
                    drive = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WindupNow - 0.75f) / 0.25f));
                else if (phase == EnemyPhase.Attack)
                    drive = 1f;
                else if (phase == EnemyPhase.Recover && _swingTime >= 0f)
                    drive = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_swingTime - 0.24f) / 0.18f));
            }
            float lunge = 0.40f;
            if (Brain != null && Brain.IsTagPunch && Brain.Zone == PunchZone.Body && Brain.Player != null)
            {
                float gap = Vector3.Dot(Brain.Player.transform.position - transform.position, transform.forward);
                lunge = Mathf.Clamp(gap - 0.50f, 0f, lunge);
                if (phase == EnemyPhase.Recover && _swingTime >= 0f)
                    drive = 1f - Mathf.SmoothStep(0f, 1f, (_swingTime - 0.20f) / 0.17f);
            }
            else if (Brain != null && Brain.IsTagFacePunch && Brain.Player != null)
            {
                // 顔面パンチは腕の軌道で届かせる。体で押し込む分だけ少し前へ出る
                // （大きく踏み込むと拳が彼女へめり込み、腕が畳まれて見える）。
                float gap = Vector3.Dot(Brain.Player.transform.position - transform.position, transform.forward);
                lunge = Mathf.Clamp(gap - 0.70f, 0f, 0.06f);
                drive = Brain.TagFaceDrive;
            }
            _lungeOffset = lunge * drive;
            _root.localPosition += Vector3.forward * _lungeOffset;
            if (Brain != null && Brain.IsHolding && holdState != null && holdState.IsGrappled
                && holdState.Captor == Brain && holdState.Kind == GrappleKind.Back)
                _root.localPosition += Vector3.forward * 0.03f;
        }

        internal void ApplyGroundMount(GroundMountAttack mount)
        {
            var stage = mount.Stage;
            if (stage == GroundMountAttack.Beat.Approach || _hips == null)
                return;

            float t = Mathf.SmoothStep(0f, 1f, mount.Progress);
            bool mounting = stage == GroundMountAttack.Beat.Mount;
            bool rolling = stage == GroundMountAttack.Beat.Roll;
            float kneel = stage switch
            {
                GroundMountAttack.Beat.Mount => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.90f, mount.Progress)),
                GroundMountAttack.Beat.Dismount => 1f - t,
                _ => 1f
            };
            float seated = mounting ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.92f, mount.Progress)) : 1f;
            float rollGrip = rolling
                ? mount.RollPhase(0f, 0.17f) * (1f - mount.RollPhase(0.86f, 1f))
                : 0f;
            float rollPull = rolling ? Mathf.Sin(Mathf.PI * mount.ShoulderRoll) : 0f;
            float transfer = mounting ? Mathf.Sin(Mathf.PI * t) : 0f;
            _hips.localPosition = Vector3.Lerp(new Vector3(_hipsX, _hipsY, _hipsZ),
                new Vector3(mount.Side * -0.06f * transfer,
                    Mathf.Lerp(0.48f, 0.39f, seated) + 0.075f * transfer, -0.06f), kneel);

            bool left = mount.LeftPunch;
            float windup = mount.Cock;
            float followThrough = mount.Drive;
            float punchSide = left ? -1f : 1f;
            float twist = punchSide * (28f * windup - 24f * followThrough);
            _chest.localRotation = Quaternion.Slerp(
                Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f, _torsoRoll),
                Quaternion.Euler(Mathf.Lerp(53f, 32f, seated) + 19f * rollGrip - 15f * rollPull - 14f * windup
                    + 32f * followThrough, twist + 18f * rollPull * mount.Side,
                    punchSide * (10f * windup - 12f * followThrough) - 9f * mount.Side * rollPull), kneel);
            _head.localRotation = Quaternion.Euler(12f * kneel, -0.55f * twist, 0f);

            float lead = mounting ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.02f, 0.56f, mount.Progress)) : 0f;
            float trail = mounting ? Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.28f, 0.86f, mount.Progress)) : 0f;
            MountLeg(false, Mathf.Max(kneel, 0.62f * lead), seated, lead);
            MountLeg(true, Mathf.Max(kneel, 0.62f * trail), seated, trail);
            SetWeaponVisible(false);

            var rig = mount.TargetRig;
            Vector3 side = transform.right * punchSide;
            Vector3 entry = rig.Hips.position + Vector3.up * 0.20f;
            Vector3 raised = MountRaisedFist(left, side);
            Vector3 outside = MountOpenHand(rig, side);
            Vector3 otherOutside = MountOpenHand(rig, -side);
            Vector3 otherHand = Vector3.Lerp(otherOutside, MountRaisedFist(!left, -side), mount.NextArmWindup);
            Vector3 contact = mount.StrikeTarget + Vector3.up * 0.095f + side * 0.055f - transform.forward * 0.025f;
            Vector3 punchHand = Vector3.Lerp(outside, raised, mount.ArmWindup);
            if (stage == GroundMountAttack.Beat.Strike)
            {
                // Drop toward cheek height outside the guard, then sweep inward.
                Vector3 outsideCheek = contact + side * 0.42f - transform.forward * 0.10f;
                punchHand = Vector3.Lerp(Vector3.Lerp(raised, outsideCheek, followThrough),
                    Vector3.Lerp(outsideCheek, contact, followThrough), followThrough);
            }
            else if (stage == GroundMountAttack.Beat.Recover)
            {
                // Clear the cheek and stay outside. The other hand starts rising before this one settles.
                punchHand = Vector3.Lerp(contact, outside, t)
                    + (side * 0.11f + Vector3.up * 0.12f) * Mathf.Sin(Mathf.PI * t);
            }
            Vector3 punchGoal = Vector3.Lerp(entry + side * 0.20f, punchHand, seated);
            Vector3 supportGoal = Vector3.Lerp(entry - side * 0.20f, otherHand, seated);
            if (rolling)
            {
                var shoulder = mount.RollLeftShoulder ? rig.LUpperArm : rig.RUpperArm;
                Vector3 grip = shoulder.position + Vector3.up * 0.066f - transform.forward * 0.035f;
                Vector3 brace = Vector3.Lerp(rig.Chest.position, rig.Hips.position, 0.25f)
                    + Vector3.up * 0.09f - transform.right * (0.10f * mount.Side);
                bool punchHandPulls = left == (mount.Side < 0f);
                punchGoal = Vector3.Lerp(punchGoal, punchHandPulls ? grip : brace, rollGrip);
                supportGoal = Vector3.Lerp(supportGoal, punchHandPulls ? brace : grip, rollGrip);
            }
            AimMountArm(!left, supportGoal, kneel);
            AimMountArm(left, punchGoal, kneel);
        }

        Vector3 MountOpenHand(ProceduralRig rig, Vector3 outside)
        {
            return rig.Hips.position + transform.forward * 0.37f + outside * 0.24f + Vector3.up * 0.20f;
        }

        Vector3 MountRaisedFist(bool left, Vector3 outside)
        {
            return ShoulderBone(left).position + outside * 0.33f + transform.forward * 0.11f + Vector3.up * 0.07f;
        }

        void MountLeg(bool left, float kneel, float seated, float swing)
        {
            Transform hip = left ? _hipL : _hipR;
            Transform knee = left ? _kneeL : _kneeR;
            Transform ankle = left ? _ankleL : _ankleR;
            float side = left ? -1f : 1f;
            // Both knees carry the weight. Open the hips and fold each shin back along the floor.
            Vector3 lateral = transform.right * side;
            Vector3 thighDirection = (lateral * 0.20f + transform.forward * 0.22f - transform.up * 0.33f).normalized;
            Vector3 shinDirection = (-transform.forward + lateral * 0.16f - transform.up * 0.06f).normalized;
            Vector3 bendDirection = Vector3.ProjectOnPlane(shinDirection, thighDirection);
            Quaternion mounted = Quaternion.Inverse(hip.parent.rotation)
                * Quaternion.LookRotation(-bendDirection, -thighDirection);
            Quaternion openHip = Quaternion.Euler((left ? -57f : -27f) - 23f * swing,
                side * (12f + 18f * swing), side * (6f + 12f * swing));
            float bend = Mathf.Lerp(left ? 83f : 111f, Vector3.Angle(thighDirection, shinDirection), seated);
            hip.localRotation = Quaternion.Slerp(
                Quaternion.Euler(left ? _legLPitch : _legRPitch, 0f, 0f),
                Quaternion.Slerp(openHip, mounted, seated), kneel);
            float flex = Mathf.Lerp(left ? _kneeLAngle : _kneeRAngle, bend + 8f * swing, kneel);
            knee.localRotation = Quaternion.Euler(Mathf.Clamp(flex, 0f, 132f), 0f, 0f);
            ankle.localRotation = Quaternion.Euler(-12f * kneel, 0f, 0f);
        }

        void AimMountArm(bool left, Vector3 goal, float weight)
        {
            Transform shoulder = ShoulderBone(left);
            Transform elbow = ElbowBone(left);
            Transform wrist = WristBone(left);
            Vector3 pole = shoulder.position + transform.right * (left ? -0.38f : 0.38f)
                - transform.forward * 0.18f - transform.up * 0.21f;
            Vector3 toGoal = Vector3.Lerp(wrist.position, goal, weight) - shoulder.position;
            float upperLength = Vector3.Distance(shoulder.position, elbow.position);
            float lowerLength = Vector3.Distance(elbow.position, wrist.position);
            const float maxFlex = 128f;
            float minReach = Mathf.Sqrt(upperLength * upperLength + lowerLength * lowerLength
                + 2f * upperLength * lowerLength * Mathf.Cos(maxFlex * Mathf.Deg2Rad));
            Vector3 target = shoulder.position + toGoal.normalized * Mathf.Max(minReach, toGoal.magnitude);
            ProceduralRig.SolveTwoBone(shoulder, elbow, wrist, target, pole);
            SettleElbowHinge(shoulder, elbow, wrist);
            // The proxy bones extend along -Y. Orient the shoulder to the bend plane,
            // then flex the elbow on its hinge instead of twisting it to aim the fist.
            Vector3 upper = (elbow.position - shoulder.position).normalized;
            Vector3 forearm = (wrist.position - elbow.position).normalized;
            Vector3 bendDirection = Vector3.ProjectOnPlane(forearm, upper);
            if (bendDirection.sqrMagnitude > 0.0001f)
            {
                shoulder.rotation = Quaternion.LookRotation(bendDirection, -upper);
                elbow.localRotation = Quaternion.Euler(-Mathf.Clamp(Vector3.Angle(upper, forearm), 5f, maxFlex), 0f, 0f);
            }
            wrist.localRotation = Quaternion.identity;
        }

        internal void PoseBackHold(float fold, float lift, float hipDrop, float leanBack, float leanSide, float chin)
        {
            Vector3 leftFoot = _ankleL.position;
            Vector3 rightFoot = _ankleR.position;
            _hips.localPosition = new Vector3(_hipsX, _hipsY - 0.065f * fold + 0.045f * lift - hipDrop,
                _hipsZ - 0.035f * fold - 0.06f * lift);
            BraceHoldUpperBody(-19f * fold + 18f * lift + leanBack, leanSide, -8f * fold + 8f * lift + chin);
            PlantHoldFeet(leftFoot, rightFoot);
        }

        void PlantHoldFeet(Vector3 leftFoot, Vector3 rightFoot)
        {
            ProceduralRig.SolveTwoBone(_hipL, _kneeL, _ankleL, leftFoot,
                _hipL.position + transform.forward * 0.5f);
            ProceduralRig.SolveTwoBone(_hipR, _kneeR, _ankleR, rightFoot,
                _hipR.position + transform.forward * 0.5f);
            _ankleL.rotation = _ankleR.rotation = transform.rotation;
        }

        // 澪の被弾姿勢を反映した後、手の位置を解く前に上体を合わせる。
        internal void BraceHoldUpperBody(float leanBack, float leanSide, float tuckChin)
        {
            // ヒットストップ中は Step が止まるので、前フレームの補正には重ねない。
            _chest.localRotation = Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f, _torsoRoll)
                * Quaternion.Euler(-leanBack, 0f, leanSide);
            _head.localRotation = Quaternion.Euler(-_headPitch * 0.5f, -_torsoYaw * 0.25f, 0f)
                * Quaternion.Euler(tuckChin, 0f, leanSide * 0.5f);
        }

        /// <summary>
        /// 予備動作の進み具合でパイプを光らせる。振り切る直前に速く点滅させて
        /// 「ここで来る」を分からせる。0 で消灯。
        /// </summary>
        void ApplyWeaponCharge()
        {
            if (_weaponSteel == null)
                return;
            float t = _charge;
            if (t <= 0.001f)
            {
                _weaponSteel.SetColor(ColorId, _weaponBase);
                _weaponSteel.SetColor(EmissionId, Color.black);
                return;
            }
            // 溜め切る前に脈打たせる
            float pulse = 1f + 0.30f * Mathf.Sin(Time.time * 42f) * Mathf.Clamp01((t - 0.55f) * 3f);
            float k = t * pulse;
            // 強くしすぎると発光が白く飛んで「鉄パイプ」に見えなくなる（光の剣になる）。
            // 赤を残して緑を抑えると、白っぽくならず「焼けた鉄の橙」に見える。
            _weaponSteel.SetColor(ColorId, Color.Lerp(_weaponBase, new Color(0.85f, 0.45f, 0.25f), t));
            _weaponSteel.SetColor(EmissionId, new Color(1f, 0.22f, 0.05f) * (2.2f * k));
        }
        // --- 振りかぶり姿勢 -----------------------------------------------
        // Windup の終わりと Recover の振り始めで必ず同じ値を使う。
        // 別々に書くと切り替わりの1フレームで腕が飛ぶ。
        //
        // Heavy は「振りかぶりが大きい」で見分けられるようにする（Jab と形が似ていて読みにくかった）。
        static float CockArm(AttackId a)
        {
            switch (a)
            {
                // 横薙ぎ: 体の横へ水平に引く（バットの振りかぶり）
                case AttackId.Break: return 92f;
                // 実測（arm_sweep.png）: -170 が真上、-80 が前方水平、+130〜+160 が「頭の後ろ」。
                // 振り下ろしは頭の後ろに担いでから振るので +145 側。
                // 以前は +96（真下の後ろ）で、振り下ろしなのに腕が下がっていた。
                case AttackId.Heavy: return 36f;
                // 突き: 脇を締めて腰の横へ引く（槍の構え）。
                // 以前は横薙ぎと 62/68 でほぼ同じで、予備動作が見分けられなかった。
                default: return 34f;
            }
        }

        static float CockElbow(AttackId a)
        {
            switch (a)
            {
                case AttackId.Break: return 46f;
                case AttackId.Heavy: return 72f;
                // 突きは肘を深く曲げて、柄を体の横に付ける
                default: return 82f;
            }
        }

        static float CockYaw(AttackId a)
        {
            switch (a)
            {
                // 横薙ぎは大きく開く。突きは正面を向いたまま（開くと突きに見えない）
                case AttackId.Heavy: return 40f;
                case AttackId.Break: return 52f;
                default: return 10f;
            }
        }

        static float CockPitch(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -14f;
                case AttackId.Break: return 6f;
                default: return -3f;
            }
        }
        // --- 振り切った形（攻撃の種類ごとに変える）--------------------------
        // 3 種とも同じ姿勢で振り切ると、見た目が「1 種類の攻撃」にしか見えない。
        //   横薙ぎ … 体を大きく開いて横へ払う
        //   振り下ろし … 上から下へ叩き落とす（腕を低く、上体を深く前へ）
        //   突き … 肘を伸ばし切って真っ直ぐ刺す（体はあまり開かない）
        static float StrikeArm(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -78f;
                case AttackId.Break: return -84f;
                default: return -70f;
            }
        }

        static float StrikeElbow(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 12f;
                case AttackId.Break: return 2f;
                default: return 10f;
            }
        }

        static float StrikeYaw(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 12f;
                case AttackId.Break: return 8f;
                default: return 34f;
            }
        }

        static float StrikePitch(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -8f;
                case AttackId.Break: return 16f;
                default: return 8f;
            }
        }

        /// <summary>振り切ったときに腰をどこまで前へ出すか（重い攻撃ほど踏み込む）。</summary>
        static float StrikeHips(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 0.145f;
                case AttackId.Break: return 0.120f;
                default: return 0.100f;
            }
        }
        // --- 脚の運び ---------------------------------------------------
        /// <summary>
        /// 歩行中の足先の位置（腰の関節から見た前後と上下）。
        /// 接地中は「体が進んだ分だけ後ろへ下がる」ので、地面の上では止まって見える。
        /// </summary>
        static void FootPath(float u, float reach, float duty, out float sweep, out float lift)
        {
            const float ground = 0.008f;   // 接地中の足首の高さ
            if (u < duty)
            {
                // 接地。前から後ろへ一定の速さで流れる
                sweep = Mathf.Lerp(reach, -reach, u / duty);
                lift = ground;
            }
            else
            {
                // 遊脚。弧を描いて前へ戻る
                float a = (u - duty) / (1f - duty);
                sweep = Mathf.Lerp(-reach, reach, Mathf.SmoothStep(0f, 1f, a));
                lift = ground + Mathf.Sin(Mathf.PI * a) * 0.10f;
            }
        }
        // --- 組み立て --------------------------------------------------
        void Build()
        {
            if (_root != null)
                return;
            var skin = BodyMaterial;
            var cloth = ClothMaterial;
            var pants = Tint(cloth, 0.78f);     // ズボン。上着と同色だと「一体の木偶」に見える
            var trim = Tint(cloth, 0.58f);      // 襟・差し色
            var dark = Tint(cloth, 0.42f);      // ベルト・靴
            var hair = HairMaterial != null ? HairMaterial : Tint(cloth, 0.30f);
            var eye = Tint(cloth, 0.14f);
            float h = Height;
            float hipsY = h * 0.515f;      // 腰の高さ（ここが回転の中心）
            float chestY = h * 0.080f;     // 腰から上体の付け根まで
            float headY = h * 0.255f;      // 胸から首の付け根まで
            float shoulderY = h * 0.200f;
            float shoulderX = h * 0.115f;
            float upperArm = h * 0.175f;
            float lowerArm = h * 0.165f;
            float hipX = h * 0.062f;
            float thigh = h * 0.235f;
            float shin = h * 0.245f;
            _root = Child("Root", transform, Vector3.zero);
            // --- 腰 -----------------------------------------------------
            // 箱ではなく楕円体で組む。角が立つと「プリミティブ」に見える。
            _hips = Child("Hips", _root, new Vector3(0f, hipsY, 0f));
            CapsuleShaped("Pelvis", _hips, new Vector3(0f, -h * 0.002f, 0f),
                h * 0.123f, h * 0.087f, h * 0.064f, pants);
            CapsuleShaped("Belt", _hips, new Vector3(0f, h * 0.024f, 0f),
                h * 0.029f, h * 0.080f, h * 0.062f, dark);
            CapsuleShaped("Waist", _hips, new Vector3(0f, h * 0.058f, 0f),
                h * 0.086f, h * 0.077f, h * 0.060f, pants);
            // --- 胴 -----------------------------------------------------
            // 側面がまっすぐなカプセルで組む。球だと腹だけが膨らんで「壺」に見える。
            _chest = Child("Chest", _hips, new Vector3(0f, chestY, 0f));
            CapsuleShaped("Torso", _chest, new Vector3(0f, h * 0.088f, 0f),
                h * 0.246f, h * 0.106f, h * 0.063f, cloth);
            CapsuleShaped("ChestTop", _chest, new Vector3(0f, h * 0.180f, -h * 0.002f),
                h * 0.077f, h * 0.082f, h * 0.058f, cloth);
            CapsuleShaped("Collar", _chest, new Vector3(0f, h * 0.200f, h * 0.006f),
                h * 0.034f, h * 0.049f, h * 0.049f, trim);
            // --- 頭 -----------------------------------------------------
            _head = Child("Head", _chest, new Vector3(0f, headY, 0f));
            Capsule("Neck", _head, new Vector3(0f, -h * 0.027f, 0f), h * 0.085f, h * 0.028f, skin);
            Blob("Skull", _head, new Vector3(0f, h * 0.071f, 0f),
                new Vector3(h * 0.114f, h * 0.140f, h * 0.126f), skin);
            Blob("Jaw", _head, new Vector3(0f, h * 0.047f, h * 0.016f),
                new Vector3(h * 0.089f, h * 0.077f, h * 0.100f), skin);
            // 髪は頭頂まで被せる。上に載せないと「後ろに黒い塊がある禿げ」に見える。
            // 顔の高さでは楕円が細るので、前に出しても目は隠れない。
            Blob("Hair", _head, new Vector3(0f, h * 0.086f, -h * 0.011f),
                new Vector3(h * 0.128f, h * 0.123f, h * 0.143f), hair);
            Blob("HairBack", _head, new Vector3(0f, h * 0.057f, -h * 0.049f),
                new Vector3(h * 0.109f, h * 0.099f, h * 0.075f), hair);
            Blob("EyeL", _head, new Vector3(-h * 0.025f, h * 0.064f, h * 0.055f),
                Vector3.one * (h * 0.022f), eye);
            Blob("EyeR", _head, new Vector3(h * 0.025f, h * 0.064f, h * 0.055f),
                Vector3.one * (h * 0.022f), eye);
            // 眉と口が無いと「目玉の付いた球」に見える。この 4 つで一気に顔になる。
            // 眉は目から少し離す。近いと 1 本の帯に見えてサングラスになる。
            Blob("BrowL", _head, new Vector3(-h * 0.026f, h * 0.079f, h * 0.054f),
                new Vector3(h * 0.028f, h * 0.006f, h * 0.012f), eye);
            Blob("BrowR", _head, new Vector3(h * 0.026f, h * 0.079f, h * 0.054f),
                new Vector3(h * 0.028f, h * 0.006f, h * 0.012f), eye);
            Blob("Mouth", _head, new Vector3(0f, h * 0.040f, h * 0.057f),
                new Vector3(h * 0.030f, h * 0.007f, h * 0.012f), eye);
            Blob("Nose", _head, new Vector3(0f, h * 0.055f, h * 0.060f),
                new Vector3(h * 0.020f, h * 0.020f, h * 0.022f), skin);
            _shoulderR = Child("ShoulderR", _chest, new Vector3(shoulderX, shoulderY, 0f));
            _shoulderL = Child("ShoulderL", _chest, new Vector3(-shoulderX, shoulderY, 0f));
            _elbowR = Arm(_shoulderR, upperArm, lowerArm, h, cloth, skin, true);
            _elbowL = Arm(_shoulderL, upperArm, lowerArm, h, cloth, skin, false);
            // 素手で戦う。予備動作は拳の色で伝える。
            _weaponSteel = skin != null ? Solid(skin, skin.color) : null;
            if (_weaponSteel != null)
            {
                _weaponBase = _weaponSteel.color;
                _weaponSteel.EnableKeyword("_EMISSION");
                _weaponSteel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                _weaponSteel.SetColor(EmissionId, Color.black);
                _fistR.GetComponent<Renderer>().sharedMaterial = _weaponSteel;
            }
            _hipR = Child("HipR", _hips, new Vector3(hipX, -h * 0.030f, 0f));
            _hipL = Child("HipL", _hips, new Vector3(-hipX, -h * 0.030f, 0f));
            _kneeR = Leg(_hipR, thigh, shin, h, pants, dark);
            _kneeL = Leg(_hipL, thigh, shin, h, pants, dark);
            _ankleR = _kneeR.Find("Ankle");
            _ankleL = _kneeL.Find("Ankle");
            _footR = _ankleR.Find("Foot");
            _footL = _ankleL.Find("Foot");
            _lastPosition = transform.position;
        }
        Transform Arm(Transform shoulder, float upperLen, float lowerLen, float h, Material cloth, Material skin,
            bool right)
        {
            // 肩の丸み。ここが無いと腕が胴から生えているように見える。
            Blob("Deltoid", shoulder, new Vector3(0f, 0f, 0f),
                new Vector3(h * 0.074f, h * 0.072f, h * 0.072f), cloth);
            Capsule("UpperArm", shoulder, new Vector3(0f, -upperLen * 0.5f, 0f),
                upperLen + h * 0.015f, h * 0.030f, cloth);
            var elbow = Child("Elbow", shoulder, new Vector3(0f, -upperLen, 0f));
            // 袖は肘まで。前腕を肌色にすると「半袖の人間」に見える。
            Blob("ElbowJoint", elbow, Vector3.zero,
                new Vector3(h * 0.056f, h * 0.056f, h * 0.056f), skin);
            // 前腕と手を分ける。長さの合計は変えず、関節の位置だけ分ける。
            // 手首が無いと拳が前腕と一直線のままになり「棒の先に塊」に見える。
            float handLen = lowerLen * 0.24f;
            float foreLen = lowerLen - handLen;
            Capsule("Forearm", elbow, new Vector3(0f, -foreLen * 0.5f, 0f),
                foreLen + h * 0.008f, h * 0.025f, skin);
            // 手首。ここを回すと拳の向きが変わる。
            var wrist = Child("Wrist", elbow, new Vector3(0f, -foreLen, 0f));
            // 関節の穴埋め。前腕の先と拳の間に隙間が空くと腕が千切れて見える。
            Blob("WristJoint", wrist, Vector3.zero,
                new Vector3(h * 0.042f, h * 0.042f, h * 0.042f), skin);
            // 拳は球ではなく握った形（縦長）に。球だと手に見えない。
            Blob("Fist", wrist, new Vector3(0f, -(handLen + h * 0.005f), h * 0.003f),
                new Vector3(h * 0.049f, h * 0.057f, h * 0.051f), skin);
            if (right)
            {
                _wristR = wrist;
                _fistR = wrist.Find("Fist");
            }
            else
            {
                _wristL = wrist;
                _fistL = wrist.Find("Fist");
            }
            return elbow;
        }

        /// <summary>
        /// 右手に持たせる鉄パイプ。素手より「人」に見えるうえ、
        /// 「振りかぶる → 振り抜く」という攻撃の意味が通るようになる。
        /// </summary>
        Transform BuildWeapon(Transform wrist, float lowerLen, float h, Material cloth)
        {
            var steel = Solid(cloth, new Color(0.56f, 0.58f, 0.62f));
            var grip = Tint(cloth, 0.26f);
            float len = h * 0.30f;
            float r = h * 0.017f;
            // 握る位置は拳の中心（手首から手の長さぶん先）
            float handLen = lowerLen * 0.24f;
            var pivot = Child("Weapon", wrist,
                new Vector3(0f, -(handLen + h * 0.004f), h * 0.004f));
            // 前腕の延長のままだと真下を向いて床に刺さる。前へ傾けて「提げている」形にする。
            // さらに外側へ振る。真下だと脚の線と重なって「足に刺さっている」ように見える。
            pivot.localRotation = Quaternion.Euler(-55f, 0f, 16f);
            // 握りから先へ伸びる。手の後ろにも少しだけ出す（握りしろが見える）
            CapsuleShaped("Shaft", pivot, new Vector3(0f, -len * 0.5f + h * 0.030f, 0f),
                len, r, r, steel);
            CapsuleShaped("Grip", pivot, new Vector3(0f, -h * 0.002f, 0f),
                h * 0.070f, r * 1.05f, r * 1.05f, grip);
            // 予備動作でこの材質だけ光らせる
            if (steel != null)
            {
                steel.EnableKeyword("_EMISSION");
                steel.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                steel.SetColor(EmissionId, Color.black);
                _weaponSteel = steel;
                _weaponBase = steel.color;
            }
            return pivot;
        }
        Transform Leg(Transform hip, float thighLen, float shinLen, float h, Material cloth, Material shoe)
        {
            Capsule("Thigh", hip, new Vector3(0f, -thighLen * 0.5f, 0f),
                thighLen + h * 0.030f, h * 0.050f, cloth);
            var knee = Child("Knee", hip, new Vector3(0f, -thighLen, 0f));
            Blob("KneeJoint", knee, Vector3.zero,
                new Vector3(h * 0.078f, h * 0.078f, h * 0.078f), cloth);
            Capsule("Shin", knee, new Vector3(0f, -shinLen * 0.5f, 0f),
                shinLen + h * 0.020f, h * 0.036f, cloth);
            // IK の終点。足首の位置に置き、靴はここにぶら下げる
            var ankle = Child("Ankle", knee, new Vector3(0f, -shinLen, 0f));
            Blob("Foot", ankle, new Vector3(0f, h * 0.012f, h * 0.030f),
                new Vector3(h * 0.060f, h * 0.036f, h * 0.145f), shoe);
            return knee;
        }

        static Transform Child(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        /// <summary>服の色を暗くした派生マテリアル。髪・ベルト・靴を同じ系統でまとめる。</summary>
        Material Tint(Material source, float mul)
        {
            if (source == null)
                return null;
            var mat = new Material(source)
            {
                name = source.name + "_tint",
                color = new Color(source.color.r * mul, source.color.g * mul, source.color.b * mul,
                    source.color.a)
            };
            _ownedMaterials.Add(mat);
            return mat;
        }

        /// <summary>色をそのまま指定して作る派生マテリアル。金属など、服と別の質感を出したいとき用。</summary>
        Material Solid(Material source, Color color)
        {
            if (source == null)
                return null;
            var mat = new Material(source) { name = source.name + "_solid", color = color };
            _ownedMaterials.Add(mat);
            return mat;
        }
        void OnDestroy()
        {
            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                if (_ownedMaterials[i] == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(_ownedMaterials[i]);
                else
                    DestroyImmediate(_ownedMaterials[i]);
            }
            _ownedMaterials.Clear();
        }

        /// <summary>楕円体。球を 3 軸別々に伸ばして、角の無い塊を作る。</summary>
        static void Blob(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Paint(go, mat);
        }

        /// <summary>断面が楕円のカプセル。側面がまっすぐなので胴や腰に向く。</summary>
        static void CapsuleShaped(string name, Transform parent, Vector3 pos,
            float length, float radiusX, float radiusZ, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radiusX * 2f, length * 0.5f, radiusZ * 2f);
            Paint(go, mat);
        }

        static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            Paint(go, mat);
        }

        static void Sphere(string name, Transform parent, Vector3 pos, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * (radius * 2f);
            Paint(go, mat);
        }

        static void Capsule(string name, Transform parent, Vector3 pos, float length, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            NoCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            Paint(go, mat);
        }

        static void NoCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null)
                return;
            // 見た目用のパーツが当たり判定を持ると、移動や被弾の判定に混ざる
            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
        }

        static void Paint(GameObject go, Material mat)
        {
            if (mat == null)
                return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = mat;
        }
    }
    /// <summary>主人公の姿勢更新後、背後から肩を抱える腕を実際の体格に合わせる。</summary>
    [DefaultExecutionOrder(21000)]
    public sealed class BackHoldPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        CharacterAnimator _targetAnimator;

        /// <summary>顔面パンチの溜め位置（殴る側のローカル座標）。振り始めの拳から取る。</summary>
        Vector3 _faceChamberLocal;
        bool _hasFaceChamber;

        /// <summary>命中した瞬間の腕の角度。振り抜きから構えへ戻すときに使う。</summary>
        Quaternion _faceContactShoulder = Quaternion.identity;
        Quaternion _faceContactElbow = Quaternion.identity;
        bool _hasFaceContactPose;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;
            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            float punchWeight = _brain.PunchPoseWeight;
            if (punchWeight > 0f)
                AimPunch(punchWeight);
            float grabGrip = _brain.IsPreparingGrab
                ? Mathf.InverseLerp(0.65f, 0.95f, _brain.WindupProgress)
                : _brain.HeldThrowPull;
            if (grabGrip > 0f && _targetRig != null)
            {
                bool front = _brain.IsPreparingFrontGrab || _brain.HeldThrowPull > 0f;
                AimShoulderGrip(false, front ? _targetRig.LUpperArm : _targetRig.RUpperArm, grabGrip);
                AimShoulderGrip(true, front ? _targetRig.RUpperArm : _targetRig.LUpperArm, grabGrip);
            }
            if (_brain.IsPushing && _targetRig != null)
            {
                float time = _brain.PushElapsed;
                float touch = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.045f, EnemyBrain.PushContactSeconds, time));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.19f, 0.28f, time));
                AimShoulderGrip(false, _targetRig.RUpperArm, touch * (1f - release), shove: true);
                AimShoulderGrip(true, _targetRig.LUpperArm, touch * (1f - release), shove: true);
            }
            if (!_brain.IsHolding && !_brain.IsCatching) return;
            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (_brain.IsCatching && _targetRig != null && hold != null && hold.Captor == _brain)
            {
                float support = (1f - _brain.CatchImpact) * (1f - Mathf.SmoothStep(0f, 1f, hold.HandOffTurn));
                AimShoulderGrip(false, _targetRig.LUpperArm, support);
                AimShoulderGrip(true, _targetRig.RUpperArm, support);
            }
            if (!_brain.IsHolding)
                return;
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain || hold.Kind != GrappleKind.Back)
                return;
            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready || _targetRig.LUpperArm == null || _targetRig.RUpperArm == null)
                return;

            if (_targetRig.Head == null || _targetRig.Neck == null || _targetRig.Hips == null) return;
            if (_targetAnimator == null)
                _targetAnimator = _targetRig.GetComponent<CharacterAnimator>();
            float belly = _targetAnimator != null ? _targetAnimator.HeldBellyPunchFold : 0f;
            float face = _targetAnimator != null ? _targetAnimator.HeldFacePunchFlick : 0f;
            float lift = _targetAnimator != null ? _targetAnimator.HeldBellyCaptorLift : 0f;
            float ready = _targetAnimator != null ? _targetAnimator.HeldFaceCaptorBrace : 0f;
            float impact = _targetAnimator != null ? Mathf.Clamp01(_targetAnimator.HeldFacePunchImpulse) : 0f;
            // 顔パンチの合間も胸を引いて頭の間隔を保つ。命中時は膝を沈め、
            // 澪の頭が横へ振れたところに拘束役の肩が来ないようにする。
            if (_targetAnimator != null && (belly > 0f || impact > 0f || ready > 0f || Time.deltaTime <= 0f))
            {
                float fold = belly * Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(12f, 45f, _targetAnimator.DebugTorsoPitch));
                Vector3 headOffset = _targetRig.Head.position - _brain.Player.transform.position;
                float headSide = Mathf.Clamp(Vector3.Dot(headOffset, transform.right) / 0.08f, -1f, 1f);
                float leanBack = 12f * impact + ready * (20f + impact);
                _figure.PoseBackHold(fold, lift, impact * Mathf.Lerp(0.025f, 0.085f, ready),
                    leanBack, 9f * impact * headSide, 6f * impact);
            }

            // 胸が傾いても、そのフレームの肩幅と胴の向きに合わせて腕を回す。
            // 前回コミット（849ac5e）と同じ腕の形にする。服との当たりを避けて肘の向きを
            // 探索したり、必要以上に体を引いたりすると、彼女から腕が離れて
            // 「脇の下をくぐっていない」画になるため、羽交い締めでは行わない。
            Solve(false, _targetRig.RUpperArm, hold.GrabContactWeight, belly, face, lift, impact * ready);
            Solve(true, _targetRig.LUpperArm, hold.GrabContactWeight, belly, face, lift, impact * ready);
        }

        void AimShoulderGrip(bool left, Transform shoulder, float weight, bool shove = false)
        {
            if (weight <= 0f)
                return;

            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null || shoulder == null)
                return;

            Vector3 side = transform.right * (left ? -1f : 1f);
            Vector3 goal = shoulder.position + side * 0.03f - transform.forward * 0.04f;
            if (shove)
                goal -= transform.up * 0.055f + transform.forward * 0.035f;
            Vector3 pole = upper.position + side * 0.25f - transform.up * 0.24f;
            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Quaternion aimedElbow = elbow.rotation;
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, aimedElbow, weight);
        }

        /// <summary>
        /// 拳を狙いの座標（顔 / 腹）へ向ける。狙いは毎フレーム、プレイヤーの今の体から取り直すので、
        /// 相手が動いても拳はその場所へ届く（ホーミング）。届かない距離なら腕が伸びきって止まり、
        /// 当たり判定（拳と狙いの距離）も外れる。
        /// </summary>
        void AimPunch(float weight)
        {
            bool left = _brain.UsesLeftPunch;
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null || _brain.Player == null) return;

            PunchZone zone = _brain.Zone;
            Vector3 target = EnemyBrain.PunchTarget(_brain.Player, zone, _brain.BodyTargetBias);
            if (_brain.IsTagFacePunch)
            {
                AimTagFacePunch(upper, elbow, wrist, target, weight);
                return;
            }

            Vector3 outside = transform.right * (left ? -1f : 1f);
            Vector3 toMe = upper.position - target;
            toMe.y = 0f;
            toMe = toMe.sqrMagnitude > 1e-4f ? toMe.normalized : -transform.forward;

            Vector3 goal;
            if (_brain.IsBellyPunch)
            {
                // タッグ腹パンは「カメラ側の面」で止める。殴り手側（彼女の正面）で止めると
                // 拳が体の奥に沈み、カメラからは当たっている場所が見えない。
                // 画面の奥向き（カメラの forward）の逆へずらすと、拳が手前の面に出る。
                Camera cam = Camera.main;
                Vector3 screenIn = cam != null
                    ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up)
                    : Vector3.zero;
                screenIn = screenIn.sqrMagnitude > 1e-4f ? screenIn.normalized : -toMe;
                Vector3 chamber = upper.position + outside * 0.12f
                    - transform.up * 0.30f - transform.forward * 0.14f;
                Vector3 contact = target - screenIn * 0.11f;
                goal = Vector3.Lerp(chamber, contact, _brain.BellyPunchDrive);
            }
            else
            {
                // 顔は手前の表面で止める。通常の腹パンチは少しめり込ませる。
                float reach = zone == PunchZone.Face ? 0.09f : -0.03f;
                goal = target + toMe * reach;
            }

            // 肘は顔なら外へ、腹なら下・外へ向ける（腹を狙うときは肘が体の前に回らない）。
            // タッグ腹パンは肘を高く外へ張り、前腕を腹へ水平気味に差し込む（ボディーブロー）。
            float down = zone == PunchZone.Face ? 0.30f : (_brain.IsBellyPunch ? 0.28f : 0.50f);
            float out_ = _brain.IsBellyPunch ? 0.34f : 0.20f;
            Vector3 pole = upper.position - transform.up * down + transform.right * out_;
            if (_brain.IsBellyPunch)
                pole = upper.position + outside * 0.36f
                    - transform.up * Mathf.Lerp(0.42f, 0.12f, _brain.BellyPunchDrive);
            Quaternion oldUpper = upper.rotation;
            Quaternion oldElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Quaternion aimedUpper = upper.rotation;
            Quaternion aimedElbow = elbow.rotation;
            upper.rotation = Quaternion.Slerp(oldUpper, aimedUpper, weight);
            elbow.rotation = Quaternion.Slerp(oldElbow, aimedElbow, weight);
        }

        /// <summary>
        /// 拘束中の顔面パンチ。
        ///
        /// 拳を「構えの位置」から「彼女の頬」へ、外側を通る弧で振り抜く。
        /// 弧の進み具合を重みの 2 乗にして、当たる瞬間が一番速くなるようにする
        /// （等速で伸ばすと、当たる直前に減速して「やる気のないパンチ」になる）。
        /// 溜めの位置は、拳を振り始めた瞬間の実際の拳から取る（構えからの段差を消す）。
        /// </summary>
        void AimTagFacePunch(Transform upper, Transform elbow, Transform wrist,
            Vector3 target, float weight)
        {
            if (upper == null || elbow == null || wrist == null)
                return;

            float w = Mathf.Clamp01(weight);
            if (w < 0.04f)
            {
                _hasFaceChamber = false;
                _hasFaceContactPose = false;
            }

            // 振り抜き（Recover）は、命中時の角度から構えへ戻す。
            // 軌道で戻すと、肘が畳まれる途中で曲がる面が反転して拳が一瞬 40cm 飛ぶ。
            if (_brain.Phase == EnemyPhase.Recover)
            {
                if (_hasFaceContactPose)
                {
                    float back = 1f - w;
                    upper.localRotation = Quaternion.Slerp(_faceContactShoulder, upper.localRotation, back);
                    elbow.localRotation = Quaternion.Slerp(_faceContactElbow, elbow.localRotation, back);
                }
                return;
            }

            float s = w * w;

            bool left = _brain.UsesLeftPunch;
            Vector3 punchSide = _brain.TagFaceOutside;
            Transform fist = _figure.Fist(left);
            if (fist == null)
                return;

            // 溜め: 振り始めの拳の位置を、彼のローカルに控えて体の回転に追従させる。
            if (!_hasFaceChamber)
            {
                _faceChamberLocal = transform.InverseTransformPoint(fist.position);
                _hasFaceChamber = true;
            }

            Vector3 chamber = transform.TransformPoint(_faceChamberLocal);

            // 弧: 拳が彼女の顔の外側を通って回り込む。
            Vector3 arc = upper.position
                + transform.forward * 0.20f
                + punchSide * 0.30f
                + Vector3.up * 0.08f;

            // 当たる場所: 彼女の頭の中心から拳 1 つぶん手前（頬の表面）。
            // IK の終点は手首なので、拳がそのぶん先へ出ることを見込んで手前を狙う。
            Vector3 toHead = target - upper.position;
            Vector3 contact = target - toHead.normalized * 0.17f + punchSide * 0.02f;

            // 体が入りすぎて拳が肩へ寄ると、肘の曲がる面が反転して拳が飛ぶ。
            // 肩から最低限の距離を保つ（そのぶん彼女の顔から離れるが、判定半径内）。
            Vector3 contactFromShoulder = contact - upper.position;
            float contactDistance = contactFromShoulder.magnitude;
            if (contactDistance < 0.34f)
            {
                contact = upper.position
                    + (contactDistance > 1e-4f ? contactFromShoulder / contactDistance : transform.forward)
                    * 0.34f;
            }

            Vector3 point = (1f - s) * (1f - s) * chamber
                + 2f * (1f - s) * s * arc
                + s * s * contact;

            // 肘は外へ張る（フックの形）。体の前へ回すと「押している」画になる。
            Vector3 pole = upper.position
                + punchSide * 0.42f
                + Vector3.up * 0.18f
                + transform.forward * 0.04f;

            ProceduralRig.SolveTwoBone(upper, elbow, wrist, point, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);

            // 命中の瞬間の角度を控える（振り抜きはここから構えへ戻す）。
            if (w > 0.985f)
            {
                _faceContactShoulder = upper.localRotation;
                _faceContactElbow = elbow.localRotation;
                _hasFaceContactPose = true;
            }
        }

        /// <summary>
        /// 羽交い締めの腕を解く。
        ///
        /// 前回コミット（849ac5e）と同じ狙い。彼女の腕の下（脇の下）をくぐって、
        /// 手を首の後ろ〜上腕のあたりへ回す。`shoulder` は彼女の上腕（左右で渡す）。
        /// </summary>
        void Solve(bool left, Transform shoulder, float contact, float belly, float face, float lift, float duck)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            float side = left ? -1f : 1f;
            Transform player = _brain.Player.transform;

            // 手は首の後ろ（後頭部の下）から上腕のあたりへ。高く置くと上腕が水平になり、
            // 彼女の腕の「上」を通ってしまうので、やや低めにする。
            Vector3 goal = _targetRig.Neck.position
                + player.forward * 0.04f
                + player.right * (side * (0.18f + 0.045f * face + 0.025f * duck))
                - Vector3.up * (0.06f + 0.025f * belly + 0.05f * face + 0.035f * duck);

            // 肘は彼女の脇の下（体の側面、やや前）へ入れる。腕が「肘を前、手を後ろ」に
            // 折れて、彼女の腕の下をくぐるのが真横からも見える。
            Vector3 pole = shoulder.position - transform.up * 0.55f
                + player.right * (side * (0.34f + 0.10f * face))
                + player.forward * 0.28f;

            goal = Vector3.Lerp(goal, shoulder.position - transform.forward * 0.045f, contact);
            goal += (Vector3.up * 0.085f - player.forward * 0.075f) * lift;
            pole = Vector3.Lerp(pole,
                upper.position + transform.right * (side * 0.25f) - transform.up * 0.24f, contact);

            // IK の終点は手首。拳まで含めると腕がひと手ぶん短くなり、脇の下へ届かず
            // 背中側で止まって「抱いていない」画になる。
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }

    /// <summary>
    /// 彼女の姿勢更新後、正面から両肩を押さえつける腕を実際の体格に合わせる。
    /// 手は肩へ置き、肘は外へ開く（腕を体の前で交差させると「抱きしめている」画になる）。
    /// 押さえつけ中はカメラも少し寄せる。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class PinHoldPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;

        /// <summary>もがかれて肩が浮いた分（m）。手の狙いを上げる。</summary>
        float _lift;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;
            if (!_brain.IsPinning)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain || hold.Kind != GrappleKind.Pin)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready ||
                _targetRig.LUpperArm == null || _targetRig.RUpperArm == null)
                return;

            // 折れた胴に沿って腹の正面を取り直し、膝頭の表面が当たる位置に関節を置く。
            if (hold.KneePulse > 0f && _targetRig.Spine != null && _targetRig.Chest != null)
            {
                Vector3 spine = _targetRig.Spine.position;
                Vector3 chest = _targetRig.Chest.position;
                Vector3 front = Vector3.ProjectOnPlane(_brain.Player.transform.forward, chest - spine).normalized;
                Vector3 belly = Vector3.Lerp(spine, chest, 0.60f) + front * (0.085f + _figure.Height * 0.039f);
                var cam = Camera.main;
                bool left = cam != null && Vector3.Dot(cam.transform.position - transform.position, transform.right) < 0f;
                float kick = Mathf.SmoothStep(0f, 1f, hold.KneePulse);
                _figure.AlignPinKnee(left, belly, kick);
                // 膝を届かせる骨盤の踏み込みと同じフレームに、跳ね上がる顔から胸と顎を引く。
                _figure.BraceHoldUpperBody(33f * kick, 0f, 20f * kick);
            }

            // 彼は彼女と正面で向かい合っている。彼の右手は彼女の左腕、左手は右腕を掴む。
            // 同じ側（右→右）にすると腕が交差して「左右が逆」に見える（実測）。
            Solve(false, _targetRig.LUpperArm, _targetRig.LLowerArm, _targetRig.LHand, hold.GrabContactWeight);
            Solve(true, _targetRig.RUpperArm, _targetRig.RLowerArm, _targetRig.RHand, hold.GrabContactWeight);

            // もがかれるほど彼女の肩が浮く（押し返されている）。手は毎フレーム腕を追う。
            _lift = 0.12f * Mathf.Clamp01(hold.Progress);

            // 演出中だけ寄る。HoldFocus は「毎フレーム呼んで構図を保つ」API で、
            // PushDistance のように減衰で薄まらない（実測: Push だと 0.28 を指定しても 0.37 までしか寄らなかった）。
            if (_camera == null)
                _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null)
            {
                if (hold.KneePulse > 0f)
                    _kneeCamUntil = Time.time + 0.30f;

                Transform player = _brain.Player.transform;
                float facing = player.forward.x;
                float sign = Mathf.Abs(facing) > 0.1f ? Mathf.Sign(facing) : 1f;

                // 画面の中心を彼女の正面方向へ少しずらす。彼女が画面の左（逆向きなら右）へ寄り、
                // 正面に立つ相手と膝が入る余白ができる。
                float x = player.position.x + sign * 0.30f;

                if (Time.time < _kneeCamUntil)
                {
                    // 秒数は腹パンと同じ 0.5。短くすると減衰で寄りが薄まる
                    // （実測: 0.12 秒だと 0.28 指定でも実効 0.46 までしか寄らない）。
                    _camera.HoldFocus(KneeCamDistance, KneeCamPivot, x, 0.5f, KneeCamPitch, KneeCamYaw * sign);
                }
                else
                {
                    _camera.HoldFocus(0.72f, -0.10f, x, 0.5f);
                }

                if (_pinLog && Time.time >= _pinCamLogAt)
                {
                    _pinCamLogAt = Time.time + 0.2f;
                    var cam = _camera.GetComponent<Camera>();
                    if (cam != null)
                    {
                        Vector3 v = cam.WorldToViewportPoint(player.position + Vector3.up * 1.0f);
                        Debug.Log($"[PinCenter] near={Time.time < _kneeCamUntil} " +
                                  $"vp=({v.x:F2},{v.y:F2}) focusX={x:F2} beltX={_camera.CurrentX:F2}");
                    }
                }
            }
        }

        // 膝蹴りの演出カメラ。寄りは腹パンと同じ 0.28 だが、彼女は前傾しているので
        // 画面の中心を胸（+0.27）ではなく体の真ん中あたり（+0.05）へ下げ、
        // 頭から腰までが入るようにする。
        const float KneeCamDistance = 0.28f;
        const float KneeCamPivot = 0.05f;
        const float KneeCamPitch = 2f;
        const float KneeCamYaw = -32f;

        /// <summary>膝蹴りの寄りを保つ終了時刻。</summary>
        float _kneeCamUntil;

        /// <summary>次の [PinCenter] ログまで（一時診断）。</summary>
        float _pinCamLogAt;

        /// <summary>構図の中心をログに出す（-pinlog）。</summary>
        bool _pinLog;

        void Awake()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-pinlog", System.StringComparison.OrdinalIgnoreCase))
                    _pinLog = true;
            }
        }

        void Solve(bool left, Transform shoulder, Transform lowerArm, Transform hand, float contact)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null || lowerArm == null)
                return;

            Transform player = _brain.Player.transform;

            // 手は彼女の前腕（手首寄り）を掴む。肩を押さえるより「押さえ込んでいる」形になり、
            // 彼女が腕を振りほどこうとする動きとも噛み合う。
            Vector3 grip = hand != null
                ? Vector3.Lerp(lowerArm.position, hand.position, 0.75f)
                : lowerArm.position;
            Vector3 goal = grip + Vector3.up * 0.02f + _lift * Vector3.up;

            // 肘は外へ開き、少し下へ。真横から見て「押し込んでいる」形にする。
            float side = left ? -1f : 1f;
            Vector3 pole = grip
                + transform.right * (side * 0.55f)
                - Vector3.up * 0.30f;

            goal = Vector3.Lerp(goal, shoulder.position - transform.forward * 0.045f, contact);
            pole = Vector3.Lerp(pole, upper.position + transform.right * (side * 0.25f) - transform.up * 0.24f, contact);
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }

    /// <summary>
    /// 彼女の姿勢更新後、正面から胴を抱え込む腕を実際の体格に合わせる。
    /// 両腕を澪の上腕の外から背中へ回し、胴と腕をまとめて抱える。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class BearHugPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;
            if (!_brain.IsBearHugging)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain
                || hold.Kind != GrappleKind.BearHug)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready
                || _targetRig.LUpperArm == null || _targetRig.RUpperArm == null)
                return;

            // 腕ごと抱えられるよう、手は肩より少し下の背中へ回す。
            Transform player = _brain.Player.transform;
            Vector3 center = _targetRig.Spine != null
                ? Vector3.Lerp(_targetRig.Spine.position, _targetRig.Chest != null
                    ? _targetRig.Chest.position : _targetRig.Spine.position, 0.80f)
                : player.position + player.up * 1.17f;
            center -= player.forward * 0.105f;

            // 抱え上げるほど手を上へ、締め付けるほど手を内側へ寄せる。
            // 締め付け中は細かく震わせて、力を込めているのを見せる。
            float lift = hold.LiftPulse;
            float squeeze = hold.SqueezePulse;
            float yank = hold.PullPulse;
            var chest = _figure.ChestBone;
            if (chest != null)
            {
                float waiting = hold.FrontEscapePoseWeight;
                float struggle = Mathf.Lerp(hold.StrugglePulse, hold.FrontEscapeEffort, waiting);
                float effort = Mathf.Clamp01(0.85f * squeeze + 0.55f * struggle + 0.2f * yank + 0.28f * waiting);
                BracePlayerArm(true, player, chest, effort);
                BracePlayerArm(false, player, chest, effort);
            }
            float tremor = Mathf.Sin(Time.time * 31f) * 0.008f * squeeze;

            // 引き寄せでは手を自分の胸元へ引き込み、彼女を抱き寄せる。
            Vector3 pull = player.up * (0.035f * lift + 0.028f * squeeze + tremor)
                + player.forward * (0.025f * yank);
            float spread = Mathf.Lerp(0.085f, 0.068f, squeeze);

            WrapArm(false, player, center + pull, spread);
            WrapArm(true, player, center + pull, spread);

            // 二人の顔と背中に回した腕が入る高さで寄り、締め付けで少しだけ近づける。
            if (_camera == null)
                _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null)
            {
                float sign = player.forward.x >= 0f ? 1f : -1f;
                float close = Mathf.Clamp01(hold.SqueezePulse);
                float x = player.position.x + sign * 0.16f;
                _camera.HoldFocus(
                    Mathf.Lerp(0.25f, 0.21f, close),
                    Mathf.Lerp(0.30f, 0.36f, close),
                    x, 0.5f, 3f, -24f * sign);
            }
        }

        void BracePlayerArm(bool left, Transform player, Transform chest, float effort)
        {
            var upper = left ? _targetRig.LUpperArm : _targetRig.RUpperArm;
            var elbow = left ? _targetRig.LLowerArm : _targetRig.RLowerArm;
            var wrist = left ? _targetRig.LHand : _targetRig.RHand;
            if (upper == null || elbow == null || wrist == null)
                return;

            // 向かい合うので、澪の左手は相手の右胸へ。胴の丸みに沿って掌を当てる。
            float side = left ? 1f : -1f;
            Vector3 goal = chest.TransformPoint(_figure.Height *
                new Vector3(side * 0.078f, Mathf.Lerp(0.073f, 0.097f, effort), 0.055f));
            Vector3 pole = upper.position
                - player.right * (side * Mathf.Lerp(0.16f, 0.38f, effort))
                - player.up * Mathf.Lerp(0.34f, 0.19f, effort)
                + player.forward * 0.10f;

            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, player.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Vector3 inward = -chest.forward - chest.right * (0.55f * side);
            _targetRig.AlignPalm(left, inward.normalized, chest.up);
        }

        void WrapArm(bool left, Transform player, Vector3 behind, float spread)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            // 向かい合う相手の上腕を基準にし、もがいて肘が動いても外側から包む。
            var playerShoulder = left ? _targetRig.RUpperArm : _targetRig.LUpperArm;
            var playerElbow = left ? _targetRig.RLowerArm : _targetRig.LLowerArm;
            Vector3 arm = playerElbow != null
                ? Vector3.Lerp(playerShoulder.position, playerElbow.position, 0.55f)
                : playerShoulder.position;
            Vector3 outward = player.right * (left ? 1f : -1f);
            Vector3 pole = Vector3.Lerp(upper.position, arm, 0.45f)
                + outward * 0.80f + player.up * 0.15f;

            ProceduralRig.SolveTwoBone(upper, elbow, wrist, behind + outward * spread, pole, player.up);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }

}
