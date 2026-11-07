using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ryona.EditorTools
{
    /// <summary>
    /// Windows の実行ファイルを作る。エディタの Play ではなく、実際に起動して触れる形にするため。
    /// </summary>
    public static class PlayerBuild
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string OutFolder = "Build";
        const string ExeName = "MioPrototype.exe";

        /// <summary>最後にビルドが通った時刻。これより新しいソースがあれば作り直す。</summary>
        const string StampName = ".buildstamp";

        /// <summary>ビルド結果に影響する拡張子。これが更新されたら作り直す。</summary>
        static readonly string[] WatchedExtensions =
            { ".cs", ".unity", ".prefab", ".asset", ".mat", ".controller", ".asmdef", ".shader" };

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        static string ExePath => Path.Combine(ProjectRoot, OutFolder, ExeName);
        static string StampPath => Path.Combine(ProjectRoot, OutFolder, StampName);

        /// <summary>
        /// <c>Build/MioPrototype.exe</c> がソースより古いか。
        /// 起動したときに最新の変更が見たいので、自動ビルドはここを見て走る。
        /// </summary>
        public static bool IsStale()
        {
            if (!File.Exists(ExePath) || !File.Exists(StampPath))
                return true;

            // ProjectSettings は見ない。Unity が終了時に必ず書き戻すので、
            // バッチビルドの直後でも「古い」と判定されて毎回作り直してしまう。
            // 設定を変えたときは Ryona/Build Windows Player で明示的に作る。
            // 時刻は stamp の「中身」（ビルド開始時刻）を使う。ファイルの mtime だと
            // ビルド中に保存された変更を取りこぼす。
            DateTime builtAt = File.GetLastWriteTimeUtc(StampPath);
            if (DateTime.TryParse(File.ReadAllText(StampPath), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime startedAt))
                builtAt = startedAt;
            string assets = Path.Combine(ProjectRoot, "Assets");
            if (!Directory.Exists(assets))
                return true;

            string editorDir = Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar;
            foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
            {
                if (!WatchedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    continue;

                // エディタ専用コードはプレイヤーに入らないので、変更しても作り直さない。
                if (file.Contains(editorDir))
                    continue;

                if (File.GetLastWriteTimeUtc(file) > builtAt)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// VRM は実行時に StreamingAssets から読み込まれるので、
        /// MToon のシェーダーがビルドから剥がされるとモデルがピンクになる。
        /// Always Included Shaders に入れて剥がれないようにする。
        /// </summary>
        static void EnsureShaders()
        {
            string[] wanted = { "VRM10/MToon10", "VRM/MToon", "Standard", "Sprites/Default" };

            var gsObjects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gsObjects == null || gsObjects.Length == 0)
                return;

            var so = new SerializedObject(gsObjects[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null)
                return;

            var existing = new HashSet<UnityEngine.Object>();
            for (int i = 0; i < list.arraySize; i++)
                existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);

            foreach (string name in wanted)
            {
                var shader = Shader.Find(name);
                if (shader == null || existing.Contains(shader))
                    continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                existing.Add(shader);
                Debug.Log($"[Ryona] Always Included Shader に追加: {name}");
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ryona/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "Scenes/Prototype.unity")))
            {
                Debug.LogError("[Ryona] Prototype.unity が無い。先に Ryona/Build Prototype Scene を実行すること。");
                return;
            }

            // ビルドを始めた時刻。これ以降に保存されたソースは「未反映」として扱う。
            DateTime startedAt = DateTime.UtcNow;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EnsureShaders();

            string root = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(root, OutFolder);
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, ExeName);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            var summary = report.summary;
            Debug.Log($"[Ryona] build result={summary.result} " +
                      $"size={summary.totalSize / (1024 * 1024)}MB errors={summary.totalErrors} " +
                      $"output={outPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Ryona] ビルド失敗");
                return;
            }

            File.WriteAllText(StampPath, startedAt.ToString("o"));
            Debug.Log("[Ryona] Build/MioPrototype.exe を最新にした");
        }

        /// <summary>
        /// コマンドラインから: Unity -batchmode -quit -projectPath . -executeMethod
        /// Ryona.EditorTools.PlayerBuild.BuildWindowsFromCli
        /// 変更が無ければ何もせず終わる（終了コード 0）。
        /// </summary>
        public static void BuildWindowsFromCli()
        {
            int code = 0;
            try
            {
                if (IsStale())
                    BuildWindows();
                else
                    Debug.Log("[Ryona] build is up to date");

                if (IsStale())
                    code = 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }

            EditorApplication.Exit(code);
        }
    }
}
