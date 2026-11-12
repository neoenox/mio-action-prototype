using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        /// <summary>
        /// 壁押さえつけの腕IK。両手首を頭の上へ置き、<see cref="PlayerGrapple.WallAdhereWeight"/>
        /// が上がると壁の表面へ貼り付ける（手のひら 1 枚ぶんだけ離す）。
        /// 壁へ貼り付いたあとは指を開かず、掌を壁へ向けて押し当てる形にする。
        /// </summary>
        void SolveOverheadArms()
        {
            if (Rig.LUpperArm == null || Rig.LLowerArm == null || Rig.LHand == null ||
                Rig.RUpperArm == null || Rig.RLowerArm == null || Rig.RHand == null ||
                Rig.Head == null)
                return;

            // 拘束に入った直後は今の手首位置からブレンドする（腕が瞬間移動しない）。
            float w = Grapple.WallRestraintWeight;
            float adhere = Grapple.WallAdhereWeight;

            Vector3 head = Rig.Head.position + transform.forward * 0.09f;
            Vector3 right = transform.right;
            // 壁の二人がかり（Y 字）では、手首を左右へ広げて二人が片腕ずつ掴める形にする。
            float half = Mathf.Lerp(0.08f, CombatConstants.WallSplitHandHalf, Grapple.WallSplitWeight);

            // 引き上げ中は頭の上。貼り付くと同時に、腕を上へ伸ばした位置へ移る。
            Vector3 goalL = head + Vector3.up * 0.24f - right * half;
            Vector3 goalR = head + Vector3.up * 0.24f + right * half;

            Vector3 normal = Vector3.back;
            bool hasWall = Grapple.HasWallFace;
            if (hasWall)
            {
                Vector3 n = Grapple.WallPinNormal;
                if (n.sqrMagnitude > 0.001f)
                    normal = n.normalized;

                // 壁へ貼り付いた手首は world 固定。頭を基準にすると、もがくたびに
                // 上体が揺れて手首まで壁から浮く（実測: 0.11m → 0.31m まで動いた）。
                // 体が揺れても手首は壁に残し、腕だけが伸び縮みする。
                Vector3 root = transform.position;
                Vector3 stuckBase = root + Vector3.up * WallStickHeight;
                Vector3 stuckL = WallSurfacePoint(stuckBase - right * half, normal, WallHandGap);
                Vector3 stuckR = WallSurfacePoint(stuckBase + right * half, normal, WallHandGap);
                goalL = Vector3.Lerp(goalL, stuckL, adhere);
                goalR = Vector3.Lerp(goalR, stuckR, adhere);
            }

            if (Grapple.WallPhase == WallStage.TossWindup && Rig.Chest != null)
            {
                Vector3 reach = Rig.Chest.position + transform.forward * 0.28f - Vector3.up * 0.12f
                    + Vector3.right * (Grapple.WallTossSide * 0.16f);
                goalL = Vector3.Lerp(goalL, reach - right * 0.18f, Grapple.WallTossLower);
                goalR = Vector3.Lerp(goalR, reach + right * 0.18f, Grapple.WallTossLower);
            }

            // 二人がかり（Y 字）では手首を壁から離し、正面に立つ二人へ差し出す。
            // 壁へ貼り付けたままだと二人は彼女の真横に並ぶしかなく、腹パンが横から
            // 棒で叩く形になる（実測）。手首を正面へ出すと二人が前に立てる。
            float split = Grapple.WallSplitWeight;
            Vector3 splitBase = transform.position + Vector3.up * 1.40f + transform.forward * 0.34f;
            if (split > 0.001f)
            {
                Vector3 splitL = splitBase - right * CombatConstants.WallSplitHandHalf;
                Vector3 splitR = splitBase + right * CombatConstants.WallSplitHandHalf;
                goalL = Vector3.Lerp(goalL, splitL, split);
                goalR = Vector3.Lerp(goalR, splitR, split);
            }

            goalL = Vector3.Lerp(Rig.LHand.position, goalL, w);
            goalR = Vector3.Lerp(Rig.RHand.position, goalR, w);

            // 肘は外へ開き、**少し後ろ**へ。前へ出すと腕が顔の前に来て表情が隠れる
            // （実測: 正面寄りのカメラで腕が顔を覆った）。後ろへ逃がすと腕が頭の枠になる。
            Vector3 poleBase = head + Vector3.up * 0.02f - transform.forward * 0.10f;
            if (split > 0.001f)
            {
                // 手首が正面にあるので、肘は後ろではなく外へ開く。
                Vector3 splitPole = head + Vector3.up * 0.02f + transform.forward * 0.02f;
                poleBase = Vector3.Lerp(poleBase, splitPole, split);
            }
            float poleSpread = Mathf.Lerp(0.50f, 0.62f, adhere);
            Vector3 poleL = poleBase - right * poleSpread;
            Vector3 poleR = poleBase + right * poleSpread;

            ProceduralRig.SolveTwoBone(Rig.LUpperArm, Rig.LLowerArm, Rig.LHand, goalL, poleL, right);
            ProceduralRig.SolveTwoBone(Rig.RUpperArm, Rig.RLowerArm, Rig.RHand, goalR, poleR, right);

            // 壁へ貼り付いた手首は、掌を壁へ向けて押し当てる。手のひらが部屋側を向いたままだと
            // 「壁へ手を突っ張っている」に見えない（実測）。二人がかりでは手首が正面へ出るので
            // 壁の掌は抜く（二人が手首を掴んでいる）。
            float wallPalm = hasWall ? adhere * (1f - split) : 0f;
            if (wallPalm > 0.01f)
            {
                Vector3 intoWall = -normal;
                Quaternion leftPose = Rig.LHand.rotation;
                Quaternion rightPose = Rig.RHand.rotation;
                Rig.AlignPalm(true, intoWall, Vector3.up);
                Rig.AlignPalm(false, intoWall, Vector3.up);
                Rig.LHand.rotation = Quaternion.Slerp(leftPose, Rig.LHand.rotation, wallPalm);
                Rig.RHand.rotation = Quaternion.Slerp(rightPose, Rig.RHand.rotation, wallPalm);

                // 手首は壁から離したので、そのままだと掌が浮いて見える。手首を軸に指先だけを
                // 壁へ寄せる（掌は壁に平行から少し傾くが、指先が触れて「押し当てている」形になる）。
                float tilt = WallHandTilt * wallPalm * (1f - Grapple.WallTossLower);
                if (tilt > 0.01f)
                {
                    Vector3 tiltAxis = Vector3.Cross(Vector3.up, intoWall);
                    if (tiltAxis.sqrMagnitude > 1e-4f)
                    {
                        Quaternion tiltRotation = Quaternion.AngleAxis(tilt, tiltAxis.normalized);
                        Rig.LHand.rotation = tiltRotation * Rig.LHand.rotation;
                        Rig.RHand.rotation = tiltRotation * Rig.RHand.rotation;
                    }
                }
            }

            // 手首が壁に付いている間は指を開く（掴む相手がいない）。
            float grip = Mathf.Lerp(0.55f, 0.10f, adhere);
            Rig.CurlFingers(true, grip);
            Rig.CurlFingers(false, grip);
        }

        /// <summary>壁の表面から <paramref name="gap"/> だけ部屋側へ離した点を返す。</summary>
        Vector3 WallSurfacePoint(Vector3 point, Vector3 normal, float gap)
        {
            // 壁は Z に垂直な面（FaceZ）。法線方向の距離だけで寄せる。
            Vector3 onFace = new Vector3(point.x, point.y, Grapple.WallFaceZ);
            float distance = Vector3.Dot(point - onFace, normal);
            return point + normal * (gap - distance);
        }

        /// <summary>壁へ貼り付く手首の高さ（キャラクターの足元から m）。頭より上に置く。</summary>
        const float WallStickHeight = 1.62f;

        /// <summary>
        /// 壁の表面から手首までの距離（m）。手首のメッシュ半径（約 3〜4cm）ぶんを空ける。
        /// 0.045 では手首が壁に埋まって見えた（実測）。<c>-wallhandgap=</c> で撮り比べられる。
        /// </summary>
        static float WallHandGap
        {
            get
            {
                if (_wallHandGap < 0f)
                    _wallHandGap = ReadWallHandGap();
                return _wallHandGap;
            }
        }

        static float _wallHandGap = -1f;

        static float ReadWallHandGap()
        {
            if (LaunchOptions.TryFloat("-wallhandgap", out float value))
                return Mathf.Clamp(value, 0.03f, 0.15f);

            return 0.075f;
        }

        /// <summary>
        /// 手首から先だけ壁へ寄せる傾き（度）。手首は浮かせたまま、指先を壁へ触れさせる。
        /// </summary>
        const float WallHandTilt = 12f;

    }
}
