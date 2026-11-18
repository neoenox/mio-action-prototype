using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// <see cref="JointSkeleton"/>（プレイヤー／敵の骨格）から体のカプセル群を組み立て、
    /// 2 キャラクター間の部位の重なり（めり込み）を測る。
    /// </summary>
    public static class BodyOverlapProbe
    {
        /// <summary>
        /// 骨格から体の部位カプセル（最大 10 本）を組み立てて <paramref name="capsules"/> へ入れる。
        /// <paramref name="heightHint"/> が 0 以下のときは骨の長さの合計から身長を推定する。
        /// </summary>
        public static void BuildCapsules(
            in JointSkeleton skeleton,
            List<BodyCapsule> capsules,
            float heightHint = 0f)
        {
            capsules.Clear();
            if (!skeleton.IsValid)
                return;

            float height = heightHint > 0.1f ? heightHint : EstimateHeight(in skeleton);

            // 胴: 腰から胸を通り首の手前まで。
            Vector3 chestTop = Vector3.Lerp(skeleton.Chest.position, skeleton.Head.position, 0.72f);
            AddCapsule(capsules, OverlapPart.Torso, skeleton.Hips.position, chestTop, height);

            // 頭: 首の付け根（Head）より少し上から頭の中央付近まで。
            Vector3 headUp = skeleton.Head.up;
            Vector3 headBottom = skeleton.Head.position + headUp * (height * 0.040f);
            Vector3 headTop = skeleton.Head.position + headUp * (height * 0.080f);
            AddCapsule(capsules, OverlapPart.Head, headBottom, headTop, height);

            TryAddLimb(capsules, OverlapPart.UpperArmL, skeleton.LUpperArm, skeleton.LLowerArm, height);
            TryAddLimb(capsules, OverlapPart.UpperArmR, skeleton.RUpperArm, skeleton.RLowerArm, height);
            TryAddLimb(capsules, OverlapPart.ForearmL, skeleton.LLowerArm, skeleton.LHand, height);
            TryAddLimb(capsules, OverlapPart.ForearmR, skeleton.RLowerArm, skeleton.RHand, height);

            TryAddLimb(capsules, OverlapPart.ThighL, skeleton.LUpperLeg, skeleton.LLowerLeg, height);
            TryAddLimb(capsules, OverlapPart.ThighR, skeleton.RUpperLeg, skeleton.RLowerLeg, height);
            TryAddLimb(capsules, OverlapPart.ShinL, skeleton.LLowerLeg, skeleton.LFoot, height);
            TryAddLimb(capsules, OverlapPart.ShinR, skeleton.RLowerLeg, skeleton.RFoot, height);
        }

        /// <summary>
        /// 2 キャラクターの部位カプセル同士を総当たりで測り、
        /// 許容（<paramref name="slackMeters"/>）を超えた重なりだけを <paramref name="violations"/> へ追記する。
        /// </summary>
        public static void MeasureViolations(
            string subjectA,
            IReadOnlyList<BodyCapsule> capsulesA,
            string subjectB,
            IReadOnlyList<BodyCapsule> capsulesB,
            List<BodyOverlapReading> violations,
            float slackMeters = BodyOverlap.DefaultSlackMeters)
        {
            for (int i = 0; i < capsulesA.Count; i++)
            {
                BodyCapsule capA = capsulesA[i];
                for (int j = 0; j < capsulesB.Count; j++)
                {
                    BodyOverlapReading reading = BodyOverlap.Measure(
                        subjectA, capA,
                        subjectB, capsulesB[j],
                        slackMeters);
                    if (reading.IsViolation)
                        violations.Add(reading);
                }
            }
        }

        /// <summary>
        /// 姿勢（しゃがみ・ダウン等）で変化しない骨の長さの合計から身長を推定する。
        /// </summary>
        public static float EstimateHeight(in JointSkeleton skeleton)
        {
            if (!skeleton.IsValid)
                return 1.6f;

            float spine = Vector3.Distance(skeleton.Hips.position, skeleton.Chest.position)
                        + Vector3.Distance(skeleton.Chest.position, skeleton.Head.position);

            float leg = 0f;
            if (skeleton.LUpperLeg != null && skeleton.LLowerLeg != null && skeleton.LFoot != null)
            {
                leg = Vector3.Distance(skeleton.LUpperLeg.position, skeleton.LLowerLeg.position)
                    + Vector3.Distance(skeleton.LLowerLeg.position, skeleton.LFoot.position);
            }
            else if (skeleton.RUpperLeg != null && skeleton.RLowerLeg != null && skeleton.RFoot != null)
            {
                leg = Vector3.Distance(skeleton.RUpperLeg.position, skeleton.RLowerLeg.position)
                    + Vector3.Distance(skeleton.RLowerLeg.position, skeleton.RFoot.position);
            }

            // 首より上（約 0.14m）＋足首より下（約 0.06m）の概算を足す。
            float estimated = spine + leg + 0.20f;
            return Mathf.Clamp(estimated, 1.2f, 2.3f);
        }

        static void TryAddLimb(
            List<BodyCapsule> capsules,
            OverlapPart part,
            Transform start,
            Transform end,
            float height)
        {
            if (start == null || end == null)
                return;
            AddCapsule(capsules, part, start.position, end.position, height);
        }

        static void AddCapsule(
            List<BodyCapsule> capsules,
            OverlapPart part,
            Vector3 start,
            Vector3 end,
            float height)
        {
            float radius = BodyPartSpecs.RadiusForHeight(part, height);
            capsules.Add(new BodyCapsule(part, ToBodyVec3(start), ToBodyVec3(end), radius));
        }

        static BodyVec3 ToBodyVec3(Vector3 v) => new BodyVec3(v.x, v.y, v.z);
    }
}
