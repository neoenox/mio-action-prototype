using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 引き起こしで髪を掴まれたとき、髪のスプリングボーンの根を上へ引き上げる。
    ///
    /// VRM の髪は物理（スプリングボーン）なので、手を当てただけでは動かない。
    /// 掴んでいる間だけ毛束の根を掴む方向へ回し、離すとシミュレーションへ戻す。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Hair Grab")]
    [DefaultExecutionOrder(26000)]
    public sealed class VrmHairGrab : MonoBehaviour
    {
        readonly List<Transform> _roots = new List<Transform>();
        readonly List<Quaternion> _rest = new List<Quaternion>();

        Vrm10Instance _vrm;
        PlayerStats _player;
        bool _ready;

        void Start() => FindRoots();

        void LateUpdate()
        {
            if (!_ready)
            {
                FindRoots();
                if (!_ready)
                    return;
            }

            var haul = ResolveHaul();
            if (haul == null || !haul.Active || haul.Stage < GroundHaulUp.Beat.Grab)
                return;

            float weight = Mathf.Clamp01(haul.Grip);
            if (weight <= 0.001f)
                return;

            float yank = Mathf.Clamp01(haul.Yank);
            float angle = (24f + 16f * yank) * weight;

            for (int i = 0; i < _roots.Count; i++)
            {
                Transform root = _roots[i];
                if (root == null || root.parent == null)
                    continue;

                // 毛束の根を、親（頭）のローカルで世界の右軸回りに回す。
                Vector3 axis = root.parent.InverseTransformDirection(root.right);
                if (axis.sqrMagnitude < 1e-5f)
                    continue;

                float jitter = Mathf.Sin(Time.unscaledTime * 17f + i * 1.7f) * 3f * yank;
                Quaternion target = Quaternion.AngleAxis(-(angle + jitter), axis.normalized) * _rest[i];
                root.localRotation = Quaternion.Slerp(root.localRotation, target, 0.35f);
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

        void FindRoots()
        {
            if (_vrm == null)
                _vrm = GetComponent<Vrm10Instance>();
            if (_vrm == null)
                return;

            var joints = GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            if (joints == null || joints.Length == 0)
                return;

            _roots.Clear();
            _rest.Clear();
            for (int i = 0; i < joints.Length; i++)
            {
                var joint = joints[i];
                if (joint == null || joint.name.IndexOf("HairJoint", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Transform parent = joint.transform.parent;
                if (parent != null && parent.GetComponent<VRM10SpringBoneJoint>() != null)
                    continue;      // 毛束の途中ではなく根だけ

                _roots.Add(joint.transform);
                _rest.Add(joint.transform.localRotation);
            }

            _ready = _roots.Count > 0;
            if (_ready)
                GameLog.Log($"[HairGrab] 毛束の根 {_roots.Count} 本");
        }
    }
}
