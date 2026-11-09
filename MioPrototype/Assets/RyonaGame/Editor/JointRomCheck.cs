using System;
using System.Collections.Generic;
using System.IO;
using Ryona.Game;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using AttackId = Ryona.Game.EnemyBrain.AttackId;

namespace Ryona.EditorTools
{
    /// <summary>
    /// 関節の可動域チェック（描画なし・ヘッドレスで動く）。
    ///
    /// プレイヤーの全状態（待機・歩き・攻撃・怯み・ダウン・立ち上がり…）と、
    /// 敵の全フェーズ（歩き・予備動作・振り抜き・怯み・倒れ・立ち上がり）を
    /// 総当たりで 1 コマずつ作り、<see cref="JointRomProbe"/> で実際の関節角度を測る。
    /// 人体の可動域（＋余裕）を超えた姿勢だけを <c>[JointROM]</c> の警告として出す。
    ///
    /// 使い方: メニュー <c>Ryona/Diagnostics/Check Joint ROM</c>。
    /// コマンドからは:
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;MioPrototype&gt; ^
    ///   -executeMethod Ryona.EditorTools.JointRomCheck.Check ^
    ///   -logFile &lt;log&gt;
    /// </code>
    /// 角度の設定を変えたら実行し、警告が増えていないことを確かめる。
    /// 実行中の警告は <see cref="JointRomWatch"/>（<c>-jointlog</c>）でも見られる。
    /// </summary>
    public static class JointRomCheck
    {
        const string VrmName = "mio_v1.vrm";

        /// <summary>コンソールに出す警告の上限（全体）。超えた分も件数には数える。</summary>
        const int MaxWarningLines = 40;

        /// <summary>計測した角度の絶対値の最大（関節ごと）。値が妥当かの確認用。</summary>
        sealed class Ranges
        {
            readonly Dictionary<JointId, float> _maxAbs = new Dictionary<JointId, float>();

            public void Add(JointRomReading reading)
            {
                float abs = Mathf.Abs(reading.Degrees);
                _maxAbs.TryGetValue(reading.Joint, out float current);
                if (abs > current)
                    _maxAbs[reading.Joint] = abs;
            }

            public string Describe()
            {
                var text = new System.Text.StringBuilder(160);
                foreach (JointId joint in Enum.GetValues(typeof(JointId)))
                {
                    if (!_maxAbs.TryGetValue(joint, out float max))
                        continue;
                    if (text.Length > 0)
                        text.Append(' ');
                    text.Append(HumanJointLimits.Of(joint).Label).Append('=').Append(max.ToString("0"));
                }
                return text.ToString();
            }
        }

        /// <summary>チェック結果の集計。</summary>
        sealed class Report
        {
            public readonly List<string> Details = new List<string>();
            public int CheckedPoses;
            public int Violations;
            public int LoggedWarnings;

            public void Pose(string subject, string label, List<JointRomReading> readings,
                JointRomTally tally, Ranges ranges)
            {
                CheckedPoses++;

                for (int i = 0; i < readings.Count; i++)
                {
                    JointRomReading reading = readings[i];
                    ranges?.Add(reading);
                    if (!reading.IsViolation)
                        continue;

                    Violations++;
                    tally.Add(reading);

                    string line = $"{subject} {label}: {reading.Describe()} " +
                                  $"{reading.Excess:0}° 超過";
                    Details.Add(line);

                    if (LoggedWarnings >= MaxWarningLines)
                        continue;

                    Debug.LogWarning($"[JointROM] {line}");
                    LoggedWarnings++;
                }
            }
        }

        [MenuItem("Ryona/Diagnostics/Check Joint ROM")]
        public static void Check()
        {
            var report = new Report();
            var playerTally = new JointRomTally();
            var enemyTally = new JointRomTally();
            var playerRanges = new Ranges();
            var enemyRanges = new Ranges();

            CheckPlayer(report, playerTally, playerRanges);
            CheckEnemies(report, enemyTally, enemyRanges);

            // 逸脱が無くても「実際に何度まで曲がっていたか」は出す。
            // 値がおかしい（0°のまま等）と計測側の不具合に気付ける。
            if (playerRanges.Describe().Length > 0)
                Debug.Log($"[JointROM] 実測レンジ（プレイヤー）: {playerRanges.Describe()}");
            if (enemyRanges.Describe().Length > 0)
                Debug.Log($"[JointROM] 実測レンジ（敵）: {enemyRanges.Describe()}");

            if (report.Violations == 0)
            {
                Debug.Log($"[JointROM] 可動域チェック完了: {report.CheckedPoses} 姿勢を確認、逸脱なし。");
                return;
            }

            Debug.LogWarning(
                $"[JointROM] 可動域チェック完了: {report.CheckedPoses} 姿勢を確認、" +
                $"{report.Violations} 件の逸脱。");
            LogTally("プレイヤー", playerTally);
            LogTally("敵", enemyTally);

            if (report.LoggedWarnings >= MaxWarningLines)
                Debug.LogWarning(
                    $"[JointROM] 警告は先頭 {MaxWarningLines} 件だけ表示しました。" +
                    " 残りは件数だけ数えています（姿勢名は詳細ログを確認）。");
        }

