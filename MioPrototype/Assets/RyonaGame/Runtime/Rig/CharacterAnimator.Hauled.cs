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

        PoseValues PoseHauled(GroundHaulUp haul)
        {
            // うつ伏せは仰向けの「横に転がる」曲線を使うと体がひねれて崩れる。
            // 自力でうつ伏せから起きる PoseGetUp の脚・腰を借り、腕と首だけ髪を掴まれた形にする。
            if (_downForward)
                return PoseHauledProne(haul);

            // 寝た姿勢（と、その終わりの値）は毎フレーム作る。持ち上がる前はこれがそのまま出る。
            var down = PoseDown();
            float lift = haul.Lift;
            float yank = haul.Yank;

            // 掴まれた手の側（彼女のローカル x）。左手なら -1。
            float side = haul.GrabbedLeftSide ? -1f : 1f;

            float sit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.00f, 0.55f, lift));
            float stand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1.00f, lift));

            var pose = PoseDefaults();

            // 体の角度: 仰向け（-92）→ 上体が引き起こされて座る（-38）→ 立つ（0）。
            pose.LayAngle = -92f + 54f * sit + 38f * stand;
            pose.LaySide = -30f * (1f - sit);

            // 腰は床から離れ、足元へ寄せられる。立たされても膝が抜けて低い。
            pose.HipsOffset.y = Mathf.Lerp(_downEndHipsY, StanceHipsY - 0.09f, stand) + 0.05f * sit * (1f - stand);
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.95f, lift));
            pose.HipsOffset.z = Mathf.Lerp(_downEndHipsZ, StanceHipsZ - 0.03f, slide);
            // 引かれる側へ腰が寄る。
            pose.HipsOffset.x = 0.06f * side * stand;

            // 上体: 引かれた肩が先に上がり（引かれる側へ傾く）、立つと前へ折れて垂れる。
            float drag = sit * (1f - stand);
            pose.TorsoPitch = 10f * drag + 8f * stand + 12f * yank;
            pose.TorsoRoll = -side * (16f * drag + 8f * stand);
            pose.TorsoYaw = side * 10f * drag;

            // 首は力なく垂れる。脇を抱えられているだけなので、頭を引かれる形にはしない。
            pose.HeadPitch = -8f * drag + 14f * stand;
            pose.HeadYaw = -side * 6f;
            pose.HeadRoll = -side * 8f * stand;

            // 腕は脱力して垂れる。抱え上げられるので、自分では掴まらない。
            Vector3 hangUpper = new Vector3(0.46f, -0.62f, 0.12f);
            Vector3 hangFore = new Vector3(0.22f, -0.72f, 0.28f);
            pose.ArmLUpper = Vector3.Lerp(DownArmLUpper, hangUpper, sit);
            pose.ArmLFore = Vector3.Lerp(DownArmLFore, hangFore, sit);
            pose.ArmRUpper = Vector3.Lerp(DownArmRUpper, Mirror(hangUpper), sit);
            pose.ArmRFore = Vector3.Lerp(DownArmRFore, Mirror(hangFore), sit);
            pose.Grip = 0.12f;

            // 抱え上げられるので脚は踏ん張らない。膝が抜けて足が垂れる。
            pose.LegTuck = 0.35f * stand;

            // 脚: 自分では踏ん張らない。引き上げの間は膝が曲がって足が床を擦り、
            // 立たされても膝が抜けて、つま先が床に残る。
            float tuck = Mathf.Sin(Mathf.Clamp01(lift / 0.80f) * Mathf.PI);
            pose.FootL += new Vector3(0.02f, 0f, 0.20f * tuck - 0.04f * stand);
            pose.FootR += new Vector3(-0.02f, 0f, 0.12f * tuck + 0.06f * stand);
            pose.FootPitchL = 14f * stand;
            pose.FootPitchR = 8f * stand;

            // 引かれた瞬間に体が浮く（ぐいっ）。上体も一拍遅れてついてくる。
            pose.HipsOffset.y += 0.08f * yank;
            pose.TorsoPitch += 9f * yank;
            pose.HipsOffset.y = Mathf.Max(pose.HipsOffset.y, 0.11f - Rig.RestHips.y);

            // 持ち上がり始めるまでは寝た姿勢のまま。
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, lift));
            return LerpPose(down, pose, enter);
        }

        /// <summary>うつ伏せからの引き起こし。脚と腰は PoseGetUp に任せ、腕・首だけ髪を掴まれた形にする。</summary>
        PoseValues PoseHauledProne(GroundHaulUp haul)
        {
            float lift = haul.Lift;
            float yank = haul.Yank;
            float side = haul.GrabbedLeftSide ? -1f : 1f;
            var pose = PoseGetUp(lift);

            Vector3 hangUpper = new Vector3(0.20f, -0.95f, 0.05f);
            Vector3 hangFore = new Vector3(0.06f, -0.95f, 0.15f);
            pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, hangUpper, lift);
            pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, hangFore, lift);
            pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, Mirror(hangUpper), lift);
            pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, Mirror(hangFore), lift);

            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 12f, lift);
            pose.HeadRoll = -side * 8f * lift;
            pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 8f, lift);

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
