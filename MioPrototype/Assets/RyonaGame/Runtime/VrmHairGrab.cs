using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引き起こしで髪を掴まれたとき、掴んだ手に近い毛束だけを少し寄せる。
    ///
    /// VRM の髪はスプリングボーン（<see cref="VRM10SpringBoneJoint"/>、名前が HairJoint...）で、
    /// FK ではない。根を大きく回すと長い髪が消えて見えるので、手の近くのジョイントだけ動かす。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Hair Grab")]
    [DefaultExecutionOrder(26000)]
    public sealed class VrmHairGrab : MonoBehaviour
    {
        readonly List<Transform> _tips = new List<Transform>();

        Vrm10Instance _vrm;
        PlayerStats _player;
        bool _ready;

        void Start() => FindJoints();

        void LateUpdate()
        {
            if (!_ready)
            {
                FindJoints();
                if (!_ready)
                    return;
            }

            var haul = ResolveHaul();
            if (haul == null || !haul.Active || haul.Stage < GroundHaulUp.Beat.Grab)
                return;

            float weight = Mathf.Clamp01(haul.Grip);
            if (weight <= 0.001f)
                return;

            Vector3 hand = haul.GrabHand;
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            for (int i = 0; i < _tips.Count; i++)
            {
                Transform tip = _tips[i];
                if (tip == null)
                    continue;

                float distance = Vector3.Distance(tip.position, hand);
                float near = Mathf.Clamp01(1f - distance / 0.20f);
                if (near <= 0.001f)
                    continue;

                Vector3 delta = hand - tip.position;
                tip.position += delta * (weight * near * k * 0.35f);
            }
        }

        GroundHaulUp ResolveHaul()
        {
            if (_player == null)
                _player = GetComponentInParent<PlayerStats>();
            if (_player == null || _player.GroundMount == null)
                return null;
            return _player.GroundMount.Haul;
        }

        void FindJoints()
        {
            if (_vrm == null)
                _vrm = GetComponent<Vrm10Instance>();
            if (_vrm == null)
                return;

            var joints = GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            if (joints == null || joints.Length == 0)
                return;

            _tips.Clear();
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (joint != null &&
                    joint.name.IndexOf("HairJoint", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _tips.Add(joint.transform);
            }

            _ready = _tips.Count > 0;
            if (_ready)
                GameLog.Log($"[HairGrab] 毛束のジョイント {_tips.Count} 本");
        }
    }
}
