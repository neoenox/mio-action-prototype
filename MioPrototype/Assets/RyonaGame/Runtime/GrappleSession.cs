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

        /// <summary>二人がかりで入った腹パンの数（HUD 用）。</summary>
        public int SplitHits { get; set; }

        /// <summary>打つ手が左か（アシスト=右、拘束役=左）。ポーズ側が空き手を選ぶ。</summary>
        public bool SplitPunchLeft { get; set; }

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
            SplitHits = 0;
            SplitPunchLeft = false;
            SplitStarted = false;
            SplitReady = false;
            SplitAssistAge = 0f;
        }
    }
}
