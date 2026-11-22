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
        Transform _head;

        /// <summary>頭の中心と腕の線分の最小距離（頭の半径 ~0.1 + 腕の太さ ~0.05 に余裕）。</summary>
        const float HeadClearance = 0.28f;

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

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

            _head = rig.Head;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.5f, haul.Grip));
            Transform shL = rig.LUpperArm;
            Transform shR = rig.RUpperArm;
            Vector3 chest = rig.Chest != null ? rig.Chest.position : rig.Hips.position + Vector3.up * 0.3f;

            // 彼女の体の向き（寝ていても立っていても体に沿う）。
            //   up    = 腰 → 胸、side = 右肩 → 左肩、front = 胸の正面。
            Vector3 up = (chest - rig.Hips.position).normalized;
            Vector3 side = (shL.position - shR.position).normalized;
            Vector3 front = Vector3.Cross(up, side).normalized;

            // 敵がいる面（near）と反対の面（far）。仰向けは背中側から、うつ伏せは胸側から入れる。
            Vector3 near = Vector3.Dot(transform.position - chest, front) > 0f ? front : -front;
            Vector3 far = -near;

            // 差し込み（scoop 0）→ すくい上げ（scoop 1）。
            //   0: 手は脇の下の手前（敵の側）。床と体の間へ指先を差し入れる位置。
            //   1: 手は脇の下をくぐって反対の面へ回り、胸（うつ伏せなら背中）の上部を抱える。
            // 寝ている間は背中側（床）へ手を回せないので、手は脇の下に差し込むまで。
            // 上体が起き始めたら（Lift）前腕が脇の下をくぐり、反対の面へ回って抱える。
            float scoop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.40f, haul.Lift));
            bool leftIsHerLeft = Vector3.Dot(side, transform.right) < 0f;
            SolveScoop(leftIsHerLeft, shL.position, +1f, chest, up, side, near, far, scoop, weight);
            SolveScoop(!leftIsHerLeft, shR.position, -1f, chest, up, side, near, far, scoop, weight);
        }

        /// <summary>
        /// 片腕を彼女の脇の下へ通す。手首の行き先と、肘の向き（脇の下の下・外側）を決めて解く。
        /// </summary>
        void SolveScoop(bool enemyLeft, Vector3 herShoulder, float sideSign, Vector3 chest,
            Vector3 up, Vector3 side, Vector3 near, Vector3 far, float scoop, float weight)
        {
            Vector3 outward = side * sideSign;
            // 脇の下: 肩の付け根から体の下（腰の方）へ少し、体の内側へ少し。
            Vector3 armpit = herShoulder - up * 0.09f - outward * 0.03f;

            // 差し込み: 手は脇の下の奥（腕と胴の隙間、敵の側の面寄り）。
            // すくい: 前腕が脇の下をくぐり、手は反対の面（仰向けなら胸の上部）を抱える。
            Vector3 insert = armpit + near * 0.03f + outward * 0.02f;
            Vector3 hook = chest + far * 0.09f + outward * 0.08f + up * 0.01f;
            Vector3 goal = Vector3.Lerp(insert, hook, scoop);

            // 肘は彼女の肩の外・頭側・敵の側。腕は肩の外を回り、前腕が脇の下へ横から入る。
            // （真上に肘を置くと、手が肩の上から胸へかぶさり「上から押さえる」画になった。撮影 2026-11-22）
            Vector3 elbowHint = herShoulder + outward * 0.30f + up * 0.10f + near * 0.10f;
            SolveArm(enemyLeft, goal, elbowHint, weight);
        }

        void SolveArm(bool left, Vector3 goal, Vector3 elbowHint, float weight)
        {
            if (weight <= 0.001f)
                return;
            var upper = _figure.ShoulderBone(left);
            var elbow = _figure.ElbowBone(left);
            var wrist = _figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 outward = transform.right * (left ? -1f : 1f);
            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;

            // 肘は脇の下の下へ向ける。それでも彼女の頭が敵の二の腕・前腕に食い込むとき
            // （計測 2026-11-22: 頭と上腕の距離が 0.04m）は、頭から離れるまで外へ張り出す。
            Vector3 head = _head != null ? _head.position : Vector3.positiveInfinity;
            for (float flare = 0f; ; flare += 0.12f)
            {
                Vector3 pole = elbowHint + outward * flare;
                ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, transform.right);
                EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
                if (flare >= 0.72f || _head == null)
                    break;
                float clear = Mathf.Min(DistanceToSegment(head, upper.position, elbow.position),
                                        DistanceToSegment(head, elbow.position, wrist.position));
                if (clear >= HeadClearance)
                    break;
            }
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, elbow.rotation, weight);
        }
    }
}
