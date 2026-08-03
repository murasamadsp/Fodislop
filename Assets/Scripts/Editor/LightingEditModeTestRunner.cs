#nullable enable

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class LightingEditModeTestRunner
    {
        [MenuItem("Fodinae/Run Lighting EditMode Tests")]
        public static void RunTests()
        {
            Type fixtureType = typeof(Fodinae.Tests.World.ContactOcclusionE2ETests);
            object fixture = Activator.CreateInstance(fixtureType)
                ?? throw new InvalidOperationException(
                    $"Unable to create test fixture '{fixtureType.FullName}'.");
            MethodInfo[] tests = fixtureType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttributes(typeof(TestAttribute), true).Length > 0)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ToArray();

            int passed = 0;
            int failed = 0;
            int ignored = 0;
            foreach (MethodInfo test in tests)
            {
                try
                {
                    test.Invoke(fixture, null);
                    passed++;
                    Debug.Log($"[LightingTests] PASS {test.Name}");
                }
                catch (TargetInvocationException exception) when (
                    exception.InnerException is IgnoreException)
                {
                    ignored++;
                    Debug.LogWarning(
                        $"[LightingTests] IGNORE {test.Name}: " +
                        exception.InnerException!.Message);
                }
                catch (TargetInvocationException exception)
                {
                    failed++;
                    Debug.LogError(
                        $"[LightingTests] FAIL {test.Name}:\n" +
                        exception.InnerException);
                }
            }

            Debug.Log(
                $"[LightingTests] Finished: total={tests.Length}, passed={passed}, " +
                $"failed={failed}, ignored={ignored}");
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
#endif
