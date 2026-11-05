namespace Ryona
{
    /// <summary>
    /// 掴まれ方。正面からの「腕を回す掴み」は作らない（はさみ込む形が取れないため、背後からのみ成立する）。
    /// <see cref="Pin"/>（押さえつけ）は仕様書 §4.3 の 3 種目。正面から肩を押さえる形なので成立する。
    /// </summary>
    public enum GrappleKind
    {
        Back = 0,
        Pin = 1
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

        /// <summary>
        /// 必要回数ぶんもがけたか。
        /// 進捗の足し込み（1/必要数 の累積）で判定すると、12 回必要でも
        /// 浮動小数の誤差で 0.9999… になり 13 回目まで逃げられない。回数で数える。
        /// </summary>
        public bool IsEscaped => InputCount >= (int)System.Math.Ceiling(_requiredInputs);

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
            Progress = InputCount / _requiredInputs;
            if (Progress > 1f)
                Progress = 1f;

            return IsEscaped;
        }

        /// <summary>
        /// 失敗（振りほどけなかった）ときに与える部位ダメージ量。
        /// 値は種類ごとに直書きする（enum の番号に依存させると、種類を増減したときに
        /// 黙ってバランスが変わる。以前 Front=0 を消したときに実際に変わった）。
        /// </summary>
        public float FailurePartDamage => Kind == GrappleKind.Pin ? 30f : 24f;

        /// <summary>失敗時に空になるスタミナ量。値は種類ごとに直書き（上と同じ理由）。</summary>
        public float FailureStaminaCost => Kind == GrappleKind.Pin ? 50f : 40f;
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
                case GrappleKind.Pin: return CombatConstants.MashBasePin;
                default: return CombatConstants.MashBaseBack;
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
