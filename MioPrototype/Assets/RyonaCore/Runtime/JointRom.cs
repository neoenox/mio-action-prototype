using System;
using System.Collections.Generic;

namespace Ryona
{
    /// <summary>
    /// 可動域チェックの対象にする関節。
    /// 左右は <see cref="JointSide"/> で分ける（体幹と首は <see cref="JointSide.Center"/>）。
    /// </summary>
    public enum JointId
    {
        /// <summary>ひじ。屈曲 0〜145°（0 = 伸展）。負の値は「逆方向へ曲がっている」。</summary>
        Elbow = 0,

        /// <summary>ひざ。屈曲 0〜150°（0 = 伸展）。負の値は「逆方向へ曲がっている」。</summary>
        Knee = 1,

        /// <summary>股関節の前後。正 = 腿を前へ上げる（屈曲）、負 = 後ろへ引く（伸展）。</summary>
        HipFlexion = 2,

        /// <summary>股関節の開き。正 = 外へ開く（外転）、負 = 内へ閉じる（内転）。左右とも外転を正にそろえる。</summary>
        HipAbduction = 3,

        /// <summary>肩の前後。正 = 腕を前へ上げる（屈曲）、負 = 後ろへ引く（伸展）。</summary>
        ShoulderFlexion = 4,

        /// <summary>肩の開き。正 = 腕を横へ上げる（外転）、負 = 体の前へ寄せる（内転）。左右とも外転を正にそろえる。</summary>
        ShoulderAbduction = 5,

        /// <summary>体幹の前後。正 = 前へ折る（屈曲）、負 = 反る（伸展）。</summary>
        SpinePitch = 6,

        /// <summary>体幹のひねり。正負で左右。</summary>
        SpineYaw = 7,

        /// <summary>体幹の側屈。正負で左右。</summary>
        SpineRoll = 8,

        /// <summary>首の前後。正 = うつむく（屈曲）、負 = 見上げる（伸展）。</summary>
        NeckPitch = 9,

        /// <summary>首のひねり。正負で左右。</summary>
        NeckYaw = 10,

        /// <summary>首の側屈。正負で左右。</summary>
        NeckRoll = 11,
    }

    /// <summary>関節の左右。体幹・首は <see cref="Center"/>。</summary>
    public enum JointSide
    {
        Center = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>可動域チェックの判定。</summary>
    public enum JointRomVerdict
    {
        /// <summary>人の可動域（＋余裕）に収まっている。</summary>
        Within = 0,

        /// <summary>曲げすぎ。可動域の上限を超えている。</summary>
        OverFlexed = 1,

        /// <summary>逆方向へ曲がっている（ひじ・ひざが人体と反対へ折れる等）。</summary>
        ReverseBend = 2
    }

    /// <summary>
    /// 1 関節ぶんの人の可動域。角度はすべて度。
    /// <para>
    /// <see cref="Min"/> / <see cref="Max"/> は解剖学的なおおよその可動域。
    /// <see cref="SlackLow"/> / <see cref="SlackHigh"/> は、そのままでは警告が
    /// 細かく出すぎるので「超えてから警告するまで」の余裕。手続きアニメの
    /// 誇張を許しつつ、人体に有り得ない角度だけを拾うための値。
    /// </para>
    /// </summary>
    public readonly struct JointLimit
    {
        public JointLimit(JointId id, string label, float min, float max, float slackLow, float slackHigh)
        {
            Id = id;
            Label = label;
            Min = min;
            Max = max;
            SlackLow = slackLow;
            SlackHigh = slackHigh;
        }

        /// <summary>どの関節か。</summary>
        public JointId Id { get; }

        /// <summary>警告に出す日本語名（例: 「ひざ」「体幹(前後)」）。</summary>
        public string Label { get; }

        /// <summary>解剖学的な可動域の下限（度）。</summary>
        public float Min { get; }

        /// <summary>解剖学的な可動域の上限（度）。</summary>
        public float Max { get; }

        /// <summary>下限側の余裕（度）。超えてからこのぶんまでは警告しない。</summary>
        public float SlackLow { get; }

        /// <summary>上限側の余裕（度）。超えてからこのぶんまでは警告しない。</summary>
        public float SlackHigh { get; }

        /// <summary>これより小さい角度で警告する（<see cref="Min"/> − <see cref="SlackLow"/>）。</summary>
        public float WarnBelow => Min - SlackLow;

        /// <summary>これより大きい角度で警告する（<see cref="Max"/> + <see cref="SlackHigh"/>）。</summary>
        public float WarnAbove => Max + SlackHigh;

        /// <summary>表示用の可動域（例: 「0〜145°」）。</summary>
        public string RangeText => $"{Min:0}〜{Max:0}°";
    }

