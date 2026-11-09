using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Ryona;
using Ryona.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Ryona.Tests
{
    /// <summary>
    /// 可動域チェックの実機テスト。
    ///
    /// 「プレイヤーと敵の両方にモニターが付く」「正常な立ち姿勢では警告しない」
    /// 「人為的に人体の可動域を超えさせると、その関節を警告する」を押さえる。
    /// 角度の計算そのものは <c>Ryona.Tests.JointRomTests</c>（EditMode）で見ている。
    /// </summary>
    public sealed class JointRomRuntimeTests
    {
        CharacterAnimator _anim;
        ProceduralRig _rig;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            TestScene.RestoreAudio();
        }

        IEnumerator LoadPlayer()
        {
            yield return TestScene.LoadPrototype();

            PlayerStats stats = Object.FindFirstObjectByType<PlayerStats>();
            Assert.IsNotNull(stats, "PlayerStats がシーンに無い");

            // VRM の読み込み後にリグと CharacterAnimator が付く。組み上がるまで待つ。
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _anim = stats.GetComponentInChildren<CharacterAnimator>();
                if (_anim != null && _anim.Rig != null && _anim.Rig.Ready)
                {
                    _rig = _anim.Rig;
                    break;
                }
                yield return null;
            }

            Assert.IsTrue(_rig != null, "キャラクターのリグが組み上がらない（VRM の読み込み待ちで時間切れ）");
        }

        static string Violations(JointRomMonitor monitor)
        {
            var text = new System.Text.StringBuilder();
            foreach (JointRomReading reading in monitor.Readings)
            {
                if (!reading.IsViolation)
                    continue;
                if (text.Length > 0)
                    text.Append(" / ");
                text.Append(reading.Describe());
            }
            return text.ToString();
        }

        [UnityTest]
        public IEnumerator プレイヤーと敵に可動域モニターが付き計測できる()
        {
            yield return LoadPlayer();

            Assert.IsTrue(JointRomWatch.Enabled, "Editor では可動域モニターが既定で有効なはず");

            // 見張り番は 0.5 秒ごとに走査する。プレイヤーの VRM 読み込みを待って付く。
            JointRomMonitor playerMonitor = null;
            var enemyMonitors = new List<JointRomMonitor>();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                playerMonitor = null;
                enemyMonitors.Clear();

                foreach (JointRomMonitor monitor in
                         Object.FindObjectsByType<JointRomMonitor>(FindObjectsInactive.Exclude))
                {
                    if (monitor.Subject == "プレイヤー")
                        playerMonitor = monitor;
                    else if (monitor.Figure != null)
                        enemyMonitors.Add(monitor);
                }

                if (playerMonitor != null && enemyMonitors.Count > 0)
                    break;

                yield return null;
            }

            Assert.IsNotNull(playerMonitor, "プレイヤーに可動域モニターが付かない");
            Assert.Greater(enemyMonitors.Count, 0, "敵に可動域モニターが付かない");

            Assert.IsTrue(playerMonitor.SampleNow(false), "プレイヤーの関節を計測できない");
            Assert.Greater(playerMonitor.Readings.Count, 10,
                $"計測した関節が少なすぎる（{playerMonitor.Readings.Count} 件）");

            Assert.IsTrue(enemyMonitors[0].SampleNow(false), "敵の関節を計測できない");
            Assert.Greater(enemyMonitors[0].Readings.Count, 10,
                $"敵の計測した関節が少なすぎる（{enemyMonitors[0].Readings.Count} 件）");
        }

        [UnityTest]
        public IEnumerator 正常な立ち姿勢では可動域の警告を出さない()
        {
            yield return LoadPlayer();

            var monitor = _rig.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(_rig, _anim, "テスト");
            monitor.LogWarnings = false;

            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);
            Assert.IsTrue(monitor.SampleNow(false), "立ち姿勢を計測できない");

            monitor.ResetReport();
            monitor.SampleNow(false);
            Assert.AreEqual(0, monitor.Tally.ViolationSamples,
                "正常な立ち姿勢で可動域の逸脱が出ている: " + Violations(monitor));

            Object.Destroy(monitor);
        }

        [UnityTest]
        public IEnumerator 可動域を超えた肘は警告になる()
        {
            yield return LoadPlayer();

            var monitor = _rig.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(_rig, _anim, "テスト");
            monitor.LogWarnings = false;

            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);

            // 右ひじを 180°（前腕を上腕と反対向き）まで畳む。人体では 145° を超える。
            Transform shoulder = _rig.RUpperArm;
            Transform elbow = _rig.RLowerArm;
            Transform hand = _rig.RHand;
            Vector3 upper = (elbow.position - shoulder.position).normalized;
            Vector3 fore = (hand.position - elbow.position).normalized;
            elbow.rotation = Quaternion.FromToRotation(fore, -upper) * elbow.rotation;

            monitor.ResetReport();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[JointROM\].*ひじ"));
            monitor.LogWarnings = true;
            Assert.IsTrue(monitor.SampleNow(), "測り直せない");

            Assert.Greater(monitor.WarningCount, 0, "警告が出ていない");
            Assert.IsTrue(monitor.Tally.TryGetWorst(JointId.Elbow, JointSide.Right, out JointRomReading worst),
                "右ひじの逸脱が集計されていない");
            Assert.Greater(Mathf.Abs(worst.Degrees), HumanJointLimits.Of(JointId.Elbow).WarnAbove,
                $"右ひじの角度が可動域を超えていない（{worst.Degrees:F0}°）");

            // 集計のまとめログ（-jointlog の 1 行）も出せる。
            monitor.LogSummaryNow();
            Assert.IsNotNull(monitor.LastSummary, "まとめログが作られていない");
            StringAssert.Contains("逸脱", monitor.LastSummary);
            StringAssert.Contains("ひじ", monitor.LastSummary);

            Object.Destroy(monitor);
        }

        /// <summary>左上腕を、体の (右, 上, 前) で指定した向きへ向けて測り、肩(前後)の読みを返す。</summary>
        bool TryMeasureLeftShoulderFlexion(Vector3 bodyDirection, out JointRomReading flexion,
            out JointRomMonitor monitor)
        {
            monitor = _rig.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(_rig, _anim, "テスト");
            monitor.LogWarnings = false;

            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);

            Transform shoulder = _rig.LUpperArm;
            Transform elbow = _rig.LLowerArm;
            Vector3 current = (elbow.position - shoulder.position).normalized;
            Transform body = _anim.transform.root;
            Vector3 target = body.right * bodyDirection.x + body.up * bodyDirection.y
                + body.forward * bodyDirection.z;
            shoulder.rotation = Quaternion.FromToRotation(current, target.normalized) * shoulder.rotation;

            monitor.ResetReport();
            Assert.IsTrue(monitor.SampleNow(false), "測り直せない");

            foreach (JointRomReading reading in monitor.Readings)
            {
                if (reading.Joint == JointId.ShoulderFlexion && reading.Side == JointSide.Left)
                {
                    flexion = reading;
                    return true;
                }
            }

            flexion = default;
            return false;
        }

        [UnityTest]
        public IEnumerator 真横に近い腕は肩の前後で警告しない()
        {
            yield return LoadPlayer();

            // 後ろ拘束で実測した向き。水平より約 14° 上・体の面より約 13° 後ろ。
            // 前後面への射影が短く、前後の角度は定まらない（以前は -138° と出て警告していた）。
            bool measured = TryMeasureLeftShoulderFlexion(new Vector3(-0.95f, 0.24f, -0.22f),
                out JointRomReading flexion, out JointRomMonitor monitor);
            if (measured)
                Assert.IsFalse(flexion.IsViolation, $"真横の腕が前後で警告されている（{flexion.Degrees:F0}°）");

            Object.Destroy(monitor);
        }

        [UnityTest]
        public IEnumerator 頭上へ上げた腕は折り返さず屈曲として測る()
        {
            yield return LoadPlayer();

            // うつ伏せマウントで実測した向き。真上近くへ上げて、わずかに後ろ。
            // atan2 が -180° へ折り返して「-176° の伸展」と出ていたが、実際は約 184° の屈曲。
            bool measured = TryMeasureLeftShoulderFlexion(new Vector3(-0.51f, 0.86f, -0.06f),
                out JointRomReading flexion, out JointRomMonitor monitor);
            Assert.IsTrue(measured, "頭上へ上げた腕の前後が測れていない");
            Assert.Greater(flexion.Degrees, 150f, $"折り返して伸展として測られている（{flexion.Degrees:F0}°）");
            Assert.IsFalse(flexion.IsViolation, $"可動域内の腕が警告されている（{flexion.Degrees:F0}°）");

            Object.Destroy(monitor);
        }

        [UnityTest]
        public IEnumerator 腕を後ろへ引きすぎると肩の前後で警告する()
        {
            yield return LoadPlayer();

            // 腕を体の後ろへ強く引く。前後面への射影は十分に長く、伸展の限界（約 -70°）を大きく超える。
            bool measured = TryMeasureLeftShoulderFlexion(new Vector3(-0.1f, 0.3f, -0.95f),
                out JointRomReading flexion, out JointRomMonitor monitor);
            Assert.IsTrue(measured, "後ろへ引いた腕の前後が測れていない");
            Assert.IsTrue(flexion.IsViolation, $"伸展しすぎが警告されていない（{flexion.Degrees:F0}°）");
            Assert.Less(flexion.Degrees, -70f, $"伸展として測られていない（{flexion.Degrees:F0}°）");

            Object.Destroy(monitor);
        }

        static bool ReadNeck(JointRomMonitor monitor, out float yaw, out float roll)
        {
            yaw = roll = 0f;
            bool hasYaw = false, hasRoll = false;
            foreach (JointRomReading reading in monitor.Readings)
            {
                if (reading.Joint == JointId.NeckYaw) { yaw = reading.Degrees; hasYaw = true; }
                if (reading.Joint == JointId.NeckRoll) { roll = reading.Degrees; hasRoll = true; }
            }
            return hasYaw && hasRoll;
        }

        [UnityTest]
        public IEnumerator 前へ折れた胸の上で首をひねってもひねりとして測る()
        {
            yield return LoadPlayer();

            var monitor = _rig.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(_rig, _anim, "テスト");
            monitor.LogWarnings = false;
            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);

            // 胸を前へ 80°倒し（寝ている姿勢に近い）、首をその胸の上軸まわりに 50°ひねる。
            // 首の曲がりは親（胸）の軸で測るので、ひねり 50°・前後と左右はほぼ 0 になるはず。
            // root の軸で測ると、ひねりが前後・左右の曲げに化ける。
            // 待機姿勢の頭は元からわずかにひねってあるので、回す前の値を基準にして増えた分を見る。
            Assert.IsTrue(monitor.SampleNow(false), "測り直せない");
            Assert.IsTrue(ReadNeck(monitor, out float yaw0, out float roll0),
                "首の読みが取れていない（初期姿勢が控えられていない？）");

            Transform root = _anim.transform.root;
            _rig.Chest.rotation = Quaternion.AngleAxis(80f, root.right) * _rig.Chest.rotation;
            _rig.Head.rotation = Quaternion.AngleAxis(50f, _rig.Chest.up) * _rig.Head.rotation;

            monitor.ResetReport();
            Assert.IsTrue(monitor.SampleNow(false), "測り直せない");
            Assert.IsTrue(ReadNeck(monitor, out float yaw, out float roll), "首の読みが取れていない");

            float turned = Mathf.Abs(yaw - yaw0);
            float tilted = Mathf.Abs(roll - roll0);
            Assert.AreEqual(50f, turned, 6f, $"首のひねりとして測られていない（ひねり +{turned:F0}°）");
            Assert.Less(tilted, 8f, $"ひねりが左右の曲げに化けている（左右 +{tilted:F0}°）");

            Object.Destroy(monitor);
        }

        [UnityTest]
        public IEnumerator 逆方向に曲がったひざを検出する()
        {
            yield return LoadPlayer();

            Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
            EnemyFigure figure = null;
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < deadline)
            {
                EnemyFigure[] figures = Object.FindObjectsByType<EnemyFigure>(FindObjectsInactive.Exclude);
                if (figures.Length > 0)
                {
                    figure = figures[0];
                    break;
                }
                yield return null;
            }

            Assert.IsNotNull(figure, "敵の人形がシーンに無い");

            var monitor = figure.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(figure, figure.Brain, "テスト敵");
            monitor.LogWarnings = false;
            Assert.IsTrue(monitor.SampleNow(false), "敵の関節を計測できない");

            // 左ひざを「前へ折る」（人体では逆）。ひざの曲がりは正常なら後ろ側へ折れる。
            Transform hip = figure.HipBone(true);
            Transform knee = figure.KneeBone(true);
            Transform ankle = figure.AnkleBone(true);
            Vector3 thigh = (knee.position - hip.position).normalized;
            Vector3 shin = (ankle.position - knee.position).normalized;
            Vector3 reversed = Quaternion.AngleAxis(-90f, figure.transform.right) * thigh;
            knee.rotation = Quaternion.FromToRotation(shin, reversed) * knee.rotation;

            monitor.ResetReport();
            monitor.LogWarnings = true;
            Assert.IsTrue(monitor.SampleNow(), "測り直せない");

            Assert.IsTrue(monitor.Tally.TryGetWorst(JointId.Knee, JointSide.Left, out JointRomReading worst),
                "逆方向のひざが集計されていない");
            Assert.AreEqual(JointRomVerdict.ReverseBend, worst.Verdict,
                $"逆方向として判定されていない（{worst.Degrees:F0}°）");

            Object.Destroy(monitor);
        }
    }
}
