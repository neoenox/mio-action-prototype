using UnityEngine;
using System.Collections.Generic;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.Game
{
    public sealed partial class EnemyFigure : MonoBehaviour
    {
        /// <summary>
        /// 予備動作の進み具合でパイプを光らせる。振り切る直前に速く点滅させて
        /// 「ここで来る」を分からせる。0 で消灯。
        /// </summary>
        void ApplyWeaponCharge()
        {
            if (_weaponSteel == null)
                return;
            float t = _charge;
            if (t <= 0.001f)
            {
                _weaponSteel.SetColor(ColorId, _weaponBase);
                _weaponSteel.SetColor(EmissionId, Color.black);
                return;
            }
            // 溜め切る前に脈打たせる
            float pulse = 1f + 0.30f * Mathf.Sin(Time.time * 42f) * Mathf.Clamp01((t - 0.55f) * 3f);
            float k = t * pulse;
            // アンドロイドなので、予備動作は拳の「青い発光」で伝える。
            _weaponSteel.SetColor(ColorId, Color.Lerp(_weaponBase, new Color(0.20f, 0.70f, 1f), t * 0.55f));
            _weaponSteel.SetColor(EmissionId, new Color(0.15f, 0.70f, 1f) * (3.2f * k));
        }
        // --- 振りかぶり姿勢 -----------------------------------------------
        // Windup の終わりと Recover の振り始めで必ず同じ値を使う。
        // 別々に書くと切り替わりの1フレームで腕が飛ぶ。
        //
        // Heavy は「振りかぶりが大きい」で見分けられるようにする（Jab と形が似ていて読みにくかった）。
        static float CockArm(AttackId a)
        {
            switch (a)
            {
                // 横薙ぎ: 体の横へ水平に引く（バットの振りかぶり）
                case AttackId.Break: return 92f;
                // 実測（arm_sweep.png）: -170 が真上、-80 が前方水平、+130〜+160 が「頭の後ろ」。
                // 振り下ろしは頭の後ろに担いでから振るので +145 側。
                // 以前は +96（真下の後ろ）で、振り下ろしなのに腕が下がっていた。
                case AttackId.Heavy: return 36f;
                // 突き: 脇を締めて腰の横へ引く（槍の構え）。
                // 以前は横薙ぎと 62/68 でほぼ同じで、予備動作が見分けられなかった。
                default: return 34f;
            }
        }

        static float CockElbow(AttackId a)
        {
            switch (a)
            {
                case AttackId.Break: return 46f;
                case AttackId.Heavy: return 72f;
                // 突きは肘を深く曲げて、柄を体の横に付ける
                default: return 82f;
            }
        }

        static float CockYaw(AttackId a)
        {
            switch (a)
            {
                // 横薙ぎは大きく開く。突きは正面を向いたまま（開くと突きに見えない）
                case AttackId.Heavy: return 40f;
                case AttackId.Break: return 52f;
                default: return 10f;
            }
        }

        static float CockPitch(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -14f;
                case AttackId.Break: return 6f;
                default: return -3f;
            }
        }
        // --- 振り切った形（攻撃の種類ごとに変える）--------------------------
        // 3 種とも同じ姿勢で振り切ると、見た目が「1 種類の攻撃」にしか見えない。
        //   横薙ぎ … 体を大きく開いて横へ払う
        //   振り下ろし … 上から下へ叩き落とす（腕を低く、上体を深く前へ）
        //   突き … 肘を伸ばし切って真っ直ぐ刺す（体はあまり開かない）
        static float StrikeArm(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -78f;
                case AttackId.Break: return -84f;
                default: return -70f;
            }
        }

        static float StrikeElbow(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 12f;
                case AttackId.Break: return 2f;
                default: return 10f;
            }
        }

        static float StrikeYaw(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 12f;
                case AttackId.Break: return 8f;
                default: return 34f;
            }
        }

        static float StrikePitch(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return -8f;
                case AttackId.Break: return 16f;
                default: return 8f;
            }
        }

        /// <summary>振り切ったときに腰をどこまで前へ出すか（重い攻撃ほど踏み込む）。</summary>
        static float StrikeHips(AttackId a)
        {
            switch (a)
            {
                case AttackId.Heavy: return 0.145f;
                case AttackId.Break: return 0.120f;
                default: return 0.100f;
            }
        }
        // --- 脚の運び ---------------------------------------------------
        /// <summary>
        /// 歩行中の足先の位置（腰の関節から見た前後と上下）。
        /// 接地中は「体が進んだ分だけ後ろへ下がる」ので、地面の上では止まって見える。
        /// </summary>
        static void FootPath(float u, float reach, float duty, out float sweep, out float lift)
        {
            const float ground = 0.008f;   // 接地中の足首の高さ
            if (u < duty)
            {
                // 接地。前から後ろへ一定の速さで流れる
                sweep = Mathf.Lerp(reach, -reach, u / duty);
                lift = ground;
            }
            else
            {
                // 遊脚。弧を描いて前へ戻る
                float a = (u - duty) / (1f - duty);
                sweep = Mathf.Lerp(-reach, reach, Mathf.SmoothStep(0f, 1f, a));
                lift = ground + Mathf.Sin(Mathf.PI * a) * 0.10f;
            }
        }
        // --- 組み立て --------------------------------------------------
    }
}
