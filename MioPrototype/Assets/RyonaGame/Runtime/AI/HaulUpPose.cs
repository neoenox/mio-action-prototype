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
            Transform shL = rig.LUpperArm;
            Transform shR = rig.RUpperArm;
            bool leftIsHerLeft = Vector3.Dot(shL.position - shR.position, transform.right) < 0f;
            Vector3 center = (shL.position + shR.position) * 0.5f;
            Vector3 torso = rig.Chest != null
                ? Vector3.Lerp(rig.Hips.position, rig.Chest.position, 0.55f)
                : rig.Hips.position + Vector3.up * 0.25f;
            Vector3 toHer = Vector3.ProjectOnPlane(torso - transform.position, Vector3.up);
            toHer = toHer.sqrMagnitude > 1e-4f ? toHer.normalized : transform.forward;
            Vector3 shoulderAxis = Vector3.ProjectOnPlane(shL.position - shR.position, Vector3.up);
            Vector3 toHerLeft = shoulderAxis.sqrMagnitude > 1e-4f ? shoulderAxis.normalized : -transform.right;

            Vector3 underL = Vector3.Lerp(shL.position, center, 0.15f) - Vector3.up * 0.08f;
            Vector3 underR = Vector3.Lerp(shR.position, center, 0.15f) - Vector3.up * 0.08f;

            // うつ伏せは正面から胴へ、仰向けは背後から脇の下を通して抱え上げる。
            Vector3 hugL = haul.IsProne
                ? torso + toHerLeft * 0.15f + toHer * 0.06f
                : torso + toHerLeft * 0.16f + toHer * 0.05f;
            Vector3 hugR = haul.IsProne
                ? torso - toHerLeft * 0.15f + toHer * 0.06f
                : torso - toHerLeft * 0.16f + toHer * 0.05f;

            float embrace = haul.EmbraceWeight;
            SolveArm(leftIsHerLeft, Vector3.Lerp(underL, hugL, embrace), weight);
            SolveArm(!leftIsHerLeft, Vector3.Lerp(underR, hugR, embrace), weight);
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
