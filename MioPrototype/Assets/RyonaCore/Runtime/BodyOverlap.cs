using System;
using System.Collections.Generic;

namespace Ryona
{
    /// <summary>
    /// 重なり判定の対象にする体の部位（1 キャラクターあたり 10 本のカプセル）。
    /// </summary>
    public enum OverlapPart
    {
        Torso = 0,
        Head = 1,
        UpperArmL = 2,
        UpperArmR = 3,
        ForearmL = 4,
        ForearmR = 5,
        ThighL = 6,
        ThighR = 7,
        ShinL = 8,
        ShinR = 9
    }

    /// <summary>
    /// UnityEngine 非依存の 3 次元ベクトル。<see cref="BodyOverlap"/> の幾何計算で使う。
    /// </summary>
    public readonly struct BodyVec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public BodyVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static BodyVec3 operator +(BodyVec3 a, BodyVec3 b)
            => new BodyVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static BodyVec3 operator -(BodyVec3 a, BodyVec3 b)
            => new BodyVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static BodyVec3 operator *(BodyVec3 a, float s)
            => new BodyVec3(a.X * s, a.Y * s, a.Z * s);

        public static float Dot(BodyVec3 a, BodyVec3 b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
    }

    /// <summary>
    /// 体の 1 部位を表すカプセル（線分 <see cref="Start"/>〜<see cref="End"/> と半径 <see cref="Radius"/>）。
    /// </summary>
    public readonly struct BodyCapsule
    {
        public BodyCapsule(OverlapPart part, BodyVec3 start, BodyVec3 end, float radius)
        {
            Part = part;
            Start = start;
            End = end;
            Radius = radius;
        }

        public OverlapPart Part { get; }
        public BodyVec3 Start { get; }
        public BodyVec3 End { get; }
        public float Radius { get; }
    }

    /// <summary>
    /// 部位ごとの日本語名と、身長 1.0 あたりの標準半径。
    /// </summary>
    public static class BodyPartSpecs
    {
        static readonly string[] Labels =
        {
            "胴",
            "頭",
            "左二の腕",
            "右二の腕",
            "左前腕",
            "右前腕",
            "左太もも",
            "右太もも",
            "左すね",
            "右すね"
        };

        // EnemyFigure.Build の実寸をベースに、楕円断面・肉付きを踏まえた有効半径を身長比で置く。
        static readonly float[] HeightRatios =
        {
            0.070f, // Torso（前後厚 0.063〜幅 0.106 のうち密着誤検知を避けるためやや薄め）
            0.058f, // Head
            0.028f, // UpperArmL
            0.028f, // UpperArmR
            0.024f, // ForearmL
            0.024f, // ForearmR
            0.042f, // ThighL
            0.042f, // ThighR
            0.032f, // ShinL
            0.032f  // ShinR
        };

        public static int Count => Labels.Length;

        public static string LabelOf(OverlapPart part)
        {
            int index = (int)part;
            if (index < 0 || index >= Labels.Length)
                throw new ArgumentOutOfRangeException(nameof(part), part, "未定義の部位です。");
            return Labels[index];
        }

        public static float RadiusForHeight(OverlapPart part, float heightMeters)
        {
            int index = (int)part;
            if (index < 0 || index >= HeightRatios.Length)
                throw new ArgumentOutOfRangeException(nameof(part), part, "未定義の部位です。");
            return Math.Max(0.01f, heightMeters) * HeightRatios[index];
        }
    }

    /// <summary>
    /// 2 キャラクターの部位ペア 1 組についての重なり計測結果。
    /// </summary>
    public readonly struct BodyOverlapReading
    {
        public BodyOverlapReading(
            string subjectA,
            OverlapPart partA,
            string subjectB,
            OverlapPart partB,
            float penetrationMeters,
            float slackMeters)
        {
            SubjectA = subjectA ?? "A";
            PartA = partA;
            SubjectB = subjectB ?? "B";
            PartB = partB;
            PenetrationMeters = penetrationMeters;
            SlackMeters = slackMeters;
        }

        public string SubjectA { get; }
        public OverlapPart PartA { get; }
        public string SubjectB { get; }
        public OverlapPart PartB { get; }

        /// <summary>カプセル表面同士が食い込んでいる深さ（m）。離れていれば負。</summary>
        public float PenetrationMeters { get; }

        /// <summary>接触・拘束の演出として許容するめり込みの余裕（m）。</summary>
        public float SlackMeters { get; }

        /// <summary>許容を超えた超過量（m）。違反していなければ 0。</summary>
        public float ExcessMeters => PenetrationMeters > SlackMeters ? PenetrationMeters - SlackMeters : 0f;

        /// <summary>許容を超える重なりが起きているか。</summary>
        public bool IsViolation => PenetrationMeters > SlackMeters;

        /// <summary>ログ用の説明（例: 「プレイヤーの胴 ↔ 敵1の胴 が 8.2cm 重なり（許容 4.0cm・4.2cm 超過）」）。</summary>
        public string Describe()
        {
            float penCm = PenetrationMeters * 100f;
            float slackCm = SlackMeters * 100f;
            float excessCm = ExcessMeters * 100f;
            return $"{SubjectA}の{BodyPartSpecs.LabelOf(PartA)} ↔ {SubjectB}の{BodyPartSpecs.LabelOf(PartB)} " +
                   $"が {penCm:0.0}cm 重なり（許容 {slackCm:0.0}cm・{excessCm:0.0}cm 超過）";
        }
    }

