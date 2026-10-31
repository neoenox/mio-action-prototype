namespace Ryona
{
    /// <summary>
    /// 1人の戦闘リソース。HP / スタミナ / 体勢 / 部位ダメージをまとめて持つ。
    /// 仕様書 §4.1・§4.2 の三層リソース。
    /// </summary>
    public sealed class CombatState
    {
        public float Hp { get; private set; }
        public float Stamina { get; private set; }
        public float Posture { get; private set; }
        public BodyDamage Body { get; } = new BodyDamage();

        /// <summary>この個体の最大 HP。既定は <see cref="CombatConstants.MaxHp"/>。</summary>
        public float MaxHp { get; private set; } = CombatConstants.MaxHp;

        /// <summary>個体ごとに最大 HP を変える（雑魚は低く、ボスは高く）。</summary>
        public void SetMaxHp(float value)
        {
            MaxHp = value < 1f ? 1f : value;
            Hp = MaxHp;
        }

        /// <summary>ダウン中かどうか。ゲーム側の状態機械が管理する。</summary>
        public bool IsDown { get; set; }

        public CombatState()
        {
            Hp = MaxHp;
            Stamina = CombatConstants.MaxStamina;
            Posture = CombatConstants.MaxPosture;
        }

        public bool IsAlive => Hp > 0f;

        /// <summary>スタミナ切れ = ガードブレイク状態。</summary>
        public bool IsGuardBroken => Stamina <= 0f;

        /// <summary>移動速度倍率（脚の損傷）。</summary>
        public float MoveSpeedMultiplier => Body.MoveSpeedMultiplier;

        /// <summary>時間経過でのスタミナ・体勢の回復。</summary>
        public void Tick(float deltaSeconds, bool moving, bool blocking = false)
        {
            if (deltaSeconds <= 0f)
                return;

            float baseRegen = moving
                ? CombatConstants.StaminaRegenMoving
                : CombatConstants.StaminaRegenIdle;

            // 構えている間は回復しない。握り得を消して、ガードブレイクを成立させる
            if (blocking)
                baseRegen *= CombatConstants.GuardRegenScale;

            Stamina = Clamp(
                Stamina + baseRegen * Body.StaminaRegenMultiplier * deltaSeconds,
                0f,
                CombatConstants.MaxStamina);

            Posture = Clamp(
                Posture + CombatConstants.PostureRegenPerSecond * deltaSeconds,
                0f,
                CombatConstants.MaxPosture);
        }

        public void ApplyHpDamage(float amount)
        {
            if (amount <= 0f)
                return;
            Hp = Clamp(Hp - amount, 0f, MaxHp);
        }

        public void ConsumeStamina(float amount)
        {
            if (amount <= 0f)
                return;
            Stamina = Clamp(Stamina - amount, 0f, CombatConstants.MaxStamina);
        }

        public void ReducePosture(float amount)
        {
            if (amount <= 0f)
                return;
            Posture = Clamp(Posture - amount, 0f, CombatConstants.MaxPosture);
        }

        public void SetPosture(float value) =>
            Posture = Clamp(value, 0f, CombatConstants.MaxPosture);

        /// <summary>チェックポイント復帰用。部位ダメージは仕様上リセットする。</summary>
        public void RestoreAll()
        {
            Hp = MaxHp;
            Stamina = CombatConstants.MaxStamina;
            Posture = CombatConstants.MaxPosture;
            Body.Reset();
            IsDown = false;
        }

        static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);
    }
}
