using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
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
            if (punchWeight > 0f || _brain.KeepsBellyPunchArm)
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
            if (!_brain.IsHolding && !_brain.IsCatching && !_brain.IsHandOffLocked) return;
            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            float rearGrip = hold != null ? hold.PinHandOffGrip(_brain) : 0f;
            if (rearGrip > 0f && _targetRig != null)
            {
                AimShoulderGrip(false, _targetRig.RUpperArm, rearGrip);
                AimShoulderGrip(true, _targetRig.LUpperArm, rearGrip);
            }
            if (!_brain.IsHolding && !_brain.IsCatching) return;
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
            // 羽交い締めの上体を基準に、顔パンチでは膝を沈めて肩を逃がす。
            // 構えと命中時の反りは重ねず、大きい方だけを使う。
            if (_targetAnimator != null && (belly > 0f || impact > 0f || ready > 0f || Time.deltaTime <= 0f))
            {
                float fold = belly * Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(12f, 45f, _targetAnimator.DebugTorsoPitch));
                Vector3 headOffset = _targetRig.Head.position - _brain.Player.transform.position;
                float headSide = Mathf.Clamp(Vector3.Dot(headOffset, transform.right) / 0.08f, -1f, 1f);
                float leanBack = Mathf.Max(3f * ready, 9f * impact);
                // 膝と股関節で重心を引き、胸の角度を足さずに肩を澪の後ろへ逃がす。
                Vector3 braceOffset = new Vector3(0f, -impact * Mathf.Lerp(0.025f, 0.10f, ready),
                    -ready * (0.06f + 0.025f * impact));
                _figure.PoseBackHold(fold, lift, braceOffset, leanBack, 6f * impact * headSide, 3f * impact);
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

            // 通常のパンチはベルトの左右へ振る。彼女の体（頭・腹）が呼吸や怯みで
            // 前後へ揺れても、拳の狙いをレーンへ寄せて斜めに振らない。
            // タッグ技（腹 / 顔）は演出側が位置と弧を決めているので触らない。
            if (!_brain.IsTagPunch)
                target.z = transform.position.z;

            if (_brain.IsTagFacePunch)
            {
                AimTagFacePunch(upper, elbow, wrist, target, weight);
                return;
            }

            Vector3 outside = transform.right * (left ? -1f : 1f);
            Vector3 toMe = upper.position - target;
            toMe.y = 0f;
            toMe = toMe.sqrMagnitude > 1e-4f ? toMe.normalized : -transform.forward;

            Transform end = wrist;
            Vector3 goal;
            Vector3 pole;
            bool keepsBellyArm = _brain.KeepsBellyPunchArm;
            if (_brain.IsBellyPunch || keepsBellyArm)
            {
                // 腹の正面に拳の中心を合わせる。肘は下に残し、前腕を斜め上へ差し込む。
                end = _figure.Fist(left);
                // 溜めの拳も肘より前に置き、伸ばす途中で肘の曲がる面をまたがない。
                Vector3 chamber = upper.position + outside * 0.07f
                    - transform.up * 0.20f + transform.forward * 0.19f;
                Vector3 contact = target + _brain.Player.transform.forward * 0.13f;
                float drive = _brain.IsBellyPunch ? _brain.BellyPunchDrive : 0f;
                if (keepsBellyArm)
                {
                    // 次の一発まで同じ溜めへ戻す。途中で通常ガードを挟むと拳が上下に揺れる。
                    if (_brain.Phase == EnemyPhase.Recover)
                        drive *= weight;
                    weight = 1f;
                }
                goal = Vector3.Lerp(chamber, contact, drive);
                pole = upper.position + outside * 0.12f - transform.up * 0.52f;
            }
            else
            {
                // 顔は手前の表面で止める。通常の腹パンチは少しめり込ませる。
                float reach = zone == PunchZone.Face ? 0.09f : -0.03f;
                goal = target + toMe * reach;
                float down = zone == PunchZone.Face ? 0.30f : 0.50f;
                pole = upper.position - transform.up * down + transform.right * 0.20f;
            }

            Quaternion oldUpper = upper.rotation;
            Quaternion oldElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, end, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, end);
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
}
