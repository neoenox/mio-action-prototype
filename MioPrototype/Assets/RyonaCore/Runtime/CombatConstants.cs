namespace Ryona
{
    /// <summary>
    /// 戦闘の数値。仕様書 SPEC.md §4 の値をそのまま持つ。
    /// ここを書き換えるだけでゲーム全体のバランスが変わる。
    /// </summary>
    public static class CombatConstants
    {
        // --- リソース -------------------------------------------------
        public const float MaxHp = 100f;
        public const float MaxStamina = 100f;
        public const float MaxPosture = 100f;
        public const float PartMax = 100f;

        public const float StaminaRegenIdle = 7.0f;
        public const float StaminaRegenMoving = 4.5f;

        /// <summary>
        /// ガード中のスタミナ回復倍率。構えている間は回復をほぼ止める。
        /// これが 1 だと「ガードを握り続けるほど得」になり、ガードブレイク
        /// （＝ダウン）が実戦でまず起きない。
        /// </summary>
        public const float GuardRegenScale = 0.15f;
        public const float PostureRegenPerSecond = 9.0f;

        // --- 部位ダメージの劣化下限（0=無傷, 100=限界） ----------------
        public const float LegsSpeedFloor = 0.70f;
        public const float ArmsDamageFloor = 0.75f;
        public const float ArmsGuardFloor = 0.50f;
        public const float TorsoRegenFloor = 0.60f;

        // --- ガード ---------------------------------------------------
        public const float GuardDamageScale = 0.25f;
        public const float GuardPostureScale = 1.5f;
        public const float GuardPartDamageScale = 0.25f;

        // --- 体勢ブレイク ---------------------------------------------
        public const float PostureBreakRecoverRatio = 0.5f;

        // --- 怯み・ダウン（秒） ---------------------------------------
        public const float StunSmall = 0.20f;
        public const float StunMedium = 0.40f;
        public const float StunLarge = 0.80f;
        public const float StunGuardBreak = 1.20f;
        public const float DownDuration = 2.00f;
        public const float GetUpVulnerable = 0.60f;

        // --- 掴み・抵抗 -----------------------------------------------
        /// <summary>正面からの掴みを振りほどくのに必要な入力回数。</summary>
        public const int MashBaseFront = 8;
        public const int MashBaseBack = 10;
        public const int MashBasePin = 12;

        /// <summary>掴まれてから投げられるまでの猶予（秒）。この間に必要回数もがく。</summary>
        public const float GrappleLimitSeconds = 3.0f;

        /// <summary>振りほどいた直後、掴んでいた側がよろける時間（反撃の隙）。</summary>
        public const float GrappleEscapeStun = 0.70f;

        /// <summary>
        /// 掴まれている最中に、掴んでいる相手以外から受けるダメージの倍率。
        /// 1 回の掴みで包囲側に袋叩きにされると HP が半分飛ぶため、追撃は弱める。
        /// </summary>
        public const float GrabbedDamageFromOthers = 0.5f;

        /// <summary>
        /// 掴みに失敗したとき、突き飛ばされて下がる距離（m）と時間（秒）。
        /// その場に残すと、怯んでいる 0.7 秒のあいだに包囲側から袋叩きにされる。
        /// </summary>
        public const float GrappleThrowKnockback = 1.5f;
        public const float GrappleThrowKnockbackSeconds = 0.30f;

        /// <summary>
        /// 掴みに失敗した直後、他の敵が攻撃を控える時間（秒）。
        /// 怯み（<see cref="StunLarge"/>）と同じ長さにして、起き上がるまでに間合いを離す。
        /// </summary>
        public const float GrappleThrowGraceSeconds = 0.70f;

        /// <summary>
        /// 怯んだ直後この秒数は、次の怯みで怯み時間を上書きしない（ダメージは受ける）。
        /// これがないと 3 人に囲まれたとき、怯みが切れ目なく続いて何もできないまま HP が溶ける。
        /// </summary>
        public const float HitStunGrace = 0.35f;


        public static float StunSeconds(StaggerTier tier)
        {
            switch (tier)
            {
                case StaggerTier.Small: return StunSmall;
                case StaggerTier.Medium: return StunMedium;
                case StaggerTier.Large: return StunLarge;
                case StaggerTier.GuardBreak: return StunGuardBreak;
                default: return 0f;
            }
        }
    }
}
