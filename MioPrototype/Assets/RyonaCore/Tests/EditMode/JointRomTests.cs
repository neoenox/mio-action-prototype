using System;
using NUnit.Framework;
using Ryona;

namespace Ryona.Tests
{
    /// <summary>
    /// 可動域テーブルと判定のテスト。
    /// 角度の計算（骨の向きの測り方）は PlayMode 側の <c>JointRomRuntimeTests</c> で見る。
    /// </summary>
    public sealed class JointRomTests
    {
        [Test]
        public void 定義済みの関節はすべてテーブルにある()
        {
            foreach (JointId joint in Enum.GetValues(typeof(JointId)))
            {
                Assert.IsTrue(HumanJointLimits.IsKnown(joint), $"{joint} が可動域テーブルに無い");

                JointLimit limit = HumanJointLimits.Of(joint);
                Assert.AreEqual(joint, limit.Id, "テーブルの並びと JointId がずれている");
                Assert.IsNotEmpty(limit.Label, $"{joint} の表示名が空");
                Assert.Less(limit.Min, limit.Max, $"{joint} の可動域が逆転している");
                Assert.GreaterOrEqual(limit.SlackLow, 0f, $"{joint} の下限の余裕が負");
                Assert.GreaterOrEqual(limit.SlackHigh, 0f, $"{joint} の上限の余裕が負");
            }
        }

        [Test]
        public void 主要な関節が人の可動域になっている()
        {
            // 出典側の代表値をそのまま固定する。ここを変えるときは警告の意味も変わる。
            Assert.AreEqual(0f, HumanJointLimits.Of(JointId.Elbow).Min, 0.01f);
            Assert.AreEqual(145f, HumanJointLimits.Of(JointId.Elbow).Max, 0.01f);
            Assert.AreEqual(150f, HumanJointLimits.Of(JointId.Knee).Max, 0.01f);
            Assert.GreaterOrEqual(HumanJointLimits.Of(JointId.ShoulderFlexion).Max, 170f,
                "肩の屈曲は 180° 近くまで上がるはず");
            Assert.GreaterOrEqual(HumanJointLimits.Of(JointId.NeckYaw).Max, 70f,
                "首の回旋は 70° 以上あるはず");
            Assert.Greater(HumanJointLimits.Of(JointId.SpinePitch).Max,
                HumanJointLimits.Of(JointId.NeckPitch).Max,
                "体幹の前屈は首より深いはず");
        }

        [Test]
        public void 未定義の関節を問い合わせると例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => HumanJointLimits.Of((JointId)99));
        }

        [Test]
        public void 正常な角度は逸脱にならない()
        {
            JointRomReading reading = JointRom.Read(JointId.Elbow, JointSide.Right, 90f);

            Assert.IsFalse(reading.IsViolation);
            Assert.AreEqual(JointRomVerdict.Within, reading.Verdict);
            Assert.AreEqual(0f, reading.Excess, 0.001f);
            Assert.AreEqual(90f, reading.Degrees, 0.01f);
            Assert.AreEqual(JointSide.Right, reading.Side);
        }

        [Test]
        public void 可動域を超えた角度は曲げすぎになる()
        {
            JointLimit limit = HumanJointLimits.Of(JointId.Knee);
            JointRomReading reading = JointRom.Read(JointId.Knee, JointSide.Left, limit.WarnAbove + 5f);

            Assert.IsTrue(reading.IsViolation);
            Assert.AreEqual(JointRomVerdict.OverFlexed, reading.Verdict);
            Assert.AreEqual(5f, reading.Excess, 0.01f);
            StringAssert.Contains("ひざ", reading.Describe());
            StringAssert.Contains("左", reading.Describe());
            StringAssert.Contains("曲げすぎ", reading.Describe());
        }

        [Test]
        public void 逆方向へ曲がった角度は逆方向になる()
        {
            JointLimit limit = HumanJointLimits.Of(JointId.Elbow);
            JointRomReading reading = JointRom.Read(JointId.Elbow, JointSide.Right, limit.WarnBelow - 8f);

            Assert.IsTrue(reading.IsViolation);
            Assert.AreEqual(JointRomVerdict.ReverseBend, reading.Verdict);
            Assert.AreEqual(8f, reading.Excess, 0.01f);
            StringAssert.Contains("逆方向", reading.Describe());
        }

        [Test]
        public void 余裕の内側はまだ警告しない()
        {
            JointLimit limit = HumanJointLimits.Of(JointId.Elbow);

            // 解剖学的な上限は超えているが、余裕の内側（手続きアニメの誇張）は黙る。
            JointRomReading inside = JointRom.Read(
                JointId.Elbow, JointSide.Left, limit.Max + limit.SlackHigh - 1f);
            Assert.IsFalse(inside.IsViolation, "余裕の内側で警告している");

            JointRomReading outside = JointRom.Read(
                JointId.Elbow, JointSide.Left, limit.WarnAbove + 1f);
            Assert.IsTrue(outside.IsViolation);
        }

        [Test]
        public void 集計は関節ごとの最悪値と回数を持つ()
        {
            var tally = new JointRomTally();

            tally.Add(JointRom.Read(JointId.Knee, JointSide.Left, 150f));   // 正常
            tally.Add(JointRom.Read(JointId.Knee, JointSide.Left, 180f));
            tally.Add(JointRom.Read(JointId.Knee, JointSide.Left, 170f));
            tally.Add(JointRom.Read(JointId.Elbow, JointSide.Right, -40f));

            Assert.AreEqual(3, tally.ViolationSamples);
            Assert.AreEqual(2, tally.TrackedKeys);
            Assert.AreEqual(2, tally.CountOf(JointId.Knee, JointSide.Left));
            Assert.AreEqual(1, tally.CountOf(JointId.Elbow, JointSide.Right));

            Assert.IsTrue(tally.TryGetWorst(JointId.Knee, JointSide.Left, out JointRomReading worst));
            Assert.AreEqual(180f, worst.Degrees, 0.01f);

            Assert.IsTrue(tally.WorstOverall.HasValue);
            Assert.AreEqual(180f, tally.WorstOverall.Value.Degrees, 0.01f);

            tally.Clear();
            Assert.AreEqual(0, tally.ViolationSamples);
            Assert.AreEqual(0, tally.TrackedKeys);
            Assert.IsFalse(tally.WorstOverall.HasValue);
        }
    }
}
