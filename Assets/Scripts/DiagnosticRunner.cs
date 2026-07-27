using System.IO;
using System.Text;
using Fodinae.Scripts.Core;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.Game;
using Fodinae.Scripts.Game.Managers;
using Fodinae.Scripts.Networking;
using Fodinae.Scripts.Networking.Connection;
using Fodinae.Scripts.Player.Logic;
using Fodinae.Scripts.UI;
using Fodinae.Scripts.World;
using Fodinae.Scripts.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.Scripts
{
    public class DiagnosticRunner : MonoBehaviour
    {
        private static readonly string LogPath = Path.Combine(Application.dataPath, "..", "diagnostic.txt");

        private float _lastHeartbeat;
        private int _lastPacketCount;
        private int _lastFrame;
        private Vector2Int _lastPlayerPos;
        private bool _terrainUpdating;
        private int _terrainUpdateCount;
        private int _robotCount;
        private float _lastRobotMoveTime;
        private Vector3 _lastRobotPos;
        private int _heartbeatIndex;

        protected void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                WriteSnapshot();
                return;
            }

            if (Time.time - _lastHeartbeat < 1f)
            {
                return;
            }

            _lastHeartbeat = Time.time;
            _heartbeatIndex++;

            var sb = new StringBuilder();
            sb.AppendLine($"--- HEARTBEAT #{_heartbeatIndex} frame={Time.frameCount} t={Time.time:F1}s delta={Time.deltaTime:F4}s ---");

            var mm = ServiceLocator.Resolve<MapManager>();
            sb.AppendLine($"  MapManager: {(mm != null ? $"Init={mm.IsWorldInitialized} '{mm.WorldCodeName}'" : "NULL")}");

            var ms = ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            sb.AppendLine($"  MapStorage: {(ms != null ? $"Ready={ms.IsReady} Disposed={ms.IsDisposed}" : "NULL")}");

            var player = PlayerMovementController.LocalPlayer;
            if (player != null)
            {
                sb.AppendLine($"  Player: Pos={player.Position} GO_active={player.gameObject.activeInHierarchy} GO_selfactive={player.isActiveAndEnabled}");
                if (player.Position != _lastPlayerPos)
                {
                    sb.AppendLine($"  Player MOVED {_lastPlayerPos} -> {player.Position}");
                    _lastPlayerPos = player.Position;
                }
            }
            else
            {
                sb.AppendLine("  Player: NULL");
            }

            var blocker = ServiceLocator.Resolve<IInputBlocker>();
            sb.AppendLine($"  Input: Blocked={(blocker != null ? blocker.IsInputBlocked : (bool?)null)} Chat={ChatInput.IsFocused} KB={Keyboard.current != null}");

            var cm = ServiceLocator.Resolve<IConnectionService>() as ConnectionManager;
            if (cm != null)
            {
                var conn = cm.Connection;
                sb.AppendLine($"  Connection: Connected={cm.IsConnected} Status={(conn != null ? conn.ConnectionStatus.ToString() : "NULL")}");
            }

            var robots = FindObjectsByType<Robot>(FindObjectsInactive.Exclude);
            if (robots.Length != _robotCount)
            {
                sb.AppendLine($"  Robots: {_robotCount} -> {robots.Length}");
                _robotCount = robots.Length;
            }

            foreach (var r in robots)
            {
                if (r.IsLocalPlayer)
                {
                    continue;
                }

                if (r.transform.position != _lastRobotPos)
                {
                    _lastRobotPos = r.transform.position;
                    _lastRobotMoveTime = Time.time;
                }
            }

            if (Time.time - _lastRobotMoveTime > 5f && robots.Length > 1)
            {
                sb.AppendLine($"  WARNING: No robot movement for {Time.time - _lastRobotMoveTime:F1}s");
            }

            var terrain = FindAnyObjectByType<TerrainRenderer>();
            sb.AppendLine($"  Terrain: {(terrain != null ? $"Active={terrain.gameObject.activeInHierarchy} Enabled={terrain.enabled}" : "NULL")}");

            var cam = Camera.main;
            sb.AppendLine($"  Camera: {(cam != null ? $"Active={cam.gameObject.activeInHierarchy} Pos={cam.transform.position}" : "NULL")}");

            sb.AppendLine($"  TimeScale={Time.timeScale} framerate={1f / Time.deltaTime:F0}");

            File.AppendAllText(LogPath, sb.ToString());
        }

        private void WriteSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SNAPSHOT frame={Time.frameCount} time={Time.time:F2}s ===");

            sb.AppendLine("\n[SERVICES]");
            W(sb, "IWorldDataStorage", ServiceLocator.Resolve<IWorldDataStorage>());
            W(sb, "INetworkService", ServiceLocator.Resolve<INetworkService>());
            W(sb, "IConnectionService", ServiceLocator.Resolve<IConnectionService>());
            W(sb, "IMapDataProvider", ServiceLocator.Resolve<IMapDataProvider>());
            W(sb, "IAssetLoader", ServiceLocator.Resolve<IAssetLoader>());
            W(sb, "IInputBlocker", ServiceLocator.Resolve<IInputBlocker>());
            W(sb, "IRobotService", ServiceLocator.Resolve<IRobotService>());
            W(sb, "MapManager", ServiceLocator.Resolve<MapManager>());
            W(sb, "GameManager", ServiceLocator.Resolve<GameManager>());
            W(sb, "RobotManager", ServiceLocator.Resolve<RobotManager>());
            W(sb, "PackManager", ServiceLocator.Resolve<PackManager>());
            W(sb, "PacketHandler", ServiceLocator.Resolve<PacketHandler>());

            sb.AppendLine("\n[MAP]");
            var ms = ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            sb.AppendLine(ms != null
                ? $"  Ready={ms.IsReady} Disposed={ms.IsDisposed} Hash={ms.GetHashCode()}"
                : "  NULL");
            var mm = ServiceLocator.Resolve<MapManager>();
            sb.AppendLine(mm != null
                ? $"  Initialized={mm.IsWorldInitialized} '{mm.WorldCodeName}' {mm.WorldWidth}x{mm.WorldHeight} Hash={mm.GetHashCode()}"
                : "  NULL");

            sb.AppendLine("\n[PLAYER]");
            var p = PlayerMovementController.LocalPlayer;
            if (p == null)
            {
                sb.AppendLine("  NULL");
            }
            else
            {
                var go = p.gameObject;
                sb.AppendLine($"  BotId={p.BotId} Pos={p.Position}");
                sb.AppendLine($"  GO={go.name} activeInHierarchy={go.activeInHierarchy} isActiveAndEnabled={p.isActiveAndEnabled}");
                sb.AppendLine($"  Transform local={go.transform.localPosition} world={go.transform.position}");
                sb.AppendLine($"  GO.layer={go.layer} GO.tag={go.tag}");
                var rb = go.GetComponent<Rigidbody2D>();
                sb.AppendLine($"  Rigidbody2D: {(rb != null ? $"bodyType={rb.bodyType} simulating={rb.simulated}" : "NONE")}");
            }

            sb.AppendLine("\n[INPUT]");
            var bl = ServiceLocator.Resolve<IInputBlocker>();
            sb.AppendLine($"  IInputBlocker: {(bl != null ? $"IsInputBlocked={bl.IsInputBlocked}" : "NULL")}");
            sb.AppendLine($"  Keyboard.current: {(Keyboard.current != null ? "OK" : "NULL")}");
            sb.AppendLine($"  ChatInput.IsFocused: {ChatInput.IsFocused}");
            sb.AppendLine($"  PauseMenu.IsMenuOpen: {Fodinae.Scripts.UI.PauseMenu.IsMenuOpen}");

            sb.AppendLine("\n[GAME]");
            var gm = ServiceLocator.Resolve<GameManager>();
            sb.AppendLine(gm != null
                ? $"  State={gm.CurrentState} Authorized={gm.IsUIAuthorized}"
                : "  NULL");

            sb.AppendLine("\n[TERRAIN]");
            var terrain = FindAnyObjectByType<TerrainRenderer>();
            if (terrain != null)
            {
                sb.AppendLine($"  activeInHierarchy={terrain.gameObject.activeInHierarchy} enabled={terrain.enabled}");
                var mf = terrain.GetComponent<MeshFilter>();
                sb.AppendLine($"  MeshFilter: {(mf != null && mf.sharedMesh != null ? $"verts={mf.sharedMesh.vertexCount}" : "NO MESH")}");
                var mr = terrain.GetComponent<MeshRenderer>();
                sb.AppendLine($"  MeshRenderer: {(mr != null ? $"enabled={mr.enabled} materials={mr.sharedMaterials.Length} sortingOrder={mr.sortingOrder}" : "NONE")}");
            }
            else
            {
                sb.AppendLine("  NOT FOUND");
            }

            sb.AppendLine("\n[CAMERA]");
            var cam = Camera.main;
            sb.AppendLine(cam != null
                ? $"  pos={cam.transform.position} ortho={cam.orthographic} size={cam.orthographicSize} active={cam.gameObject.activeInHierarchy}"
                : "  NULL");

            sb.AppendLine("\n[ENTITIES]");
            foreach (var r in FindObjectsByType<Robot>(FindObjectsInactive.Exclude))
            {
                var rgo = r.gameObject;
                sb.AppendLine($"  #{r.BotId} local={r.IsLocalPlayer} GO={rgo.name} active={rgo.activeInHierarchy} pos={r.transform.position}");
            }

            foreach (var pk in FindObjectsByType<Fodinae.Scripts.Game.Pack>(FindObjectsInactive.Exclude))
            {
                sb.AppendLine($"  Pack {pk.name} pos={pk.transform.position}");
            }

            sb.AppendLine("\n[TIME]");
            sb.AppendLine($"  timeScale={Time.timeScale} deltaTime={Time.deltaTime:F4} frame={Time.frameCount}");

            sb.AppendLine("=== END ===\n");
            File.WriteAllText(LogPath, sb.ToString());
            Debug.Log($"[Diagnostic] Snapshot -> {LogPath}");
        }

        private static void W(StringBuilder sb, string name, object obj)
        {
            sb.AppendLine(obj != null
                ? $"  {name}: OK [{obj.GetType().Name} #{obj.GetHashCode()}]"
                : $"  {name}: NULL");
        }
    }
}
