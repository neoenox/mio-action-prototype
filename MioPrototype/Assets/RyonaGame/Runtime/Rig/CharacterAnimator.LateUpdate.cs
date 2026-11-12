using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
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
            if (Time.frameCount != _heldFacePunchStartFrame)
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
            if (Stats != null && Stats.State != null && Stats.State.IsDown && !Stats.IsDead
                && _state != State.Down && (_state != State.GetUp || !Stats.IsGettingUp)
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
                    // 倒れ終えても、操作が来るまでは床に留まる。HP 0 からの復帰もここを通る。
                    if (Time.deltaTime > 0f && _stateTime >= _stateDuration &&
                        (Stats == null || (!Stats.IsDead && (!Stats.IsDown || Stats.GetUpRequested))))
                    {
                        float getUp = Mathf.Max(0.2f, GetUpSeconds);
                        SetState(State.GetUp, getUp);
                        Stats?.BeginGetUp(getUp);
                        Debug.Log($"[Anim] ダウン終了 → 立ち上がり {GetUpSeconds:F2}s");
                        if (Stats != null)
                            Stats.AddStun(getUp);
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
