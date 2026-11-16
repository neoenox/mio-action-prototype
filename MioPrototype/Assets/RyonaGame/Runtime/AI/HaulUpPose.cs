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

            float weight = Mathf.SmoothStep(0f, 1f, haul.Grip);
            if (haul.IsProne)
            {
                bool grabbedLeft = haul.GrabbedLeftSide;
                Transform herUpper = grabbedLeft ? rig.LUpperArm : rig.RUpperArm;
                Transform herElbow = grabbedLeft ? rig.LLowerArm : rig.RLowerArm;
                Transform herHand = grabbedLeft ? rig.LHand : rig.RHand;
                if (herUpper != null && herElbow != null && herHand != null)
                {
                    // 手首と上腕・肘を両手で掴んで引き起こす。腕が交差しないように左右を割り当てる。
                    bool leftForWrist = Vector3.Dot(herHand.position - herUpper.position, transform.right) < 0f;
                    Vector3 wristGoal = herHand.position;
                    Vector3 armGoal = Vector3.Lerp(herUpper.position, herElbow.position, 0.65f);
                    SolveArm(leftForWrist, wristGoal, weight);
                    SolveArm(!leftForWrist, armGoal, weight);
                    return;
                }
            }

            // 彼女の体の中心から見て、敵のいる側へ少しだけ手を回す。
            Vector3 toEnemy = transform.position - rig.Hips.position;
            toEnemy.y = 0f;
            toEnemy = toEnemy.sqrMagnitude > 0.001f ? toEnemy.normalized : transform.forward;
            Vector3 under = toEnemy * 0.10f - Vector3.up * 0.10f;

            // 脇の下へ。手首は上腕の少し内側・下。
            SolveArm(true, rig.LUpperArm.position + under, weight);
            SolveArm(false, rig.RUpperArm.position + under, weight);
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
            Vector3 pole = upper.position + outward * 0.30f - Vector3.up * 0.10f;
            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, elbow.rotation, weight);
        }
    }
}
