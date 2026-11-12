using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        internal void PoseBackHold(float fold, float lift, Vector3 braceOffset, float leanBack, float leanSide, float chin)
        {
            Vector3 leftFoot = _ankleL.position;
            Vector3 rightFoot = _ankleR.position;
            _hips.localPosition = new Vector3(_hipsX, _hipsY - 0.065f * fold + 0.045f * lift,
                _hipsZ - 0.035f * fold - 0.06f * lift) + braceOffset;
            BraceHoldUpperBody(-19f * fold + 18f * lift + leanBack, leanSide, -8f * fold + 8f * lift + chin);
            PlantHoldFeet(leftFoot, rightFoot);
        }

        void PlantHoldFeet(Vector3 leftFoot, Vector3 rightFoot)
        {
            ProceduralRig.SolveTwoBone(_hipL, _kneeL, _ankleL, leftFoot,
                _hipL.position + transform.forward * 0.5f);
            ProceduralRig.SolveTwoBone(_hipR, _kneeR, _ankleR, rightFoot,
                _hipR.position + transform.forward * 0.5f);
            _ankleL.rotation = _ankleR.rotation = transform.rotation;
        }

        // 澪の被弾姿勢を反映した後、手の位置を解く前に上体を合わせる。
        internal void BraceHoldUpperBody(float leanBack, float leanSide, float tuckChin, float yaw = 0f)
        {
            // ヒットストップ中は Step が止まるので、前フレームの補正には重ねない。
            _chest.localRotation = Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f + yaw, _torsoRoll)
                * Quaternion.Euler(-leanBack, 0f, leanSide);
            _head.localRotation = Quaternion.Euler(-_headPitch * 0.5f, -_torsoYaw * 0.25f, 0f)
                * Quaternion.Euler(tuckChin, 0f, leanSide * 0.5f);
        }

    }
}
