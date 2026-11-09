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

            ApplyPoseToRig(pose);
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
                // 顔を殴られたら、顔がカメラ側へ弾かれるように横を向く。
                // 一瞬で振られ、少し止まり、ゆっくり戻る（往復して痙攣しない）。
                float turn = Mathf.SmoothStep(0f, 1f, hit / 0.07f)
                             * (1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.35f));

                float toCamera = 0f;
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 to = cam.transform.position - transform.position;
                    to.y = 0f;
                    Vector3 local = transform.InverseTransformDirection(to.normalized);
                    toCamera = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
                }

                pose.HeadYaw = StanceHeadYaw + toCamera * 0.9f * turn;
                pose.HeadRoll = -Mathf.Sign(toCamera == 0f ? 1f : toCamera) * 9f * turn;
                pose.HeadPitch = StanceHeadPitch - 6f * turn;
                // 首だけでなく肩も少し付いていく。ただし「殴られた方向への 20° のねじれ」は
                // 消さずに足す（上書きすると、横から殴られたときだけ上体が正面を向く）。
                // side が 0 の正面ヒットでは 20*kv*0 ＝ 0 なので、従来と同じ値になる。
                pose.TorsoYaw = StanceTorsoYaw
                    + Mathf.Clamp(20f * kv * side + toCamera * 0.22f * turn, -34f, 34f);
            }
            else if (bodyBlow)
            {
                // 腹を殴られたら、腰を引いて上体を「く」の字に折る。腹を抱えて頭が落ちる。
                // 当たった瞬間に折れ、少し耐えてから起き上がる。
                float fold = Mathf.SmoothStep(0f, 1f, hit / 0.09f)
                             * (1f - Mathf.SmoothStep(0f, 1f, (hit - hold) / 0.45f));

                pose.TorsoPitch = StanceTorsoPitch + Mathf.Min(bend * 1.25f, 66f) * fold;
                // 横から殴られたときは、折れながら殴られた方向へ少しねじれる。
                // 前へ折れる動きが主役なので、顔（20°）より控えめの 12°。
                // 正面ヒットでは lateralSide が 0 なので、従来どおりまっすぐ折れる。
                pose.TorsoYaw = StanceTorsoYaw + 12f * kv * lateralSide;
                pose.TorsoRoll = 0f;
                pose.HeadPitch = StanceHeadPitch + bend * 0.55f * fold;
                pose.HeadYaw = StanceHeadYaw;
                pose.HeadRoll = 0f;

                // 腰は後ろへ引いて沈む。上体が前、腰が後ろで「く」の形になる。
                pose.HipsOffset.y = StanceHipsY - 0.13f * fold;
                pose.HipsOffset.z = StanceHipsZ - 0.20f * fold;

                // 両手は腹へ。
                var bellyUpper = new Vector3(0.30f, -0.88f, 0.25f);
                var bellyFore = new Vector3(-0.70f, -0.20f, 0.68f);
                pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, bellyUpper, fold);
                pose.ArmLFore = Vector3.Lerp(StanceArmLFore, bellyFore, fold);
                pose.ArmRUpper = Vector3.Lerp(StanceArmRUpper, bellyUpper, fold);
                pose.ArmRFore = Vector3.Lerp(StanceArmRFore, bellyFore, fold);
            }

            pose.HandRollL = StanceHandRollL;
            pose.HandRollR = StanceHandRollR;
            pose.Grip = 0.55f;
            return pose;
        }

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
                float recoil = HeldBellyPunchFold;
                if (recoil > 0f)
                {
                    float hitTime = _heldBellyPunchTotal - _heldBellyPunchRemaining;
                    float bendIn = Mathf.SmoothStep(0f, 1f, (hitTime - 0.015f) / 0.055f);
                    float fold = Mathf.Lerp(_heldBellyCarry, recoil, bendIn);
                    float straighten = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.32f, hitTime));
                    fold *= Mathf.Lerp(1f, 0.27f + 0.09f * _heldBellyFatigue, straighten);
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 52f, fold);
                    pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, 0f, recoil);
                    // 胴の前傾を首で打ち消し、腹で折れても顔は前に残す。
                    pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, -72f, fold);
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, Mathf.Clamp(toCamera, -35f, 35f) * 0.35f, fold);
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

                // 顔面パンチ: 頭の振れに続いて胸が起き、肩が短く揺れる。
                float faceFlick = HeldFacePunchFlick;
                if (faceFlick > 0f)
                {
                    float side = toCamera >= 0f ? 1f : -1f;
                    float hitTime = _heldFacePunchTotal - _heldFacePunchRemaining;
                    float shoulderRecoil = Mathf.SmoothStep(0.45f, 1f, (hitTime - 0.01f) / 0.035f)
                        * (1f - Mathf.SmoothStep(0f, 1f, (hitTime - 0.045f) / 0.14f));
                    // 腰を拘束位置に残し、胸を起こしながら肩を手前へ逃がす。
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, 1f, shoulderRecoil);
                    pose.TorsoYaw += side * (5f * shoulderRecoil + 3f * faceFlick);
                    pose.TorsoRoll -= side * 7f * shoulderRecoil;
                    pose.HeadPitch -= 5f * faceFlick;
                    pose.HeadYaw = Mathf.Lerp(pose.HeadYaw, Mathf.Clamp(toCamera * 1.15f, -42f, 42f), faceFlick);
                    pose.HeadRoll = Mathf.Lerp(pose.HeadRoll, -side * 13f, faceFlick);
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

        /// <summary>攻撃。振りかぶり → 打ち込み → 戻しの 3 段。</summary>
        PoseValues PoseAttack()
        {
            var pose = PoseDefaults();
            // 攻撃データの発生 / 持続 / 硬直に合わせて、
            // 「振りかぶり → 打ち込み → 戻し」の3段で動かす。
            AttackData attack = Combat != null && Combat.CurrentAttack != null
                ? Combat.CurrentAttack
                : AttackOverride;

            float startup = 0.05f;
            float total = 0.30f;
            AttackKind kind = AttackKind.Light;
            float power = 1f;

            if (attack != null)
            {
                startup = Mathf.Max(0.01f, attack.startupSeconds);
                total = Mathf.Max(startup + 0.02f, attack.TotalSeconds);
                kind = attack.kind;

                power = kind switch
                {
                    AttackKind.Heavy => 1.25f,
                    AttackKind.Break => 1.45f,
                    _ => 1f
                };
            }

            float cockEnd = Mathf.Max(0.015f, startup * 0.40f);

            // 手が伸びきる時刻。既定では「当たり判定の瞬間＝腕が伸びきった瞬間」に揃える。
            // 溜めと伸びが短すぎると腕が瞬間移動して見えるので、最低 60ms は伸びに使う。
            float strikeEnd = Mathf.Max(startup, cockEnd + 0.060f);
            float recover = Mathf.Max(0.02f, total - strikeEnd);

            // c = 振りかぶり量（0 = 構え, 1 = 引き切った）
            // p = 打ち込み量（0 = 引き切った, 1 = 当たった）
            // 2 つの量を別々に持つことで、戻しの最後にちゃんと構えへ帰る。
            float c;
            float p;

            // 打ち込みの速さ。ease-out（速く出て、伸び切る直前に減速）＝鞭のように振る。
            float strikeSpan = Mathf.Max(0.001f, strikeEnd - cockEnd);

            if (_stateTime < cockEnd)
            {
                float x = Mathf.Clamp01(_stateTime / cockEnd);
                c = 1f - Mathf.Pow(1f - x, 2f);
                p = 0f;
            }
            else if (_stateTime < strikeEnd)
            {
                c = 1f;
                float x = Mathf.Clamp01((_stateTime - cockEnd) / strikeSpan);
                p = 1f - Mathf.Pow(1f - x, 3f);
            }
            else
            {
                float u = Mathf.Clamp01((_stateTime - strikeEnd) / recover);
                c = 1f - Mathf.SmoothStep(0.55f, 1f, u);
                p = 1f - Mathf.SmoothStep(0.40f, 1f, u);
            }

            // 上体は腕より 12ms 先行させる。体が先に回ってから腕が出ると、
            // 「腕だけ振っている」ではなく体重が乗った打撃に見える。
            float pBody;
            if (_stateTime < cockEnd)
            {
                pBody = 0f;
            }
            else if (_stateTime < strikeEnd)
            {
                float x = Mathf.Clamp01((_stateTime - cockEnd + 0.012f) / strikeSpan);
                pBody = 1f - Mathf.Pow(1f - x, 3f);
            }
            else
            {
                float u = Mathf.Clamp01((_stateTime - strikeEnd) / recover);
                pBody = 1f - Mathf.SmoothStep(0.40f, 1f, u);
            }

            // 伸び切ったあと、拳と胸がわずかに行き過ぎてから戻る（follow-through）。
            // 打ち終わりで「その場で止まった」感じを消す。戻りの前半で 0 に戻る。
            float followThrough = 0f;
            if (_stateTime >= strikeEnd)
            {
                float fu = Mathf.Clamp01((_stateTime - strikeEnd) /
                    Mathf.Max(0.06f, recover * 0.55f));
                followThrough = Mathf.Sin(fu * Mathf.PI);
            }

            // 攻撃の種類で「振りかぶり」と「打ち込み」の形を変える。
            // どれも 構え → 振りかぶり(c) → 打ち込み(p) → 構え の同じ骨組みを使う。
            Vector3 cockUpper;
            Vector3 cockFore;
            Vector3 punchUpper;
            Vector3 punchFore;
            float yawCock;
            float yawPunch;
            float pitchCock;
            float pitchPunch;
            float lungeDistance;
            float rollPunch;      // 伸び切ったときの手首のひねり（0 = 掌が下・拳頭が前）
            float shoulderYaw;    // 打つ腕の肩を前へ送る胸郭の回旋
            float shoulderPitch;  // 打つ腕の肩を押し込む胸郭の前傾

            // 左腕（引き手）。胸の前で守るが、手は顔まで上げない（上げると「降参」に見える）。
            var cockUpperL = new Vector3(0.34f, -0.90f, 0.06f);
            var cockForeL = new Vector3(-0.02f, -0.55f, 0.78f);
            var punchUpperL = new Vector3(0.30f, -0.86f, 0.22f);
            var punchForeL = new Vector3(-0.04f, -0.35f, 0.92f);

            switch (kind)
            {
                case AttackKind.Heavy:
                    // 体重を乗せたフック。腕を横に広げると T ポーズに見えるので、
                    // 肘を横へ上げて拳を胸の前へ引き、振り抜きは腰の回転で見せる。
                    // 前腕を背中側へ振ると肘が畳まれて「腕が折れる」ので、前腕は前向きのまま。
                    cockUpper = new Vector3(0.70f, -0.58f, -0.18f);
                    cockFore = new Vector3(0.30f, 0.34f, 0.89f);
                    punchUpper = new Vector3(0.22f, 0.10f, 0.95f);
                    punchFore = new Vector3(0.10f, -0.05f, 1.00f);
                    yawCock = 34f;
                    yawPunch = -30f;
                    pitchCock = -3f;
                    pitchPunch = 10f;
                    lungeDistance = 0.075f;
                    rollPunch = -22f;      // フックは拳を立て気味に残す
                    shoulderYaw = 7f;
                    shoulderPitch = 2f;
                    break;

                case AttackKind.Break:
                    // 片手の掌底。肘は下げて脇を締めたまま、胸の前から突き出す。
                    // 溜めは「拳を胸へ引きつける」形。前腕を立てたままなので肘が畳まれない。
                    // 両手で押すと拳が 2 つ横に並び、真横からは「両腕が前に出た」画になる
                    // （実測で左右とも z=0.51 に揃っていた）ので、押すのは前の手だけにする。
                    cockUpper = new Vector3(0.22f, -0.86f, -0.28f);
                    cockFore = new Vector3(0.06f, 0.42f, 0.90f);
                    punchUpper = new Vector3(0.18f, 0.05f, 0.98f);
                    punchFore = new Vector3(0.10f, 0.00f, 1.00f);
                    yawCock = 16f;
                    yawPunch = -14f;
                    pitchCock = -4f;
                    pitchPunch = 13f;
                    lungeDistance = 0.060f;
                    // 掌底なので掌は前向きのまま残し、肩を胸ごと押し込む。
                    rollPunch = -12f;
                    shoulderYaw = 0f;
                    shoulderPitch = 6f;
                    break;

                default:
                    // ストレート。拳を肋の横まで引いてから前へ突き出す。
                    // 溜めを顔の高さにすると「手を振っている」に見えるので、必ず胸より下に置く。
                    // 前腕まで後ろへ振ると拳が背中に回って「両手を広げた」画になるので、
                    // 肘だけ後ろへ引き、前腕は前向きのままにして拳を肋の横へ落とす。
                    cockUpper = new Vector3(0.30f, -0.90f, -0.30f);
                    cockFore = new Vector3(0.05f, -0.20f, 0.98f);
                    punchUpper = new Vector3(0.15f, 0.08f, 0.98f);
                    punchFore = new Vector3(0.03f, 0.02f, 1.00f);
                    yawCock = 26f;
                    yawPunch = -24f;
                    pitchCock = 0f;
                    pitchPunch = 6f;
                    lungeDistance = 0.045f;
                    // ストレートは掌を下へ返しきる（＝拳頭が前へ出る）。
                    rollPunch = -3f;
                    shoulderYaw = 6f;
                    shoulderPitch = 3f;
                    break;
            }

            // 相手が近いのに腕を伸ばし切ると、拳が体にめり込んで見える。
            // 間合いに合わせて拳の止まる位置を手前へ寄せる（当たり判定は変えない）。
            if (Combat != null && Combat.AimDistance > 0.01f)
            {
                // 0.45m で最も短く、1.00m 以上で伸び切る
                float s = Mathf.Clamp01((Combat.AimDistance - 0.45f) / 0.55f);
                float k = Mathf.Lerp(0.80f, 1f, s);
                punchUpper = new Vector3(punchUpper.x * k, punchUpper.y, punchUpper.z * k);
                punchFore = new Vector3(punchFore.x * k, punchFore.y, punchFore.z * k);

            }

            // 右腕。基準は構え（アイドルと同じ姿勢）。
            // 腕を下ろした姿勢から補間すると、攻撃の 1 フレーム目で
            // 拳が下りて足がそろい、腰が浮く「姿勢の飛び」が出る。
            pose.ArmRUpper = Vector3.Lerp(
                Vector3.Lerp(StanceArmRUpper, cockUpper, c), punchUpper, p);
            pose.ArmRFore = Vector3.Lerp(
                Vector3.Lerp(StanceArmRFore, cockFore, c), punchFore, p);

            // 左腕: 崩しは両手、それ以外は顔の前の構えを保つ（拳を引いたまま振らない）
            // 突きで左のガード手まで下ろすと「両手を広げた」画になるので、少ししか動かさない。
            // 下ろしすぎると「打つたびにガードを落とす」ように見えるので、
            // 構えから 1 割前後しか動かさない（拳は顎の前に残る）。
            float leftAmount = Mathf.Max(c * 0.16f, p * 0.10f);
            Vector3 leftUpper = Vector3.Lerp(
                Vector3.Lerp(StanceArmLUpper, cockUpperL, c), punchUpperL, p);
            Vector3 leftFore = Vector3.Lerp(
                Vector3.Lerp(StanceArmLFore, cockForeL, c), punchForeL, p);
            pose.ArmLUpper = Vector3.Lerp(StanceArmLUpper, leftUpper, leftAmount);
            pose.ArmLFore = Vector3.Lerp(StanceArmLFore, leftFore, leftAmount);

            // 打っていない側の手を「肩の横」へ引く。
            // 構えのままだと拳が胸の 27cm 前・肩の高さに残り、真横から見ると
            // 打つ手と高さが揃って「両腕が前に出ている」画になる（実測で 2 本とも
            // z=0.2〜0.7 の帯に並んでいた）。
            // このリグは肩 1.15m・腕 0.50m なので、肘を下げたまま拳を顎まで
            // 上げると 1.12m が上限（顎は 1.31m）。高さでは離せない。
            // なので前ではなく「外・わずかに後ろ」へ逃がす。手は肩の外側に付き、
            // 真横カメラからは体の陰に入る。肘は肋に付いたまま。
            var tuckUpperL = new Vector3(0.40f, -0.90f, -0.04f);
            var tuckForeL = new Vector3(0.08f, 0.97f, -0.10f);
            float tuck = Mathf.Max(c * 0.70f, p * 1.00f);
            pose.ArmLUpper = Vector3.Lerp(pose.ArmLUpper, tuckUpperL, tuck);
            pose.ArmLFore = Vector3.Lerp(pose.ArmLFore, tuckForeL, tuck);

            // 手首のひねりも構えから続ける（0 に戻すと毎回拳がくるっと反転する）。
            // 伸び切る瞬間は「ナックルが前に出る」向きへ返す。前腕軸まわりの 0 が
            // 掌下（＝拳頭が前）なので、構えの -48 のままだと掌が横向きに残り、
            // 「手のひらを押し出す」形に見える。行き過ぎだけ少しだけ余計に返す。
            pose.HandRollL = StanceHandRollL;
            pose.HandRollR = Mathf.Lerp(StanceHandRollR, rollPunch - followThrough * 3f, p);

            // 当たる瞬間は握り切る。握りが緩いと指が伸びて「手を押し出す」形に見える。
            pose.Grip = 0.92f + 0.06f * p;

            // 3段コンボの 2 段目は左で打つ。右腕で組んだ姿勢を X 反転して左腕へ移し、
            // 引き手と入れ替える（左右で同じ形のパンチになる）。
            bool leftPunch = ComboStep == 1;
            if (leftPunch)
            {
                Vector3 punchU = pose.ArmRUpper;
                Vector3 punchF = pose.ArmRFore;
                Vector3 guardU = pose.ArmLUpper;
                Vector3 guardF = pose.ArmLFore;

                pose.ArmLUpper = MirrorX(punchU);
                pose.ArmLFore = MirrorX(punchF);
                pose.ArmRUpper = MirrorX(guardU);
                pose.ArmRFore = MirrorX(guardF);

                pose.HandRollL = Mathf.Lerp(StanceHandRollL,
                    -rollPunch + followThrough * 3f, p);
                pose.HandRollR = StanceHandRollR;
            }

            // 腰の捻りと体重移動。振りかぶりは構えの半身から捻り込み、
            // 打ち込みの到達角度は今までの見た目（構え基準ではない絶対角）を保つ。
            // ただし伸び切る瞬間だけ胸郭を shoulderYaw / shoulderPitch ぶん余計に回し、
            // 打つ腕の肩が前へ出ているように見せる（リグに肩甲骨を動かす API は無いので、
            // BendTorso が配る胸の回旋・前傾で代用する）。
            float cockYaw = StanceTorsoYaw + c * yawCock * power;
            pose.TorsoYaw = Mathf.Lerp(cockYaw, yawPunch * power - shoulderYaw, pBody)
                - followThrough * shoulderYaw * 0.30f;
            float cockPitch = StanceTorsoPitch + pitchCock * c;
            pose.TorsoPitch = Mathf.Lerp(cockPitch, pitchPunch + shoulderPitch, pBody)
                + followThrough * shoulderPitch * 0.35f;
            // 肩を送った分だけ胸も回るので、顔はその分だけ戻して相手を見続ける。
            pose.HeadYaw = Mathf.Lerp(StanceHeadYaw + c * 4f, -4f + shoulderYaw, p);
            // 胸を押し込んだ分だけ顔が落ちるので、その分だけ顔を戻して相手を見る。
            pose.HeadPitch = Mathf.Lerp(StanceHeadPitch, pitchPunch * 0.35f - shoulderPitch * 0.35f, p);

            // 左で打つときは体の向きも左右反転させる（右で打つ形のまま左腕だけ
            // 動かすと、胸は右へ捻ったまま左腕が出て「ねじ切れた」画になる）。
            if (leftPunch)
            {
                pose.TorsoYaw = -pose.TorsoYaw;
                pose.HeadYaw = -pose.HeadYaw;
            }

            // 踏み込み（足は構えの位置に固定したまま、腰だけ前へ出る）
            pose.FootL += StanceFootL;
            pose.FootR += StanceFootR;

            // 体重が後ろ足に残らないよう、打ち込みに合わせてかかとを上げる。
            // つま先を床に残す処理は脚の IK のあと（RaiseHeel）で行う。
            // このリグは足首→ボールが 12cm しかないので、26° では 1.5cm しか
            // 浮かない（実測）。振り抜きで体重が乗ったと分かる角度まで上げる。
            float heel = 44f * p * power;
            pose.FootPitchR = heel;

            // 前足に体重を乗せる。足は床に固定したまま腰だけ前へ送るので滑らない
            // （脚の IK が伸びを吸収し、後ろ足はかかとが上がって付いてくる）。
            // 伸び切ったあとは拳に引かれて少しだけ行き過ぎてから戻る。
            pose.HipsOffset.z = StanceHipsZ
                + Mathf.Lerp(-0.012f * c, lungeDistance * power, pBody)
                + 0.016f * pBody * power
                + followThrough * 0.008f;

            // 前足が体重を受ける分だけ腰を沈める（前膝が伸びきって見えないように）。
            // 打ち終わりに少しだけ深く沈んでから戻る＝前足で踏ん張った感触。
            pose.HipsOffset.y = StanceHipsY
                - 0.030f * pBody
                - 0.010f * pBody * power
                - followThrough * 0.004f;
            return pose;
        }

        /// <summary>組み立てた姿勢をリグへ流し込む（IK・倒れ・指・かかと）。</summary>
        void ApplyPoseToRig(PoseValues pose)
        {
            Vector3 pole = transform.position + transform.forward * 2f + Vector3.up * 0.55f;

            // 倒れる途中の足。IK は「倒す前」の座標で解き、そのあと LayBody で
            // 体ごと倒れるので、狙った床の位置に足を置くには倒れるぶんを打ち消す。
            // さらに倒れきる直前で、足を「脚を伸ばした先」へ滑らせる（膝の二つ折り防止）。
            if (pose.LayAngle < -1f)
            {
                Vector3 hipsLocal = Rig.RestHips + pose.HipsOffset;
                float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-70f, -88f, pose.LayAngle));
                Vector3 fallAxis = Quaternion.AngleAxis(pose.LaySide, Vector3.up) * Vector3.right;
                Quaternion fallRotation = Quaternion.AngleAxis(pose.LayAngle, fallAxis);

                // Let the heels slide apart as the back lands, with one knee left a little more bent.
                Vector3 extendedL = hipsLocal + fallRotation * (Rig.RestFootL - Rig.RestHips)
                    + new Vector3(-0.035f, -0.025f, -0.105f);
                Vector3 extendedR = hipsLocal + fallRotation * (Rig.RestFootR - Rig.RestHips)
                    + new Vector3(0.020f, -0.025f, -0.032f);

                pose.FootL = Vector3.Lerp(pose.FootL, extendedL, follow);
                pose.FootR = Vector3.Lerp(pose.FootR, extendedR, follow);

                // IK は「倒す前」の姿勢で解き、そのあと LayBody で体ごと倒れる。
                // 狙った床の位置に足を着けるには、倒れるぶんを打ち消しておく。
                Quaternion undo = Quaternion.Inverse(fallRotation);
                pose.FootL = hipsLocal + undo * (pose.FootL - hipsLocal);
                pose.FootR = hipsLocal + undo * (pose.FootR - hipsLocal);
            }

            // 押された瞬間に足が残り、片足で体勢を立て直し始める。
            // ここで足先を動かすと、この後の IK（SolveLeg）が脚の形を合わせてくれるので、
            // 「滑っている」ではなく「踏ん張って持ちこたえている」に見える。
            if (Grapple != null && !Grapple.IsHandingOff && Grapple.PushPulse > 0f && _state == State.Grabbed)
            {
                float p = Mathf.Clamp01(Grapple.PushPulse);
                float t = 1f - p;                                   // 0 → 1（押され始め → 終わり）
                float beat = t * Mathf.PI * 2f;
                float stepL = Mathf.Max(0f, Mathf.Sin(beat));       // 左足の踏み直し
                float stepR = Mathf.Max(0f, -Mathf.Sin(beat));      // 右足の踏み直し
                float drag = Mathf.Sin(Mathf.PI * t);               // 押されて足が残る量

                // 向きを変えても、足は実際に押された向きと逆へ一瞬残る。
                Vector3 localPush = Vector3.forward * (Grapple.Kind == GrappleKind.Back ? -1f : 1f);
                pose.FootL += new Vector3(0.02f, 0.08f * stepL, 0f)
                    + localPush * (0.08f * stepL - 0.13f * drag);
                pose.FootR += new Vector3(-0.02f, 0.08f * stepR, 0f)
                    + localPush * (0.08f * stepR - 0.13f * drag);

                // 踏み直す足はつま先立ちになる（かかとが浮く）
                pose.FootPitchL = Mathf.Max(pose.FootPitchL, 18f * stepL);
                pose.FootPitchR = Mathf.Max(pose.FootPitchR, 18f * stepR);

                // 押されて腰が落ちる
                pose.HipsOffset.y -= 0.025f * drag;
            }

            // 腰のずれはキャラクター基準。ワールドのまま足すと、体が回ったときに
            // 「後ろへ押される」が横や前へずれる。
            // 踏み込みで root が前へ出た分、足を後ろへ残す（足が床を滑らない）。
            // 踏み込みが終わると 0 に戻るので、戻る分だけ足が体の下へ寄る。
            if (Motor != null && Motor.LungeTravel > 0.0005f)
            {
                Vector3 back = transform.InverseTransformDirection(Motor.transform.forward);
                back.y = 0f;
                pose.FootL -= back * Motor.LungeTravel;
                pose.FootR -= back * Motor.LungeTravel;
            }

            // 腕の狙いを体幹に追従させるための、このフレームの体幹角度。
            // ArmDirection が「肩の面」を基準に腕の向きを作るのに使う。
            _armFollowYaw = pose.TorsoYaw;
            _armFollowPitch = pose.TorsoPitch;
            _armFollowRoll = pose.TorsoRoll;

            Rig.OffsetHips(transform.TransformDirection(pose.HipsOffset));
            Rig.BendTorso(pose.TorsoPitch, pose.TorsoRoll, pose.TorsoYaw);
            ApplyFaceFlick(ref pose.HeadPitch, ref pose.HeadYaw, ref pose.HeadRoll);
            Rig.TurnHead(pose.HeadPitch, pose.HeadYaw, pose.HeadRoll);
            // 姿勢を組んだ後の頭の位置。次のフレームで「敵の顔を見る」角度を測るのに使う
            // （姿勢を組む前は ResetPose で立ち姿勢の頭の高さに戻っていて、見上げる角度が浅くなる）。
            _posedHeadPosition = Rig.Head.position;
            _hasPosedHead = true;

            Rig.SetArm(true, ArmDirection(true, pose.ArmLUpper).normalized,
                ArmDirection(true, pose.ArmLFore).normalized, pose.HandRollL);
            Rig.SetArm(false, ArmDirection(false, pose.ArmRUpper).normalized,
                ArmDirection(false, pose.ArmRFore).normalized, pose.HandRollR);

            if (pose.HandOffBrace > 0f && Grapple.Captor != null
                && Grapple.Captor.TryGetComponent<EnemyFigure>(out var receiverFigure)
                && receiverFigure.ChestBone != null)
            {
                Transform chest = receiverFigure.ChestBone;
                Transform shoulderL = receiverFigure.ShoulderBone(true);
                Transform shoulderR = receiverFigure.ShoulderBone(false);
                if (shoulderL != null && shoulderR != null)
                {
                    Vector3 braceCenter = (shoulderL.position + shoulderR.position) * 0.5f - chest.up * 0.16f;
                    BracePalm(true, chest, braceCenter, pose.HandOffBrace);
                    BracePalm(false, chest, braceCenter, pose.HandOffBrace);
                }
            }

            DebugArmLUpper = ArmDirection(true, pose.ArmLUpper).normalized;
            DebugArmLFore = ArmDirection(true, pose.ArmLFore).normalized;
            DebugArmRUpper = ArmDirection(false, pose.ArmRUpper).normalized;
            DebugArmRFore = ArmDirection(false, pose.ArmRFore).normalized;
            DebugTorsoPitch = pose.TorsoPitch;
            DebugHeadPitch = pose.HeadPitch;
            DebugTorsoYaw = pose.TorsoYaw;
            DebugHeadYaw = pose.HeadYaw;

            // 指を握る。開いたままだと攻撃が「手を差し出す」に見える。
            // 曲がる向きは左右で鏡像なので、リグ側が左右別々の軸を使う。
            Rig.CurlFingers(true, pose.Grip);
            Rig.CurlFingers(false, pose.Grip);

            // 足を動かす必要が無いときは IK を掛けない（無駄な計算と微妙な揺れを避ける）。
            float hipMove = Mathf.Abs(pose.HipsOffset.x) + Mathf.Abs(pose.HipsOffset.y) + Mathf.Abs(pose.HipsOffset.z);
            bool needLegSolve = hipMove > 0.02f
                || (pose.FootL - Rig.RestFootL).sqrMagnitude > 0.0004f
                || (pose.FootR - Rig.RestFootR).sqrMagnitude > 0.0004f;

            // うつ伏せは膝の向き（ポール）を下へ回す。立ち姿勢のまま「前＋上」で解くと、
            // 体を伏せたときに膝が逆向き（過伸展）に折れる（実測）。
            Vector3 poleNow = pole;
            if (_downForward && (_state == State.Down || _state == State.GetUp) && pose.LayAngle > 1f)
            {
                float t = Mathf.Clamp01(pose.LayAngle / 92f);
                Vector3 pronePole = transform.position - Vector3.up * 1.6f + transform.forward * 0.6f;
                poleNow = Vector3.Lerp(pole, pronePole, t);
            }

            if (needLegSolve)
            {
                // 足を狙った位置へ置く（接地）。このとき脚の形は IK が決めるので、
                // しゃがみ姿勢を上書きしない（上書きすると足が地面から外れる）。
                Rig.SolveLeg(true, transform.TransformPoint(pose.FootL), poleNow);
                Rig.SolveLeg(false, transform.TransformPoint(pose.FootR), poleNow);
            }
            else if (pose.LegTuck > 0.001f)
            {
                // 倒れるときは脚を伸ばす（曲がったまま倒れると体育座りになる）
                Rig.BlendLegsToCrouch(pose.LegTuck);
            }
            else if (pose.LayAngle < -1f)
            {
                Rig.BlendLegsToRest(Mathf.Clamp01(-pose.LayAngle / 88f) * 0.96f);
            }

            // 倒れるのは最後。立った姿勢を丸ごと倒すので、IK の結果も一緒に倒れる。
            if (Mathf.Abs(pose.LayAngle) > 0.01f)
                Rig.LayBody(pose.LayAngle, pose.LaySide);

            // うつ伏せ（前へ崩れる）だけは、体を回した後に脚を解き直す。
            // LayBody は脚の IK より後に来るので、解いた脚ごと回すと脚が宙を向く（実測）。
            // 回した後の骨格から足先へ向けて引き直せば、床に沿った脚になる。
            if (_downForward && (_state == State.Down || _state == State.GetUp)
                && needLegSolve && pose.LayAngle > 1f)
            {
                Rig.SolveLeg(true, transform.TransformPoint(pose.FootL), poleNow);
                Rig.SolveLeg(false, transform.TransformPoint(pose.FootR), poleNow);
            }

            // うつ伏せ系の腕は、体を回した後に**世界の向き**で置き直す。
            // 回す前に「上」で組むと、体が起きるにつれて腕が上を向き、バンザイで起き上がる（実測）。
            // 手は地面（-Y）へ、肘は外へ。起き上がりの後半で構えの腕へ戻す。
            if (_downForward && (_state == State.Down || _state == State.GetUp))
            {
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
                Vector3 left = Vector3.Cross(Vector3.up, fwd);   // 彼女の左
                Vector3 up = Vector3.up;

                Vector3 upperL, foreL, upperR, foreR;
                float toStanceArms = 0f;

                if (_state == State.Down)
                {
                    // うつ伏せ: 左腕は頭の先へ伸ばし、右腕は肘を張って手を顔の横へ
                    upperL = (fwd * 0.92f + left * 0.22f - up * 0.10f).normalized;
                    foreL = (fwd * 0.96f + left * 0.10f - up * 0.04f).normalized;
                    upperR = (fwd * 0.52f - left * 0.62f - up * 0.20f).normalized;
                    foreR = (fwd * 0.58f + left * 0.52f - up * 0.26f).normalized;
                    float reach = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.22f, 0.86f, _stateTime / _downFallSeconds));
                    upperL = Vector3.Slerp((Rig.LLowerArm.position - Rig.LUpperArm.position).normalized, upperL, reach);
                    foreL = Vector3.Slerp((Rig.LHand.position - Rig.LLowerArm.position).normalized, foreL, reach);
                    upperR = Vector3.Slerp((Rig.RLowerArm.position - Rig.RUpperArm.position).normalized, upperR, reach);
                    foreR = Vector3.Slerp((Rig.RHand.position - Rig.RLowerArm.position).normalized, foreR, reach);
                }
                else
                {
                    // 起き上がり: 両手を頭の横について床を押す → 立ち上がりで構えへ
                    float gu = Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, _stateDuration));
                    toStanceArms = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.92f, gu));

                    upperL = (fwd * 0.42f + left * 0.50f - up * 0.72f).normalized;
                    foreL = (fwd * 0.46f - up * 0.88f).normalized;
                    upperR = (fwd * 0.42f - left * 0.50f - up * 0.72f).normalized;
                    foreR = (fwd * 0.46f - up * 0.88f).normalized;
                }

                if (toStanceArms > 0f)
                {
                    upperL = Vector3.Slerp(upperL, ArmDirection(true, StanceArmLUpper).normalized, toStanceArms);
                    foreL = Vector3.Slerp(foreL, ArmDirection(true, StanceArmLFore).normalized, toStanceArms);
                    upperR = Vector3.Slerp(upperR, ArmDirection(false, StanceArmRUpper).normalized, toStanceArms);
                    foreR = Vector3.Slerp(foreR, ArmDirection(false, StanceArmRFore).normalized, toStanceArms);
                }

                Rig.SetArm(true, upperL, foreL, pose.HandRollL);
                Rig.SetArm(false, upperR, foreR, pose.HandRollR);
            }

            // The supine pose normally points toward the opposite end of the lane.
            // Mirror its facing around the feet without turning the gameplay root mid-roll.
            if (_groundRolled && (_state == State.Down || _state == State.GetUp))
            {
                float turn = _state == State.GetUp
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.98f, _stateTime / _stateDuration)) : 0f;
                Quaternion facing = Quaternion.AngleAxis(180f * (1f - turn), Vector3.up);
                Vector3 origin = transform.position;
                Rig.Hips.SetPositionAndRotation(origin + facing * (Rig.Hips.position - origin),
                    facing * Rig.Hips.rotation);
            }

            if (_state == State.Down && Stats != null && Stats.GroundMount.BlocksGetUp)
                ApplyGroundMountReaction(Stats.GroundMount);

            // 状態が切り替わった直後は、切り替え前の姿勢から寄せる（カクつき防止）
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + Time.deltaTime / Mathf.Max(0.01f, _blendSeconds));
                Rig.BlendFromSnapshot(Mathf.SmoothStep(0f, 1f, _blend));
            }

            // かかと上げは最後。つま先を床に残したまま足首を回す（IK の解き直しを含む）。
            if (pose.FootPitchL > 0.01f)
                Rig.RaiseHeel(true, pose.FootPitchL, pole);

            if (pose.FootPitchR > 0.01f)
                Rig.RaiseHeel(false, pose.FootPitchR, pole);
        }

        void ApplyGroundMountReaction(GroundMountAttack mount)
        {
            if (mount.Stage == GroundMountAttack.Beat.Roll)
            {
                ApplyProneMountRoll(mount);
                return;
            }

            float effort = mount.StrugglePulse;
            float headRecoil = mount.HeadRecoil;
            float torsoRecoil = mount.TorsoRecoil;
            float legRecoil = mount.LegRecoil;
            float guard = mount.Fear;
            if (guard <= 0f)
                return;

            float windup = mount.Cock;
            Vector3 bodyAxis = Vector3.ProjectOnPlane(Rig.Head.position - Rig.Hips.position, Vector3.up);
            if (bodyAxis.sqrMagnitude <= 0.001f)
                return;

            bodyAxis.Normalize();
            Vector3 crossBody = Vector3.Cross(Vector3.up, bodyAxis);
            Vector3 leftShoulder = Rig.LUpperArm.position - Rig.RUpperArm.position;
            Vector3 left = crossBody * Mathf.Sign(Vector3.Dot(crossBody, leftShoulder));
            float incomingSide = Mathf.Sign(Vector3.Dot(crossBody, mount.StrikeSide));
            float impactSide = Mathf.Sign(Vector3.Dot(crossBody, mount.ImpactSide));
            bool incomingLeft = Vector3.Dot(left, mount.StrikeSide) > 0f;
            bool impactLeft = Vector3.Dot(left, mount.ImpactSide) > 0f;
            Vector3 guardOffset = Rig.Head.position - Rig.RUpperArm.position;

            // Follow the side of the fist at contact, even as the other hand begins winding up.
            Rig.Hips.position += (bodyAxis * 0.022f - crossBody * (0.027f * impactSide)) * torsoRecoil * guard;
            Rig.Chest.rotation = Quaternion.AngleAxis((incomingSide * 5f * windup + impactSide * 22f * torsoRecoil)
                * guard, bodyAxis) * Quaternion.AngleAxis(9f * torsoRecoil, crossBody) * Rig.Chest.rotation;
            float defaultHeadRecoil = mount.ImpactWasLeftPunch ? 0f : headRecoil;
            if (Rig.Neck != null)
                Rig.Neck.rotation = Quaternion.AngleAxis(impactSide * 11f * defaultHeadRecoil * guard, bodyAxis)
                    * Rig.Neck.rotation;

            float cheekTurn = -25f * Vector3.Dot(crossBody, Vector3.back);
            Rig.Head.rotation = Quaternion.AngleAxis((cheekTurn + incomingSide * 7f * windup
                + impactSide * 41f * defaultHeadRecoil) * guard, bodyAxis) * Rig.Head.rotation;

            float centering = mount.LeftHookCentering * guard;
            if (centering > 0.001f)
            {
                Vector3 facing = Vector3.ProjectOnPlane(Rig.Head.forward, bodyAxis);
                float neutralTurn = Vector3.SignedAngle(facing, Vector3.up, bodyAxis);
                Rig.Head.rotation = Quaternion.AngleAxis(neutralTurn * 0.60f * centering, bodyAxis) * Rig.Head.rotation;
            }
            if (mount.ImpactWasLeftPunch && headRecoil > 0.001f)
                TurnHeadToLeftShoulder(bodyAxis, crossBody, left, headRecoil * guard);

            float lookWeight = mount.HasTakenPunch ? 0f : guard;
            if (lookWeight > 0.001f)
            {
                Vector3 toAttacker = mount.AttackerHead - Rig.Head.position;
                Quaternion look = Quaternion.FromToRotation(Rig.Head.forward, toAttacker);
                Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, look, 42f);
                Rig.Head.rotation = Quaternion.Slerp(Quaternion.identity, turn, lookWeight) * Rig.Head.rotation;
            }

            if (legRecoil > 0.001f)
            {
                Vector3 pole = Rig.Hips.position + Vector3.up * 0.65f;
                Vector3 curl = bodyAxis * 0.10f + Vector3.up * 0.026f;
                BendMountKnee(true, curl, pole, (impactLeft ? 0.43f : 0.20f) * legRecoil);
                BendMountKnee(false, curl, pole, (impactLeft ? 0.20f : 0.43f) * legRecoil);
            }

            float flinch = mount.ArmRecoil;
            float brace = windup + 0.5f * effort;
            float give = mount.Stage == GroundMountAttack.Beat.Strike ? 0.28f * mount.Drive : 0f;
            bool rightNearCamera = Vector3.Dot(-left, mount.ShotOffset) > 0f;
            float leftBrace = brace * (incomingLeft ? 1f : 0.35f);
            float leftRecoil = Mathf.Max(incomingLeft ? give : 0f, impactLeft ? flinch : 0f);
            float rightBrace = brace * (incomingLeft ? 0.35f : 1f);
            float rightRecoil = Mathf.Max(incomingLeft ? 0f : give, impactLeft ? 0f : flinch);

            Quaternion rightShoulder = Rig.RUpperArm.localRotation;
            Quaternion rightElbow = Rig.RLowerArm.localRotation;
            Quaternion rightHand = Rig.RHand.localRotation;
            // Pose the right arm with the left side's reaction, then reflect that solution.
            PoseMountRightArm(-left, bodyAxis, guardOffset, guard, leftBrace, leftRecoil, rightNearCamera);
            Vector3 leftUpper = Vector3.Reflect(Rig.RLowerArm.position - Rig.RUpperArm.position, left);
            Vector3 leftForearm = Vector3.Reflect(Rig.RHand.position - Rig.RLowerArm.position, left);
            Rig.SetArm(true,
                Vector3.Slerp(Rig.LLowerArm.position - Rig.LUpperArm.position, leftUpper, guard),
                Vector3.Slerp(Rig.LHand.position - Rig.LLowerArm.position, leftForearm, guard));
            Rig.ReflectRightWristToLeft(left, guard);

            Rig.RUpperArm.localRotation = rightShoulder;
            Rig.RLowerArm.localRotation = rightElbow;
            Rig.RHand.localRotation = rightHand;
            PoseMountRightArm(-left, bodyAxis, guardOffset, guard, rightBrace, rightRecoil, rightNearCamera);

            Rig.CurlFingers(true, Mathf.Lerp(0.18f, 0.68f - 0.30f * leftRecoil, guard));
            Rig.CurlFingers(false, Mathf.Lerp(0.18f, 0.68f - 0.30f * rightRecoil, guard));
        }

        void TurnHeadToLeftShoulder(Vector3 bodyAxis, Vector3 crossBody, Vector3 left, float weight)
        {
            Vector3 facing = Vector3.ProjectOnPlane(Rig.Head.forward, bodyAxis);
            Vector3 towardLeft = Vector3.up * 0.87f + left * 0.50f;
            float turn = Vector3.SignedAngle(facing, towardLeft, bodyAxis) * weight;
            if (Rig.Neck != null)
            {
                Rig.Neck.rotation = Quaternion.AngleAxis(turn * 0.27f, bodyAxis) * Rig.Neck.rotation;
                turn *= 0.73f;
            }
            Rig.Head.rotation = Quaternion.AngleAxis(turn, bodyAxis)
                * Quaternion.AngleAxis(-6f * weight, crossBody) * Rig.Head.rotation;
        }

        void ApplyProneMountRoll(GroundMountAttack mount)
        {
            Vector3 axis = Vector3.ProjectOnPlane(Rig.Head.position - Rig.Hips.position, Vector3.up);
            if (axis.sqrMagnitude <= 0.001f)
                return;

            axis.Normalize();
            Vector3 across = Vector3.Cross(Vector3.up, axis);
            float chestTurn = mount.ShoulderRoll;
            float pelvisTurn = mount.RollProgress;
            float twist = chestTurn - pelvisTurn;
            float signedRoll = mount.Side * 180f;
            Quaternion spine = Rig.Spine != null ? Rig.Spine.rotation : Quaternion.identity;
            Quaternion chest = Rig.Chest.rotation;
            Quaternion leftThigh = Rig.LUpperLeg.rotation;
            Quaternion rightThigh = Rig.RUpperLeg.rotation;
            Quaternion leftShin = Rig.LLowerLeg.rotation;
            Quaternion rightShin = Rig.RLowerLeg.rotation;
            float leftFootHeight = Rig.LFoot.position.y;
            float rightFootHeight = Rig.RFoot.position.y;
            bool pulledLeft = mount.RollLeftShoulder;

            // The grabbed shoulder leads. Keep the pelvis and legs in their own phases
            // so they do not inherit the shoulder's turn as a single rigid pose.
            Rig.Hips.position += across * (0.09f * mount.Side * twist);
            Rig.Hips.rotation = Quaternion.AngleAxis(signedRoll * pelvisTurn, axis) * Rig.Hips.rotation;
            if (Rig.Spine != null)
                Rig.Spine.rotation = Quaternion.AngleAxis(signedRoll * Mathf.Lerp(pelvisTurn, chestTurn, 0.50f), axis)
                    * Quaternion.AngleAxis(-14f * twist, across) * spine;
            Rig.Chest.rotation = Quaternion.AngleAxis(signedRoll * chestTurn, axis)
                * Quaternion.AngleAxis(-24f * twist, across) * chest;

            float leadingLeg = mount.RollPhase(0.46f, 0.96f);
            float trailingLeg = mount.RollPhase(0.54f, 0.99f);
            float leftTurn = pulledLeft ? leadingLeg : trailingLeg;
            float rightTurn = pulledLeft ? trailingLeg : leadingLeg;
            // The leading leg sweeps out from the hip. Carry the shin along without curling the knee.
            Quaternion sweep = Quaternion.AngleAxis(-mount.Side * 16f * Mathf.Sin(Mathf.PI * leadingLeg), Vector3.up);
            Quaternion trail = Quaternion.AngleAxis(mount.Side * 5f * Mathf.Sin(Mathf.PI * trailingLeg), Vector3.up);
            Quaternion leftRoll = (pulledLeft ? sweep : trail)
                * Quaternion.AngleAxis(signedRoll * leftTurn, axis);
            Quaternion rightRoll = (pulledLeft ? trail : sweep)
                * Quaternion.AngleAxis(signedRoll * rightTurn, axis);
            Rig.LUpperLeg.rotation = leftRoll * leftThigh;
            Rig.RUpperLeg.rotation = rightRoll * rightThigh;
            Rig.LLowerLeg.rotation = leftRoll * leftShin;
            Rig.RLowerLeg.rotation = rightRoll * rightShin;

            float lowest = Mathf.Min(Rig.Head.position.y - 0.085f, Rig.Chest.position.y - 0.09f,
                Rig.LFoot.position.y, Rig.RFoot.position.y);
            Rig.Hips.position += Vector3.up * Mathf.Max(0f, Stats.transform.position.y - lowest);

            // The trailing foot drags against the floor while the near knee rolls over.
            float trailingPlant = mount.RollPhase(0.38f, 0.60f) * (1f - mount.RollPhase(0.80f, 0.98f));
            float leadingPlant = mount.RollPhase(0.56f, 0.74f) * (1f - mount.RollPhase(0.83f, 0.98f));
            KeepRollFootLow(true, leftFootHeight, pulledLeft ? 0.42f * leadingPlant : 0.85f * trailingPlant);
            KeepRollFootLow(false, rightFootHeight, pulledLeft ? 0.85f * trailingPlant : 0.42f * leadingPlant);

            // --- 腕 ------------------------------------------------------
            // ロール中は「体の外側」と「床」を基準に手を置く。
            // ワールド座標へ手を固定すると、体が回るにつれて肩がその点へ近づき、
            // 肘が畳み切られる（実測: 肘 19°・手が肩の 17cm まで寄り、腕が一瞬
            // 折り畳まれて見える）。手先の距離を固定して解けば、その破綻は起きない。
            //
            // 入りは「いま体に付いてきた腕の位置」から始める。伏せダウン中のマウントは
            // ガードを出さない（`GroundMountAttack.Fear` はうつ伏せでは 0）ので、
            // 守りの手を入りの基準にすると 1 フレームで 40cm 飛ぶ。
            Vector3 carriedLeftHand = Rig.LHand.position;
            Vector3 carriedRightHand = Rig.RHand.position;
            Vector3 carriedLeftElbow = Rig.LLowerArm.position;
            Vector3 carriedRightElbow = Rig.RLowerArm.position;

            // 体の左右は「右肩 → 左肩」の向きをそのまま使う。ワールドの `across` と肩の位置で
            // 符号を決めると、体が 90° 回ったところで符号が反転し、手が体の反対側へ飛ぶ
            // （実測: 0.5 秒の時点で片手が 70cm 飛んだ）。肩の線はロールと一緒に回るので途切れない。
            Vector3 shoulderLine = Rig.LUpperArm.position - Rig.RUpperArm.position;
            Vector3 left = shoulderLine.sqrMagnitude > 1e-6f ? shoulderLine.normalized : Rig.Chest.right;
            float groundY = Stats.transform.position.y;

            // ロール明けは次の Windup の「守り」へ戻す（そちらはガードが出る）。
            // 戻さないと、ロール最後の 1 フレームからガードへ腕が飛ぶ。
            bool rightNearCamera = Vector3.Dot(-left, mount.ShotOffset) > 0f;
            float nearHand = rightNearCamera ? 1f : 0f;
            Vector3 guardRight = Rig.Head.position
                - left * (0.14f + 0.05f * nearHand)
                - axis * (0.085f + 0.05f * nearHand)
                + Vector3.up * (0.06f - 0.02f * nearHand);
            Vector3 guardRightPole = Rig.RUpperArm.position - left * 0.34f
                - axis * 0.24f + Vector3.up * 0.02f;
            Vector3 shoulderMid = (Rig.LUpperArm.position + Rig.RUpperArm.position) * 0.5f;
            Vector3 guardLeft = MirrorPoint(guardRight, shoulderMid, left);
            Vector3 guardLeftPole = MirrorPoint(guardRightPole, shoulderMid, left);

            // 掴まれた肩の腕は体側へ抱え、反対の腕は床へ逃がす。
            Vector3 pulledOut = pulledLeft ? left : -left;
            Vector3 freeOut = pulledLeft ? -left : left;
            Transform pulledShoulder = pulledLeft ? Rig.LUpperArm : Rig.RUpperArm;
            Transform freeShoulder = pulledLeft ? Rig.RUpperArm : Rig.LUpperArm;

            // 掴まれた肩の腕も、反対の腕も、ロール中は床の側へ垂らす。
            // 手先は「肩から固定距離」で置き、床へ潜る分だけ向きを上へ回す（距離は保つ）。
            // 向きには必ず「頭（+axis）」成分を入れる。体が真横を向くと体の外向きが
            // 真上・真下になり、床へ向ける水平の方位が定まらず手が大きく振れる
            // （実測: 0.5 秒で片手が 67cm 飛ぶ）。頭側成分があれば方位は常に決まる。
            Vector3 pulledHand = ArmGoalAboveFloor(pulledShoulder.position,
                Vector3.down * 0.55f + pulledOut * 0.25f + axis * 0.30f, 0.28f, groundY + 0.05f);
            Vector3 pulledPole = pulledShoulder.position + pulledOut * 0.25f
                + axis * 0.22f + Vector3.down * 0.08f;

            Vector3 freeHand = ArmGoalAboveFloor(freeShoulder.position,
                Vector3.down * 0.50f + freeOut * 0.30f + axis * 0.30f, 0.40f, groundY + 0.05f);
            Vector3 freePole = freeShoulder.position + freeOut * 0.25f
                + axis * 0.22f + Vector3.down * 0.08f;

            Vector3 leftRollHand = pulledLeft ? pulledHand : freeHand;
            Vector3 rightRollHand = pulledLeft ? freeHand : pulledHand;
            Vector3 leftRollPole = pulledLeft ? pulledPole : freePole;
            Vector3 rightRollPole = pulledLeft ? freePole : pulledPole;

            // 入りは「体に付いてきた手」から、終わりは次の Windup のガード位置へ。
            // 位置をそのまま Lerp すると肩の近くを横切って肘が畳まれる（実測: 肘 28°）ので、
            // 肩を中心に「向き」と「距離」を別々に補間する。
            float enter = mount.RollPhase(0.04f, 0.34f);
            float exit = mount.RollPhase(0.86f, 0.995f);

            BlendRollArm(Rig.LUpperArm, carriedLeftHand, carriedLeftElbow,
                leftRollHand, leftRollPole, guardLeft, guardLeftPole, enter, exit,
                out Vector3 leftGoal, out Vector3 leftPole);
            BlendRollArm(Rig.RUpperArm, carriedRightHand, carriedRightElbow,
                rightRollHand, rightRollPole, guardRight, guardRightPole, enter, exit,
                out Vector3 rightGoal, out Vector3 rightPole);

            AimRollHand(true, leftGoal, leftPole);
            AimRollHand(false, rightGoal, rightPole);
        }

        /// <summary>
        /// 腕の目標を「肩からの向き」と「距離」に分けて補間する。
        /// 手先の座標をそのまま Lerp すると、肩の近くを通る軌道で肘が畳み切られる。
        /// </summary>
        static void BlendRollArm(Transform shoulder, Vector3 carriedHand, Vector3 carriedElbow,
            Vector3 rollHand, Vector3 rollPole, Vector3 guardHand, Vector3 guardPole,
            float enter, float exit, out Vector3 goal, out Vector3 pole)
        {
            Vector3 origin = shoulder.position;
            Vector3 baseOffset = carriedHand - origin;
            Vector3 rollOffset = rollHand - origin;
            Vector3 guardOffset = guardHand - origin;

            float baseDistance = baseOffset.magnitude;
            float rollDistance = rollOffset.magnitude;
            float guardDistance = guardOffset.magnitude;

            Vector3 direction = rollDistance > 1e-4f ? rollOffset / rollDistance : Vector3.down;
            if (baseDistance > 1e-4f)
                direction = Vector3.Slerp(baseOffset / baseDistance, direction, enter);
            float distance = Mathf.Lerp(baseDistance, rollDistance, enter);

            if (guardDistance > 1e-4f)
                direction = Vector3.Slerp(direction, guardOffset / guardDistance, exit);
            distance = Mathf.Lerp(distance, guardDistance, exit);

            goal = origin + direction * distance;
            pole = Vector3.Lerp(Vector3.Lerp(carriedElbow, rollPole, enter), guardPole, exit);
        }

        /// <summary>点を、指定した平面（点と法線）で鏡映する。</summary>
        static Vector3 MirrorPoint(Vector3 point, Vector3 planePoint, Vector3 normal)
        {
            return point - 2f * Vector3.Dot(point - planePoint, normal) * normal;
        }

        /// <summary>
        /// 肩から <paramref name="reach"/> の距離を保ったまま、手先が床より下へ行かない
        /// 目標位置を作る。y をクランプすると肩の真下へ手が寄り、肘が畳み切られる
        /// （実測: 肘 14°）。向きだけを上へ回せば距離が変わらない。
        /// </summary>
        static Vector3 ArmGoalAboveFloor(Vector3 shoulder, Vector3 direction, float reach, float floorY)
        {
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.down;
            float minY = Mathf.Clamp((floorY - shoulder.y) / Mathf.Max(0.01f, reach), -1f, 1f);
            if (direction.y < minY)
            {
                float flatLength = Mathf.Sqrt(Mathf.Max(0f, 1f - minY * minY));
                Vector3 flat = new Vector3(direction.x, 0f, direction.z);
                direction = flat.sqrMagnitude > 1e-6f
                    ? flat.normalized * flatLength + Vector3.up * minY
                    : Vector3.up;
            }

            return shoulder + direction * reach;
        }

        /// <summary>
        /// ロール中の腕。手先の目標と肘の向きを直接指定して 2 ボーン IK で解く。
        /// 極（肘の曲がる面）は体の向きから作った点を渡す。ワールドの上を極にすると、
        /// 体が横を向くロール中は肘が床を抜ける向きに解けて腕が反転しうる。
        /// </summary>
        void AimRollHand(bool leftSide, Vector3 goal, Vector3 pole)
        {
            Transform shoulder = leftSide ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = leftSide ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = leftSide ? Rig.LHand : Rig.RHand;
            ProceduralRig.SolveTwoBone(shoulder, elbow, hand, goal, pole);
            Rig.SetArm(leftSide, elbow.position - shoulder.position,
                hand.position - elbow.position, 0f);
        }

        void KeepRollFootLow(bool left, float floorHeight, float weight)
        {
            Transform foot = left ? Rig.LFoot : Rig.RFoot;
            Vector3 target = foot.position;
            float maxHeight = floorHeight + 0.025f;
            if (weight <= 0.001f || target.y <= maxHeight)
                return;

            Transform knee = left ? Rig.LLowerLeg : Rig.RLowerLeg;
            Quaternion footRotation = foot.rotation;
            target.y = Mathf.Lerp(target.y, maxHeight, weight);
            Rig.SolveLeg(left, target, knee.position);
            foot.rotation = footRotation;
        }

        void BendMountKnee(bool left, Vector3 curl, Vector3 pole, float weight)
        {
            Transform thigh = left ? Rig.LUpperLeg : Rig.RUpperLeg;
            Transform shin = left ? Rig.LLowerLeg : Rig.RLowerLeg;
            Transform foot = left ? Rig.LFoot : Rig.RFoot;
            Quaternion thighBefore = thigh.localRotation;
            Quaternion shinBefore = shin.localRotation;
            Quaternion footBefore = foot.rotation;
            Rig.SolveLeg(left, foot.position + curl, pole);
            thigh.localRotation = Quaternion.Slerp(thighBefore, thigh.localRotation, weight);
            shin.localRotation = Quaternion.Slerp(shinBefore, shin.localRotation, weight);
            foot.rotation = footBefore;
        }

        void PoseMountRightArm(Vector3 outside, Vector3 towardHead, Vector3 guardOffset, float weight,
            float brace, float recoil, bool nearCamera)
        {
            float nearHand = nearCamera ? 1f : 0f;
            // 手は頭の上ではなく、耳の横（顔の高さ）へ。頭中心 +15cm だと腕が頭上へ伸び、
            // マウントされたときに「万歳」して見える。
            Vector3 goal = Rig.RUpperArm.position + guardOffset + outside * (0.14f + 0.05f * nearHand)
                + Vector3.up * (0.06f - 0.02f * nearHand + 0.045f * brace)
                - towardHead * (0.085f + 0.05f * nearHand - 0.075f * brace);
            AimMountHand(false, outside, towardHead, goal, weight);

            // Keep the guard's elbow bend through the impact. Moving the IK target across
            // the elbow guide can put the forearm on the other side of the bend in one frame.
            float give = recoil * weight;
            if (give <= 0.001f)
                return;

            Transform shoulder = Rig.RUpperArm;
            Transform elbow = Rig.RLowerArm;
            Transform hand = Rig.RHand;
            Vector3 reach = hand.position - shoulder.position;
            Vector3 fall = (outside * 0.12f - towardHead * 0.15f - Vector3.up * 0.10f) * give;
            shoulder.rotation = Quaternion.FromToRotation(reach, reach + fall) * shoulder.rotation;

            Vector3 upper = elbow.position - shoulder.position;
            Vector3 lower = hand.position - elbow.position;
            Vector3 hinge = Vector3.Cross(upper, lower);
            if (hinge.sqrMagnitude > 0.000001f)
            {
                float open = Mathf.Clamp(Vector3.Angle(upper, lower) - 12f, 0f, 14f * give);
                elbow.rotation = Quaternion.AngleAxis(-open, hinge) * elbow.rotation;
            }
        }

        void AimMountHand(bool left, Vector3 outside, Vector3 towardHead, Vector3 goal, float weight)
        {
            Transform shoulder = left ? Rig.LUpperArm : Rig.RUpperArm;
            Transform elbow = left ? Rig.LLowerArm : Rig.RLowerArm;
            Transform hand = left ? Rig.LHand : Rig.RHand;
            // 彼女は床に寝ている。極を床の下（-up）に置くと、肘が床を突き抜ける向きに解けて
            // 腕が反転する（内側へ手を寄せる左腕で特に出やすい）。肘は横へ出し、床すれすれに置く。
            Vector3 pole = shoulder.position + outside * 0.34f - towardHead * 0.24f + Vector3.up * 0.02f;
            ProceduralRig.SolveTwoBone(shoulder, elbow, hand, Vector3.Lerp(hand.position, goal, weight), pole);
            Rig.SetArm(left, elbow.position - shoulder.position, hand.position - elbow.position, 0f);
        }

    }
}
