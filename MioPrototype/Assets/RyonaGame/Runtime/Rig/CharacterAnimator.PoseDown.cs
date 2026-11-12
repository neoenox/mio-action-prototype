using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        void ApplyFrontEscapePose(ref PoseValues pose, float waiting, float pulse)
        {
            if (waiting <= 0f)
                return;

            // ゆっくり力を込め、腕に止められて戻されてから反対側でもう一度引く。
            float time = Grapple.HeldSeconds;
            float firstPull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.82f, time))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.00f, 1.32f, time)));
            float secondPull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.32f, 2.02f, time))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.23f, 2.60f, time)));
            float effort = waiting * (0.65f + 0.35f * pulse);
            float retreat = (firstPull + secondPull) * effort;
            float twist = (firstPull - secondPull) * effort;
            pose.HipsOffset.z -= 0.085f * retreat;
            pose.HipsOffset.y -= 0.035f * retreat;
            pose.HipsOffset.x += 0.035f * twist;
            pose.TorsoPitch -= 16f * retreat;
            pose.TorsoYaw = pose.TorsoYaw * (1f - waiting) + 13f * twist;
            pose.TorsoRoll -= 8f * twist;
            pose.HeadYaw = pose.HeadYaw * (1f - waiting) - 11f * twist;
            pose.HeadPitch += 6f * retreat;
            pose.ArmLUpper.x += 0.23f * firstPull * effort;
            pose.ArmRUpper.x += 0.23f * secondPull * effort;
            pose.FootPitchL *= 1f - waiting;
            pose.FootPitchR *= 1f - waiting;
        }

        /// <summary>ダウン（仰向け / うつ伏せ / 死亡）。</summary>
        PoseValues PoseDown()
        {
            var pose = PoseDefaults();
            // 倒れる速さは状態の長さに依存させない（ダウン 2.0s / 気絶 1.2s / 死亡 ∞ の
            // どれでも同じ 0.95 秒で倒れきる）。以前は状態の長さで正規化していたため、
            // 死亡だけが 0.1 秒で瞬間的に倒れ、気絶は 1.2 秒かけてゆっくり倒れる逆転があった。
            float rise = Mathf.Clamp01(_stateTime / _downFallSeconds);

            // 崩れ落ちる: 最初に速く落ちて、最後にゆっくり収まる
            float settle = 1f - Mathf.Pow(1f - Mathf.Clamp01(rise), 2.2f);

            // 腰が先に落ち、少し遅れて背中が床へつく。
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.78f, rise));

            // 前へ崩れる（腹を殴られた）: 膝が抜けて前へ倒れ、うつ伏せになる。
            if (_downForward)
            {
                // 腰が落ちる（膝が抜ける）
                float drop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.66f, rise));
                // 体が前へ倒れる（0 → 92° でうつ伏せ）
                float lay = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.78f, rise));

                float lieY = 0.115f - Rig.RestHips.y;
                pose.HipsOffset.y = Mathf.Lerp(0f, lieY, drop);
                // うつ伏せでは腰が足より前へ出る（体が前へ伸びる）。
                // 仰向けのとき腰が 0.87m 後ろへ回るのと裏返しで、出さないと脚が畳まれる。
                pose.HipsOffset.z = Mathf.Lerp(0f, 0.78f, lay);

                pose.LayAngle = 92f * lay;
                pose.LaySide = 5f * lay;

                // 上体は腹を庇って折れ、着地してから脱力する
                pose.TorsoPitch = Mathf.Lerp(12f, 2f, lay);
                pose.TorsoRoll = 4f * lay;
                pose.HeadPitch = Mathf.Lerp(10f, -2f, lay);
                // 顔は横へ逃がす（伏せたまま正面を向くと前髪が顔を覆う）
                pose.HeadYaw = Mathf.Lerp(0f, 52f, lay);
                pose.HeadRoll = Mathf.Lerp(0f, -8f, lay);

                // 膝から落ちる。落ちきる前に脚を伸ばして、うつ伏せで真っ直ぐにする。
                float knees = Mathf.Sin(Mathf.Clamp01(rise / 0.72f) * Mathf.PI);
                pose.LegTuck = 0f;
                pose.FootL += new Vector3(0f, 0f, -0.06f * knees);
                pose.FootR += new Vector3(0f, 0f, -0.04f * knees);

                // 両手が腹へ寄る（腹パンの直後）→ 着地で床へ広がる
                pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, DownArmLUpper, drop);
                pose.ArmLFore = Vector3.Lerp(StanceArmLFore, DownArmLFore, drop);
                pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper, DownArmRUpper, drop);
                pose.ArmRFore = Vector3.Lerp(StanceArmRFore, DownArmRFore, drop);

                float hold = Mathf.Sin(Mathf.Clamp01(rise / 0.58f) * Mathf.PI);
                pose.ArmLUpper += new Vector3(-0.30f * hold, -0.10f * hold, 0.44f * hold);
                pose.ArmRUpper += new Vector3(0.30f * hold, -0.10f * hold, 0.44f * hold);

                // 参考のうつ伏せ: 片腕は頭の先へ伸ばし、もう片方は肘を張って手を顔の横へ。
                // 体を 92° 前に回した後で床に沿うのは立ち姿勢の「上」向きなので、
                // 上体の向きで組むと腕が宙に浮く（実測）。
                float spread = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, rise));
                Vector3 proneArmL = new Vector3(0.22f, 0.95f, 0.05f);    // 左腕は頭の先へ伸ばす
                Vector3 proneForeL = new Vector3(0.10f, 0.95f, 0.05f);
                Vector3 proneArmR = new Vector3(-0.88f, 0.30f, -0.06f);  // 右は肘を張る
                Vector3 proneForeR = new Vector3(0.62f, 0.66f, -0.04f);  // 前腕を内へ、手は顔の横

                pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, proneArmL, spread);
                pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, proneForeL, spread);
                pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, proneArmR, spread);
                pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, proneForeR, spread);

                pose.Grip = Mathf.Lerp(0.45f, 0.10f, spread);

                // 床に着いた「ドサッ」
                float landF = Mathf.Clamp01((lay - 0.88f) / 0.12f);
                float bump = Mathf.Sin(landF * Mathf.PI);
                pose.HipsOffset.y -= 0.012f * bump;

                // うつ伏せのあとの浅い息
                if (rise > 0.97f)
                    pose.HipsOffset.y += Mathf.Sin(_stateTime * 2.4f) * 0.003f;

                pose.HipsOffset.y = Mathf.Max(pose.HipsOffset.y, 0.115f - Rig.RestHips.y);

                _downEndHipsY = pose.HipsOffset.y;
                _downEndHipsZ = pose.HipsOffset.z;
                _downEndLay = 92f;   // 起き上がりの開始角度
                return pose;
            }

            // 腰が落ちる（片膝 → 仰向け）。最初の 0.2 秒で目に見えて沈むようにする
            // 仰向けに倒す（-90 で完全に水平）。
            // 真上を向いたままだと、重力で前髪が顔の上に落ちてくる。
            // 体ごと横向きに倒すと、前髪は顔の横（床）へ落ちるので顔が見える。
            pose.LayAngle = _downEndLay * fall;
            pose.LaySide = -30f * fall;

            // 空中はまだ倒しきらない。着地してから水平になる（0.25 秒で寄せる）。
            // 空中で -92° まで回すと、板のように飛んでいく画になる（実測）。
            if (_downLaunched)
            {
                float air = Motor != null && Motor.IsAirborne ? 1f : 0f;
                _airBlend = Mathf.MoveTowards(_airBlend, air, Time.deltaTime * 4f);
                float tilt = Mathf.Lerp(1f, 0.40f, _airBlend);
                pose.LayAngle *= tilt;
                pose.LaySide *= tilt;
            }

            // 崩れ落ちる: 足首を支点に腰が弧を描いて後ろへ倒れる。
            // 「膝をついてから倒れる」形は腰が低すぎて膝が床下へ潜り、
            // 回転後に膝が背中側へ跳ね上がる（人体に有り得ない形）ので使わない。
            // 倒れる途中は膝を少し曲げる。棒のように真っ直ぐ倒れると
            // 「板が倒れた」ように見えるので、足を少しだけ腰へ寄せる。
            // 倒れきると 0 に戻るので、寝た姿勢は脚を伸ばしたまま。
            float buckle = Mathf.Sin(Mathf.Clamp01(fall) * Mathf.PI) * 0.09f;
            pose.FootL += (Rig.RestHips - pose.FootL).normalized * buckle;
            pose.FootR += (Rig.RestHips - pose.FootR).normalized * buckle;

            // 足は倒れ始めに地面から離れる。かかとを支点に回したままだと
            // 「板を後ろへ倒した」画になり、ゲームのダウンに見えない（実測）。
            // 山なりに浮いて、倒れきる前に床へ戻る。
            float liftL = Mathf.Sin(Mathf.Clamp01((rise - 0.05f) / 0.62f) * Mathf.PI);
            float liftR = Mathf.Sin(Mathf.Clamp01((rise - 0.14f) / 0.64f) * Mathf.PI);
            pose.FootL += new Vector3(0f, 0.10f * liftL, 0.10f * liftL);
            pose.FootR += new Vector3(0f, 0.15f * liftR, 0.13f * liftR);

            // 支点は倒れ始めの足元で固定する。浮いた足を支点にすると
            // 腰の軌道が途中で折れて「宙返り」に見える。
            Vector3 pivot = (Rig.RestFootL + Rig.RestFootR) * 0.5f;
            Vector3 arcHips = pivot +
                Quaternion.AngleAxis(pose.LayAngle, Vector3.right) * (Rig.RestHips - pivot);
            pose.HipsOffset = arcHips - Rig.RestHips;

            // 倒れ始めに一度沈む（膝が抜ける感じ）。倒れきる前に戻す。
            pose.HipsOffset.y -= 0.17f * settle * (1f - fall);

            // 上体は前へ潰れてから、倒れるに従って脱力する
            pose.TorsoPitch = Mathf.Lerp(20f * settle, 3f, fall);
            pose.TorsoRoll = 6f * settle;
            pose.HeadPitch = Mathf.Lerp(8f * settle, -16f, fall);

            // 倒れている途中は背中を反らせ、頭を一拍遅らせる。
            // 一直線のまま回ると「板が倒れる」画になる（実測）。
            float arch = Mathf.Sin(Mathf.Clamp01(fall) * Mathf.PI);
            pose.TorsoPitch -= 15f * arch;
            pose.HeadPitch += 13f * arch;
            pose.TorsoYaw += 5f * arch;

            // 首を横に倒す。仰向けのまま正面を向くと、重力で前髪が顔の上に落ちてくる。
            // 横に倒すと重力が顔の面から横へ逃げるので、前髪が顔を覆わない。
            pose.HeadYaw = Mathf.Lerp(7f * settle, 15f, fall);

            // 左手は膝に添え、右腕は力なく垂れる。
            // 補間の出発点は「構えの腕」にする（T ポーズ由来の垂れ腕から始めると、
            // 殴られた瞬間に腕だけ別の場所へ飛ぶ）。
            pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, DownArmLUpper, settle);
            pose.ArmLFore = Vector3.Lerp(StanceArmLFore, DownArmLFore, settle);
            pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper, DownArmRUpper, settle);
            pose.ArmRFore = Vector3.Lerp(StanceArmRFore, DownArmRFore, settle);

            // 二次動作: 被弾で腕が前へ振られ、倒れるにつれて横へ開いて流れる。
            // 脱力姿勢へ補間するだけだと腕が最初から最後まで同じ場所にあり、
            // 「棒人間が回っている」ように見える。
            float whip = Mathf.Sin(Mathf.Clamp01(rise / 0.42f) * Mathf.PI);
            pose.ArmLUpper += new Vector3(0.10f * whip, 0.30f * whip, 0.50f * whip);
            pose.ArmRUpper += new Vector3(0.12f * whip, 0.10f * whip, 0.34f * whip);
            pose.ArmLFore += new Vector3(0f, 0.16f * whip, 0.20f * whip);
            pose.ArmRFore += new Vector3(0.30f * whip, 0.45f * whip, 0.24f * whip);

            // 倒れる途中で腕が横へ開く（体から離れて流れる＝力が抜けた画）
            float splay = Mathf.Sin(Mathf.Clamp01(fall) * Mathf.PI);
            pose.ArmLUpper += new Vector3(0.40f * splay, 0.10f * splay, 0.12f * splay);
            pose.ArmRUpper += new Vector3(-0.40f * splay, 0.08f * splay, 0.12f * splay);

            pose.Grip = 0.18f;

            // 吹き飛ばされている間は、体が後ろへ流れて脚が前へ残る。
            // 着地した瞬間にこの上乗せを外すので、地面に着いてから崩れる形になる。
            if (_downLaunched && Motor != null && Motor.IsAirborne)
            {
                float air = Mathf.Clamp01(_stateTime / Mathf.Max(0.05f, _downFallSeconds));
                pose.TorsoPitch -= 10f * (1f - air);
                pose.HeadPitch -= 8f * (1f - air);

                pose.LegTuck = 0f;
                pose.FootL += new Vector3(0f, 0.10f, 0.18f);
                pose.FootR += new Vector3(0f, 0.06f, 0.12f);
            }

            // 倒れきったあと、膝を少し立てる。棒のように真っ直ぐ伸びていると
            // 「置かれた人形」に見えるので、脱力して曲がった脚を残す。
            pose.LegTuck = 0.22f * fall;

            // 寝きったあとは片膝を立て、体を少し横へ流す。
            // 脚を伸ばしたまま真っ直ぐ寝ると「床に置いた板」に見える（実測）。
            float restT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, fall));
            pose.FootL += new Vector3(0.05f, 0.02f, -0.34f) * restT;   // 左膝を立てる（足は床のまま）
            pose.FootR += new Vector3(-0.03f, 0.03f, -0.08f) * restT;
            pose.ArmRUpper += new Vector3(-0.10f, 0.04f, 0.20f) * restT; // 右腕が腹の上へ落ちる
            pose.ArmRFore += new Vector3(0f, 0.04f, 0.26f) * restT;
            pose.ArmLUpper += new Vector3(0.14f, 0.02f, -0.10f) * restT; // 左腕は床へ流れる
            pose.TorsoRoll += 6f * restT;

            // 背中が床に着いた瞬間の「ドサッ」。ここが無いと、倒れ終わりが
            // ふわっと止まって「自分で寝た」ように見える。着地後に一度だけ沈む。
            // 着地の「ドサッ」は、体が床に着いた瞬間（fall=1）に合わせる。
            // rise 基準にすると、体がもう平らになった後に沈みが来て「二度落ちた」ように見える。
            float landT = Mathf.Clamp01((fall - 0.86f) / 0.14f);
            float land = Mathf.Sin(landT * Mathf.PI);
            pose.HipsOffset.y -= 0.018f * land;
            pose.TorsoRoll += Mathf.Sin(landT * 26f) * 2.6f * land;
            pose.HeadPitch -= 4f * land;

            // 倒れきったあとは浅い息。完全に止めると「止め絵」に見える。
            // 痛みで胸が小刻みに動く程度に留める（大きくすると寝返りに見える）。
            if (rise > 0.97f)
                pose.HipsOffset.y += Mathf.Sin(_stateTime * 2.6f) * 0.0035f;

            // 床下がりのクランプは最後に 1 回だけ。先に掛けると、着地の沈みと呼吸で
            // 背中が床を突き抜けたり、クランプ位置で腰の軌道が折れたりする。
            pose.HipsOffset.y = Mathf.Max(pose.HipsOffset.y, 0.11f - Rig.RestHips.y);

            // 立ち上がりはこの位置から始める（固定値だと倒れた位置と合わない）。
            _downEndHipsY = pose.HipsOffset.y;
            _downEndHipsZ = pose.HipsOffset.z;
            _downEndLay = -92f;   // 起き上がりの開始角度（現在値を入れると 0 に潰れる）
            return pose;
        }

        /// <summary>起き上がり。手をつく → 片膝 → 立ち。</summary>
        PoseValues PoseGetUp()
        {
            var pose = PoseDefaults();
            // 倒れた姿勢から、横に転がって上体を起こし、立ち上がる。
            float u = _stateDuration > 100f
                ? 1f
                : Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _stateDuration));

            // うつ伏せからの起き上がりは別物:
            //   1) 両手を頭の横について上体を押し上げる
            //   2) 片膝をつく
            //   3) 立ち上がる
            // 仰向けの「横に転がって座る」曲線を使うと、腹這いのまま背中側へ
            // 転がる不自然な動きになる（実測）。
            if (_downForward)
            {
                float press = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.00f, 0.34f, u));
                float knee = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.66f, u));
                float stand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1.00f, u));

                // 体の角度: うつ伏せ 92 → 押し上げ 54 → 膝立ち 28 → 立つ 0
                pose.LayAngle = Mathf.Lerp(92f, 54f, press);
                pose.LayAngle = Mathf.Lerp(pose.LayAngle, 28f, knee);
                pose.LayAngle = Mathf.Lerp(pose.LayAngle, 0f, stand);
                pose.LaySide = 5f * (1f - press);

                // 腰: 前へ出たまま（0.78）→ 膝の上（0.30）→ 立ち位置（0）
                pose.HipsOffset.z = Mathf.Lerp(0.78f, 0.34f, knee);
                pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ, stand);

                float lieY = 0.115f - Rig.RestHips.y;
                float kneelY = 0.46f - Rig.RestHips.y;
                float y = Mathf.Lerp(lieY, kneelY, knee);
                pose.HipsOffset.y = Mathf.Lerp(y, StanceHipsY, stand);

                // 片膝（右）を前へ出す。つま先は床、膝は腰の下。
                float kneelFoot = Mathf.Lerp(0f, 0.34f, knee);
                pose.FootR += new Vector3(0f, 0f, kneelFoot);
                pose.FootR += new Vector3(0f, 0f, -kneelFoot) * stand;
                pose.FootL += new Vector3(0f, 0f, 0.10f) * knee;
                pose.FootL += new Vector3(0f, 0f, -0.10f) * stand;
                pose.FootPitchR = Mathf.Max(pose.FootPitchR, 0f);

                // 上体: 押し上げで顔が上がり、立ち上がりで構えへ
                pose.TorsoPitch = Mathf.Lerp(2f, 24f, press);
                pose.TorsoRoll = 4f * (1f - press);
                pose.HeadPitch = Mathf.Lerp(-2f, -10f, press);
                pose.HeadYaw = Mathf.Lerp(52f, 18f, press);
                pose.HeadRoll = -6f * (1f - press);

                // 腕: 頭の横について押す → 立ち上がりで構えへ戻す
                Vector3 plantArmL = new Vector3(0.66f, 0.58f, 0.02f);
                Vector3 plantArmR = new Vector3(-0.66f, 0.58f, 0.02f);
                Vector3 plantForeL = new Vector3(0.22f, 0.72f, -0.06f);
                Vector3 plantForeR = new Vector3(-0.22f, 0.72f, -0.06f);

                pose.ArmLUpper = Vector3.Lerp(DownArmLUpper, plantArmL, press);
                pose.ArmLFore = Vector3.Lerp(DownArmLFore, plantForeL, press);
                pose.ArmRUpper = Vector3.Lerp(DownArmRUpper, plantArmR, press);
                pose.ArmRFore = Vector3.Lerp(DownArmRFore, plantForeR, press);

                pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, StanceArmLUpper, stand);
                pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, StanceArmLFore, stand);
                pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, StanceArmRUpper, stand);
                pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, StanceArmRFore, stand);

                pose.Grip = Mathf.Lerp(0.10f, 0.95f, stand);
                pose.LegTuck = 0f;

                // 最後は構えの数値へ着地させる（切り替わりで跳ねないように）
                float toStanceF = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.70f, 1f, u));
                pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY, toStanceF);
                pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ, toStanceF);
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, StanceTorsoPitch, toStanceF);
                pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, StanceTorsoYaw, toStanceF);
                pose.TorsoRoll = Mathf.Lerp(pose.TorsoRoll, 0f, toStanceF);
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, StanceHeadPitch, toStanceF);
                pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, StanceHeadYaw, toStanceF);
                pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, 0f, toStanceF);

                pose.HipsOffset.y = Mathf.Max(pose.HipsOffset.y, 0.10f - Rig.RestHips.y);
                return pose;
            }

            float sit = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.60f, u));
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1.00f, u));

            // 仰向け(-88) → 上体を起こす(-40) → 立ち(0)
            // 横への倒れ込みは増やさない（増やすと体がひねれて側転に見える）
            pose.LayAngle = -92f + 52f * sit + 40f * rise;
            pose.LaySide = -30f * (1f - rise);

            pose.HipsOffset.y = Mathf.Lerp(_downEndHipsY, StanceHipsY, rise) + 0.08f * sit * (1f - rise);

            // 腰は rise で一気に寄せない。寝た姿勢では腰が 0.87m 後ろにあり、
            // 最後にまとめて寄せると立ち上がる瞬間に腰が滑って「ワープ」に見える。
            // 座っている間に少しずつ足元へ寄せると、股関節と足が近づいて
            // 膝が自然に立ってくる（かかとを引き寄せる動き）。
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.88f, u));
            pose.HipsOffset.z = Mathf.Lerp(_downEndHipsZ, StanceHipsZ, Mathf.Max(slide, rise));

            // 起き上がるときの「腹筋」。rise で戻す
            pose.TorsoPitch = 3f * (1f - rise) + 26f * sit * (1f - rise);
            pose.TorsoRoll = 6f * (1f - rise);
            pose.HeadPitch = -9f * (1f - rise);
            pose.HeadYaw = 15f * (1f - rise);

            // 手を地面について押し上げる。上体が起きたら素早く体の前へ戻す。
            // ただし「寝た姿勢 → 構え」を一気に補間すると、途中で腕が
            // だらんと垂れて「気絶から起きた人形」に見える。腰の横に手を突いて
            // 押す形を一度挟み、そこから構えへ戻す。
            Vector3 pushLUpper = new Vector3(0.42f, -0.80f, -0.42f);
            Vector3 pushLFore = new Vector3(0.10f, -0.86f, -0.50f);

            float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.32f, u));
            float leave = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.84f, u));

            Vector3 armLUpperPush = Vector3.Lerp(DownArmLUpper, pushLUpper, plant);
            Vector3 armLForePush = Vector3.Lerp(DownArmLFore, pushLFore, plant);
            Vector3 armRUpperPush = Vector3.Lerp(DownArmRUpper, Mirror(pushLUpper), plant);
            Vector3 armRForePush = Vector3.Lerp(DownArmRFore, Mirror(pushLFore), plant);

            // 腕の行き先は「構え」の腕。Idle（腕が下がった形）へ戻すと、
            // 立ち上がりきった瞬間に構えへ跳ねる。
            pose.ArmLUpper = Vector3.Lerp(armLUpperPush, StanceArmLUpper, leave);
            pose.ArmLFore = Vector3.Lerp(armLForePush, StanceArmLFore, leave);
            pose.ArmRUpper = Vector3.Lerp(armRUpperPush, StanceArmRUpper, leave);
            pose.ArmRFore = Vector3.Lerp(armRForePush, StanceArmRFore, leave);

            pose.Grip = 0.18f * (1f - rise);

            // 途中で膝を畳む（脚が棒のまま回らないように）
            pose.LegTuck = Mathf.Sin(Mathf.Clamp01((u - 0.10f) / 0.72f) * Mathf.PI) * 0.95f;
            pose.HipsOffset.y += 0.06f * pose.LegTuck;   // 畳んだ分だけ腰を浮かせて足のめり込みを防ぐ

            // 上体を起こす間は足を前へ出す。真下に置いたままだと
            // 腰が低いぶん膝が限界まで畳まれ（160°超）、すねが腿に刺さる。
            // ただし出しすぎると脚が伸びきって「体育座り」に見えるので、
            // 膝が曲がって見える範囲に留め、rise が始まる前に戻す
            // （戻すのが遅いと、立ち上がる瞬間に足が体の前から下へ飛ぶ）。
            float feetOut = Mathf.Sin(Mathf.Clamp01((u - 0.14f) / 0.44f) * Mathf.PI);
            pose.FootL += new Vector3(0f, 0f, 0.22f) * feetOut;
            pose.FootR += new Vector3(0f, 0f, 0.25f) * feetOut;

            // 立ち上がりきる直前、少し沈んでから伸びる（地面を踏む感触）
            float settle2 = Mathf.Sin(Mathf.Clamp01((u - 0.78f) / 0.22f) * Mathf.PI);

            // 手で床を押す山場。上体が起きる前に腕を突っ張る（押し上げている感じ）。
            float push = Mathf.Sin(Mathf.Clamp01((u - 0.30f) / 0.28f) * Mathf.PI);

            // 最後は構えの数値へ着地させる。状態が切り替わる瞬間に
            // 腰や上体が数センチ跳ぶのを消す。
            float toStance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.95f, u));
            pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY, toStance);
            pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ, toStance);
            pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, StanceTorsoPitch, toStance);
            pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, StanceTorsoYaw, toStance);
            pose.TorsoRoll = Mathf.Lerp(pose.TorsoRoll, 0f, toStance);
            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, StanceHeadPitch, toStance);
            pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, StanceHeadYaw, toStance);
            pose.Grip = Mathf.Lerp(pose.Grip, 0.95f, toStance);

            // 沈み込みと踏ん張りは、着地した姿勢の上に乗せる（最後は 0 に戻る）
            pose.HipsOffset.y -= 0.045f * settle2;
            pose.HipsOffset.y += 0.018f * push;

            // 立ち上がった直後、足元が定まらず小さくよろける。
            // 最後は 0 に戻すので、構えへは継ぎ目なく繋がる。
            float wobble = Mathf.Clamp01((u - 0.84f) / 0.16f);
            float wob = Mathf.Sin(wobble * 15f) * 3.2f * wobble * (1f - wobble) * 2f;
            pose.TorsoRoll += wob;
            pose.HeadRoll += wob * 0.8f;
            pose.HeadPitch += Mathf.Sin(wobble * 19f) * 2.0f * wobble * (1f - wobble) * 2f;
            return pose;
        }

    }
}
