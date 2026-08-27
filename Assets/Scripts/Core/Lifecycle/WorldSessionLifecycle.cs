#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.World;
using VContainer;

namespace Fodinae.Core.Lifecycle
{
    public sealed class WorldSessionLifecycle : LifecycleParticipant
    {
        private ulong _preparedGeneration;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private RobotManager _robotManager = null!;
        [Inject]
        private BuildingManager _buildingManager = null!;
        [Inject]
        private VFXPool _vfxPool = null!;
        [Inject]
        private CameraFollow _cameraFollow = null!;

        public override LifecyclePhase Phase => LifecyclePhase.World;

        protected override UniTask OnPrepareAsync(
            LifecycleContext context,
            CancellationToken cancellationToken)
        {
            if (_preparedGeneration != 0 && _preparedGeneration != context.Generation)
            {
                _mapManager.ResetWorldState();
                _robotManager.ClearAllRobots();
                _buildingManager.ClearAllBuildings();
                _vfxPool.ResetForNewGeneration();
                _cameraFollow.Reinitialize();
            }

            _preparedGeneration = context.Generation;
            return UniTask.CompletedTask;
        }
    }
}
