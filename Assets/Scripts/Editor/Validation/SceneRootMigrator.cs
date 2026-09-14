#nullable enable

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Fodinae.Editor;

// Сводит каждую сцену сборки к одному корню — её LifetimeScope.
//
// Объекты рядом со scope контейнер не видит (MainGame: SceneSetup с пустыми
// [Inject]). Правило стережёт ProductionSceneContractValidator.ValidateSingleRoot,
// этот пункт исправляет найденное.
//
// Корни кладутся прямо под объект scope с сохранением мировых позиций. Не в
// Services MainGame: тот выключен до внедрения, и игрок с интерфейсом там не
// проснулись бы. Пустой корень — только Transform, без детей — удаляется.
// Сцена без единственного scope не трогается: там сначала нужен сам scope.
public static class SceneRootMigrator
{
    [MenuItem("Fodinae/Architecture/Move Scene Roots Under Composition Root")]
    public static void Migrate()
    {
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                MigrateScene(scene);
            }
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static void MigrateScene(Scene scene)
    {
        LifetimeScope[] scopes = scene.GetRootGameObjects()
            .Where(root => root.TryGetComponent<LifetimeScope>(out _))
            .Select(root => root.GetComponent<LifetimeScope>())
            .ToArray();
        if (scopes.Length != 1)
        {
            Debug.LogWarning($"[SceneRoots] {scene.name}: expected one root LifetimeScope, found {scopes.Length}; scene left untouched.");
            return;
        }

        Transform scopeTransform = scopes[0].transform;
        var moved = new List<string>();
        var removed = new List<string>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.transform == scopeTransform)
            {
                continue;
            }

            if (root.transform.childCount == 0 && root.GetComponents<Component>().Length == 1)
            {
                removed.Add(root.name);
                Object.DestroyImmediate(root);
                continue;
            }

            root.transform.SetParent(scopeTransform, worldPositionStays: true);
            moved.Add(root.name);
        }

        if (moved.Count == 0 && removed.Count == 0)
        {
            Debug.Log($"[SceneRoots] {scene.name}: already a single root.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(
            $"[SceneRoots] {scene.name}: moved under {scopes[0].GetType().Name}: " +
            $"{(moved.Count == 0 ? "none" : string.Join(", ", moved))}; " +
            $"removed empty: {(removed.Count == 0 ? "none" : string.Join(", ", removed))}.");
    }
}
