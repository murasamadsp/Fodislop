#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using VContainer.Unity;

namespace Fodinae.Core;

public sealed class ApplicationBootstrap : IStartable
{
    private readonly BootstrapLifetimeScope _scope;
    private readonly IClientConfigManager _clientConfig;
    private readonly BootstrapLoadingScreen _loadingScreen;

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
    }

    public void Start()
    {
        StartAsync().Forget();
    }

    private async UniTaskVoid StartAsync()
    {
        CancellationToken scopeToken = _scope.destroyCancellationToken;
        try
        {
            _clientConfig.EnsureInitialized();
            _loadingScreen.Initialize();
            await RuntimeAssetPaths.EnsureReadyAsync();
            await _scope.TransitionAsync("Gateway", scopeToken);
        }
        catch (OperationCanceledException) when (scopeToken.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
    }
}
