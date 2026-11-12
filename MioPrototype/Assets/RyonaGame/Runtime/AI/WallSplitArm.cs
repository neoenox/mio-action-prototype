using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;


namespace Ryona.Game
{
    /// <summary>
    /// 二体がかりの空き手。拘束役（左手）とアシスト（右手）で同じ形を使う。
    /// <para>
    /// 打たない間は脇へ下ろす。掴みの姿勢のまま上げていると「殴る腕を上げている」に
    /// 見える（実測）。打つときは拳を胸元に溜めて腹へ短く突き出すボディーブロー。
    /// </para>
    /// </summary>
    static class WallSplitArm
    {
        /// <summary>腹パン（ボディーブロー）。0.22 秒で当たる。</summary>
        public static void Punch(EnemyFigure figure, bool left, Vector3 her, Vector3 front,
            float side, float age, float weight, Transform body)
        {
            var upper = figure.ShoulderBone(left);
            var elbow = figure.ElbowBone(left);
            var wrist = figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            // 拳を自分の胸元に溜め、腹へ短く突き出す。大きく振りかぶって横へ振り抜くと
            // 「太鼓を叩いている」ように見えた（実測）。
            // 振りかぶりは自分の胸の前（体の面より手前）。体の後ろへ引くと腕が折れて肘が上がる。
            Vector3 windup = her + Vector3.up * 1.00f + front * 0.56f + Vector3.right * (side * 0.30f);
            Vector3 belly = her + Vector3.up * 0.88f + front * 0.12f;

            float drive = age < 0.12f ? 0f : Mathf.Clamp01((age - 0.12f) / 0.10f);
            if (age > 0.22f)
                drive = 1f - Mathf.Clamp01((age - 0.22f) / 0.18f);   // 打ったあとは胸元へ戻す
            float punch = Mathf.SmoothStep(0f, 1f, drive) * weight;
            Vector3 goal = Vector3.Lerp(windup, belly, Mathf.SmoothStep(0f, 1f, drive));
            // 肘は拳の後ろ（体側）。横に張らない。
            Vector3 pole = upper.position - Vector3.up * 0.40f + front * 0.10f;

            // 肩ごと踏み込む（腕だけを振ると棒で叩いているように見える）。
            figure.BraceHoldUpperBody(-22f * punch, side * 6f * punch, 6f * punch, -side * 22f * punch);

            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, body.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Quaternion aimedElbow = elbow.rotation;
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, aimedElbow, weight);
        }

        /// <summary>打たない間の空き手。自分の脇に下ろして構える。</summary>
        public static void Guard(EnemyFigure figure, bool left, Vector3 her, Vector3 front,
            float side, float weight, Transform body)
        {
            var upper = figure.ShoulderBone(left);
            var elbow = figure.ElbowBone(left);
            var wrist = figure.WristBone(left);
            if (upper == null || elbow == null || wrist == null)
                return;

            Vector3 goal = her + Vector3.up * 1.00f + front * 0.56f + Vector3.right * (side * 0.30f);
            Vector3 pole = upper.position - Vector3.up * 0.45f + front * 0.10f;

            Quaternion previousUpper = upper.rotation;
            Quaternion previousElbow = elbow.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, wrist, goal, pole, body.right);
            EnemyFigure.SettleElbowHinge(upper, elbow, wrist);
            Quaternion aimedElbow = elbow.rotation;
            upper.rotation = Quaternion.Slerp(previousUpper, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(previousElbow, aimedElbow, weight);
        }
    }
}
