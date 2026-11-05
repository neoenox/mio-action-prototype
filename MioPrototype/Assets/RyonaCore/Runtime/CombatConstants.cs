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
        /// <summary>背後からの掴み（羽交い締め）を振りほどくのに必要な入力回数。</summary>
        public const int MashBaseBack = 10;

        /// <summary>押さえつけ（仕様書 §4.3 の 3 種目。まだ実装していない）の入力回数。</summary>
        public const int MashBasePin = 12;

        /// <summary>
        /// 押さえつけ（<see cref="GrappleKind.Pin"/>）で掴まれてから投げられるまでの猶予（秒）。
        /// 掴まれてすぐ膝蹴りを始めず、しばらくそのまま押さえる（脱出する猶予）。
        /// 後ろ拘束（<see cref="GrappleKind.Back"/>）は時間切れで解けないので、この値は使わない。
        /// </summary>
        public const float GrappleLimitSeconds = 4.2f;

        /// <summary>振りほどいた直後、掴んでいた側がよろける時間（反撃の隙）。</summary>
        public const float GrappleEscapeStun = 0.70f;

        // --- 押さえつけ（Pin）の入口と膝蹴り ---------------------------
        /// <summary>押さえつけを始める間合いの下限（m）。これより近いと殴る。</summary>
        public const float PinMinDistance = 0.80f;

        /// <summary>押さえつけを始める間合いの上限（m）。</summary>
        public const float PinMaxDistance = 1.30f;

        /// <summary>前回の押さえつけから次を始めるまで空ける秒。連発で「何もできない時間」にしない。</summary>
        public const float PinCooldown = 2.5f;

        /// <summary>押さえつけ中に入れる膝蹴りの回数。</summary>
        public const int PinKneeCount = 5;

        /// <summary>押さえつけ開始から 1 発目の膝蹴りまでの秒。しばらく押さえたままにして脱出の猶予を作る。</summary>
        public const float PinKneeDelay = 1.1f;

        /// <summary>膝蹴りの間隔（秒）。タッグ腹パンと同じテンポ。</summary>
        public const float PinKneeInterval = 0.55f;

        /// <summary>膝蹴り 1 発のダメージ。</summary>
        public const float PinKneeDamage = 4.0f;

        /// <summary>
        /// 掴まれている最中に、掴んでいる相手以外から受けるダメージの倍率。
        /// 1 回の掴みで包囲側に袋叩きにされると HP が半分飛ぶため、追撃は弱める。
        /// </summary>
        public const float GrabbedDamageFromOthers = 0.5f;

        /// <summary>
        /// 押さえつけの時間切れで投げられたとき、突き飛ばされて下がる距離（m）と時間（秒）。
        /// その場に残すと、怯んでいる 0.7 秒のあいだに包囲側から袋叩きにされる。
        /// </summary>
        public const float GrappleThrowKnockback = 1.5f;
        public const float GrappleThrowKnockbackSeconds = 0.30f;

        /// <summary>
        /// 押さえつけの時間切れで投げた直後、他の敵が攻撃を控える時間（秒）。
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
