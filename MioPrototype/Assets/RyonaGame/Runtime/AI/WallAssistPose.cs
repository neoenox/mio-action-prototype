using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
    /// <summary>
    /// 壁の二体がかり（Y 字）で、二体目（アシスト）の腕。
    /// 内側の手（左手）で彼女の右前腕を掴み、外側の手（右手）で腹を打つ。
    /// 彼女の手首は壁に固定されたままなので、掴むのは前腕。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class WallAssistPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null || !_brain.IsWallAssisting)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || !hold.IsWallPin)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready || _targetRig.RHand == null ||
                _targetRig.RLowerArm == null || _targetRig.Spine == null || _targetRig.Chest == null)
                return;

            float split = hold.WallSplitWeight;
            if (split <= 0.001f)
                return;

            // 内側の手（彼の左手）で彼女の右前腕を掴む。3 人目は掴まない（腕は力を抜く）。
            // 3 人がかり（最後の段階）では、アシストも手を離す（誰も彼女を持たない）。
            float gang = hold.WallGangWeight;
            Vector3 her = _brain.Player.transform.position;
            Vector3 front = Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up).normalized;
            float side = transform.position.x >= her.x ? 1f : -1f;
            if (hold.WallThird != _brain)
            {
                Vector3 grip = Vector3.Lerp(_targetRig.RHand.position, _targetRig.RLowerArm.position, 0.25f);
                SolveArm(true, grip + Vector3.up * 0.02f, split * (1f - gang));
                if (gang > 0.001f)
                    WallSplitArm.Guard(_figure, true, her, front, side, gang, transform);
            }
            else
                WallSplitArm.Relax(_figure, true, split, transform);

            // 外側の手（彼の右手）は腹パン。打たない間は脇へ下ろす（上げたままだと不自然）。
            if (hold.WallSplitPuncher == _brain && hold.WallSplitPunchAge >= 0f && !hold.WallSplitKneeStrike)
                WallSplitArm.Punch(_figure, false, her, front, side,
                    hold.WallSplitPunchAge, split, transform, hold.WallSplitFace,
                    hold.WallSplitFace && _targetRig.Head != null
                        ? _targetRig.Head.position + front * 0.10f
                        : (Vector3?)null);
            else
                WallSplitArm.Guard(_figure, false, her, front, side, split, transform);

            // 締めの膝入れは、空き手で彼女の腰を掴んで引き寄せる。
            if (hold.WallSplitKneeStrike && hold.WallSplitPuncher == _brain)
                WallSplitArm.Pull(_figure, false, her, front, side,
                    split * Mathf.Max(hold.WallSplitKneeWeight(_brain), hold.WallSplitKneeThrust(_brain)),
                    transform);
        }

        void SolveArm(bool left, Vector3 goal, float weight)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 outward = transform.right * (left ? -1f : 1f);
            Vector3 pole = upper.position + outward * 0.55f - Vector3.up * 0.30f;
            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Quaternion aimedElbow = elbow.rotation;
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, aimedElbow, weight);
        }
    }
}
