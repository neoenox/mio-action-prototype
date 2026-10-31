namespace Ryona
{
    public enum GrappleKind
    {
        Front = 0,
        Back = 1,
        Pin = 2
    }

    /// <summary>
    /// 掴まれた状態の「もがき」。仕様書 §4.3 の抵抗システム。
    /// 演出ではなく入力に責任を持たせるための層。
    /// </summary>
    public sealed class GrappleResistance
    {
        readonly float _requiredInputs;

        public GrappleKind Kind { get; }
        public int InputCount { get; private set; }
        public float Progress { get; private set; }
        public bool IsEscaped => Progress >= 1f;

        public GrappleResistance(GrappleKind kind, float requiredInputs)
        {
            Kind = kind;
            _requiredInputs = requiredInputs < 1f ? 1f : requiredInputs;
        }

        /// <summary>1回のレバガチャ入力。振りほどけたら true。</summary>
        public bool RegisterInput()
        {
            if (IsEscaped)
                return true;

            InputCount++;
            Progress += 1f / _requiredInputs;

            if (Progress > 1f)
                Progress = 1f;

            return IsEscaped;
        }

        /// <summary>失敗（振りほどけなかった）ときに与える部位ダメージ量。</summary>
        public float FailurePartDamage => 18f + 6f * (int)Kind;

        /// <summary>失敗時に空になるスタミナ量。</summary>
        public float FailureStaminaCost => 30f + 10f * (int)Kind;
    }

    /// <summary>
    /// 必要入力数の算出。スキル「抵抗強化」と腕の損傷を反映する。
    /// </summary>
    public static class ResistanceMath
    {
        public static int BaseInputs(GrappleKind kind)
        {
            switch (kind)
            {
                case GrappleKind.Front: return CombatConstants.MashBaseFront;
                case GrappleKind.Back: return CombatConstants.MashBaseBack;
                case GrappleKind.Pin: return CombatConstants.MashBasePin;
                default: return CombatConstants.MashBaseFront;
            }
        }

        /// <param name="upgradeMultiplier">スキル「抵抗強化」で 0.80 を渡す。</param>
        /// <param name="armInjuryValue">腕の部位ダメージ 0..100。腕が死ぬほど逃げにくい。</param>
        public static float RequiredInputs(
            GrappleKind kind,
            float upgradeMultiplier = 1f,
            float armInjuryValue = 0f)
        {
            float t = armInjuryValue / CombatConstants.PartMax;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            float injuryPenalty = 1f + 0.25f * t;
            float required = BaseInputs(kind) * injuryPenalty * upgradeMultiplier;

            return required < 1f ? 1f : required;
        }
    }
}
