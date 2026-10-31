using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// VRM は T ポーズのまま出力されるので、腕を下ろして立たせる。
    /// ボーンの向きに依存しない方法（ベクトルを曲げる）でやっているので、
    /// VRoid 以外のモデルでもそのまま動く。
    /// </summary>
    [AddComponentMenu("Ryona/Idle Pose")]
    public sealed class IdlePose : MonoBehaviour
    {
        [Header("Arms（肩から肘・肘から手首の向き）")]
        public Vector3 LeftArm = new Vector3(0.22f, -1f, 0.03f);
        public Vector3 LeftForearm = new Vector3(0.08f, -1f, 0.14f);

        [Header("Breathing")]
        public bool Breathing = true;
        public float BreathDegrees = 0.9f;
        public float BreathSpeed = 1.15f;

        Animator _animator;
        Transform _chest;
        Quaternion _chestBase;
        bool _posed;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
        }

        void LateUpdate()
        {
            if (!_posed)
            {
                Pose();
                _posed = true;
            }

            if (Breathing && _chest != null)
            {
                float angle = Mathf.Sin(Time.time * BreathSpeed) * BreathDegrees;
                _chest.localRotation = _chestBase * Quaternion.Euler(angle, 0f, 0f);
            }
        }

        /// <summary>腕を下ろす。編集モードからも呼べる。</summary>
        public void Pose()
        {
            _animator ??= GetComponentInChildren<Animator>();

            var lUpper = Find(HumanBodyBones.LeftUpperArm, "J_Bip_L_UpperArm");
            var lLower = Find(HumanBodyBones.LeftLowerArm, "J_Bip_L_LowerArm");
            var lHand = Find(HumanBodyBones.LeftHand, "J_Bip_L_Hand");
            var rUpper = Find(HumanBodyBones.RightUpperArm, "J_Bip_R_UpperArm");
            var rLower = Find(HumanBodyBones.RightLowerArm, "J_Bip_R_LowerArm");
            var rHand = Find(HumanBodyBones.RightHand, "J_Bip_R_Hand");

            Vector3 leftArm = LeftArm.normalized;
            Vector3 leftForearm = LeftForearm.normalized;
            Vector3 rightArm = new Vector3(-leftArm.x, leftArm.y, leftArm.z);
            Vector3 rightForearm = new Vector3(-leftForearm.x, leftForearm.y, leftForearm.z);

            Point(lUpper, lLower, leftArm);
            Point(lLower, lHand, leftForearm);
            Point(rUpper, rLower, rightArm);
            Point(rLower, rHand, rightForearm);

            _chest = Find(HumanBodyBones.Chest, "J_Bip_C_Chest");
            if (_chest != null)
                _chestBase = _chest.localRotation;
        }

        static void Point(Transform bone, Transform tip, Vector3 worldDirection)
        {
            if (bone == null || tip == null)
                return;

            Vector3 current = tip.position - bone.position;
            if (current.sqrMagnitude < 1e-8f)
                return;

            Quaternion delta = Quaternion.FromToRotation(current.normalized, worldDirection);
            bone.rotation = delta * bone.rotation;
        }

        Transform Find(HumanBodyBones id, string fallbackName)
        {
            if (_animator != null && _animator.isHuman)
            {
                var t = _animator.GetBoneTransform(id);
                if (t != null)
                    return t;
            }

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == fallbackName)
                    return t;
            }

            return null;
        }
    }
}
