#nullable enable

using Fodinae.Scripts.Game;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IRobotService
    {
        void RegisterRobot(Robot robot);
        void UnregisterRobot(uint botId, Robot instance);
        Robot GetOrCreateRobot(uint botId);
        void UpdateRobotMetadata(uint botId, int playerId, byte clanId, string nickname, string skinPath, string tailPath);
        void RemoveRobot(uint botId);
        void ClearAllRobots();
        uint LocalPlayerBotId { get; set; }
    }
}
