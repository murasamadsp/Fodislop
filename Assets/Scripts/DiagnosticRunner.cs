#nullable enable

using System.IO;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Effekseer;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player.Logic;
using Fodinae.UI;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace Fodinae
{
    public class DiagnosticRunner : MonoBehaviour
    {
        private static readonly string LogPath = Path.Combine(Application.dataPath, "..", "diagnostic.txt");
        private static readonly string MemoryLogPath = Path.Combine(Application.dataPath, "..", "memory_growth.txt");
        private float _nextMemorySampleTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureCreated()
        {
            File.WriteAllText(MemoryLogPath, string.Empty);

            if (FindAnyObjectByType<DiagnosticRunner>() != null)
            {
                return;
            }

            var go = new GameObject("FodinaeDiagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<DiagnosticRunner>();
        }

        protected void Update()
        {
            if (Time.unscaledTime >= _nextMemorySampleTime)
            {
                _nextMemorySampleTime = Time.unscaledTime + 5f;
                WriteMemorySample();
            }

            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                WriteSnapshot();
            }
        }

        private static void WriteMemorySample()
        {
            var ms = ServiceLocator.Resolve<IWorldDataStorage>() as MapStorage;
            var lighting = TerrariaLightingEngine.Instance;
            string line =
                $"t={Time.unscaledTime:F1}s frame={Time.frameCount} " +
                $"allocated={Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"reserved={Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"graphics={Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f):F1}MB " +
                $"mono={Profiler.GetMonoUsedSizeLong() / (1024f * 1024f):F1}MB " +
                $"gc={System.GC.GetTotalMemory(false) / (1024f * 1024f):F1}MB " +
                $"runtimeEffects={RuntimeEffekseerLoader.ActiveRuntimeEffectCount} " +
                $"chunks={ms?.CellLayer?.GetLoadedCount() ?? 0} " +
                $"lightingSolves={lighting?.SolveCount ?? 0} " +
                $"lightingContactAOSolves={lighting?.ContactOcclusionSolveCount ?? 0} " +
                $"dynamicLights={lighting?.DynamicLightCount ?? 0} " +
                $"dynamicUploaded={lighting?.UploadedDynamicLightCount ?? 0} " +
                $"dynamicDropped={lighting?.DroppedDynamicLightCount ?? 0} " +
                $"lightingField={lighting?.FieldWidth ?? 0}x{lighting?.FieldHeight ?? 0} " +
                $"lightingAtlas={lighting?.AtlasEntryCount ?? 0}\n";

            File.AppendAllText(MemoryLogPath, line);
        }

        private void WriteSnapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SNAPSHOT frame={Time.frameCount} time={Time.time:F2}s ===");

            sb.AppendLine("\n[MEMORY]");
            sb.AppendLine($"  TotalAllocated={Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  TotalReserved={Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  GraphicsDriver={Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  MonoUsed={Profiler.GetMonoUsedSizeLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  MonoHeap={Profiler.GetMonoHeapSizeLong() / (1024f * 1024f):F1} MB");
            sb.AppendLine($"  GCHeap={System.GC.GetTotalMemory(false) / (1024f * 1024f):F1} MB");
            sb.AppendLine("  Unity resource object counts omitted; diagnostics do not scan the heap.");
            sb.AppendLine($"  ActiveRuntimeEffects={RuntimeEffekseerLoader.ActiveRuntimeEffectCount}");

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
            if (ms?.CellLayer != null)
            {
                sb.AppendLine($"  CellChunks loaded={ms.CellLayer.GetLoadedCount()} dirty={ms.CellLayer.GetDirtyCount()} max={ms.CellLayer.MaxChunksInMemory}");
            }

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
            sb.AppendLine($"  PauseMenu.IsMenuOpen: {Fodinae.UI.PauseMenu.IsMenuOpen}");

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

            foreach (var pk in FindObjectsByType<Fodinae.Game.Pack>(FindObjectsInactive.Exclude))
            {
                sb.AppendLine($"  Pack {pk.name} pos={pk.transform.position}");
            }

            sb.AppendLine("\n[TIME]");
            sb.AppendLine($"  timeScale={Time.timeScale} deltaTime={Time.deltaTime:F4} frame={Time.frameCount}");

            sb.AppendLine("=== END ===\n");
            File.WriteAllText(LogPath, sb.ToString());
            Debug.Log($"[Diagnostic] Snapshot -> {LogPath}");
        }

        private static void W(StringBuilder sb, string name, object? obj)
        {
            sb.AppendLine(obj != null
                ? $"  {name}: OK [{obj.GetType().Name} #{obj.GetHashCode()}]"
                : $"  {name}: NULL");
        }
    }
}
