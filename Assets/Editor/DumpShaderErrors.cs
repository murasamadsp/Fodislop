#nullable enable

using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    // Shader compile errors do not reliably reach the console when a shader is
    // imported by a background refresh - the material just silently renders
    // magenta. This surfaces the import messages on demand so a failed shader
    // can be diagnosed from its actual error text instead of by guesswork.
    internal static class DumpShaderErrors
    {
        private static readonly string[] Shaders =
        {
            "Assets/Shaders/UI/PlanetSurface.shader",
            "Assets/Shaders/UI/PlanetAtmosphere.shader",
        };

        [MenuItem("Fodinae/Art/Dump Planet Shader Errors")]
        public static void Dump()
        {
            var report = new StringBuilder();

            foreach (string path in Shaders)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    report.AppendLine($"{path}: NOT FOUND");
                    continue;
                }

                // Typed with var: the ShaderMessage struct lives in a different
                // namespace across Unity versions, naming it explicitly breaks.
                var messages = ShaderUtil.GetShaderMessages(shader);
                report.AppendLine($"{path}: errors={ShaderUtil.ShaderHasError(shader)} messages={messages.Length}");

                foreach (var m in messages)
                {
                    report.AppendLine($"  [{m.severity}] line {m.line} ({m.platform}): {m.message} {m.messageDetails}");
                }
            }

            Debug.Log($"[DumpShaderErrors]\n{report}");
        }
    }
}
