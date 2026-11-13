using UnityEngine;

namespace Ryona.Game
{
    /// <summary>壁拘束の段階。掴み → せり上がり → 叩きつけ → … → 手首固定 → 投げ捨て。</summary>
    public enum WallStage
    {
        None,
        Grab,
        Rise,
        Throw,
        Slam,
        Lean,
        Regrip,
        Restrain,
        Held,
        TossWindup,
        TossAir,
        TossLand
    }

    /// <summary>
    /// 1 回の拘束の状態。**誰が・どの種類で・どの段階か**を 1 か所に持つ。
    ///
    /// 以前はこの情報が <see cref="PlayerGrapple"/> の私有フィールドと
    /// <see cref="EnemyBrain"/> の `_wallAssistHold` に分かれていて、
    /// 「二体がかりの今の段階」を出すのに両方を見る必要があった。
    /// ここへ集めることで、カメラ・ポーズ・HUD は <see cref="PlayerGrapple.Session"/> だけを見ればよい。
    ///
    /// まだ移していないもの（次の段階で移す）:
    /// 振りほどきの入力数・制限時間（<see cref="PlayerGrapple"/> 側の `_remaining` 等）。
    /// </summary>
    internal sealed class GrappleSession
    {
        /// <summary>掴み方。引き渡しで変わる（背中・押さえつけ・ベアハッグ）。</summary>
        public GrappleKind Kind { get; set; } = GrappleKind.Back;

        /// <summary>拘束している敵。引き渡しで入れ替わる。</summary>
        public EnemyBrain Captor { get; set; }

        /// <summary>拘束の残り秒（時間切れで解ける種類だけ意味がある）。</summary>
        public float LimitRemaining { get; set; }

        /// <summary>掴まれてからの経過秒（正面拘束の猶予に使う）。</summary>
        public float HeldSeconds { get; set; }

        /// <summary>もがき入力を抑止している残り秒（演出中）。</summary>
        public float SuppressRemaining { get; set; }

        /// <summary>壁押さえつけ中か。壁の外へ押し出されている間だけ true。</summary>
        public bool IsWall { get; set; }

        /// <summary>
        /// 壁拘束の段階進行（膝 → 猶予 → 二人がかり → 猶予 → 解放）が動いているか。
        /// 動いている間は、制限時間が尽きても時間切れで投げ捨てにしない（進行が自分で進める）。
        /// </summary>
        public bool WallSequenceActive { get; set; }

        /// <summary>段階の間の猶予中か。このとき <see cref="LimitRemaining"/> が猶予の残り秒になる。</summary>
        public bool WallPauseActive { get; set; }

        /// <summary>壁拘束の段階。<see cref="WallStage.None"/> なら壁ではない。</summary>
        public WallStage Stage { get; private set; }

        /// <summary>今の段階の進み具合（0..1）。</summary>
        public float StageProgress { get; private set; }

        /// <summary>今の段階に入ってからの秒数。</summary>
        public float StageElapsed { get; private set; }

        // --- 二体がかり（Y 字拘束） ---------------------------------
        /// <summary>壁で腕を掴んでいる二体目。居なければ null。</summary>
        public EnemyBrain Assist { get; set; }

        /// <summary>壁拘束が始まった時点で横に残した二体目（まだ加わっていない）。</summary>
        public EnemyBrain AssistCandidate { get; set; }

        /// <summary>いま殴る側（拘束役かアシスト）。null なら待ち。</summary>
        public EnemyBrain SplitPuncher { get; set; }

        /// <summary>いまの 1 発の経過秒（0 = 振りかぶり開始、負 = 待ち）。</summary>
        public float SplitPunchAge { get; set; } = -1f;

        /// <summary>
        /// いまの 1 発の振りかぶり秒数。段階（序盤→締め）で変わるので、
        /// 姿勢側が「溜めきる瞬間・当たる瞬間」を割り出すのにここを見る。
        /// </summary>
        public float SplitWindup { get; set; } = 0.16f;

        /// <summary>二人がかりで入った腹パンの数（HUD 用）。</summary>
        public int SplitHits { get; set; }

        /// <summary>打つ手が左か（アシスト=右、拘束役=左）。ポーズ側が空き手を選ぶ。</summary>
        public bool SplitPunchLeft { get; set; }

        /// <summary>いまの 1 発が締め（膝入れ）か。腕のパンチではなく膝で打つ。</summary>
        public bool SplitFinisher { get; set; }

        /// <summary>二体目が横に着いて、二人で押さえ始めたか。</summary>
        public bool SplitStarted { get; set; }

        /// <summary>二体目の位置合わせが終わったか。</summary>
        public bool SplitReady { get; set; }

        /// <summary>二体目が横へ着くまでの経過秒。</summary>
        public float SplitAssistAge { get; set; }

        /// <summary>壁の段階を進める。前の段階の進捗は捨てる。</summary>
        public void SetStage(WallStage stage)
        {
            Stage = stage;
            StageProgress = 0f;
            StageElapsed = 0f;
        }

        // --- 壁の見た目（カメラ・ポーズ・HUD が読む重み） ------------
        // 段階と進捗から決まる純粋な関数。ここに置くことで、
        // 「今どのくらい壁に押さえつけられているか」を 1 か所で説明できる。

        /// <summary>壁の段階が変わっている途中（固定前）か。</summary>
        public bool IsWallTransition => IsWall && Stage != WallStage.Held;

        /// <summary>投げ捨ての段階に入っているか。</summary>
        public bool IsWallToss => IsWall && Stage >= WallStage.TossWindup;

