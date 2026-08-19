#nullable enable

using Fodinae.Game;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Grouped metadata for a robot, sent as a single unit from the server.
    /// </summary>
    public readonly record struct RobotMetadata(
        int PlayerId,
        byte ClanId,
        string Nickname,
        string SkinPath,
        string TailPath);

    public interface IRobotService
    {
        void RegisterRobot(Robot robot);
        void UnregisterRobot(uint botId);
        Robot GetOrCreateRobot(uint botId);
        void UpdateRobotMetadata(uint botId, RobotMetadata metadata);
        void SetLocalPlayerBotId(uint botId);
        uint LocalPlayerBotId { get; }
        void ClearAllRobots();
        int RobotCount { get; }
    }
}
