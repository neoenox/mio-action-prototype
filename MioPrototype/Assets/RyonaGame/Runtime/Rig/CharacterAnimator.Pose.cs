using UnityEngine;

namespace Ryona.Game
{
    /// <summary>
    /// 手続き的モーション。ここは「毎フレームの姿勢評価」だけを持つ。
    /// </summary>
    public sealed partial class CharacterAnimator
    {
        void BracePalm(bool left, Transform chest, Vector3 center, float weight)
        {
            Transform upper = left ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = left ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = left ? Rig.LHand : Rig.RHand;
            if (upper == null || elbow == null || hand == null)
                return;

            Vector3 side = transform.right * (left ? -1f : 1f);
            Vector3 goal = center + side * 0.13f + chest.forward * 0.12f;
            Quaternion upperPose = upper.rotation;
            Quaternion elbowPose = elbow.rotation;
            Quaternion handPose = hand.rotation;
            ProceduralRig.SolveTwoBone(upper, elbow, hand, goal,
                elbow.position + side * 0.16f - Vector3.up * 0.20f, transform.right);
            upper.rotation = Quaternion.Slerp(upperPose, upper.rotation, weight);
            elbow.rotation = Quaternion.Slerp(elbowPose, elbow.rotation, weight);
            Rig.AlignPalm(left, -chest.forward, chest.up);
            hand.rotation = Quaternion.Slerp(handPose, hand.rotation, weight);
        }

        /// <summary>
        /// 1 フレームぶんの姿勢。状態ごとのメソッドが 1 つ返し、
        /// <see cref="ApplyPoseToRig"/> がリグへ流し込む。
        /// どの状態でも触らない値は <see cref="PoseDefaults"/> の初期値のまま。
        /// </summary>
        struct PoseValues
        {
            public Vector3 FootL;
            public Vector3 FootR;
            public Vector3 HipsOffset;
            public Vector3 ArmLUpper;
            public Vector3 ArmLFore;
            public Vector3 ArmRUpper;
            public Vector3 ArmRFore;
            public float HandOffBrace;
            public float TorsoPitch;
            public float TorsoRoll;
            public float TorsoYaw;
            public float HeadPitch;
            public float HeadYaw;
            public float HeadRoll;
            public float LayAngle;
            public float LaySide;
            public float LegTuck;
            public float FootPitchL;
            public float FootPitchR;
            public float Grip;
            public float HandRollL;
            public float HandRollR;
        }

        /// <summary>
        /// 今の状態の姿勢を組み立ててリグへ流し込む（毎フレーム）。
        /// 状態ごとの中身は PoseIdle / PoseGuard / … に分けてある。
        /// </summary>
        public void ApplyPose()
        {
            if (Rig == null || !Rig.Ready)
                return;

            Rig.ResetPose();

            // 歩行以外では位相の追跡を切る（次に歩き出した 1 歩目を鳴らし直す）
            if (_state != State.Walk)
                _lastWalkU = -1f;

            PoseValues pose = _state switch
            {
                State.Idle => PoseIdle(),
                State.Guard => PoseGuard(),
                State.Walk => PoseWalk(),
                State.Stagger => PoseStagger(),
                State.Grabbed => PoseGrabbed(),
                State.Down => PoseDown(),
                State.GetUp => PoseGetUp(),
                State.Attack => PoseAttack(),
                _ => PoseDefaults(),
            };

            // 壁から戻るあいだは掴みが解けて Idle などへ移っている。
            // 壁の反りは重みで抜く（PoseGrabbed の中では膝蹴りの前後関係を保つため
            // 先に掛けているので、二重に掛けない）。倒れ姿勢には掛けない。
            if (_state != State.Grabbed && _state != State.Down && _state != State.GetUp
                && Grapple != null)
                ApplyWallBrace(ref pose, Grapple.WallPinWeight);

            ApplyPoseToRig(pose);
        }

        /// <summary>
        /// 壁押さえつけの姿勢（背中を壁に付けて上体を反らせる）。
        /// 前へ折れる通常の押さえつけと逆の形にする（折れると「壁に押し付けられた」に見えない）。
        /// <para>
        /// 状態ではなく <paramref name="weight"/> だけで効かせる。壁から戻るあいだは
        /// 掴みが解けて <see cref="State.Grabbed"/> を離れているので、状態で切ると
        /// 位置が戻りきる前に立ち姿勢へ戻って横滑りして見える（実測）。
        /// </para>
        /// </summary>
        void ApplyWallBrace(ref PoseValues pose, float weight)
        {
            if (weight <= 0.001f)
                return;

            float w = Mathf.SmoothStep(0f, 1f, weight);

            // 引き起こした高さで両手首を固定する。
            pose.HipsOffset.y = Mathf.Lerp(pose.HipsOffset.y, -0.12f, w);
            // 壁へ貼り付くので、腰ごと壁側へ寄せる。反り（TorsoPitch）を残すと
            // 肩甲骨と後頭部だけが壁から離れて「背中が浮いた」画になる。
            pose.HipsOffset.z = Mathf.Lerp(pose.HipsOffset.z, -0.07f, w);

            // 反りは小さく。反らせすぎると頭が壁へ入る。
            pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, -3f, w);
            pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 9f, w);
            pose.HeadRoll += 3f * w;

