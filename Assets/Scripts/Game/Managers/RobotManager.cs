#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game;
using Fodinae.World;
using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;

namespace Fodinae.Game.Managers
{
    public class RobotManager : MonoBehaviour, IRobotService
    {
        private const string TAG = "[RobotManager]";
        private Dictionary<uint, Robot> _robots = new();
        private readonly HashSet<uint> _overwriteWarningsLogged = [];

        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ILocalPlayerState _localPlayer = null!;

        public uint LocalPlayerBotID { get; private set; }

        public int RobotCount => _robots.Count;

        public void RegisterRobot(IRobotView robot)
        {
            if (robot is not Robot concrete)
            {
                Debug.LogWarning($"{TAG} RegisterRobot called with non-Robot view");
                return;
            }

            // Same instance re-registered (e.g. Start() + Initialize()) — idempotent.
            uint? staleKey = null;
            foreach (var kvp in _robots)
            {
                if (ReferenceEquals(kvp.Value, robot) && kvp.Key != robot.BotID)
                {
                    staleKey = kvp.Key;
                    break;
                }
            }

            if (staleKey.HasValue)
            {
                _robots.Remove(staleKey.Value);
            }

            if (_robots.TryGetValue(robot.BotID, out var existing))
            {
                if (ReferenceEquals(existing, robot))
                {
                    return;
                }

                // Server resends can target a bot whose stale instance is still
                // registered. Warn once per bot id so a resend storm cannot
                // flood the console.
                if (_overwriteWarningsLogged.Add(robot.BotID))
                {
                    Debug.LogWarning($"{TAG} Robot {robot.BotID} already registered, overwriting");
                }
            }

            _robots[robot.BotID] = concrete;
        }

        public IRobotView GetOrCreateRobot(uint botID)
        {
            if (_robots.TryGetValue(botID, out var robot))
            {
                return robot;
            }

            if (botID != 0 && botID == LocalPlayerBotID)
            {
                var pmc = _localPlayer.Current;
                var playerObj = pmc != null ? pmc.gameObject : null;
                if (playerObj != null)
                {
                    robot = playerObj.GetComponent<Robot>();
                    if (robot != null)
                    {
                        robot.Initialize(botID);
                        _robots[botID] = robot;
                        return robot;
                    }
                }
            }

            robot = _sceneObjects.Create<Robot>($"Robot_{botID}", RuntimeOwner.Robots);

            robot.Initialize(botID);
            _robots[botID] = robot;
            return robot;
        }

        public void UpdateRobotPosition(uint botID, ushort x, ushort y, byte rotation)
        {
            var robot = GetOrCreateRobot(botID);
            robot.SetPosition(x, y);
            robot.SetRotation(rotation);
        }

        public void UpdateRobotMetadata(uint botID, RobotMetadata metadata)
        {
            var robot = GetOrCreateRobot(botID);
            robot.SetMetadata(metadata.PlayerID, metadata.ClanID, metadata.Nickname, metadata.SkinPath, metadata.TailPath);
        }

        public void SetLocalPlayerBotID(uint botID)
        {
            LocalPlayerBotID = botID;
        }

        public void RemoveRobot(uint botID)
        {
            if (_robots.TryGetValue(botID, out var robot))
            {
                Destroy(robot.gameObject);
                _robots.Remove(botID);
            }
            else
            {
                Debug.LogWarning($"{TAG} RemoveRobot: bot {botID} not found");
            }
        }

        public void ClearAllRobots()
        {
            int cleared = 0;
            _overwriteWarningsLogged.Clear();
            var keysToRemove = new List<uint>();
            foreach (var kvp in _robots)
            {
                if (kvp.Key == LocalPlayerBotID || (kvp.Value != null && kvp.Value.gameObject.CompareTag("Player")))
                {
                    continue;
                }

                if (kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }

                keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                _robots.Remove(key);
                cleared++;
            }

            Debug.Log($"{TAG} Cleared {cleared} robots, kept {(_robots.ContainsKey(LocalPlayerBotID) ? "local player" : "none")}");
        }

        public void UnregisterRobot(uint botID)
        {
            _robots.Remove(botID);
            _overwriteWarningsLogged.Remove(botID);
        }
    }
}
