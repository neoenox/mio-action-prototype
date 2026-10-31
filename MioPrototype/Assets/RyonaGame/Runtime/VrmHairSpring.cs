using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// 倒れている間だけ髪の重力を弱める。
    ///
    /// 横向きに倒すと、重力で前髪が顔の上に垂れてきて顔が隠れる。
    /// 重力を弱めて初期姿勢に戻すと、立ち姿勢と同じ髪の形のまま残るので顔が見える。
    /// 立ち上がると元の重力に戻る。
    /// </summary>
    [AddComponentMenu("Ryona/VRM Hair Spring")]
    public sealed class VrmHairSpring : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("倒れている間の重力の割合。0 で無重力（立ち姿勢の髪のまま）")]
        public float DownGravity = 0.1f;

        Vrm10Instance _vrm;
        CharacterAnimator _anim;
        VRM10SpringBoneJoint[] _joints;
        float[] _original;

        float _applied = -1f;

        void Awake()
        {
            _vrm = GetComponent<Vrm10Instance>();
            _anim = GetComponent<CharacterAnimator>();
        }

        void LateUpdate()
        {
            if (_vrm == null || _vrm.Runtime == null)
                return;

            if (_joints == null || _joints.Length == 0)
                Cache();

            if (_joints == null || _joints.Length == 0)
                return;

            bool down = _anim != null && _anim.Current == CharacterAnimator.State.Down;
            float want = down ? DownGravity : 1f;

            if (Mathf.Abs(want - _applied) < 0.01f)
                return;

            bool snap = want < 0.99f;
            _applied = want;
            Apply(want, snap);
        }

        void Cache()
        {
            _joints = GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            _original = new float[_joints.Length];
            for (int i = 0; i < _joints.Length; i++)
                _original[i] = _joints[i] != null ? _joints[i].m_gravityPower : 0f;
        }

        void Apply(float scale, bool snap)
        {
            var runtime = _vrm.Runtime.SpringBone;
            if (runtime == null)
                return;

            for (int i = 0; i < _joints.Length; i++)
            {
                if (_joints[i] != null)
                    _joints[i].m_gravityPower = _original[i] * scale;
            }

            // バッファは作り直さないと値が反映されない
            runtime.ReconstructSpringBone();

            // 垂れ切った姿勢からではなく、初期姿勢（立ち姿勢の髪）から始める
            if (snap)
                runtime.RestoreInitialTransform();

            Debug.Log($"[Hair] gravity x{scale:F2} joints={_joints.Length} snap={snap}");
        }
    }
}
