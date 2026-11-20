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

        // --- 体の寸法と、拘束中の間合い（m） ---------------------------
        /// <summary>敵のカプセルの半径。体が重ならない下限の計算に使う。</summary>
        public const float EnemyCapsuleRadius = 0.40f;

        /// <summary>彼女の体の半径（肩幅の半分より少し小さい）。</summary>
        public const float PlayerBodyRadius = 0.30f;

        /// <summary>
        /// 互いの体が重ならない最小の間合い（半径の和）。
        /// 押さえつけ・ベアハッグで彼女を立たせる位置の基準になる。
        /// </summary>
        public const float BodyGap = EnemyCapsuleRadius + PlayerBodyRadius;

        /// <summary>
        /// 押さえつけ・ベアハッグで彼女を立たせる、拘束者からの距離（m）。
        /// 近すぎると敵が <see cref="PinMinDistance"/> 側へ押し戻され、伸ばした腕が宙に浮く。
        /// </summary>
        public const float GrabStandoff = 0.60f;

        /// <summary>ベアハッグの基準距離（離れ気味のとき）。締め付けでここから 0.16m 詰めて密着する。</summary>
        public const float BearHugStandoff = 0.54f;

        /// <summary>
        /// 羽交い締めで立たせる距離（m）。胸を相手の背中に付けて組む。
        /// 離れていると腕が伸びきって「首へ手を伸ばしている」画になる。
        /// </summary>
        public const float BackGrabStandoff = 0.34f;

        // --- 掴み・抵抗 -----------------------------------------------
        /// <summary>背後からの掴み（羽交い締め）を振りほどくのに必要な入力回数。</summary>
        public const int MashBaseBack = 10;

        /// <summary>押さえつけ（仕様書 §4.3 の 3 種目）の入力回数。</summary>
        public const int MashBasePin = 12;

        /// <summary>ベアハッグを振りほどくのに必要な入力回数。</summary>
        public const int MashBaseBearHug = 12;

        // 足取り（<see cref="GrappleKind.AnkleGrab"/>）の値はここには置かない。
        // もがき回数・制限時間・間合い・発生率は RyonaConfig.Grapple
        // （MioPrototype/Assets/RyonaGame/Resources/RyonaConfig.asset）が真実源。
        // 必要入力数だけは <see cref="ResistanceMath.AnkleMashInputs"/> 経由で渡る。

        /// <summary>
        /// 押さえつけ（<see cref="GrappleKind.Pin"/>）で掴まれてから解放されるまでの猶予（秒）。
        /// 掴まれてすぐ膝蹴りを始めず、しばらくそのまま押さえる（脱出する猶予）。
        /// 後ろ拘束（<see cref="GrappleKind.Back"/>）は時間切れで解けないので、この値は使わない。
        /// </summary>
        public const float GrappleLimitSeconds = FrontGrappleEscapeSeconds + 3.1f;

        /// <summary>正面拘束で、膝蹴りや締め付けが始まる前にもがける秒数。</summary>
        public const float FrontGrappleEscapeSeconds = 2.7f;

        public const float BearHugLimitSeconds = FrontGrappleEscapeSeconds + 3.6f;

        /// <summary>振りほどいた直後、掴んでいた側がよろける時間（反撃の隙）。</summary>
        public const float GrappleEscapeStun = 0.70f;

        // --- 押さえつけ（Pin）の入口と膝蹴り ---------------------------
        /// <summary>正面の掴み（Pin / BearHug）を始める間合いの下限（m）。これより近いと殴る。</summary>
        public const float PinMinDistance = 0.80f;

        /// <summary>正面の掴み（Pin / BearHug）を始める間合いの上限（m）。</summary>
        public const float PinMaxDistance = 1.30f;

        /// <summary>前回の押さえつけから次を始めるまで空ける秒。連発で「何もできない時間」にしない。</summary>
        public const float PinCooldown = 2.5f;

        /// <summary>押さえつけ中に入れる膝蹴りの回数。</summary>
        public const int PinKneeCount = 5;

        /// <summary>膝蹴りの間隔（秒）。一発ずつ間を取り、5 発目が制限時間内に収まるようにする。</summary>
        public const float PinKneeInterval = 0.65f;

        /// <summary>膝蹴り 1 発のダメージ。</summary>
        public const float PinKneeDamage = 4.0f;

        // --- ベアハッグ（正面から胴を抱えて締める） ---------------------
        /// <summary>正面の掴みのうち、ベアハッグを選ぶ割合。</summary>
        public const float BearHugChance = 0.5f;

        /// <summary>ベアハッグ中に締め付ける間隔と 1 回のダメージ。</summary>
        public const float BearHugSqueezeInterval = 0.65f;
        public const float BearHugSqueezeDamage = 2.0f;

        /// <summary>締め付けで加算するひるみ（ダウンはさせない程度）。</summary>
        public const float BearHugSqueezeFlinch = 3.0f;

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

        /// <summary>通常のひるみとは別に、拘束中の腹パンと膝蹴りだけで溜まる値の上限。</summary>
        public const float MaxHeldBellyDamage = 100f;
        public const float HeldBellyPunchDamage = 16f;
        public const float HeldKneeBellyDamage = 12f;

        // --- 壁・二体がかり（Y字拘束） -------------------------------
        /// <summary>二人がかりのときの腹パンの回数（左右の敵が 3 発ずつ交互）。</summary>
        public const int WallSplitPunchCount = 6;

        /// <summary>腹パンの間隔（秒）。1 発は「振りかぶり 0.22 + 戻り 0.33」。</summary>
        public const float WallSplitPunchInterval = 0.55f;

        /// <summary>腹パン 1 発の HP ダメージ。膝 5 発の代わりに 6 発で近い総量にする。</summary>
        public const float WallSplitPunchDamage = 4f;

        /// <summary>腹パン 1 発で溜まる腹ダメージ（膝 1 発 = 12 の代わり）。</summary>
        public const float WallSplitPunchBelly = 10f;

        /// <summary>序盤（軽い腹パンを速いテンポで交互に）の発数。</summary>
        public const int WallSplitOpeningHits = 3;

        /// <summary>締めの 1 発（最後の重い一撃）を除いた、中盤の終わりまでの発数。</summary>
        public const int WallSplitBuildHits = WallSplitPunchCount - 1;

        /// <summary>
        /// 腹パン i 発目の段階別の値。序盤は軽く速く、中盤は標準、最後の 1 発だけ重く遅い。
        /// 同じ殴りの繰り返しに見えないよう、時間経過で緩急をつける。
        /// </summary>
        public static float WallSplitHitInterval(int index) =>
            index < WallSplitOpeningHits ? 0.50f
            : index < WallSplitBuildHits ? 0.65f
            : 0.95f;

        /// <summary>振りかぶりの秒数。締めは大きく溜める。</summary>
        public static float WallSplitHitWindup(int index) =>
            index < WallSplitOpeningHits ? 0.22f
            : index < WallSplitBuildHits ? 0.26f
            : 0.45f;

        /// <summary>ダメージ・腹ダメージの倍率。締めは約 2 倍。</summary>
        public static float WallSplitHitScale(int index) =>
            index < WallSplitOpeningHits ? 0.75f
            : index < WallSplitBuildHits ? 1.0f
            : 2.0f;

        /// <summary>被弾姿勢の重さ（折れの深さ・沈み・揺れの倍率）。序盤は浅く、締めは深く崩れる。</summary>
        public static float WallSplitHitImpact(int index) =>
            index < WallSplitOpeningHits ? 0.85f
            : index < WallSplitBuildHits ? 1.1f
            : 1.7f;

        /// <summary>カメラの寄り（0..1）。序盤は引き、中盤で寄り始め、締めで大きく寄る。</summary>
        public static float WallSplitCameraPush(int index) =>
            index < WallSplitOpeningHits ? 0f
            : index < WallSplitBuildHits ? 0.10f
            : 0.15f;

        /// <summary>締めの 1 発か。</summary>
        public static bool IsWallSplitFinisher(int index) => index >= WallSplitBuildHits;

        /// <summary>壁拘束の段階の間（膝のあと・二人がかりのあと）に、連打で逃げられる猶予（秒）。</summary>
        public const float WallPauseSeconds = 1.5f;

        /// <summary>膝蹴りのあとの猶予で、脱出に必要な連打数。</summary>
        public const int WallPauseInputsAfterKnees = 10;

        /// <summary>二人がかりのあとの猶予で、脱出に必要な連打数（段階が進むほど重い）。</summary>
        public const int WallPauseInputsAfterSplit = 14;

        // --- 3 人がかり（壁拘束の最後の段階） --------------------------------------------
        // 二人がかりの腹パンの最後で、彼女は壁に寄りかかってグロッキーになる。誰も彼女を持たない
        // （抵抗もできない）。そこを 3 人（アシスト・拘束役・3 人目）が順番に殴る蹴る。

        /// <summary>3 人が順番に打つ回数（3 の倍数。休む暇なく続ける）。</summary>
        public const int WallGangStrikes = 15;

        /// <summary>
        /// 序盤の打撃数。ここまでは「速く・軽く」で畳みかける。
        /// 以前は 15 発すべて同じ間隔（0.50 秒固定）で、同じ 3 連が 5 回繰り返すだけだった。
        /// 二体がかり側（<see cref="WallSplitHitInterval"/> 等）と同じく、段階で緩急を付ける。
        /// </summary>
        public const int WallGangOpeningHits = 4;

        /// <summary>3 人がかりの段階。0 = 序盤 / 1 = 中盤 / 2 = 締め（最後の 1 発）。</summary>
        public static int WallGangStage(int index) =>
            index < WallGangOpeningHits ? 0
            : index < WallGangStrikes - 1 ? 1
            : 2;

        /// <summary>殴る・顔パンの振りかぶり（秒）。締めへ向けて溜める。</summary>
        public static float WallGangWindup(int index) => WallGangStage(index) switch
        {
            0 => 0.12f,
            1 => 0.15f,
            _ => 0.20f
        };

        /// <summary>1 発ぶんの間隔（秒）。序盤は詰め、締めは空ける。</summary>
        public static float WallGangInterval(int index) => WallGangStage(index) switch
        {
            0 => 0.34f,
            1 => 0.44f,
            _ => 0.60f
        };

        /// <summary>脚（膝・蹴り）の振りかぶり（秒）。足を上げきるのに拳より時間が要る。</summary>
        public static float WallGangLegWindup(int index) => WallGangStage(index) switch
        {
            0 => 0.26f,
            1 => 0.36f,
            _ => 0.48f
        };

        /// <summary>脚（膝・蹴り）の間隔（秒）。振り抜きから戻すまで。</summary>
        public static float WallGangLegInterval(int index) => WallGangStage(index) switch
        {
            0 => 0.52f,
            1 => 0.78f,
            _ => 1.05f
        };

        /// <summary>段階ごとの威力・揺れの倍率（種類別の倍率に掛ける）。</summary>
        public static float WallGangImpactScale(int index) => WallGangStage(index) switch
        {
            0 => 0.75f,
            1 => 1.0f,
            _ => 1.25f
        };

        /// <summary>
        /// 3 人目の持ち場（彼女の中心から +X 側へ / 前へ（カメラ側）、m）。画面右の手前の外側に立ち、蹴りを入れる。
        /// 彼女の正面に立つと彼女が隠れる。拘束役と同じ奥行きだと、蹴りの脚が拘束役の体を通って見える
        /// （実測）ので、拘束役より手前に出す。
        /// </summary>
        public const float WallGangThirdLateral = 0.70f;
        public const float WallGangThirdDepth = 0.70f;

        /// <summary>3 人がかりでの拘束役の持ち場。3 人目の蹴りの脚が通らないよう、少し奥（壁寄り）へ下がる。</summary>
        public const float WallGangCaptorLateral = 0.55f;
        public const float WallGangCaptorDepth = 0.30f;

        /// <summary>威力の倍率（二人がかりの 1 発 = 1.0）。回数が多いので、HP が尽きないよう 1 発は軽くする。</summary>
        public const float WallGangBellyScale = 0.45f;
        public const float WallGangFaceScale = 0.40f;
        public const float WallGangKneeScale = 0.8f;
        public const float WallGangFinisherScale = 1.4f;

        /// <summary>3 人がかりの間、カメラを引く量（m）。3 人が画面に収まるように。</summary>
        public const float WallGangCameraBack = 0.55f;

        /// <summary>3 人がかりの間、カメラの狙いを 3 人目の側（画面右）へ寄せる量（m）。</summary>
        public const float WallGangCameraShift = 0.30f;

        /// <summary>3 人目の蹴りの威力の倍率。</summary>
        public const float WallGangKickScale = 0.9f;

        /// <summary>二人がかりのときの脱出に必要な連打数（通常の 1.5 倍）。</summary>
        public const float WallSplitEscapeInputs = MashBasePin * 1.5f;

        /// <summary>Y 字に広げたときの手首の横位置（m、中心から）。</summary>
        public const float WallSplitHandHalf = 0.48f;

        /// <summary>二体目が彼女の横に立つ距離（m）。</summary>
        public const float WallSplitAssistGap = 0.62f;

        /// <summary>二人がかりで二人が彼女の正面側に立つときの、横のずれ（m、中心から）。</summary>
        public const float WallSplitStandGap = 0.45f;

        /// <summary>二人がかりで二人が彼女の正面側に立つときの、前への距離（m）。</summary>
        public const float WallSplitStandFront = 0.55f;

        /// <summary>締めの膝入れで、打つ側が踏み込む位置の横のずれ（m、彼女の中心から）。斜めから腹へ入る。</summary>
        public const float WallSplitKneeGap = 0.30f;

        /// <summary>締めの膝入れで、打つ側が踏み込む位置の前への距離（m）。</summary>
        public const float WallSplitKneeFront = 0.34f;

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
