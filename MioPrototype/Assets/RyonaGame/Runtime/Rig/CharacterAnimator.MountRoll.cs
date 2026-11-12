using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        void ApplyGroundMountReaction(GroundMountAttack mount)
        {
            if (mount.Stage == GroundMountAttack.Beat.Roll)
            {
                ApplyProneMountRoll(mount);
                return;
            }

            float effort = mount.StrugglePulse;
            float headRecoil = mount.HeadRecoil;
            float torsoRecoil = mount.TorsoRecoil;
            float legRecoil = mount.LegRecoil;
            float guard = mount.Fear;
            if (guard <= 0f)
                return;

            float windup = mount.Cock;
            Vector3 bodyAxis = Vector3.ProjectOnPlane(Rig.Head.position - Rig.Hips.position, Vector3.up);
            if (bodyAxis.sqrMagnitude <= 0.001f)
                return;

            bodyAxis.Normalize();
            Vector3 crossBody = Vector3.Cross(Vector3.up, bodyAxis);
            Vector3 leftShoulder = Rig.LUpperArm.position - Rig.RUpperArm.position;
            Vector3 left = crossBody * Mathf.Sign(Vector3.Dot(crossBody, leftShoulder));
            float incomingSide = Mathf.Sign(Vector3.Dot(crossBody, mount.StrikeSide));
            float impactSide = Mathf.Sign(Vector3.Dot(crossBody, mount.ImpactSide));
            bool incomingLeft = Vector3.Dot(left, mount.StrikeSide) > 0f;
            bool impactLeft = Vector3.Dot(left, mount.ImpactSide) > 0f;
            Vector3 guardOffset = Rig.Head.position - Rig.RUpperArm.position;

            // Follow the side of the fist at contact, even as the other hand begins winding up.
            Rig.Hips.position += (bodyAxis * 0.022f - crossBody * (0.027f * impactSide)) * torsoRecoil * guard;
            Rig.Chest.rotation = Quaternion.AngleAxis((incomingSide * 5f * windup + impactSide * 22f * torsoRecoil)
                * guard, bodyAxis) * Quaternion.AngleAxis(9f * torsoRecoil, crossBody) * Rig.Chest.rotation;
            float defaultHeadRecoil = mount.ImpactWasLeftPunch ? 0f : headRecoil;
            if (Rig.Neck != null)
                Rig.Neck.rotation = Quaternion.AngleAxis(impactSide * 11f * defaultHeadRecoil * guard, bodyAxis)
                    * Rig.Neck.rotation;

            float cheekTurn = -25f * Vector3.Dot(crossBody, Vector3.back);
            Rig.Head.rotation = Quaternion.AngleAxis((cheekTurn + incomingSide * 7f * windup
                + impactSide * 41f * defaultHeadRecoil) * guard, bodyAxis) * Rig.Head.rotation;

            float centering = mount.LeftHookCentering * guard;
            if (centering > 0.001f)
            {
                Vector3 facing = Vector3.ProjectOnPlane(Rig.Head.forward, bodyAxis);
                float neutralTurn = Vector3.SignedAngle(facing, Vector3.up, bodyAxis);
                Rig.Head.rotation = Quaternion.AngleAxis(neutralTurn * 0.60f * centering, bodyAxis) * Rig.Head.rotation;
            }
            if (mount.ImpactWasLeftPunch && headRecoil > 0.001f)
                TurnHeadToLeftShoulder(bodyAxis, crossBody, left, headRecoil * guard);

            float lookWeight = mount.HasTakenPunch ? 0f : guard;
            if (lookWeight > 0.001f)
            {
                Vector3 toAttacker = mount.AttackerHead - Rig.Head.position;
                Quaternion look = Quaternion.FromToRotation(Rig.Head.forward, toAttacker);
                Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, look, 42f);
                Rig.Head.rotation = Quaternion.Slerp(Quaternion.identity, turn, lookWeight) * Rig.Head.rotation;
            }

            if (legRecoil > 0.001f)
            {
                Vector3 pole = Rig.Hips.position + Vector3.up * 0.65f;
                Vector3 curl = bodyAxis * 0.10f + Vector3.up * 0.026f;
                BendMountKnee(true, curl, pole, (impactLeft ? 0.43f : 0.20f) * legRecoil);
                BendMountKnee(false, curl, pole, (impactLeft ? 0.20f : 0.43f) * legRecoil);
            }

            float flinch = mount.ArmRecoil;
            float brace = windup + 0.5f * effort;
            float give = mount.Stage == GroundMountAttack.Beat.Strike ? 0.28f * mount.Drive : 0f;
            bool rightNearCamera = Vector3.Dot(-left, mount.ShotOffset) > 0f;
            float leftBrace = brace * (incomingLeft ? 1f : 0.35f);
            float leftRecoil = Mathf.Max(incomingLeft ? give : 0f, impactLeft ? flinch : 0f);
            float rightBrace = brace * (incomingLeft ? 0.35f : 1f);
            float rightRecoil = Mathf.Max(incomingLeft ? 0f : give, impactLeft ? 0f : flinch);

            Quaternion rightShoulder = Rig.RUpperArm.localRotation;
            Quaternion rightElbow = Rig.RLowerArm.localRotation;
            Quaternion rightHand = Rig.RHand.localRotation;
            // Pose the right arm with the left side's reaction, then reflect that solution.
            PoseMountRightArm(-left, bodyAxis, guardOffset, guard, leftBrace, leftRecoil, rightNearCamera);
            Vector3 leftUpper = Vector3.Reflect(Rig.RLowerArm.position - Rig.RUpperArm.position, left);
            Vector3 leftForearm = Vector3.Reflect(Rig.RHand.position - Rig.RLowerArm.position, left);
            Rig.SetArm(true,
                Vector3.Slerp(Rig.LLowerArm.position - Rig.LUpperArm.position, leftUpper, guard),
                Vector3.Slerp(Rig.LHand.position - Rig.LLowerArm.position, leftForearm, guard));
            Rig.ReflectRightWristToLeft(left, guard);

            Rig.RUpperArm.localRotation = rightShoulder;
            Rig.RLowerArm.localRotation = rightElbow;
            Rig.RHand.localRotation = rightHand;
            PoseMountRightArm(-left, bodyAxis, guardOffset, guard, rightBrace, rightRecoil, rightNearCamera);

            Rig.CurlFingers(true, Mathf.Lerp(0.18f, 0.68f - 0.30f * leftRecoil, guard));
            Rig.CurlFingers(false, Mathf.Lerp(0.18f, 0.68f - 0.30f * rightRecoil, guard));
        }

        void TurnHeadToLeftShoulder(Vector3 bodyAxis, Vector3 crossBody, Vector3 left, float weight)
        {
            Vector3 facing = Vector3.ProjectOnPlane(Rig.Head.forward, bodyAxis);
            Vector3 towardLeft = Vector3.up * 0.87f + left * 0.50f;
            float turn = Vector3.SignedAngle(facing, towardLeft, bodyAxis) * weight;
            if (Rig.Neck != null)
            {
                Rig.Neck.rotation = Quaternion.AngleAxis(turn * 0.27f, bodyAxis) * Rig.Neck.rotation;
                turn *= 0.73f;
            }
            Rig.Head.rotation = Quaternion.AngleAxis(turn, bodyAxis)
                * Quaternion.AngleAxis(-6f * weight, crossBody) * Rig.Head.rotation;
        }

        void ApplyProneMountRoll(GroundMountAttack mount)
        {
            Vector3 axis = Vector3.ProjectOnPlane(Rig.Head.position - Rig.Hips.position, Vector3.up);
            if (axis.sqrMagnitude <= 0.001f)
                return;

            axis.Normalize();
            Vector3 across = Vector3.Cross(Vector3.up, axis);
            float chestTurn = mount.ShoulderRoll;
            float pelvisTurn = mount.RollProgress;
            float twist = chestTurn - pelvisTurn;
            float signedRoll = mount.Side * 180f;
            Quaternion spine = Rig.Spine != null ? Rig.Spine.rotation : Quaternion.identity;
            Quaternion chest = Rig.Chest.rotation;
            Quaternion leftThigh = Rig.LUpperLeg.rotation;
            Quaternion rightThigh = Rig.RUpperLeg.rotation;
            Quaternion leftShin = Rig.LLowerLeg.rotation;
            Quaternion rightShin = Rig.RLowerLeg.rotation;
            float leftFootHeight = Rig.LFoot.position.y;
            float rightFootHeight = Rig.RFoot.position.y;
            bool pulledLeft = mount.RollLeftShoulder;

            // The grabbed shoulder leads. Keep the pelvis and legs in their own phases
            // so they do not inherit the shoulder's turn as a single rigid pose.
            Rig.Hips.position += across * (0.09f * mount.Side * twist);
            Rig.Hips.rotation = Quaternion.AngleAxis(signedRoll * pelvisTurn, axis) * Rig.Hips.rotation;
            if (Rig.Spine != null)
                Rig.Spine.rotation = Quaternion.AngleAxis(signedRoll * Mathf.Lerp(pelvisTurn, chestTurn, 0.50f), axis)
                    * Quaternion.AngleAxis(-14f * twist, across) * spine;
            Rig.Chest.rotation = Quaternion.AngleAxis(signedRoll * chestTurn, axis)
                * Quaternion.AngleAxis(-24f * twist, across) * chest;

            float leadingLeg = mount.RollPhase(0.46f, 0.96f);
            float trailingLeg = mount.RollPhase(0.54f, 0.99f);
            float leftTurn = pulledLeft ? leadingLeg : trailingLeg;
            float rightTurn = pulledLeft ? trailingLeg : leadingLeg;
            // The leading leg sweeps out from the hip. Carry the shin along without curling the knee.
            Quaternion sweep = Quaternion.AngleAxis(-mount.Side * 16f * Mathf.Sin(Mathf.PI * leadingLeg), Vector3.up);
            Quaternion trail = Quaternion.AngleAxis(mount.Side * 5f * Mathf.Sin(Mathf.PI * trailingLeg), Vector3.up);
            Quaternion leftRoll = (pulledLeft ? sweep : trail)
                * Quaternion.AngleAxis(signedRoll * leftTurn, axis);
            Quaternion rightRoll = (pulledLeft ? trail : sweep)
                * Quaternion.AngleAxis(signedRoll * rightTurn, axis);
            Rig.LUpperLeg.rotation = leftRoll * leftThigh;
            Rig.RUpperLeg.rotation = rightRoll * rightThigh;
            Rig.LLowerLeg.rotation = leftRoll * leftShin;
            Rig.RLowerLeg.rotation = rightRoll * rightShin;

            float lowest = Mathf.Min(Rig.Head.position.y - 0.085f, Rig.Chest.position.y - 0.09f,
                Rig.LFoot.position.y, Rig.RFoot.position.y);
            Rig.Hips.position += Vector3.up * Mathf.Max(0f, Stats.transform.position.y - lowest);

            // The trailing foot drags against the floor while the near knee rolls over.
            float trailingPlant = mount.RollPhase(0.38f, 0.60f) * (1f - mount.RollPhase(0.80f, 0.98f));
            float leadingPlant = mount.RollPhase(0.56f, 0.74f) * (1f - mount.RollPhase(0.83f, 0.98f));
            KeepRollFootLow(true, leftFootHeight, pulledLeft ? 0.42f * leadingPlant : 0.85f * trailingPlant);
            KeepRollFootLow(false, rightFootHeight, pulledLeft ? 0.85f * trailingPlant : 0.42f * leadingPlant);

            // --- 腕 ------------------------------------------------------
            // ロール中は「体の外側」と「床」を基準に手を置く。
            // ワールド座標へ手を固定すると、体が回るにつれて肩がその点へ近づき、
            // 肘が畳み切られる（実測: 肘 19°・手が肩の 17cm まで寄り、腕が一瞬
            // 折り畳まれて見える）。手先の距離を固定して解けば、その破綻は起きない。
            //
            // 入りは「いま体に付いてきた腕の位置」から始める。伏せダウン中のマウントは
            // ガードを出さない（`GroundMountAttack.Fear` はうつ伏せでは 0）ので、
            // 守りの手を入りの基準にすると 1 フレームで 40cm 飛ぶ。
            Vector3 carriedLeftHand = Rig.LHand.position;
            Vector3 carriedRightHand = Rig.RHand.position;
            Vector3 carriedLeftElbow = Rig.LLowerArm.position;
            Vector3 carriedRightElbow = Rig.RLowerArm.position;

            // 体の左右は「右肩 → 左肩」の向きをそのまま使う。ワールドの `across` と肩の位置で
            // 符号を決めると、体が 90° 回ったところで符号が反転し、手が体の反対側へ飛ぶ
            // （実測: 0.5 秒の時点で片手が 70cm 飛んだ）。肩の線はロールと一緒に回るので途切れない。
            Vector3 shoulderLine = Rig.LUpperArm.position - Rig.RUpperArm.position;
            Vector3 left = shoulderLine.sqrMagnitude > 1e-6f ? shoulderLine.normalized : Rig.Chest.right;
            float groundY = Stats.transform.position.y;

            // ロール明けは次の Windup の「守り」へ戻す（そちらはガードが出る）。
            // 戻さないと、ロール最後の 1 フレームからガードへ腕が飛ぶ。
            bool rightNearCamera = Vector3.Dot(-left, mount.ShotOffset) > 0f;
            float nearHand = rightNearCamera ? 1f : 0f;
            Vector3 guardRight = Rig.Head.position
                - left * (0.14f + 0.05f * nearHand)
                - axis * (0.085f + 0.05f * nearHand)
                + Vector3.up * (0.06f - 0.02f * nearHand);
            Vector3 guardRightPole = Rig.RUpperArm.position - left * 0.34f
                - axis * 0.24f + Vector3.up * 0.02f;
            Vector3 shoulderMid = (Rig.LUpperArm.position + Rig.RUpperArm.position) * 0.5f;
            Vector3 guardLeft = MirrorPoint(guardRight, shoulderMid, left);
            Vector3 guardLeftPole = MirrorPoint(guardRightPole, shoulderMid, left);

            // 掴まれた肩の腕は体側へ抱え、反対の腕は床へ逃がす。
            Vector3 pulledOut = pulledLeft ? left : -left;
            Vector3 freeOut = pulledLeft ? -left : left;
            Transform pulledShoulder = pulledLeft ? Rig.LUpperArm : Rig.RUpperArm;
            Transform freeShoulder = pulledLeft ? Rig.RUpperArm : Rig.LUpperArm;

            // 掴まれた肩の腕も、反対の腕も、ロール中は床の側へ垂らす。
            // 手先は「肩から固定距離」で置き、床へ潜る分だけ向きを上へ回す（距離は保つ）。
            // 向きには必ず「頭（+axis）」成分を入れる。体が真横を向くと体の外向きが
            // 真上・真下になり、床へ向ける水平の方位が定まらず手が大きく振れる
            // （実測: 0.5 秒で片手が 67cm 飛ぶ）。頭側成分があれば方位は常に決まる。
            Vector3 pulledHand = ArmGoalAboveFloor(pulledShoulder.position,
                Vector3.down * 0.55f + pulledOut * 0.25f + axis * 0.30f, 0.28f, groundY + 0.05f);
            Vector3 pulledPole = pulledShoulder.position + pulledOut * 0.25f
                + axis * 0.22f + Vector3.down * 0.08f;

            Vector3 freeHand = ArmGoalAboveFloor(freeShoulder.position,
                Vector3.down * 0.50f + freeOut * 0.30f + axis * 0.30f, 0.40f, groundY + 0.05f);
            Vector3 freePole = freeShoulder.position + freeOut * 0.25f
                + axis * 0.22f + Vector3.down * 0.08f;

            Vector3 leftRollHand = pulledLeft ? pulledHand : freeHand;
            Vector3 rightRollHand = pulledLeft ? freeHand : pulledHand;
            Vector3 leftRollPole = pulledLeft ? pulledPole : freePole;
            Vector3 rightRollPole = pulledLeft ? freePole : pulledPole;

            // 入りは「体に付いてきた手」から、終わりは次の Windup のガード位置へ。
            // 位置をそのまま Lerp すると肩の近くを横切って肘が畳まれる（実測: 肘 28°）ので、
            // 肩を中心に「向き」と「距離」を別々に補間する。
            float enter = mount.RollPhase(0.04f, 0.34f);
            float exit = mount.RollPhase(0.86f, 0.995f);

            BlendRollArm(Rig.LUpperArm, carriedLeftHand, carriedLeftElbow,
                leftRollHand, leftRollPole, guardLeft, guardLeftPole, enter, exit,
                out Vector3 leftGoal, out Vector3 leftPole);
            BlendRollArm(Rig.RUpperArm, carriedRightHand, carriedRightElbow,
                rightRollHand, rightRollPole, guardRight, guardRightPole, enter, exit,
                out Vector3 rightGoal, out Vector3 rightPole);

            AimRollHand(true, leftGoal, leftPole);
            AimRollHand(false, rightGoal, rightPole);
        }

        /// <summary>
        /// 腕の目標を「肩からの向き」と「距離」に分けて補間する。
        /// 手先の座標をそのまま Lerp すると、肩の近くを通る軌道で肘が畳み切られる。
        /// </summary>
        static void BlendRollArm(Transform shoulder, Vector3 carriedHand, Vector3 carriedElbow,
            Vector3 rollHand, Vector3 rollPole, Vector3 guardHand, Vector3 guardPole,
            float enter, float exit, out Vector3 goal, out Vector3 pole)
        {
            Vector3 origin = shoulder.position;
            Vector3 baseOffset = carriedHand - origin;
            Vector3 rollOffset = rollHand - origin;
            Vector3 guardOffset = guardHand - origin;

            float baseDistance = baseOffset.magnitude;
            float rollDistance = rollOffset.magnitude;
            float guardDistance = guardOffset.magnitude;

            Vector3 direction = rollDistance > 1e-4f ? rollOffset / rollDistance : Vector3.down;
            if (baseDistance > 1e-4f)
                direction = Vector3.Slerp(baseOffset / baseDistance, direction, enter);
            float distance = Mathf.Lerp(baseDistance, rollDistance, enter);

            if (guardDistance > 1e-4f)
                direction = Vector3.Slerp(direction, guardOffset / guardDistance, exit);
            distance = Mathf.Lerp(distance, guardDistance, exit);

            goal = origin + direction * distance;
            pole = Vector3.Lerp(Vector3.Lerp(carriedElbow, rollPole, enter), guardPole, exit);
        }

        /// <summary>点を、指定した平面（点と法線）で鏡映する。</summary>
        static Vector3 MirrorPoint(Vector3 point, Vector3 planePoint, Vector3 normal)
        {
            return point - 2f * Vector3.Dot(point - planePoint, normal) * normal;
        }

        /// <summary>
        /// 肩から <paramref name="reach"/> の距離を保ったまま、手先が床より下へ行かない
        /// 目標位置を作る。y をクランプすると肩の真下へ手が寄り、肘が畳み切られる
        /// （実測: 肘 14°）。向きだけを上へ回せば距離が変わらない。
        /// </summary>
        static Vector3 ArmGoalAboveFloor(Vector3 shoulder, Vector3 direction, float reach, float floorY)
        {
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.down;
            float minY = Mathf.Clamp((floorY - shoulder.y) / Mathf.Max(0.01f, reach), -1f, 1f);
            if (direction.y < minY)
            {
                float flatLength = Mathf.Sqrt(Mathf.Max(0f, 1f - minY * minY));
                Vector3 flat = new Vector3(direction.x, 0f, direction.z);
                direction = flat.sqrMagnitude > 1e-6f
                    ? flat.normalized * flatLength + Vector3.up * minY
                    : Vector3.up;
            }

            return shoulder + direction * reach;
        }

        /// <summary>
        /// ロール中の腕。手先の目標と肘の向きを直接指定して 2 ボーン IK で解く。
        /// 極（肘の曲がる面）は体の向きから作った点を渡す。ワールドの上を極にすると、
        /// 体が横を向くロール中は肘が床を抜ける向きに解けて腕が反転しうる。
        /// </summary>
        void AimRollHand(bool leftSide, Vector3 goal, Vector3 pole)
        {
            Transform shoulder = leftSide ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = leftSide ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = leftSide ? Rig.LHand : Rig.RHand;
            ProceduralRig.SolveTwoBone(shoulder, elbow, hand, goal, pole);
            Rig.SetArm(leftSide, elbow.position - shoulder.position,
                hand.position - elbow.position, 0f);
        }

        void KeepRollFootLow(bool left, float floorHeight, float weight)
        {
            Transform foot = left ? Rig.LFoot : Rig.RFoot;
            Vector3 target = foot.position;
            float maxHeight = floorHeight + 0.025f;
            if (weight <= 0.001f || target.y <= maxHeight)
                return;

            Transform knee = left ? Rig.LLowerLeg : Rig.RLowerLeg;
            Quaternion footRotation = foot.rotation;
            target.y = Mathf.Lerp(target.y, maxHeight, weight);
            Rig.SolveLeg(left, target, knee.position);
            foot.rotation = footRotation;
        }

        void BendMountKnee(bool left, Vector3 curl, Vector3 pole, float weight)
        {
            Transform thigh = left ? Rig.LUpperLeg : Rig.RUpperLeg;
            Transform shin = left ? Rig.LLowerLeg : Rig.RLowerLeg;
            Transform foot = left ? Rig.LFoot : Rig.RFoot;
            Quaternion thighBefore = thigh.localRotation;
            Quaternion shinBefore = shin.localRotation;
            Quaternion footBefore = foot.rotation;
            Rig.SolveLeg(left, foot.position + curl, pole);
            thigh.localRotation = Quaternion.Slerp(thighBefore, thigh.localRotation, weight);
            shin.localRotation = Quaternion.Slerp(shinBefore, shin.localRotation, weight);
            foot.rotation = footBefore;
        }

        void PoseMountRightArm(Vector3 outside, Vector3 towardHead, Vector3 guardOffset, float weight,
            float brace, float recoil, bool nearCamera)
        {
            float nearHand = nearCamera ? 1f : 0f;
            // 手は頭の上ではなく、耳の横（顔の高さ）へ。頭中心 +15cm だと腕が頭上へ伸び、
            // マウントされたときに「万歳」して見える。
            Vector3 goal = Rig.RUpperArm.position + guardOffset + outside * (0.14f + 0.05f * nearHand)
                + Vector3.up * (0.06f - 0.02f * nearHand + 0.045f * brace)
                - towardHead * (0.085f + 0.05f * nearHand - 0.075f * brace);
            AimMountHand(false, outside, towardHead, goal, weight);

            // Keep the guard's elbow bend through the impact. Moving the IK target across
            // the elbow guide can put the forearm on the other side of the bend in one frame.
            float give = recoil * weight;
            if (give <= 0.001f)
                return;

            Transform shoulder = Rig.RUpperArm;
            Transform elbow = Rig.RLowerArm;
            Transform hand = Rig.RHand;
            Vector3 reach = hand.position - shoulder.position;
            Vector3 fall = (outside * 0.12f - towardHead * 0.15f - Vector3.up * 0.10f) * give;
            shoulder.rotation = Quaternion.FromToRotation(reach, reach + fall) * shoulder.rotation;

            Vector3 upper = elbow.position - shoulder.position;
            Vector3 lower = hand.position - elbow.position;
            Vector3 hinge = Vector3.Cross(upper, lower);
            if (hinge.sqrMagnitude > 0.000001f)
            {
                float open = Mathf.Clamp(Vector3.Angle(upper, lower) - 12f, 0f, 14f * give);
                elbow.rotation = Quaternion.AngleAxis(-open, hinge) * elbow.rotation;
            }
        }

        void AimMountHand(bool left, Vector3 outside, Vector3 towardHead, Vector3 goal, float weight)
        {
            Transform shoulder = left ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = left ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = left ? Rig.LHand : Rig.RHand;
            // 彼女は床に寝ている。極を床の下（-up）に置くと、肘が床を突き抜ける向きに解けて
            // 腕が反転する（内側へ手を寄せる左腕で特に出やすい）。肘は横へ出し、床すれすれに置く。
            Vector3 pole = shoulder.position + outside * 0.34f - towardHead * 0.24f + Vector3.up * 0.02f;
            ProceduralRig.SolveTwoBone(shoulder, elbow, hand, Vector3.Lerp(hand.position, goal, weight), pole);
            Rig.SetArm(left, elbow.position - shoulder.position, hand.position - elbow.position, 0f);
        }

    }
}
