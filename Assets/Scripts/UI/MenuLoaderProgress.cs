#nullable enable

using System.Collections.Generic;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking.Connection;
using Fodinae.Player.Logic;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    /// <summary>
    /// Отвечает за экран загрузки/спуска главного меню: список фаз загрузки,
    /// вычисление текущей фазы по состоянию сессии и обновление визуального
    /// прогресса (шкала, подпись фазы, счётчик, список шагов).
    /// </summary>
    internal sealed class MenuLoaderProgress
    {
        private enum LoadPhase
        {
            Handshake,
            WorldManifest,
            SpawnSync,
            TerrainMesh,
            SurfaceAssets,
            Done,
        }

        private static readonly (LoadPhase Phase, string Label)[] PhaseSteps =
        {
            (LoadPhase.Handshake, "Подключение к серверу"),
            (LoadPhase.WorldManifest, "Загрузка карты мира"),
            (LoadPhase.SpawnSync, "Синхронизация позиции"),
            (LoadPhase.TerrainMesh, "Построение террейна"),
            (LoadPhase.SurfaceAssets, "Загрузка текстур"),
        };

        private readonly VisualElement? _loaderProgressFill;
        private readonly Label? _loaderPhaseLabel;
        private readonly Label? _loaderPhaseCount;
        private readonly VisualElement? _loaderPhaseList;

        private readonly List<(VisualElement Item, Label Icon)> _phaseItems = new();

        public MenuLoaderProgress(
            VisualElement? loaderProgressFill,
            Label? loaderPhaseLabel,
            Label? loaderPhaseCount,
            VisualElement? loaderPhaseList)
        {
            _loaderProgressFill = loaderProgressFill;
            _loaderPhaseLabel = loaderPhaseLabel;
            _loaderPhaseCount = loaderPhaseCount;
            _loaderPhaseList = loaderPhaseList;

            BuildPhaseList();
        }

        private void BuildPhaseList()
        {
            if (_loaderPhaseList == null)
            {
                return;
            }

            _phaseItems.Clear();
            _loaderPhaseList.Clear();
            foreach ((LoadPhase _, string label) in PhaseSteps)
            {
                var item = new VisualElement();
                item.AddToClassList("mm-loader-phase-item");

                var icon = new Label("○");
                icon.AddToClassList("mm-loader-phase-icon");
                item.Add(icon);

                var text = new Label(label);
                item.Add(text);

                _loaderPhaseList.Add(item);
                _phaseItems.Add((item, icon));
            }
        }

        private static LoadPhase ComputeLoadPhase(ISessionContainer? session)
        {
            if (session == null)
            {
                return LoadPhase.Handshake;
            }

            IConnectionService? connectionService = session.TryResolve<IConnectionService>();
            if (connectionService == null || !connectionService.IsConnected)
            {
                return LoadPhase.Handshake;
            }

            MapManager? mapManager = session.TryResolve<MapManager>();
            if (mapManager == null || !mapManager.IsWorldInitialized)
            {
                return LoadPhase.WorldManifest;
            }

            PlayerMovementController? player = PlayerMovementController.LocalPlayer;
            if (player == null || !player.HasServerPosition)
            {
                return LoadPhase.SpawnSync;
            }

            Robot? robot = player.GetComponent<Robot>();
            if (robot == null || !robot.IsMetadataLoaded)
            {
                return LoadPhase.SpawnSync;
            }

            IPlayerStats? stats = session.TryResolve<IPlayerStats>();
            if (stats == null || !stats.IsReady)
            {
                return LoadPhase.SpawnSync;
            }

            TerrainRenderer? terrain = session.TryResolve<TerrainRenderer>();
            if (terrain == null || !terrain.IsReadyForGameplay)
            {
                return LoadPhase.TerrainMesh;
            }

            ITextureService? textureService = session.TryResolve<ITextureService>();
            IAssetLoader? assetLoader = session.TryResolve<IAssetLoader>();
            bool assetsBusy = (textureService != null && textureService.PendingCellTextureRequests > 0) ||
                (assetLoader is ClientAssetLoader clientAssetLoader &&
                    (clientAssetLoader.PendingAssetCount > 0 || clientAssetLoader.QueuedAssetCount > 0));
            if (assetsBusy)
            {
                return LoadPhase.SurfaceAssets;
            }

            return LoadPhase.Done;
        }

        public void UpdateProgress(ISessionContainer? session)
        {
            LoadPhase phase = ComputeLoadPhase(session);
            int phaseIndex = (int)phase;
            int totalPhases = PhaseSteps.Length;

            float progress = Mathf.Clamp01((float)phaseIndex / totalPhases);

            if (_loaderProgressFill != null)
            {
                _loaderProgressFill.style.width = new Length(progress * 100f, LengthUnit.Percent);
            }

            if (_loaderPhaseLabel != null)
            {
                _loaderPhaseLabel.text = phaseIndex < totalPhases
                    ? PhaseSteps[phaseIndex].Label
                    : "Готово к высадке";
            }

            if (_loaderPhaseCount != null)
            {
                _loaderPhaseCount.text = $"{Mathf.Min(phaseIndex + 1, totalPhases)} / {totalPhases}";
            }

            for (int i = 0; i < _phaseItems.Count; i++)
            {
                (VisualElement item, Label icon) = _phaseItems[i];
                bool isDone = i < phaseIndex;
                bool isActive = i == phaseIndex;
                item.EnableInClassList("mm-loader-phase-item--done", isDone);
                item.EnableInClassList("mm-loader-phase-item--active", isActive);
                icon.text = isDone ? "✓" : isActive ? "◆" : "○";
            }
        }
    }
}
