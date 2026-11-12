using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator : MonoBehaviour
    {
        /// <summary>
        /// 「腕を外へ開く向き」を骨の実際の位置から求める。
        /// モデルによって左右が +X のことも -X のこともあるので、決め打ちにしない。
        /// </summary>
        void CacheArmAxes()
        {
            if (Rig == null || Rig.Hips == null || Rig.LUpperArm == null || Rig.RUpperArm == null)
                return;

            // ワールドの向きで覚えると、被弾で体が回ったときに「外へ」が付いてこず、
            // 腕だけ元の向きを向いたままになる。キャラクターのローカルに直して覚える。
            _leftOut = Horizontal(
                transform.InverseTransformDirection(Rig.LUpperArm.position - Rig.Hips.position), Vector3.left);
            _rightOut = Horizontal(
                transform.InverseTransformDirection(Rig.RUpperArm.position - Rig.Hips.position), Vector3.right);
            _axesReady = true;
        }

        static Vector3 Horizontal(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? fallback : v.normalized;
        }

        /// <summary>
        /// 仕様 (開き, 高さ, 前後) を、このモデルの実際の向きに変換する。
        /// x は必ず「外へ」の大きさとして扱う。
        /// </summary>
        Vector3 ArmDirection(bool left, Vector3 spec)
        {
            if (!_axesReady)
                CacheArmAxes();

            Vector3 side = transform.TransformDirection(left ? _leftOut : _rightOut);
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;

            Vector3 dir = side * Mathf.Abs(spec.x) + Vector3.up * spec.y + forward * spec.z;

            // 腕は肩の面に付いている。体幹がねじれる・傾くと肩も一緒に回るので、
            // 腕の狙いも同じだけ回す。これをやらないと、強くねじる姿勢（のけぞり・
            // 振りかぶり）で「肩から先だけ別の場所にある」画になる。
            // BendTorso は Spine に yaw/pitch/roll を、Chest にその 0.4 倍を掛けるので、
            // 肩の面は合わせて 1.4 倍回っている。
            const float shoulderShare = 1.4f;

            // 攻撃だけ追従を弱める。攻撃の腕は「踏み込んで拳を相手へ出す」形として
            // すでに体のひねり込みで作ってあるので、肩の面に合わせて回すと
            // 拳が体の前へ回り込んで伸びが減る（当てにいっている画にならない）。
            float share = _state == State.Attack ? 0.35f : 1f;

            Quaternion torso =
                Quaternion.AngleAxis(_armFollowYaw * shoulderShare * share, Vector3.up) *
                Quaternion.AngleAxis(_armFollowPitch * shoulderShare * share, Vector3.right) *
                Quaternion.AngleAxis(_armFollowRoll * shoulderShare * share, Vector3.forward);

            // BendTorso と同じく、軸はキャラクターの向きを基準に取る。
            Quaternion basis = transform.rotation;
            dir = basis * torso * Quaternion.Inverse(basis) * dir;

            return dir;
        }

        // このフレームの体幹角度。腕の狙いを肩の面に合わせるのに使う。
        float _armFollowYaw;
        float _armFollowPitch;
        float _armFollowRoll;

        /// <summary>検証用: 直近の ApplyPose で SetArm に渡した向き（ワールド）。</summary>
        [System.NonSerialized] public Vector3 DebugArmLUpper;
        [System.NonSerialized] public Vector3 DebugArmLFore;
        [System.NonSerialized] public Vector3 DebugArmRUpper;
        [System.NonSerialized] public Vector3 DebugArmRFore;
        [System.NonSerialized] public float DebugTorsoPitch;
        [System.NonSerialized] public float DebugHeadPitch;
        [System.NonSerialized] public float DebugTorsoYaw;
        [System.NonSerialized] public float DebugHeadYaw;

        /// <summary>
        /// プレビュー専用: 被弾の場所（顔 / 腹）。設定すると <see cref="Stats"/> より優先する。
        /// Editor の診断や PlayMode テストはこの経路で顔 / 腹の分岐を確かめる
        /// （実機では常に null なので、ふだんは <see cref="PlayerStats.LastHitZone"/> を見る）。
        /// </summary>
        [System.NonSerialized] public PunchZone? PreviewHitZone;

        /// <summary>
        /// いま使っている被弾の場所。プレビュー / テストで指定があればそれを優先し、
        /// 無ければ実機の <see cref="PlayerStats.LastHitZone"/>（投げなどでは null）。
        /// </summary>
        public PunchZone? CurrentHitZone =>
            PreviewHitZone ?? (Stats != null ? Stats.LastHitZone : null);

    }
}
