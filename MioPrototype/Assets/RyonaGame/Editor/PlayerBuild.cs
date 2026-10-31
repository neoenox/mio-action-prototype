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

            var existing = new HashSet<Object>();
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
                Debug.LogError("[Ryona] ビルド失敗");
        }
    }
}
