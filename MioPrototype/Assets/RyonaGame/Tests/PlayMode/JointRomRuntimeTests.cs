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

        /// <summary>
        /// 既存の全ポーズ（待機・歩き・構え・掴まれ・攻撃・怯み・ダウン・立ち上がり）を
        /// 1 コマずつ作って測り、関節が「曲がらない向き」へ折れていないかを見る。
        ///
        /// ひじ・ひざは 0° がまっすぐで、そこから負の側へは人体では曲がらない。
        /// 手続きアニメの角度を詰めると、この向きへ折れてしまうことがある
        /// （腕を後ろへ回したとき・脚を前へ跳ね上げたとき等）。
        ///
        /// 逸脱が出たら<b>姿勢の角度を直す</b>。可動域（<see cref="HumanJointLimits"/>）の
        /// ほうを緩めない。失敗メッセージに姿勢名と角度が出るので、どこを直すかはそこから分かる。
        /// </summary>
        [UnityTest]
        public IEnumerator 既存の全ポーズで曲がらない向きに折れる関節が無い()
        {
            yield return LoadPlayer();

            var monitor = _rig.gameObject.AddComponent<JointRomMonitor>();
            monitor.Bind(_rig, _anim, "ポーズ走査");
            monitor.LogWarnings = false;

            var reverseBends = new List<string>();
            var seen = new HashSet<string>();
            int poses = 0;

            void Measure(string label)
            {
                poses++;
                if (!monitor.SampleNow(false))
                    return;

                foreach (JointRomReading reading in monitor.Readings)
                {
                    if (reading.Verdict != JointRomVerdict.ReverseBend)
                        continue;
                    // 同じ姿勢・同じ関節の重複は 1 件に畳む（走査点が細かいため）。
                    if (!seen.Add($"{label}|{reading.Joint}|{reading.Side}"))
                        continue;
                    reverseBends.Add(
                        $"{label}: {reading.Describe()}（{reading.Excess:0}° 逆方向）");
                }
            }

            // 待機・歩き・構え
            for (float t = 0.2f; t <= 2.01f; t += 0.3f)
            {
                float time = t;
                _anim.ForceState(CharacterAnimator.State.Idle, time, 2f, StaggerTier.None, log: false);
                Measure($"Idle {time:0.0}s");
            }

            for (float t = 0f; t <= 1.001f; t += 0.1f)
            {
                float time = t;
                _anim.ForceState(CharacterAnimator.State.Walk, time, 1f, StaggerTier.None, log: false);
                Measure($"Walk {time:0.00}");
            }

            for (float t = 0f; t <= 1.001f; t += 0.25f)
            {
                float time = t;
                _anim.ForceState(CharacterAnimator.State.Guard, time, 1f, StaggerTier.None, log: false);
                Measure($"Guard {time:0.00}");
            }

            // 掴まれて もがく姿勢（相手の腕が無いので腕の角度だけ見る）
            for (float t = 0f; t <= 1.501f; t += 0.15f)
            {
                float time = t;
                _anim.ForceState(CharacterAnimator.State.Grabbed, time, 3f, StaggerTier.None, log: false);
                Measure($"Grabbed {time:0.00}");
            }

            // 攻撃（弱・強・崩し）
            var attacks = new[]
            {
                ("Light", AttackLibrary.Light()),
                ("Heavy", AttackLibrary.Heavy()),
                ("Break", AttackLibrary.BreakAttack()),
            };

            foreach ((string name, AttackData data) in attacks)
            {
                _anim.AttackOverride = data;
                _anim.ComboStep = name == "Light" ? 1 : 0;

                float duration = data.TotalSeconds;
                for (float t = 0f; t <= duration + 0.0001f; t += 0.02f)
                {
                    float time = t;
                    _anim.ForceState(CharacterAnimator.State.Attack, time, duration,
                        StaggerTier.None, log: false);
                    Measure($"Attack {name} {time:0.00}/{duration:0.00}");
                }
            }

            _anim.AttackOverride = null;
            _anim.ComboStep = 0;

            // 怯み（弱・中・大・崩し）× 顔/腹 × 正面/左右
            var tiers = new[]
            {
                (StaggerTier.Small, 0.35f),
                (StaggerTier.Medium, 0.50f),
                (StaggerTier.Large, 0.70f),
                (StaggerTier.GuardBreak, 1.20f),
            };
            var zones = new PunchZone?[] { null, PunchZone.Face, PunchZone.Body };
            var sides = new[] { 0, 1, -1 };

            foreach ((StaggerTier tier, float duration) in tiers)
            {
                foreach (PunchZone? zone in zones)
                {
                    foreach (int side in sides)
                    {
                        for (float u = 0.1f; u <= 0.91f; u += 0.4f)
                        {
                            _anim.PreviewHitZone = zone;
                            _anim.PreviewHurtSide(side);
                            float time = u * duration;
                            _anim.ForceState(CharacterAnimator.State.Stagger, time, duration,
                                tier, log: false);
                            Measure($"Stagger {tier} zone={zone?.ToString() ?? "none"} side={side} u={u:0.0}");
                        }
                    }
                }
            }

            _anim.PreviewHitZone = null;
            _anim.PreviewHurtSide(0);

            // ダウン・立ち上がり
            for (float t = 0.3f; t <= 2.01f; t += 0.1f)
            {
                float time = t;
                _anim.ForceState(CharacterAnimator.State.Down, time, 2f,
                    StaggerTier.GuardBreak, log: false);
                Measure($"Down {time:0.00}");
            }

            for (float u = 0.05f; u <= 1.001f; u += 0.1f)
            {
                float time = u * 1.2f;
                _anim.ForceState(CharacterAnimator.State.GetUp, time, 1.2f,
                    StaggerTier.GuardBreak, log: false);
                Measure($"GetUp u={u:0.00}");
            }

            _anim.ForceState(CharacterAnimator.State.Idle, 0.5f, 1f, StaggerTier.None, log: false);
            Object.Destroy(monitor);

            // 走査が空振りしていないこと（骨格が組めず 0 件、を「合格」にしない）。
            Assert.Greater(poses, 50, $"測った姿勢が少なすぎる（{poses} 件）");

            if (reverseBends.Count == 0)
                yield break;

            int shown = Mathf.Min(30, reverseBends.Count);
            string detail = string.Join("\n", reverseBends.GetRange(0, shown));
            if (shown < reverseBends.Count)
                detail += $"\n… ほか {reverseBends.Count - shown} 件";

            Assert.Fail(
                $"曲がらない向きに折れている関節が {reverseBends.Count} 件あります。" +
                "可動域ではなく姿勢の角度を直してください。\n" + detail);
        }

        /// <summary>
        /// 敵（プリミティブ人形）の全ポーズを 1 コマずつ作って測り、
        /// 人体の可動域（＋余裕）を超えた姿勢が無いことを確かめる。
        ///
        /// プレイヤー側（<see cref="既存の全ポーズで曲がらない向きに折れる関節が無い"/>）は
        /// 逆方向の折れだけを見るが、こちらは曲げすぎ・逆方向の両方を見る
        /// （Editor の <c>Ryona/Diagnostics/Check Joint ROM</c> の敵側と同じ判定）。
        ///
        /// 人形は <b>非アクティブで作る</b>。アクティブにすると <c>Awake</c> が走って
        /// 姿勢コンポーネントが一斉に付き、Editor 版と条件が変わってしまうため。
        /// </summary>
        [UnityTest]
        public IEnumerator 敵の全ポーズで可動域を超える関節が無い()
        {
            yield return null;

            var skin = new Material(Shader.Find("Standard")) { color = new Color(0.63f, 0.51f, 0.45f) };
            var cloth = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.30f, 0.28f) };

            var go = new GameObject("敵の可動域走査");
            go.SetActive(false);
            var figure = go.AddComponent<EnemyFigure>();
            figure.BodyMaterial = skin;
            figure.ClothMaterial = cloth;

            var violations = new List<string>();
            int poses = 0;

            try
            {
                // 1 回姿勢を作ってプリミティブ骨格を組ませる（Build は Preview が呼ぶ）。
                figure.Preview(EnemyPhase.Chase, 0f, EnemyBrain.AttackId.Jab, 0f);
                Assert.IsTrue(JointSkeletonBuilder.TryBuild(figure, out JointSkeleton skeleton),
                    "敵の骨格が組めない（必要なボーンが無い）");

                var readings = new List<JointRomReading>();

                void Measure(string label)
                {
                    poses++;
                    JointRomProbe.Measure(skeleton, readings);
                    foreach (JointRomReading reading in readings)
                    {
                        if (!reading.IsViolation)
                            continue;
                        violations.Add($"{label}: {reading.Describe()}（{reading.Excess:0}° 超過）");
                    }
                }

                // 立ち・歩き
                Measure("Chase 立ち");

                // 骨格が組めていても測る対象が空なら「逸脱なし」になってしまうので、
                // 実際に関節が取れていることを確かめる。
                Assert.Greater(readings.Count, 8, $"敵の関節を測れていない（{readings.Count} 件）");

                for (int i = 0; i < 8; i++)
                {
                    figure.PreviewTick(0.06f, 1.4f);
                    Measure($"Walk {i}");
                }

                // 攻撃 4 種 × 予備動作 / 振り抜き
                var attackIds = new[]
                {
                    EnemyBrain.AttackId.Jab, EnemyBrain.AttackId.Heavy,
                    EnemyBrain.AttackId.Break, EnemyBrain.AttackId.Grab,
                };

                foreach (EnemyBrain.AttackId attack in attackIds)
                {
                    for (float windup = 0.15f; windup <= 0.951f; windup += 0.2f)
                    {
                        figure.Preview(EnemyPhase.Windup, windup, attack, 0f);
                        Measure($"Windup {attack} {windup:0.00}");
                    }

                    figure.Preview(EnemyPhase.Attack, 1f, attack, 0f, 0.0f);
                    Measure($"Strike {attack} hit");

                    for (float swing = 0.04f; swing <= 0.321f; swing += 0.06f)
                    {
                        figure.Preview(EnemyPhase.Recover, 0f, attack, 0f, swing);
                        Measure($"Recover {attack} {swing:0.00}");
                    }
                }

                // 怯み・倒れ・立ち上がり
                for (float t = 0.05f; t <= 0.501f; t += 0.1f)
                {
                    figure.PreviewStagger(t);
                    Measure($"Stagger {t:0.00}");
                }

                for (float t = 0.05f; t <= 0.801f; t += 0.1f)
                {
                    figure.PreviewDead(t);
                    Measure($"Dead {t:0.00}");
                }

                for (float u = 0.1f; u <= 1.001f; u += 0.15f)
                {
                    figure.PreviewRise(u);
                    Measure($"Rise {u:0.00}");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(skin);
                Object.DestroyImmediate(cloth);
            }

            Assert.Greater(poses, 40, $"測った姿勢が少なすぎる（{poses} 件）");

            if (violations.Count == 0)
                yield break;

            int shown = Mathf.Min(30, violations.Count);
            string detail = string.Join("\n", violations.GetRange(0, shown));
            if (shown < violations.Count)
                detail += $"\n… ほか {violations.Count - shown} 件";

            Assert.Fail(
                $"敵のポーズに可動域の逸脱が {violations.Count} 件あります。" +
                "可動域ではなく姿勢の角度を直してください。\n" + detail);
        }
    }
}
