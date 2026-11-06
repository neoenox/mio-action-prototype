namespace Ryona
{
    /// <summary>
    /// 1人の戦闘リソース。HP / 部位ダメージ / ひるみ（ダウンゲージ）をまとめて持つ。
    /// 仕様書 §4.1・§4.2。
    /// </summary>
    public sealed class CombatState
    {
        public float Hp { get; private set; }
        public BodyDamage Body { get; } = new BodyDamage();

        /// <summary>
        /// ひるみ（ダウンゲージ）0〜<see cref="CombatConstants.MaxFlinch"/>。
        /// 被弾で溜まり、<see cref="CombatConstants.FlinchDecayDelay"/> 秒攻撃を受けないと減る。
        /// 上限に達するとダウンする（ゲーム側の状態機械が <see cref="PlayerStats"/> で倒す）。
        /// </summary>
        public float Flinch { get; private set; }

        /// <summary>最後にひるみが増えてからの経過秒（減衰の待ち時間に使う）。</summary>
        float _flinchIdle;

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
        }

        public bool IsAlive => Hp > 0f;

        /// <summary>移動速度倍率（脚の損傷）。</summary>
        public float MoveSpeedMultiplier => Body.MoveSpeedMultiplier;

        /// <summary>時間経過でのひるみの減衰。</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;

            // ひるみは最後の被弾からしばらく減らない。殴られ続けている間は溜まり続け、
            // 手が止まると抜けていく（＝「一方的に殴られ続けると倒れる」）。
            _flinchIdle += deltaSeconds;
            if (_flinchIdle >= CombatConstants.FlinchDecayDelay && Flinch > 0f)
            {
                Flinch = Clamp(
                    Flinch - CombatConstants.FlinchDecayPerSecond * deltaSeconds,
                    0f, CombatConstants.MaxFlinch);
            }
        }

        /// <summary>
        /// ひるみを加算する。上限に達したら true（＝ダウンさせる）。
        /// </summary>
        public bool AddFlinch(float amount)
        {
            if (amount <= 0f)
                return false;

            _flinchIdle = 0f;
            Flinch = Clamp(Flinch + amount, 0f, CombatConstants.MaxFlinch);
            return Flinch >= CombatConstants.MaxFlinch;
        }

        /// <summary>ひるみを 0 に戻す（ダウンしたとき）。</summary>
        public void ResetFlinch()
        {
            Flinch = 0f;
            _flinchIdle = 0f;
        }

        public void ApplyHpDamage(float amount)
        {
            if (amount <= 0f)
                return;
            Hp = Clamp(Hp - amount, 0f, MaxHp);
        }

        /// <summary>HP を回復する。</summary>
        public void HealHp(float amount)
        {
            if (amount <= 0f)
                return;
            Hp = Clamp(Hp + amount, 0f, MaxHp);
        }

        /// <summary>HP をこの値未満にしない（ゴッドモードの死亡防止）。</summary>
        public void EnsureHpAtLeast(float value)
        {
            if (Hp < value)
                Hp = Clamp(value, 0f, MaxHp);
        }

        /// <summary>チェックポイント復帰用。部位ダメージは仕様上リセットする。</summary>
        public void RestoreAll()
        {
            Hp = MaxHp;
            Body.Reset();
            IsDown = false;
            ResetFlinch();
        }

        static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);
    }
}