    /// <summary>
    /// 人体の主要な関節の可動域テーブル。
    /// 出典はリハビリテーション系の一般的な ROM 値（肘 0〜145° / 膝 0〜150° /
    /// 股関節 伸展 25°・屈曲 120°・外転 50° / 肩 屈曲 180° / 体幹 前屈 80°・
    /// ひねり 45° / 首 前屈 50°・伸展 60°・回旋 80°・側屈 45°）を丸めたもの。
    /// 手続きアニメの誇張を許す余裕は <see cref="JointLimit.SlackLow"/> /
    /// <see cref="JointLimit.SlackHigh"/> に分けて持たせてある。
    /// </summary>
    public static class HumanJointLimits
    {
        /// <summary>迷ったときの余裕（度）。</summary>
        public const float StandardSlack = 10f;

        // JointId の数値がそのまま添字になるように並べる（テストで検査している）。
        static readonly JointLimit[] JointTable =
        {
            // ひじ・ひざは「0 = まっすぐ」。逆曲がりは負で来るので下限側の余裕を広めに取る
            // （まっすぐ付近のわずかな揺れを警告にしないため）。
            new JointLimit(JointId.Elbow, "ひじ", 0f, 145f, 20f, StandardSlack),
            new JointLimit(JointId.Knee, "ひざ", 0f, 150f, 20f, StandardSlack),
            new JointLimit(JointId.HipFlexion, "股関節(曲げ)", -25f, 120f, 15f, StandardSlack),
            new JointLimit(JointId.HipAbduction, "股関節(開き)", -30f, 50f, StandardSlack, 20f),
            new JointLimit(JointId.ShoulderFlexion, "肩(前後)", -70f, 180f, 25f, 15f),
            new JointLimit(JointId.ShoulderAbduction, "肩(開き)", -45f, 180f, 15f, 15f),
            // 体幹は胸を腰から見た角度。手続きアニメでは実際の脊柱の曲がりが
            // 姿勢パラメータより大きく出るので、上限は広めに取る。
            new JointLimit(JointId.SpinePitch, "体幹(前後)", -35f, 90f, 15f, 20f),
            new JointLimit(JointId.SpineYaw, "体幹(ひねり)", -50f, 50f, 20f, 20f),
            new JointLimit(JointId.SpineRoll, "体幹(左右)", -45f, 45f, 15f, 15f),
            new JointLimit(JointId.NeckPitch, "首(前後)", -60f, 50f, 15f, 15f),
            new JointLimit(JointId.NeckYaw, "首(ひねり)", -80f, 80f, 15f, 15f),
            new JointLimit(JointId.NeckRoll, "首(左右)", -45f, 45f, 15f, 15f),
        };

        /// <summary>全関節の定義（<see cref="JointId"/> の順）。</summary>
        public static IReadOnlyList<JointLimit> All => JointTable;

        /// <summary>定義されている関節の数。</summary>
        public static int Count => JointTable.Length;

        /// <summary>関節の可動域を返す。</summary>
        public static JointLimit Of(JointId joint)
        {
            int index = (int)joint;
            if (index < 0 || index >= JointTable.Length)
                throw new ArgumentOutOfRangeException(nameof(joint), joint, "未定義の関節です。");
            return JointTable[index];
        }

        /// <summary>定義済みの関節か（テーブルを増やす途中の安全確認用）。</summary>
        public static bool IsKnown(JointId joint) => (int)joint >= 0 && (int)joint < JointTable.Length;
    }

