using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引きずり起こし（<see cref="GroundHaulUp"/>）で、手首を引かれて持ち上がる姿勢。
    ///
    /// 自分で起き上がる動き（<see cref="PoseGetUp"/>）は使わない。床を押す手も、踏ん張る脚も無く、
    /// 掴まれた腕がピンと張って敵の手へ伸び、上体はその腕にぶら下がるように持ち上がる。
    /// 首は一拍遅れて後ろへ残り（引かれた瞬間）、立たされると前へ垂れる。脚は引きずられる。
    /// </summary>
    public sealed partial class CharacterAnimator
    {
        /// <summary>引きずり起こしで彼女の姿勢を作っている最中か。</summary>
        bool HauledNow => _state == State.Down && Stats != null && Stats.GroundMount != null
            && Stats.GroundMount.Haul != null && Stats.GroundMount.Haul.PosesHer;

        /// <summary>脇の下を抱えられて立ち上がる。起き上がりの曲線は PoseGetUp に任せる。</summary>
        PoseValues PoseHauled(GroundHaulUp haul)
        {
            float lift = Mathf.Clamp01(haul.Lift);
            float side = haul.GrabbedLeftSide ? -1f : 1f;

            // 寝返り・片膝・立ちの自然な曲線。ここに腕だけ「抱えられて開いた」形を重ねる。
            var pose = PoseGetUp(lift);

            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 14f, lift);
            pose.HeadRoll = -side * 6f * lift;
            return pose;
        }

        /// <summary>
        /// うつ伏せの引き起こし中の腕。掴まれている側の腕は敵の引く手（<see cref="GroundHaulUp.PullAnchor"/>）へ
        /// IK で引かれて伸び、反対側の腕は床の体勢から体の脇へ自然に下がる。
        /// </summary>
        void ApplyProneHauledArms(GroundHaulUp haul, Vector3 fwd, Vector3 left, Vector3 up,
            float handRollL, float handRollR)
        {
            float lift = Mathf.Clamp01(haul.Lift);
            float dropT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.80f, lift));

            Vector3 downUpperL = (fwd * 0.92f + left * 0.22f - up * 0.10f).normalized;
            Vector3 downForeL = (fwd * 0.96f + left * 0.10f - up * 0.04f).normalized;
            Vector3 downUpperR = (fwd * 0.52f - left * 0.62f - up * 0.20f).normalized;
            Vector3 downForeR = (fwd * 0.58f + left * 0.52f - up * 0.26f).normalized;

            Vector3 hangUpperL = (left * 0.18f - up * 0.96f + fwd * 0.10f).normalized;
            Vector3 hangForeL = (left * 0.06f - up * 0.94f + fwd * 0.28f).normalized;
            Vector3 hangUpperR = (-left * 0.18f - up * 0.96f + fwd * 0.10f).normalized;
            Vector3 hangForeR = (-left * 0.06f - up * 0.94f + fwd * 0.28f).normalized;

            Rig.SetArm(true,
                Vector3.Slerp(downUpperL, hangUpperL, dropT),
                Vector3.Slerp(downForeL, hangForeL, dropT),
                handRollL);
            Rig.SetArm(false,
                Vector3.Slerp(downUpperR, hangUpperR, dropT),
                Vector3.Slerp(downForeR, hangForeR, dropT),
                handRollR);

            float pull = Mathf.Clamp01(haul.ArmPull);
            if (pull <= 0.001f)
                return;

            bool grabbedLeft = haul.GrabbedLeftSide;
            Transform upper = grabbedLeft ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = grabbedLeft ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = grabbedLeft ? Rig.LHand : Rig.RHand;
            if (upper == null || elbow == null || hand == null)
                return;

            float armLen = Vector3.Distance(upper.position, elbow.position)
                + Vector3.Distance(elbow.position, hand.position);
            Vector3 anchor = haul.PullAnchor;
            Vector3 toAnchor = anchor - upper.position;
            float maxReach = armLen * 0.90f;
            Vector3 goal = toAnchor.magnitude > maxReach && toAnchor.sqrMagnitude > 1e-6f
                ? upper.position + toAnchor.normalized * maxReach
                : anchor;

            Vector3 outward = grabbedLeft ? left : -left;
            Vector3 pole = elbow.position + outward * 0.20f - up * (0.18f * (1f - lift)) - fwd * (0.18f * lift);
            SolveHand(upper, elbow, hand, goal, pole, pull);
        }

        void SolveHand(Transform upper, Transform elbow, Transform hand,
            Vector3 goal, Vector3 pole, float weight)
        {
            if (upper == null || elbow == null || hand == null)
                return;

            Quaternion upperPose = upper.rotation;
            Quaternion elbowPose = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, hand, goal, pole, transform.right);
            upper.rotation = Quaternion.Slerp(upperPose, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(elbowPose, elbow.rotation, weight);
        }

        static PoseValues LerpPose(PoseValues a, PoseValues b, float t)
        {
            if (t <= 0f)
                return a;
            if (t >= 1f)
                return b;
            return new PoseValues
            {
                FootL = Vector3.Lerp(a.FootL, b.FootL, t),
                FootR = Vector3.Lerp(a.FootR, b.FootR, t),
                HipsOffset = Vector3.Lerp(a.HipsOffset, b.HipsOffset, t),
                ArmLUpper = Vector3.Lerp(a.ArmLUpper, b.ArmLUpper, t),
                ArmLFore = Vector3.Lerp(a.ArmLFore, b.ArmLFore, t),
                ArmRUpper = Vector3.Lerp(a.ArmRUpper, b.ArmRUpper, t),
                ArmRFore = Vector3.Lerp(a.ArmRFore, b.ArmRFore, t),
                HandOffBrace = Mathf.Lerp(a.HandOffBrace, b.HandOffBrace, t),
                TorsoPitch = Mathf.Lerp(a.TorsoPitch, b.TorsoPitch, t),
                TorsoRoll = Mathf.Lerp(a.TorsoRoll, b.TorsoRoll, t),
                TorsoYaw = Mathf.Lerp(a.TorsoYaw, b.TorsoYaw, t),
                HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t),
                HeadYaw = Mathf.Lerp(a.HeadYaw, b.HeadYaw, t),
                HeadRoll = Mathf.Lerp(a.HeadRoll, b.HeadRoll, t),
                LayAngle = Mathf.Lerp(a.LayAngle, b.LayAngle, t),
                LaySide = Mathf.Lerp(a.LaySide, b.LaySide, t),
                LegTuck = Mathf.Lerp(a.LegTuck, b.LegTuck, t),
                FootPitchL = Mathf.Lerp(a.FootPitchL, b.FootPitchL, t),
                FootPitchR = Mathf.Lerp(a.FootPitchR, b.FootPitchR, t),
                Grip = Mathf.Lerp(a.Grip, b.Grip, t),
                HandRollL = Mathf.Lerp(a.HandRollL, b.HandRollL, t),
                HandRollR = Mathf.Lerp(a.HandRollR, b.HandRollR, t),
            };
        }
        /// <summary>相手の腕を掴み返さず腕を下ろしている秒数。</summary>
        float _haulDazeUntil;

        internal void BeginHaulDaze(float seconds) => _haulDazeUntil = Time.time + Mathf.Max(0.1f, seconds);

        void ApplyHaulDaze(ref PoseValues pose)
        {
            Vector3 hangL = new Vector3(0.18f, -0.96f, 0.04f);
            Vector3 hangR = new Vector3(-0.18f, -0.96f, 0.04f);
            pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, hangL, 0.85f);
            pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, new Vector3(0.06f, -0.95f, 0.15f), 0.85f);
            pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, hangR, 0.85f);
            pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, new Vector3(-0.06f, -0.95f, 0.15f), 0.85f);
            pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 12f, 0.6f);
            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 14f, 0.6f);
        }
    }
}
