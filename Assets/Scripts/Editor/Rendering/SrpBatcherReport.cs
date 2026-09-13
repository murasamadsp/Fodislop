#nullable enable

using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Fodinae.EditorTools;

    internal static class SrpBatcherReport
{
    [MenuItem("Fodinae/Диагностика/Отчёт SRP Batcher")]
    private static void Report()
    {
        string[] paths =
        [
            "Assets/Shaders/Terrain.shader",
            "Assets/Resources/Shaders/WorldEntity.shader",
        ];

        MethodInfo? probe = typeof(ShaderUtil).GetMethod(
            "GetSRPBatcherCompatibilityCode",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        if (probe == null)
        {
            Debug.LogError("[SRP] ShaderUtil.GetSRPBatcherCompatibilityCode недоступен.");
            return;
        }

        foreach (string path in paths)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[SRP] Шейдер не найден: {path}");
                continue;
            }

            object? raw = probe.Invoke(null, [shader, 0]);
            int code = raw is int value ? value : -1;
            string verdict = code == 0 ? "СОВМЕСТИМ" : $"НЕ совместим (код {code})";
            Debug.Log($"[SRP] {shader.name}: {verdict}");
        }
    }
}
