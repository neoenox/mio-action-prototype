using System.IO;
using UnityEditor;
using UnityEngine;
using Ryona.Game;

namespace Ryona.EditorTools
{
    /// <summary>
    /// 設定アセット（<see cref="RyonaConfig"/>）の作成。設定の真実源は
    /// <c>Assets/RyonaGame/Resources/RyonaConfig.asset</c> 1 つで、既にあれば何もしない。
    /// </summary>
    public static class RyonaConfigSetup
    {
        const string ResourcesDir = "Assets/RyonaGame/Resources";
        const string AssetPath = ResourcesDir + "/" + RyonaConfig.ResourcePath + ".asset";

        /// <summary>
        /// アセットが無ければ作る。バッチからも呼べる:
        /// <c>Unity -batchmode -quit -executeMethod Ryona.EditorTools.RyonaConfigSetup.CreateIfMissing</c>
        /// </summary>
        [MenuItem("Ryona/Setup/Create RyonaConfig (if missing)")]
        public static void CreateIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<RyonaConfig>(AssetPath) != null)
            {
                Debug.Log($"[Config] 既にあります: {AssetPath}");
                return;
            }

            if (!Directory.Exists(ResourcesDir))
                Directory.CreateDirectory(ResourcesDir);

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<RyonaConfig>(), AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Config] 設定アセットを作成しました: {AssetPath}");
        }
    }
}