            // 足は相手側（前）へ踏ん張る。押し込まれるほど前へ残る。
            pose.FootL += new Vector3(0f, 0f, 0.06f * w);
            pose.FootR += new Vector3(0f, 0f, 0.04f * w);
            pose.FootPitchL += 10f * w;
            pose.FootPitchR += 6f * w;
        }

        /// <summary>どの状態でも共通の初期値（立ち姿勢と構えの腕）。</summary>
        PoseValues PoseDefaults() => new PoseValues
        {
            FootL = Rig.RestFootL,
            FootR = Rig.RestFootR,
            ArmLUpper = ArmIdleUpper,
            ArmLFore = ArmIdleFore,
            ArmRUpper = Mirror(ArmIdleUpper),
            ArmRFore = Mirror(ArmIdleFore),
            Grip = 0.30f,
        };

        /// <summary>立ち（ファイティングスタンス）。呼吸と体重移動だけを足す。</summary>
        PoseValues PoseIdle()
        {
            var pose = PoseDefaults();
            // ファイティングスタンス。左足を半歩前、右足を後ろに引いた半身。
            // 呼吸・体重移動・膝の細かい上下だけを足して、止まって見えないようにする。
            float t = Time.time;
            float shift = Mathf.Sin(t * 0.62f);
            float bounce = Mathf.Sin(t * 3.4f) * 0.004f;

            // スタミナ廃止に伴い、自動で疲労姿勢になることはない。
            // _tired は Editor の撮り比べ（PreviewTired）専用に残している。
            if (!_tiredHold)
                _tired = Mathf.MoveTowards(_tired, 0f, Time.deltaTime * 0.8f);

            float breathAmp = 1f + 2.2f * _tired;
            float breathRate = 1.7f + 1.3f * _tired;
            float slowBreath = Mathf.Sin(t * breathRate);

            // 呼吸の位相を部位ごとにずらす。全身が同じ位相で膨らむと
            // 「袋が膨らんでいる」ように見える。胸が先、腰と頭が遅れて付いてくる。
            float breathHips = Mathf.Sin(t * breathRate - 0.30f);
            float breathHead = Mathf.Sin(t * breathRate - 0.85f);

            // 腰は沈める（膝を曲げる）。腰だけ後ろへ引くと「お尻を突き出した」画になるので、
            // 前後はほぼ真下に置き、沈み込みだけで「構えている」を作る。
            pose.HipsOffset.y = StanceHipsY + breathHips * 0.006f * breathAmp + bounce - 0.02f * _tired;
            pose.HipsOffset.x = shift * 0.010f;
            pose.HipsOffset.z = StanceHipsZ;

            pose.TorsoPitch = StanceTorsoPitch + slowBreath * 0.7f * breathAmp + 5f * _tired;
            pose.TorsoYaw = StanceTorsoYaw + shift * 2.0f;   // 半身（左肩が前）
            pose.TorsoRoll = shift * 1.0f;
            pose.HeadYaw = StanceHeadYaw - shift * 2.0f;     // 半身の分だけ顔を正面へ戻す
            pose.HeadPitch = StanceHeadPitch + breathHead * 0.5f * breathAmp + 4f * _tired;

            // 前の手（左）は顎の高さから前へ差し出し、後の手（右）は頬の横に引く。
            // 左右で高さと前後を変えると「構え」に見える（揃えるとガードと区別が付かない）。
            // 疲れてくると腕が下がり、拳も開き気味になる。
            float droop = 0.55f * _tired;
            pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, new Vector3(0.30f, -0.98f, 0.08f), droop);
            pose.ArmLFore = Vector3.Lerp(StanceArmLFore, new Vector3(0.10f, -0.92f, 0.30f), droop);
            pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper, new Vector3(-0.30f, -0.98f, 0.08f), droop);
            pose.ArmRFore = Vector3.Lerp(StanceArmRFore, new Vector3(0.06f, -0.90f, 0.34f), droop);

            // 呼吸で拳がわずかに上下する。腕を完全に止めると「止め絵」に見える。
            float armBreath = slowBreath * 0.013f * breathAmp;
            pose.ArmLUpper += new Vector3(0f, armBreath, 0f);
            pose.ArmLFore += new Vector3(0f, armBreath * 0.6f, 0f);
            pose.ArmRUpper += new Vector3(0f, armBreath * 0.8f, 0f);
            pose.ArmRFore += new Vector3(0f, armBreath * 0.5f, 0f);

            // 掌が前を向くと「両手を上げた」に見えるので、前腕軸まわりにひねって
            // 掌を内側（自分の顔）へ向ける。拳は小指側を前に。
            // 手首も完全には止めない（ゆっくり左右で違う周期で揺らす）。
            pose.HandRollL = StanceHandRollL + Mathf.Sin(t * 0.83f) * 2.5f;
            pose.HandRollR = StanceHandRollR + Mathf.Sin(t * 0.71f + 1.7f) * 2.5f;

            pose.Grip = 0.95f - 0.35f * _tired;

            // 振りほどいた直後は両手で突き放す（構えへ戻る前に 1 拍だけ入れる）。
            if (_shoveTime > 0f)
            {
                // 突き放しは「押し出して、戻る」の 2 段。残り時間をそのまま使うと
                // 始まった瞬間に最大まで飛び、戻りが等速になる（人が腕を出す形にならない）。
                float since = ShoveSeconds - _shoveTime;
                float pushIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.07f, since));
                float pushOut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.22f, _shoveTime));
                float s = Mathf.Min(pushIn, pushOut);

                Vector3 pushUpper = new Vector3(0.16f, -0.26f, 0.95f);
                Vector3 pushFore = new Vector3(0.04f, 0.10f, 0.99f);

                pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, pushUpper, s);
                pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, pushFore, s);
                pose.ArmRUpper = Vector3.Lerp(pose.ArmRUpper, new Vector3(-pushUpper.x, pushUpper.y, pushUpper.z), s);
                pose.ArmRFore = Vector3.Lerp(pose.ArmRFore, pushFore, s);

                pose.TorsoPitch += 7f * s;
                pose.HipsOffset.z -= 0.02f * s;
                pose.Grip = Mathf.Lerp(pose.Grip, 0.35f, s);
            }

            // 足は左を前、右を後ろへ、肩幅より少し広く。位置だけで半身を作る。
            pose.FootL += StanceFootL;
            pose.FootR += StanceFootR;
            return pose;
        }

        /// <summary>ガード（撮り比べ用）。</summary>
        PoseValues PoseGuard()
        {
            var pose = PoseDefaults();
            // 両腕を顔の前に構えて顎を引く。
            // 腕の指定は「外へ開く量」しか持てないので、肘は体の横に置いたまま
            // 前腕だけを立てて拳を顔の前に出す。
            // 残り時間をそのまま使うと等速で戻り「バネ」に見えないので、
            // 減衰の最後ほどゆっくり戻す。
            float recoil = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(_guardRecoil / Mathf.Max(0.01f, GuardRecoilSeconds)));
            float sway = Mathf.Sin(Time.time * 2.1f) * 0.004f;

            pose.ArmLUpper = new Vector3(0.16f, -0.55f, 0.62f + recoil * 0.08f);
            pose.ArmLFore = new Vector3(0.03f, 0.85f - recoil * 0.10f, 0.30f - recoil * 0.14f);
            pose.ArmRUpper = pose.ArmLUpper;
            pose.ArmRFore = pose.ArmLFore;

            // ガードも拳を握る。握りが緩いと指が開いて「猫の手」に見える。
            pose.Grip = 0.92f;

            // 殴られたら腰が引いて、腕が体にめり込む
            // 構えは「踏ん張り」。腰を沈めて膝を曲げ、足を広げて半歩引く。
            // 沈みは構え（StanceHipsY）から数える。絶対値で書くと
            // ガードに入った瞬間にかえって腰が上がる（以前そうなっていた）。
            pose.HipsOffset.y = StanceHipsY - 0.045f - recoil * 0.012f + sway;
            pose.HipsOffset.z = StanceHipsZ - 0.035f * recoil;

            pose.TorsoPitch = 8f - recoil * 12f;
            pose.TorsoYaw = -3f;
            pose.HeadPitch = 4f + recoil * 7f;

            // 右脚を引いた半歩の構え。足を固定したまま腰だけ沈める。
            // 左右にも広げる（狭いと上から押されたときに耐えられない形になる）。
            pose.FootR += new Vector3(0.030f, 0f, -0.100f);
            pose.FootL += new Vector3(-0.030f, 0f, 0.050f);
            return pose;
        }

        /// <summary>歩き。足は IK に任せ、腰と上体・腕だけを振る。</summary>
        PoseValues PoseWalk()
        {
            var pose = PoseDefaults();
            float p = _walkPhase;
            // 横歩きは歩幅を詰める。位相の進みと同じ値を使う（ずれると接地が滑る）
            float lat = Mathf.Abs(WalkDir.x);
            float stepLen = Mathf.Lerp(StepLength, StepLength * 0.45f, lat);
            float half = stepLen * 0.5f;
            // 足を運ぶ向き。前進なら前後、横歩きなら横
            Vector2 dir = WalkDir;

            // 位相 0〜0.5 が接地、0.5〜1 が遊脚。
            // 接地中は「体が進むぶん足が後ろへ流れる」ので、地面に対して止まって見える。
            float u = Mathf.Repeat(p / (2f * Mathf.PI), 1f);

            // 足音のタイミング。u=0 で左、u=0.5 で右が接地する。
            DetectFootstep(u);

            Vector3 FootOffset(float uu)
            {
                float sweep;
                float lift = 0f;

                if (uu < 0.5f)
                {
                    // 接地。前から後ろへ一定の速さで流れる
                    sweep = half - uu * 2f * stepLen;
                }
                else
                {
                    // 遊脚。弧を描いて前へ戻る
                    float s = (uu - 0.5f) * 2f;
                    sweep = -half + s * stepLen;
                    lift = Mathf.Sin(s * Mathf.PI) * StepHeight;
                }

                // 進行方向へ運ぶ。横歩きでは足が横に出る（前後だけだと足が滑る）
                return new Vector3(dir.x * sweep, lift, dir.y * sweep);
            }

            // 構え（アイドル）の足の置き方を基準にして、そこへ歩行の踏み出しを足す。
            // 基準を外すと歩き出した瞬間に足の幅・腰の高さ・上体の向きが変わり、
            // 「構えていた人が急に別の人になった」ように見える。
            // 横歩きは足の間隔を広げる（守りながら横へ動く形）。狭いままだと足が交差する
            Vector3 stanceL = StanceFootL + new Vector3(-0.035f * lat, 0f, 0f);
            Vector3 stanceR = StanceFootR + new Vector3(0.035f * lat, 0f, 0f);
            pose.FootL += stanceL + FootOffset(u);
            pose.FootR += stanceR + FootOffset(Mathf.Repeat(u + 0.5f, 1f));

            // 腰は構えの沈みを保ったまま、1 歩ごとに少し沈む（1 周期に 2 回）
            // ＋ 体重移動で左右に振れる
            pose.HipsOffset.y = StanceHipsY - Mathf.Abs(Mathf.Cos(p)) * 0.018f;
            pose.HipsOffset.x = Mathf.Sin(p) * 0.012f;
            pose.HipsOffset.z = StanceHipsZ;

            pose.TorsoPitch = StanceTorsoPitch + 0.5f * dir.y;
            // 横歩きでは進行方向へ少し傾ける。まっすぐ立ったまま横へ滑ると
            // 「台車に乗っている」ように見える
            pose.TorsoRoll = -dir.x * 3f;
            pose.TorsoYaw = StanceTorsoYaw + Mathf.Sin(p) * 3f;
            pose.HeadYaw = StanceHeadYaw - Mathf.Sin(p) * 2f;
            pose.HeadPitch = StanceHeadPitch;

            // 構えを崩さない。歩いても拳は顔の前に残し、肩と肘だけ小さく揺らす。
            // 腕を下ろすと「構えていた人が急に歩き出した」ように見えてしまう。
            // ただし振り幅が小さすぎると「腕を固定したまま歩く」人形になるので、
            // 肩の前後動が目で追える程度まで上げてある。
            float swing = Mathf.Cos(p) * ArmSwing * 0.55f;

            pose.ArmLUpper = StanceArmLUpper + new Vector3(0.02f, 0f, -swing * 0.40f);
            pose.ArmLFore = StanceArmLFore + new Vector3(0f, 0f, -swing * 0.28f);
            pose.ArmRUpper = StanceArmRUpper + new Vector3(-0.02f, 0f, swing * 0.40f);
            pose.ArmRFore = StanceArmRFore + new Vector3(0f, 0f, swing * 0.28f);

            pose.HandRollL = StanceHandRollL;
            pose.HandRollR = StanceHandRollR;
            pose.Grip = 0.95f;
            return pose;
        }

    }
}
