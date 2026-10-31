using System;

namespace Ryona
{
    /// <summary>
    /// 部位ごとの蓄積ダメージ。0 = 無傷 / 100 = 限界。
    /// ステージ内では自然回復しない（仕様書 §4.2）。
    /// 劣化は閾値ではなく連続的に効かせることで、
    /// 「気づいたら脚が重い」という手触りを作る。
    /// </summary>
    [Serializable]
    public sealed class BodyDamage
    {
        readonly float[] _values = new float[3];

        public float this[BodyPart part]
        {
            get => _values[(int)part];
            private set => _values[(int)part] = ClampPart(value);
        }

        public void Apply(BodyPart part, float amount)
        {
            if (amount <= 0f)
                return;
            this[part] = _values[(int)part] + amount;
        }

        public void Reset()
        {
            for (int i = 0; i < _values.Length; i++)
                _values[i] = 0f;
        }

        public float Total => _values[0] + _values[1] + _values[2];

        /// <summary>脚の損傷による移動速度倍率（1.0 → 0.70）。</summary>
        public float MoveSpeedMultiplier =>
            Lerp(1f, CombatConstants.LegsSpeedFloor, this[BodyPart.Legs] / CombatConstants.PartMax);

        /// <summary>腕の損傷による与ダメージ倍率（1.0 → 0.75）。</summary>
        public float AttackDamageMultiplier =>
            Lerp(1f, CombatConstants.ArmsDamageFloor, this[BodyPart.Arms] / CombatConstants.PartMax);

        /// <summary>腕の損傷によるガード耐久倍率（1.0 → 0.50）。</summary>
        public float GuardDurabilityMultiplier =>
            Lerp(1f, CombatConstants.ArmsGuardFloor, this[BodyPart.Arms] / CombatConstants.PartMax);

        /// <summary>胴の損傷によるスタミナ回復倍率（1.0 → 0.60）。</summary>
        public float StaminaRegenMultiplier =>
            Lerp(1f, CombatConstants.TorsoRegenFloor, this[BodyPart.Torso] / CombatConstants.PartMax);

        static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float ClampPart(float v) =>
            v < 0f ? 0f : (v > CombatConstants.PartMax ? CombatConstants.PartMax : v);
    }
}
