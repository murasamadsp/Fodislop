#nullable enable

using System;
using Fodinae.Audio.Backend;
using Fodinae.Core;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Editor
{
    /// <summary>
    /// Авторинг Bootstrap-сцены. Менеджеры — часть сцены: строгий контракт
    /// <see cref="BootstrapLifetimeScope"/> требует, чтобы все менеджеры были
    /// законтрибучены под скоупом, и падает с ошибкой при рассинхроне. Чтобы
    /// этот контракт никогда не срабатывал на здоровой сцене, недостающие
    /// менеджеры материализуются автоматически: при открытии Bootstrap в
    /// редакторе и непосредственно перед входом в Play Mode. Авто-лечение
    /// работает аддитивно и не трогает активную сцену пользователя.
    /// </summary>
    public static class BootstrapSceneAuthoring
    {
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        private static readonly Type[] ManagerTypes =
        [
            typeof(ConnectionManager),
            typeof(NetworkService),
            typeof(AudioSystem),
            typeof(ClientConfigManager),
            typeof(ClientAssetLoader),
        ];

        [InitializeOnLoadMethod]
        private static void RegisterSceneSelfHeal()
        {
            // Сцена открыта в редакторе (Single): лечим сразу, пока она активна.
            EditorSceneManager.sceneOpened += (scene, _) =>
            {
                if (scene.path == BootstrapScenePath &&
                    scene == SceneManager.GetActiveScene() &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EnsureBootstrapMaterialized();
                }
            };

            // Перед входом в Play Mode: покрывает случай, когда Bootstrap-сцена
            // вообще не открывалась в редакторе (Play стартует из другой сцены).
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode && !Application.isBatchMode)
                {
                    EnsureBootstrapMaterialized();
                }
            };
        }

        [MenuItem("Fodinae/Architecture/Materialize Bootstrap Managers")]
        public static void MaterializeBootstrapManagers()
        {
            EnsureBootstrapMaterialized();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Материализует недостающих менеджеров в Bootstrap-сцене. Если сцена уже
        /// активна — работает прямо в ней (не переоткрывает, не теряет несохранённое);
        /// иначе открывает её аддитивно, лечит, сохраняет и закрывает, не меняя
        /// активную сцену пользователя. Идемпотентно: при полной сцене — no-op.
        /// </summary>
        private static void EnsureBootstrapMaterialized()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool wasActive = scene.path == BootstrapScenePath;
            if (!wasActive)
            {
                scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
            }

            try
            {
                BootstrapLifetimeScope scope = RequireSingleScope(scene);

                bool changed = false;
                Camera camera = scope.GetComponent<Camera>();
                if (camera != null)
                {
                    Color targetBg = new Color(0.012f, 0.018f, 0.032f, 1f);
                    if (camera.backgroundColor != targetBg || camera.clearFlags != CameraClearFlags.SolidColor)
                    {
                        camera.backgroundColor = targetBg;
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        changed = true;
                    }
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                        if (removed > 0)
                        {
                            changed = true;
                            Debug.LogWarning($"[BootstrapSceneAuthoring] Removed {removed} missing MonoBehaviours on '{t.name}' in '{scene.path}'.");
                        }
                    }
                }

                foreach (Type managerType in ManagerTypes)
                {
                    MonoBehaviour? manager = FindSingleManager(scene, managerType);
                    if (manager == null)
                    {
                        CreateManager(scope, managerType);
                        changed = true;
                        continue;
                    }

                    if (!manager.transform.IsChildOf(scope.transform))
                    {
                        manager.transform.SetParent(scope.transform, worldPositionStays: true);
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, BootstrapScenePath))
                {
                    throw new InvalidOperationException($"Failed to save '{BootstrapScenePath}'.");
                }

                Debug.Log($"[BootstrapSceneAuthoring] Materialized missing managers into '{BootstrapScenePath}'.");
            }
            finally
            {
                if (!wasActive)
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }

        private static BootstrapLifetimeScope RequireSingleScope(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BootstrapLifetimeScope candidate in root.GetComponentsInChildren<BootstrapLifetimeScope>(true))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Bootstrap scene has no BootstrapLifetimeScope.");
        }

        private static MonoBehaviour? FindSingleManager(Scene scene, Type managerType)
        {
            MonoBehaviour? result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour candidate in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (candidate == null || candidate.GetType() != managerType)
                    {
                        continue;
                    }

                    if (result != null)
                    {
                        throw new InvalidOperationException(
                            $"Bootstrap scene contains multiple '{managerType.FullName}' components.");
                    }

                    result = candidate;
                }
            }

            return result;
        }

        private static MonoBehaviour CreateManager(BootstrapLifetimeScope scope, Type managerType)
        {
            var managerObject = new GameObject(managerType.Name);
            managerObject.SetActive(false);
            managerObject.transform.SetParent(scope.transform, worldPositionStays: false);
            var manager = (MonoBehaviour)managerObject.AddComponent(managerType);
            managerObject.SetActive(true);
            return manager;
        }
    }
}
