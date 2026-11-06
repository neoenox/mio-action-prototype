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
        public const float PartMax = 100f;

        // --- 部位ダメージの劣化下限（0=無傷, 100=限界） ----------------
        public const float LegsSpeedFloor = 0.70f;
        public const float ArmsDamageFloor = 0.75f;

        // --- ガード ---------------------------------------------------
        public const float GuardDamageScale = 0.25f;
        public const float GuardPartDamageScale = 0.25f;

        // --- 怯み・ダウン（秒） ---------------------------------------
        public const float StunSmall = 0.20f;
        public const float StunMedium = 0.40f;
        public const float StunLarge = 0.80f;

        /// <summary>
        /// 旧・ガードブレイク用の怯み秒。スタミナ廃止に伴いゲームプレイでは発生しないが、
        /// Editor のプレビューが「大きめの怯み」として使うため値だけ残している。
        /// </summary>
        public const float StunGuardBreak = 1.20f;
        public const float DownDuration = 2.00f;
        public const float GetUpVulnerable = 0.60f;

        // --- 掴み・抵抗 -----------------------------------------------
        /// <summary>背後からの掴み（羽交い締め）を振りほどくのに必要な入力回数。</summary>
        public const int MashBaseBack = 10;

        /// <summary>押さえつけ（仕様書 §4.3 の 3 種目。まだ実装していない）の入力回数。</summary>
        public const int MashBasePin = 12;

        /// <summary>
        /// 押さえつけ（<see cref="GrappleKind.Pin"/>）で掴まれてから解放されるまでの猶予（秒）。
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

        /// <summary>膝蹴りの間隔（秒）。一発ずつ間を取り、5 発目が制限時間内に収まるようにする。</summary>
        public const float PinKneeInterval = 0.65f;

        /// <summary>膝蹴り 1 発のダメージ。</summary>
        public const float PinKneeDamage = 4.0f;

        /// <summary>
        /// 掴まれている最中に、掴んでいる相手以外から受けるダメージの倍率。
        /// 1 回の掴みで包囲側に袋叩きにされると HP が半分飛ぶため、追撃は弱める。
        /// </summary>
        public const float GrabbedDamageFromOthers = 0.5f;

        /// <summary>
        /// 押さえつけが時間切れで解放された直後、他の敵が攻撃を控える時間（秒）。
        /// 解放された瞬間に包囲側から袋叩きにされないための余白。
        /// </summary>
        public const float GrappleReleaseGraceSeconds = 0.70f;

        /// <summary>
        /// 怯んだ直後この秒数は、次の怯みで怯み時間を上書きしない（ダメージは受ける）。
        /// これがないと 3 人に囲まれたとき、怯みが切れ目なく続いて何もできないまま HP が溶ける。
        /// </summary>
        public const float HitStunGrace = 0.35f;

        // --- ひるみ（ダウンゲージ） -----------------------------------
        /// <summary>ひるみの上限。ここに達するとダウンする。</summary>
        public const float MaxFlinch = 100f;

        /// <summary>最後の被弾からこの秒数は減衰しない（殴られ続けている間は溜まり続ける）。</summary>
        public const float FlinchDecayDelay = 1.5f;

        /// <summary>減衰の速さ（1 秒あたり）。</summary>
        public const float FlinchDecayPerSecond = 10f;

        /// <summary>ガード越しの被弾で加算されるひるみの倍率。ガードしていれば完全に防げるわけではない。</summary>
        public const float BlockedFlinchScale = 0.35f;

        /// <summary>通常のひるみとは別に、拘束中の腹パンと膝蹴りだけで溜まる値の上限。</summary>
        public const float MaxHeldBellyDamage = 100f;
        public const float HeldBellyPunchDamage = 16f;
        public const float HeldKneeBellyDamage = 12f;

        /// <summary>起き上がり完了後、この秒数はダウンしない（無限ダウンの防止）。</summary>
        public const float DownImmunitySeconds = 1.0f;

        // --- ダウンの吹き飛ばし（後ろへ放物線） ---------------------
        /// <summary>
        /// ダウン時に後ろへ下がる水平距離（m）。
        /// 1.6m だと「吹き飛びすぎ」に見えた（ユーザー評）。
        /// 姿勢側でも腰が後ろへ回るので、root は 0.35m だけ動かす（二重に動かすと滑って見える）。
        /// </summary>
        public const float DownLaunchDistance = 0.35f;

        /// <summary>
        /// ダウン時に浮く高さ（m）。**低く抑える**のが大事。
        /// 0.5m 級にすると「爆発で吹き飛ばされた」ように見える。0.16m なら足を払われて
        /// 後ろへ倒れ込む高さで、着地してから背中をつける（滞空は約 0.24 秒）。
        /// </summary>
        public const float DownLaunchHeight = 0.16f;

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
