#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fodinae.Core;
using Fodinae.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Fodinae.Tests.Core
{
    [TestFixture]
    public sealed class ProductionSceneContractValidatorTests
    {
        private SceneSetup[] _originalSetup = null!;

        [SetUp]
        public void SetUp()
        {
            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        }

        [TearDown]
        public void TearDown()
        {
            if (_originalSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_originalSetup);
            }
        }

        [Test]
        public void EmptyContentScene_ReportsScopeAndDocumentViolations()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var errors = new List<string>();

            ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

            Assert.That(errors.Any(error => error.Contains("exactly one LifetimeScope")), Is.True);
            Assert.That(errors.Any(error => error.Contains("exactly one UIDocument")), Is.True);
        }

        [Test]
        public void EnabledContentCamera_IsRejected()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var documentObject = new GameObject("Document");
            documentObject.AddComponent<UIDocument>();
            var cameraObject = new GameObject("DisplayCamera");
            cameraObject.AddComponent<Camera>().enabled = true;
            SceneManager.MoveGameObjectToScene(documentObject, scene);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var errors = new List<string>();

            ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

            Assert.That(errors.Any(error => error.Contains("enabled display camera 'DisplayCamera'")), Is.True);
        }

        [Test]
        public void ProductionScenes_AreValid_AndOriginalEditorSetupIsRestored()
        {
            SceneSetup[] before = EditorSceneManager.GetSceneManagerSetup();
            var errors = new List<string>();

            try
            {
                foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
                {
                    EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    errors.Clear();
                    ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);
                    Assert.That(errors, Is.Empty, buildScene.path);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(before);
            }

            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            Assert.That(after.Select(item => item.path), Is.EqualTo(before.Select(item => item.path)));
            Assert.That(after.Select(item => item.isLoaded), Is.EqualTo(before.Select(item => item.isLoaded)));
            Assert.That(after.Select(item => item.isActive), Is.EqualTo(before.Select(item => item.isActive)));
        }

        [Test]
        public void BootstrapMissingAuthoredReference_IsReportedWithoutSavingScene()
        {
            Scene bootstrapScene = EditorSceneManager.OpenScene(
                "Assets/Scenes/Bootstrap.unity",
                OpenSceneMode.Single);
            BootstrapLifetimeScope scope = bootstrapScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BootstrapLifetimeScope>(true))
                .Single();
            SerializedObject serialized = new(scope);
            SerializedProperty connection = serialized.FindProperty("_connectionManager");
            Assert.That(connection, Is.Not.Null);
            UnityEngine.Object? original = connection.objectReferenceValue;

            try
            {
                connection.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var errors = new List<string>();

                ProductionSceneContractValidator.ValidateAllLoadedScenes(errors);

                Assert.That(
                    errors,
                    Has.Some.Contains("BootstrapLifetimeScope._connectionManager is not assigned"));
            }
            finally
            {
                connection.objectReferenceValue = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(bootstrapScene);
            }
        }
    }
}
