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

            Vector3 openUpper = new Vector3(0.46f, -0.62f, 0.12f);
            Vector3 openFore = new Vector3(0.22f, -0.72f, 0.28f);
            pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, openUpper, lift);
            pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, openFore, lift);
            pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, Mirror(openUpper), lift);
            pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, Mirror(openFore), lift);

            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 14f, lift);
            pose.HeadRoll = -side * 6f * lift;
            return pose;
        }

        /// <summary>髪を掴まれている敵の手首へ、両手で縋る。</summary>
        void ApplyHauledArm(GroundHaulUp haul)
        {
            float weight = haul.ArmPull;
            if (weight <= 0.001f)
                return;

            // うつ伏せは手をついて起き上がる。両手を髪へ上げると体が泳いで見える。
            if (Stats != null && Stats.DownIsForward)
                return;

            Transform head = Rig.Head;
            if (head == null)
                return;

            Vector3 hair = head.position + Vector3.up * 0.10f;
            Vector3 right = transform.right;
            SolveHand(Rig.LUpperArm, Rig.LLowerArm, Rig.LHand, hair + right * 0.06f, -right, weight);
            SolveHand(Rig.RUpperArm, Rig.RLowerArm, Rig.RHand, hair - right * 0.06f, right, weight);
        }

        void SolveHand(Transform upper, Transform elbow, Transform hand,
            Vector3 goal, Vector3 outward, float weight)
        {
            if (upper == null || elbow == null || hand == null)
                return;

            Vector3 pole = elbow.position + outward * 0.25f - Vector3.up * 0.10f;
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
