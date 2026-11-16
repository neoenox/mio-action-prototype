using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引きずり起こし（<see cref="GroundHaulUp"/>）で、敵の両腕を彼女の脇の下（上腕）へ入れる。
    ///
    /// 髪は掴まない。仰向けなら背後から、うつ伏せなら前方から抱え上げる。
    /// 体のかがみ・反りは <see cref="EnemyFigure"/> の姿勢（<c>BuildHaulPose</c>）が作る。
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
            if (rig == null || !rig.Ready || rig.LUpperArm == null || rig.RUpperArm == null)
                return;

            // 彼女の体の中心から見て、敵のいる側へ少しだけ手を回す。
            Vector3 toEnemy = transform.position - rig.Hips.position;
            toEnemy.y = 0f;
            toEnemy = toEnemy.sqrMagnitude > 0.001f ? toEnemy.normalized : transform.forward;
            Vector3 under = toEnemy * 0.07f - Vector3.up * 0.04f;

            // 脇の下へ。手首は上腕の少し内側・下。
            SolveArm(true, rig.LUpperArm.position + under, haul.Grip);
            SolveArm(false, rig.RUpperArm.position + under, haul.Grip);
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