    /// <summary>
    /// 2 つのカプセル間の最短距離とめり込み量を計算する純粋幾何ロジック。
    /// </summary>
    public static class BodyOverlap
    {
        /// <summary>
        /// 既定の許容めり込み余裕（m）。
        /// 拘束や密着で表面が触れ合う程度（〜4cm）は許し、体を突き抜ける深い重なりだけを警告する。
        /// </summary>
        public const float DefaultSlackMeters = 0.04f;

        /// <summary>2 つのカプセルのめり込み量を測る。</summary>
        public static BodyOverlapReading Measure(
            string subjectA,
            BodyCapsule capsuleA,
            string subjectB,
            BodyCapsule capsuleB,
            float slackMeters = DefaultSlackMeters)
        {
            float centerDistance = SegmentSegmentDistance(
                capsuleA.Start, capsuleA.End,
                capsuleB.Start, capsuleB.End);
            float penetration = (capsuleA.Radius + capsuleB.Radius) - centerDistance;
            return new BodyOverlapReading(
                subjectA, capsuleA.Part,
                subjectB, capsuleB.Part,
                penetration, slackMeters);
        }

        /// <summary>
        /// 2 つの線分 AB と CD の最短距離（m）を返す。
        /// どちらかが点（長さ 0）に縮退していても正しく計算する。
        /// </summary>
        public static float SegmentSegmentDistance(BodyVec3 a, BodyVec3 b, BodyVec3 c, BodyVec3 d)
        {
            const float Eps = 1e-8f;
            BodyVec3 d1 = b - a;
            BodyVec3 d2 = d - c;
            BodyVec3 r = a - c;
            float len1Sq = BodyVec3.Dot(d1, d1);
            float len2Sq = BodyVec3.Dot(d2, d2);

            float s;
            float t;

            if (len1Sq <= Eps && len2Sq <= Eps)
            {
                return (float)Math.Sqrt(r.SqrMagnitude);
            }

            if (len1Sq <= Eps)
            {
                s = 0f;
                t = Clamp01(BodyVec3.Dot(d2, r) / len2Sq);
            }
            else
            {
                float cDot = BodyVec3.Dot(d1, r);
                if (len2Sq <= Eps)
                {
                    t = 0f;
                    s = Clamp01(-cDot / len1Sq);
                }
                else
                {
                    float bDot = BodyVec3.Dot(d1, d2);
                    float fDot = BodyVec3.Dot(d2, r);
                    float denom = len1Sq * len2Sq - bDot * bDot;

                    s = denom > Eps ? Clamp01((bDot * fDot - cDot * len2Sq) / denom) : 0f;
                    t = (bDot * s + fDot) / len2Sq;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Clamp01(-cDot / len1Sq);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Clamp01((bDot - cDot) / len1Sq);
                    }
                }
            }

            BodyVec3 p1 = a + d1 * s;
            BodyVec3 p2 = c + d2 * t;
            return (float)Math.Sqrt((p1 - p2).SqrMagnitude);
        }

        static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    /// <summary>
    /// 重なり違反の最悪値と延べ回数を（キャラA, 部位A, キャラB, 部位B）ごとに記録する集計器。
    /// </summary>
    public sealed class BodyOverlapTally
    {
        readonly Dictionary<string, BodyOverlapReading> _worst = new Dictionary<string, BodyOverlapReading>();
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public int TrackedPairs => _worst.Count;
        public int ViolationSamples { get; private set; }
        public IReadOnlyCollection<BodyOverlapReading> WorstReadings => _worst.Values;
        public BodyOverlapReading? WorstOverall { get; private set; }

        public void Add(BodyOverlapReading reading)
        {
            if (!reading.IsViolation)
                return;

            ViolationSamples++;
            string key = KeyOf(reading);

            if (_counts.TryGetValue(key, out int count))
                _counts[key] = count + 1;
            else
                _counts[key] = 1;

            if (!_worst.TryGetValue(key, out BodyOverlapReading worst) || reading.ExcessMeters > worst.ExcessMeters)
                _worst[key] = reading;

            if (!WorstOverall.HasValue || reading.ExcessMeters > WorstOverall.Value.ExcessMeters)
                WorstOverall = reading;
        }

        public int CountOf(BodyOverlapReading reading)
            => _counts.TryGetValue(KeyOf(reading), out int count) ? count : 0;

        public void Clear()
        {
            _worst.Clear();
            _counts.Clear();
            ViolationSamples = 0;
            WorstOverall = null;
        }

        public static string KeyOf(BodyOverlapReading reading)
            => $"{reading.SubjectA}:{(int)reading.PartA}|{reading.SubjectB}:{(int)reading.PartB}";
    }
}
