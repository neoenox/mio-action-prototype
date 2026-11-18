using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        /// <summary>怯み。構えから崩れ、踏ん張って戻る。</summary>
        PoseValues PoseStagger()
        {
            var pose = PoseDefaults();
            // 構えから崩れる。0 から作ると、当たった瞬間に足がそろって立ち位置が飛ぶ。

            // 30ms で一気に当たりの形へ入り、そこから減衰しながら戻る。
            // 減衰の途中で 1 回だけ前へ振れる（のけぞりの余韻）。
            //
            // 減衰と揺れは「当たってからの秒数」で決める。状態の長さで正規化すると、
            // 弱い一撃（0.2 秒）は 0.2 秒で 0.7 往復して痙攣し、
            // 強い一撃（0.8 秒）は同じ 0.7 往復を 0.8 秒かけて行うので
            // 「軽い一撃の方が激しく揺れる」という逆転が起きていた。
            float snap = Mathf.Clamp01(_stateTime / 0.03f);

            // 余韻の長さは当たりの強さで変える。同じ減衰だと、軽い一撃は
            // 一瞬で戻って「弾いた」ように見え、重い一撃も同じ速さで戻って
            // 「軽いのに長く止まっている」ように見える。
            // 重い一撃ほどゆっくり・低い周期で揺れて、戻りきるのは状態の終わり際。
            float decay = _tier switch
            {
                StaggerTier.Small => 9.0f,
                StaggerTier.Medium => 5.5f,
                StaggerTier.Large => 3.2f,
                StaggerTier.GuardBreak => 3.0f,
                _ => 9.0f
            };

            float sway = _tier switch
            {
                StaggerTier.Small => 13.0f,
                StaggerTier.Medium => 10.0f,
                StaggerTier.Large => 8.0f,
                StaggerTier.GuardBreak => 7.5f,
                _ => 13.0f
            };

            float k = snap * 1.22f * Mathf.Exp(-decay * _stateTime) * Mathf.Cos(sway * _stateTime);

            // 上体も腰も脚も同じ値で動かす。以前は上体だけ生の k（負値もそのまま）で、
            // 他は 0 で止めていたため、戻りの途中で上体だけ前へ出て腰が置いていかれていた。
            float kv = Mathf.Clamp(k, -0.30f, 1f);

            float bend = _tier switch
            {
                StaggerTier.Small => LightBend,
                StaggerTier.Medium => MediumBend,
                StaggerTier.Large => HeavyBend,
                StaggerTier.GuardBreak => HeavyBend,
                _ => LightBend
            };

            // 弱いヒットで腕を大きく振り回すと大げさに見えるので、強さに比例させる
            float flail = Mathf.Clamp01(bend / 46f);

            // 横からならその側へ、背中からなら前に折れる（どちらも「押された方向」へ崩れる）。
            bool fromBehind = _hurtSide == 2;
            float side = _hurtSide == 0 ? 1f : (_hurtSide == 2 ? 0f : -_hurtSide);

            // 本当に横から殴られたときの符号（0 = 正面 / 背後）。
            // side は正面ヒットでも 1 になる（揺れに方向を与えるため）ので、
            // 「横からの一撃か」はこちらで判断する。
            float lateralSide = _hurtSide == 1 ? -1f : (_hurtSide == -1 ? 1f : 0f);

            // 殴られた場所で崩れ方を変える。
            //   顔: 頭が弾かれて後ろへ振れる（上体は少し反る）
            //   腹: 上体が前へ折れて腹を抱える（息が詰まる）
            // 場所が分からないとき（投げなど）は、どちらにも倒さない。
            bool bodyBlow = !fromBehind && CurrentHitZone == PunchZone.Body;
            bool faceBlow = !fromBehind && CurrentHitZone == PunchZone.Face;

            // 顔ヒットは上体を大きく反らせない（のけぞりすぎると「吹き飛ばされた」画になる）。
            // 顔は首が弾かれる（+ 少しだけ上体が付く）で「殴られた」に見える。
            // 腹と背後は押された方向へ崩れるので、これまでどおり深く倒す。
            float torsoBack = faceBlow ? 0.16f : 0.38f;
            pose.TorsoPitch = StanceTorsoPitch + (fromBehind ? bend * 0.80f
                : (bodyBlow ? bend * 0.95f : -bend * torsoBack)) * kv;
            // 上体は真後ろへ反るだけでなく、殴られた方向へ少しねじれる。
            // ねじれが無いと「自分で反った」ようにしか見えない。
            pose.TorsoYaw = StanceTorsoYaw + 20f * kv * side;
            // 揺れは当たった直後だけ。長く続けると震えているように見える。
            pose.TorsoRoll = Mathf.Sin(_stateTime * 20f) * 2.4f * kv
                        * Mathf.Exp(-_stateTime * 9f);

            // 頭は殴られた所なので、上体より先に振れる（20ms）。
            // 上体が先に反ると「自分で反った」ように見える（実測でそうなっていた）。
            // 上体の反りは抑え、その分を頭に持たせると「殴られた」に見える。
            float headFollow = Mathf.Clamp01(_stateTime / 0.02f);
            pose.HeadPitch = StanceHeadPitch - bend * 0.62f * kv * headFollow * (fromBehind ? -0.7f : 1f);
            if (bodyBlow)
                pose.HeadPitch = StanceHeadPitch + bend * 0.45f * kv * headFollow;   // 腹を抱えて頭が落ちる
            else if (faceBlow)
                pose.HeadPitch -= bend * 0.25f * kv * headFollow;                    // 顔を打たれて頭が跳ねる
            pose.HeadYaw = StanceHeadYaw + 14f * kv * headFollow * side;
            pose.HeadRoll = 15f * kv * headFollow * side;   // 横から来た一撃で首が傾く

            // 効いた直後、首が小さく揺れる（頭がガクッと残る感じ）。
            // 減衰を速くしないと、いつまでも震えて「寒い」に見える。
            float shakeT = Mathf.Max(0f, _stateTime - 0.05f);
            float shakeAmp = 9f * flail * Mathf.Exp(-shakeT * 5f);
            pose.HeadRoll += Mathf.Sin(shakeT * 24f) * shakeAmp;
            pose.HeadYaw += Mathf.Cos(shakeT * 18f) * shakeAmp * 0.5f;
            pose.HeadPitch += Mathf.Sin(shakeT * 30f) * shakeAmp * 0.35f;

            // 腰ごと後ろへ押され、同時に沈む（膝が抜ける）。
            // 腰より足を大きく動かすと、脚が突っ張って「自分から下がった」画になる。
            // 顔ヒットは腰を引かずに首だけで受ける（引くと上体ごと逃げて見える）。
            pose.HipsOffset.y = StanceHipsY - 0.075f * kv;
            pose.HipsOffset.z = StanceHipsZ - (faceBlow ? 0.05f : 0.13f) * kv;

            // 前の足（左）は引きずられて浮き、後ろの足（右）で踏ん張る。
            float drag = Mathf.Clamp01(kv * 1.4f);
            pose.FootL += StanceFootL + new Vector3(0f, 0.035f * Mathf.Sin(Mathf.PI * drag), -0.09f * kv);
            pose.FootR += StanceFootR + new Vector3(0f, 0f, -0.05f * kv);

            // 構えが崩れる。前の手（左）は外へ弾け、後の手（右）は顔の前に残る
            // （とっさに顔を守ろうとしている＝痛みと抵抗が同時に見える）。
            pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper,
                new Vector3(0.86f, -0.34f, -0.22f), kv * flail);
            pose.ArmLFore = Vector3.Lerp(StanceArmLFore,
                new Vector3(0.40f, 0.42f, 0.22f), kv * flail);
            pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper,
                new Vector3(-0.62f, -0.52f, 0.06f), kv * flail * 0.6f);
            pose.ArmRFore = Vector3.Lerp(StanceArmRFore,
                new Vector3(-0.24f, 0.72f, 0.36f), kv * flail * 0.6f);

            // --- 殴られた場所ごとの崩れ方（上の共通の動きを上書きする）-----------------
            float hit = _stateTime;
            float hold = _tier switch
            {
                StaggerTier.Small => 0.10f,
                StaggerTier.Medium => 0.18f,
                _ => 0.32f
            };

            if (faceBlow)
            {
                // 顔を殴られたら、頭が先に振られ、胸と肩が遅れて付いてくる。腕は慣性で流れる。
                // 以前は両腕を肘から上へ跳ね上げ（万歳のような形）、腰を 9.5cm 落として屈んでいた。
                // 軽い一撃で全身が縮こまると「殴られた」より「驚いた」に見える（実測）。
                // 顔はカメラ側へ弾かれて横を向く（顔が読めるように）。
                float toCamera = 0f;
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 to = cam.transform.position - transform.position;
                    to.y = 0f;
                    Vector3 local = transform.InverseTransformDirection(to.normalized);
                    toCamera = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
                }
                float camSign = Mathf.Sign(toCamera == 0f ? 1f : toCamera);

                // 頭: 40ms で振り切り、少し行き過ぎてから戻る。
                float whipIn = Mathf.SmoothStep(0f, 1f, hit / 0.04f);
                float settleOut = 1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.42f);
                float overshoot = 1f + 0.18f * Mathf.Sin(Mathf.Clamp01((hit - 0.04f) / 0.14f) * Mathf.PI);
                float turn = whipIn * settleOut;

                // 胸・肩: 頭より 30ms 遅れて付いてくる（頭が先、体が後）。
                float chest = Mathf.SmoothStep(0f, 1f, (hit - 0.03f) / 0.07f) * settleOut;

                pose.HeadYaw = StanceHeadYaw + toCamera * 0.9f * turn * overshoot;
                pose.HeadRoll = -camSign * 14f * turn;
                pose.HeadPitch = StanceHeadPitch - 12f * turn;
                pose.TorsoPitch = StanceTorsoPitch - 9f * chest;
                pose.TorsoRoll = -camSign * 4.5f * chest;
                pose.TorsoYaw = StanceTorsoYaw
                    + Mathf.Clamp(14f * kv * side + toCamera * 0.22f * chest, -28f, 28f);

                // 腰は少しだけ引いて沈む。足が先に動くと「自分で下がった」ように見える。
                pose.HipsOffset.y = StanceHipsY - 0.035f * chest;
                pose.HipsOffset.z = StanceHipsZ - 0.065f * chest;
                pose.FootR += new Vector3(0f, 0f, -0.07f * chest);

                // 腕: 胸が回る反動で、前の手（左）は外へ流れ、後ろの手（右）は体の脇に残る。
                // 手が上がるのは、頭が戻り始めてから（庇う動き）。肘を上げて拳を上へ向けない。
                float guard = Mathf.SmoothStep(0f, 1f, (hit - 0.09f) / 0.11f) * settleOut;
                Vector3 flungUpperL = new Vector3(0.62f, -0.76f, -0.16f);
                Vector3 flungForeL = new Vector3(0.32f, -0.30f, 0.74f);
                Vector3 flungUpperR = new Vector3(-0.30f, -0.88f, 0.00f);
                Vector3 flungForeR = new Vector3(-0.06f, -0.22f, 0.96f);
                Vector3 guardUpperR = new Vector3(-0.28f, -0.62f, 0.40f);
                Vector3 guardForeR = new Vector3(0.18f, 0.52f, 0.66f);

                pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, flungUpperL, chest);
                pose.ArmLFore = Vector3.Lerp(StanceArmLFore, flungForeL, chest);
                pose.ArmRUpper = Vector3.Lerp(Vector3.Lerp(StanceArmRUpper, flungUpperR, chest), guardUpperR, guard);
                pose.ArmRFore = Vector3.Lerp(Vector3.Lerp(StanceArmRFore, flungForeR, chest), guardForeR, guard);
            }
            else if (bodyBlow)
            {
                // 腹を殴られたら、腰を引いて上体を「く」の字に折る。両腕で腹を抱えてうずくまる。
                float fold = Mathf.SmoothStep(0f, 1f, hit / 0.09f)
                             * (1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.45f));

                pose.TorsoPitch = StanceTorsoPitch + Mathf.Min(bend * 1.25f, 66f) * fold;
                pose.TorsoYaw = StanceTorsoYaw + 12f * kv * lateralSide;
                pose.TorsoRoll = 0f;
                pose.HeadPitch = StanceHeadPitch - Mathf.Min(bend * 1.15f, 64f) * fold;
                pose.HeadYaw = StanceHeadYaw;
                pose.HeadRoll = 0f;

                // 膝がガクッと落ちて沈み、片足を半歩引く（お辞儀に見せない）。
                pose.HipsOffset.y = StanceHipsY - 0.165f * fold;
                pose.HipsOffset.z = StanceHipsZ - 0.145f * fold;
                pose.FootL += new Vector3(0f, 0f, -0.075f * fold);

                // 両腕は左右対称に腹を抱え込む。
                var bellyUpperL = new Vector3(0.24f, -0.85f, 0.42f);
                var bellyForeL = new Vector3(-0.76f, 0.08f, 0.64f);
                pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, bellyUpperL, fold);
                pose.ArmLFore = Vector3.Lerp(StanceArmLFore, bellyForeL, fold);
                pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper, MirrorX(bellyUpperL), fold);
                pose.ArmRFore = Vector3.Lerp(StanceArmRFore, MirrorX(bellyForeL), fold);
            }

            pose.HandRollL = StanceHandRollL;
            pose.HandRollR = StanceHandRollR;
            pose.Grip = 0.55f;
            return pose;
        }

    }
}
