using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
    /// <summary>
    /// 頭上拘束。背後から彼女の両手首を掴み、頭の上へ固定する。
    /// 彼女の腕は <see cref="CharacterAnimator"/> が頭上へ IK する。こちらは
    /// 相手（押さえ役）の両腕を、その手首へ毎フレーム合わせる。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class OverheadHoldPose : MonoBehaviour
    {
        EnemyFigure _figure;
        EnemyBrain _brain;
        ProceduralRig _targetRig;
        ThirdPersonCamera _camera;

        bool _log;
        float _logAt;

        void Awake()
        {
            _log = LaunchOptions.Has("-overheadlog");
        }

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || hold.Captor != _brain ||
                !hold.IsWallPin)
                return;

            if (_targetRig == null)
                _targetRig = _brain.Player.GetComponentInChildren<ProceduralRig>();
            if (_targetRig == null || !_targetRig.Ready ||
                _targetRig.LHand == null || _targetRig.RHand == null)
                return;

            // 壁へ寄る間に腕を上げ、押さえつけ中は手首に追従する。
            float weight = hold.WallGripWeight;
            if (weight <= 0f)
                return;

            float lift = hold.WallPhase == WallStage.TossWindup
                ? 1f - hold.WallTossLower : hold.WallRestraintWeight;

            // 壁へ貼り付いたあとも、彼は彼女の腕を掴んだままにする。
            // 手首は壁の上（頭の上）まで上がるので、彼の腕では届かない（実測: 肩から 0.7m、腕は 0.55m）。
            // 掴み所を手首から前腕（肘寄り）へ滑らせ、腕を押さえ込む形にする。
            float adhere = hold.WallAdhereWeight;
            Vector3 gripL = _targetRig.LHand.position;
            Vector3 gripR = _targetRig.RHand.position;
            if (adhere > 0f)
            {
                if (_targetRig.LLowerArm != null)
                    gripL = Vector3.Lerp(gripL, _targetRig.LLowerArm.position, 0.22f * adhere);
                if (_targetRig.RLowerArm != null)
                    gripR = Vector3.Lerp(gripR, _targetRig.RLowerArm.position, 0.22f * adhere);
            }

            // 二体がかり（Y 字）: 拘束役は右手で彼女の左腕だけを持ち、
            // 左手を空けて腹パンへ回す。もう片腕はアシストが持つ。
            float split = hold.WallSplitWeight;
            Solve(true, gripR, weight * (1f - split), lift, adhere);
            Solve(false, gripL, weight, lift, adhere);

            if (split > 0.001f)
            {
                // 空き手は腹パン。打たない間は脇へ下ろす（上げたままだと不自然）。
                Vector3 herPos = _brain.Player.transform.position;
                Vector3 herFront = Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up).normalized;
                float herSide = transform.position.x >= herPos.x ? 1f : -1f;
                bool punching = hold.WallSplitPuncher == _brain && hold.WallSplitPunchAge >= 0f;
                if (punching)
                    WallSplitArm.Punch(_figure, true, herPos, herFront, herSide,
                        hold.WallSplitPunchAge, split, transform);
                else
                    WallSplitArm.Guard(_figure, true, herPos, herFront, herSide, split, transform);
            }

            // 頭が壁側へ揺れた分は、敵が届いた位置へ澪の手首を寄せて接触を保つ。
            // 貼り付いたあとは手首を手放しているので、手前に引き戻さない。
            if (weight >= 0.99f && adhere <= 0.01f)
            {
                SettlePlayerWrist(_targetRig.LUpperArm, _targetRig.LLowerArm, _targetRig.LHand,
                    _figure.WristBone(false), -_targetRig.transform.right);
                SettlePlayerWrist(_targetRig.RUpperArm, _targetRig.RLowerArm, _targetRig.RHand,
                    _figure.WristBone(true), _targetRig.transform.right);
            }

            if (_log && Time.time - _logAt > 0.25f)
            {
                _logAt = Time.time;
                Vector3 head = _targetRig.Head != null ? _targetRig.Head.position : Vector3.zero;
                Vector3 pl = _targetRig.LHand.position;
                Vector3 pr = _targetRig.RHand.position;
                Vector3 cl = _figure.WristBone(true).position;
                Vector3 cr = _figure.WristBone(false).position;
                Debug.Log($"[OverheadPose] w={weight:F2} adhere={adhere:F2} headY={head.y:F2} " +
                          $"playerL=({pl.x:F2},{pl.y:F2},{pl.z:F2}) playerR=({pr.x:F2},{pr.y:F2},{pr.z:F2}) " +
                          $"elbowL=({_targetRig.LLowerArm.position.x:F2},{_targetRig.LLowerArm.position.z:F2}) " +
                          $"captorL=({cl.x:F2},{cl.y:F2},{cl.z:F2}) captorR=({cr.x:F2},{cr.y:F2},{cr.z:F2}) " +
                          $"gripGapL={Vector3.Distance(cl, gripR):F2}");
            }

        }

        static void SettlePlayerWrist(Transform upper, Transform elbow, Transform hand,
            Transform captorWrist, Vector3 outward)
        {            if (elbow == null || captorWrist == null ||
                (hand.position - captorWrist.position).sqrMagnitude <= 0.000625f)
                return;

            ProceduralRig.SolveTwoBone(upper, elbow, hand, captorWrist.position,
                elbow.position + outward * 0.20f, outward);
        }

        /// <summary>
        /// 相手の腕を狙いの位置へ。肘は外へ開く。壁へ押し込むときは肘を下げて体重を乗せる。
        /// </summary>
        void Solve(bool left, Vector3 goal, float weight, float lift, float press)
        {
            if (weight <= 0f)
                return;

            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 outward = transform.right * (left ? -1f : 1f);
            Vector3 pole = upper.position
                + Vector3.up * Mathf.Lerp(-0.40f, 0.55f, lift)
                + outward * Mathf.Lerp(0.45f, 0.30f, press)
                - Vector3.up * 0.18f * press;

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
