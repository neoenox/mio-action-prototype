using UnityEngine;

namespace Ryona.Game
{
    public sealed partial class CharacterAnimator
    {
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

    }
}
