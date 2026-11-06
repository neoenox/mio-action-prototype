using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 横一列の移動。脚の部位ダメージが速度に反映される。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Ryona/Player Motor")]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("References")]
        public PlayerStats Stats;
        public Transform CameraTransform;

        [Header("Tuning")]
        public float BaseSpeed = 4.2f;
        public float Acceleration = 28f;
        public float TurnSpeed = 14f;
        public float Gravity = -22f;

        [Tooltip("ベルト用の向き。奥行き（画面の上下）に動くときは向きを変えず、左右入力でだけ向き直る")]
        [System.NonSerialized]
        public bool BeltFacing = true;

        /// <summary>ベルトの関所（<see cref="BeltScreenLock"/>）が決める、前進できる上限 X。</summary>
        [System.NonSerialized]
        public float ForwardLimitX = float.PositiveInfinity;

        /// <summary>画面の左端。ここより左へは下がれない。</summary>
        [System.NonSerialized]
        public float BackLimitX = float.NegativeInfinity;

        float _laneZ;
        CharacterController _controller;
        Vector3 _velocity;
        float _verticalVelocity;
        float _lastFacingSign = 1f;

        /// <summary>1ライン上の向き。回転途中や初期姿勢では直前の左右を使う。</summary>
        public float LaneFacingSign
        {
            get
            {
                float x = transform.forward.x;
                if (Mathf.Abs(x) > 0.01f) _lastFacingSign = Mathf.Sign(x);
                return _lastFacingSign;
            }
        }

        public void SnapToLaneFacing()
        {
            transform.rotation = Quaternion.LookRotation(Vector3.right * LaneFacingSign, Vector3.up);
        }

        // 攻撃の踏み込み（root を前へ出す）
        float _lungeSpeed;
        float _lungeLeft;
        PlayerGrapple _grapple;
        float _lungeTravel;
        float _lungeFade;
        Vector3 _lungeStart;
        Vector3 _lungeDir = Vector3.forward;
        Vector3 _lungeFootPos;
        string _lungeId;

        // 突き飛ばされ（掴み失敗時に相手へ押し返される）
        float _knockLeft;
        float _knockSpeed;
        Vector3 _knockDir = Vector3.back;
        Vector3 _knockStart;

        public bool IsMoving { get; private set; }

        /// <summary>
        /// 踏み込みで実際に前へ出た距離。足を床に残すために Animator が読む。
        /// 踏み込みが終わると 0 へ戻る（足が体の下へ寄る）。
        /// </summary>
        public float LungeTravel => _lungeTravel;

        /// <summary>
        /// 攻撃の踏み込み。発生の瞬間までに distance ぶん前へ出る。
        /// 当たり判定は root の位置で決まるので、出た分だけ間合いが伸びる。
        /// 壁や相手はキャラクタコントローラが押し返すので、めり込まない。
        /// </summary>
        public void Lunge(float distance, float seconds, string attackId = null)
        {
            if (distance <= 0f || seconds <= 0.01f)
                return;

            _lungeSpeed = distance / seconds;
            _lungeLeft = seconds;
            _lungeId = attackId;
            // 前の踏み込みの戻しが終わる前に次が来ても、足を跳ばさないよう 0 に戻さない。
            _lungeFade = distance / 0.22f;   // 0.22 秒かけて足を体の下へ戻す
            _lungeStart = transform.position;
            _lungeDir = transform.forward;

            // 検証用: 踏み込みで足が滑っていないか（前足の位置）を測る
            var rig = GetComponentInChildren<ProceduralRig>();
            _lungeFootPos = rig != null && rig.LFoot != null ? rig.LFoot.position : new Vector3(float.NaN, 0f, 0f);
        }

        void Awake()
        {
            _laneZ = transform.position.z;
            _controller = GetComponent<CharacterController>();
            if (Stats == null)
                Stats = GetComponent<PlayerStats>();
        }

        /// <summary>
        /// 後ろへ突き飛ばされる。掴みから逃げられなかったときに使い、
        /// 怯んでいても止めない（その場に残すと包囲されて袋叩きになる）。
        /// </summary>
        public void Knockback(float distance, float seconds, Vector3? direction = null)
        {
            if (distance <= 0f || seconds <= 0.01f)
                return;

            _knockDir = direction ?? -transform.forward;
            _knockDir.y = 0f;
            _knockDir.z = 0f;
            _knockDir = _knockDir.sqrMagnitude > 1e-4f ? _knockDir.normalized : Vector3.left;

            _knockSpeed = distance / seconds;
            _knockLeft = seconds;
            _knockStart = transform.position;

            Debug.Log($"[Knockback] {distance:F2}m / {seconds:F2}s");
        }

        // ダウンの放物線（吹き飛ばされ）
        float _launchUp;
        float _launchLeft;

        /// <summary>空中にいるか（演出側が「飛ばされている」姿勢を選ぶのに使う）。</summary>
        public bool IsAirborne => _launchLeft > 0f;

        /// <summary>
        /// 後ろへ放物線を描いて吹き飛ばす。`height` は最高到達点（m）。
        /// 滞空時間は重力から決まるので、`distance` の水平移動をその時間で割って飛ばす。
        /// </summary>
        public void LaunchBack(float distance, float height, Vector3? direction = null)
        {
            float g = Mathf.Abs(Gravity) > 0.01f ? Mathf.Abs(Gravity) : 22f;
            _launchUp = Mathf.Sqrt(2f * g * Mathf.Max(0.05f, height));
            float air = 2f * _launchUp / g;
            _launchLeft = air;

            // 上向きの初速を渡す（以降は Update の重力が放物線を作る）
            _verticalVelocity = _launchUp;

            Knockback(distance, air, direction);
            Debug.Log($"[Launch] {distance:F2}m h={height:F2} air={air:F2}s");
        }
        void LateUpdate()
        {
            var position = transform.position;
            position.z = _laneZ;
            transform.position = position;
        }

        void Update()
        {
            if (Stats == null || Stats.IsDead)
            {
                IsMoving = false;
                return;
            }

            // 掴まれている間は動けない（レバガチャだけ受け付ける）。
            // 踏み込み・ノックバック・吹き飛びの残り時間はここで切る。
            // 減らさずに抱えていると、解放直後に不意に滑り出す。
            if (_grapple == null)
                _grapple = GetComponent<PlayerGrapple>();
            if (_grapple != null && _grapple.IsGrappled)
            {
                IsMoving = false;
                Stats.IsMoving = false;
                _velocity = Vector3.zero;
                _lungeLeft = 0f;
                _lungeTravel = 0f;
                _knockLeft = 0f;
                _launchLeft = 0f;
                return;
            }

            Vector2 input = Stats.IsStunned || Stats.State.IsDown ? Vector2.zero : InputReader.Move();

            // 横一列の戦闘: 上下入力とカメラの奥行きには移動しない。
            Vector3 wish = Vector3.right * input.x;
            if (wish.sqrMagnitude > 1f)
                wish.Normalize();

            float speed = BaseSpeed * Stats.State.MoveSpeedMultiplier;
            Vector3 target = wish * speed;
            _velocity = Vector3.MoveTowards(_velocity, target, Acceleration * Time.deltaTime);

            if (wish.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = wish;

                // ベルト: 奧行き（画面の上下）に動くときは向きを変えない。
                // 上を押すたびにカメラへ向いてしまうと、横顔で歩けなくなる。
                if (BeltFacing)
                    dir = Mathf.Abs(wish.x) < 0.2f
                        ? Vector3.zero
                        : new Vector3(Mathf.Sign(wish.x), 0f, 0f);

                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion look = Quaternion.LookRotation(dir);
                    if (BeltFacing)
                    {
                        _lastFacingSign = Mathf.Sign(dir.x);
                        transform.rotation = look;
                    }
                    else
                        transform.rotation = Quaternion.Slerp(transform.rotation, look, TurnSpeed * Time.deltaTime);
                }
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            _verticalVelocity += Gravity * Time.deltaTime;

            Vector3 motion = _velocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            // ベルトの画面。左右の端より外へは歩けない（ファイナルファイトと同じ）。
            if (transform.position.x > ForwardLimitX || transform.position.x < BackLimitX)
            {
                Vector3 clamped = transform.position;
                clamped.x = Mathf.Clamp(clamped.x, BackLimitX, ForwardLimitX);
                transform.position = clamped;
                _velocity.x = 0f;
            }

            // 踏み込み。当たり判定の瞬間に予定の距離まで届くよう、残り時間で刻む。
            // 被弾したら踏み込みは止める（スタッガー中に滑っていかない）。
            if (_lungeLeft > 0f)
            {
                if (Stats.IsStunned)
                {
                    _lungeLeft = 0f;
                }
                else
                {
                    float step = Mathf.Min(Time.deltaTime, _lungeLeft);
                    _lungeLeft -= step;
                    _controller.Move(Vector3.right * Mathf.Sign(transform.forward.x) * (_lungeSpeed * step));
                    _lungeTravel += _lungeSpeed * step;

                    if (_lungeLeft <= 0f)
                    {
                        Vector3 d = transform.position - _lungeStart;
                        d.y = 0f;

                        string foot = "";
                        var rig = GetComponentInChildren<ProceduralRig>();
                        if (rig != null && rig.LFoot != null && !float.IsNaN(_lungeFootPos.x))
                        {
                            // 進行方向に射影する（体が回っても比較できるように）
                            Vector3 slide = rig.LFoot.position - _lungeFootPos;
                            slide.y = 0f;
                            foot = $" footDz={Vector3.Dot(slide, _lungeDir):F3}";

                            if (rig.RFoot != null)
                            {
                                float kneeL = Vector3.Angle(
                                    rig.LLowerLeg.position - rig.LUpperLeg.position,
                                    rig.LFoot.position - rig.LLowerLeg.position);
                                float kneeR = Vector3.Angle(
                                    rig.RLowerLeg.position - rig.RUpperLeg.position,
                                    rig.RFoot.position - rig.RLowerLeg.position);

                                foot += $" relFootY={(rig.LFoot.position.y - transform.position.y):F3}" +
                                    $"/{(rig.RFoot.position.y - transform.position.y):F3}" +
                                    $" knee={kneeL:F0}/{kneeR:F0}";
                            }
                        }

                        Debug.Log($"[Lunge] {_lungeId} moved={d.magnitude:F2}{foot}");
                    }
                }
            }

            // 踏み込みが終わったら、足が体の下へ戻るぶんだけ踏み込み量を戻す。
            if (_lungeLeft <= 0f && _lungeTravel > 0f)
                _lungeTravel = Mathf.MoveTowards(_lungeTravel, 0f, _lungeFade * Time.deltaTime);

            // 突き飛ばされ。怯み中でも止めない（囲みから抜けるための移動）
            if (_knockLeft > 0f)
            {
                float knockStep = Mathf.Min(Time.deltaTime, _knockLeft);
                _knockLeft -= knockStep;
                _controller.Move(_knockDir * (_knockSpeed * knockStep));

                if (_knockLeft <= 0f)
                {
                    Vector3 moved = transform.position - _knockStart;
                    moved.y = 0f;
                    Debug.Log($"[Knockback] moved={moved.magnitude:F2}m");
                }
            }

            // 着地したら空中扱いを終える（次フレームからは普通の接地）
            if (_launchLeft > 0f)
            {
                _launchLeft = Mathf.Max(0f, _launchLeft - Time.deltaTime);
                if (_launchLeft <= 0f)
                    Debug.Log($"[Launch] landed y={transform.position.y:F2}");
            }

            IsMoving = _velocity.sqrMagnitude > 0.05f;
            Stats.IsMoving = IsMoving;
        }
    }
}
