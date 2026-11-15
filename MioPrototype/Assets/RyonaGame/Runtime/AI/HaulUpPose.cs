using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引きずり起こし（<see cref="GroundHaulUp"/>）で、掴む側の腕を彼女の手首と服の裾へ合わせる。
    /// 体のかがみ・反りは <see cref="EnemyFigure"/> の姿勢（<c>BuildHaulPose</c>）が作る。
    ///
    /// 実行順は彼女の姿勢（<see cref="CharacterAnimator"/>、20000）より後。彼女の腕は
    /// 敵の肩の前（<see cref="GroundHaulUp.PullAnchor"/>）へ向けて張られているので、
    /// ここでは「張った結果の手首」をそのまま握りに行く（二人の手が必ず合う）。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class HaulUpPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;

            var haul = _brain.Player.GroundMount.Haul;
            if (haul == null || haul.Attacker != _brain || haul.Grip <= 0.001f)
                return;

            ProceduralRig rig = haul.TargetRig;
            if (rig == null || !rig.Ready || rig.LHand == null || rig.RHand == null || rig.Hips == null)
                return;

            Transform wrist = haul.HerLeftWrist ? rig.LHand : rig.RHand;
            bool leftArm = haul.EnemyLeftArm;
            SolveArm(leftArm, wrist.position, haul.Grip);

            // 服の裾（腰の、敵に近い側）。立たされたら離して手首だけで吊る。
            Vector3 here = transform.position;
            Vector3 toMe = Vector3.ProjectOnPlane(here - rig.Hips.position, Vector3.up).normalized;
            Vector3 hem = rig.Hips.position + toMe * 0.12f + Vector3.up * 0.02f;
            float hemWeight = haul.Grip * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, haul.Lift)));
            SolveArm(!leftArm, hem, hemWeight);
        }

        void SolveArm(bool left, Vector3 goal, float weight)
        {
            if (weight <= 0.001f)
                return;
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 outward = transform.right * (left ? -1f : 1f);
            Vector3 pole = upper.position + outward * 0.35f - Vector3.up * 0.35f;
            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, elbow.rotation, weight);
        }
    }
}