    /// <summary>
    /// 1 回ぶんの関節角度の計測結果。<see cref="JointRom.Read"/> で作る。
    /// </summary>
    public readonly struct JointRomReading
    {
        public JointRomReading(JointId joint, JointSide side, float degrees, JointLimit limit)
        {
            Joint = joint;
            Side = side;
            Degrees = degrees;
            Limit = limit;
        }

        public JointId Joint { get; }

        public JointSide Side { get; }

        /// <summary>測った角度（度）。</summary>
        public float Degrees { get; }

        /// <summary>適用した可動域。</summary>
        public JointLimit Limit { get; }

        /// <summary>人の可動域（＋余裕）を超えているか。</summary>
        public bool IsViolation => Verdict != JointRomVerdict.Within;

        /// <summary>判定。正常 / 曲げすぎ / 逆方向。</summary>
        public JointRomVerdict Verdict
        {
            get
            {
                if (Degrees > Limit.WarnAbove)
                    return JointRomVerdict.OverFlexed;
                if (Degrees < Limit.WarnBelow)
                    return JointRomVerdict.ReverseBend;
                return JointRomVerdict.Within;
            }
        }

        /// <summary>警告のしきい値をどれだけ超えたか（度）。正常なら 0。</summary>
        public float Excess
        {
            get
            {
                if (Degrees > Limit.WarnAbove)
                    return Degrees - Limit.WarnAbove;
                if (Degrees < Limit.WarnBelow)
                    return Limit.WarnBelow - Degrees;
                return 0f;
            }
        }

        /// <summary>ログ用の説明（例: 「右ひざ 168°（可動域 0〜150°・曲げすぎ）」）。</summary>
        public string Describe()
        {
            string verdict = Verdict switch
            {
                JointRomVerdict.OverFlexed => "曲げすぎ",
                JointRomVerdict.ReverseBend => "逆方向",
                _ => "正常"
            };
            return $"{JointRom.SideName(Side)}{Limit.Label} {Degrees:0}°" +
                   $"（可動域 {Limit.RangeText}・{verdict}）";
        }
    }

    /// <summary>関節角度の読み取りと表示の共通処理。</summary>
    public static class JointRom
    {
        /// <summary>測った角度を可動域に当てはめて 1 件の結果にする。</summary>
        public static JointRomReading Read(JointId joint, JointSide side, float degrees)
            => new JointRomReading(joint, side, degrees, HumanJointLimits.Of(joint));

        /// <summary>左右の日本語名。中央は空文字。</summary>
        public static string SideName(JointSide side) => side switch
        {
            JointSide.Left => "左",
            JointSide.Right => "右",
            _ => ""
        };

        /// <summary>判定の日本語名。</summary>
        public static string VerdictName(JointRomVerdict verdict) => verdict switch
        {
            JointRomVerdict.OverFlexed => "曲げすぎ",
            JointRomVerdict.ReverseBend => "逆方向",
            _ => "正常"
        };
    }

    /// <summary>
    /// 関節ごとの「最悪値」と逸脱回数を貯める集計。
    /// ログを 1 行にまとめるためと、テストで「どの関節が何度まで行ったか」を
    /// 確かめるために使う。1 フレームに何十件出てもここでは関節ごとに 1 件に畳む。
    /// </summary>
    public sealed class JointRomTally
    {
        readonly Dictionary<int, JointRomReading> _worst = new Dictionary<int, JointRomReading>();
        readonly Dictionary<int, int> _counts = new Dictionary<int, int>();

        /// <summary>逸脱した計測の延べ数。</summary>
        public int ViolationSamples { get; private set; }

        /// <summary>逸脱した（関節, 左右）の種類数。</summary>
        public int TrackedKeys => _worst.Count;

        /// <summary>関節ごとの最悪値。</summary>
        public IReadOnlyCollection<JointRomReading> WorstReadings => _worst.Values;

        /// <summary>いちばん大きく超えた 1 件。</summary>
        public JointRomReading? WorstOverall { get; private set; }

        public void Add(JointRomReading reading)
        {
            if (!reading.IsViolation)
                return;

            ViolationSamples++;

            int key = Key(reading.Joint, reading.Side);
            _counts.TryGetValue(key, out int count);
            _counts[key] = count + 1;

            if (!_worst.TryGetValue(key, out JointRomReading worst) || reading.Excess > worst.Excess)
                _worst[key] = reading;

            if (!WorstOverall.HasValue || reading.Excess > WorstOverall.Value.Excess)
                WorstOverall = reading;
        }

        /// <summary>この関節が逸脱した延べ回数。</summary>
        public int CountOf(JointId joint, JointSide side)
            => _counts.TryGetValue(Key(joint, side), out int count) ? count : 0;

        /// <summary>この関節の最悪値を返す（逸脱していなければ false）。</summary>
        public bool TryGetWorst(JointId joint, JointSide side, out JointRomReading reading)
            => _worst.TryGetValue(Key(joint, side), out reading);

        public void Clear()
        {
            _worst.Clear();
            _counts.Clear();
            ViolationSamples = 0;
            WorstOverall = null;
        }

        static int Key(JointId joint, JointSide side) => (int)joint * 3 + (int)side;
    }
}
