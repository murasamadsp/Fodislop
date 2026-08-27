#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace Fodinae.Core.Lifecycle;

public interface ISceneCoordinator
{
    string? CurrentSceneName { get; }

    ulong Generation { get; }

    UniTask TransitionAsync(string sceneName, CancellationToken cancellationToken = default);

    UniTask StageAsync(string sceneName, CancellationToken cancellationToken = default);

    UniTask CommitStagedAsync(CancellationToken cancellationToken = default);

    UniTask DiscardStagedAsync(CancellationToken cancellationToken = default);

    UniTask RestartCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed class SceneCoordinator(IObjectResolver resolver) : ISceneCoordinator, IDisposable
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private ContentSceneRoot? _current;
    private ContentSceneRoot? _staged;
    private ulong _generation;
    private bool _disposed;

    public string? CurrentSceneName => _current != null ? _current.gameObject.scene.name : null;

    public ulong Generation => _generation;

    public async UniTask TransitionAsync(
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SceneCoordinator));
        }

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new ArgumentException("Scene name is required.", nameof(sceneName));
        }

        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            await TransitionExclusiveAsync(sceneName, cancellationToken);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async UniTask StageAsync(
        string sceneName,
        CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (_staged != null)
            {
                throw new InvalidOperationException(
                    $"Scene '{_staged.gameObject.scene.name}' is already staged.");
            }

            _staged = await LoadAndPrepareAsync(sceneName, cancellationToken);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async UniTask CommitStagedAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            ContentSceneRoot candidate = _staged ?? throw new InvalidOperationException(
                "No staged scene is waiting for commit.");
            _staged = null;
            await CommitAsync(candidate, cancellationToken);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async UniTask DiscardStagedAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            if (_staged == null)
            {
                return;
            }

            ContentSceneRoot staged = _staged;
            _staged = null;
            await staged.DisposeAsync();
            UnloadInBackground(staged.gameObject.scene);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async UniTask RestartCurrentAsync(CancellationToken cancellationToken = default)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            ContentSceneRoot current = _current ?? throw new InvalidOperationException(
                "Cannot restart a scene before the first scene has entered.");
            await current.ExitAsync(cancellationToken);
            await current.PrepareAsync(resolver, checked(_generation + 1), cancellationToken);
            await current.EnterAsync(cancellationToken);
            _generation++;
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _transitionGate.Dispose();
    }

    private async UniTask TransitionExclusiveAsync(
        string sceneName,
        CancellationToken cancellationToken)
    {
        ContentSceneRoot candidate = await LoadAndPrepareAsync(sceneName, cancellationToken);
        await CommitAsync(candidate, cancellationToken);
    }

    private async UniTask<ContentSceneRoot> LoadAndPrepareAsync(
        string sceneName,
        CancellationToken cancellationToken)
    {
        Debug.Log($"[SceneCoordinator] Loading scene '{sceneName}'...");
        Scene candidateScene = SceneManager.GetSceneByName(sceneName);
        bool loadedHere = !candidateScene.isLoaded;
        try
        {
            if (loadedHere)
            {
                await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).ToUniTask();
                candidateScene = FindNewestLoadedScene(sceneName);
                cancellationToken.ThrowIfCancellationRequested();
            }

            Debug.Log($"[SceneCoordinator] Preparing ContentSceneRoot for '{sceneName}'...");
            ContentSceneRoot candidate = RequireSceneRoot(candidateScene);
            await candidate.PrepareAsync(resolver, checked(_generation + 1), cancellationToken);
            Debug.Log($"[SceneCoordinator] ContentSceneRoot prepared for '{sceneName}'.");
            return candidate;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneCoordinator] Failed to load/prepare '{sceneName}': {ex.Message}\n{ex.StackTrace}");
            if (loadedHere && candidateScene.IsValid() && candidateScene.isLoaded)
            {
                UnloadInBackground(candidateScene);
            }

            throw;
        }
    }

    private async UniTask CommitAsync(
        ContentSceneRoot candidate,
        CancellationToken cancellationToken)
    {
        ContentSceneRoot? previous = _current;
        bool previousExited = false;
        try
        {
            if (previous != null)
            {
                await previous.ExitAsync(cancellationToken);
                previousExited = true;
            }

            SceneManager.SetActiveScene(candidate.gameObject.scene);
            await candidate.EnterAsync(cancellationToken);
            _current = candidate;
            _generation++;
            Debug.Log($"[SceneCoordinator] Scene '{candidate.gameObject.scene.name}' entered successfully.");

            if (previous != null)
            {
                await previous.DisposeAsync();
                UnloadInBackground(previous.gameObject.scene);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneCoordinator] Failed to commit '{candidate.gameObject.scene.name}': {ex.Message}\n{ex.StackTrace}");
            await candidate.DisposeAsync();
            UnloadInBackground(candidate.gameObject.scene);
            if (previous != null && previousExited)
            {
                SceneManager.SetActiveScene(previous.gameObject.scene);
                await previous.PrepareAsync(resolver, _generation, CancellationToken.None);
                await previous.EnterAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static ContentSceneRoot RequireSceneRoot(Scene scene)
    {
        ContentSceneRoot? result = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (ContentSceneRoot candidate in root.GetComponentsInChildren<ContentSceneRoot>(true))
            {
                if (result != null)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scene.name}' contains more than one ContentSceneRoot.");
                }

                result = candidate;
            }
        }

        return result ?? throw new InvalidOperationException(
            $"Scene '{scene.name}' has no ContentSceneRoot.");
    }

    private static Scene FindNewestLoadedScene(string sceneName)
    {
        for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            Scene scene = SceneManager.GetSceneAt(index);
            if (scene.isLoaded && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
            {
                return scene;
            }
        }

        throw new InvalidOperationException($"Scene '{sceneName}' did not finish loading.");
    }

    private static void UnloadInBackground(Scene scene)
    {
        AsyncOperation? operation = SceneManager.UnloadSceneAsync(scene);
        if (operation == null)
        {
            Debug.LogError($"Failed to begin unloading scene '{scene.name}'.");
        }
    }
}
