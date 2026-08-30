#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Networking.Auth;
using VContainer.Unity;

namespace Fodinae.Core;

public sealed class ApplicationBootstrap : IStartable
{
    private readonly BootstrapLifetimeScope _scope;
    private readonly IClientConfigManager _clientConfig;
    private readonly BootstrapLoadingScreen _loadingScreen;
    private readonly IVkOfflineProvider _offlineVk;
    private readonly AsyncOperationSupervisor _operations;

    public ApplicationBootstrap(
        BootstrapLifetimeScope scope,
        IClientConfigManager clientConfig,
        BootstrapLoadingScreen loadingScreen,
        IVkOfflineProvider offlineVk,
        AsyncOperationSupervisor operations)
    {
        _scope = scope;
        _clientConfig = clientConfig;
        _loadingScreen = loadingScreen;
        _offlineVk = offlineVk;
        _operations = operations;
    }

    public void Start()
    {
        // Офлайн-симулятор VK-входа подключается в бутстрапе: VkAuthService
        // пойдёт через него при UseDummyConnection=true (без сети и client_id).
        VkAuthService.OfflineProvider = _offlineVk;
        _operations.Run("application_startup", _ => StartAsync());
    }

    private async UniTask StartAsync()
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
