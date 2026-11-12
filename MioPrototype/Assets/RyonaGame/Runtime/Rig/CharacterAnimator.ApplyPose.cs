using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        /// <summary>組み立てた姿勢をリグへ流し込む（IK・倒れ・指・かかと）。</summary>
        void ApplyPoseToRig(PoseValues pose)
        {
            Vector3 pole = transform.position + transform.forward * 2f + Vector3.up * 0.55f;

            // 倒れる途中の足。IK は「倒す前」の座標で解き、そのあと LayBody で
            // 体ごと倒れるので、狙った床の位置に足を置くには倒れるぶんを打ち消す。
            // さらに倒れきる直前で、足を「脚を伸ばした先」へ滑らせる（膝の二つ折り防止）。
            if (pose.LayAngle < -1f)
            {
                Vector3 hipsLocal = Rig.RestHips + pose.HipsOffset;
                float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-70f, -88f, pose.LayAngle));
                Vector3 fallAxis = Quaternion.AngleAxis(pose.LaySide, Vector3.up) * Vector3.right;
                Quaternion fallRotation = Quaternion.AngleAxis(pose.LayAngle, fallAxis);

                // Let the heels slide apart as the back lands, with one knee left a little more bent.
                Vector3 extendedL = hipsLocal + fallRotation * (Rig.RestFootL - Rig.RestHips)
                    + new Vector3(-0.035f, -0.025f, -0.105f);
                Vector3 extendedR = hipsLocal + fallRotation * (Rig.RestFootR - Rig.RestHips)
                    + new Vector3(0.020f, -0.025f, -0.032f);

                pose.FootL = Vector3.Lerp(pose.FootL, extendedL, follow);
                pose.FootR = Vector3.Lerp(pose.FootR, extendedR, follow);

                // IK は「倒す前」の姿勢で解き、そのあと LayBody で体ごと倒れる。
                // 狙った床の位置に足を着けるには、倒れるぶんを打ち消しておく。
                Quaternion undo = Quaternion.Inverse(fallRotation);
                pose.FootL = hipsLocal + undo * (pose.FootL - hipsLocal);
                pose.FootR = hipsLocal + undo * (pose.FootR - hipsLocal);
            }

            // 押された瞬間に足が残り、片足で体勢を立て直し始める。
            // ここで足先を動かすと、この後の IK（SolveLeg）が脚の形を合わせてくれるので、
            // 「滑っている」ではなく「踏ん張って持ちこたえている」に見える。
            if (Grapple != null && !Grapple.IsHandingOff && Grapple.PushPulse > 0f && _state == State.Grabbed)
            {
                float p = Mathf.Clamp01(Grapple.PushPulse);
                float t = 1f - p;                                   // 0 → 1（押され始め → 終わり）
                float beat = t * Mathf.PI * 2f;
                float stepL = Mathf.Max(0f, Mathf.Sin(beat));       // 左足の踏み直し
                float stepR = Mathf.Max(0f, -Mathf.Sin(beat));      // 右足の踏み直し
                float drag = Mathf.Sin(Mathf.PI * t);               // 押されて足が残る量

                // 向きを変えても、足は実際に押された向きと逆へ一瞬残る。
                Vector3 localPush = Vector3.forward * (Grapple.Kind == GrappleKind.Back ? -1f : 1f);
                pose.FootL += new Vector3(0.02f, 0.08f * stepL, 0f)
                    + localPush * (0.08f * stepL - 0.13f * drag);
                pose.FootR += new Vector3(-0.02f, 0.08f * stepR, 0f)
                    + localPush * (0.08f * stepR - 0.13f * drag);

                // 踏み直す足はつま先立ちになる（かかとが浮く）
                pose.FootPitchL = Mathf.Max(pose.FootPitchL, 18f * stepL);
                pose.FootPitchR = Mathf.Max(pose.FootPitchR, 18f * stepR);

                // 押されて腰が落ちる
                pose.HipsOffset.y -= 0.025f * drag;
            }

            // 腰のずれはキャラクター基準。ワールドのまま足すと、体が回ったときに
            // 「後ろへ押される」が横や前へずれる。
            // 踏み込みで root が前へ出た分、足を後ろへ残す（足が床を滑らない）。
            // 踏み込みが終わると 0 に戻るので、戻る分だけ足が体の下へ寄る。
            if (Motor != null && Motor.LungeTravel > 0.0005f)
            {
                Vector3 back = transform.InverseTransformDirection(Motor.transform.forward);
                back.y = 0f;
                pose.FootL -= back * Motor.LungeTravel;
                pose.FootR -= back * Motor.LungeTravel;
            }

            // 腕の狙いを体幹に追従させるための、このフレームの体幹角度。
            // ArmDirection が「肩の面」を基準に腕の向きを作るのに使う。
            _armFollowYaw = pose.TorsoYaw;
            _armFollowPitch = pose.TorsoPitch;
            _armFollowRoll = pose.TorsoRoll;

            Rig.OffsetHips(transform.TransformDirection(pose.HipsOffset));
            Rig.BendTorso(pose.TorsoPitch, pose.TorsoRoll, pose.TorsoYaw);
            ApplyFaceFlick(ref pose.HeadPitch, ref pose.HeadYaw, ref pose.HeadRoll);
            Rig.TurnHead(pose.HeadPitch, pose.HeadYaw, pose.HeadRoll);
            // 姿勢を組んだ後の頭の位置。次のフレームで「敵の顔を見る」角度を測るのに使う
            // （姿勢を組む前は ResetPose で立ち姿勢の頭の高さに戻っていて、見上げる角度が浅くなる）。
            _posedHeadPosition = Rig.Head.position;
            _hasPosedHead = true;

            Rig.SetArm(true, ArmDirection(true, pose.ArmLUpper).normalized,
                ArmDirection(true, pose.ArmLFore).normalized, pose.HandRollL);
            Rig.SetArm(false, ArmDirection(false, pose.ArmRUpper).normalized,
                ArmDirection(false, pose.ArmRFore).normalized, pose.HandRollR);

            // 頭上拘束は手首の位置そのものが見せ場なので、方向ベクトルではなく IK で
            // 頭の上へ置く（前腕は方向ベクトルに反応しない。docs/GRAPPLE_NOTES.md §2）。
            if (Grapple != null && !Grapple.IsHandingOff && Grapple.IsWallPin && Grapple.WallRestraintWeight > 0f
                && _state == State.Grabbed)
            {
                SolveOverheadArms();
            }

            if (pose.HandOffBrace > 0f && Grapple.Captor != null
                && Grapple.Captor.TryGetComponent<EnemyFigure>(out var receiverFigure)
                && receiverFigure.ChestBone != null)
            {
                Transform chest = receiverFigure.ChestBone;
                Transform shoulderL = receiverFigure.ShoulderBone(true);
                Transform shoulderR = receiverFigure.ShoulderBone(false);
                if (shoulderL != null && shoulderR != null)
                {
                    Vector3 braceCenter = (shoulderL.position + shoulderR.position) * 0.5f - chest.up * 0.16f;
                    BracePalm(true, chest, braceCenter, pose.HandOffBrace);
                    BracePalm(false, chest, braceCenter, pose.HandOffBrace);
                }
            }

            DebugArmLUpper = ArmDirection(true, pose.ArmLUpper).normalized;
            DebugArmLFore = ArmDirection(true, pose.ArmLFore).normalized;
            DebugArmRUpper = ArmDirection(false, pose.ArmRUpper).normalized;
            DebugArmRFore = ArmDirection(false, pose.ArmRFore).normalized;
            DebugTorsoPitch = pose.TorsoPitch;
            DebugHeadPitch = pose.HeadPitch;
            DebugTorsoYaw = pose.TorsoYaw;
            DebugHeadYaw = pose.HeadYaw;

            // 指を握る。開いたままだと攻撃が「手を差し出す」に見える。
            // 曲がる向きは左右で鏡像なので、リグ側が左右別々の軸を使う。
            Rig.CurlFingers(true, pose.Grip);
            Rig.CurlFingers(false, pose.Grip);

            // 足を動かす必要が無いときは IK を掛けない（無駄な計算と微妙な揺れを避ける）。
            float hipMove = Mathf.Abs(pose.HipsOffset.x) + Mathf.Abs(pose.HipsOffset.y) + Mathf.Abs(pose.HipsOffset.z);
            bool needLegSolve = hipMove > 0.02f
                || (pose.FootL - Rig.RestFootL).sqrMagnitude > 0.0004f
                || (pose.FootR - Rig.RestFootR).sqrMagnitude > 0.0004f;

            // うつ伏せは膝の向き（ポール）を下へ回す。立ち姿勢のまま「前＋上」で解くと、
            // 体を伏せたときに膝が逆向き（過伸展）に折れる（実測）。
            Vector3 poleNow = pole;
            if (_downForward && (_state == State.Down || _state == State.GetUp) && pose.LayAngle > 1f)
            {
                float t = Mathf.Clamp01(pose.LayAngle / 92f);
                Vector3 pronePole = transform.position - Vector3.up * 1.6f + transform.forward * 0.6f;
                poleNow = Vector3.Lerp(pole, pronePole, t);
            }

            if (needLegSolve)
            {
                // 足を狙った位置へ置く（接地）。このとき脚の形は IK が決めるので、
                // しゃがみ姿勢を上書きしない（上書きすると足が地面から外れる）。
                Rig.SolveLeg(true, transform.TransformPoint(pose.FootL), poleNow);
                Rig.SolveLeg(false, transform.TransformPoint(pose.FootR), poleNow);
            }
            else if (pose.LegTuck > 0.001f)
            {
                // 倒れるときは脚を伸ばす（曲がったまま倒れると体育座りになる）
                Rig.BlendLegsToCrouch(pose.LegTuck);
            }
            else if (pose.LayAngle < -1f)
            {
                Rig.BlendLegsToRest(Mathf.Clamp01(-pose.LayAngle / 88f) * 0.96f);
            }

            // 倒れるのは最後。立った姿勢を丸ごと倒すので、IK の結果も一緒に倒れる。
            if (Mathf.Abs(pose.LayAngle) > 0.01f)
                Rig.LayBody(pose.LayAngle, pose.LaySide);

            // うつ伏せ（前へ崩れる）だけは、体を回した後に脚を解き直す。
            // LayBody は脚の IK より後に来るので、解いた脚ごと回すと脚が宙を向く（実測）。
            // 回した後の骨格から足先へ向けて引き直せば、床に沿った脚になる。
            if (_downForward && (_state == State.Down || _state == State.GetUp)
                && needLegSolve && pose.LayAngle > 1f)
            {
                Rig.SolveLeg(true, transform.TransformPoint(pose.FootL), poleNow);
                Rig.SolveLeg(false, transform.TransformPoint(pose.FootR), poleNow);
            }

            // うつ伏せ系の腕は、体を回した後に**世界の向き**で置き直す。
            // 回す前に「上」で組むと、体が起きるにつれて腕が上を向き、バンザイで起き上がる（実測）。
            // 手は地面（-Y）へ、肘は外へ。起き上がりの後半で構えの腕へ戻す。
            if (_downForward && (_state == State.Down || _state == State.GetUp))
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
                Vector3 left = Vector3.Cross(Vector3.up, fwd);   // 彼女の左
                Vector3 up = Vector3.up;

                Vector3 upperL, foreL, upperR, foreR;
                float toStanceArms = 0f;

                if (_state == State.Down)
                {
                    // うつ伏せ: 左腕は頭の先へ伸ばし、右腕は肘を張って手を顔の横へ
                    upperL = (fwd * 0.92f + left * 0.22f - up * 0.10f).normalized;
                    foreL = (fwd * 0.96f + left * 0.10f - up * 0.04f).normalized;
                    upperR = (fwd * 0.52f - left * 0.62f - up * 0.20f).normalized;
                    foreR = (fwd * 0.58f + left * 0.52f - up * 0.26f).normalized;
                    float reach = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.22f, 0.86f, _stateTime / _downFallSeconds));
                    upperL = Vector3.Slerp((Rig.LLowerArm.position - Rig.LUpperArm.position).normalized, upperL, reach);
                    foreL = Vector3.Slerp((Rig.LHand.position - Rig.LLowerArm.position).normalized, foreL, reach);
                    upperR = Vector3.Slerp((Rig.RLowerArm.position - Rig.RUpperArm.position).normalized, upperR, reach);
                    foreR = Vector3.Slerp((Rig.RHand.position - Rig.RLowerArm.position).normalized, foreR, reach);
                }
                else
                {
                    // 起き上がり: 両手を頭の横について床を押す → 立ち上がりで構えへ
                    float gu = Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _stateDuration));
                    toStanceArms = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.92f, gu));

                    upperL = (fwd * 0.42f + left * 0.50f - up * 0.72f).normalized;
                    foreL = (fwd * 0.46f - up * 0.88f).normalized;
                    upperR = (fwd * 0.42f - left * 0.50f - up * 0.72f).normalized;
                    foreR = (fwd * 0.46f - up * 0.88f).normalized;
                }

                if (toStanceArms > 0f)
                {
                    upperL = Vector3.Slerp(upperL, ArmDirection(true, StanceArmLUpper).normalized, toStanceArms);
                    foreL = Vector3.Slerp(foreL, ArmDirection(true, StanceArmLFore).normalized, toStanceArms);
                    upperR = Vector3.Slerp(upperR, ArmDirection(false, StanceArmRUpper).normalized, toStanceArms);
                    foreR = Vector3.Slerp(foreR, ArmDirection(false, StanceArmRFore).normalized, toStanceArms);
                }

                Rig.SetArm(true, upperL, foreL, pose.HandRollL);
                Rig.SetArm(false, upperR, foreR, pose.HandRollR);
            }

            // The supine pose normally points toward the opposite end of the lane.
            // Mirror its facing around the feet without turning the gameplay root mid-roll.
            if (_groundRolled && (_state == State.Down || _state == State.GetUp))
            {
                float turn = _state == State.GetUp
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.98f, _stateTime / _stateDuration)) : 0f;
                Quaternion facing = Quaternion.AngleAxis(180f * (1f - turn), Vector3.up);
                Vector3 origin = transform.position;
                Rig.Hips.SetPositionAndRotation(origin + facing * (Rig.Hips.position - origin),
                    facing * Rig.Hips.rotation);
            }

            if (_state == State.Down && Stats != null && Stats.GroundMount.BlocksGetUp)
                ApplyGroundMountReaction(Stats.GroundMount);

            // 状態が切り替わった直後は、切り替え前の姿勢から寄せる（カクつき防止）
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + Time.deltaTime / Mathf.Max(0.01f, _blendSeconds));
                Rig.BlendFromSnapshot(Mathf.SmoothStep(0f, 1f, _blend));
            }

            // かかと上げは最後。つま先を床に残したまま足首を回す（IK の解き直しを含む）。
            if (pose.FootPitchL > 0.01f)
                Rig.RaiseHeel(true, pose.FootPitchL, pole);

            if (pose.FootPitchR > 0.01f)
                Rig.RaiseHeel(false, pose.FootPitchR, pole);
        }

    }
}
