using System;
using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// 体の重なり判定（カプセル距離・許容余裕・集計）のテスト。
    /// 実際の骨格からの計測と警告は PlayMode 側の <c>BodyOverlapRuntimeTests</c> で見る。
    /// </summary>
    public sealed class BodyOverlapTests
    {
        [Test]
        public void 定義済みの部位はすべて名前と半径がある()
        {
            foreach (OverlapPart part in Enum.GetValues(typeof(OverlapPart)))
            {
                string label = BodyPartSpecs.LabelOf(part);
                float radius = BodyPartSpecs.RadiusForHeight(part, 1.6f);
                Assert.IsNotEmpty(label, $"{part} の表示名が空");
                Assert.Greater(radius, 0.01f, $"{part} の半径が小さすぎる");
            }
        }

        [Test]
        public void 平行な線分の最短距離を正しく測る()
        {
            var a = new BodyVec3(0f, 0f, 0f);
            var b = new BodyVec3(0f, 1f, 0f);
            var c = new BodyVec3(0.3f, 0.2f, 0f);
            var d = new BodyVec3(0.3f, 1.2f, 0f);

            float dist = BodyOverlap.SegmentSegmentDistance(a, b, c, d);
            Assert.AreEqual(0.3f, dist, 1e-4f);
        }

        [Test]
        public void ねじれの位置の線分同士の最短距離を正しく測る()
        {
            // X 軸に沿った線分 (y=0, z=0) と Z 軸に沿った線分 (x=0, y=0.25)
            var a = new BodyVec3(-1f, 0f, 0f);
            var b = new BodyVec3(1f, 0f, 0f);
            var c = new BodyVec3(0f, 0.25f, -1f);
            var d = new BodyVec3(0f, 0.25f, 1f);

            float dist = BodyOverlap.SegmentSegmentDistance(a, b, c, d);
            Assert.AreEqual(0.25f, dist, 1e-4f);
        }

        [Test]
        public void 長さゼロの点カプセル同士でも距離を測れる()
        {
            var p1 = new BodyVec3(0f, 0f, 0f);
            var p2 = new BodyVec3(0.3f, 0.4f, 0f);

            float dist = BodyOverlap.SegmentSegmentDistance(p1, p1, p2, p2);
            Assert.AreEqual(0.5f, dist, 1e-4f);
        }

        [Test]
        public void 離れていれば重なり違反にならない()
        {
            var capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0f, 0f, 0f), new BodyVec3(0f, 0.4f, 0f), 0.10f);
            var capB = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.5f, 0f, 0f), new BodyVec3(0.5f, 0.4f, 0f), 0.10f);

            BodyOverlapReading reading = BodyOverlap.Measure("プレイヤー", capA, "敵1", capB);
            Assert.IsFalse(reading.IsViolation);
            Assert.Less(reading.PenetrationMeters, 0f);
            Assert.AreEqual(0f, reading.ExcessMeters);
        }

        [Test]
        public void 許容範囲内のわずかな接触は警告にしない()
        {
            // 半径合計 0.20m、中心距離 0.17m → めり込み 0.03m <= 許容 0.04m
            var capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0f, 0f, 0f), new BodyVec3(0f, 0.4f, 0f), 0.10f);
            var capB = new BodyCapsule(OverlapPart.ForearmR, new BodyVec3(0.17f, 0f, 0f), new BodyVec3(0.17f, 0.4f, 0f), 0.10f);

            BodyOverlapReading reading = BodyOverlap.Measure("プレイヤー", capA, "敵1", capB, slackMeters: 0.04f);
            Assert.AreEqual(0.03f, reading.PenetrationMeters, 1e-4f);
            Assert.IsFalse(reading.IsViolation);
            Assert.AreEqual(0f, reading.ExcessMeters);
        }

        [Test]
        public void 許容を超えるめり込みは違反として超過量を出す()
        {
            // 半径合計 0.20m、中心距離 0.09m → めり込み 0.11m > 許容 0.04m → 超過 0.07m
            var capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0f, 0f, 0f), new BodyVec3(0f, 0.4f, 0f), 0.10f);
            var capB = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.09f, 0f, 0f), new BodyVec3(0.09f, 0.4f, 0f), 0.10f);

            BodyOverlapReading reading = BodyOverlap.Measure("プレイヤー", capA, "敵1", capB, slackMeters: 0.04f);
            Assert.IsTrue(reading.IsViolation);
            Assert.AreEqual(0.11f, reading.PenetrationMeters, 1e-4f);
            Assert.AreEqual(0.07f, reading.ExcessMeters, 1e-4f);
            StringAssert.Contains("プレイヤーの胴", reading.Describe());
            StringAssert.Contains("敵1の胴", reading.Describe());
        }

        [Test]
        public void 集計器はペアごとの最悪値と回数を記録する()
        {
            var tally = new BodyOverlapTally();
            var capA = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0f, 0f, 0f), new BodyVec3(0f, 0.4f, 0f), 0.10f);
            var capFar = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.5f, 0f, 0f), new BodyVec3(0.5f, 0.4f, 0f), 0.10f);
            var capNear = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.10f, 0f, 0f), new BodyVec3(0.10f, 0.4f, 0f), 0.10f);
            var capDeep = new BodyCapsule(OverlapPart.Torso, new BodyVec3(0.02f, 0f, 0f), new BodyVec3(0.02f, 0.4f, 0f), 0.10f);

            tally.Add(BodyOverlap.Measure("プレイヤー", capA, "敵1", capFar));
            Assert.AreEqual(0, tally.ViolationSamples);

            BodyOverlapReading mild = BodyOverlap.Measure("プレイヤー", capA, "敵1", capNear);
            BodyOverlapReading deep = BodyOverlap.Measure("プレイヤー", capA, "敵1", capDeep);
            tally.Add(mild);
            tally.Add(deep);

            Assert.AreEqual(2, tally.ViolationSamples);
            Assert.AreEqual(1, tally.TrackedPairs);
            Assert.AreEqual(2, tally.CountOf(mild));
            Assert.IsTrue(tally.WorstOverall.HasValue);
            Assert.AreEqual(deep.ExcessMeters, tally.WorstOverall.Value.ExcessMeters, 1e-4f);

            tally.Clear();
            Assert.AreEqual(0, tally.ViolationSamples);
            Assert.AreEqual(0, tally.TrackedPairs);
        }
    }
}
