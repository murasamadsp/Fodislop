#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Audio.Backend;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player;
using Fodinae.Player.Logic;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using Fodinae.UI.HUD.Inventory.View;
using Fodinae.UI.HUD.Player.View;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using MinesServer.Networking.Connection.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VContainer;
using VContainer.Unity;

namespace Fodinae.Core
{
    public sealed class GameBootstrap : IPostStartable
    {
        private readonly IProjectDefaults _projectDefaults;
        private readonly IClientConfigManager _clientConfig;
        private readonly IAudioSystem _audioSystem;
        private readonly IConnectionService _connection;
        private readonly NetworkService _network;
        private readonly PacketHandler _packetHandler;
        private readonly ClientAssetLoader _clientAssetLoader;
        private readonly TerrainRenderer _terrain;
        private readonly PostProcessController _postProcess;
        private readonly LightingEngine _lighting;
        private readonly SurfaceRenderer _surface;
        private readonly GameManager _gameManager;
        private readonly ServerConfig _serverConfig;
        private readonly PlayerHUDView _playerHud;
        private readonly InventoryView _inventory;
        private readonly Scene _ownScene;
        private readonly GameLifetimeScope _scope;
        private readonly SceneTransitionTicket _ticket;

        public GameBootstrap(
            IProjectDefaults projectDefaults,
            IClientConfigManager clientConfig,
            IAudioSystem audioSystem,
            IConnectionService connection,
            NetworkService network,
            PacketHandler packetHandler,
            ClientAssetLoader clientAssetLoader,
            TerrainRenderer terrain,
            PostProcessController postProcess,
            LightingEngine lighting,
            SurfaceRenderer surface,
            GameManager gameManager,
            ServerConfig serverConfig,
            PlayerHUDView playerHud,
            InventoryView inventory,
            Scene ownScene,
            GameLifetimeScope scope,
            SceneTransitionTicket ticket)
        {
            _projectDefaults = projectDefaults;
            _clientConfig = clientConfig;
            _audioSystem = audioSystem;
            _connection = connection;
            _network = network;
            _packetHandler = packetHandler;
            _clientAssetLoader = clientAssetLoader;
            _terrain = terrain;
            _postProcess = postProcess;
            _lighting = lighting;
            _surface = surface;
            _gameManager = gameManager;
            _serverConfig = serverConfig;
            _playerHud = playerHud;
            _inventory = inventory;
            _ownScene = ownScene;
            _scope = scope;
            _ticket = ticket;
            _ticket.Attach(_ownScene);
        }

        public void PostStart()
        {
            StartAsync().Forget();
        }

        private async UniTaskVoid StartAsync()
        {
            CancellationToken scopeToken = _scope.destroyCancellationToken;
            try
            {
                await _ticket.WaitForActivationAsync()
                    .AttachExternalCancellation(scopeToken);
                _scope.ActivateSceneServices();
                InitializeRequiredServices();
                InitializeOfflineServerConfig();
                _terrain.ApplyClientConfig();
                _postProcess.EnsureVolumeSetup();
                _lighting.EnsureInitialized();
                _surface.ApplyClientConfig();
                _gameManager.EnsureUISetup();
                _playerHud.EnsureInitialized();
                _inventory.EnsureInitialized();
                _connection.Connect();
                ValidateStartup();
                _ticket.MarkStartupReady();
                await WaitForWorldReadyAsync();
                // Presentation readiness requires the required audio banks to
                // be resident: world audio must be live the moment the game
                // scene is shown, not pop in when the background load lands.
                await _audioSystem.WaitUntilBanksReadyAsync(scopeToken);
                if (!_serverConfig.IsInitialized)
                {
                    throw new InvalidOperationException(
                        "MainGame presentation contract: authored ServerConfig was not initialized by world startup.");
                }

                _scope.MarkReady();
                _ticket.MarkPresentationReady();
            }
            catch (OperationCanceledException) when (scopeToken.IsCancellationRequested)
            {
                _ticket.Fail(new OperationCanceledException(
                    $"Game scene '{_ownScene.name}' was destroyed during startup."));
            }
            catch (Exception exception)
            {
                _scope.MarkFailed(exception);
                _ticket.Fail(exception);
            }
        }

        private async UniTask WaitForWorldReadyAsync()
        {
            CancellationToken scopeToken = _scope.destroyCancellationToken;
            if (_gameManager.IsWorldLoaded)
            {
                return;
            }

            var completion = new UniTaskCompletionSource();
            void OnWorldLoaded()
            {
                completion.TrySetResult();
            }

            _gameManager.OnWorldLoaded += OnWorldLoaded;
            try
            {
                if (_gameManager.IsWorldLoaded)
                {
                    return;
                }

                UniTask worldReady = completion.Task.AttachExternalCancellation(scopeToken);
                UniTask transitionFailed = _ticket.WaitForFailureAsync().AttachExternalCancellation(scopeToken);
                int winner = await UniTask.WhenAny(worldReady, transitionFailed);
                if (winner == 1)
                {
                    await _ticket.WaitForPresentationAsync();
                }
            }
            finally
            {
                _gameManager.OnWorldLoaded -= OnWorldLoaded;
            }
        }

        private void InitializeOfflineServerConfig()
        {
            // DummyConnection has no server-config packet. Seed the runtime
            // contract that a real server supplies before gameplay input can
            // query cooldown and chat limits.
            if (_connection is DummyConnection && !_serverConfig.IsInitialized)
            {
                _serverConfig.ApplyValues(0.3f, 256, 256);
            }
        }

        private void InitializeRequiredServices()
        {
            _clientConfig.EnsureInitialized();
            _network.EnsureConnectionSubscription();
            if (!_network.IsConnectionSubscriptionEstablished && Application.isPlaying)
            {
                throw new InvalidOperationException("MainGame startup contract: NetworkService subscription failed.");
            }

            _packetHandler.EnsureInitialized();
            _clientAssetLoader.EnsureAssetSubscription();
            if (!_clientAssetLoader.IsAssetSubscriptionEstablished && Application.isPlaying)
            {
                throw new InvalidOperationException("MainGame startup contract: ClientAssetLoader subscription failed.");
            }

            _terrain.EnsureSubscriptions();
        }

        private void ValidateStartup()
        {
            var errors = new List<string>();
            if (_projectDefaults.SchemaVersion != ProjectDefaults.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(_projectDefaults.ContentHash))
            {
                errors.Add("ProjectDefaults snapshot is missing or invalid");
            }

            ValidateRenderAssets(errors);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"[GameBootstrap] FATAL STARTUP FAILURE: {errors.Count} critical systems failed:\n- " +
                    string.Join("\n- ", errors));
            }

            Debug.Log("[GameBootstrap] Startup validation PASSED — MainGame contract is ready");
        }

        private static void ValidateRenderAssets(List<string> errors)
        {
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.Terrain);
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.DynamicEmission);
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.WorldSurface);
            ValidateShader(errors, ProjectRuntimeContracts.ShaderNames.WorldEntity);
            if (Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) == null)
            {
                errors.Add($"Required compute shader Resources/{ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute}.compute is missing");
            }
        }

        private static void ValidateShader(List<string> errors, string shaderName)
        {
            Shader? shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported)
            {
                errors.Add($"Required shader '{shaderName}' is missing or unsupported");
            }
        }
    }
}
