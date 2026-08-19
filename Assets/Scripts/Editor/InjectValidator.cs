#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class InjectValidator
    {
        private const string LogPath = "inject_diagnostic_editmode.txt";

        [MenuItem("Fodinae/Diagnostics/Validate Injections")]
        public static void ValidateInjections()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== INJECT VALIDATION (Edit Mode) ===\n");

            var monoBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
            var gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            int totalInjectFields = 0;
            int nullInjectFields = 0;
            int missingComponents = 0;

            foreach (var gameObject in gameObjects)
            {
                var components = gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        continue;
                    }

                    missingComponents++;
                    sb.AppendLine($"[MISSING SCRIPT] '{GetHierarchyPath(gameObject)}'");
                }
            }

            foreach (var mb in monoBehaviours)
            {
                if (mb == null || mb.gameObject == null)
                {
                    continue;
                }

                var type = mb.GetType();
                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                bool hasInjectFields = false;
                var fieldSb = new StringBuilder();

                foreach (var field in fields)
                {
                    if (!Attribute.IsDefined(field, typeof(VContainer.InjectAttribute)))
                    {
                        continue;
                    }

                    hasInjectFields = true;
                    totalInjectFields++;

                    var value = field.GetValue(mb);
                    bool isNull = value == null;
                    if (isNull)
                    {
                        nullInjectFields++;
                    }

                    var mark = value is null ? "NULL !!!" : $"OK [{value.GetType().Name}]";
                    fieldSb.AppendLine($"    {field.FieldType.Name} {field.Name} = {mark}");
                }

                if (!hasInjectFields)
                {
                    continue;
                }

                var go = mb.gameObject;
                sb.AppendLine($"[{type.Name}] on '{go.name}' active={go.activeInHierarchy}");
                sb.Append(fieldSb);
                sb.AppendLine();
            }

            sb.AppendLine("=== SUMMARY ===");
            sb.AppendLine($"Total [Inject] fields scanned: {totalInjectFields}");
            sb.AppendLine($"NULL (uninjected): {nullInjectFields}");
            sb.AppendLine($"OK: {totalInjectFields - nullInjectFields}");
            sb.AppendLine($"Missing script components: {missingComponents}");

            if (nullInjectFields > 0)
            {
                sb.AppendLine($"\n>>> ROOT CAUSE: {nullInjectFields} [Inject] fields are NULL.");
                sb.AppendLine(">>> VContainer only injects objects it creates or objects explicitly injected via Container.Inject().");
                sb.AppendLine(">>> Scene MonoBehaviours with [Inject] must be explicitly injected in GameLifetimeScope.");
            }

            var path = Path.Combine(Application.dataPath, "..", LogPath);
            File.WriteAllText(path, sb.ToString());

            if (nullInjectFields > 0 || missingComponents > 0)
            {
                Debug.LogError(
                    $"[InjectValidator] null injections={nullInjectFields}/{totalInjectFields}, " +
                    $"missing scripts={missingComponents}. Report: {path}");
            }
            else
            {
                Debug.Log($"[InjectValidator] All {totalInjectFields} [Inject] fields OK. Report: {path}");
            }
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            var path = gameObject.name;
            var current = gameObject.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
