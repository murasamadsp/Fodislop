#nullable enable

using System;
using System.IO;
using System.Text;
using Fodinae.Core;
using Fodinae.Core.DI;
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
using VContainer;

namespace Fodinae
{
    public class DiagnosticRunner : MonoBehaviour
    {
        // Диагностика пишется в persistentDataPath (Application.dataPath/.. — каталог
        // установки: на Windows в Program Files это UnauthorizedAccessException каждые
        // 5 секунд). Файлы живут только в dev-сборках/редакторе.
        private static string LogPath =>
            Path.Combine(Application.persistentDataPath, "diagnostic.txt");
        private static string MemoryLogPath =>
            Path.Combine(Application.persistentDataPath, "memory_growth.txt");
        private static readonly object MemoryLogWriteLock = new();
        private float _nextMemorySampleTime;
        private Camera? _mainCamera;

        [Inject]
        private ISessionContainer _session = null!;

        protected void Awake()
        {
            _mainCamera = GameplayCamera.Resolve();
        }

        protected void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Time.unscaledTime >= _nextMemorySampleTime)
            {
                _nextMemorySampleTime = Time.unscaledTime + 5f;
                if (_session != null)
                {
                    WriteMemorySample();
                }
            }

            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                WriteSnapshot();
            }
#endif
        }

        private void WriteMemorySample()
        {
            if (_session == null)
            {
                return;
            }

            MapStorage? ms = _session.TryResolve<MapStorage>();
            TerrariaLightingEngine? lighting = _session.TryResolve<TerrariaLightingEngine>();
            string line =
                $"t={Time.unscaledTime:F1}s frame={Time.frameCount} " +
                $"allocated={Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"reserved={Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f):F1}MB " +
                $"graphics={Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f):F1}MB " +
                $"mono={Profiler.GetMonoUsedSizeLong() / (1024f * 1024f):F1}MB " +
                $"gc={System.GC.GetTotalMemory(false) / (1024f * 1024f):F1}MB " +
                $"allocRate={Fodinae.Core.FrameProfiler.GcAllocTotalPerSecondBytes / (1024f * 1024f):F2}MB/s " +
                $"collections={Fodinae.Core.FrameProfiler.GcCollectionCount} " +
                $"runtimeEffects={RuntimeEffekseerLoader.ActiveRuntimeEffectCount} " +
                $"chunks={ms?.CellLayer?.GetLoadedCount() ?? 0} " +
                $"lightingSolves={lighting?.SolveCount ?? 0} " +
                $"lightingContactAOSolves={lighting?.ContactOcclusionSolveCount ?? 0} " +
                $"dynamicLights={lighting?.DynamicLightCount ?? 0} " +
                $"dynamicUploaded={lighting?.UploadedDynamicLightCount ?? 0} " +
                $"dynamicDropped={lighting?.DroppedDynamicLightCount ?? 0} " +
                $"lightingField={lighting?.FieldWidth ?? 0}x{lighting?.FieldHeight ?? 0} " +
                $"lightingAtlas={lighting?.AtlasEntryCount ?? 0}\n";

            // Off the main thread. File.AppendAllText opens, writes and closes
            // the file synchronously; on a five-second timer that is a periodic
            // main-thread stall in exactly the builds this component runs in -
            // the editor and development builds, which is where anyone is
            // looking at a frame graph. The line is already fully built, so
            // nothing Unity-thread-affine crosses over.
            string sampleLine = line;
            string logPath = MemoryLogPath;
            System.Threading.Tasks.Task.Run(() =>
            {
                lock (MemoryLogWriteLock)
                {
                    File.AppendAllText(logPath, sampleLine);
                }
            });

        }

        private void WriteSnapshot()
        {
            if (_session == null)
            {
                Debug.LogWarning(
                    "[DiagnosticRunner] Snapshot skipped: session container is not available yet.");
                return;
            }

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
            W(sb, "IWorldDataStorage", _session.TryResolve<IWorldDataStorage>());
            W(sb, "INetworkService", _session.TryResolve<INetworkService>());
            W(sb, "IConnectionService", _session.TryResolve<IConnectionService>());
            W(sb, "IMapDataProvider", _session.TryResolve<IMapDataProvider>());
            W(sb, "IAssetLoader", _session.TryResolve<IAssetLoader>());
            W(sb, "IInputBlocker", _session.TryResolve<IInputBlocker>());
            W(sb, "IRobotService", _session.TryResolve<IRobotService>());
            W(sb, "MapManager", _session.TryResolve<MapManager>());
            W(sb, "GameManager", _session.TryResolve<GameManager>());
            W(sb, "RobotManager", _session.TryResolve<RobotManager>());
            W(sb, "PackManager", _session.TryResolve<PackManager>());
            W(sb, "PacketHandler", _session.TryResolve<PacketHandler>());

            sb.AppendLine("\n[MAP]");
            MapStorage? ms = _session.TryResolve<MapStorage>();
            if (ms != null)
            {
                sb.AppendLine(
                    $"  Ready={ms.IsReady} Disposed={ms.IsDisposed} Hash={ms.GetHashCode()}");
                if (ms.CellLayer != null)
                {
                    sb.AppendLine($"  CellChunks loaded={ms.CellLayer.GetLoadedCount()} dirty={ms.CellLayer.GetDirtyCount()} max={ms.CellLayer.MaxChunksInMemory}");
                }
            }
            else
            {
                sb.AppendLine("  NULL (not in world scene)");
            }

            var mm = _session.TryResolve<MapManager>();
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
            var bl = _session.TryResolve<IInputBlocker>();
            sb.AppendLine($"  IInputBlocker: {(bl != null ? $"IsInputBlocked={bl.IsInputBlocked}" : "NULL")}");
            sb.AppendLine($"  Keyboard.current: {(Keyboard.current != null ? "OK" : "NULL")}");
            sb.AppendLine($"  ChatInput.IsFocused: {ChatInput.IsFocused}");
            sb.AppendLine($"  PauseMenu.IsMenuOpen: {Fodinae.UI.PauseMenu.IsMenuOpen}");

            sb.AppendLine("\n[GAME]");
            var gm = _session.TryResolve<GameManager>();
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
            var cam = _mainCamera;
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
