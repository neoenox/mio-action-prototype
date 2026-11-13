using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 壁の二体がかりの締め（膝入れ）で、膝を彼女の腹へ合わせる。
    /// 関節の角度（FK）だけで振り上げると、膝が腹の横を通って当たらない（実測: 腹から 0.25m）。
    /// 突き上げの間、内側の脚の付け根を、膝が腹の点を向くように毎フレーム回す。
    /// 効きは <see cref="PlayerGrapple.WallSplitKneeThrust"/> に合わせ、FK の姿勢から滑らかに切り替える。
    /// </summary>
    [DefaultExecutionOrder(21000)]
    public sealed class WallKneeStrikePose : MonoBehaviour
    {
        /// <summary>
        /// 腹の点の高さ（彼女の足元から）と、彼女の中心から手前へ出す量。
        /// 腹の表面は 0.12m 前後なので、膝の半径（約 7cm）ぶん手前を狙う（表面を狙うと膝が半分めり込む）。
        /// </summary>
        const float BellyHeight = 0.86f;
        const float BellyFront = 0.19f;

        EnemyFigure _figure;
        EnemyBrain _brain;

        void LateUpdate()
        {
            if (_figure == null) _figure = GetComponent<EnemyFigure>();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_figure == null || _brain == null || _brain.Player == null)
                return;

            var hold = _brain.Player.GetComponent<PlayerGrapple>();
            if (hold == null || !hold.IsGrappled || !hold.IsWallPin)
                return;

            float thrust = hold.WallSplitKneeThrust(_brain);
            if (thrust <= 0.02f)
                return;

            Vector3 her = _brain.Player.transform.position;
            Vector3 front = Vector3.ProjectOnPlane(_brain.Player.transform.forward, Vector3.up).normalized;
            // 蹴りは足の裏を腹の表面へ（膝より手前に出さない）。
            Vector3 belly = her + Vector3.up * BellyHeight + front * (hold.WallSplitKickOf(_brain) ? 0.14f : BellyFront);

            // 彼女に近い側（内側）の脚。姿勢側（BuildWallSplitKneePose）と同じ選び方。
            Transform hipL = _figure.HipBone(true);
            Transform hipR = _figure.HipBone(false);
            if (hipL == null || hipR == null)
                return;
            bool left = (hipL.position - her).sqrMagnitude < (hipR.position - her).sqrMagnitude;

            Transform hip = left ? hipL : hipR;
            // 膝入れは膝を、蹴りは足首（足）を腹へ向ける。
            Transform knee = hold.WallSplitKickOf(_brain) ? _figure.AnkleBone(left) : _figure.KneeBone(left);
            if (knee == null)
                return;

            Vector3 current = knee.position - hip.position;
            Vector3 wanted = belly - hip.position;
            if (current.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f)
                return;

            // 太ももの向きを、腹の点へ向ける（膝は付け根から太ももの長さだけ離れた位置に来る）。
            Quaternion aim = Quaternion.FromToRotation(current.normalized, wanted.normalized);
            float weight = Mathf.SmoothStep(0f, 1f, thrust);
            hip.rotation = Quaternion.Slerp(Quaternion.identity, aim, weight) * hip.rotation;
        }
    }
}