        static void LogTally(string subject, JointRomTally tally)
        {
            if (tally == null || tally.TrackedKeys == 0)
                return;

            var readings = new List<JointRomReading>(tally.WorstReadings);
            readings.Sort((a, b) => b.Excess.CompareTo(a.Excess));

            int shown = Mathf.Min(4, readings.Count);
            var text = new System.Text.StringBuilder(160);
            text.Append("[JointROM] まとめ（").Append(subject).Append("）: ")
                .Append(tally.TrackedKeys).Append(" 種 / ")
                .Append(tally.ViolationSamples).Append(" 件。");
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    text.Append(" / ");
                JointRomReading reading = readings[i];
                text.Append(reading.Describe())
                    .Append(' ').Append(reading.Excess.ToString("0")).Append("°超過");
            }

            Debug.LogWarning(text.ToString());
        }

        // --- プレイヤー ---------------------------------------------------

        static void CheckPlayer(Report report, JointRomTally tally, Ranges ranges)
        {
            string vrmPath = Path.Combine(Application.dataPath, "StreamingAssets", VrmName);
            if (!File.Exists(vrmPath))
            {
                Debug.LogWarning($"[JointROM] プレイヤーの VRM が無いため姿勢を確認できません: {vrmPath}");
                return;
            }

            Vrm10Instance vrm = null;
            try
            {
                vrm = Vrm10.LoadPathAsync(vrmPath, canLoadVrm0X: true, showMeshes: false)
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Debug.LogError($"[JointROM] プレイヤーの VRM の読み込みに失敗: {e}");
                return;
            }

            if (vrm == null)
            {
                Debug.LogError("[JointROM] プレイヤーの VRM の読み込み結果が null。");
                return;
            }

            try
            {
                var rig = vrm.gameObject.AddComponent<ProceduralRig>();
                rig.Capture();
                var anim = vrm.gameObject.AddComponent<CharacterAnimator>();
                anim.Rig = rig;

                if (!JointSkeletonBuilder.TryBuild(rig, out JointSkeleton skeleton))
                {
                    Debug.LogWarning("[JointROM] プレイヤーの骨格が組めない（必要なボーンが無い）。");
                    return;
                }

                var readings = new List<JointRomReading>();

                void Measure(string label)
                {
                    JointRomProbe.Measure(skeleton, readings);
                    report.Pose("プレイヤー", label, readings, tally, ranges);
                }

                // 待機・歩き・構え
                for (float t = 0.2f; t <= 2.01f; t += 0.3f)
                {
                    float time = t;
                    anim.ForceState(CharacterAnimator.State.Idle, time, 2f, StaggerTier.None, log: false);
                    Measure($"Idle {time:0.0}s");
                }

                for (float t = 0f; t <= 1.001f; t += 0.1f)
                {
                    float time = t;
                    anim.ForceState(CharacterAnimator.State.Walk, time, 1f, StaggerTier.None, log: false);
                    Measure($"Walk {time:0.00}");
                }

                for (float t = 0f; t <= 1.001f; t += 0.25f)
                {
                    float time = t;
                    anim.ForceState(CharacterAnimator.State.Guard, time, 1f, StaggerTier.None, log: false);
                    Measure($"Guard {time:0.00}");
                }

                // 掴まれて もがく姿勢（相手の腕を掴む形。Grapple が無いので腕の角度だけ見る）
                for (float t = 0f; t <= 1.501f; t += 0.15f)
                {
                    float time = t;
                    anim.ForceState(CharacterAnimator.State.Grabbed, time, 3f, StaggerTier.None, log: false);
                    Measure($"Grabbed {time:0.00}");
                }

                // 攻撃（3 段コンボ・強・崩し）。1 フレームずつ測る。
                var attacks = new[]
                {
                    ("Light", AttackLibrary.Light()),
                    ("Heavy", AttackLibrary.Heavy()),
                    ("Break", AttackLibrary.BreakAttack()),
                };

                foreach ((string name, AttackData data) in attacks)
                {
                    anim.AttackOverride = data;
                    anim.ComboStep = name == "Light" ? 1 : 0;

                    float duration = data.TotalSeconds;
                    for (float t = 0f; t <= duration + 0.0001f; t += 0.02f)
                    {
                        float time = t;
                        anim.ForceState(CharacterAnimator.State.Attack, time, duration,
                            StaggerTier.None, log: false);
                        Measure($"Attack {name} {time:0.00}/{duration:0.00}");
                    }
                }

                anim.AttackOverride = null;
                anim.ComboStep = 0;

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
                                anim.PreviewHitZone = zone;
                                anim.PreviewHurtSide(side);
                                float time = u * duration;
                                anim.ForceState(CharacterAnimator.State.Stagger, time, duration,
                                    tier, log: false);
                                Measure($"Stagger {tier} zone={zone?.ToString() ?? "none"} side={side} u={u:0.0}");
                            }
                        }
                    }
                }

                anim.PreviewHitZone = null;
                anim.PreviewHurtSide(0);

                // ダウン・立ち上がり
                for (float t = 0.3f; t <= 2.01f; t += 0.1f)
                {
                    float time = t;
                    anim.ForceState(CharacterAnimator.State.Down, time, 2f,
                        StaggerTier.GuardBreak, log: false);
                    Measure($"Down {time:0.00}");
                }

                for (float u = 0.05f; u <= 1.001f; u += 0.1f)
                {
                    float time = u * 1.2f;
                    anim.ForceState(CharacterAnimator.State.GetUp, time, 1.2f,
                        StaggerTier.GuardBreak, log: false);
                    Measure($"GetUp u={u:0.00}");
                }
            }
            finally
            {
                if (vrm != null)
                    UnityEngine.Object.DestroyImmediate(vrm.gameObject);
            }
        }

        // --- 敵 -----------------------------------------------------------

        static void CheckEnemies(Report report, JointRomTally tally, Ranges ranges)
        {
            var skin = new Material(Shader.Find("Standard")) { color = new Color(0.63f, 0.51f, 0.45f) };
            var cloth = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.30f, 0.28f) };

            var go = new GameObject("Joint ROM Enemy Check");
            try
            {
                var figure = go.AddComponent<EnemyFigure>();
                figure.BodyMaterial = skin;
                figure.ClothMaterial = cloth;

                // エディタ（非 Play）では Awake が走らないので、姿勢を 1 回作って
                // プリミティブ骨格を組ませる。Preview が Build を呼んでくれる。
                figure.Preview(EnemyPhase.Chase, 0f, AttackId.Jab, 0f);

                if (!JointSkeletonBuilder.TryBuild(figure, out JointSkeleton skeleton))
                {
                    Debug.LogWarning("[JointROM] 敵の骨格が組めない（必要なボーンが無い）。");
                    return;
                }

                var readings = new List<JointRomReading>();

                void Measure(string label)
                {
                    JointRomProbe.Measure(skeleton, readings);
                    report.Pose("敵", label, readings, tally, ranges);
                }

                // 立ち・歩き（実際の歩きは距離で足を運ぶので、少しずつ進める）
                Measure("Chase 立ち");
                for (int i = 0; i < 8; i++)
                {
                    figure.PreviewTick(0.06f, 1.4f);
                    Measure($"Walk {i}");
                }

                // 攻撃 4 種 × 予備動作 / 振り抜き
                var attackIds = new[] { AttackId.Jab, AttackId.Heavy, AttackId.Break, AttackId.Grab };
                foreach (AttackId attack in attackIds)
                {
                    for (float windup = 0.15f; windup <= 0.951f; windup += 0.2f)
                    {
                        figure.Preview(EnemyPhase.Windup, windup, attack, 0f);
                        Measure($"Windup {attack} {windup:0.00}");
                    }

                    // 当てる（Attack）→ 振り抜き（Recover）
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
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(skin);
                UnityEngine.Object.DestroyImmediate(cloth);
            }
        }
    }
}
