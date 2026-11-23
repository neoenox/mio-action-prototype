using UnityEngine;
using UniVRM10;

namespace Ryona.Game
{
    /// <summary>
    /// 髪を重力で垂れやすくする。VRM のスプリング設定が硬すぎて板のように見えるため、
    /// 通常時は剛性と空気抵抗を緩めて重力に従わせる。倒れている間・顔面パン中だけ
    /// 前髪が顔を覆わないよう重力を弱める（従来どおり）。
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

        [Range(0f, 1f)]
        [Tooltip("顔面パンのコンボ中の重力の割合。前髪が落ちて顔を覆うのを防ぐ")]
        public float FaceComboGravity = 0.35f;

        [Range(0.1f, 1f)]
        [Tooltip("スプリングの剛性の倍率。小さいほど重力で垂れる（1 が VRM 元設定）")]
        public float StiffnessScale = 0.60f;

        [Range(0.1f, 1f)]
        [Tooltip("空気抵抗の倍率。小さいほど揺れが残る（1 が VRM 元設定）")]
        public float DragScale = 0.75f;

        Vrm10Instance _vrm;
        CharacterAnimator _anim;
        HeldBellyPunchDirector _punch;
        VRM10SpringBoneJoint[] _joints;
        float[] _originalGravity;
        float[] _originalStiffness;
        float[] _originalDrag;

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

            // 顔面パンのコンボ中は、前髪が落ちて顔を覆うのを避けるため重力を弱める。
            if (_punch == null)
                _punch = FindFirstObjectByType<HeldBellyPunchDirector>();
            bool faceCombo = _punch != null && _punch.isActiveAndEnabled && _punch.IsFaceCombo;

            float want = faceCombo ? FaceComboGravity : down ? DownGravity : 1f;

            if (Mathf.Abs(want - _applied) < 0.01f)
                return;

            bool snap = want < 0.99f;
            _applied = want;
            Apply(want, snap);
        }

        void Cache()
        {
            _joints = GetComponentsInChildren<VRM10SpringBoneJoint>(true);
            _originalGravity = new float[_joints.Length];
            _originalStiffness = new float[_joints.Length];
            _originalDrag = new float[_joints.Length];
            for (int i = 0; i < _joints.Length; i++)
            {
                var joint = _joints[i];
                if (joint == null)
                    continue;
                _originalGravity[i] = joint.m_gravityPower;
                _originalStiffness[i] = joint.m_stiffnessForce;
                _originalDrag[i] = joint.m_dragForce;
            }
        }

        void Apply(float scale, bool snap)
        {
            var runtime = _vrm.Runtime.SpringBone;
            if (runtime == null)
                return;

            for (int i = 0; i < _joints.Length; i++)
            {
                if (_joints[i] != null)
                    _joints[i].m_gravityPower = _originalGravity[i] * scale;
                    _joints[i].m_stiffnessForce = _originalStiffness[i] * StiffnessScale;
                    _joints[i].m_dragForce = _originalDrag[i] * DragScale;
            }

            // バッファは作り直さないと値が反映されない
            runtime.ReconstructSpringBone();

            // 垂れ切った姿勢からではなく、初期姿勢（立ち姿勢の髪）から始める
            if (snap)
                runtime.RestoreInitialTransform();

            GameLog.Log($"[Hair] gravity x{scale:F2} stiffness x{StiffnessScale:F2} " +
                        $"drag x{DragScale:F2} joints={_joints.Length} snap={snap}");
        }
    }
}
