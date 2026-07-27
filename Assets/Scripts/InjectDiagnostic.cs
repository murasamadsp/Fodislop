using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.Scripts
{
    public class InjectDiagnostic : MonoBehaviour
    {
        private static readonly string LogPath = Path.Combine(Application.dataPath, "..", "inject_diagnostic.txt");
        private static readonly BindingFlags BindFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly Type InjectType = typeof(InjectAttribute);
        private bool _scanned = false;

        private void Start()
        {
            if (_scanned)
            {
                return;
            }

            _scanned = true;
            Scan(autoRun: true);
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.f11Key.wasPressedThisFrame)
            {
                return;
            }

            Scan(autoRun: false);
        }

        private void Scan(bool autoRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== INJECT SCAN frame={Time.frameCount} time={Time.time:F2}s ===\n");

            var monoBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            int totalInjectFields = 0;
            int nullInjectFields = 0;

            foreach (var mb in monoBehaviours)
            {
                if (mb == null || mb.gameObject == null)
                {
                    continue;
                }

                var type = mb.GetType();
                var fields = type.GetFields(BindFlags);
                bool hasInjectFields = false;
                var fieldSb = new StringBuilder();

                foreach (var field in fields)
                {
                    if (!Attribute.IsDefined(field, InjectType))
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

                    var mark = isNull ? "NULL !!!" : $"OK [{value.GetType().Name}]";
                    fieldSb.AppendLine($"    {field.FieldType.Name} {field.Name} = {mark}");
                }

                if (!hasInjectFields)
                {
                    continue;
                }

                var go = mb.gameObject;
                sb.AppendLine($"[{type.Name}] on '{go.name}' active={go.activeInHierarchy} enabled={mb.enabled}");
                sb.Append(fieldSb);
                sb.AppendLine();
            }

            sb.AppendLine("=== SUMMARY ===");
            sb.AppendLine($"Total [Inject] fields scanned: {totalInjectFields}");
            sb.AppendLine($"NULL (uninjected): {nullInjectFields}");
            sb.AppendLine($"OK: {totalInjectFields - nullInjectFields}");

            if (nullInjectFields > 0)
            {
                sb.AppendLine($"\n>>> ROOT CAUSE: {nullInjectFields} [Inject] fields are NULL.");
                sb.AppendLine(">>> VContainer only injects objects it creates or objects explicitly injected via Container.Inject().");
                sb.AppendLine(">>> Scene MonoBehaviours with [Inject] must be explicitly injected in GameLifetimeScope.");
            }

            File.WriteAllText(LogPath, sb.ToString());
            var summary = $"[InjectDiagnostic] Scan -> {LogPath} ({nullInjectFields}/{totalInjectFields} NULL)";
            Debug.Log(summary);

            if (autoRun && nullInjectFields > 0)
            {
                Debug.LogError($"[InjectDiagnostic] AUTORUN FAILED: {nullInjectFields} [Inject] fields are NULL. F11 = manual re-scan.");
            }
        }
    }
}
