#nullable enable

using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools
{
    internal static class PluginImporterUpgrade
    {
        [MenuItem("Tools/Fodinae/Пересохранить метаданные плагинов")]
        private static void ResaveAll()
        {
            int upgraded = 0;
            foreach (string guid in AssetDatabase.FindAssets(string.Empty))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not PluginImporter importer)
                {
                    continue;
                }

                importer.SaveAndReimport();
                upgraded++;
            }

            Debug.Log($"[PluginImporterUpgrade] Пересохранено импортёров плагинов: {upgraded}.");
        }
    }
}
