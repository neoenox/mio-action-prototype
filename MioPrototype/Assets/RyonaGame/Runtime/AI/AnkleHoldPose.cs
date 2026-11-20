using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 足取り（<see cref="GrappleKind.AnkleGrab"/>）で、掴む側の片腕を彼女の足首へ合わせる。
    /// 彼女の掴まれた足（左）は姿勢側で後ろへ持ち上がっているので、毎フレーム足首の位置を追う。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class AnkleHoldPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null || !_brain.IsHolding)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain || hold.Kind != GrappleKind.AnkleGrab)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready || _targetRig.LFoot == null)
                return;

            // 足首に近い側の腕で足首を、もう片方の腕ですね（少し上）を掴んで両手で引き止める。
            Vector3 grip = _targetRig.LFoot.position + Vector3.up * 0.03f;
            Vector3 shin = Vector3.Lerp(_targetRig.LFoot.position, _targetRig.Hips.position, 0.24f);
            bool left = Vector3.Dot(grip - transform.position, transform.right) < 0f;
            SolveArm(left, grip);
            SolveArm(!left, shin);

            if (_camera == null)
                _camera = FindFirstObjectByType<ThirdPersonCamera>();
            if (_camera != null)
            {
                Transform p = _brain.Player.transform;
                float sign = Mathf.Abs(p.forward.x) > 0.1f ? Mathf.Sign(p.forward.x) : 1f;
                _camera.HoldFocus(0.44f, -0.04f, p.position.x - sign * 0.22f, 0.35f, 4f, -18f * sign);
            }
        }

        void SolveArm(bool left, Vector3 goal)
        {
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            // 肘は体の外側・下へ向ける（腕を前へ伸ばして低い位置を掴む）。
            Vector3 outward = transform.right * (left ? -1f : 1f);
            Vector3 pole = upper.position + outward * 0.35f - Vector3.up * 0.40f;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
        }
    }
}
