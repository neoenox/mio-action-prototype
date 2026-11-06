using System;

namespace Ryona
{
    public enum AttackKind
    {
        Light = 0,
        Heavy = 1,
        Break = 2,
        Grapple = 3
    }

    /// <summary>
    /// 1つの攻撃の定義。フレームデータは 60fps 基準で秒に換算して持つ。
    /// </summary>
    [Serializable]
    public sealed class AttackData
    {
        public string id = "light";
        public AttackKind kind = AttackKind.Light;
        public float damage = 8f;
        public float partDamage = 6f;
        public BodyPart targetPart = BodyPart.Torso;
        public float startupSeconds = 0.05f;
        public float activeSeconds = 0.08f;
        public float recoverySeconds = 0.18f;

        /// <summary>
        /// 踏み込み。発生の瞬間までに root が前へ出る距離（メートル）。
        /// 当たり判定は root 基準なので、この分だけ間合いが伸びる。
        /// </summary>
        public float lungeDistance = 0.05f;

        /// <summary>崩し攻撃はガードを無視する（仕様書 §4.5）。</summary>
        public bool IsUnblockable => kind == AttackKind.Break;

        /// <summary>
        /// ひるみ値。プレイヤーのダウンゲージ（<see cref="CombatState.Flinch"/>）に加算される。
        /// 0 のままなら <see cref="Flinch"/> が <see cref="AttackKind"/> から既定値を出す
        /// （押さえつけの膝のように、その場で組む攻撃を書き漏らさないため）。
        /// </summary>
        public float flinch = 0f;

        /// <summary>この攻撃が与えるひるみ値。100 でダウン（<see cref="CombatConstants.MaxFlinch"/>）。</summary>
        public float Flinch => flinch > 0f ? flinch : DefaultFlinch(kind);

        static float DefaultFlinch(AttackKind kind)
        {
            switch (kind)
            {
                case AttackKind.Light: return 12f;
                case AttackKind.Heavy: return 26f;
                case AttackKind.Break: return 34f;
                case AttackKind.Grapple: return 6f;
                default: return 0f;
            }
        }

        public float TotalSeconds => startupSeconds + activeSeconds + recoverySeconds;
    }

    /// <summary>
    /// SPEC §4.5 の攻撃テーブル。3F / 9F / 15F の発生を秒に直してある。
    /// </summary>
    public static class AttackLibrary
    {
        public static AttackData Light() => new AttackData
        {
            id = "light",
            kind = AttackKind.Light,
            damage = 8f,
            partDamage = 6f,
            targetPart = BodyPart.Torso,
            startupSeconds = 6f / 60f,
            activeSeconds = 4f / 60f,
            recoverySeconds = 10f / 60f,
            lungeDistance = 0.38f
        };

        public static AttackData Heavy() => new AttackData
        {
            id = "heavy",
            kind = AttackKind.Heavy,
            damage = 18f,
            partDamage = 14f,
            targetPart = BodyPart.Torso,
            startupSeconds = 12f / 60f,
            activeSeconds = 5f / 60f,
            recoverySeconds = 18f / 60f,
            lungeDistance = 0.42f
        };

        public static AttackData BreakAttack() => new AttackData
        {
            id = "break",
            kind = AttackKind.Break,
            damage = 14f,
            partDamage = 10f,
            targetPart = BodyPart.Legs,
            startupSeconds = 18f / 60f,
            activeSeconds = 5f / 60f,
            recoverySeconds = 24f / 60f,
            lungeDistance = 0.34f
        };

        /// <summary>
        /// 掴み。ダメージはほぼ無く、当たった後はレバガチャ（GrappleResistance）に移る。
        /// ガードは不可能ではないが、ガード越しでも掴まれる（仕様書 §4.3）。
        /// </summary>
        public static AttackData GrappleAttack() => new AttackData
        {
            id = "grapple",
            kind = AttackKind.Grapple,
            damage = 3f,
            partDamage = 0f,
            targetPart = BodyPart.Arms,
            startupSeconds = 33f / 60f,
            activeSeconds = 5f / 60f,
            recoverySeconds = 30f / 60f,
            lungeDistance = 0.10f
        };
    }
}
