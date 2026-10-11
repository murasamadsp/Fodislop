#nullable enable

using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public static class RenderingColorContract
{
    [MenuItem("Kern/Rendering/Apply linear color contract")]
    public static void Apply()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.allowHDRDisplaySupport = true;
        AssetDatabase.SaveAssets();
        Debug.Log($"[RenderingColorContract] colorSpace={PlayerSettings.colorSpace}; " +
            $"allowHDRDisplaySupport={PlayerSettings.allowHDRDisplaySupport}; " +
            $"useHDRDisplay={PlayerSettings.useHDRDisplay}");
    }
}
