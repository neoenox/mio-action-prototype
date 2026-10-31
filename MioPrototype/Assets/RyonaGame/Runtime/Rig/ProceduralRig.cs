using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// ボーンを「向き」で操作するための薄い層。
    ///
    /// 回転軸を決め打ちにするとモデルごとに破綻するので、
    /// 「この骨を、この方向へ向ける」「この足を、この位置へ置く」という
    /// ベクトル指定だけでポーズを作れるようにしてある。
    /// </summary>
    public sealed class ProceduralRig : MonoBehaviour
    {
        public Transform Hips { get; private set; }
        public Transform Spine { get; private set; }
        public Transform Chest { get; private set; }
        public Transform Neck { get; private set; }
        public Transform Head { get; private set; }

        public Transform LUpperArm { get; private set; }
        public Transform LLowerArm { get; private set; }
        public Transform LHand { get; private set; }
        public Transform RUpperArm { get; private set; }
        public Transform RLowerArm { get; private set; }
        public Transform RHand { get; private set; }

        public Transform LUpperLeg { get; private set; }
        public Transform LLowerLeg { get; private set; }
        public Transform LFoot { get; private set; }
        public Transform RUpperLeg { get; private set; }
        public Transform RLowerLeg { get; private set; }
        public Transform RFoot { get; private set; }

        /// <summary>足の指の付け根（かかとを上げるときの支点）。</summary>
        public Transform LToe { get; private set; }
        public Transform RToe { get; private set; }

        public bool Ready { get; private set; }
        public float LegLength => _legLength;
        public float RestHipHeight => _restHipHeight;

        /// <summary>足の初期位置（キャラクターのローカル座標）。</summary>
        public Vector3 RestFootL { get; private set; }
        public Vector3 RestFootR { get; private set; }
        public Vector3 RestHips { get; private set; }

        readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> _rest =
            new Dictionary<Transform, (Vector3, Quaternion)>();

        readonly Dictionary<Transform, Quaternion> _snapshot =
            new Dictionary<Transform, Quaternion>();

        Vector3 _snapshotHips;

        Animator _animator;
        float _legLength;
        float _restHipHeight;

        void Awake()
        {
            Capture();
        }

        public void Capture()
        {
            _animator ??= GetComponentInChildren<Animator>();

            Hips = Bone(HumanBodyBones.Hips, "J_Bip_C_Hips");
            Spine = Bone(HumanBodyBones.Spine, "J_Bip_C_Spine");
            Chest = Bone(HumanBodyBones.Chest, "J_Bip_C_Chest");
            Neck = Bone(HumanBodyBones.Neck, "J_Bip_C_Neck");
            Head = Bone(HumanBodyBones.Head, "J_Bip_C_Head");

            LUpperArm = Bone(HumanBodyBones.LeftUpperArm, "J_Bip_L_UpperArm");
            LLowerArm = Bone(HumanBodyBones.LeftLowerArm, "J_Bip_L_LowerArm");
            LHand = Bone(HumanBodyBones.LeftHand, "J_Bip_L_Hand");
            RUpperArm = Bone(HumanBodyBones.RightUpperArm, "J_Bip_R_UpperArm");
            RLowerArm = Bone(HumanBodyBones.RightLowerArm, "J_Bip_R_LowerArm");
            RHand = Bone(HumanBodyBones.RightHand, "J_Bip_R_Hand");

            LUpperLeg = Bone(HumanBodyBones.LeftUpperLeg, "J_Bip_L_UpperLeg");
            LLowerLeg = Bone(HumanBodyBones.LeftLowerLeg, "J_Bip_L_LowerLeg");
            LFoot = Bone(HumanBodyBones.LeftFoot, "J_Bip_L_Foot");
            RUpperLeg = Bone(HumanBodyBones.RightUpperLeg, "J_Bip_R_UpperLeg");
            RLowerLeg = Bone(HumanBodyBones.RightLowerLeg, "J_Bip_R_LowerLeg");
            RFoot = Bone(HumanBodyBones.RightFoot, "J_Bip_R_Foot");

            // つま先（足の指の付け根＝ボール）。かかとを上げるときの支点に使う。
            LToe = Bone(HumanBodyBones.LeftToes, "J_Bip_L_ToeBase");
            RToe = Bone(HumanBodyBones.RightToes, "J_Bip_R_ToeBase");

            Ready = Hips != null && LUpperLeg != null && LFoot != null;

            if (!Ready)
            {
                Debug.LogError("[Ryona] ヒューマノイドボーンが見つかりません。");
                return;
            }

            _rest.Clear();
            foreach (var t in All())
            {
                if (t != null)
                    _rest[t] = (t.localPosition, t.localRotation);
            }

            _restHipHeight = Hips.position.y - transform.position.y;

            if (LUpperLeg != null && LLowerLeg != null && LFoot != null)
            {
                _legLength = Vector3.Distance(LUpperLeg.position, LLowerLeg.position)
                             + Vector3.Distance(LLowerLeg.position, LFoot.position);
            }

            RestFootL = transform.InverseTransformPoint(LFoot.position);
            RestFootR = transform.InverseTransformPoint(RFoot.position);
            RestHips = transform.InverseTransformPoint(Hips.position);

            CaptureFingers();
            CaptureFlexAxes();
            CaptureElbowHinges();
        }

        /// <summary>
        /// 肘の蝶番軸を上腕のローカル座標で求める。
        /// 肘は前腕が体の前（+Z）へ曲がるのが正常なので、軸 = 上腕の向き × 体の前。
        /// これを基準に上腕をひねると、どの姿勢でも肘が同じ面で曲がる。
        /// </summary>
        void CaptureElbowHinges()
        {
            _elbowHingeLocalL = Vector3.zero;
            _elbowHingeLocalR = Vector3.zero;

            if (LUpperArm == null || LLowerArm == null || RUpperArm == null || RLowerArm == null)
                return;

            Vector3 armL = (LLowerArm.position - LUpperArm.position).normalized;
            Vector3 armR = (RLowerArm.position - RUpperArm.position).normalized;

            Vector3 hingeL = Vector3.Cross(armL, Vector3.forward);
            Vector3 hingeR = Vector3.Cross(armR, Vector3.forward);

            if (hingeL.sqrMagnitude > 1e-6f)
                _elbowHingeLocalL = Quaternion.Inverse(LUpperArm.rotation) * hingeL.normalized;

            if (hingeR.sqrMagnitude > 1e-6f)
                _elbowHingeLocalR = Quaternion.Inverse(RUpperArm.rotation) * hingeR.normalized;

            Debug.Log($"[Rig] elbow hinge L=({_elbowHingeLocalL.x:F2},{_elbowHingeLocalL.y:F2},{_elbowHingeLocalL.z:F2})" +
                $" R=({_elbowHingeLocalR.x:F2},{_elbowHingeLocalR.y:F2},{_elbowHingeLocalR.z:F2})");
        }

        IEnumerable<Transform> All()
        {
            yield return Hips;
            yield return Spine;
            yield return Chest;
            yield return Neck;
            yield return Head;
            yield return LUpperArm;
            yield return LLowerArm;
            yield return LHand;
            yield return RUpperArm;
            yield return RLowerArm;
            yield return RHand;
            yield return LUpperLeg;
            yield return LLowerLeg;
            yield return LFoot;
            yield return RUpperLeg;
            yield return RLowerLeg;
            yield return RFoot;
        }

        /// <summary>全ボーンを初期姿勢（Tポーズ）に戻す。</summary>
        public void ResetPose()
        {
            foreach (var kv in _rest)
            {
                if (kv.Key == null)
                    continue;
                kv.Key.localPosition = kv.Value.pos;
                kv.Key.localRotation = kv.Value.rot;
            }
        }

        /// <summary>
        /// いま当たっている姿勢を控える。状態が切り替わった瞬間に呼ぶと、
        /// 切り替え前の姿勢から新しい姿勢へ寄せられる（カクつき防止）。
        /// </summary>
        public void SnapshotPose()
        {
            _snapshot.Clear();

            foreach (var bone in All())
            {
                if (bone != null)
                    _snapshot[bone] = bone.localRotation;
            }

            if (Hips != null)
                _snapshotHips = Hips.position;
        }

        /// <summary>
        /// 控えた姿勢から、いまの姿勢へ寄せる。t=0 で控えた姿勢、t=1 で今の姿勢。
        /// </summary>
        public void BlendFromSnapshot(float t)
        {
            if (_snapshot.Count == 0)
                return;

            t = Mathf.Clamp01(t);

            foreach (var kv in _snapshot)
            {
                if (kv.Key != null)
                    kv.Key.localRotation = Quaternion.Slerp(kv.Value, kv.Key.localRotation, t);
            }

            if (Hips != null)
                Hips.position = Vector3.Lerp(_snapshotHips, Hips.position, t);
        }

        /// <summary>骨を、tip の方向が worldDirection を向くように回す。</summary>
        public static void Aim(Transform bone, Transform tip, Vector3 worldDirection)
        {
            if (bone == null || tip == null)
                return;

            Vector3 current = tip.position - bone.position;
            if (current.sqrMagnitude < 1e-8f || worldDirection.sqrMagnitude < 1e-8f)
                return;

            Quaternion delta = Quaternion.FromToRotation(current.normalized, worldDirection.normalized);
            bone.rotation = delta * bone.rotation;
        }

        /// <summary>
        /// 2ボーンIK。upper→mid→end を曲げて、end を target に置く。
        /// pole は関節が曲がる方向の目印。
        /// </summary>
        public static void SolveTwoBone(Transform upper, Transform mid, Transform end,
            Vector3 target, Vector3 pole, Vector3 fallbackNormal = default)
        {
            if (upper == null || mid == null || end == null)
                return;

            Vector3 root = upper.position;
            float l1 = Vector3.Distance(root, mid.position);
            float l2 = Vector3.Distance(mid.position, end.position);
            if (l1 < 1e-5f || l2 < 1e-5f)
                return;

            Vector3 toTarget = target - root;
            float rawDistance = toTarget.magnitude;
            if (rawDistance < 1e-5f)
                return;

            // 伸びきり／畳みきりに近づくと、膝（肘）の曲がる面が決まらなくなり、
            // 1 フレームで膝の向きが飛ぶ（足首がスナップして見える）。
            // 端を 2cm 余らせて、脚が完全な直線に入らないようにする。
            // 見た目では分からない差で、向きの計算だけが安定する。
            const float reachMargin = 0.02f;
            float distance = Mathf.Clamp(rawDistance,
                Mathf.Abs(l1 - l2) + reachMargin, l1 + l2 - reachMargin);
            Vector3 direction = toTarget / rawDistance;

            float cosAngle = (l1 * l1 + distance * distance - l2 * l2) / (2f * l1 * distance);
            float angle = Mathf.Acos(Mathf.Clamp(cosAngle, -1f, 1f));

            // 曲がる面。極と的の向きがほぼ同じ（＝足が真横・真前にある）ときは
            // 外積がほぼ 0 になり、正規化すると向きがノイズになる。
            // そのままだと膝が有り得ない方向へ飛ぶので、足の曲がる面（体の右）へ落とす。
            Vector3 poleDirection = pole - root;
            Vector3 normal = Vector3.Cross(direction, poleDirection);
            bool fallback = normal.sqrMagnitude
                < 1e-4f * direction.sqrMagnitude * Mathf.Max(poleDirection.sqrMagnitude, 1e-8f);
            if (fallback)
            {
                normal = fallbackNormal;
                if (normal.sqrMagnitude < 1e-6f)
                    normal = Vector3.Cross(direction, Vector3.up);
                if (normal.sqrMagnitude < 1e-6f)
                    normal = Vector3.Cross(direction, Vector3.right);
            }
            normal.Normalize();

            Vector3 upperDirection = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, normal) * direction;

            // 極が退化して代わりの軸を使ったときは、膝が極と反対側（＝体の後ろ）へ
            // 折れることがある。極の側へ曲がる向きを選び直す。
            if (fallback && Vector3.Dot(upperDirection, poleDirection) < 0f)
            {
                normal = -normal;
                upperDirection = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, normal) * direction;
            }

            // 上段を膝の位置へ
            Aim(upper, mid, upperDirection);
            // 下段を的へ
            Aim(mid, end, target - mid.position);
        }

        public bool SolveLeg(bool left, Vector3 worldTarget, Vector3 worldPole)
        {
            var upper = left ? LUpperLeg : RUpperLeg;
            var lower = left ? LLowerLeg : RLowerLeg;
            var foot = left ? LFoot : RFoot;
            if (upper == null || lower == null || foot == null)
                return false;

            SolveTwoBone(upper, lower, foot, worldTarget, worldPole, transform.right);
            return true;
        }

        /// <summary>
        /// かかとを上げる。つま先（ボール）を床に固定したまま、足首を回して持ち上げる。
        /// 足首を中心に回すとつま先が後ろへ滑るので、滑った分だけ足首の狙いを戻して解き直す。
        /// 脚の IK を解いたあとに呼ぶこと。
        /// </summary>
        public void RaiseHeel(bool left, float degrees, Vector3 worldPole)
        {
            var foot = left ? LFoot : RFoot;
            var toe = left ? LToe : RToe;
            if (foot == null || toe == null || Mathf.Abs(degrees) < 0.01f)
                return;

            // 足首の角度は毎回この 1 回ぶんだけにする（足すと周回ごとに角度が増える）。
            Quaternion baseRotation = foot.rotation;

            for (int i = 0; i < 3; i++)
            {
                Vector3 toeBefore = toe.position;
                foot.rotation = Quaternion.AngleAxis(degrees, transform.right) * baseRotation;

                // つま先が動いた分だけ足首を戻す（下へは潜らせない）
                Vector3 delta = toeBefore - toe.position;
                delta.y = Mathf.Max(0f, delta.y);
                if (delta.sqrMagnitude < 1e-6f)
                    return;

                Vector3 target = transform.InverseTransformPoint(foot.position)
                    + transform.InverseTransformVector(delta);
                SolveLeg(left, transform.TransformPoint(target), worldPole);
            }

            // 最後にもう一度、決めた角度に合わせる（解き直しで向きが戻るため）
            foot.rotation = Quaternion.AngleAxis(degrees, transform.right) * baseRotation;
        }

        /// <summary>
        /// 足首を「つま先が下がる」向きへ回す（＝かかとが上がる）。
        /// 脚の IK を解いたあとに呼ぶこと。先に呼ぶと脚の回転で上書きされる。
        /// </summary>
        public void PitchFoot(bool left, float degrees)
        {
            var foot = left ? LFoot : RFoot;
            if (foot == null || Mathf.Abs(degrees) < 0.01f)
                return;

            // 体の右軸（＝足の横軸）まわり。正でつま先が下がる。
            foot.rotation = Quaternion.AngleAxis(degrees, transform.right) * foot.rotation;
        }

        public void SetArm(bool left, Vector3 upperDirection, Vector3 forearmDirection,
            float handRollDegrees = 0f)
        {
            var upper = left ? LUpperArm : RUpperArm;
            var lower = left ? LLowerArm : RLowerArm;
            var hand = left ? LHand : RHand;

            Aim(upper, lower, upperDirection);
            AlignElbowHinge(upper, upperDirection, forearmDirection, left);
            Aim(lower, hand, forearmDirection);

            // 手のひらの向き（前腕の軸まわりのひねり）。
            // Aim は最小回転なので、手のひらが上を向いたままになる。攻撃のときは特に目立つ。
            if (Mathf.Abs(handRollDegrees) > 0.01f)
            {
                Vector3 axis = hand.position - lower.position;
                if (axis.sqrMagnitude > 1e-8f)
                    hand.rotation = Quaternion.AngleAxis(handRollDegrees, axis.normalized) * hand.rotation;
            }
        }

        /// <summary>
        /// 上腕を自分の軸まわりにひねり、肘が「前腕の曲がる面」へ来るようにする。
        /// Aim は最小回転なので上腕のひねりが不定になり、肘が逆側へ折れる姿勢が出る。
        /// 骨の位置は変えずに、ひねりだけを合わせる（見た目の位置はそのまま）。
        /// </summary>
        void AlignElbowHinge(Transform upper, Vector3 upperDirection, Vector3 forearmDirection, bool left)
        {
            Vector3 hingeLocal = left ? _elbowHingeLocalL : _elbowHingeLocalR;
            if (upper == null || hingeLocal == Vector3.zero)
                return;

            Vector3 up = upperDirection.normalized;
            Vector3 fore = forearmDirection.normalized;
            Vector3 want = Vector3.Cross(up, fore);

            // ほぼ真っ直ぐだと曲がる面が決まらないので、そのままにする。
            if (want.sqrMagnitude < 1e-4f)
                return;

            want.Normalize();

            Vector3 now = Vector3.ProjectOnPlane(upper.rotation * hingeLocal, up);
            Vector3 target = Vector3.ProjectOnPlane(want, up);
            if (now.sqrMagnitude < 1e-6f || target.sqrMagnitude < 1e-6f)
                return;

            float angle = Vector3.SignedAngle(now, target, up);
            upper.rotation = Quaternion.AngleAxis(angle, up) * upper.rotation;
        }

        /// <summary>
        /// 指を握る向きの保険値。通常は Capture 時に形態から求めた軸を使う。
        /// ボーンのローカル軸は VRM ごとに違うので、固定値は最後の手段。
        /// </summary>
        public Vector3 LeftFingerAxis = new Vector3(0f, 0f, 1f);
        public Vector3 RightFingerAxis = new Vector3(0f, 0f, -1f);

        /// <summary>
        /// 指を握らせる。曲げる軸はモデルの形から求めた値を使う。
        /// </summary>
        public void CurlFingers(bool left, float amount)
        {
            if (_animator == null)
                return;

            amount = Mathf.Clamp01(amount);
            var bones = left ? LeftFingerBones : RightFingerBones;

            for (int i = 0; i < bones.Length; i++)
                CurlOne(bones[i], CurlDegrees[i % 3] * amount, left);

            // 親指は向きが違うので控えめに。ただし緩すぎると拳から親指だけ
            // 突き出て「猫の手」に見えるので、指の根元と同じくらいまで曲げる。
            var thumb = left ? LeftThumbBones : RightThumbBones;
            for (int i = 0; i < thumb.Length; i++)
                CurlOne(thumb[i], 65f * amount, left);

            AdductThumb(left, amount);
            AdductFingers(left, amount);
        }

        /// <summary>
        /// 親指を人差し指の中節へ向ける（拳の前に親指を載せる）。
        /// 角度で回すと向きの誤差が出やすいので、狙い点へ向けてから Slerp で混ぜる。
        /// 曲げだけだと親指が拳の外へ突き出て「猫の手」に見える。
        /// </summary>
        void AdductThumb(bool left, float amount)
        {
            if (amount <= 0.05f || _animator == null)
                return;

            var thumbBones = left ? LeftThumbBones : RightThumbBones;
            var prox = _animator.GetBoneTransform(thumbBones[0]);
            var dist = _animator.GetBoneTransform(thumbBones[2]);
            var indexMid = _animator.GetBoneTransform(
                left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);

            if (prox == null || dist == null || indexMid == null || prox.parent == null)
                return;

            Vector3 palm = PalmNormalWorld(left);
            Vector3 target = indexMid.position + palm * 0.006f;

            Vector3 cur = dist.position - prox.position;
            Vector3 want = target - prox.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f)
                return;

            Quaternion delta = Quaternion.FromToRotation(cur.normalized, want.normalized);
            Quaternion aimedWorld = delta * prox.rotation;
            Quaternion aimedLocal = Quaternion.Inverse(prox.parent.rotation) * aimedWorld;

            prox.localRotation = Quaternion.Slerp(
                prox.localRotation, aimedLocal, Mathf.Clamp01(ThumbAim * amount));
        }

        /// <summary>
        /// 指を中指へ寄せる（開きを閉じる）。
        /// 曲げるだけだと指の間が開いたままで「かぎ爪」に見えるので、
        /// 手のひらの法線まわりに少しだけ内側へ回す。
        /// </summary>
        void AdductFingers(bool left, float amount)
        {
            if (amount <= 0.1f || _animator == null)
                return;

            if (_fingerAdductAxis.Count == 0)
                return;

            var fingerBones = left ? LeftFingerBones : RightFingerBones;

            // 小指ほど強く寄せる（開きが大きい）
            var targets = new[]
            {
                (start: 0, degrees: 10f),   // 人差し指
                (start: 6, degrees: 9f),    // 薬指
                (start: 9, degrees: 16f),   // 小指
            };

            foreach (var (start, degrees) in targets)
            {
                var prox = _animator.GetBoneTransform(fingerBones[start]);
                if (prox == null
                    || !_fingerAdductAxis.TryGetValue(prox, out var axis)
                    || !_fingerRest.TryGetValue(prox, out var rest))
                {
                    continue;
                }

                // 寄せる向きはリスト姿勢で決めた固定軸。曲げた後の形から毎回決めると、
                // 指先が手のひら側へ入っているせいで符号が反転し、指が外へ跳ねる。
                Quaternion delta = rest * Quaternion.AngleAxis(degrees * amount, axis)
                                   * Quaternion.Inverse(rest);
                prox.localRotation = delta * prox.localRotation;
            }
        }

        /// <summary>
        /// 指を握らせる。amount 0 = 開いた手、1 = 握り拳。
        /// VRM の指ボーンの曲がる軸はモデル依存なので axis で指定する。
        /// </summary>
        public void CurlFingers(bool left, float amount, Vector3 axis)
        {
            if (_animator == null)
                return;

            amount = Mathf.Clamp01(amount);
            var bones = left ? LeftFingerBones : RightFingerBones;

            for (int i = 0; i < bones.Length; i++)
                CurlOne(bones[i], CurlDegrees[i % 3] * amount, axis);

            // 親指は向きが違うので控えめに。ただし緩すぎると拳から親指だけ
            // 突き出て「猫の手」に見えるので、指の根元と同じくらいまで曲げる。
            var thumb = left ? LeftThumbBones : RightThumbBones;
            for (int i = 0; i < thumb.Length; i++)
                CurlOne(thumb[i], 52f * amount, axis);

            AdductThumb(left, amount);
        }

        void CurlOne(HumanBodyBones bone, float degrees, bool left)
        {
            var t = _animator.GetBoneTransform(bone);
            if (t == null || !_fingerRest.TryGetValue(t, out _))
                return;

            // Capture で形態から求めた軸。取れなかったときだけ固定値へ落とす。
            Vector3 axis = _fingerAxis.TryGetValue(t, out var computed)
                ? computed
                : (left ? LeftFingerAxis : RightFingerAxis);

            CurlOne(bone, degrees, axis);
        }

        void CurlOne(HumanBodyBones bone, float degrees, Vector3 axis)
        {
            var t = _animator.GetBoneTransform(bone);
            if (t == null || !_fingerRest.TryGetValue(t, out var rest))
                return;

            // 毎フレーム加算すると閉じ続けるので、初期姿勢から絶対値で作る
            t.localRotation = rest * Quaternion.AngleAxis(degrees, axis);
        }

        readonly Dictionary<Transform, Quaternion> _fingerRest = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, Vector3> _fingerAxis = new Dictionary<Transform, Vector3>();
        readonly Dictionary<Transform, Vector3> _fingerAdductAxis = new Dictionary<Transform, Vector3>();

        // 脚を曲げる軸（体の右）を、各ボーンの初期姿勢のローカルへ落としたもの。
        readonly Dictionary<Transform, Vector3> _flexAxis = new Dictionary<Transform, Vector3>();

        // リスト姿勢での手のひらの法線（ワールド）。指を寄せる向きに使う。
        Vector3 _palmNormalL;
        Vector3 _palmNormalR;
        Vector3 _palmLocalL;
        Vector3 _palmLocalR;

        // 肘の蝶番軸（上腕のローカル）。肘が曲がる面を揃えるのに使う。
        Vector3 _elbowHingeLocalL;
        Vector3 _elbowHingeLocalR;

        /// <summary>
        /// 掌の法線（ワールド）。掌が向いている側を指す。左右で同じ意味になるよう符号を揃えてある。
        /// </summary>
        public Vector3 PalmNormalWorld(bool left)
        {
            var hand = left ? LHand : RHand;
            Vector3 local = left ? _palmLocalL : _palmLocalR;
            if (hand == null || local == Vector3.zero)
                return Vector3.zero;

            return hand.TransformDirection(local);
        }

        void CaptureFingers()
        {
            _fingerRest.Clear();
            _fingerAxis.Clear();
            _fingerAdductAxis.Clear();
            _palmNormalL = Vector3.zero;
            _palmNormalR = Vector3.zero;
            _palmLocalL = Vector3.zero;
            _palmLocalR = Vector3.zero;

            foreach (var set in new[] { LeftFingerBones, RightFingerBones, LeftThumbBones, RightThumbBones })
            {
                foreach (var bone in set)
                {
                    var t = _animator.GetBoneTransform(bone);
                    if (t != null)
                        _fingerRest[t] = t.localRotation;
                }
            }

            CaptureFingerAxis(true);
            CaptureFingerAxis(false);
        }

        /// <summary>
        /// 指を曲げる軸を、リスト姿勢の形から求める。
        /// 軸 = (手首→指の付け根) × (手のひらの法線) を各指ボーンのローカルへ変換する。
        /// ボーンのローカル軸は VRM ごと・左右ごとに違うので、固定値を書かずに
        /// 形から出したほうが、どのモデルでも同じように拳になる。
        /// </summary>
        void CaptureFingerAxis(bool left)
        {
            if (_animator == null)
                return;

            var hand = left ? LHand : RHand;
            var fingerBones = left ? LeftFingerBones : RightFingerBones;
            var thumbBones = left ? LeftThumbBones : RightThumbBones;
            if (hand == null || fingerBones.Length < 6 || thumbBones.Length < 3)
                return;

            var indexProx = _animator.GetBoneTransform(fingerBones[0]);
            var midProx = _animator.GetBoneTransform(fingerBones[3]);
            var midDist = _animator.GetBoneTransform(fingerBones[5]);
            var thumbProx = _animator.GetBoneTransform(thumbBones[0]);
            var thumbDist = _animator.GetBoneTransform(thumbBones[2]);
            if (midProx == null || midDist == null || thumbProx == null)
                return;

            // 手のひらの法線（指の付け根の向き × 親指の向き）
            Vector3 toFingers = midProx.position - hand.position;
            Vector3 toThumb = thumbProx.position - hand.position;
            if (toFingers.sqrMagnitude < 1e-8f || toThumb.sqrMagnitude < 1e-8f)
                return;

            Vector3 palmNormal = Vector3.Cross(toFingers.normalized, toThumb.normalized);
            if (palmNormal.sqrMagnitude < 1e-6f)
                return;
            palmNormal.Normalize();

            // cross(指, 親指) は左右で符号が反転する。T ポーズの掌は下向きなので、
            // 法線が下を向く側に揃える（これで「掌がどちらを向いているか」が左右で同じ意味になる）。
            if (Vector3.Dot(palmNormal, Vector3.down) < 0f)
                palmNormal = -palmNormal;

            if (left)
            {
                _palmNormalL = palmNormal;
                _palmLocalL = hand.InverseTransformDirection(palmNormal);
            }
            else
            {
                _palmNormalR = palmNormal;
                _palmLocalR = hand.InverseTransformDirection(palmNormal);
            }

            // 指は手のひら側へ巻く = 指の向きと法線の外積まわりに回す
            Vector3 fingerAxisWorld = Vector3.Cross(toFingers.normalized, palmNormal).normalized;

            // 符号は「中指の先が手首へ近づく向き」を採る（左右の鏡像を吸収する）
            fingerAxisWorld = PickFlexionSign(midProx, midDist, hand.position, fingerAxisWorld);
            foreach (var bone in fingerBones)
                StoreAxis(bone, fingerAxisWorld);

            // 親指は付け根の向きが違うので、自分の向きから軸を作り、
            // 「人差し指の付け根へ近づく向き」を正とする（対立運動）。
            Vector3 thumbAxisWorld = Vector3.Cross(toThumb.normalized, palmNormal);
            if (thumbAxisWorld.sqrMagnitude > 1e-6f)
            {
                thumbAxisWorld.Normalize();
                Vector3 toward = indexProx != null ? indexProx.position : hand.position;
                if (thumbDist != null)
                    thumbAxisWorld = PickFlexionSign(thumbProx, thumbDist, toward, thumbAxisWorld);
                else if (Vector3.Dot(thumbAxisWorld, fingerAxisWorld) < 0f)
                    thumbAxisWorld = -thumbAxisWorld;

                foreach (var bone in thumbBones)
                    StoreAxis(bone, thumbAxisWorld);
            }

            // 指を寄せる（内転）軸。リスト姿勢のうちに決めておく。
            // 曲げた後の形から毎回符号を選ぶと、指先が手のひら側へ入っているせいで
            // 反転し、指が外へ跳ねて「かぎ爪」になる。
            var adductTargets = new[]
            {
                (start: 0, degrees: 10f),   // 人差し指
                (start: 6, degrees: 9f),    // 薬指
                (start: 9, degrees: 16f),   // 小指
            };

            foreach (var (start, degrees) in adductTargets)
            {
                var prox = _animator.GetBoneTransform(fingerBones[start]);
                var dist = _animator.GetBoneTransform(fingerBones[start + 2]);
                if (prox == null || dist == null)
                    continue;

                Vector3 axisWorld = PickFlexionSign(prox, dist, midProx.position, palmNormal);
                _fingerAdductAxis[prox] = BoneLocalAxis(prox, axisWorld);

                Debug.Log($"[Rig] adduct {(left ? "L" : "R")} f{start} {degrees:F0}deg");
            }

            Debug.Log($"[Rig] finger axis {(left ? "L" : "R")} world=({fingerAxisWorld.x:F2},{fingerAxisWorld.y:F2},{fingerAxisWorld.z:F2}) thumb=({thumbAxisWorld.x:F2},{thumbAxisWorld.y:F2},{thumbAxisWorld.z:F2}) restTip={FingertipDistanceToWrist(left):F3}");
        }

        /// <summary>
        /// 中指の先と手首の距離。握れているかを数値で確かめるためのもの
        /// （握ると指先が手首へ寄るので小さくなる）。
        /// </summary>
        public float FingertipDistanceToWrist(bool left)
        {
            if (_animator == null)
                return 0f;

            var hand = left ? LHand : RHand;
            var fingerBones = left ? LeftFingerBones : RightFingerBones;
            var distal = _animator.GetBoneTransform(fingerBones[5]);
            if (hand == null || distal == null)
                return 0f;

            return Vector3.Distance(TipPosition(distal), hand.position);
        }

        /// <summary>
        /// 指の第 1 関節の曲がり角（度）。伸びていれば 0 付近、握れば 60〜80 度。
        /// </summary>
        public float FingerBend(bool left, int finger)
        {
            if (_animator == null)
                return 0f;

            var fingerBones = left ? LeftFingerBones : RightFingerBones;
            int start = Mathf.Clamp(finger, 0, 3) * 3;
            var prox = _animator.GetBoneTransform(fingerBones[start]);
            var mid = _animator.GetBoneTransform(fingerBones[start + 1]);
            var dist = _animator.GetBoneTransform(fingerBones[start + 2]);
            if (prox == null || mid == null || dist == null)
                return 0f;

            return Vector3.Angle(mid.position - prox.position, dist.position - mid.position);
        }

        /// <summary>
        /// 30 度曲げて指先が目標へ近づくならその符号を、遠ざかるなら反転して返す。
        /// </summary>
        Vector3 PickFlexionSign(Transform joint, Transform distal, Vector3 toward, Vector3 worldAxis)
        {
            Vector3 localAxis = BoneLocalAxis(joint, worldAxis);
            float before = Vector3.Distance(TipPosition(distal), toward);

            Quaternion saved = joint.localRotation;
            joint.localRotation = saved * Quaternion.AngleAxis(30f, localAxis);
            float after = Vector3.Distance(TipPosition(distal), toward);
            joint.localRotation = saved;

            return after <= before ? worldAxis : -worldAxis;
        }

        void StoreAxis(HumanBodyBones bone, Vector3 worldAxis)
        {
            var t = _animator.GetBoneTransform(bone);
            if (t == null)
                return;

            _fingerAxis[t] = BoneLocalAxis(t, worldAxis);
        }

        /// <summary>
        /// CurlOne は rest * AngleAxis(deg, axis) で回すので、axis は rest 適用後の
        /// ボーン座標系の向きになる。ワールドの向きをその座標系へ落とす。
        /// </summary>
        static Vector3 BoneLocalAxis(Transform bone, Vector3 worldAxis)
            => Quaternion.Inverse(bone.rotation) * worldAxis;

        static Vector3 TipPosition(Transform distal)
        {
            if (distal == null)
                return Vector3.zero;

            return distal.childCount > 0 ? distal.GetChild(0).position : distal.position;
        }

        /// <summary>
        /// 指を曲げる角度（MCP / PIP / DIP）。
        /// 浅いと指先が掌に届かず「C の字＝猫の手」になる。拳は MCP 90 度・PIP 100 度が目安。
        /// </summary>
        public static float[] CurlDegrees = { 95f, 105f, 65f };

        /// <summary>
        /// 親指を人差し指の中節へ向ける強さ。0=曲げだけ（突き出る） / 1=完全に狙い付け。
        /// </summary>
        public static float ThumbAim = 1f;

        static readonly HumanBodyBones[] LeftFingerBones =
        {
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        };

        static readonly HumanBodyBones[] RightFingerBones =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        static readonly HumanBodyBones[] LeftThumbBones =
        {
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
        };

        static readonly HumanBodyBones[] RightThumbBones =
        {
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        };

        /// <summary>
        /// 脚を初期姿勢（まっすぐ）へ寄せる。1 で完全に伸びる。
        /// IK で深く曲げた脚を、倒れるのに合わせて伸ばすために使う。
        /// </summary>
        public void BlendLegsToRest(float t)
        {
            if (t <= 0f)
                return;

            t = Mathf.Clamp01(t);
            BlendToRest(LUpperLeg, t);
            BlendToRest(LLowerLeg, t);
            BlendToRest(LFoot, t);
            BlendToRest(RUpperLeg, t);
            BlendToRest(RLowerLeg, t);
            BlendToRest(RFoot, t);
        }

        void BlendToRest(Transform bone, float t)
        {
            if (bone == null || !_rest.TryGetValue(bone, out var rest))
                return;

            bone.localRotation = Quaternion.Slerp(bone.localRotation, rest.rot, t);
        }

        /// <summary>
        /// 脚をしゃがみ姿勢へ寄せる。1 で膝を深く曲げ、足を体の下へ畳む。
        /// 立ち上がるときに「脚が棒のまま回る」のを防ぐために使う。
        /// </summary>
        public void BlendLegsToCrouch(float t)
        {
            if (t <= 0f)
                return;

            t = Mathf.Clamp01(t);
            BlendToRestOffset(LUpperLeg, t, -68f);
            BlendToRestOffset(LLowerLeg, t, 96f);
            BlendToRestOffset(LFoot, t, -24f);
            BlendToRestOffset(RUpperLeg, t, -68f);
            BlendToRestOffset(RLowerLeg, t, 96f);
            BlendToRestOffset(RFoot, t, -24f);
        }

        void BlendToRestOffset(Transform bone, float t, float pitchDegrees)
        {
            if (bone == null || !_rest.TryGetValue(bone, out var rest))
                return;

            // 回す軸は「体の右」をボーンの初期姿勢へ落としたもの。
            // ボーンのローカル X で回すと、モデルによっては膝が前へ折れて
            // 人体に有り得ない形になる。
            Vector3 axis = _flexAxis.TryGetValue(bone, out var a) ? a : Vector3.right;

            // t は「曲げ量」。以前は毎フレーム t で補間していたため、t が小さくても
            // 時間が経つと完全な深いしゃがみへ収束してしまっていた（膝が折り畳まれる）。
            Quaternion target = rest.rot * Quaternion.AngleAxis(pitchDegrees * Mathf.Clamp01(t), axis);
            bone.localRotation = target;
        }

        /// <summary>
        /// 脚を曲げる向き（体の右）を、各ボーンの初期姿勢のローカルへ落として覚える。
        /// 膝・股関節・足首はすべて同じ面（前後）で曲がる。
        /// </summary>
        void CaptureFlexAxes()
        {
            _flexAxis.Clear();

            foreach (var bone in new[]
                     {
                         LUpperLeg, LLowerLeg, LFoot,
                         RUpperLeg, RLowerLeg, RFoot,
                     })
            {
                if (bone != null)
                    _flexAxis[bone] = Quaternion.Inverse(bone.rotation) * transform.right;
            }
        }

        /// <summary>腰を world 座標で動かす。足を固定したまま腰だけ落とせる。</summary>
        public void OffsetHips(Vector3 worldOffset)
        {
            if (Hips != null)
                Hips.position += worldOffset;
        }

        /// <summary>
        /// 体全体を world X 軸まわりに倒す（負で仰向け、正でうつ伏せ）。
        /// 腰を支点に回すので、脚・腕・頭も一緒に倒れる。
        /// 倒したあとの姿勢は「立っていた体を丸ごと倒したもの」になるので、
        /// 足の IK はこのメソッドを呼ぶ前に済ませておくこと。
        /// </summary>
        /// <param name="sideDegrees">
        /// 倒れる向きを左右へずらす。0 で真後ろ。少し傾けると、真後ろから見ても顔が隠れない。
        /// </param>
        public void LayBody(float degrees, float sideDegrees = 0f)
        {
            if (Hips == null)
                return;

            // 軸はキャラクター基準。「world の右」で倒すと、体が回っているときに
            // 真後ろではなく真横へ倒れてしまう。
            Vector3 axis = transform.right;
            if (Mathf.Abs(sideDegrees) > 0.01f)
                axis = Quaternion.AngleAxis(sideDegrees, transform.up) * axis;

            Hips.rotation = Quaternion.AngleAxis(degrees, axis) * Hips.rotation;
        }

        /// <summary>体幹を、キャラクターのローカル軸まわりに曲げる。</summary>
        public void BendTorso(float pitch, float roll, float yaw)
        {
            if (Spine == null)
                return;

            Quaternion rotation =
                Quaternion.AngleAxis(yaw, Vector3.up) *
                Quaternion.AngleAxis(pitch, Vector3.right) *
                Quaternion.AngleAxis(roll, Vector3.forward);

            // ワールド軸で回すと、ボーンの初期向きに依存しない
            Quaternion world = transform.rotation * rotation * Quaternion.Inverse(transform.rotation);
            Spine.rotation = world * Spine.rotation;

            if (Chest != null)
            {
                Quaternion half =
                    Quaternion.AngleAxis(yaw * 0.4f, Vector3.up) *
                    Quaternion.AngleAxis(pitch * 0.4f, Vector3.right) *
                    Quaternion.AngleAxis(roll * 0.4f, Vector3.forward);
                Quaternion halfWorld = transform.rotation * half * Quaternion.Inverse(transform.rotation);
                Chest.rotation = halfWorld * Chest.rotation;
            }
        }

        public void TurnHead(float pitch, float yaw, float roll = 0f)
        {
            if (Head == null)
                return;

            Quaternion rotation =
                Quaternion.AngleAxis(yaw, Vector3.up) *
                Quaternion.AngleAxis(pitch, Vector3.right) *
                Quaternion.AngleAxis(roll, Vector3.forward);
            Quaternion world = transform.rotation * rotation * Quaternion.Inverse(transform.rotation);
            Head.rotation = world * Head.rotation;
        }

        Transform Bone(HumanBodyBones id, string fallbackName)
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
