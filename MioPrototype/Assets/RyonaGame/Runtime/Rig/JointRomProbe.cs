using System.Collections.Generic;
using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 関節の角度を測るのに使う骨の組。
    ///
    /// プレイヤー（VRM + <see cref="ProceduralRig"/>）と敵（<see cref="EnemyFigure"/> の
    /// プリミティブ骨格）で、同じ <see cref="JointRomProbe"/> を使えるように
    /// 骨の持ち方をそろえてある。肘・膝は「向き」から、体幹・首は「初期姿勢からの
    /// 回転差」から測るので、モデルごとのボーン軸の違いに影響されない。
    /// </summary>
    public struct JointSkeleton
    {
        public Transform Root;

        public Transform Hips;
        public Transform Chest;
        public Transform Head;

        public Transform LUpperArm;
        public Transform LLowerArm;
        public Transform LHand;
        public Transform RUpperArm;
        public Transform RLowerArm;
        public Transform RHand;

        /// <summary>脚の付け根（股関節）。</summary>
        public Transform LUpperLeg;
        public Transform RUpperLeg;

        /// <summary>ひざ。</summary>
        public Transform LLowerLeg;
        public Transform RLowerLeg;

        /// <summary>足首。ひざの曲がりは「ひざ→足首」の向きで測る。</summary>
        public Transform LFoot;
        public Transform RFoot;

        /// <summary>初期姿勢での腰・胸・頭の向き（root から見た回転）。</summary>
        public Quaternion RestHips;
        public Quaternion RestChest;
        public Quaternion RestHead;

        /// <summary>初期姿勢の回転が分かっているか（体幹・首の計測に必要）。</summary>
        public bool HasRestRotations;

        /// <summary>肘が曲がる軸（上腕のローカル座標）。正の回転 = 前腕が前へ折れる。</summary>
        public Vector3 ElbowHingeLocalL;
        public Vector3 ElbowHingeLocalR;

        /// <summary>ひざが曲がる軸（腿のローカル座標）。正の回転 = ふくらはぎが後ろへ折れる。</summary>
        public Vector3 KneeHingeLocalL;
        public Vector3 KneeHingeLocalR;

        /// <summary>肘・ひざの蝶番軸が求まっているか（逆方向の判定に必要）。</summary>
        public bool HasHinges;

        /// <summary>計測に必要な骨がそろっているか。</summary>
        public bool IsValid => Root != null && Hips != null && Chest != null && Head != null;
    }

    /// <summary>
    /// プレイヤー／敵の骨格から <see cref="JointSkeleton"/> を組み立てる。
    /// 追加のコンポーネントやシーン設定は要らない（既にある骨を読むだけ）。
    /// </summary>
    public static class JointSkeletonBuilder
    {
        /// <summary>プレイヤー（VRM。初期姿勢は <see cref="ProceduralRig"/> が控えている）。</summary>
        public static bool TryBuild(ProceduralRig rig, out JointSkeleton skeleton)
        {
            skeleton = default;
            if (rig == null || !rig.Ready)
                return false;

            var built = new JointSkeleton
            {
                Root = rig.transform,
                Hips = rig.Hips,
                Chest = rig.Chest,
                Head = rig.Head,
                LUpperArm = rig.LUpperArm,
                LLowerArm = rig.LLowerArm,
                LHand = rig.LHand,
                RUpperArm = rig.RUpperArm,
                RLowerArm = rig.RLowerArm,
                RHand = rig.RHand,
                LUpperLeg = rig.LUpperLeg,
                RUpperLeg = rig.RUpperLeg,
                LLowerLeg = rig.LLowerLeg,
                RLowerLeg = rig.RLowerLeg,
                LFoot = rig.LFoot,
                RFoot = rig.RFoot
            };

            if (!built.IsValid || built.LLowerLeg == null || built.LFoot == null
                || built.RLowerLeg == null || built.RFoot == null)
                return false;

            bool hasRest =
                rig.TryGetRestRotation(built.Hips, out built.RestHips)
                & rig.TryGetRestRotation(built.Chest, out built.RestChest)
                & rig.TryGetRestRotation(built.Head, out built.RestHead);

            // 初期姿勢が取れていなくても、肘・膝・股関節・肩は測れる。
            // 体幹・首だけは初期姿勢が要るので、その場合だけ省く。
            built.HasRestRotations = hasRest;

            // 肘・ひざの蝶番軸も初期姿勢から求める。上腕（腿）のローカルに落とすので、
            // 肩をひねった姿勢・脚を開いた姿勢でも軸が付いてくる。
            if (rig.TryGetRestRotation(built.LUpperArm, out Quaternion restArmL)
                & rig.TryGetRestRotation(built.RUpperArm, out Quaternion restArmR))
            {
                built.ElbowHingeLocalL = ElbowHingeLocal(built.LLowerArm, restArmL);
                built.ElbowHingeLocalR = ElbowHingeLocal(built.RLowerArm, restArmR);
                built.HasHinges = true;
            }

            if (rig.TryGetRestRotation(built.LUpperLeg, out Quaternion restLegL)
                & rig.TryGetRestRotation(built.RUpperLeg, out Quaternion restLegR))
            {
                built.KneeHingeLocalL = KneeHingeLocal(restLegL);
                built.KneeHingeLocalR = KneeHingeLocal(restLegR);
            }

            skeleton = built;
            return true;
        }

        /// <summary>敵（<see cref="EnemyFigure"/> のプリミティブ骨格）。</summary>
        public static bool TryBuild(EnemyFigure figure, out JointSkeleton skeleton)
        {
            skeleton = default;
            if (figure == null)
                return false;

            Transform chest = figure.ChestBone;
            var built = new JointSkeleton
            {
                Root = figure.transform,
                Hips = figure.HipsBone != null ? figure.HipsBone : (chest != null ? chest.parent : null),
                Chest = chest,
                Head = figure.HeadBone,
                LUpperArm = figure.ShoulderBone(true),
                LLowerArm = figure.ElbowBone(true),
                LHand = figure.WristBone(true),
                RUpperArm = figure.ShoulderBone(false),
                RLowerArm = figure.ElbowBone(false),
                RHand = figure.WristBone(false),
                LUpperLeg = figure.HipBone(true),
                RUpperLeg = figure.HipBone(false),
                LLowerLeg = figure.KneeBone(true),
                RLowerLeg = figure.KneeBone(false),
                LFoot = figure.AnkleBone(true),
                RFoot = figure.AnkleBone(false)
            };

            if (!built.IsValid || built.LLowerLeg == null || built.LFoot == null
                || built.RLowerLeg == null || built.RFoot == null)
                return false;

            // 敵のプリミティブは関節の初期回転を持たない（子は位置だけで作られている）。
            // 初期姿勢は root と同じ向きになるので、identity を基準にすればよい。
            built.RestHips = Quaternion.identity;
            built.RestChest = Quaternion.identity;
            built.RestHead = Quaternion.identity;
            built.HasRestRotations = true;

            built.ElbowHingeLocalL = ElbowHingeLocal(built.LLowerArm, Quaternion.identity);
            built.ElbowHingeLocalR = ElbowHingeLocal(built.RLowerArm, Quaternion.identity);
            built.KneeHingeLocalL = KneeHingeLocal(Quaternion.identity);
            built.KneeHingeLocalR = KneeHingeLocal(Quaternion.identity);
            built.HasHinges = true;

            skeleton = built;
            return true;
        }

        /// <summary>
        /// 肘の蝶番軸を上腕ローカルで求める。初期姿勢の上腕の向き × 体の前。
        /// 前腕が「前へ折れる」向きが正になる。
        /// </summary>
        public static Vector3 ElbowHingeLocal(Transform lowerArm, Quaternion restUpperArm)
        {
            if (lowerArm == null)
                return Vector3.zero;

            Vector3 arm = lowerArm.localPosition;
            if (arm.sqrMagnitude < 1e-8f)
                return Vector3.zero;

            // 上腕ローカルの腕の向きを root 基準へ戻し、体の前との外積で軸を作る。
            Vector3 restArmInRoot = restUpperArm * arm.normalized;
            Vector3 hinge = Vector3.Cross(restArmInRoot, Vector3.forward);
            if (hinge.sqrMagnitude < 1e-6f)
                hinge = Vector3.Cross(restArmInRoot, Vector3.up);
            if (hinge.sqrMagnitude < 1e-6f)
                return Vector3.zero;

            hinge.Normalize();
            return Quaternion.Inverse(restUpperArm) * hinge;
        }

        /// <summary>
        /// ひざの蝶番軸を腿のローカルで求める。初期姿勢の「体の右」。
        /// ふくらはぎが「後ろへ折れる」向きが正になる。
        /// </summary>
        public static Vector3 KneeHingeLocal(Quaternion restUpperLeg)
        {
            return (Quaternion.Inverse(restUpperLeg) * Vector3.right).normalized;
        }
    }

    /// <summary>
    /// 骨の Transform から関節角度を測り、<see cref="JointRom.Read"/> の判定にする。
    ///
    /// 角度の決め方（ログを読むときに必要）:
    /// <list type="bullet">
    /// <item>ひじ・ひざ … 0° = まっすぐ。正 = 正常に曲がっている、負 = 逆方向。</item>
    /// <item>股関節・肩 … 正 = 前（肩は横）へ上げる。左右の開きは外側を正にそろえる。</item>
    /// <item>体幹・首 … 胸（頭）の初期姿勢からの回転差を、キャラクターの正面基準で
    /// 前後（X）・ひねり（Y）・左右（Z）に分解したもの。</item>
    /// </list>
    /// </summary>
    public static class JointRomProbe
    {
        const float MinSegmentLength = 1e-4f;

        /// <summary>骨格を測って <paramref name="results"/> に詰め直す。</summary>
        public static void Measure(in JointSkeleton skeleton, List<JointRomReading> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (!skeleton.IsValid)
                return;

            MeasureTorso(in skeleton, results);
            MeasureNeck(in skeleton, results);

            MeasureElbow(skeleton.LUpperArm, skeleton.LLowerArm, skeleton.LHand,
                skeleton.ElbowHingeLocalL, skeleton.HasHinges, JointSide.Left, results);
            MeasureElbow(skeleton.RUpperArm, skeleton.RLowerArm, skeleton.RHand,
                skeleton.ElbowHingeLocalR, skeleton.HasHinges, JointSide.Right, results);

            MeasureKnee(skeleton.LUpperLeg, skeleton.LLowerLeg, skeleton.LFoot,
                skeleton.KneeHingeLocalL, skeleton.HasHinges, JointSide.Left, results);
            MeasureKnee(skeleton.RUpperLeg, skeleton.RLowerLeg, skeleton.RFoot,
                skeleton.KneeHingeLocalR, skeleton.HasHinges, JointSide.Right, results);

            MeasureHip(skeleton, true, results);
            MeasureHip(skeleton, false, results);

            MeasureShoulder(skeleton, true, results);
            MeasureShoulder(skeleton, false, results);
        }

        // --- 体幹・首 ---------------------------------------------------

        static void MeasureTorso(in JointSkeleton s, List<JointRomReading> results)
        {
            if (!s.HasRestRotations)
                return;

            Vector3 bend = BendEuler(s.Root, s.Hips, s.Chest, s.RestHips, s.RestChest);
            results.Add(JointRom.Read(JointId.SpinePitch, JointSide.Center, bend.x));
            results.Add(JointRom.Read(JointId.SpineYaw, JointSide.Center, bend.y));
            results.Add(JointRom.Read(JointId.SpineRoll, JointSide.Center, bend.z));
        }

        static void MeasureNeck(in JointSkeleton s, List<JointRomReading> results)
        {
            if (!s.HasRestRotations)
                return;

            Vector3 bend = BendEuler(s.Root, s.Chest, s.Head, s.RestChest, s.RestHead);
            results.Add(JointRom.Read(JointId.NeckPitch, JointSide.Center, bend.x));
            results.Add(JointRom.Read(JointId.NeckYaw, JointSide.Center, bend.y));
            results.Add(JointRom.Read(JointId.NeckRoll, JointSide.Center, bend.z));
        }

        /// <summary>
        /// 子ボーンの向きを親ボーン基準に直し、初期姿勢からの差を
        /// 前後・ひねり・左右に分解する。
        ///
        /// 「親が動いた分」を差し引くのが要点。親（腰）が倒れたり回ったりしても、
        /// 子（胸）が付いてきただけなら曲がりではない。親の剛体回転で説明できる
        /// 向きを基準にし、そこからの差だけを測る。
        ///
        /// 差は<b>親ボーン自身の軸</b>で表す（以前は root の軸だった）。root の軸だと、
        /// 体が寝ているとき（ダウン・マウント）に、体の長軸まわりの頭のひねりが
        /// 水平な軸まわりの回転になり、前後・左右の曲げとして測られていた
        /// （頬を打たれて顔を横へ向けただけの首が、左右 83° と出た）。
        /// </summary>
        static Vector3 BendEuler(Transform root, Transform parent, Transform child,
            Quaternion restParent, Quaternion restChild)
        {
            Quaternion rootInverse = Quaternion.Inverse(root.rotation);
            Quaternion parentNow = rootInverse * parent.rotation;   // 親の今の向き（root 基準）
            Quaternion childNow = rootInverse * child.rotation;     // 子の今の向き（root 基準）

            // 子の「親から見た今の向き」と「親から見た初期の向き」の差。
            // 親が動いて子が付いてきただけなら単位回転になり、軸は親のローカル軸になる。
            Quaternion delta = Quaternion.Inverse(parentNow) * childNow
                * Quaternion.Inverse(restChild) * restParent;
            return BendAngles(delta);
        }

        /// <summary>
        /// 体幹・首の曲がりを、前後（pitch）・ひねり（yaw）・左右（roll）に分ける。
        ///
        /// オイラー角で分けると、大きく前屈したときの「ひねり」や「左右」が実際より
        /// 大きく出る（回転の合成順の都合で軸が混ざる）。ここでは
        ///   1. 体の上軸まわりのひねりを抜き出す
        ///   2. 残りの曲げを「曲げの大きさ × 曲げの軸（右 or 前）」に分ける
        /// ので、深い前屈とひねりが同時でも漏れない。90° を超える前屈でも
        /// 左右の曲げが 180° に化けたりしない。
        /// </summary>
        static Vector3 BendAngles(Quaternion delta)
        {
            float yaw = WrapDegrees(TwistDegrees(delta, Vector3.up));
            Quaternion twist = Quaternion.AngleAxis(yaw, Vector3.up);
            Quaternion swing = Quaternion.Inverse(twist) * delta;

            swing.ToAngleAxis(out float swingDegrees, out Vector3 swingAxis);
            if (swingDegrees > 180f)
                swingDegrees = 360f - swingDegrees;

            Vector3 axis = Vector3.ProjectOnPlane(swingAxis, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f)
                return new Vector3(0f, yaw, 0f);

            axis.Normalize();

            // 右軸まわりの曲げ = 前後（正で前屈）。前軸まわりの曲げ = 左右（正で右へ）。
            float pitch = swingDegrees * Vector3.Dot(axis, Vector3.right);
            float roll = -swingDegrees * Vector3.Dot(axis, Vector3.forward);
            return new Vector3(pitch, yaw, roll);
        }

        /// <summary><paramref name="axis"/> まわりの回転成分（度）。</summary>
        static float TwistDegrees(Quaternion rotation, Vector3 axis)
        {
            float projected = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
            if (projected * projected + rotation.w * rotation.w < 1e-12f)
                return 0f;

            return 2f * Mathf.Atan2(projected, rotation.w) * Mathf.Rad2Deg;
        }

        static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            else if (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        // --- 肘・膝 -----------------------------------------------------

        static void MeasureElbow(Transform shoulder, Transform elbow, Transform hand,
            Vector3 hingeLocal, bool hasHinge, JointSide side, List<JointRomReading> results)
        {
            if (shoulder == null || elbow == null || hand == null)
                return;

            if (!TryDirection(shoulder.position, elbow.position, out Vector3 upper))
                return;
            if (!TryDirection(elbow.position, hand.position, out Vector3 fore))
                return;

            float flex = Vector3.Angle(upper, fore);

            // 肘の蝶番は「上腕ローカルの初期姿勢から求めた軸」を、いまの上腕の回転で
            // ワールドへ戻したもの。肩をひねっても軸が付いてくる。
            // 曲がる面が蝶番とそろっているときだけ「逆方向」を判断する
            // （腕をひねった姿勢で、横へ折れているのを逆曲がりと誤認しないため）。
            if (hasHinge && hingeLocal.sqrMagnitude > 1e-8f)
            {
                Vector3 hinge = shoulder.rotation * hingeLocal;
                Vector3 bend = Vector3.Cross(upper, fore);
                if (bend.sqrMagnitude > 1e-8f
                    && Mathf.Abs(Vector3.Dot(bend.normalized, hinge)) > 0.35f)
                {
                    if (PlanarAngle(upper, fore, hinge, out float signed) && signed < 0f)
                        flex = -flex;
                }
            }

            results.Add(JointRom.Read(JointId.Elbow, side, flex));
        }

        static void MeasureKnee(Transform upper, Transform lower, Transform foot,
            Vector3 hingeLocal, bool hasHinge, JointSide side, List<JointRomReading> results)
        {
            if (upper == null || lower == null || foot == null)
                return;

            if (!TryDirection(upper.position, lower.position, out Vector3 thigh))
                return;
            if (!TryDirection(lower.position, foot.position, out Vector3 shin))
                return;

            float flex = Vector3.Angle(thigh, shin);

            // ひざは「ふくらはぎが後ろへ折れる」が正。腿ローカルの蝶番軸で向きを見る。
            if (hasHinge && hingeLocal.sqrMagnitude > 1e-8f)
            {
                Vector3 hinge = upper.rotation * hingeLocal;
                Vector3 bend = Vector3.Cross(thigh, shin);
                if (bend.sqrMagnitude > 1e-8f
                    && Mathf.Abs(Vector3.Dot(bend.normalized, hinge)) > 0.35f)
                {
                    if (PlanarAngle(thigh, shin, hinge, out float signed) && signed < 0f)
                        flex = -flex;
                }
                // 曲がる面が蝶番から大きく外れている（脚を大きく開いた等）ときは
                // 向きの判断をしない。誤警告を避けるため。
            }

            results.Add(JointRom.Read(JointId.Knee, side, flex));
        }

        // --- 股関節 -----------------------------------------------------

        static void MeasureHip(in JointSkeleton s, bool left, List<JointRomReading> results)
        {
            Transform upper = left ? s.LUpperLeg : s.RUpperLeg;
            Transform lower = left ? s.LLowerLeg : s.RLowerLeg;
            if (upper == null || lower == null || s.Hips == null)
                return;

            if (!TryDirection(upper.position, lower.position, out Vector3 thigh))
                return;

            // 骨盤の「体の上・右・前」を初期姿勢から求める。ボーンのローカル軸は
            // モデルごとに違うので、root 基準の向きを腰へ落として使う。
            BodyAxes(s.Hips, s.RestHips, out Vector3 up, out Vector3 right, out Vector3 forward);

            // 腿の向きを骨盤の座標へ落とす。(x, y, z) = (右, 上, 前)。
            Vector3 local = ToBodyFrame(thigh, up, right, forward);
            JointSide side = left ? JointSide.Left : JointSide.Right;

            // 前後: 真下から前へ持ち上がった角度。後ろへ引くと負。
            // 真上まで上がっても atan2 なので潰れない（オイラー角の混ざりが無い）。
            float flexion = Mathf.Atan2(local.z, -local.y) * Mathf.Rad2Deg;
            results.Add(JointRom.Read(JointId.HipFlexion, side, flexion));

            // 開き: 体の前後（矢状）面から横へどれだけ外れたか。
            // 前へ上げた脚では 0 のままなので、屈曲と混ざらない。
            float abduction = Mathf.Asin(Mathf.Clamp(local.x, -1f, 1f)) * Mathf.Rad2Deg;
            if (left)
                abduction = -abduction;
            results.Add(JointRom.Read(JointId.HipAbduction, side, abduction));
        }

        // --- 肩 ---------------------------------------------------------

        static void MeasureShoulder(in JointSkeleton s, bool left, List<JointRomReading> results)
        {
            Transform upper = left ? s.LUpperArm : s.RUpperArm;
            Transform lower = left ? s.LLowerArm : s.RLowerArm;
            if (upper == null || lower == null || s.Chest == null)
                return;

            if (!TryDirection(upper.position, lower.position, out Vector3 arm))
                return;

            BodyAxes(s.Chest, s.RestChest, out Vector3 up, out Vector3 right, out Vector3 forward);

            Vector3 local = ToBodyFrame(arm, up, right, forward);
            JointSide side = left ? JointSide.Left : JointSide.Right;

            // 前後（屈曲）は、腕の向きを体の前後面（上・前）へ落とした角度。
            // 腕が真横に近いと、その射影が小さくなって角度が定まらない
            // （水平より 14° 上・13° 後ろの腕が -138° と出た）。射影の長さが
            // しきい値より短いときは前後を測らず、開き（下）だけを見る。
            float sagittal = Mathf.Sqrt(local.y * local.y + local.z * local.z);
            if (sagittal >= ShoulderSagittalMin)
            {
                float flexion = Mathf.Atan2(local.z, -local.y) * Mathf.Rad2Deg;

                // 真上を越えて後ろ寄りへ上げた腕は atan2 が -180° 側へ折り返す
                // （184° の屈曲が -176°）。伸展は -110° より深くならないので、
                // それより下は折り返しとみなして 180° 超の屈曲へ戻す。
                if (flexion < -110f)
                    flexion += 360f;

                results.Add(JointRom.Read(JointId.ShoulderFlexion, side, flexion));
            }

            float abduction = Mathf.Asin(Mathf.Clamp(local.x, -1f, 1f)) * Mathf.Rad2Deg;
            if (left)
                abduction = -abduction;
            results.Add(JointRom.Read(JointId.ShoulderAbduction, side, abduction));
        }

        /// <summary>
        /// 肩の前後を測る最小の射影の長さ（腕の単位ベクトルの上・前成分の長さ）。
        /// 0.5 = 腕が体の左右の面から 30° 以内に入ったら前後は測らない。
        /// </summary>
        const float ShoulderSagittalMin = 0.5f;

        /// <summary>ワールドの向きを、体の (右, 上, 前) 成分へ落とす。</summary>
        static Vector3 ToBodyFrame(Vector3 worldDirection, Vector3 up, Vector3 right, Vector3 forward)
        {
            return new Vector3(
                Vector3.Dot(worldDirection, right),
                Vector3.Dot(worldDirection, up),
                Vector3.Dot(worldDirection, forward));
        }

        /// <summary>
        /// ボーンの「体の上・右・前」を初期姿勢から求める。
        /// 初期姿勢でボーンのローカル軸がどれを向いていたかを控えておき、
        /// いまの回転でそれをワールドへ戻す（横になっても体の軸が付いてくる）。
        /// </summary>
        static void BodyAxes(Transform bone, Quaternion restRotation,
            out Vector3 up, out Vector3 right, out Vector3 forward)
        {
            Quaternion toLocal = Quaternion.Inverse(restRotation);
            up = bone.rotation * (toLocal * Vector3.up);
            right = bone.rotation * (toLocal * Vector3.right);
            forward = bone.rotation * (toLocal * Vector3.forward);
            up.Normalize();
            right.Normalize();
            forward.Normalize();
        }

        // --- 共通の計算 -------------------------------------------------

        static bool TryDirection(Vector3 from, Vector3 to, out Vector3 direction)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < MinSegmentLength * MinSegmentLength)
            {
                direction = Vector3.zero;
                return false;
            }

            direction = delta.normalized;
            return true;
        }

        /// <summary>
        /// <paramref name="axis"/> のまわりで from → to が何度回っているか。
        /// 軸と平行な成分は落として測る。回転面が潰れている（軸とほぼ平行で、
        /// 角度が決まらない）ときは false を返す。
        /// </summary>
        static bool PlanarAngle(Vector3 from, Vector3 to, Vector3 axis, out float degrees,
            float minProjection = 1e-2f)
        {
            degrees = 0f;
            Vector3 a = Vector3.ProjectOnPlane(from, axis);
            Vector3 b = Vector3.ProjectOnPlane(to, axis);
            if (a.magnitude < minProjection || b.magnitude < minProjection)
                return false;

            float angle = Vector3.Angle(a, b);
            Vector3 cross = Vector3.Cross(a, b);
            degrees = Vector3.Dot(cross, axis) >= 0f ? angle : -angle;
            return true;
        }
    }
}
