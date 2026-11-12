using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
        /// <summary>拘束（後ろ / 正面 / ベアハッグ / 押さえつけ）。</summary>
        PoseValues PoseGrabbed()
        {
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
                    pose.TorsoPitch = Mathf.Lerp(-8f, -2f, slide) - 10f * impact;
                    pose.HeadPitch = Mathf.Lerp(Mathf.Lerp(10f, 6f, lift), 24f, slide) + 12f * impact;
                    pose.HipsOffset.y -= Mathf.Lerp(0.06f, 0.38f, slide) + 0.04f * impact;
                    pose.HipsOffset.z -= 0.04f + 0.05f * slide;
                    float armsDrop = stage == WallStage.Slam ? t : 1f;
                    pose.ArmLUpper = pose.ArmRUpper = Vector3.Lerp(
                        new Vector3(0.50f, 0.20f, 0.55f), new Vector3(0.30f, -0.75f, 0.10f), armsDrop);
                    pose.ArmLFore = pose.ArmRFore = Vector3.Lerp(
                        new Vector3(-0.15f, 0.25f, 0.80f), new Vector3(-0.10f, -0.75f, 0.45f), armsDrop);
                    pose.HipsOffset.y -= 0.06f * lift;
                    pose.FootL.z += 0.08f + 0.16f * slide;
                    pose.FootR.z += 0.05f + 0.20f * slide;
                }
                return pose;
            }

            // 掴まれた向きで姿勢を作り替える。背後は羽交い締め（Back）、
            // 正面の押さえつけ（Pin）、正面から胴を抱えるベアハッグ。
            if (Grapple != null && Grapple.Kind == GrappleKind.Back)
            {
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
                var nelsonUpper = new Vector3(0.98f, 0.18f, 0.02f);
                var nelsonFore = new Vector3(-0.35f, -0.45f, 0.82f);
                float claw = 0.14f * pulse;

                pose.ArmLUpper = nelsonUpper + new Vector3(0f, claw, 0f);
                pose.ArmRUpper = nelsonUpper + new Vector3(0f, claw * 0.6f, 0f);
                pose.ArmLFore = nelsonFore + new Vector3(0f, claw * 1.6f, claw);
                pose.ArmRFore = nelsonFore + new Vector3(0f, claw * 1.2f, claw * 0.8f);

                // 逃げようと前へ体を投げ出す（腕は押さえられたまま、体だけが相手から離れようとする）。
                pose.TorsoPitch += 9f * strain;
                pose.HeadPitch += 5f * strain;
                pose.HipsOffset.z += 0.05f * strain;
                pose.ArmLUpper += new Vector3(0f, 0.12f * strain, 0f);
                pose.ArmRUpper += new Vector3(0f, 0.12f * strain, 0f);

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
                    pose.HeadPitch -= 8f * ready;
                    pose.HipsOffset.y -= 0.02f * ready;
                    pose.HipsOffset.z -= 0.05f * ready;
                }

                // 殴りに来る敵を見る。うつむいたままだと「何が来るか分かっている」怯えが出ない。
                // 最初に気づいたときの角度で顔を上げ、拳が出る直前だけ視線を外す。
                float dread = _dreadLook;
                if (dread > 0.001f)
                {
                    // 首の角度は上体に対して付くので、前傾ぶんを差し引いて相手の顔へ合わせる。
                    // 少しだけ顎を引く（+4°）。真っ直ぐ見上げると睨んでいるように見える。
                    float wantPitch = _dreadPitch - pose.TorsoPitch + 4f;
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

                // 腹パン: 腹で一度折れた後、胸を起こそうとする間に膝が折れて腰が沈む。
                float toCamera = CameraYawToMe();
                bool bodyCombo = _punchDirector != null && _punchDirector.IsBodyCombo;
                float recoil = HeldBellyPunchFold;
                if (recoil > 0f)
                {
                    float hitTime = _heldBellyPunchTotal - _heldBellyPunchRemaining;
                    float bendIn = Mathf.SmoothStep(0f, 1f, (hitTime - 0.015f) / 0.055f);
                    float fold = Mathf.Lerp(_heldBellyCarry, recoil, bendIn);
                    float straighten = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.32f, hitTime));
                    fold *= Mathf.Lerp(1f, 0.27f + 0.09f * _heldBellyFatigue, straighten);
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, bodyCombo ? 43f : 52f, fold);
                    pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, 0f, recoil);
                    // 胴の前傾を首で打ち消し、腹で折れても顔は前に残す。
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, bodyCombo ? -60f : -72f, fold);

                    // 足の IK 目標は床に残し、左右どちらかへ重さが偏りながら膝を折る。
                    float buckle = recoil * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.07f, 0.23f, hitTime));
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY - 0.055f, recoil)
                        - (0.14f + 0.085f * _heldBellyFatigue) * buckle;
                    pose.HipsOffset.x += (toCamera >= 0f ? 1f : -1f) * 0.035f * buckle;
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ - 0.17f, recoil);

                    // 脇を引かれて肩が先に戻り、腰は少し遅れて持ち上がる。
                    float lift = HeldBellyCaptorLift;
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 12f, lift);
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -17f, lift);
                    pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, StanceHipsY + 0.025f, lift * lift);
                    pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, StanceHipsZ - 0.055f, lift);
                    pose.ArmLUpper.y += 0.36f * lift;
                    pose.ArmRUpper.y += 0.36f * lift;
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

                    pose.TorsoYaw += lateral * 14f * torsoDelay;
                    pose.TorsoRoll += -lateral * 8f * torsoDelay;
                    pose.TorsoPitch -= 13f * torsoDelay;

                    // 打たれた方向へ腰が逃げ、膝が抜ける。足は一歩ぶん残る。
                    pose.HipsOffset.x += lateral * 0.050f * torsoDelay;
                    pose.HipsOffset.y -= 0.05f * faceImpulse;
                    pose.FootL += new Vector3(lateral * 0.030f, 0f, -0.018f) * faceImpulse;
                    pose.FootR += new Vector3(lateral * 0.038f, 0f, -0.012f) * faceImpulse;
                    pose.FootPitchR += 16f * faceImpulse;
                    pose.FootPitchL += 8f * faceImpulse;

                    // 押さえられた腕は突かれて跳ねる（拘束は解けない）。
                    pose.ArmLUpper.y += 0.10f * faceImpulse;
                    pose.ArmRUpper.y += 0.10f * faceImpulse;
                    pose.ArmLFore += new Vector3(0f, 0.12f, 0.05f) * faceImpulse;
                    pose.ArmRFore += new Vector3(0f, 0.12f, 0.05f) * faceImpulse;
                    pose.Grip = 1f;

                    // 踏ん張り: 顔を上げて相手を睨み返す（顎を引くと髪で顔が隠れる）。
                    pose.TorsoPitch += 5f * faceBrace;
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -8f, faceBrace * 0.8f);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw,
                        Mathf.Clamp(toCamera, -45f, 45f), faceBrace * 0.55f);
                    pose.TorsoYaw -= lateral * 4f * faceBrace;
                    pose.TorsoRoll += lateral * 3f * faceBrace;
                    pose.HipsOffset.y -= 0.012f * faceBrace;
                }

                // 腹パン中は胸と顔を打ち手へ向けたまま、前後の反動で見せる。
                if (bodyCombo)
                {
                    var puncher = _punchDirector.BellyPuncher;
                    Vector3 toPuncher = puncher != null
                        ? transform.InverseTransformPoint(puncher.transform.position)
                        : Vector3.forward;
                    pose.TorsoYaw = 0f;
                    pose.HeadYaw = Mathf.Atan2(toPuncher.x, toPuncher.z) * Mathf.Rad2Deg;
                }

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

                // 締め付けに合わせて胸を起こし、両腕で押し返す。
                pose.TorsoPitch -= 5f * strain + 8f * crush;
                pose.HeadPitch -= 3f * strain;
                pose.HipsOffset.z -= 0.03f * strain;

                pose.HandRollL = StanceHandRollL;
                pose.HandRollR = StanceHandRollR;
                pose.Grip = 0.12f;

                // 抱え上げられている間は両足が浮く。つま先は下へ垂れる。
                // 引き寄せでは足が床に残ろうとして、体より後ろへ遅れる。
                Vector3 drag = new Vector3(0f, 0.01f, -0.06f) * yank;
                pose.FootL += StanceFootL + drag
                    + new Vector3(0f, 0.10f * hugLift, -0.02f * hugLift);
                pose.FootR += StanceFootR + drag
                    + new Vector3(0f, 0.10f * hugLift, -0.02f * hugLift);
                pose.FootPitchL = 22f * pull + 28f * hugLift + 12f * yank;
                pose.FootPitchR = 22f * pull + 28f * hugLift + 12f * yank;
                ApplyFrontEscapePose(ref pose, frontWait, pulse);
                return pose;
            }

            // 押さえつけ（Pin）。正面から両肩を押され、膝が折れかけて耐える。
            // 上体は前へ折り、顔は上げて相手を見る（下を向くと前髪で顔が隠れる）。
            pose.HipsOffset.y = StanceHipsY - 0.18f * pull;
            pose.HipsOffset.z = StanceHipsZ - 0.02f * pull;

            pose.TorsoPitch = 18f * pull + 6f * strain;
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
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 49f, fold);
                pose.HipsOffset.y += 0.14f * knee;
                pose.HipsOffset.z -= 0.10f * knee;
                pose.FootL += new Vector3(0f, 0.085f, -0.075f) * knee;
                pose.FootR += new Vector3(0f, 0.085f, -0.075f) * knee;
                // 胸にも前傾が配られるので、その分も首で打ち消して顔を前に残す。
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -67f, fold);
            }

            pose.Grip = 1f;

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
                pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 32f + 6f * fatigue, fold);
                // 胴の前傾を首で打ち消し、腹で折れても顔は前に残す（前髪で顔が隠れない）。
                pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -52f - 4f * fatigue, fold);
                pose.HeadYaw += side * 10f * fold;
                pose.TorsoRoll += side * 6f * fold;
                pose.HipsOffset.y -= (0.10f + 0.03f * fatigue) * fold;
                // ローカルの -Z が壁側。殴られて上体が前へ折れるぶん、腰は壁から少し離れる。
                pose.HipsOffset.z += 0.03f * fold;
                pose.HipsOffset.x += side * 0.03f * fold;

                // 当たった瞬間だけ頭と肩が短く揺れる（0.15 秒で消える）。
                float age = HeldBellyPunchAge;
                float shake = Mathf.Exp(-age * 14f) * Mathf.Sin(age * 55f) * fold;
                pose.HeadPitch += shake * 3f;
                pose.TorsoRoll += shake * 2f;
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
