using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        /// <summary>拘束（後ろ / 正面 / ベアハッグ / 押さえつけ）。</summary>
        PoseValues PoseGrabbedDazed()
        {
            var pose = PoseDefaults();
            pose.FootL += StanceFootL;
            pose.FootR += StanceFootR;
            // 腕は体の後ろへ。下ろすだけだと相手の腕を掴み返す形と区別しづらい。
            pose.ArmLUpper = new Vector3(0.30f, -0.55f, -0.55f);
            pose.ArmLFore = new Vector3(0.15f, -0.35f, -0.85f);
            pose.ArmRUpper = new Vector3(-0.30f, -0.55f, -0.55f);
            pose.ArmRFore = new Vector3(-0.15f, -0.35f, -0.85f);
            pose.TorsoPitch = 8f;
            pose.HeadPitch = 2f;
            pose.HeadRoll = 7f;
            pose.Grip = 0.3f;
            return pose;
        }

        PoseValues PoseGrabbed()
        {
            // 引き起こしから直接入った拘束は、腕を下ろしたまま固定する。
            // 通常の拘束ポーズは「相手の腕を掴み返す」ので、髪から離した手がまた頭へ行く。
            if (Grapple != null && Grapple.HaulHandoff)
                return PoseGrabbedDazed();

            var pose = PoseDefaults();
            if (Grapple != null && Grapple.IsHandingOff)
            {
                // 押された間は腕が自由。受け止められたら膝を折り、両腕で釣り合いを取る。
                var receiver = Grapple.Captor;
                bool caught = receiver != null && receiver.IsCatching;
                float impact = caught ? receiver.CatchImpact : 0f;
                float push = Grapple.PushPulse;
                Vector3 localPush = transform.InverseTransformDirection(Grapple.PushDirection);
                pose.HipsOffset.y = StanceHipsY - (caught ? 0.08f : 0.035f + 0.07f * push);
                pose.HipsOffset.z = StanceHipsZ + 0.05f * push * localPush.z;
                pose.TorsoPitch = caught ? -6f : 4f + 33f * push * localPush.z;
                pose.TorsoRoll = -18f * push * localPush.x;
                pose.HeadPitch = caught ? 6f : -4f - 15f * push * localPush.z;
                pose.HipsOffset -= localPush * (0.045f * impact);
                pose.HipsOffset.y -= 0.035f * impact;
                pose.TorsoPitch -= 9f * impact * localPush.z;
                pose.TorsoRoll += 15f * impact * localPush.x;
                pose.ArmLUpper = pose.ArmRUpper = new Vector3(0.58f, -0.80f, 0.10f - 0.63f * push * localPush.z);
                pose.ArmLFore = pose.ArmRFore = new Vector3(0.10f, -0.30f - 0.22f * push, 0.94f - 0.45f * push);
                pose.FootL += StanceFootL;
                pose.FootR += StanceFootR;
                if (Grapple.RecoveryPoseWeight > 0f)
                {
                    float step = Grapple.RecoveryStep;
                    float blend = Grapple.RecoveryPoseWeight;
                    float momentum = Mathf.InverseLerp(1.15f, 0f, step) * blend;
                    float rush = Mathf.InverseLerp(0.85f, 1.65f, step) * (caught ? impact : 1f);
                    pose.HandOffBrace = caught
                        ? (1f - Grapple.HandOffTurn) * blend
                        : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.05f, 1.75f, step));
                    pose.Grip *= 1f - pose.HandOffBrace;
                    float flight = Mathf.Sin(Mathf.PI * Mathf.Clamp01(step));
                    float swing = Mathf.InverseLerp(1.02f, 1.88f, step);
                    float imbalance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.34f, step))
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.78f, 1.38f, step)));
                    float travelled = Grapple.RecoveryTravel;
                    float landing = PlayerGrapple.ShoveLandingFraction * Grapple.RecoveryDistance;
                    float planted = Mathf.Min(0f, landing - travelled);
                    float plantR = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, step));
                    // 後ろ足が残っている間に、右膝と足先を前へ出して転倒を防ぐ。
                    float reflex = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.26f, step))
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.94f, step)));
                    float reflexReach = Mathf.Min(0.24f, Grapple.RecoveryDistance * 0.21f) * reflex;
                    float rightReach = Mathf.Lerp(planted, landing - travelled, plantR) + 0.065f + reflexReach;
                    float footZ = (pose.FootL.z + pose.FootR.z) * 0.5f;
                    Vector3 launchFoot = pose.FootL - localPush * travelled - Vector3.up * Grapple.RecoveryLift;
                    // 右足を先に置いて体を支え、後ろに残った左足が追いつく。
                    float reach = Grapple.RecoveryDistance - landing;
                    float leftReach = Mathf.SmoothStep(-0.14f - 0.09f * flight, reach, swing);
                    pose.FootL += blend * (localPush * (planted + leftReach)
                        + new Vector3(-0.045f * imbalance, 0.055f * flight + 0.075f * Mathf.Sin(Mathf.PI * swing), footZ - pose.FootL.z));
                    pose.FootR += blend * (localPush * rightReach
                        + new Vector3(0.025f * reflex, 0.10f * reflex, footZ - pose.FootR.z));
                    // 押された直後の左足は床に残し、かかとから遅れて離す。
                    float releaseFoot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.34f, step));
                    pose.FootL = Vector3.Lerp(launchFoot, pose.FootL, releaseFoot);
                    pose.FootPitchL += 14f * Mathf.Sin(Mathf.PI * releaseFoot);
                    float landingLoad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1.12f, step))
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.12f, 1.62f, step)));
                    pose.HipsOffset.y -= 0.072f * landingLoad;
                    pose.TorsoPitch += 8f * landingLoad * localPush.z;
                    pose.TorsoPitch += 12f * momentum * localPush.z;
                    pose.TorsoPitch += 14f * rush * localPush.z;
                    pose.TorsoRoll += 11f * imbalance;
                    pose.HipsOffset.x += 0.045f * imbalance;
                    pose.HeadPitch -= 9f * pose.HandOffBrace;
                    pose.ArmLUpper.x += 0.30f * imbalance;
                    pose.ArmRUpper.z -= 0.36f * imbalance;
                    pose.ArmLFore.z -= 0.38f * momentum;
                    pose.ArmRFore.z -= 0.38f * momentum;
                }
                return pose;
            }

            // 相手に掴まれ、引かれている。上体は反り、両手で相手の腕を掴もうとする。
            // もがくたびに肘が開き、掴もうとする手が跳ねる（レバガチャの手応えを体で返す）。
            float pull = 1f - Mathf.Exp(-_stateTime * 10f);
            float pulse = StrugglePulseOverride >= 0f
                ? StrugglePulseOverride
                : (Grapple != null ? Grapple.StrugglePulse : 0f);
            float frontWait = Grapple != null ? Grapple.FrontEscapePoseWeight : 0f;
            if (frontWait > 0f)
                pulse = Mathf.Lerp(pulse, Grapple.FrontEscapeEffort, frontWait);
            // 細かく震えるのではなく、「逃げようと体を引く → 掴まれて止められる → 戻される」を
            // ゆっくり繰り返す。逃げたいのに逃げられない、が体の動きで見えるようにする。
            // 連打すると、引く力が強くなって、止められる衝撃も大きくなる。
            float cycle = Mathf.Repeat(_stateTime * 0.9f, 1f);
            float ramp = cycle < 0.62f
                ? Mathf.SmoothStep(0f, 1f, cycle / 0.62f)                       // じわじわ引く
                : 1f - Mathf.SmoothStep(0f, 1f, (cycle - 0.62f) / 0.12f);       // 止められて引き戻される
            float strain = Mathf.Clamp01(ramp * 0.6f + pulse * 0.55f) * pull;
            strain *= 1f - 0.8f * frontWait;

            if (Grapple != null && Grapple.IsWallTransition)
            {
                float t = Mathf.SmoothStep(0f, 1f, Grapple.WallPhaseProgress);
                var stage = Grapple.WallPhase;
                pose.FootL += StanceFootL;
                pose.FootR += StanceFootR;
                pose.TorsoPitch = 8f;
                pose.HeadPitch = 6f;
                pose.HeadYaw = 0f;
                pose.TorsoYaw = 0f;
                pose.ArmLUpper = pose.ArmRUpper = new Vector3(0.50f, -0.20f, 0.55f);
                pose.ArmLFore = pose.ArmRFore = new Vector3(-0.15f, 0.25f, 0.80f);
                if (stage == WallStage.Throw)
                {
                    float flight = Mathf.Sin(Mathf.PI * t);
                    pose.TorsoPitch = -14f * flight - 8f * t;
                    pose.HeadPitch = 12f * flight + 10f * t;
                    pose.HipsOffset.y += 0.04f * flight;
                    pose.FootL += new Vector3(0f, 0.12f * flight, -0.10f * flight);
                    pose.FootR += new Vector3(0f, 0.08f * flight, -0.14f * flight);
                    pose.ArmLUpper.y = pose.ArmRUpper.y = 0.20f;
                }
                else if (stage == WallStage.TossWindup)
                {
                    pose.TorsoPitch = Mathf.Lerp(-7f, 12f, t);
                    pose.HeadPitch = Mathf.Lerp(6f, 16f, t);
                    pose.HipsOffset.y = -0.12f;
                    pose.HipsOffset.z = -0.04f;
                }
                else if (stage == WallStage.TossAir)
                {
                    float hang = Mathf.Sin(Mathf.PI * Grapple.WallPhaseProgress);
                    pose.TorsoPitch = Mathf.Lerp(12f, 24f, t) - 31f * hang;
                    pose.TorsoRoll = -Grapple.WallTossSide * 17f * hang;
                    pose.HeadPitch = 12f + 9f * hang;
                    pose.HipsOffset.y = -0.12f + 0.10f * hang;
                    pose.FootL += new Vector3(0f, 0.12f * hang, 0.18f * t);
                    pose.FootR += new Vector3(0f, 0.19f * hang, -0.12f * t);
                    pose.ArmLUpper.x += 0.36f * hang;
                    pose.ArmRUpper.x += 0.52f * hang;
                }
                else if (stage == WallStage.TossLand)
                {
                    float absorb = Mathf.Sin(Mathf.PI * t);
                    pose.TorsoPitch = Mathf.Lerp(24f, 6f, t) + 14f * absorb;
                    pose.HeadPitch = Mathf.Lerp(12f, 4f, t);
                    pose.HipsOffset.y = Mathf.Lerp(-0.12f, StanceHipsY, t) - 0.14f * absorb;
                    pose.FootL.z += 0.18f;
                    pose.FootR.z -= 0.12f;
                }
                else if (stage >= WallStage.Slam)
                {
                    float slide = Grapple.WallSlideWeight;
                    float lift = Grapple.WallRestraintWeight;
                    float impact = stage == WallStage.Slam ? Mathf.Sin(Mathf.PI * t) : 0f;
                    // 腰の沈みに合わせて背中を壁へ預け、足を前に出して座面を作る。
                    // 膝だけを曲げると座面のない「空気椅子」に見える（実測）。
                    pose.TorsoPitch = Mathf.Lerp(-8f, 8f, slide) - 10f * impact;
                    pose.HeadPitch = Mathf.Lerp(Mathf.Lerp(10f, 6f, lift), 14f, slide) + 12f * impact;
                    pose.HipsOffset.y -= Mathf.Lerp(0.06f, 0.38f, slide) + 0.04f * impact;
                    pose.HipsOffset.z -= 0.04f + 0.12f * slide;
                    float armsDrop = stage == WallStage.Slam ? t : 1f;
                    pose.ArmLUpper = pose.ArmRUpper = Vector3.Lerp(
                        new Vector3(0.50f, 0.20f, 0.55f), new Vector3(0.30f, -0.75f, 0.10f), armsDrop);
                    pose.ArmLFore = pose.ArmRFore = Vector3.Lerp(
                        new Vector3(-0.15f, 0.25f, 0.80f), new Vector3(-0.10f, -0.75f, 0.45f), armsDrop);
                    pose.HipsOffset.y -= 0.06f * lift;
                    pose.FootL.z += 0.12f + 0.30f * slide;
                    pose.FootR.z += 0.08f + 0.34f * slide;
                }
                return pose;
            }

            // 掴まれた向きで姿勢を作り替える。背後は羽交い締め（Back）、
            // 正面の押さえつけ（Pin）、正面から胴を抱えるベアハッグ。
            if (Grapple != null && Grapple.Kind == GrappleKind.AnkleGrab)
            {
                // 足取り。後ろから左の足首を掴まれて引かれ、前へつんのめる。
                // 掴まれた足（左）は後ろへ持ち上がり、軸足（右）で踏ん張る。
                // 上体は前へ倒れ、両腕は前へ出てバランスを取る。もがくと腕が大きく振れる。
                pose.HipsOffset.y = StanceHipsY - 0.05f * pull - 0.03f * strain;
                pose.HipsOffset.z = StanceHipsZ + 0.05f * pull;

                float camSide = CameraYawToMe() >= 0f ? 1f : -1f;
                pose.TorsoPitch = 18f * pull + 10f * strain;
                pose.TorsoYaw = camSide * 16f * pull + Mathf.Sin(Time.time * 3.1f) * 3f;
                pose.TorsoRoll = 0f;

                // 後ろから足首を引かれ、カメラ側の肩越しに怯えて振り返る（うつむくと表情が隠れる）。
                pose.HeadPitch = -14f * pull;
                pose.HeadYaw = camSide * 34f * pull + Mathf.Sin(Time.time * 4.6f) * 4f;
                pose.HeadRoll = camSide * 8f * pull;

                var reachUpper = new Vector3(0.50f, -0.20f, 0.55f);
                var reachFore = new Vector3(-0.15f, 0.25f, 0.80f);
                float flail = 0.16f * pulse + 0.10f * strain;
                pose.ArmLUpper = reachUpper + new Vector3(0.20f * flail, flail, 0f);
                pose.ArmRUpper = reachUpper + new Vector3(0.20f * flail, flail * 0.8f, 0f);
                pose.ArmLFore = reachFore + new Vector3(0f, flail * 1.2f, 0f);
                pose.ArmRFore = reachFore + new Vector3(0f, flail, 0f);

                pose.HandRollL = StanceHandRollL;
                pose.HandRollR = StanceHandRollR;
                pose.Grip = 0.3f;

                pose.FootL += StanceFootL;
                pose.FootR += StanceFootR;
                pose.FootL += new Vector3(0f, 0.22f * pull, -0.36f * pull);
                pose.FootPitchL = -24f * pull;
                pose.FootPitchR = 12f * pull;
                if (HeldBellyPunchFold > 0f || HeldFacePunchImpulse > 0f)
                {
                    pose.TorsoPitch += 16f * HeldBellyPunchFold;
                    pose.HeadPitch -= 12f * HeldBellyPunchFold;
                    pose.HeadYaw += camSide * 32f * HeldFacePunchImpulse;
                }
                return pose;
            }

            if (Grapple != null && Grapple.Kind == GrappleKind.Back)
            {
                float toCamera = CameraYawToMe();
                float comboDangle = HeldComboDangle;

                // 羽交い締め。相手の腕が首に回り、上体を後ろへ反らされる。
                // 腰は相手側（後ろ）へ引かれ、膝を折って踏ん張る。
                // 腰を沈めないと脚が突っ張って「吊られている」画になる。
                pose.HipsOffset.y = StanceHipsY - 0.075f * pull;
                pose.HipsOffset.z = StanceHipsZ - 0.02f * pull;

                // 羽交い締め: 相手の腕が脇の下をくぐって首の後ろで組まれる。
                // 彼女の腕は、その腕に押し上げられて肘が横へ大きく開き、
                // 前腕は力なく体の前へ垂れる（抗って、相手の腕を掴もうとする）。
                // 頭と上体は後ろから押されて前へ折れ、顎が引ける。苦悶の形。
                pose.TorsoPitch = 6f * pull;
                pose.TorsoYaw = Mathf.Sin(Time.time * 2.6f) * 4f;
                pose.TorsoRoll = 0f;

                pose.HeadPitch = 16f * pull;
                pose.HeadYaw = Mathf.Sin(Time.time * 3.8f) * 5f;
                pose.HeadRoll = 0f;

                // 上腕は肩の高さで真横へ、前腕は体の前へ向けて下へ垂らす。
                // もがくと肘が上がり、前腕が相手の腕を掴みに跳ね上がる。
                var nelsonUpper = new Vector3(0.95f, 0.26f, 0.12f);
                var nelsonFore = new Vector3(-0.35f, -0.45f, 0.82f);
                float claw = 0.14f * pulse;

                pose.ArmLUpper = nelsonUpper + new Vector3(0f, claw, 0f);
                pose.ArmRUpper = nelsonUpper + new Vector3(0f, claw * 0.6f, 0f);
                pose.ArmLFore = nelsonFore + new Vector3(0f, claw * 1.6f, claw);
                pose.ArmRFore = nelsonFore + new Vector3(0f, claw * 1.2f, claw * 0.8f);

                // 逃げようと前へ体を投げ出す（腕は押さえられたまま、体だけが相手から離れようとする）。
                float backStrain = strain * (1f - 0.85f * comboDangle);
                pose.TorsoPitch += 9f * backStrain;
                pose.HeadPitch += 5f * backStrain;
                pose.HipsOffset.z += 0.05f * backStrain;
                pose.ArmLUpper += new Vector3(0f, 0.12f * backStrain, 0f);
                pose.ArmRUpper += new Vector3(0f, 0.12f * backStrain, 0f);

                pose.HandRollL = StanceHandRollL;
                pose.HandRollR = StanceHandRollR;
                pose.Grip = 1f;

                // 前の足（左）は引かれてかかとが浮き、つま先で踏ん張る。
                // 後ろの足（右）は残して全体重を支える。
                pose.FootL += StanceFootL;
                pose.FootR += StanceFootR;
                pose.FootL += new Vector3(0f, 0f, 0.05f);
                pose.FootPitchL = 30f * pull;

                // 投げられて正面の敵に差し出される体勢。ここでは折れない
                // （折れると「殴られた」に見える）。腰を引いて腹を前へ出し、顔は上げる。
                // 一発目が当たると、下の被弾姿勢が重なる。
                float ready = HeldThrowLead;
                if (ready > 0f)
                {
                    pose.TorsoPitch += 12f * ready;
                    pose.HeadPitch -= 4f * ready;
                    pose.HipsOffset.y -= 0.02f * ready;
                    pose.HipsOffset.z -= 0.05f * ready;
                }

                // 殴りに来る敵を見る。うつむいたままだと「何が来るか分かっている」怯えが出ない。
                // 最初に気づいたときの角度で顔を上げ、拳が出る直前だけ視線を外す。
                float dread = _dreadLook * (1f - comboDangle);
                if (dread > 0.001f)
                {
                    // 首の角度は上体に対して付くので、前傾ぶんを差し引いて相手の顔へ合わせる。
                    // 少しだけ顎を引く（+4°）。真っ直ぐ見上げると睨んでいるように見える。
                    float wantPitch = Mathf.Clamp(_dreadPitch - pose.TorsoPitch + 4f, -6f, 25f);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, wantPitch, dread);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, _dreadYaw, dread);

                    // 首をかしげて身を縮める。相手から体を引く（上体を少し起こす）。
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, 7f, dread);
                    pose.TorsoPitch -= 4f * dread;
                    pose.HipsOffset.z -= 0.015f * dread;

                    // 怯えの震え。大きいと「寒い」に見えるので 1° 程度。
                    pose.HeadYaw += Mathf.Sin(Time.time * 13.0f) * 1.1f * dread;
                    pose.HeadPitch += Mathf.Sin(Time.time * 9.7f + 1.3f) * 0.8f * dread;
                }

                // フィニッシュ後（腹パン / 顔パン共通）のグロッキー土台: 脇の下を敵の腕に掬い上げられたまま意識朦朧と脱力する。
                // 後続の通常パンチ（下の腹/顔リアクション）をこの上に重ねて、打たれたら反応し、収まったら再び項垂れるようにする。
                if (comboDangle > 0f)
                {
                    float limpSide = toCamera >= 0f ? 1f : -1f;
                    float weakStruggle = 0.06f * pulse;
                    pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, 0f, comboDangle)
                        + Mathf.Sin(Time.time * 22f) * 3f * pulse * comboDangle;
                    pose.TorsoRoll = Mathf.Lerp(pose.TorsoRoll, limpSide * 3.5f, comboDangle);
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 11f, comboDangle);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 12f, comboDangle);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw,
                        Mathf.Clamp(toCamera * 0.30f, -16f, 16f), comboDangle * 0.75f);
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, limpSide * 11f, comboDangle);
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY - 0.115f, comboDangle);
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ - 0.03f, comboDangle);
                    var dangleUpper = new Vector3(0.95f, 0.26f + weakStruggle, 0.14f);
                    var dangleFore = new Vector3(0.12f, -0.86f + weakStruggle * 1.4f, 0.46f + weakStruggle);
                    pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, dangleUpper, comboDangle);
                    pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, dangleUpper, comboDangle);
                    pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, dangleFore, comboDangle);
                    pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, dangleFore, comboDangle);
                    pose.FootPitchL = Mathf.Lerp(pose.FootPitchL, 28f, comboDangle);
                    pose.FootPitchR = Mathf.Lerp(pose.FootPitchR, 22f, comboDangle);
                    pose.Grip = Mathf.Lerp(pose.Grip, 0.12f, comboDangle);
                }

                // グロッキー後の引き起こし晒し: 拘束役が脇の下をグッと持ち上げ、無防備な腹・顔を正面へ晒す。
                float hoist = FinisherHoistWeight * (1f - Mathf.Clamp01(Mathf.Max(HeldBellyPunchFold, HeldFacePunchImpulse) * 1.5f));
                if (hoist > 0.001f)
                {
                    float limpSide = toCamera >= 0f ? 1f : -1f;
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, -5f, hoist);
                    pose.TorsoRoll = Mathf.Lerp(pose.TorsoRoll, limpSide * 2.5f, hoist);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 9f, hoist);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw,
                        Mathf.Clamp(toCamera * 0.32f, -18f, 18f), hoist);
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, limpSide * 12f, hoist);
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY + HoistHipRise, hoist);
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ + 0.045f, hoist);
                    // 足先の IK 目標も一緒に持ち上げる（腰だけだと脚が伸びるだけで床に残る）。
                    // 腰より少し多めに上げて膝を曲げ、つま先を下にして「ぶら下がって」見せる。
                    float liftFeet = HoistFootRise * hoist;
                    float hoistSway = Mathf.Sin(Time.time * 2.2f) * 0.012f * hoist;
                    pose.FootL = new Vector3(pose.FootL.x, pose.FootL.y + liftFeet, pose.FootL.z + hoistSway);
                    pose.FootR = new Vector3(pose.FootR.x, pose.FootR.y + liftFeet, pose.FootR.z - hoistSway);
                    pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, new Vector3(0.95f, 0.28f, 0.14f), hoist);
                    pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, new Vector3(0.95f, 0.28f, 0.14f), hoist);
                    pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, new Vector3(0.08f, -0.86f, 0.44f), hoist);
                    pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, new Vector3(0.08f, -0.86f, 0.44f), hoist);
                    pose.FootPitchL = Mathf.Lerp(pose.FootPitchL, 34f, hoist);
                    pose.FootPitchR = Mathf.Lerp(pose.FootPitchR, 28f, hoist);
                }

                // 腹パン: 腹で一度折れた後、拳の二段めり込みに合わせて腰を引きながら深く二段折れする。
                bool bodyCombo = _punchDirector != null && _punchDirector.IsBodySequence;
                float recoil = HeldBellyPunchFold;
                float lift = HeldBellyCaptorLift;
                if (recoil > 0f || lift > 0f)
                {
                    float hitTime = _heldBellyPunchTotal - _heldBellyPunchRemaining;
                    float bendIn = Mathf.SmoothStep(0.60f, 1f, hitTime / 0.045f);
                    float fold = Mathf.Lerp(_heldBellyCarry, recoil, bendIn);
                    // 折れたまま止まる長さ。従来は 0.15 秒で立ち直り始めていたため、
                    // 5 発食らっている間も合間に立ち姿へ戻って見えた（プレビューで確認）。
                    float holdAt = _heldBellyHoldSeconds > 0f ? _heldBellyHoldSeconds : 0.15f;
                    float straighten = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(holdAt, holdAt + 0.17f, hitTime));
                    if (!bodyCombo)
                        fold *= Mathf.Lerp(1f, 0.27f + 0.09f * _heldBellyFatigue, straighten);
                    float gougeSink = bodyCombo
                        ? recoil * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.16f, hitTime))
                            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.46f, hitTime)))
                        : 0f;
                    float impact = HeldBellyPunchImpact;
                    float targetPitch = Mathf.Clamp((bodyCombo ? 35f : 48f) * impact, 28f, bodyCombo ? 48f : 62f)
                        + 6.5f * gougeSink;
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, targetPitch, fold);
                    pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, 0f, Mathf.Max(recoil, lift));
                    // 胴の前傾（Spine + Chest で 1.4 倍）を首で打ち消し、腹で折れても顔は前に残す（過伸展を防ぐ）。
                    float targetHeadPitch = Mathf.Clamp(
                        (bodyCombo ? -60f : -66f) * Mathf.Lerp(1f, impact, 0.65f) - 6.5f * gougeSink,
                        bodyCombo ? -64f : -82f, -42f);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, targetHeadPitch, fold);

                    // 足の IK 目標は床に残し、打数が進むほど膝が折れて腰が沈み、めり込み時はつま先立ちで浮く。
                    float buckle = recoil * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.22f, hitTime));
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY - 0.030f * impact, recoil)
                        - (0.035f + 0.035f * _heldBellyFatigue) * buckle
                        + 0.016f * gougeSink;
                    pose.HipsOffset.x += (toCamera >= 0f ? 1f : -1f) * (0.028f + 0.022f * _heldBellyFatigue) * buckle;
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ - 0.155f * impact - 0.042f * gougeSink, recoil);
                    pose.FootPitchL += 22f * recoil * _heldBellyFatigue + 14f * gougeSink;
                    pose.FootPitchR += 18f * recoil * _heldBellyFatigue + 12f * gougeSink;

                    if (comboDangle > 0f && fold > 0f)
                    {
                        float limpFold = fold * comboDangle;
                        pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, new Vector3(0.95f, 0.36f, -0.08f), limpFold);
                        pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, new Vector3(0.95f, 0.36f, -0.08f), limpFold);
                        pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, new Vector3(0.18f, -0.42f, 0.86f), limpFold);
                        pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, new Vector3(0.18f, -0.42f, 0.86f), limpFold);
                    }

                    // 拘束役が脇を引き上げ、胸を張らせて無防備な腹を前へ突き出す体勢に引き戻す（視線は天井ではなく前〜下へ）。
                    float archPitch = Mathf.Lerp(-9f, -15f, _heldBellyFatigue);
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, archPitch, lift);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 20f, lift);
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y,
                        StanceHipsY + Mathf.Lerp(0.030f, 0.055f, _heldBellyFatigue), lift * lift);
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z,
                        StanceHipsZ + Mathf.Lerp(0.050f, 0.085f, _heldBellyFatigue), lift);
                    // 胸が反るぶん（shoulderShare 1.4倍）で前腕が前へ跳ね上がらないよう、肘を後ろへ引き前腕は下へ垂らす。
                    float armLift = Mathf.Lerp(0.06f, 0.12f, _heldBellyFatigue) * lift;
                    pose.ArmLUpper += new Vector3(0.08f * lift, armLift, -0.18f * lift);
                    pose.ArmRUpper += new Vector3(0.08f * lift, armLift, -0.18f * lift);
                    pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, new Vector3(-0.28f, -0.82f, 0.42f), lift);
                    pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, new Vector3(-0.28f, -0.82f, 0.42f), lift);

                    // 腹パンの合間は苦悶しながらも、しゃがみ込む敵と低い拳を下向きに見つめる（天井を仰がない）。
                    if (bodyCombo && _hasDreadAngles && comboDangle < 0.99f)
                    {
                        float gaze = Mathf.Lerp(0.35f, 0.95f, lift) * (1f - comboDangle);
                        float lowTargetPitch = Mathf.Clamp(_dreadPitch, 2f, 20f) + 8f * lift;
                        float wantPitch = lowTargetPitch - pose.TorsoPitch * 1.4f;
                        pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, wantPitch, gaze);
                        pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, _dreadYaw + Mathf.Sin(Time.time * 11f) * 1.2f, gaze);
                        pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, 6f, gaze);
                    }
                }

                // 顔面パンチ: 頭が先行して振れ、胸・肩が少し遅れて付いてくる。
                // 当たったあとは歯を食いしばって踏ん張り、次の一発を睨む。
                float faceImpulse = HeldFacePunchImpulse;
                float faceBrace = HeldFacePunchBrace;
                if (faceImpulse > 0f || faceBrace > 0f)
                {
                    float hitTime = _heldFacePunchTotal - _heldFacePunchRemaining;
                    float lateral = _hasFacePunchDirection
                        ? Mathf.Clamp(_facePunchLocalX, -1f, 1f)
                        : (toCamera >= 0f ? 1f : -1f);
                    if (Mathf.Abs(lateral) < 0.15f)
                        lateral = toCamera >= 0f ? 1f : -1f;

                    // 頭は先行、胸は少し遅れて付く。腰は拘束位置に残す。
                    float torsoDelay = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hitTime - 0.030f) / 0.045f))
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hitTime - 0.18f) / 0.30f)));
                    float shake = Mathf.Exp(-hitTime * 6.5f) * Mathf.Sin(hitTime * 38f);

                    // 頭は一気に振り切る。少し遅れて胸が付き、腰は拘束位置に残す。
                    // 「振れ幅を足す」のではなく、当たった形へ寄せる（直前の「目を逸らす」
                    // 角度が残ると、うつむいたまま髪で顔が隠れて何が起きたか読めない）。
                    float snap = Mathf.Clamp01(faceImpulse * 1.25f);
                    // 奥へ振れる側も頬が見える角度で止める。打たれた方向には振る。
                    float snapYaw = lateral * (lateral * toCamera < 0f ? 34f : 46f);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, snapYaw, snap);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -14f, snap);
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, -lateral * 18f, snap);
                    pose.HeadYaw += shake * 6f * faceImpulse;
                    pose.HeadRoll += shake * 5f * faceImpulse;

                    pose.TorsoYaw += lateral * 12f * torsoDelay;
                    pose.TorsoRoll += -lateral * 5f * torsoDelay;
                    pose.TorsoPitch -= 11f * torsoDelay;

                    // 打たれた方向へ腰が逃げ、膝が抜ける。足は一歩ぶん残る。
                    pose.HipsOffset.x += lateral * 0.045f * torsoDelay;
                    pose.HipsOffset.y -= 0.05f * faceImpulse;
                    pose.FootL += new Vector3(lateral * 0.030f, 0f, -0.018f) * faceImpulse;
                    pose.FootR += new Vector3(lateral * 0.038f, 0f, -0.012f) * faceImpulse;
                    pose.FootPitchR += 16f * faceImpulse;
                    pose.FootPitchL += 8f * faceImpulse;

                    // 腕は羽交い締めで抑えられたまま、肩だけがわずかにすくむ（外へ跳ね上げない）。
                    pose.ArmLUpper.y += 0.025f * faceImpulse;
                    pose.ArmRUpper.y += 0.025f * faceImpulse;
                    pose.Grip = Mathf.Lerp(1f, 0.20f, comboDangle);

                    // 踏ん張り: 序盤は相手を見つめて怯え、打数が進むほど首が据わらなくなり傾く。
                    // グロッキー中の通常顔パンでは、弾かれたあと警戒姿勢に戻らずすぐグロッキーへ落とす。
                    float activeBrace = faceBrace * (1f - 0.85f * comboDangle);
                    pose.TorsoPitch += (5f + 4f * _heldFaceFatigue) * activeBrace;
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, Mathf.Lerp(-10f, -4f, _heldFaceFatigue), activeBrace * 0.85f);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw,
                        Mathf.Clamp(toCamera, -45f, 45f), activeBrace * 0.55f);
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, -lateral * (6f + 10f * _heldFaceFatigue), activeBrace * 0.75f);
                    pose.TorsoYaw -= lateral * 4f * activeBrace;
                    pose.TorsoRoll += lateral * (3f + 4f * _heldFaceFatigue) * activeBrace;
                    pose.HipsOffset.y -= (0.012f + 0.025f * _heldFaceFatigue) * activeBrace;
                }

                // 腹パン中は胸と顔を打ち手へ向けたまま、前後の反動で見せる。
                if (bodyCombo)
                {
                    var puncher = _punchDirector.BellyPuncher;
                    Vector3 toPuncher = puncher != null
                        ? transform.InverseTransformPoint(puncher.transform.position)
                        : Vector3.forward;
                    pose.TorsoYaw = 0f;
                    float aimYaw = Mathf.Atan2(toPuncher.x, toPuncher.z) * Mathf.Rad2Deg;
                    pose.HeadYaw = Mathf.Lerp(aimYaw, pose.HeadYaw, comboDangle);
                }

                // 顔パン中は顔を上げたままにする。うつむくと前髪が落ちて表情が読めなくなる
                // （プレビューで確認）。被弾の一瞬だけでなく、コンボの間ずっと保つ。
                float faceComboTarget = _punchDirector != null && _punchDirector.IsFaceCombo && comboDangle < 0.2f ? 1f : 0f;
                _faceComboLift = Mathf.MoveTowards(_faceComboLift, faceComboTarget, Time.deltaTime / 0.20f);
                if (_faceComboLift > 0f)
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, Mathf.Min(pose.HeadPitch, -8f), _faceComboLift * (1f - comboDangle));

                return pose;
            }

            // ベアハッグ（BearHug）。正面から胴を抱え込まれ、両腕を体側に封じられる。
            if (Grapple != null && Grapple.Kind == GrappleKind.BearHug)
            {
                // 抱え上げ（LiftPulse）と締め付けの脈（SqueezePulse）。
                // 締め付けは一定の脈で「ぎゅっ」と来るようにし、単発の衝撃だけにしない。
                float hugLift = Grapple.LiftPulse;
                float squeeze = Grapple.SqueezePulse;
                float yank = Grapple.PullPulse;
                float tight = 0.5f + 0.5f * Mathf.Sin(Time.time * 5.0f);
                float crush = squeeze * (0.65f + 0.35f * tight);

                // 相手の胸に引き寄せられ、腰ごと浮く。足は床を離れてぶら下がる。
                pose.HipsOffset.y = StanceHipsY - 0.10f * pull + 0.13f * hugLift + 0.02f * crush;
                pose.HipsOffset.z = StanceHipsZ + 0.03f * pull + 0.01f * crush
                    + 0.045f * yank;
                pose.HipsOffset.x = Mathf.Sin(Time.time * 2.7f) * 0.015f * hugLift;

                // 引き寄せ: 上体だけ先に持っていかれ、頭は一瞬残ってから付いてくる。
                pose.TorsoPitch = -8f * pull + 4f * strain + 2f * crush + 12f * yank;
                pose.TorsoYaw = Mathf.Sin(Time.time * 2.4f) * 3f;
                pose.TorsoRoll = Mathf.Sin(Time.time * 2.1f) * 1.5f * hugLift
                    - 2.5f * yank;

                pose.HeadPitch = -14f * pull - 5f * crush - 11f * yank;
                pose.HeadYaw = Mathf.Sin(Time.time * 3.2f) * 5f;
                pose.HeadRoll = 0f;

                // 手を開いて胴の前へ構える。実際の接触位置は BearHugPose が合わせる。
                pose.ArmLUpper = pose.ArmRUpper = new Vector3(0.46f, -0.82f, 0.34f);
                pose.ArmLFore = pose.ArmRFore = new Vector3(-0.10f, 0.30f, 0.95f);

                // 締め付けに合わせて胸を起こし、両腕で押し返す。苦痛で頭が反り小さく揺れる。
                pose.TorsoPitch -= 5f * strain + 8f * crush;
                pose.HeadPitch -= 4f * strain + 12f * crush;
                pose.HeadYaw -= Mathf.Sin(Time.time * 24f) * 7f * pulse;
                pose.HeadRoll += Mathf.Sin(Time.time * 6.5f) * 8f * crush;
                pose.HipsOffset.z -= 0.03f * strain;

                pose.HandRollL = StanceHandRollL;
                pose.HandRollR = StanceHandRollR;
                pose.Grip = 0.12f;

                // 抱え上げられている間は両足が浮く。つま先は下へ垂れる（左右を少しずらして吊られ感を出す）。
                // 引き寄せでは足が床に残ろうとして、体より後ろへ遅れる。
                Vector3 drag = new Vector3(0f, 0.01f, -0.06f) * yank;
                pose.FootL += StanceFootL + drag
                    + new Vector3(0f, 0.12f * hugLift, -0.04f * hugLift);
                pose.FootR += StanceFootR + drag
                    + new Vector3(0f, 0.09f * hugLift, -0.02f * hugLift);
                pose.FootPitchL = 22f * pull + 34f * hugLift + 12f * yank;
                pose.FootPitchR = 22f * pull + 32f * hugLift + 12f * yank;
                ApplyFrontEscapePose(ref pose, frontWait, pulse);
                if (HeldFacePunchImpulse > 0f || HeldBellyPunchFold > 0f)
                {
                    pose.HeadYaw += (CameraYawToMe() >= 0f ? 1f : -1f) * 34f * HeldFacePunchImpulse;
                    pose.HeadPitch -= 14f * Mathf.Max(HeldFacePunchImpulse, HeldBellyPunchFold);
                }
                return pose;
            }

            // 押さえつけ（Pin）。正面から両肩を押され、膝が折れかけて耐える。
            // 上体は前へ折り、顔は上げて相手を見る（下を向くと前髪で顔が隠れる）。
            float pinBreath = Mathf.Sin(Time.time * 4.2f);
            pose.HipsOffset.y = StanceHipsY - 0.18f * pull + 0.008f * pinBreath;
            pose.HipsOffset.z = StanceHipsZ - 0.02f * pull;

            pose.TorsoPitch = 18f * pull + 6f * strain + 1.8f * pinBreath;
            pose.TorsoYaw = Mathf.Sin(Time.time * 2.2f) * 3f;
            pose.TorsoRoll = 0f;

            pose.HeadPitch = -30f * pull;
            pose.HeadYaw = Mathf.Sin(Time.time * 3.4f) * 4f;
            pose.HeadRoll = 0f;

            // 相手の腕を掴んで剥がそうとする。胸の高さで前へ（肘は肩より上げない）。
            Vector3 gripUpper = new Vector3(0.20f, -0.42f, 0.88f);
            Vector3 gripFore = new Vector3(0.05f, 0.10f, 0.99f);

            // もがくと肘が開き、また締まる
            float spread = 0.10f * pulse;
            pose.ArmLUpper = gripUpper + new Vector3(spread, -spread * 0.4f, 0f);
            pose.ArmRUpper = gripUpper + new Vector3(spread * 0.6f, -spread * 0.3f, 0f);
            pose.ArmLFore = gripFore + new Vector3(0f, spread * 0.5f, 0f);
            pose.ArmRFore = gripFore + new Vector3(0f, spread * 0.4f, 0f);

            // 残り時間が少ないのに押し返せていないと、腕が落ちて力尽きて見える。
            // 逆に逃げられそう（進捗が高い）なときは腕が上がったまま。
            float progress = Grapple != null ? Mathf.Clamp01(Grapple.Progress) : 0f;
            float timeLeft = Grapple != null && Grapple.HasTimeLimit
                ? Mathf.Clamp01(Grapple.Remaining) : 1f;
            float give = (1f - timeLeft) * (1f - progress);
            pose.ArmLUpper += new Vector3(0f, -0.16f * give, 0f);
            pose.ArmRUpper += new Vector3(0f, -0.16f * give, 0f);
            pose.ArmLFore += new Vector3(0f, -0.12f * give, 0f);
            pose.ArmRFore += new Vector3(0f, -0.12f * give, 0f);

            // 壁押さえつけ: 背中を壁に付けて押されるので、上体を反らせて耐える。
            // 膝蹴りより先に置くこと。後に置くと、膝で折れた上体をこの反りが打ち消す。
            ApplyWallBrace(ref pose, Grapple != null ? Grapple.WallPinWeight : 0f);

            // 膝に押された腹を引き、みぞおちから上を前へ折る。足は一瞬だけ浮く。
            float knee = Grapple != null ? Grapple.KneePulse : 0f;
            if (knee > 0f)
            {
                float fold = Mathf.SmoothStep(0f, 1f, knee);
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 53f, fold);
                // 膝が入った瞬間に腰ごと持ち上がり、つま先が浮く。以前は 0.14/0.085 で
                // 変化が小さく、彼の体で膝が隠れる回では「入った」ことが読めなかった。
                pose.HipsOffset.y += 0.19f * knee;
                pose.HipsOffset.z -= 0.13f * knee;
                pose.FootL += new Vector3(0f, 0.125f, -0.105f) * knee;
                pose.FootR += new Vector3(0f, 0.085f, -0.075f) * knee;
                // 胸にも前傾が配られるので、その分も首で打ち消して顔を前に残す。
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -67f, fold);
            }

            if (Grapple != null && !Grapple.IsWallPin)
            {
                float kneeReset = Grapple.KneeResetLift;
                if (kneeReset > 0f)
                {
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 6f, kneeReset);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -18f, kneeReset);
                    pose.HipsOffset.z += 0.035f * kneeReset;
                    pose.HipsOffset.y += 0.025f * kneeReset;
                }

                float exhaust = Grapple.KneeExhaustion;
                if (exhaust > 0f)
                {
                    pose.HipsOffset.y -= 0.065f * exhaust;
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 28f, exhaust);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -22f, exhaust);
                    pose.HeadRoll += 8f * exhaust;
                }
            }

            pose.Grip = 1f;

            // 3 人がかり: 彼女はグロッキーで、壁にもたれてずり落ちる。腰が落ち、足が前へ投げ出され、
            // 上体は壁に背中を預けて（前へ倒さず、わずかに反らせる）、頭は力なく後ろへ傾いて横へ垂れる。
            // 腹で折れる動きや顔パンの反応は、この上に重なる（殴られると前へ折れて、また壁へ戻る）。
            // （順番が逆だと、グロッキーの角度が折れの動きを上書きして、殴られても体が動かない。実測）
            // （上体を前へ倒して頭を前へ垂らすと、壁にもたれたのではなく前かがみに見える。実測）
            float groggy = Grapple != null ? Grapple.WallGangWeight : 0f;
            if (groggy > 0.001f)
            {
                pose.HipsOffset.y -= 0.26f * groggy;
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, -4f, groggy);
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -8f, groggy);
                pose.HeadRoll += (16f + Mathf.Sin(Time.time * 1.3f) * 3f) * groggy;
                pose.TorsoRoll += Mathf.Sin(Time.time * 1.7f) * 2.0f * groggy;

                // 壁をずり落ちて、足が前へ投げ出される（壁から離れる向きが前）。
                pose.FootL += new Vector3(0f, 0f, 0.20f * groggy);
                pose.FootR += new Vector3(0f, 0f, 0.26f * groggy);
            }

            // 二体がかり（Y 字）の腹パン: 両腕は壁に固定されたまま、腹で折れて膝が抜ける。
            // 腕のポーズ値は触らない。手首は SolveOverheadArms が world 固定しているので、
            // 上体が折れると肘が曲がり「押さえ込まれたまま殴られている」形になる。
            float wallHit = HeldBellyPunchFold;
            if (wallHit > 0f && Grapple != null && Grapple.WallSplitWeight > 0.5f)
            {
                // 打った側と逆へ逃げる（拘束役が左で打てば、彼女の左から来るので右へ）。
                float side = Grapple.WallSplitPunchLeft ? -1f : 1f;
                float fatigue = HeldBellyPunchFatigue;
                float fold = wallHit;

                // 壁の反り（-3°）から前へ折る。正面の押さえつけ（52°）ほど深くは折らない
                // （深いと、腕を伸ばしたまま吊られた画になる）。
                // 重さ（序盤 0.7 → 締め 1.7）で折れの深さ・沈み・横揺れが変わる。
                float impact = HeldBellyPunchImpact;
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, (32f + 6f * fatigue) * impact, fold);
                // 胴の前傾を首で打ち消し、腹で折れても顔は前に残す（前髪で顔が隠れない）。
                // 締めの impact=1.7 だと -79° まで反って天井を向くので、通常の腹パンと同じ -64° で止める。
                float splitHeadPitch = Mathf.Max(-64f,
                    (-52f - 4f * fatigue) * Mathf.Lerp(1f, impact, 0.6f));
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, splitHeadPitch, fold);
                pose.HeadYaw += side * 10f * impact * fold;
                pose.TorsoRoll += side * 6f * impact * fold;
                pose.HipsOffset.y -= (0.10f + 0.03f * fatigue) * impact * fold;
                // ローカルの -Z が壁側。殴られて上体が前へ折れるぶん、腰は壁から少し離れる。
                // 締めの膝入れ（重さ 1.5 以上）は、腰が膝の側へ出ると腹が膝へ食い込むので出さない。
                pose.HipsOffset.z += (impact > 1.5f ? 0f : 0.03f * impact) * fold;
                pose.HipsOffset.x += side * 0.03f * impact * fold;

                // 締めの膝は腹を突き上げるので、当たった瞬間に体が持ち上がる（足が床から浮く）。
                float lift = Mathf.Clamp01((impact - 1.2f) / 0.5f) * fold;
                pose.HipsOffset.y += 0.07f * lift;
                pose.FootL.y += 0.05f * lift;
                pose.FootR.y += 0.05f * lift;

                // 当たった瞬間だけ頭と肩が短く揺れる（0.15 秒で消える）。重いほど大きい。
                float age = HeldBellyPunchAge;
                float shake = Mathf.Exp(-age * 14f) * Mathf.Sin(age * 55f) * fold * impact;
                pose.HeadPitch += shake * 3f;
                pose.TorsoRoll += shake * 2f;
            }
            else if (wallHit > 0f)
            {
                float impact = HeldBellyPunchImpact;
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, Mathf.Clamp(42f * impact, 30f, 52f), wallHit);
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -54f, wallHit);
                pose.HipsOffset.y -= 0.065f * impact * wallHit;
                pose.HipsOffset.z -= 0.075f * impact * wallHit;
            }

            // 3 人がかり・レーン拘束の顔パン: 殴られた側と反対へ頭が弾かれる（腹の折れとは別に、頭だけが動く）。
            float faceHit = HeldFacePunchImpulse;
            if (faceHit > 0f)
            {
                float faceSide = (Grapple != null && Grapple.WallSplitFace)
                    ? (_facePunchLocalX >= 0f ? -1f : 1f)
                    : (_hasFacePunchDirection && Mathf.Abs(_facePunchLocalX) >= 0.15f
                        ? Mathf.Sign(_facePunchLocalX)
                        : (CameraYawToMe() >= 0f ? 1f : -1f));
                pose.HeadYaw += faceSide * 62f * faceHit;
                pose.HeadRoll += faceSide * 18f * faceHit;
                pose.HeadPitch += 14f * faceHit;
                pose.TorsoYaw += faceSide * 12f * faceHit;
                pose.TorsoRoll += faceSide * 6f * faceHit;
                // 顔を殴られて足がもつれる（膝が少し沈み、腰が殴られた側と反対へ流れる）。
                pose.HipsOffset.y -= 0.05f * faceHit;
                pose.HipsOffset.x += faceSide * 0.05f * faceHit;
            }

            // 構えの足の位置から始める。Rest（T ポーズ）のままだと脚が伸びきって
            // 爪先立ちになり、「踏ん張り」ではなく「吊られている」画になる。
            pose.FootL += StanceFootL;
            pose.FootR += StanceFootR;

            // 前後に足を割って踏ん張る。両足を揃えると「座っている」画になる。
            pose.FootL += new Vector3(0f, 0f, 0.04f);
            pose.FootR += new Vector3(0f, 0f, -0.04f);
            ApplyFrontEscapePose(ref pose, frontWait, pulse);
            return pose;
        }

    }
}
