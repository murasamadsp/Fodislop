#nullable enable

#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Fodinae.Editor
{
    public static class LightingEditModeTestRunner
    {
        private const string FixtureTypeName =
            "Fodinae.Tests.World.ContactOcclusionE2ETests, Fodinae.Tests.Editor";
        private const string TestAttributeName = "NUnit.Framework.TestAttribute";
        private const string IgnoreExceptionName = "NUnit.Framework.IgnoreException";

        [MenuItem("Fodinae/Run Lighting EditMode Tests")]
        public static void RunTests()
        {
            Type? fixtureType = Type.GetType(FixtureTypeName, throwOnError: false);
            if (fixtureType == null)
            {
                Debug.LogWarning(
                    $"[LightingTests] Fixture '{FixtureTypeName}' is unavailable. " +
                    "Install or reload the editor test assembly before running this menu item.");
                return;
            }

            object fixture = Activator.CreateInstance(fixtureType)
                ?? throw new InvalidOperationException(
                    $"Unable to create test fixture '{fixtureType.FullName}'.");
            MethodInfo[] tests = fixtureType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttributesData().Any(attribute =>
                    string.Equals(attribute.AttributeType.FullName, TestAttributeName, StringComparison.Ordinal)))
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
                    exception.InnerException != null &&
                    string.Equals(
                        exception.InnerException.GetType().FullName,
                        IgnoreExceptionName,
                        StringComparison.Ordinal))
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
