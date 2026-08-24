#nullable enable

using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
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

        [Inject]
        private IObjectResolver _resolver = null!;

        public static bool ShowDebugVisuals { get; set; }

        public uint LocalPlayerBotId { get; private set; }

        public int RobotCount => _robots.Count;

        public void RegisterRobot(Robot robot)
        {
            if (robot == null)
            {
                Debug.LogWarning($"{TAG} RegisterRobot called with null robot");
                return;
            }

            // Same instance re-registered (e.g. Start() + Initialize()) — idempotent.
            uint? staleKey = null;
            foreach (var kvp in _robots)
            {
                if (ReferenceEquals(kvp.Value, robot) && kvp.Key != robot.BotId)
                {
                    staleKey = kvp.Key;
                    break;
                }
            }

            if (staleKey.HasValue)
            {
                _robots.Remove(staleKey.Value);
            }

            if (_robots.TryGetValue(robot.BotId, out var existing))
            {
                if (ReferenceEquals(existing, robot))
                {
                    return;
                }

                Debug.LogWarning($"{TAG} Robot {robot.BotId} already registered, overwriting");
            }

            _robots[robot.BotId] = robot;
        }

        public Robot GetOrCreateRobot(uint botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                return robot;
            }

            if (botId != 0 && botId == LocalPlayerBotId)
            {
                var pmc = Fodinae.Player.Logic.PlayerMovementController.LocalPlayer;
                var playerObj = pmc != null ? pmc.gameObject : GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    robot = playerObj.GetComponent<Robot>();
                    if (robot != null)
                    {
                        robot.Initialize(botId);
                        _robots[botId] = robot;
                        return robot;
                    }
                }
            }

            GameObject robotGo = new GameObject($"Robot_{botId}");
            robotGo.transform.SetParent(transform);

            robot = robotGo.GetComponent<Robot>();
            if (robot == null)
            {
                robot = robotGo.AddComponent<Robot>();
            }

            // Runtime-created components never reach GameLifetimeScope's startup
            // injection scan — inject explicitly so Robot's [Inject] fields are filled.
            _resolver?.Inject(robot);

            robot.Initialize(botId);
            _robots[botId] = robot;
            return robot;
        }

        public void UpdateRobotPosition(uint botId, ushort x, ushort y, byte rotation)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetPosition(x, y);
            robot.SetRotation(rotation);
        }

        public void UpdateRobotMetadata(uint botId, RobotMetadata metadata)
        {
            var robot = GetOrCreateRobot(botId);
            robot.SetMetadata(metadata.PlayerId, metadata.ClanId, metadata.Nickname, metadata.SkinPath, metadata.TailPath);
        }

        public void SetLocalPlayerBotId(uint botId)
        {
            LocalPlayerBotId = botId;
        }

        public void RemoveRobot(uint botId)
        {
            if (_robots.TryGetValue(botId, out var robot))
            {
                Destroy(robot.gameObject);
                _robots.Remove(botId);
            }
            else
            {
                Debug.LogWarning($"{TAG} RemoveRobot: bot {botId} not found");
            }
        }

        public void ClearAllRobots()
        {
            int cleared = 0;
            var keysToRemove = new List<uint>();
            foreach (var kvp in _robots)
            {
                if (kvp.Key == LocalPlayerBotId || (kvp.Value != null && kvp.Value.gameObject.CompareTag("Player")))
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

            Debug.Log($"{TAG} Cleared {cleared} robots, kept {(_robots.ContainsKey(LocalPlayerBotId) ? "local player" : "none")}");
        }

        public void UnregisterRobot(uint botId)
        {
            _robots.Remove(botId);
        }
    }
}
