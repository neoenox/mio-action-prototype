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

            // 寝返り・片膝・立ちの自然な曲線。腕は自分で床を押さず、脱力して垂らす。
            var pose = PoseGetUp(lift);

            // 背後から両脇へ腕を差し込まれて持ち上がるので、横へ転がらず真っ直ぐ起こされる
            // （自分で起きる曲線の「横に転がって座る」は使わない）。
            pose.LaySide = 0f;
            pose.TorsoRoll = 0f;

            // 腕は脇の下に入った敵の前腕に押し上げられ、二の腕が横へ開いて肘から先が垂れる。
            float limp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.35f, lift));
            Vector3 hangLUpper = new Vector3(0.66f, -0.70f, 0.12f);
            Vector3 hangLFore = new Vector3(0.22f, -0.95f, 0.16f);
            pose.ArmLUpper = Vector3.Lerp(DownArmLUpper, hangLUpper, limp);
            pose.ArmLFore = Vector3.Lerp(DownArmLFore, hangLFore, limp);
            pose.ArmRUpper = Vector3.Lerp(DownArmRUpper, Mirror(hangLUpper), limp);
            pose.ArmRFore = Vector3.Lerp(DownArmRFore, Mirror(hangLFore), limp);
            pose.Grip = 0.15f;

            // 引き上げ途中の足元の引きずり遅れと、立ち上がり直前の膝のふらつき。
            float drag = Mathf.Sin(Mathf.PI * lift) * 0.08f;
            pose.FootL.z -= drag;
            pose.FootR.z -= drag * 0.85f;

            float wobble = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 0.90f, lift))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.90f, 1f, lift)));
            pose.TorsoRoll += Mathf.Sin(lift * 28f) * 3.5f * wobble;
            pose.HipsOffset.y -= 0.025f * wobble;

            // ぐいっと引かれた瞬間は頭が後ろへ残り、立たされても前髪で顔が隠れないよう少し顔を上げつつカメラ側へ傾ける。
            float toCam = CameraYawToMe();
            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 2f, lift) - 12f * Mathf.Clamp01(haul.Yank);
            pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, Mathf.Clamp(toCam * 0.35f, -24f, 24f), lift);
            pose.HeadRoll = -side * 9f * lift;

            // 連打のもがき揺れ（肩と首を振って抵抗する）。
            float pulse = Mathf.Clamp01(haul.StrugglePulse);
            if (pulse > 0.001f)
            {
                float twist = Mathf.Sin(Time.time * 28f) * pulse;
                pose.TorsoYaw += 10f * twist;
                pose.TorsoRoll += 6f * twist;
                pose.HeadYaw -= 8f * twist;
                pose.HipsOffset.y -= 0.02f * pulse;
            }
            return pose;
        }

        /// <summary>
        /// うつ伏せの引き起こし中の腕。敵が肩の下へ手を入れて上体を起こし、
        /// 抱きかかえて立たせる間、両腕は力なく床側〜体の脇へ垂れ下がる。
        /// </summary>
        void ApplyProneHauledArms(GroundHaulUp haul, Vector3 fwd, Vector3 left, Vector3 up,
            float handRollL, float handRollR)
        {
            float lift = Mathf.Clamp01(haul.Lift);
            float dropT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.55f, lift));

            Vector3 downUpperL = (fwd * 0.92f + left * 0.22f - up * 0.10f).normalized;
            Vector3 downForeL = (fwd * 0.96f + left * 0.10f - up * 0.04f).normalized;
            Vector3 downUpperR = (fwd * 0.52f - left * 0.62f - up * 0.20f).normalized;
            Vector3 downForeR = (fwd * 0.58f + left * 0.52f - up * 0.26f).normalized;

            // 正面から脇の下へ腕を差し込まれているので、二の腕は横へ開いて敵の前腕に乗る。
            Vector3 hangUpperL = (left * 0.66f - up * 0.70f + fwd * 0.12f).normalized;
            Vector3 hangForeL = (left * 0.22f - up * 0.95f + fwd * 0.16f).normalized;
            Vector3 hangUpperR = (-left * 0.66f - up * 0.70f + fwd * 0.12f).normalized;
            Vector3 hangForeR = (-left * 0.22f - up * 0.95f + fwd * 0.16f).normalized;

            float pulse = Mathf.Clamp01(haul.StrugglePulse);
            if (pulse > 0.001f)
            {
                float flail = Mathf.Sin(Time.time * 28f) * pulse;
                hangUpperL = (hangUpperL + fwd * (0.25f * pulse) + left * (0.20f * flail)).normalized;
                hangUpperR = (hangUpperR + fwd * (0.25f * pulse) - left * (0.20f * flail)).normalized;
            }

            Rig.SetArm(true,
                Vector3.Slerp(downUpperL, hangUpperL, dropT),
                Vector3.Slerp(downForeL, hangForeL, dropT),
                handRollL);
            Rig.SetArm(false,
                Vector3.Slerp(downUpperR, hangUpperR, dropT),
                Vector3.Slerp(downForeR, hangForeR, dropT),
                handRollR);
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
