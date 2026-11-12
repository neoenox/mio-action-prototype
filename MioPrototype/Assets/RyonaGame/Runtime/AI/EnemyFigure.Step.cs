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
            float restY = Height * 0.515f;
            float hipsY = restY;
            float hipsZ = 0f;
         // + で前へ踏み込む
            float hipsX = 0f;         // + で右へ（軸足へ体重を移す）
            float torsoPitch = 0f;    // - で前傾（胸の X 回転には符号を反転して適用する）
            float torsoYaw = 0f;      // + で左へひねる
            float torsoRoll = 0f;     // + で左へ傾く
            float headPitch = 0f;
            float armRPitch = -8f;    // - で前、+ で後ろ
            float armLPitch = -8f;
            float armRSpread = -8f;   // 肩の開き（右は負が基準）
            float armLSpread = 8f;
            Vector3 shoulderLOffset = Vector3.zero;
            float elbowR = 22f;
            float elbowL = 22f;
            float legRPitch = 0f;
            float legLPitch = 0f;
            float kneeR = 10f;
            float kneeL = 10f;
            float fall = 0f;
            bool legsExact = false;   // 歩行中は脚を補間しない（接地を保つ）
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
                    hipsY = hipBase + Height * 0.030f;
                    // 足首の目標。進行方向（横歩きなら横）へ運ぶ
                    FootPath(u, reachEff, WalkDuty, out float sweepR, out float liftR);
                    FootPath(Mathf.Repeat(u + 0.5f, 1f), reachEff, WalkDuty, out float sweepL, out float liftL);
                    // 体重を軸足へ預ける。これが無いと足だけが横へ滑って「跳び箱」に見える。
                    // 軸足は接地中の側なので、右足が着地する u=0 から 0.30 あたりが右、
                    // 0.80 あたりが左。
                    hipsX = Mathf.Cos(2f * Mathf.PI * (u - 0.30f)) * Height * 0.016f * amp;
                    // 進行方向へ上体を傾ける。横歩きは横へ、後退りは傾けない
                    torsoRoll = -dir.x * 4.5f * amp;
                    _legTargetR = new Vector3(dir.x * sweepR, liftR - hipBase, dir.y * sweepR);
                    _legTargetL = new Vector3(dir.x * sweepL, liftL - hipBase, dir.y * sweepL);
                    // 接地中の足は地面に固定したいので、脚の角度は補間せずそのまま使う
                    legsExact = true;
                    float s = -Mathf.Cos(2f * Mathf.PI * u);
                    // 横歩きでは前後の腕振りを減らし、代わりに腕を横へ小さく振る。
                    // 前後だけだと、横へ動いているのに「その場足踏み」に見える。
                    armRPitch = -s * 22f * amp * fwdness;
                    armLPitch = s * 22f * amp * fwdness;
                    armRSpread = -8f - s * 6f * amp * latness;
                    armLSpread = 8f - s * 6f * amp * latness;
                    elbowR = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    elbowL = 24f + Mathf.Abs(s) * 14f * (0.35f + 0.65f * fwdness);
                    torsoPitch = 5f * amp * (dir.y >= 0f ? dir.y : dir.y * 0.45f);
                    torsoYaw = s * 4f * amp;
                    // 止まっているときは腕を下ろしたままにせず、拳（武器）を軽く構える。
                    // 歩いている最中に混ぜると腕振りが消えるので、速度が落ちた分だけ効かせる。
                    float guard = Mathf.Clamp01(1f - amp * 2f);
                    if (guard > 0f)
                    {
                        armRPitch += -20f * guard;
                        elbowR += 30f * guard;
                        armLPitch += -13f * guard;
                        elbowL += 24f * guard;
                        torsoPitch += 3f * guard;
                        torsoYaw += -6f * guard;      // 半身になって相手の正面を外す
                        // 立ち止まっている間も呼吸と重心移動を入れる。
                        // 完全に静止すると「置物」に見え、こちらを狙っている感じが消える。
                        // 個体ごとに位相をずらして、3 体が同時に同じ呼吸をしないようにする。
                        if (_idlePhase < 0f)
                            _idlePhase = Random.value * 6.283f;
                        float idle = Time.time * 1.6f + _idlePhase;
                        float breath = Mathf.Sin(idle) * guard;
                        torsoPitch += breath * 0.9f;
                        torsoYaw += Mathf.Sin(idle * 0.37f) * 1.6f * guard;
                        hipsY += breath * 0.004f;
                        armRPitch += breath * 1.2f;
                        armLPitch += breath * 1.2f;
                        elbowR += breath * 1.8f;
                        elbowL += breath * 1.8f;
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
                    armRPitch = Mathf.Lerp(-8f, CockArm(attack), k);
                    elbowR = Mathf.Lerp(22f, CockElbow(attack), k);
                    // 振りかぶるほど肩を開く。前後（ピッチ）だけで振ると「蝶番」に見え、
                    // 肩が入っていない拳になる。開きは振り切る直前に戻す。
                    armRSpread = Mathf.Lerp(-8f, -21f, k);
                    armLSpread = Mathf.Lerp(8f, breakAttack ? 17f : 11f, k);
                    // 引き切ったあとも上体だけ僅かにひねり続ける（固まって見えないように）
                    float creep = Mathf.Clamp01((t - pull) / Mathf.Max(0.01f, 1f - pull));
                    torsoYaw = -Mathf.Lerp(0f, CockYaw(attack), k) - 3f * creep;
                    torsoPitch = Mathf.Lerp(4f, CockPitch(attack), k);
                    hipsY = restY - 0.05f * k;
                    // 振りかぶるほど体重を後ろへ預ける（振り始めの踏み込みが効く）
                    hipsZ = -0.035f * k;
                    // 崩しは両手を引く（見てから避けにくい代わりに、両腕が動く）
                    if (breakAttack)
                    {
                        armLPitch = Mathf.Lerp(-8f, 58f, k);
                        elbowL = Mathf.Lerp(22f, 46f, k);
                    }
                    else
                    {
                        armLPitch = Mathf.Lerp(-8f, -26f, k);
                        elbowL = Mathf.Lerp(22f, 70f, k);
                    }
                    legRPitch = Mathf.Lerp(0f, 8f, k);
                    legLPitch = Mathf.Lerp(0f, -10f, k);
                    kneeR = Mathf.Lerp(10f, 24f, k);
                    kneeL = Mathf.Lerp(10f, 26f, k);
                    // 振り切る直前。予備動作の最後で腕を打ち込み始める。
                    // ここを入れないと「当たった瞬間はまだ振りかぶったまま」に見える。
                    float release = Mathf.Clamp01((t - 0.88f) / 0.12f);
                    if (release > 0f)
                    {
                        armRPitch = Mathf.Lerp(armRPitch, StrikeArm(attack), release);
                        elbowR = Mathf.Lerp(elbowR, StrikeElbow(attack), release);
                        armRSpread = Mathf.Lerp(armRSpread, -12f, release);
                        armLSpread = Mathf.Lerp(armLSpread, breakAttack ? 9f : 7f, release);
                        torsoYaw = Mathf.Lerp(torsoYaw, StrikeYaw(attack), release);
                        torsoPitch = Mathf.Lerp(torsoPitch, StrikePitch(attack), release);
                        hipsZ = Mathf.Lerp(hipsZ, StrikeHips(attack), release);
                        headPitch = Mathf.Lerp(headPitch, 7f, release);
                    }
                    break;
                }
                case EnemyPhase.Attack:
                {
                    // 判定のフレーム。ここが「当たった瞬間」なので、腕を伸ばし切った形を出す。
                    // 以前は case が無く、当たりの 1 フレームだけ立ち姿に戻ってしまっていた。
                    AttackId attack = AttackNow;
                    bool breakAttack = attack == AttackId.Break;
                    armRPitch = StrikeArm(attack);
                    elbowR = StrikeElbow(attack);
                    armRSpread = -12f;
                    armLSpread = breakAttack ? 9f : 7f;
                    torsoYaw = StrikeYaw(attack);
                    torsoPitch = StrikePitch(attack);
                    headPitch = 7f;
                    hipsY = restY - 0.10f;
                    hipsZ = StrikeHips(attack);
                    if (breakAttack)
                    {
                        armLPitch = -70f;
                        elbowL = 10f;
                    }
                    else
                    {
                        armLPitch = -34f;
                        elbowL = 86f;
                    }
                    legRPitch = -6f;
                    legLPitch = 12f;
                    kneeR = 14f;
                    kneeL = 12f;
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
                    armRPitch = Mathf.Lerp(CockArm(attack), StrikeArm(attack), s);
                    elbowR = Mathf.Lerp(CockElbow(attack), StrikeElbow(attack), s);
                    torsoYaw = Mathf.Lerp(-CockYaw(attack), StrikeYaw(attack), s);
                    torsoPitch = Mathf.Lerp(CockPitch(attack), StrikePitch(attack), s);
                    hipsY = restY - Mathf.Lerp(0.05f, 0.10f, s);
                    headPitch = Mathf.Lerp(0f, 7f, s);
                    // 振り抜きに合わせて踏み込む（腰ごと前へ出ると打撃の重さが出る）
                    hipsZ = Mathf.Lerp(-0.02f, StrikeHips(attack), s);
                    if (breakAttack)
                    {
                        armLPitch = Mathf.Lerp(58f, -70f, s);
                        elbowL = Mathf.Lerp(46f, 10f, s);
                    }
                    else
                    {
                        armLPitch = Mathf.Lerp(-26f, -34f, s);
                        elbowL = Mathf.Lerp(70f, 86f, s);
                    }
                    legRPitch = Mathf.Lerp(8f, -6f, s);
                    legLPitch = Mathf.Lerp(-10f, 12f, s);
                    kneeR = Mathf.Lerp(24f, 14f, s);
                    kneeL = Mathf.Lerp(26f, 12f, s);
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
                    torsoPitch = 4f - back - 4f * settle;
                    torsoYaw = (26f * w + 7f * settle) * heaviness * 0.8f;
                    // 首は体のあとから落ち、戻りで一度だけ前に残る。
                    headPitch = 2f - back * 0.72f * lagHead + 4f * settle;
                    // 腕は振り遅れて外へ流れる。殴られた側（右）ほど大きく。
                    armRPitch = 24f + 34f * w * lagArm * heaviness * 0.7f;
                    armLPitch = 24f + 16f * w * lagArm;
                    elbowR = 46f - 18f * w;
                    elbowL = 40f - 8f * w;
                    // 膝が抜けて腰が落ち、同時に後ろへ押される。
                    hipsY = restY - 0.03f - 0.06f * w * heaviness;
                    hipsZ = -0.03f - 0.12f * w * heaviness;
                    // 前の足が突っ張り、後ろの足が引いて体重を逃がす。
                    legLPitch = -11f * w;
                    legRPitch = 13f * w;
                    kneeL = 10f + 8f * w;
                    kneeR = 10f + 20f * w * heaviness * 0.7f;
                    // 重い一撃は「のけぞる → こらえる」の2段にする。
                    // 反り切ったあと前へ折れて腹を抱えるので、単調な往復に見えない。
                    // （小さい一撃でこれをやると、いちいち痛がりすぎて戦闘が止まる）
                    if (heaviness >= 1.6f)
                    {
                        float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - 0.14f) / 0.14f))
                            * Mathf.Clamp01(1f - (e - 0.34f) / 0.26f);
                        torsoPitch += 24f * fold;
                        torsoYaw += -10f * fold;
                        headPitch += 8f * fold;       // 上体が折れた分、顔も落ちる
                        hipsY -= 0.05f * fold;
                        kneeR += 12f * fold;
                        // 腕は前へ伸ばさず、腹の前へ引き寄せる（伸ばすと「掴もうとしている」に見える）
                        armRPitch += -12f * fold;
                        armLPitch += -10f * fold;
                        elbowR += 8f * fold;
                        elbowL += 6f * fold;
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

                    fall = -84f;
                    hipsY = Mathf.Lerp(restY, restY * 0.35f, p);
                    armRPitch = 12f - 26f * buckle;
                    armLPitch = 12f - 24f * buckle;
                    elbowR = 16f + 26f * buckle;
                    elbowL = 16f + 24f * buckle;
                    legRPitch = -8f - 14f * buckle;
                    legLPitch = -8f - 12f * buckle;
                    kneeR = 12f + 66f * buckle;
                    kneeL = 12f + 60f * buckle;
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
                    armRPitch = Mathf.Lerp(-8f, -30f, open);
                    armLPitch = Mathf.Lerp(-8f, -30f, open);
                    elbowR = Mathf.Lerp(22f, 58f, open);
                    elbowL = Mathf.Lerp(22f, 58f, open);
                    armRSpread = Mathf.Lerp(-8f, -14f, open);
                    armLSpread = Mathf.Lerp(8f, 14f, open);

                    armRPitch = Mathf.Lerp(armRPitch, -76f, close);
                    armLPitch = Mathf.Lerp(armLPitch, -72f, close);
                    elbowR = Mathf.Lerp(elbowR, 18f, close);
                    elbowL = Mathf.Lerp(elbowL, 24f, close);
                    armRSpread = Mathf.Lerp(armRSpread, -14f, close);
                    armLSpread = Mathf.Lerp(armLSpread, 14f, close);

                    torsoPitch = Mathf.Lerp(4f * open, -18f, close);
                    torsoYaw = 0f;
                    hipsY = restY - 0.045f * open - 0.055f * close;
                    hipsZ = Mathf.Lerp(-0.035f * open, 0.08f, close);
                    kneeR = Mathf.Lerp(10f, 26f, open);
                    kneeL = Mathf.Lerp(10f, 30f, open);
                    legRPitch = 0f;
                    legLPitch = 0f;
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
                    armRPitch = Mathf.Lerp(-76f, -8f, retract);
                    armLPitch = Mathf.Lerp(-74f, -8f, retract);
                    elbowR = Mathf.Lerp(14f, 22f, retract);
                    elbowL = Mathf.Lerp(18f, 22f, retract);
                    armRSpread = Mathf.Lerp(-14f, -8f, retract);
                    armLSpread = Mathf.Lerp(14f, 8f, retract);
                    torsoPitch = Mathf.Lerp(-12f, 4f, retract);
                    torsoYaw = 0f;
                    headPitch = Mathf.Lerp(-12f, 2f, retract);   // 掴んでいる間は相手を見下ろす（正=上向き）
                    hipsY = restY - Mathf.Lerp(0.07f, 0.03f, retract);
                    hipsZ = Mathf.Lerp(0.06f, 0f, retract);
                    kneeR = Mathf.Lerp(26f, 10f, retract);
                    kneeL = Mathf.Lerp(22f, 10f, retract);
                    legRPitch = 0f;
                    legLPitch = 0f;
                    var hold = Brain != null && Brain.Player != null
                        ? Brain.Player.GetComponent<PlayerGrapple>() : null;
                    if (holding && hold != null && hold.Kind == GrappleKind.Back)
                    {
                        armRPitch = -78f;
                        armLPitch = -78f;
                        elbowR = 28f;
                        elbowL = 28f;
                        armRSpread = -28f;
                        armLSpread = 28f;
                        // 彼女の肩の横へ覆いかぶさる形（参考: マネキン写真の「拘束＋腹パン」）。
                        // 上体はわずかに前へ、頭を伏せて彼女の頭の横へ置く。前傾を深くすると
                        // 頭が真後ろへ入って髪と重なるので、腰を落として高さと間隔を稼ぐ
                        // （実測: 頭中心の距離 0.29m。前傾 -16° の旧姿勢は 0.19m で重なった）。
                        torsoPitch = -2f;
                        hipsY = restY - 0.17f;
                        // 前後に足を割って踏ん張る（両足を揃えて沈むと「座っている」画になる）。
                        // 彼女に近い足を前へ、反対の足を後ろへ引く。
                        kneeR = 62f;
                        kneeL = 40f;
                        legRPitch = -16f;
                        legLPitch = 14f;
                        headPitch = -22f;
                    }
                }

                if (Brain != null && Brain.IsPreparingGrab)
                {
                    float step = Mathf.InverseLerp(0.44f, 0.90f, WindupNow);
                    float advance = Brain.GrabAdvance;
                    float floor = Height * 0.030f - hipsY;
                    float forward = Mathf.SmoothStep(0f, 0.21f, step);
                    _legTargetR = new Vector3(0f, floor + 0.085f * Mathf.Sin(Mathf.PI * step), forward - hipsZ);
                    _legTargetL = new Vector3(0f, floor, -0.07f - Mathf.Min(advance, 0.30f) - hipsZ);
                    legsExact = true;
                }
            }
            if (Brain != null && Brain.HeldThrowPull > 0f)
            {
                float pull = Brain.HeldThrowPull;
                torsoPitch += 18f * pull;
                hipsY -= 0.08f * pull;
                kneeR += 16f * pull;
                kneeL += 10f * pull;
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
                    torsoYaw = Mathf.Lerp(24f, -31f, bodyDrive) * bw;
                    torsoPitch = Mathf.Lerp(torsoPitch, Mathf.Lerp(-14f, -33f, bodyDrive), bw);
                    hipsY = Mathf.Lerp(hipsY, restY - Mathf.Lerp(0.27f, 0.23f, bodyDrive), bw);
                    hipsZ = Mathf.Lerp(hipsZ, 0.11f * bodyDrive, bw);
                    kneeR = Mathf.Lerp(kneeR, Mathf.Lerp(48f, 67f, bodyDrive), bw);
                    kneeL = Mathf.Lerp(kneeL, Mathf.Lerp(61f, 34f, bodyDrive), bw);
                    legRPitch = Mathf.Lerp(legRPitch, Mathf.Lerp(-20f, -29f, bodyDrive), bw);
                    legLPitch = Mathf.Lerp(legLPitch, Mathf.Lerp(-4f, 18f, bodyDrive), bw);
                }
                else
                {
                    torsoPitch -= 16f * bw;
                    hipsY -= 0.10f * bw;
                    kneeR += 14f * bw;
                    kneeL += 14f * bw;
                }
            }
            // 拳を引いても次の打撃までは腰を上げない。顔を打つ瞬間だけ必要な高さを戻す。
            if (Brain != null && Brain.HoldsBellyPunchGuard &&
                (phase == EnemyPhase.Recover || phase == EnemyPhase.Chase || phase == EnemyPhase.Strafe ||
                 (Brain.IsTagPunch && (phase == EnemyPhase.Windup || phase == EnemyPhase.Attack))))
            {
                float guard = Brain.IsTagPunch && Brain.Zone == PunchZone.Face ? 1f - Brain.PunchPoseWeight : 1f;
                float lowHips = Mathf.Min(hipsY, restY - 0.20f * guard);
                if (legsExact)
                {
                    _legTargetR.y += hipsY - lowHips;
                    _legTargetL.y += hipsY - lowHips;
                }
                hipsY = lowHips;
                torsoPitch = Mathf.Min(torsoPitch, -13f * guard);
                kneeR = Mathf.Max(kneeR, 35f * guard);
                kneeL = Mathf.Max(kneeL, 23f * guard);
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
                    torsoYaw = side * (24f * faceCoil - 34f * faceDrive);
                    torsoPitch = -5f * faceCoil - 16f * faceDrive;
                    torsoRoll = side * (-4f * faceCoil + 8f * faceDrive);

                    // 溜めで後ろへ預けた体重を、踏み込みで拳へ乗せる。
                    // 決めの一発でも腰を出しすぎない（出しすぎると拳が彼女の顔に近づきすぎ、
                    // 肘が折れて「押している」画になる）。強さは上体のひねりと反りに乗せる。
                    float bodyDrive = Mathf.Min(faceDrive, 1.05f);
                    hipsZ = 0.18f * bodyDrive - 0.05f * faceCoil;
                    hipsY = restY - 0.06f * bodyDrive + 0.008f * faceCoil;

                    // 拳側（後ろ足）で踏ん張り、前足が体重を受けて曲がる。
                    bool punchRight = side > 0f;
                    legRPitch = punchRight ? (-3f - 12f * faceDrive) : (8f + 4f * faceDrive);
                    legLPitch = punchRight ? (8f + 4f * faceDrive) : (-3f - 12f * faceDrive);
                    kneeR = punchRight
                        ? (16f + 6f * faceCoil - 8f * faceDrive)
                        : (16f + 14f * faceDrive);
                    kneeL = punchRight
                        ? (16f + 14f * faceDrive)
                        : (16f + 6f * faceCoil - 8f * faceDrive);

                    // 打つ拳は頬の横へ引いて構える（振り抜きは BackHoldPose が実際の腕を解く）。
                    // 反対の腕は顎の前に構え、振り抜きで引いて反動をつくる。
                    // 構えの拳は体の中心ではなく「自分の頬の外側」に置く（戻すときに
                    // 彼女の顔の前を横切らない）。
                    float chamber = -30f - 12f * faceDrive;
                    if (punchRight)
                    {
                        armRPitch = chamber;
                        elbowR = 132f;
                        armRSpread = -18f;
                        armLPitch = -22f - 14f * faceDrive;
                        elbowL = 128f;
                        armLSpread = 10f;
                    }
                    else
                    {
                        armLPitch = chamber;
                        elbowL = 132f;
                        armLSpread = 18f;
                        armRPitch = -22f - 14f * faceDrive;
                        elbowR = 128f;
                        armRSpread = -10f;
                    }
                }
                else
                {
                    armLPitch = _tagArmL;
                    elbowL = _tagElbowL;
                    armLSpread = _tagSpreadL;
                    if (Brain.IsBellyPunch || Brain.KeepsBellyPunchArm)
                    {
                        // 右拳を出すときは左肩を引き、脇の手で上体のひねりを受ける。
                        float counter = Brain.IsBellyPunch ? Brain.BellyPunchDrive * Brain.PunchPoseWeight : 0f;
                        armLPitch += Mathf.Lerp(-12f, 18f, counter);
                        elbowL += Mathf.Lerp(16f, -15f, counter);
                        armLSpread += Mathf.Lerp(-4f, 8f, counter);
                        shoulderLOffset = new Vector3(0f, 0.014f * counter, Mathf.Lerp(0.012f, -0.052f, counter));
                    }
                }
            }
            // 検証用の上書き（エディタで姿勢を撮り比べる）。実行中は null なので影響しない。
            if (ForceLeftArm.HasValue)            {
                armLPitch = ForceLeftArm.Value.x;
                elbowL = ForceLeftArm.Value.y;
                armLSpread = ForceLeftArm.Value.z;
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
                armRPitch = armLPitch = Mathf.Lerp(-36f, -77f, shoveDrive);
                elbowR = elbowL = Mathf.Lerp(70f, 12f, shoveDrive);
                armRSpread = -10f;
                armLSpread = 10f;
                torsoPitch = 5f + 6f * load - 26f * shoveDrive;
                hipsY = restY - 0.09f * load - 0.055f * shoveDrive;
                hipsZ += 0.14f * shoveDrive - 0.06f * load;
                legRPitch = Mathf.Lerp(4f, -28f, shoveDrive);
                legLPitch = Mathf.Lerp(12f, 23f, shoveDrive);
                kneeR = 17f + 20f * shoveDrive + 15f * load;
                kneeL = 18f + 18f * load;
            }
            var holdState = Brain != null && Brain.Player != null ? Brain.Player.GetComponent<PlayerGrapple>() : null;
            // 体で衝撃を受けてから腕で支え、背面拘束では片腕で向きを誘導する。
            if (Brain != null && Brain.IsCatching)
            {
                float impact = Brain.CatchImpact;
                armRPitch = armLPitch = Mathf.Lerp(-58f, -44f, impact);
                elbowR = elbowL = Mathf.Lerp(60f, 78f, impact);
                armRSpread = -Mathf.Lerp(24f, 11f, impact);
                armLSpread = -armRSpread;
                torsoPitch += 24f * impact - 6f;
                hipsZ -= 0.04f + 0.045f * impact;
                hipsY -= 0.03f + 0.06f * impact;
                legLPitch += 13f * impact;
                kneeR += 8f + 16f * impact;
                kneeL += 8f + 10f * impact;
                if (holdState != null)
                {
                    float guide = Mathf.Sin(Mathf.PI * holdState.HandOffTurn);
                    armRPitch -= 18f * guide;
                    elbowR += 16f * guide;
                    armLPitch += 14f * guide;
                    legRPitch -= 12f * guide;
                }
            }
            else if (holdState != null && holdState.IsHandingOff && holdState.Captor == Brain)
            {
                float ready = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.20f, 1.85f, holdState.RecoveryStep));
                armRPitch = armLPitch = Mathf.Lerp(-8f, -52f, ready);
                elbowR = elbowL = Mathf.Lerp(18f, 40f, ready);
                armRSpread = -Mathf.Lerp(8f, 23f, ready);
                armLSpread = -armRSpread;
            }
            else if (holdState != null && holdState.IsHandingOff && Brain.IsHandOffLocked && !Brain.IsPushing)
            {
                // 送り手は押し切った腕を戻し、受け渡しを見送る。
                armRPitch = armLPitch = -20f;
                elbowR = elbowL = 42f;
                torsoPitch = 0f;
                hipsZ = 0f;
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
                torsoPitch += Mathf.Lerp(8f, -4f, progress)
                    - 4f * lift + 2f * squeeze - 7f * yank;
                hipsZ += 0.05f + 0.02f * squeeze - 0.03f * yank;
                hipsY += 0.03f * lift - 0.03f + 0.02f * yank;
                kneeR += 8f - 4f * lift + 5f * yank;
                kneeL += 8f - 4f * lift + 5f * yank;
            }
            // 押さえつけ（Pin）: 肩を押し込むので上体を前へ倒し、膝を落として踏ん張る。            // 腕は PinHoldPose が彼女の肩へ向けて毎フレーム引き直す。
            // もがかれるほど押し返されて上体が起こされる（振りほどきが効いているのが見える）。
            if (Brain != null && Brain.IsPinning)
            {
                var pin = Brain.Player.GetComponent<PlayerGrapple>();
                float progress = pin != null ? Mathf.Clamp01(pin.Progress) : 0f;
                torsoPitch += Mathf.Lerp(12f, -6f, progress);
                hipsY -= 0.06f;
                kneeR += 10f;
                kneeL += 10f;

                // 壁押さえつけは、壁へ体重を預けて押し込む。上体を起こしたまま前へ出る。
                if (Brain.IsWallPinning)
                {
                    torsoPitch += 9f;
                    hipsZ += 0.06f;
                    float reachDown = pin != null ? pin.WallSlideWeight * pin.WallGripWeight : 0f;
                    hipsY -= 0.14f * reachDown;
                    torsoPitch += 17f * reachDown;
                    kneeR += 22f * reachDown;
                    kneeL += 22f * reachDown;
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

                    hipsY += 0.02f * knee;
                    // 壁押さえつけは彼女との距離が近い（腹の表面まで 0.14m）。脚を大きく前へ
                    // 振り上げると、腿のほうが長いぶん膝が腹を突き抜ける（実測）。
                    // 腰を前へ押さず、膝は低めに上げて、深さは PinHoldPose の合わせに任せる。
                    // さらに骨盤を沈める。腰が高いまま膝を腹へ合わせると、骨盤を上へ
                    // 持ち上げることになって彼が浮き上がる（実測）。しゃがんで膝を入れる。
                    bool wallKnee = Brain != null && Brain.IsWallPinning;
                    if (wallKnee)
                        hipsY -= 0.26f * knee;
                    hipsZ += (wallKnee ? 0f : 0.08f) * knee;
                    float raise = wallKnee ? 70f : 105f;
                    float swing = wallKnee ? -85f : -115f;
                    if (leftKnee)
                    {
                        // 骨盤を右へずらして、左膝を彼女の腹の中心線へ入れる
                        // （そのまま上げると膝が彼女の脇腹の横を通って「当たっていない」画になる）
                        hipsX += 0.10f * knee;
                        kneeL += raise * knee;
                        legLPitch += swing * knee;
                        kneeR += 8f * knee;
                    }
                    else
                    {
                        hipsX -= 0.10f * knee;
                        kneeR += raise * knee;
                        legRPitch += swing * knee;
                        kneeL += 8f * knee;
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
                fall = SampleRise(RiseFall, rise);
                hipsY = restY * SampleRise(RiseHip, rise);
                hipsZ = 0f;
                torsoPitch = SampleRise(RiseTorso, rise);
                torsoYaw = SampleRise(RiseYaw, rise);
                headPitch = SampleRise(RiseHead, rise);
                armRPitch = SampleRise(RiseArmR, rise);
                armLPitch = SampleRise(RiseArmL, rise);
                elbowR = SampleRise(RiseElbowR, rise);
                elbowL = SampleRise(RiseElbowL, rise);
                kneeR = SampleRise(RiseKnee, rise);
                kneeL = SampleRise(RiseKnee, rise);
                legRPitch = SampleRise(RiseLeg, rise);
                legLPitch = SampleRise(RiseLeg, rise);
                legsExact = false;   // 脚は角度で作る（歩行の接地 IK は使わない）
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
            float wristR = 8f;
            float wristL = 8f;
            float twistR = -12f;   // 前腕の軸まわり（手のひらを内へ）
            float twistL = 12f;
            switch (phase)
            {
                case EnemyPhase.Windup:
                    wristR = -16f;   // 拳を立てて振りかぶる
                    wristL = 4f;
                    twistR = -22f;
                    break;
                case EnemyPhase.Attack:
                    wristR = 2f;     // 当たる瞬間は前腕と一直線
                    twistR = -4f;
                    break;
                case EnemyPhase.Recover:
                    wristR = 12f;    // 振り抜いたあとは脱力
                    twistR = -8f;
                    break;
                case EnemyPhase.Stagger:
                    wristR = 18f;    // 力が抜れて手が遅れる
                    wristL = 18f;
                    break;
                case EnemyPhase.Dead:
                    wristR = 4f;
                    wristL = 4f;
                    break;
            }
            if (Brain != null && Brain.IsTagFacePunch)
                wristR = wristL = 3f;
            if (Brain != null && (Brain.IsBellyPunch || Brain.KeepsBellyPunchArm))
            {
                wristR = 0f;
                twistR = -8f;
            }
            // 掴みは手を開いて伸ばすので、拳を立てない
            if (grabbing)
            {
                wristR = 0f;
                wristL = 0f;
            }
            // Author the belly punch on the right, then mirror the whole pose for a left strike.
            Vector3 shoulderROffset = Vector3.zero;
            if (Brain != null && !Brain.IsTagFacePunch && Brain.UsesLeftPunch)
            {
                (armRPitch, armLPitch) = (armLPitch, armRPitch);
                (armRSpread, armLSpread) = (-armLSpread, -armRSpread);
                (elbowR, elbowL) = (elbowL, elbowR);
                (wristR, wristL) = (wristL, wristR);
                (twistR, twistL) = (-twistL, -twistR);
                (legRPitch, legLPitch) = (legLPitch, legRPitch);
                (kneeR, kneeL) = (kneeL, kneeR);
                (shoulderROffset, shoulderLOffset) = (shoulderLOffset, shoulderROffset);
                torsoYaw = -torsoYaw;
                torsoRoll = -torsoRoll;
                hipsX = -hipsX;
            }
            if (Brain != null && Brain.IsWallPinning && holdState != null)
            {
                float progress = Mathf.SmoothStep(0f, 1f, holdState.WallPhaseProgress);
                if (holdState.WallPhase == WallStage.Throw)
                {
                    torsoPitch = 18f * progress;
                    armRPitch = armLPitch = Mathf.Lerp(-48f, -72f, progress);
                    elbowR = elbowL = Mathf.Lerp(65f, 22f, progress);
                    armRSpread = -12f;
                    armLSpread = 12f;
                }
                else if (holdState.IsWallToss)
                {
                    float side = holdState.WallTossSide;
                    if (holdState.WallPhase == WallStage.TossWindup)
                    {
                        torsoYaw = -side * 24f * progress;
                        torsoPitch = Mathf.Lerp(9f, -6f, progress);
                        hipsY -= 0.08f * progress;
                    }
                    else
                    {
                        float follow = holdState.WallPhase == WallStage.TossAir
                            ? 1f - progress : 0f;
                        torsoYaw = side * 42f * follow;
                        torsoPitch = 16f * follow;
                        armRPitch = armLPitch = Mathf.Lerp(-18f, -75f, follow);
                        armRSpread = -12f - side * 32f * follow;
                        armLSpread = 12f - side * 32f * follow;
                        elbowR = elbowL = Mathf.Lerp(44f, 15f, follow);
                    }
                }
                else if (holdState.WallPhase == WallStage.Slam ||
                    holdState.WallPhase == WallStage.Lean)
                {
                    // 壁に当たったら手を離し、ずり落ちる間に腕を下ろす。
                    float release = holdState.WallPhase == WallStage.Slam
                        ? progress * 0.55f : Mathf.Lerp(0.55f, 1f, progress);
                    torsoPitch = Mathf.Lerp(18f, 6f, release);
                    armRPitch = armLPitch = Mathf.Lerp(-72f, -14f, release);
                    elbowR = elbowL = Mathf.Lerp(22f, 42f, release);
                }
            }
            // --- なめらかに適用 ---------------------------------------
            float k2 = instant ? 1f : 1f - Mathf.Exp(-14f * dt);
            _hipsY = Mathf.Lerp(_hipsY, hipsY, k2);
            _hipsZ = Mathf.Lerp(_hipsZ, hipsZ, k2);
            _hipsX = Mathf.Lerp(_hipsX, hipsX, k2);
            _torsoPitch = Mathf.Lerp(_torsoPitch, torsoPitch, k2);
            _torsoRoll = Mathf.Lerp(_torsoRoll, torsoRoll, k2);
            _torsoYaw = Mathf.Lerp(_torsoYaw, torsoYaw, k2);
            _headPitch = Mathf.Lerp(_headPitch, headPitch, k2);
            _armRPitch = Mathf.Lerp(_armRPitch, armRPitch, k2);
            _armLPitch = Mathf.Lerp(_armLPitch, armLPitch, k2);
            _armRSpread = Mathf.Lerp(_armRSpread, armRSpread, k2);
            _armLSpread = Mathf.Lerp(_armLSpread, armLSpread, k2);
            _shoulderROffset = Vector3.Lerp(_shoulderROffset, shoulderROffset, k2);
            _shoulderLOffset = Vector3.Lerp(_shoulderLOffset, shoulderLOffset, k2);
            _elbowRAngle = Mathf.Lerp(_elbowRAngle, elbowR, k2);
            _elbowLAngle = Mathf.Lerp(_elbowLAngle, elbowL, k2);
            _wristRBend = Mathf.Lerp(_wristRBend, wristR, k2);
            _wristLBend = Mathf.Lerp(_wristLBend, wristL, k2);
            _wristRTwist = Mathf.Lerp(_wristRTwist, twistR, k2);
            _wristLTwist = Mathf.Lerp(_wristLTwist, twistL, k2);
            if (!legsExact)
            {
                _legRPitch = Mathf.Lerp(_legRPitch, legRPitch, k2);
                _legLPitch = Mathf.Lerp(_legLPitch, legLPitch, k2);
                _kneeRAngle = Mathf.Lerp(_kneeRAngle, kneeR, k2);
                _kneeLAngle = Mathf.Lerp(_kneeLAngle, kneeL, k2);
            }
            _fallAngle = Mathf.Lerp(_fallAngle, fall, instant ? 1f : (phase == EnemyPhase.Dead ? 1f - Mathf.Exp(-4f * dt) : k2));
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
            if (legsExact)
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
