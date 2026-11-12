using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        void Step(float dt, bool instant)
        {
            if (!instant && dt <= 0f)
                return;

            TickMotion(dt, instant);

            float speed = SpeedNow;
            EnemyPhase phase = PhaseNow;
            // 倒れの進み具合は Dead のときだけ進める（他の状態では 0）。
            if (phase != EnemyPhase.Dead)
                _deadProgress = 0f;
            if (!instant && phase != _lastPhase)
            {
                // 攻撃の瞬間は 1 フレームしかないので、振り抜きはここから自分で再生する
                if (phase == EnemyPhase.Recover && _lastPhase == EnemyPhase.Attack)
                    _swingTime = 0.20f;
                _lastPhase = phase;
            }
            if (instant)
                _swingTime = phase == EnemyPhase.Recover ? _previewSwing : -1f;
            else if (phase != EnemyPhase.Recover)
                _swingTime = -1f;
            else if (_swingTime >= 0f)
                _swingTime += dt;
            // --- 目標姿勢を組み立てる ---------------------------------

            // 掴まれている最中か（姿勢の分岐と踏み込みの両方が見る）。
            var holdState = Brain != null && Brain.Player != null
                ? Brain.Player.GetComponent<PlayerGrapple>()
                : null;

            var pose = BuildTargetPose(dt, instant, phase, speed, holdState);
            ApplyPoseTarget(pose, dt, instant, phase, holdState);
        }

        /// <summary>
        /// 今の状態から「目標の姿勢」を組み立てる。
        /// ここでは FK/IK を触らず、数値だけを決める（適用は <see cref="ApplyPoseTarget"/>）。
        /// </summary>
        PoseTarget BuildTargetPose(float dt, bool instant, EnemyPhase phase, float speed, PlayerGrapple holdState)
        {
            float restY = Height * 0.515f;
            var pose = new PoseTarget
            {
                HipsY = restY,
                ArmRPitch = -8f,
                ArmLPitch = -8f,
                ArmRSpread = -8f,
                ArmLSpread = 8f,
                ElbowR = 22f,
                ElbowL = 22f,
                KneeR = 10f,
                KneeL = 10f,
            };
            switch (phase)
            {
                case EnemyPhase.Chase:
                case EnemyPhase.Strafe:
                {
                    float amp = Mathf.Clamp01(speed / 1.2f);
                    // 一歩で踏み出す幅。速いほど大きく、届く範囲まで
                    float reach = WalkReach(speed);
                    // 横歩きは歩幅が短い。前と同じ幅で横へ運ぶと、引き足が軸足を
                    // 追い越して足が交差してしまう。横成分が増えるほど詰める。
                    Vector2 dir = _walkDir;
                    float latness = Mathf.Abs(dir.x);
                    float fwdness = Mathf.Abs(dir.y);
                    float reachEff = Mathf.Lerp(reach, reach * 0.55f, latness);
                    float cycle = 2f * reachEff / WalkDuty;
          // 1往復（2歩）で進む距離
                    float u = cycle > 0.01f ? Mathf.Repeat(_walkDist / cycle, 1f) : 0f;
                    // 止まったときに遊脚が宙に浮いたままにならないよう、着地まで進める
                    if (speed < 0.15f && cycle > 0.01f && u > WalkDuty)
                    {
                        _walkDist += 0.35f * dt;
                        u = Mathf.Repeat(_walkDist / cycle, 1f);
                    }
                    // 距離を溜め続けると精度が落ちるので、1周期分に丸めておく
                    if (cycle > 0.01f)
                        _walkDist = Mathf.Repeat(_walkDist, cycle);
                    WalkPhase = u;
                    // 腰は軽く落とす。脚を伸ばしきらずに済むので、踏み出しても届く
                    float hipBase = Height * 0.455f + 0.008f * Mathf.Cos(4f * Mathf.PI * u);
                    pose.HipsY = hipBase + Height * 0.030f;
                    // 足首の目標。進行方向（横歩きなら横）へ運ぶ
                    FootPath(u, reachEff, WalkDuty, out float sweepR, out float liftR);
                    FootPath(Mathf.Repeat(u + 0.5f, 1f), reachEff, WalkDuty, out float sweepL, out float liftL);
                    // 体重を軸足へ預ける。これが無いと足だけが横へ滑って「跳び箱」に見える。
                    // 軸足は接地中の側なので、右足が着地する u=0 から 0.30 あたりが右、
                    // 0.80 あたりが左。
                    pose.HipsX = Mathf.Cos(2f * Mathf.PI * (u - 0.30f)) * Height * 0.016f * amp;
                    // 進行方向へ上体を傾ける。横歩きは横へ、後退りは傾けない
                    pose.TorsoRoll = -dir.x * 4.5f * amp;
                    _legTargetR = new Vector3(dir.x * sweepR, liftR - hipBase, dir.y * sweepR);
                    _legTargetL = new Vector3(dir.x * sweepL, liftL - hipBase, dir.y * sweepL);
                    // 接地中の足は地面に固定したいので、脚の角度は補間せずそのまま使う
                    pose.LegsExact = true;
                    float s = -Mathf.Cos(2f * Mathf.PI * u);
                    // 横歩きでは前後の腕振りを減らし、代わりに腕を横へ小さく振る。
                    // 前後だけだと、横へ動いているのに「その場足踏み」に見える。
                    pose.ArmRPitch = -s * 22f * amp * fwdness;
                    pose.ArmLPitch = s * 22f * amp * fwdness;
                    pose.ArmRSpread = -8f - s * 6f * amp * latness;
                    pose.ArmLSpread = 8f - s * 6f * amp * latness;
                    pose.ElbowR = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    pose.ElbowL = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    pose.TorsoPitch = 5f * amp * (dir.y >= 0f ? dir.y : dir.y * 0.45f);
                    pose.TorsoYaw = s * 4f * amp;
                    // 止まっているときは腕を下ろしたままにせず、拳（武器）を軽く構える。
                    // 歩いている最中に混ぜると腕振りが消えるので、速度が落ちた分だけ効かせる。
                    float guard = Mathf.Clamp01(1f - amp * 2f);
                    if (guard > 0f)
                    {
                        pose.ArmRPitch += -20f * guard;
                        pose.ElbowR += 30f * guard;
                        pose.ArmLPitch += -13f * guard;
                        pose.ElbowL += 24f * guard;
                        pose.TorsoPitch += 3f * guard;
                        pose.TorsoYaw += -6f * guard;      // 半身になって相手の正面を外す
                        // 立ち止まっている間も呼吸と重心移動を入れる。
                        // 完全に静止すると「置物」に見え、こちらを狙っている感じが消える。
                        // 個体ごとに位相をずらして、3 体が同時に同じ呼吸をしないようにする。
                        if (_idlePhase < 0f)
                            _idlePhase = Random.value * 6.283f;
                        float idle = Time.time * 1.6f + _idlePhase;
                        float breath = Mathf.Sin(idle) * guard;
                        pose.TorsoPitch += breath * 0.9f;
                        pose.TorsoYaw += Mathf.Sin(idle * 0.37f) * 1.6f * guard;
                        pose.HipsY += breath * 0.004f;
                        pose.ArmRPitch += breath * 1.2f;
                        pose.ArmLPitch += breath * 1.2f;
                        pose.ElbowR += breath * 1.8f;
                        pose.ElbowL += breath * 1.8f;
                    }
                    break;
                }
                case EnemyPhase.Windup:
                {
                    float t = WindupNow;
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    // 引きは素早く見せ、引き切ったら姿勢を止めて溜める。
                    // 溜めの間ずっと動き続けると「いつ振るか」が読めないので、止める時間を作る。
                    const float pull = 0.55f;
                    float x = Mathf.Clamp01(t / pull);
                    float k = 1f - (1f - x) * (1f - x);
                    // 拳は腰の後ろへ。肘を曲げたまま高く上げると「ガード」に見えるので、
                    // 腕ごと後ろへ引いて「振りかぶっている」形にする。
                    pose.ArmRPitch = Mathf.Lerp(-8f, CockArm(attack), k);
                    pose.ElbowR = Mathf.Lerp(22f, CockElbow(attack), k);
                    // 振りかぶるほど肩を開く。前後（ピッチ）だけで振ると「蝶番」に見え、
                    // 肩が入っていない拳になる。開きは振り切る直前に戻す。
                    pose.ArmRSpread = Mathf.Lerp(-8f, -21f, k);
                    pose.ArmLSpread = Mathf.Lerp(8f, breakAttack ? 17f : 11f, k);
                    // 引き切ったあとも上体だけ僅かにひねり続ける（固まって見えないように）
                    float creep = Mathf.Clamp01((t - pull) / Mathf.Max(0.01f, 1f - pull));
                    pose.TorsoYaw = -Mathf.Lerp(0f, CockYaw(attack), k) - 3f * creep;
                    pose.TorsoPitch = Mathf.Lerp(4f, CockPitch(attack), k);
                    pose.HipsY = restY - 0.05f * k;
                    // 振りかぶるほど体重を後ろへ預ける（振り始めの踏み込みが効く）
                    pose.HipsZ = -0.035f * k;
                    // 崩しは両手を引く（見てから避けにくい代わりに、両腕が動く）
                    if (breakAttack)
                    {
                        pose.ArmLPitch = Mathf.Lerp(-8f, 58f, k);
                        pose.ElbowL = Mathf.Lerp(22f, 46f, k);
                    }
                    else
                    {
                        pose.ArmLPitch = Mathf.Lerp(-8f, -26f, k);
                        pose.ElbowL = Mathf.Lerp(22f, 70f, k);
                    }
                    pose.LegRPitch = Mathf.Lerp(0f, 8f, k);
                    pose.LegLPitch = Mathf.Lerp(0f, -10f, k);
                    pose.KneeR = Mathf.Lerp(10f, 24f, k);
                    pose.KneeL = Mathf.Lerp(10f, 26f, k);
                    // 振り切る直前。予備動作の最後で腕を打ち込み始める。
                    // ここを入れないと「当たった瞬間はまだ振りかぶったまま」に見える。
                    float release = Mathf.Clamp01((t - 0.88f) / 0.12f);
                    if (release > 0f)
                    {
                        pose.ArmRPitch = Mathf.Lerp(pose.ArmRPitch, StrikeArm(attack), release);
                        pose.ElbowR = Mathf.Lerp(pose.ElbowR, StrikeElbow(attack), release);
                        pose.ArmRSpread = Mathf.Lerp(pose.ArmRSpread, -12f, release);
                        pose.ArmLSpread = Mathf.Lerp(pose.ArmLSpread, breakAttack ? 9f : 7f, release);
                        pose.TorsoYaw = Mathf.Lerp(pose.TorsoYaw, StrikeYaw(attack), release);
                        pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, StrikePitch(attack), release);
                        pose.HipsZ = Mathf.Lerp(pose.HipsZ, StrikeHips(attack), release);
                        pose.HeadPitch = Mathf.Lerp(pose.HeadPitch, 7f, release);
                    }
                    break;
                }
                case EnemyPhase.Attack:
                {
                    // 判定のフレーム。ここが「当たった瞬間」なので、腕を伸ばし切った形を出す。
                    // 以前は case が無く、当たりの 1 フレームだけ立ち姿に戻ってしまっていた。
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    pose.ArmRPitch = StrikeArm(attack);
                    pose.ElbowR = StrikeElbow(attack);
                    pose.ArmRSpread = -12f;
                    pose.ArmLSpread = breakAttack ? 9f : 7f;
                    pose.TorsoYaw = StrikeYaw(attack);
                    pose.TorsoPitch = StrikePitch(attack);
                    pose.HeadPitch = 7f;
                    pose.HipsY = restY - 0.10f;
                    pose.HipsZ = StrikeHips(attack);
                    if (breakAttack)
                    {
                        pose.ArmLPitch = -70f;
                        pose.ElbowL = 10f;
                    }
                    else
                    {
                        pose.ArmLPitch = -34f;
                        pose.ElbowL = 86f;
                    }
                    pose.LegRPitch = -6f;
                    pose.LegLPitch = 12f;
                    pose.KneeR = 14f;
                    pose.KneeL = 12f;
                    break;
                }
                case EnemyPhase.Recover:
                {
                    // 振り抜き。0〜1 で伸びきり、その後ゆっくり戻す。
                    // 速く振って長く止める（0.055 で振り切り、0.24 まで伸びたまま）と
                    // 「重いものを振った」感触が出る。
                    float s = 0f;
                    if (_swingTime >= 0f)
                    {
                        float t = _swingTime;
                        if (Brain != null && Brain.IsTagPunch && Brain.Zone == PunchZone.Body)
                            s = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.20f) / 0.17f);
                        else if (t < 0.055f)
                            s = Mathf.SmoothStep(0f, 1f, t / 0.055f);
                        else if (t < 0.24f)
                            s = 1f;
                        else
                            s = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.24f) / 0.18f);
                    }
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    // 予備動作の終わりと同じ姿勢から振り始める（切り替わりで飛ばないように）
                    pose.ArmRPitch = Mathf.Lerp(CockArm(attack), StrikeArm(attack), s);
                    pose.ElbowR = Mathf.Lerp(CockElbow(attack), StrikeElbow(attack), s);
                    pose.TorsoYaw = Mathf.Lerp(-CockYaw(attack), StrikeYaw(attack), s);
                    pose.TorsoPitch = Mathf.Lerp(CockPitch(attack), StrikePitch(attack), s);
                    pose.HipsY = restY - Mathf.Lerp(0.05f, 0.10f, s);
                    pose.HeadPitch = Mathf.Lerp(0f, 7f, s);
                    // 振り抜きに合わせて踏み込む（腰ごと前へ出ると打撃の重さが出る）
                    pose.HipsZ = Mathf.Lerp(-0.02f, StrikeHips(attack), s);
                    if (breakAttack)
                    {
                        pose.ArmLPitch = Mathf.Lerp(58f, -70f, s);
                        pose.ElbowL = Mathf.Lerp(46f, 10f, s);
                    }
                    else
                    {
                        pose.ArmLPitch = Mathf.Lerp(-26f, -34f, s);
                        pose.ElbowL = Mathf.Lerp(70f, 86f, s);
                    }
                    pose.LegRPitch = Mathf.Lerp(8f, -6f, s);
                    pose.LegLPitch = Mathf.Lerp(-10f, 12f, s);
                    pose.KneeR = Mathf.Lerp(24f, 14f, s);
                    pose.KneeL = Mathf.Lerp(26f, 12f, s);
                    break;
                }
                case EnemyPhase.Stagger:
                {
                    // 一定の姿勢を出すと「止め絵」に見えるので、殴られた瞬間を山として減衰させる。
                    float e;
                    float w;
                    float heaviness;
                    if (_previewing && _previewStaggerTime >= 0f)
                    {
                        e = _previewStaggerTime;
                        w = Mathf.Clamp01(1f - e / 0.32f);
                        // シートは「重い一撃」を見たいので、こらえる2段目まで出す
                        heaviness = 1.8f;
                    }
                    else
                    {
                        e = _previewing ? 0f : (Brain != null ? Brain.StaggerElapsed : 0f);
                        w = _previewing ? 1f : (Brain != null ? Brain.StaggerProgress : 1f);
                        heaviness = _previewing ? 1.4f : (Brain != null ? Brain.StaggerWeight : 1.4f);
                    }
                    w = Mathf.Clamp01(w);
                    // 頭と腕は体より 50〜90ms 遅れて付いてくる。
                    // 全部が同じフレームで振り切れると「棒が倒れた」ように見える。
                    float lagHead = Mathf.Clamp01((e - 0.05f) / 0.05f);
                    float lagArm = Mathf.Clamp01((e - 0.08f) / 0.09f);
                    float back = 31f * w * heaviness;
                    float settle = Mathf.Sin(e * 16f) * Mathf.Exp(-e * 7f);
                    // 上体は真後ろへ反るだけにせず、殴られた側へひねる。
                    // ひねりが無いと「自分で反った」ようにしか見えない。
                    pose.TorsoPitch = 4f - back - 4f * settle;
                    pose.TorsoYaw = (26f * w + 7f * settle) * heaviness * 0.8f;
                    // 首は体のあとから落ち、戻りで一度だけ前に残る。
                    pose.HeadPitch = 2f - back * 0.72f * lagHead + 4f * settle;
                    // 腕は振り遅れて外へ流れる。殴られた側（右）ほど大きく。
                    pose.ArmRPitch = 24f + 34f * w * lagArm * heaviness * 0.7f;
                    pose.ArmLPitch = 24f + 16f * w * lagArm;
                    pose.ElbowR = 46f - 18f * w;
                    pose.ElbowL = 40f - 8f * w;
                    // 膝が抜けて腰が落ち、同時に後ろへ押される。
                    pose.HipsY = restY - 0.03f - 0.06f * w * heaviness;
                    pose.HipsZ = -0.03f - 0.12f * w * heaviness;
                    // 前の足が突っ張り、後ろの足が引いて体重を逃がす。
                    pose.LegLPitch = -11f * w;
                    pose.LegRPitch = 13f * w;
                    pose.KneeL = 10f + 8f * w;
                    pose.KneeR = 10f + 20f * w * heaviness * 0.7f;
                    // 重い一撃は「のけぞる → こらえる」の2段にする。
                    // 反り切ったあと前へ折れて腹を抱えるので、単調な往復に見えない。
                    // （小さい一撃でこれをやると、いちいち痛がりすぎて戦闘が止まる）
                    if (heaviness >= 1.6f)
                    {
                        float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - 0.14f) / 0.14f))
                            * Mathf.Clamp01(1f - (e - 0.34f) / 0.26f);
                        pose.TorsoPitch += 24f * fold;
                        pose.TorsoYaw += -10f * fold;
                        pose.HeadPitch += 8f * fold;       // 上体が折れた分、顔も落ちる
                        pose.HipsY -= 0.05f * fold;
                        pose.KneeR += 12f * fold;
                        // 腕は前へ伸ばさず、腹の前へ引き寄せる（伸ばすと「掴もうとしている」に見える）
                        pose.ArmRPitch += -12f * fold;
                        pose.ArmLPitch += -10f * fold;
                        pose.ElbowR += 8f * fold;
                        pose.ElbowL += 6f * fold;
                    }
                    break;
                }
                case EnemyPhase.Dead:
                {
                    // 倒れる進み具合。腰・膝・腕を「崩れる順」に動かすために持つ。
                    _deadProgress = instant
                        ? 1f
                        : Mathf.Clamp01(_deadProgress + dt / 0.5f);
                    float p = Mathf.SmoothStep(0f, 1f, _deadProgress);

                    // 膝が抜ける。途中で一番深く畳み、寝たら伸びる。
                    // 脚を伸ばしたまま根元を回すと、脚が床を突き抜けて
                    // 「板が倒れる」ように見える（実測でそうなっていた）。
                    float buckle = Mathf.Sin(_deadProgress * Mathf.PI);

                    pose.Fall = -84f;
                    pose.HipsY = Mathf.Lerp(restY, restY * 0.35f, p);
                    pose.ArmRPitch = 12f - 26f * buckle;
                    pose.ArmLPitch = 12f - 24f * buckle;
                    pose.ElbowR = 16f + 26f * buckle;
                    pose.ElbowL = 16f + 24f * buckle;
                    pose.LegRPitch = -8f - 14f * buckle;
                    pose.LegLPitch = -8f - 12f * buckle;
                    pose.KneeR = 12f + 66f * buckle;
                    pose.KneeL = 12f + 60f * buckle;
                    break;
                }
            }
            // --- 掴み: 両腕を前へ伸ばして掴みかかる ----------------------
            if (AttackNow == AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))
            {
                if (phase == EnemyPhase.Windup)
                {
                    // 予備動作: まず両腕を横へ大きく開く（武器を下ろした合図）。
                    // いきなり前へ出すと、突きの構えと見分けが付きにくい。
                    // 開き切ってから掴む直前に前へ寄せる、の2段にする。
                    float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(WindupNow / 0.62f));
                    float close = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((WindupNow - 0.72f) / 0.28f));

                    // 腕は前後（ピッチ）でしか振れないリグなので「横に開く」は使えない。
                    // 掴みは「両腕を低く前へ出して腰を落とす」で読ませる。
                    pose.ArmRPitch = Mathf.Lerp(-8f, -30f, open);
                    pose.ArmLPitch = Mathf.Lerp(-8f, -30f, open);
                    pose.ElbowR = Mathf.Lerp(22f, 58f, open);
                    pose.ElbowL = Mathf.Lerp(22f, 58f, open);
                    pose.ArmRSpread = Mathf.Lerp(-8f, -14f, open);
                    pose.ArmLSpread = Mathf.Lerp(8f, 14f, open);

                    pose.ArmRPitch = Mathf.Lerp(pose.ArmRPitch, -76f, close);
                    pose.ArmLPitch = Mathf.Lerp(pose.ArmLPitch, -72f, close);
                    pose.ElbowR = Mathf.Lerp(pose.ElbowR, 18f, close);
                    pose.ElbowL = Mathf.Lerp(pose.ElbowL, 24f, close);
                    pose.ArmRSpread = Mathf.Lerp(pose.ArmRSpread, -14f, close);
                    pose.ArmLSpread = Mathf.Lerp(pose.ArmLSpread, 14f, close);

                    pose.TorsoPitch = Mathf.Lerp(4f * open, -18f, close);
                    pose.TorsoYaw = 0f;
                    pose.HipsY = restY - 0.045f * open - 0.055f * close;
                    pose.HipsZ = Mathf.Lerp(-0.035f * open, 0.08f, close);
                    pose.KneeR = Mathf.Lerp(10f, 26f, open);
                    pose.KneeL = Mathf.Lerp(10f, 30f, open);
                    pose.LegRPitch = 0f;
                    pose.LegLPitch = 0f;
                }
                else
                {
                    // 掴んだ後: 腕を伸ばしたまま。掴めていなければ引く。
                    bool holding = _previewing || (Brain != null && Brain.IsHolding);
                    float retract = holding
                        ? 0f
                        : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_swingTime / 0.22f));

                    // 掴んだ後: 上体を前へ倒し、腕を下ろして彼女の肩・上腕に手を乗せる。
                    // 彼（肩 1.45m）と彼女（構えで肩 1.15m）は 30cm 差があるので、
                    // 水平に伸ばすと手が頭の高さで止まる。前傾＋腕を下ろすで合わせる。
                    pose.ArmRPitch = Mathf.Lerp(-76f, -8f, retract);
                    pose.ArmLPitch = Mathf.Lerp(-74f, -8f, retract);
                    pose.ElbowR = Mathf.Lerp(14f, 22f, retract);
                    pose.ElbowL = Mathf.Lerp(18f, 22f, retract);
                    pose.ArmRSpread = Mathf.Lerp(-14f, -8f, retract);
                    pose.ArmLSpread = Mathf.Lerp(14f, 8f, retract);
                    pose.TorsoPitch = Mathf.Lerp(-12f, 4f, retract);
                    pose.TorsoYaw = 0f;
                    pose.HeadPitch = Mathf.Lerp(-12f, 2f, retract);   // 掴んでいる間は相手を見下ろす（正=上向き）
                    pose.HipsY = restY - Mathf.Lerp(0.07f, 0.03f, retract);
                    pose.HipsZ = Mathf.Lerp(0.06f, 0f, retract);
                    pose.KneeR = Mathf.Lerp(26f, 10f, retract);
                    pose.KneeL = Mathf.Lerp(22f, 10f, retract);
                    pose.LegRPitch = 0f;
                    pose.LegLPitch = 0f;
                    var hold = Brain != null && Brain.Player != null
                        ? Brain.Player.GetComponent<PlayerGrapple>() : null;
                    if (holding && hold != null && hold.Kind == GrappleKind.Back)
                    {
                        pose.ArmRPitch = -78f;
                        pose.ArmLPitch = -78f;
                        pose.ElbowR = 28f;
                        pose.ElbowL = 28f;
                        pose.ArmRSpread = -28f;
                        pose.ArmLSpread = 28f;
                        // 彼女の肩の横へ覆いかぶさる形（参考: マネキン写真の「拘束＋腹パン」）。
                        // 上体はわずかに前へ、頭を伏せて彼女の頭の横へ置く。前傾を深くすると
                        // 頭が真後ろへ入って髪と重なるので、腰を落として高さと間隔を稼ぐ
                        // （実測: 頭中心の距離 0.29m。前傾 -16° の旧姿勢は 0.19m で重なった）。
                        pose.TorsoPitch = -2f;
                        pose.HipsY = restY - 0.17f;
                        // 前後に足を割って踏ん張る（両足を揃えて沈むと「座っている」画になる）。
                        // 彼女に近い足を前へ、反対の足を後ろへ引く。
                        pose.KneeR = 62f;
                        pose.KneeL = 40f;
                        pose.LegRPitch = -16f;
                        pose.LegLPitch = 14f;
                        pose.HeadPitch = -22f;
                    }
                }

                if (Brain != null && Brain.IsPreparingGrab)
                {
                    float step = Mathf.InverseLerp(0.44f, 0.90f, WindupNow);
                    float advance = Brain.GrabAdvance;
                    float floor = Height * 0.030f - pose.HipsY;
                    float forward = Mathf.SmoothStep(0f, 0.21f, step);
                    _legTargetR = new Vector3(0f, floor + 0.085f * Mathf.Sin(Mathf.PI * step), forward - pose.HipsZ);
                    _legTargetL = new Vector3(0f, floor, -0.07f - Mathf.Min(advance, 0.30f) - pose.HipsZ);
                    pose.LegsExact = true;
                }
            }
            if (Brain != null && Brain.HeldThrowPull > 0f)
            {
                float pull = Brain.HeldThrowPull;
                pose.TorsoPitch += 18f * pull;
                pose.HipsY -= 0.08f * pull;
                pose.KneeR += 16f * pull;
                pose.KneeL += 10f * pull;
            }

            // ボディーブロー: 腹へ届くよう、腰を落として上体を前へ倒す。
            // 顔を殴るときは立ったまま、腹を殴るときは沈む。構えを見れば狙いが読める。
            if (Brain != null && Brain.Zone == PunchZone.Body && AttackNow != AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))
            {
                float bw = Brain.PunchPoseWeight;
                if (Brain.IsBellyPunch)
                {
                    // 先に膝を曲げて拳を脇に溜め、前足に体重を移しながら腰を返す。
                    float bodyDrive = Brain.BellyPunchDrive;
                    pose.TorsoYaw = Mathf.Lerp(24f, -31f, bodyDrive) * bw;
                    pose.TorsoPitch = Mathf.Lerp(pose.TorsoPitch, Mathf.Lerp(-14f, -33f, bodyDrive), bw);
                    pose.HipsY = Mathf.Lerp(pose.HipsY, restY - Mathf.Lerp(0.27f, 0.23f, bodyDrive), bw);
                    pose.HipsZ = Mathf.Lerp(pose.HipsZ, 0.11f * bodyDrive, bw);
                    pose.KneeR = Mathf.Lerp(pose.KneeR, Mathf.Lerp(48f, 67f, bodyDrive), bw);
                    pose.KneeL = Mathf.Lerp(pose.KneeL, Mathf.Lerp(61f, 34f, bodyDrive), bw);
                    pose.LegRPitch = Mathf.Lerp(pose.LegRPitch, Mathf.Lerp(-20f, -29f, bodyDrive), bw);
                    pose.LegLPitch = Mathf.Lerp(pose.LegLPitch, Mathf.Lerp(-4f, 18f, bodyDrive), bw);
                }
                else
                {
                    pose.TorsoPitch -= 16f * bw;
                    pose.HipsY -= 0.10f * bw;
                    pose.KneeR += 14f * bw;
                    pose.KneeL += 14f * bw;
                }
            }
            // 拳を引いても次の打撃までは腰を上げない。顔を打つ瞬間だけ必要な高さを戻す。
            if (Brain != null && Brain.HoldsBellyPunchGuard &&
                (phase == EnemyPhase.Recover || phase == EnemyPhase.Chase || phase == EnemyPhase.Strafe ||
                 (Brain.IsTagPunch && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack))))
            {
                float guard = Brain.IsTagPunch && Brain.Zone == PunchZone.Face ? 1f - Brain.PunchPoseWeight : 1f;
                float lowHips = Mathf.Min(pose.HipsY, restY - 0.20f * guard);
                if (pose.LegsExact)
                {
                    _legTargetR.y += pose.HipsY - lowHips;
                    _legTargetL.y += pose.HipsY - lowHips;
                }
                pose.HipsY = lowHips;
                pose.TorsoPitch = Mathf.Min(pose.TorsoPitch, -13f * guard);
                pose.KneeR = Mathf.Max(pose.KneeR, 35f * guard);
                pose.KneeL = Mathf.Max(pose.KneeL, 23f * guard);
            }
            // タッグ技（腹 / 顔）は片手で打つ。通常のジャブは左前腕を胸の前へ水平に構えるので、
            // 腹パンの体勢（上体を前へ倒す）と重なると「両手を前に出して押している」画になる。
            // 腹パンの打たない腕は腰の脇へ引く。顔は奥側の腕で打つ。
            if (Brain != null && (Brain.KeepsBellyPunchArm ||
                (Brain.IsTagPunch && AttackNow != AttackId.Grab
                 && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover))))
            {
                if (Brain.IsTagFacePunch)
                {
                    float side = Brain.UsesLeftPunch ? -1f : 1f;
                    // 決めの一発（5 発目）は踏み込みを大きくして、連打の締めに見せる。
                    float power = Brain.TagFacePower;
                    float faceCoil = Brain.TagFaceCoil * power;
                    float faceDrive = Brain.TagFaceDrive * power;

                    // 腰 → 胸 → 肩の順にひねる。溜めは拳側へ、振り抜きは逆へ回す。
                    // 上体も前へ入れて、腕だけでなく体重が拳に乗っていることを見せる。
                    pose.TorsoYaw = side * (24f * faceCoil - 34f * faceDrive);
                    pose.TorsoPitch = -5f * faceCoil - 16f * faceDrive;
                    pose.TorsoRoll = side * (-4f * faceCoil + 8f * faceDrive);

                    // 溜めで後ろへ預けた体重を、踏み込みで拳へ乗せる。
                    // 決めの一発でも腰を出しすぎない（出しすぎると拳が彼女の顔に近づきすぎ、
                    // 肘が折れて「押している」画になる）。強さは上体のひねりと反りに乗せる。
                    float bodyDrive = Mathf.Min(faceDrive, 1.05f);
                    pose.HipsZ = 0.18f * bodyDrive - 0.05f * faceCoil;
                    pose.HipsY = restY - 0.06f * bodyDrive + 0.008f * faceCoil;

                    // 拳側（後ろ足）で踏ん張り、前足が体重を受けて曲がる。
                    bool punchRight = side > 0f;
                    pose.LegRPitch = punchRight ? (-3f - 12f * faceDrive) : (8f + 4f * faceDrive);
                    pose.LegLPitch = punchRight ? (8f + 4f * faceDrive) : (-3f - 12f * faceDrive);
                    pose.KneeR = punchRight
                        ? (16f + 6f * faceCoil - 8f * faceDrive)
                        : (16f + 14f * faceDrive);
                    pose.KneeL = punchRight
                        ? (16f + 14f * faceDrive)
                        : (16f + 6f * faceCoil - 8f * faceDrive);

                    // 打つ拳は頬の横へ引いて構える（振り抜きは BackHoldPose が実際の腕を解く）。
                    // 反対の腕は顎の前に構え、振り抜きで引いて反動をつくる。
                    // 構えの拳は体の中心ではなく「自分の頬の外側」に置く（戻すときに
                    // 彼女の顔の前を横切らない）。
                    float chamber = -30f - 12f * faceDrive;
                    if (punchRight)
                    {
                        pose.ArmRPitch = chamber;
                        pose.ElbowR = 132f;
                        pose.ArmRSpread = -18f;
                        pose.ArmLPitch = -22f - 14f * faceDrive;
                        pose.ElbowL = 128f;
                        pose.ArmLSpread = 10f;
                    }
                    else
                    {
                        pose.ArmLPitch = chamber;
                        pose.ElbowL = 132f;
                        pose.ArmLSpread = 18f;
                        pose.ArmRPitch = -22f - 14f * faceDrive;
                        pose.ElbowR = 128f;
                        pose.ArmRSpread = -10f;
                    }
                }
                else
                {
                    pose.ArmLPitch = _tagArmL;
                    pose.ElbowL = _tagElbowL;
                    pose.ArmLSpread = _tagSpreadL;
                    if (Brain.IsBellyPunch || Brain.KeepsBellyPunchArm)
                    {
                        // 右拳を出すときは左肩を引き、脇の手で上体のひねりを受ける。
                        float counter = Brain.IsBellyPunch ? Brain.BellyPunchDrive * Brain.PunchPoseWeight : 0f;
                        pose.ArmLPitch += Mathf.Lerp(-12f, 18f, counter);
                        pose.ElbowL += Mathf.Lerp(16f, -15f, counter);
                        pose.ArmLSpread += Mathf.Lerp(-4f, 8f, counter);
                        pose.ShoulderLOffset = new Vector3(0f, 0.014f * counter, Mathf.Lerp(0.012f, -0.052f, counter));
                    }
                }
            }
            // 検証用の上書き（エディタで姿勢を撮り比べる）。実行中は null なので影響しない。
            if (ForceLeftArm.HasValue)            {
                pose.ArmLPitch = ForceLeftArm.Value.x;
                pose.ElbowL = ForceLeftArm.Value.y;
                pose.ArmLSpread = ForceLeftArm.Value.z;
            }
            // 腰を落として手を届かせ、前足に体重を乗せて押し切る。
            if (Brain != null && Brain.IsPushing)
            {
                float time = Brain.PushElapsed;
                float reach = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.03f, EnemyBrain.PushContactSeconds, time));
                float release = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.19f, EnemyBrain.PushDuration, time));
                float shoveDrive = reach * (1f - release);
                float load = Mathf.Sin(Mathf.PI * Mathf.Clamp01(time / EnemyBrain.PushContactSeconds));
                pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-36f, -77f, shoveDrive);
                pose.ElbowR = pose.ElbowL = Mathf.Lerp(70f, 12f, shoveDrive);
                pose.ArmRSpread = -10f;
                pose.ArmLSpread = 10f;
                pose.TorsoPitch = 5f + 6f * load - 26f * shoveDrive;
                pose.HipsY = restY - 0.09f * load - 0.055f * shoveDrive;
                pose.HipsZ += 0.14f * shoveDrive - 0.06f * load;
                pose.LegRPitch = Mathf.Lerp(4f, -28f, shoveDrive);
                pose.LegLPitch = Mathf.Lerp(12f, 23f, shoveDrive);
                pose.KneeR = 17f + 20f * shoveDrive + 15f * load;
                pose.KneeL = 18f + 18f * load;
            }
            // 体で衝撃を受けてから腕で支え、背面拘束では片腕で向きを誘導する。
            if (Brain != null && Brain.IsCatching)
            {
                float impact = Brain.CatchImpact;
                pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-58f, -44f, impact);
                pose.ElbowR = pose.ElbowL = Mathf.Lerp(60f, 78f, impact);
                pose.ArmRSpread = -Mathf.Lerp(24f, 11f, impact);
                pose.ArmLSpread = -pose.ArmRSpread;
                pose.TorsoPitch += 24f * impact - 6f;
                pose.HipsZ -= 0.04f + 0.045f * impact;
                pose.HipsY -= 0.03f + 0.06f * impact;
                pose.LegLPitch += 13f * impact;
                pose.KneeR += 8f + 16f * impact;
                pose.KneeL += 8f + 10f * impact;
                if (holdState != null)
                {
                    float guide = Mathf.Sin(Mathf.PI * holdState.HandOffTurn);
                    pose.ArmRPitch -= 18f * guide;
                    pose.ElbowR += 16f * guide;
                    pose.ArmLPitch += 14f * guide;
                    pose.LegRPitch -= 12f * guide;
                }
            }
            else if (holdState != null && holdState.IsHandingOff && holdState.Captor == Brain)
            {
                float ready = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.20f, 1.85f, holdState.RecoveryStep));
                pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-8f, -52f, ready);
                pose.ElbowR = pose.ElbowL = Mathf.Lerp(18f, 40f, ready);
                pose.ArmRSpread = -Mathf.Lerp(8f, 23f, ready);
                pose.ArmLSpread = -pose.ArmRSpread;
            }
            else if (holdState != null && holdState.IsHandingOff && Brain.IsHandOffLocked && !Brain.IsPushing)
            {
                // 送り手は押し切った腕を戻し、受け渡しを見送る。
                pose.ArmRPitch = pose.ArmLPitch = -20f;
                pose.ElbowR = pose.ElbowL = 42f;
                pose.TorsoPitch = 0f;
                pose.HipsZ = 0f;
            }
            // ベアハッグ: 胴を抱え込むので上体を前へ寄せ、膝を落として締める。
            if (Brain != null && Brain.IsBearHugging)
            {
                var hug = Brain.Player.GetComponent<PlayerGrapple>();
                float progress = hug != null ? Mathf.Clamp01(hug.Progress) : 0f;
                float lift = hug != null ? hug.LiftPulse : 0f;
                float squeeze = hug != null ? hug.SqueezePulse : 0f;
                float yank = hug != null ? hug.PullPulse : 0f;

                // 抱え上げるほど上体を起こし、腰を彼女へ寄せる。締め付けでは少し沈む。
                // 引き寄せでは体重を後ろへ預けて、腕で彼女を引きずり込む。
                pose.TorsoPitch += Mathf.Lerp(8f, -4f, progress)
                    - 4f * lift + 2f * squeeze - 7f * yank;
                pose.HipsZ += 0.05f + 0.02f * squeeze - 0.03f * yank;
                pose.HipsY += 0.03f * lift - 0.03f + 0.02f * yank;
                pose.KneeR += 8f - 4f * lift + 5f * yank;
                pose.KneeL += 8f - 4f * lift + 5f * yank;
            }
            // 押さえつけ（Pin）: 肩を押し込むので上体を前へ倒し、膝を落として踏ん張る。            // 腕は PinHoldPose が彼女の肩へ向けて毎フレーム引き直す。
            // もがかれるほど押し返されて上体が起こされる（振りほどきが効いているのが見える）。
            if (Brain != null && Brain.IsPinning)
            {
                var pin = Brain.Player.GetComponent<PlayerGrapple>();
                float progress = pin != null ? Mathf.Clamp01(pin.Progress) : 0f;
                pose.TorsoPitch += Mathf.Lerp(12f, -6f, progress);
                pose.HipsY -= 0.06f;
                pose.KneeR += 10f;
                pose.KneeL += 10f;

                // 壁押さえつけは、壁へ体重を預けて押し込む。上体を起こしたまま前へ出る。
                if (Brain.IsWallPinning)
                {
                    pose.TorsoPitch += 9f;
                    pose.HipsZ += 0.06f;
                    float reachDown = pin != null ? pin.WallSlideWeight * pin.WallGripWeight : 0f;
                    pose.HipsY -= 0.14f * reachDown;
                    pose.TorsoPitch += 17f * reachDown;
                    pose.KneeR += 22f * reachDown;
                    pose.KneeL += 22f * reachDown;
                }

                // 膝蹴り。カメラ側の脚で入れる（奥の脚だと彼の体に隠れて見えない）。
                float knee = pin != null ? pin.KneePulse : 0f;
                if (knee > 0f)
                {
                    var cam = Camera.main;
                    bool leftKnee = false;
                    if (cam != null)
                    {
                        Vector3 toCam = cam.transform.position - transform.position;
                        toCam.y = 0f;
                        leftKnee = Vector3.Dot(toCam, transform.right) < 0f;
                    }

                    pose.HipsY += 0.02f * knee;
                    // 壁押さえつけは彼女との距離が近い（腹の表面まで 0.14m）。脚を大きく前へ
                    // 振り上げると、腿のほうが長いぶん膝が腹を突き抜ける（実測）。
                    // 腰を前へ押さず、膝は低めに上げて、深さは PinHoldPose の合わせに任せる。
                    // さらに骨盤を沈める。腰が高いまま膝を腹へ合わせると、骨盤を上へ
                    // 持ち上げることになって彼が浮き上がる（実測）。しゃがんで膝を入れる。
                    bool wallKnee = Brain != null && Brain.IsWallPinning;
                    if (wallKnee)
                        pose.HipsY -= 0.26f * knee;
                    pose.HipsZ += (wallKnee ? 0f : 0.08f) * knee;
                    float raise = wallKnee ? 70f : 105f;
                    float swing = wallKnee ? -85f : -115f;
                    if (leftKnee)
                    {
                        // 骨盤を右へずらして、左膝を彼女の腹の中心線へ入れる
                        // （そのまま上げると膝が彼女の脇腹の横を通って「当たっていない」画になる）
                        pose.HipsX += 0.10f * knee;
                        pose.KneeL += raise * knee;
                        pose.LegLPitch += swing * knee;
                        pose.KneeR += 8f * knee;
                    }
                    else
                    {
                        pose.HipsX -= 0.10f * knee;
                        pose.KneeR += raise * knee;
                        pose.LegRPitch += swing * knee;
                        pose.KneeL += 8f * knee;
                    }
                }
            }
            // --- 立ち上がり（倒れた姿勢から起こす） ---------------------
            // 4 つの形を順に通す: 仰向け → 上体を起こす → しゃがみ（膝を深く折る）
            // → 立ち上がる。途中で必ず膝を折って足を体の下へ入れる。
            // これが無いと、脚を伸ばしたまま足首を支点に板が起きるように見える。
            float rise = RiseNow;
            bool rising = RisingNow && (phase == EnemyPhase.Chase || phase == EnemyPhase.Strafe);
            if (rising)
            {
                // 倒れている間も頭は前を見る。上体と一緒に垂れると
                // 「気を失ったまま体だけ起きた」ように見える。
                pose.Fall = SampleRise(RiseFall, rise);
                pose.HipsY = restY * SampleRise(RiseHip, rise);
                pose.HipsZ = 0f;
                pose.TorsoPitch = SampleRise(RiseTorso, rise);
                pose.TorsoYaw = SampleRise(RiseYaw, rise);
                pose.HeadPitch = SampleRise(RiseHead, rise);
                pose.ArmRPitch = SampleRise(RiseArmR, rise);
                pose.ArmLPitch = SampleRise(RiseArmL, rise);
                pose.ElbowR = SampleRise(RiseElbowR, rise);
                pose.ElbowL = SampleRise(RiseElbowL, rise);
                pose.KneeR = SampleRise(RiseKnee, rise);
                pose.KneeL = SampleRise(RiseKnee, rise);
                pose.LegRPitch = SampleRise(RiseLeg, rise);
                pose.LegLPitch = SampleRise(RiseLeg, rise);
                pose.LegsExact = false;   // 脚は角度で作る（歩行の接地 IK は使わない）
            }
            // 掴みは両手を使うので、パイプは手元から隠す。
            // 立ち上がりの途中も隠す（床に手を突いている間、手にパイプがあると刺さる）。
            bool grabbing = AttackNow == AttackId.Grab
                && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack || phase == EnemyPhase.Recover);
            bool weaponHidden = grabbing || (rising && rise < 0.9f);
            SetWeaponVisible(!weaponHidden);
            // --- 手首 ---------------------------------------------------
            // 前腕と拳が一直線のままだと「棒の先に塊」に見える。
            // 当てる瞬間だけ伸ばし、それ以外は少し折って力を抜く。
            pose.WristR = 8f;
            pose.WristL = 8f;
            pose.TwistR = -12f;   // 前腕の軸まわり（手のひらを内へ）
            pose.TwistL = 12f;
            switch (phase)
            {
                case EnemyPhase.Windup:
                    pose.WristR = -16f;   // 拳を立てて振りかぶる
                    pose.WristL = 4f;
                    pose.TwistR = -22f;
                    break;
                case EnemyPhase.Attack:
                    pose.WristR = 2f;     // 当たる瞬間は前腕と一直線
                    pose.TwistR = -4f;
                    break;
                case EnemyPhase.Recover:
                    pose.WristR = 12f;    // 振り抜いたあとは脱力
                    pose.TwistR = -8f;
                    break;
                case EnemyPhase.Stagger:
                    pose.WristR = 18f;    // 力が抜れて手が遅れる
                    pose.WristL = 18f;
                    break;
                case EnemyPhase.Dead:
                    pose.WristR = 4f;
                    pose.WristL = 4f;
                    break;
            }
            if (Brain != null && Brain.IsTagFacePunch)
                pose.WristR = pose.WristL = 3f;
            if (Brain != null && (Brain.IsBellyPunch || Brain.KeepsBellyPunchArm))
            {
                pose.WristR = 0f;
                pose.TwistR = -8f;
            }
            // 掴みは手を開いて伸ばすので、拳を立てない
            if (grabbing)
            {
                pose.WristR = 0f;
                pose.WristL = 0f;
            }
            // Author the belly punch on the right, then mirror the whole pose for a left strike.
            pose.ShoulderROffset = Vector3.zero;
            if (Brain != null && !Brain.IsTagFacePunch && Brain.UsesLeftPunch)
            {
                (pose.ArmRPitch, pose.ArmLPitch) = (pose.ArmLPitch, pose.ArmRPitch);
                (pose.ArmRSpread, pose.ArmLSpread) = (-pose.ArmLSpread, -pose.ArmRSpread);
                (pose.ElbowR, pose.ElbowL) = (pose.ElbowL, pose.ElbowR);
                (pose.WristR, pose.WristL) = (pose.WristL, pose.WristR);
                (pose.TwistR, pose.TwistL) = (-pose.TwistL, -pose.TwistR);
                (pose.LegRPitch, pose.LegLPitch) = (pose.LegLPitch, pose.LegRPitch);
                (pose.KneeR, pose.KneeL) = (pose.KneeL, pose.KneeR);
                (pose.ShoulderROffset, pose.ShoulderLOffset) = (pose.ShoulderLOffset, pose.ShoulderROffset);
                pose.TorsoYaw = -pose.TorsoYaw;
                pose.TorsoRoll = -pose.TorsoRoll;
                pose.HipsX = -pose.HipsX;
            }
            if (Brain != null && Brain.IsWallPinning && holdState != null)
            {
                float progress = Mathf.SmoothStep(0f, 1f, holdState.WallPhaseProgress);
                if (holdState.WallPhase == WallStage.Throw)
                {
                    pose.TorsoPitch = 18f * progress;
                    pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-48f, -72f, progress);
                    pose.ElbowR = pose.ElbowL = Mathf.Lerp(65f, 22f, progress);
                    pose.ArmRSpread = -12f;
                    pose.ArmLSpread = 12f;
                }
                else if (holdState.IsWallToss)
                {
                    float side = holdState.WallTossSide;
                    if (holdState.WallPhase == WallStage.TossWindup)
                    {
                        pose.TorsoYaw = -side * 24f * progress;
                        pose.TorsoPitch = Mathf.Lerp(9f, -6f, progress);
                        pose.HipsY -= 0.08f * progress;
                    }
                    else
                    {
                        float follow = holdState.WallPhase == WallStage.TossAir
                            ? 1f - progress : 0f;
                        pose.TorsoYaw = side * 42f * follow;
                        pose.TorsoPitch = 16f * follow;
                        pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-18f, -75f, follow);
                        pose.ArmRSpread = -12f - side * 32f * follow;
                        pose.ArmLSpread = 12f - side * 32f * follow;
                        pose.ElbowR = pose.ElbowL = Mathf.Lerp(44f, 15f, follow);
                    }
                }
                else if (holdState.WallPhase == WallStage.Slam ||
                    holdState.WallPhase == WallStage.Lean)
                {
                    // 壁に当たったら手を離し、ずり落ちる間に腕を下ろす。
                    float release = holdState.WallPhase == WallStage.Slam
                        ? progress * 0.55f : Mathf.Lerp(0.55f, 1f, progress);
                    pose.TorsoPitch = Mathf.Lerp(18f, 6f, release);
                    pose.ArmRPitch = pose.ArmLPitch = Mathf.Lerp(-72f, -14f, release);
                    pose.ElbowR = pose.ElbowL = Mathf.Lerp(22f, 42f, release);
                }
            }

            return pose;
        }

        /// <summary>
        /// 組み立てた姿勢を実際のボーンへ流し込む（補間・IK・接地補正）。
        /// </summary>
        void ApplyPoseTarget(PoseTarget pose, float dt, bool instant, EnemyPhase phase, PlayerGrapple holdState)
        {
            // --- なめらかに適用 ---------------------------------------
            float k2 = instant ? 1f : 1f - Mathf.Exp(-14f * dt);
            _hipsY = Mathf.Lerp(_hipsY, pose.HipsY, k2);
            _hipsZ = Mathf.Lerp(_hipsZ, pose.HipsZ, k2);
            _hipsX = Mathf.Lerp(_hipsX, pose.HipsX, k2);
            _torsoPitch = Mathf.Lerp(_torsoPitch, pose.TorsoPitch, k2);
            _torsoRoll = Mathf.Lerp(_torsoRoll, pose.TorsoRoll, k2);
            _torsoYaw = Mathf.Lerp(_torsoYaw, pose.TorsoYaw, k2);
            _headPitch = Mathf.Lerp(_headPitch, pose.HeadPitch, k2);
            _armRPitch = Mathf.Lerp(_armRPitch, pose.ArmRPitch, k2);
            _armLPitch = Mathf.Lerp(_armLPitch, pose.ArmLPitch, k2);
            _armRSpread = Mathf.Lerp(_armRSpread, pose.ArmRSpread, k2);
            _armLSpread = Mathf.Lerp(_armLSpread, pose.ArmLSpread, k2);
            _shoulderROffset = Vector3.Lerp(_shoulderROffset, pose.ShoulderROffset, k2);
            _shoulderLOffset = Vector3.Lerp(_shoulderLOffset, pose.ShoulderLOffset, k2);
            _elbowRAngle = Mathf.Lerp(_elbowRAngle, pose.ElbowR, k2);
            _elbowLAngle = Mathf.Lerp(_elbowLAngle, pose.ElbowL, k2);
            _wristRBend = Mathf.Lerp(_wristRBend, pose.WristR, k2);
            _wristLBend = Mathf.Lerp(_wristLBend, pose.WristL, k2);
            _wristRTwist = Mathf.Lerp(_wristRTwist, pose.TwistR, k2);
            _wristLTwist = Mathf.Lerp(_wristLTwist, pose.TwistL, k2);
            if (!pose.LegsExact)
            {
                _legRPitch = Mathf.Lerp(_legRPitch, pose.LegRPitch, k2);
                _legLPitch = Mathf.Lerp(_legLPitch, pose.LegLPitch, k2);
                _kneeRAngle = Mathf.Lerp(_kneeRAngle, pose.KneeR, k2);
                _kneeLAngle = Mathf.Lerp(_kneeLAngle, pose.KneeL, k2);
            }
            _fallAngle = Mathf.Lerp(_fallAngle, pose.Fall, instant ? 1f : (phase == EnemyPhase.Dead ? 1f - Mathf.Exp(-4f * dt) : k2));
            // 予備動作の間、構えたパイプを光らせる。
            // 以前は頭の上に赤い球を出していたが、振りかぶった武器と二重になって
            // 「宙に玉が浮いている」ようにしか見えなかったため、武器そのもので示す。
            float chargeTarget = phase == EnemyPhase.Windup ? Mathf.Clamp01(WindupNow) : 0f;
            if (DebugCharge >= 0f)
                chargeTarget = DebugCharge;   // 検証用（PhotoShot -shotcharge）
            _charge = instant ? chargeTarget : Mathf.Lerp(_charge, chargeTarget, 1f - Mathf.Exp(-16f * dt));
            ApplyWeaponCharge();
            _hips.localPosition = new Vector3(_hipsX, _hipsY, _hipsZ);
            _hips.localRotation = Quaternion.Euler(0f, _torsoYaw * 0.45f, 0f);
            _chest.localRotation = Quaternion.Euler(-_torsoPitch, _torsoYaw * 0.55f, _torsoRoll);
            float headCounterYaw = Brain != null && Brain.IsTagFacePunch ? 0.90f : 0.25f;
            _head.localRotation = Quaternion.Euler(-_headPitch * 0.5f, -_torsoYaw * headCounterYaw, 0f);
            _shoulderR.localRotation = Quaternion.Euler(_armRPitch, 0f, _armRSpread);
            _shoulderR.localPosition = _shoulderRRest + _shoulderROffset;
            _shoulderL.localPosition = _shoulderLRest + _shoulderLOffset;
            _shoulderL.localRotation = Quaternion.Euler(_armLPitch, 0f, _armLSpread);
            _elbowR.localRotation = Quaternion.Euler(-_elbowRAngle, 0f, 0f);
            _elbowL.localRotation = Quaternion.Euler(-_elbowLAngle, 0f, 0f);
            if (_wristR != null)
                _wristR.localRotation = Quaternion.Euler(_wristRBend, _wristRTwist, 0f);
            if (_wristL != null)
                _wristL.localRotation = Quaternion.Euler(_wristLBend, _wristLTwist, 0f);
            if (pose.LegsExact)
            {
                // 進行方向へ踏み出す。前後だけでなく横にも運べるよう、位置から IK で解く
                // 腰の横移動は足首の目標も一緒に動かすので、同じ量だけ引いて接地を保つ
                Vector3 targetR = _legTargetR;
                Vector3 targetL = _legTargetL;
                targetR.x -= _hipsX;
                targetL.x -= _hipsX;
                Vector3 poleR = _hipR.position + transform.forward * 0.5f + Vector3.up * 0.15f;
                Vector3 poleL = _hipL.position + transform.forward * 0.5f + Vector3.up * 0.15f;
                ProceduralRig.SolveTwoBone(_hipR, _kneeR, _ankleR,
                    _hipR.position + transform.rotation * targetR, poleR);
                ProceduralRig.SolveTwoBone(_hipL, _kneeL, _ankleL,
                    _hipL.position + transform.rotation * targetL, poleL);
                // 足は脛と一緒に回すとつま先立ちに見える。地面と平行に保つ
                Quaternion flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _ankleR.rotation = flat;
                _ankleL.rotation = flat;
                // 歩き終わりに姿勢が飛ばないよう、実際の角度を覚えておく
                _legRPitch = Mathf.DeltaAngle(0f, _hipR.localEulerAngles.x);
                _legLPitch = Mathf.DeltaAngle(0f, _hipL.localEulerAngles.x);
                _kneeRAngle = Mathf.DeltaAngle(0f, _kneeR.localEulerAngles.x);
                _kneeLAngle = Mathf.DeltaAngle(0f, _kneeL.localEulerAngles.x);
            }
            else
            {
                _hipR.localRotation = Quaternion.Euler(_legRPitch, 0f, 0f);
                _hipL.localRotation = Quaternion.Euler(_legLPitch, 0f, 0f);
                _kneeR.localRotation = Quaternion.Euler(_kneeRAngle, 0f, 0f);
                _kneeL.localRotation = Quaternion.Euler(_kneeLAngle, 0f, 0f);
                _ankleR.localRotation = Quaternion.identity;
                _ankleL.localRotation = Quaternion.identity;
            }
            // 拳側の足はかかとを上げて踏ん張る（体重が拳へ乗ったことが足元で分かる）。
            if (Brain != null && Brain.IsTagFacePunch)
            {
                float heel = 34f * Brain.TagFaceDrive * Brain.TagFacePower;
                if (heel > 0.1f)
                {
                    if (Brain.UsesLeftPunch)
                        _ankleL.localRotation = Quaternion.Euler(heel, 0f, 0f);
                    else
                        _ankleR.localRotation = Quaternion.Euler(heel, 0f, 0f);
                }
            }
            // 前フレームの持ち上げを戻してから測る（残したまま測ると、
            // 沈みが小さく見えて補正が足りず、毎フレーム少しずつずれる）。
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.Euler(_fallAngle, 0f, 0f);

            // 倒れている間は、体の一番低いところが床に着くように持ち上げる。
            ApplyFloorClamp();

            // 拳が届くまで全身を小さく踏み込み、判定と見た目の距離を揃える。
            ApplyLunge(phase, holdState);
        }

        /// <summary>目標姿勢の数値一式。ローカル変数 25 個の受け渡しをやめるための入れ物。</summary>
        struct PoseTarget
        {
            public float HipsY, HipsZ, HipsX;
            public float TorsoPitch, TorsoYaw, TorsoRoll;
            public float HeadPitch;
            public float ArmRPitch, ArmLPitch, ArmRSpread, ArmLSpread;
            public Vector3 ShoulderLOffset, ShoulderROffset;
            public float ElbowR, ElbowL;
            public float WristR, WristL, TwistR, TwistL;
            public float LegRPitch, LegLPitch;
            public float KneeR, KneeL;
            public float Fall;
            public bool LegsExact;
        }

        /// <summary>
        /// 位置の差分から歩行の速さ・位相・向きを求める。
        ///
        /// 固定係数で平滑するとフレームレートで歩く速さが変わるため、
        /// 経過時間から係数を作る（60fps と 144fps で足の回る速さを揃える）。
        /// </summary>
        void TickMotion(float dt, bool instant)
        {
            if (instant)
                return;

            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            float speedBlend = 1f - Mathf.Exp(-15f * dt);
            _speed = Mathf.Lerp(_speed, flat.magnitude / dt, speedBlend);
            // 進んだ距離をそのまま足の運びに使う。時間で位相を回すと速度とズレて滑る。
            _walkDist += flat.magnitude;
            // 体から見た進行方向。横歩きでも足を進行方向へ運ぶため
            if (flat.sqrMagnitude > 1e-8f)
            {
                Vector3 localDir = transform.InverseTransformDirection(flat);
                Vector2 wanted = new Vector2(localDir.x, localDir.z).normalized;
                Vector2 blended = Vector2.Lerp(_walkDir, wanted, 1f - Mathf.Exp(-12f * dt));
                if (blended.sqrMagnitude > 1e-6f)
                    _walkDir = blended.normalized;
            }
        }

        /// <summary>
        /// 倒れている間、体の一番低いところが床に着くように根元を持ち上げる。
        /// 根元を回すだけだと脚や背中が床を突き抜ける（実測で足首が -25cm、胸が -6cm）。
        /// </summary>
        void ApplyFloorClamp()
        {
            if (_fallAngle < -5f)
            {
                float lowest = Mathf.Min(_ankleL.position.y, _ankleR.position.y);
                lowest = Mathf.Min(lowest, _hips.position.y - 0.14f);
                lowest = Mathf.Min(lowest, _chest.position.y - 0.12f);
                lowest = Mathf.Min(lowest, _head.position.y - 0.10f);
                _root.localPosition = new Vector3(0f, Mathf.Max(0f, -lowest), 0f);
            }
            else
            {
                _root.localPosition = Vector3.zero;
            }
        }

        /// <summary>
        /// 拳が届くまで全身を小さく踏み込み、判定と見た目の距離を揃える。
        /// タッグ技（腹・顔）は体で押し込む量を変える。
        /// </summary>
        void ApplyLunge(EnemyPhase phase, PlayerGrapple holdState)
        {
            float drive = 0f;
            if (AttackNow != AttackId.Grab)
            {
                if (phase == EnemyPhase.Windup)
                    drive = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WindupNow - 0.75f) / 0.25f));
                else if (phase == EnemyPhase.Attack)
                    drive = 1f;
                else if (phase == EnemyPhase.Recover && _swingTime >= 0f)
                    drive = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_swingTime - 0.24f) / 0.18f));
            }
            float lunge = 0.40f;
            if (Brain != null && Brain.IsTagPunch && Brain.Zone == PunchZone.Body && Brain.Player != null)
            {
                float gap = Vector3.Dot(Brain.Player.transform.position - transform.position, transform.forward);
                // 左向きの構図では打ち手が手前に入る。顔の前まで上体を運ばず、拳を伸ばして届かせる。
                lunge = Mathf.Clamp(gap - CombatConstants.BodyGap, 0f, lunge);
                if (phase == EnemyPhase.Recover && _swingTime >= 0f)
                    drive = 1f - Mathf.SmoothStep(0f, 1f, (_swingTime - 0.20f) / 0.17f);
            }
            else if (Brain != null && Brain.IsTagFacePunch && Brain.Player != null)
            {
                // 顔面パンチは腕の軌道で届かせる。体で押し込む分だけ少し前へ出る
                // （大きく踏み込むと拳が彼女へめり込み、腕が畳まれて見える）。
                float gap = Vector3.Dot(Brain.Player.transform.position - transform.position, transform.forward);
                lunge = Mathf.Clamp(gap - CombatConstants.BodyGap, 0f, 0.06f);
                drive = Brain.TagFaceDrive;
            }
            _lungeOffset = lunge * drive;
            _root.localPosition += Vector3.forward * _lungeOffset;
            if (Brain != null && Brain.IsHolding && holdState != null && holdState.IsGrappled
                && holdState.Captor == Brain && holdState.Kind == GrappleKind.Back)
                _root.localPosition += Vector3.forward * 0.03f;
        }

    }
}
