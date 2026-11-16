using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引きずり起こし（<see cref="GroundHaulUp"/>）で、掴む側の腕を彼女の髪（後頭部）へ合わせる。
    /// 体のかがみ・反りは <see cref="EnemyFigure"/> の姿勢（<c>BuildHaulPose</c>）が作る。
    ///
    /// 手首ではなく後頭部を掴むので、彼女の腕は自由。両手でその手首へ縋る姿勢は
    /// <see cref="CharacterAnimator"/> 側が作る。
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
            if (rig == null || !rig.Ready || rig.Head == null)
                return;

            // 後頭部の髪を掴む。手は頭の少し上・後ろ。
            Vector3 hair = rig.Head.position + Vector3.up * 0.10f - rig.Head.forward * 0.03f;
            bool leftArm = haul.EnemyLeftArm;
            SolveArm(leftArm, hair, haul.Grip);

            // 空き手で近い側の上腕を押さえる（暴れられないように）。
            Transform control = haul.EnemyLeftArm ? rig.RUpperArm : rig.LUpperArm;
            if (control != null)
                SolveArm(!leftArm, control.position, haul.Grip * 0.55f);
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