        /// <summary>壁に押さえつけられている強さ（0..1）。Regrip 以降は固定。</summary>
        public float WallRestraintWeight => Stage == WallStage.Restrain
            ? Mathf.SmoothStep(0f, 1f, StageProgress)
            : Stage == WallStage.Held || Stage == WallStage.TossWindup ? 1f : 0f;

        /// <summary>投げ捨てで壁から下ろされる度合い（0..1）。</summary>
        public float WallTossLower => Mathf.SmoothStep(0f, 1f, StageProgress);

        /// <summary>両手首を掴み直す度合い（0..1）。</summary>
        public float WallGripWeight => Stage == WallStage.Regrip
            ? Mathf.SmoothStep(0f, 1f, StageProgress)
            : WallRestraintWeight > 0f || Stage == WallStage.Restrain ? 1f : 0f;

        /// <summary>壁に沿ってずり落ちる度合い（0..1）。</summary>
        public float WallSlideWeight => Stage switch
        {
            WallStage.Lean => Mathf.SmoothStep(0f, 1f, StageProgress),
            WallStage.Regrip => 1f,
            WallStage.Restrain => 1f - WallRestraintWeight,
            _ => 0f
        };

        /// <summary>拘束役が壁際まで詰める距離（0..1）。</summary>
        public float WallCaptorTravel => Stage switch
        {
            WallStage.Throw => 0.55f * StageProgress,
            WallStage.Slam or WallStage.Lean => 0.55f,
            WallStage.Regrip => Mathf.Lerp(0.55f, 0.90f, WallGripWeight),
            WallStage.Restrain => Mathf.Lerp(0.90f, 1f, WallRestraintWeight),
            WallStage.Held or WallStage.TossWindup or WallStage.TossAir or WallStage.TossLand => 1f,
            _ => 0f
        };

        // --- 3 人がかり（壁拘束の最後の段階） -------------------------
        /// <summary>3 人目。居なければ null。</summary>
        public EnemyBrain Third { get; set; }

        /// <summary>3 人がかりの段階か。拘束役が片手を離し、拘束役と 3 人目が交代で殴る蹴る。</summary>
        public bool GangActive { get; set; }

        /// <summary>いま前へ入って打つ側（拘束役か 3 人目）。もう一人は横で待つ。</summary>
        public EnemyBrain GangStriker { get; set; }

        /// <summary>この段階で入った打撃の数と、予定の数（HUD 用）。</summary>
        public int GangHits { get; set; }
        public int GangTotal { get; set; }

        /// <summary>いまの 1 発が顔パンか。</summary>
        public bool SplitFace { get; set; }

        /// <summary>いまの 1 発が蹴り（脚をまっすぐ伸ばして足を腹へ入れる）か。膝入れと同じ枠で動く。</summary>
        public bool SplitKick { get; set; }

        /// <summary>二人がかりの腹パンを打ち終えたか（Y 字はまだ解かない）。</summary>
        public bool SplitPunchesDone { get; set; }

        /// <summary>拘束役が手を離して、彼女の腕が下がる見た目 0..1。</summary>
        public float GangWeight { get; private set; }

        public void TickGangWeight(float dt) =>
            GangWeight = Mathf.MoveTowards(GangWeight, GangActive ? 1f : 0f, dt / 0.35f);

        public void ClearGang()
        {
            Third = null;
            GangActive = false;
            GangStriker = null;
            GangHits = 0;
            GangTotal = 0;
            GangWeight = 0f;
        }

        // --- 二体がかりの見た目 -------------------------------------
        /// <summary>Y 字拘束の見た目 0..1（手首の開き・二人の腕の持ち方に使う）。</summary>
        public float SplitWeight { get; private set; }

        /// <summary>二体目が横に着いて、二人で押さえ始めているか。</summary>
        public bool IsSplitActive => IsWall && Stage == WallStage.Held &&
            Assist != null && SplitReady && Assist.IsAlive && Assist.isActiveAndEnabled;

        /// <summary>Y 字の見た目を目標へ寄せる（0.22 秒で切り替わる速さ）。</summary>
        public void TickSplitWeight(float dt)
        {
            SplitWeight = Mathf.MoveTowards(SplitWeight, IsSplitActive ? 1f : 0f, dt / 0.22f);
        }

        /// <summary>二人がかりの見た目を初期化する。</summary>
        public void ResetSplitWeight() => SplitWeight = 0f;


        /// <summary>今の段階の経過を進める。<paramref name="seconds"/> はその段階の長さ。</summary>
        public void AdvanceStage(float deltaSeconds, float seconds)
        {
            StageElapsed += deltaSeconds;
            StageProgress = seconds > 0f ? Mathf.Clamp01(StageElapsed / seconds) : 1f;
        }

        /// <summary>壁拘束が終わった（<see cref="PlayerGrapple"/> の EndWallPin 相当）。</summary>
        public void ClearWall()
        {
            IsWall = false;
            WallSequenceActive = false;
            WallPauseActive = false;
            Stage = WallStage.None;
            StageProgress = 0f;
            StageElapsed = 0f;
        }

        /// <summary>二人がかりが終わった（EndWallSplit 相当）。腹パンの数も戻す。</summary>
        public void ClearSplit()
        {
            Assist = null;
            AssistCandidate = null;
            SplitPuncher = null;
            SplitPunchAge = -1f;
            SplitWindup = 0.16f;
            SplitHits = 0;
            SplitPunchLeft = false;
            SplitFinisher = false;
            SplitFace = false;
            SplitKick = false;
            SplitPunchesDone = false;
            ClearGang();
            SplitStarted = false;
            SplitReady = false;
            SplitAssistAge = 0f;
            SplitWeight = 0f;
        }
    }
}
