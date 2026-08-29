#nullable enable

using UnityEngine;

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

    /// <summary>
    /// Neutral view over a robot, published/consumed across assembly boundaries.
    /// Implemented by <c>Robot</c>; contracts keep only the surface that
    /// networking/UI/audio consumers actually touch, so presentation and
    /// domain types never leak into the contracts layer.
    /// </summary>
    public interface IRobotView
    {
        Transform transform { get; }

        uint BotId { get; }

        bool IsMetadataLoaded { get; }

        bool IsVisualsLoaded { get; }

        float LogicalFacingAngle { get; }

        void Initialize(uint botId);

        void SetMetadata(int playerId, byte clanId, string nickname, string skinPath, string tailPath);

        void SetPosition(ushort x, ushort y);

        void SetRotation(byte rotation);
    }

    public interface IRobotService
    {
        void RegisterRobot(IRobotView robot);
        void UnregisterRobot(uint botId);
        IRobotView GetOrCreateRobot(uint botId);
        void UpdateRobotMetadata(uint botId, RobotMetadata metadata);
        void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation);
        void SetLocalPlayerBotId(uint botId);
        uint LocalPlayerBotId { get; }
        void ClearAllRobots();
        int RobotCount { get; }
    }
}
