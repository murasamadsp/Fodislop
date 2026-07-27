# Architecture Audit & SOLID Refactoring Roadmap

## 1. Current State: "The Big Ball of Mud"

### 1.1 Critical Problems

#### 1.1.1 God-object `PacketHandler`
**File:** `Assets/Scripts/Networking/PacketHandler.cs:109-165`

- 40+ hardcoded subscriptions in `Awake()`
- Every new packet type requires touching `PacketHandler`
- Violates **Single Responsibility** and **Open/Closed Principle**
- No way to add packet processing without modifying the central dispatcher

#### 1.1.2 `GameManager` as Center of Universe
**File:** `Assets/Scripts/Game/Managers/GameManager.cs`

- Knows about: UI, connection, map, inventory, HUD, robots, packs, audio
- Started as Facade, became God object
- Violates **Single Responsibility** — has at least 8 distinct responsibilities
- Tight coupling makes testing impossible

#### 1.1.3 Singleton Explosion
**Files:** `ServiceLocator.cs`, all `*Manager.cs`, `NetworkService.cs`, etc.

- `Instance`, `InstanceIfExists`, `DontDestroyOnLoad` everywhere
- Not DI — global mutable memory with side effects
- Race conditions in `Awake()` order (the exact bug we just fixed)
- No way to mock/replace for tests

#### 1.1.4 Network Layer Tied to Concrete Processors
**Files:** `PacketHandler.cs`, `Networking/Processors/*`

- `PacketHandler` instantiates `WorldInitProcessor`, `MapRegionProcessor` directly
- Cannot add new packet types without modifying `PacketHandler`
- Violates **Dependency Inversion Principle** — high-level module depends on low-level details

#### 1.1.5 UI Toolkit Mixed Paradigms
**Files:** `UI/HUD/Player/View/PlayerHUDView.cs`, `UI/PauseMenu.cs`

- UXML + programmatic UI + presenters + custom Tooltip system
- No unified pattern. `PlayerHUDView` directly pulls `ClientAssetLoader` for crystals
- Violates **Single Responsibility** — view knows about asset loading

#### 1.1.6 `MapStorage` Monolith
**File:** `Assets/Scripts/Game/Managers/MapStorage.cs` (413 lines)

- Knows about: `WorldLayer<T>`, file caching, `.mapb` format, `SingleMeshTerrainRenderer`
- Violates **Interface Segregation** — consumers forced to depend on methods they don't need

### 1.2 Less Critical Problems

#### 1.2.1 `DummyConnection` — Production/Test Code in One File
**File:** `Assets/Scripts/Networking/Connection/Client/DummyConnection.cs` (2000+ lines)

- Contains mock data generation, test world creation, packet simulation
- **Verdict:** Keep it — it's essential for offline mode and testing. Extract test helpers to separate file.

#### 1.2.2 `WorldTextureManager` — Rendering God Object
**File:** `Assets/Scripts/World/WorldTextureManager.cs`

- Handles: texture loading, atlas packing, atlas rebuilding, shader initialization, cache invalidation
- At least 5 distinct responsibilities

#### 1.2.3 `Robot` — Data + Behavior Mix
**File:** `Assets/Scripts/Game/Robot.cs`

- Animations, skins, clans, movement, network state, visual effects in one MonoBehaviour
- Cannot reuse `RobotVisual` without `RobotNetwork`

#### 1.2.4 `ServiceLocator` Residual
**File:** `Assets/Scripts/Core/ServiceLocator.cs`

- Thin bridge to VContainer now, but managers still reach each other via `.Instance`
- Creates hidden dependencies

---

## 2. What To Do About Singletons

### 2.1 The Problem with Current Singletons

```csharp
// Current pattern (BAD)
public class GameManager : SingletonMonoBehaviour<GameManager>
{
    public static GameManager Instance { get; private set; }
    
    protected void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
```

**Issues:**
1. **Order dependency** — if `Awake()` runs before another singleton's `Awake()`, `Instance` is null
2. **No interface** — consumers depend on concrete class, not abstraction
3. **Hidden dependencies** — `ClientAssetLoader.Awake()` subscribes to `ConnectionManager.Instance` silently
4. **Cannot mock** — impossible to inject `IConnectionManager` mock for tests
5. **Scene reload bugs** — `DontDestroyOnLoad` objects survive scene reloads, causing duplicates

### 2.2 Migration Strategy: From Singleton to DI

**Phase 1: Keep Singleton, Add Interface (1-2 days)**

```csharp
// Step 1: Extract interface
public interface IConnectionManager
{
    event Action<IPacket> OnPacketReceived;
    void Connect();
    void Disconnect();
    ConnectionStatus ConnectionStatus { get; }
}

// Step 2: Implement interface
public class ConnectionManager : SingletonMonoBehaviour<ConnectionManager>, IConnectionManager
{
    // Existing implementation stays, but consumers use IConnectionManager
}

// Step 3: Update consumers
// OLD:
ConnectionManager.Instance.OnPacketReceived += ...;

// NEW:
[Inject] private IConnectionManager _connectionManager;
```

**Phase 2: VContainer Registration (2-3 days)**

```csharp
// GameLifetimeScope.cs
builder.RegisterInstance(existingConnectionManager).As<IConnectionManager>();
// Or for new instances:
builder.RegisterComponentOnNewGameObject<ConnectionManager>().As<IConnectionManager>();
```

**Phase 3: Remove Singleton Pattern (1 week)**

- Replace `Instance` with `[Inject]` fields
- Remove `DontDestroyOnLoad` where not needed
- Use `LifetimeScope` hierarchy for scene-specific vs global singletons

### 2.3 When Singletons Are Acceptable

| Acceptable | Not Acceptable |
|------------|----------------|
| `AudioSystem` — true global service | `GameManager` — orchestrator with state |
| `ServiceLocator` — DI bridge | `MapStorage` — domain logic |
| `VFXPool` — resource pool | `RobotManager` — business logic |
| `ClientAssetLoader` — I/O service | `PlayerHUDView` — UI component |

**Rule of thumb:** If the singleton holds **state that changes during gameplay** (like `GameManager.CurrentState`), it's not a singleton — it's a stateful service that should be injected.

---

## 3. What To Do About `DummyConnection`

**Verdict: Keep it, but extract.**

### 3.1 Why It's Needed

- **Offline mode** — players can test without server
- **Prebaked maps** — deterministic world generation for testing
- **Packet simulation** — tests for UI, HUD, robots without network
- **CI/CD** — Unity Test Runner can use `DummyConnection` for play mode tests

### 3.2 How to Improve It

```csharp
// Current: 2000-line monolith
public class DummyConnection : IConnection
{
    // Mock data generation
    // Packet simulation
    // World generation
    // Bot management
    // ... everything in one class
}

// Proposed: Split by concern
public class DummyConnection : IConnection
{
    // Only connection lifecycle (Connect, Disconnect, Send)
}

public class DummyWorldGenerator
{
    public WorldInitPacket GenerateWorldInit(int seed);
    public MapRegionPacket GenerateMapRegion(int regionX, int regionY);
    public RobotInfoPacket[] GenerateRobots(int count);
}

public class DummyPlayerSimulator
{
    public PlayerInfoPacket GeneratePlayerInfo();
    public SkillProgressPacket[] GenerateSkillProgress();
}

public class TestPacketQueue
{
    // Queue of pre-configured packets for deterministic testing
}
```

**Benefits:**
- `DummyConnection` stays thin — just connection simulation
- World generation can be reused in `StandaloneWorldInitializer`
- Test scenarios can be configured as data, not code
- `DummyConnection` can be registered in VContainer as `IConnection` for offline mode

---

## 4. SOLID Refactoring Roadmap

### 4.1 Phase 1: PacketHandler Decomposition (Week 1)

**Goal:** Open/Closed Principle for packet processing

**Current:**
```csharp
// PacketHandler.cs — 40+ subscriptions hardcoded
protected void Awake()
{
    _networkService.Subscribe<WorldInitPacket>(WorldInit.Process);
    _networkService.Subscribe<MapRegionPacket>(MapRegion.Process);
    // ... 38 more
}
```

**Target:**
```csharp
// PacketHandler becomes pure router
public class PacketHandler : IInitializable
{
    private readonly IObjectResolver _resolver;
    private readonly ConcurrentDictionary<Type, object> _processors = new();
    
    public void Initialize()
    {
        // Auto-discover all IProcessor<T> implementations
        var processorTypes = TypeCache.GetTypesDerivedFrom(typeof(IPacketProcessor<>));
        foreach (var type in processorTypes)
        {
            var processor = (dynamic)_resolver.Resolve(type);
            _processors[typeof(type.GetGenericArguments()[0])] = processor;
        }
    }
    
    public void ProcessPacket(IPacket packet)
    {
        var packetType = packet.GetType();
        if (_processors.TryGetValue(packetType, out var processor))
        {
            ((dynamic)processor).Process((dynamic)packet);
        }
    }
}

// Each processor registers itself
public class WorldInitProcessor : IPacketProcessor<WorldInitPacket>
{
    public void Process(WorldInitPacket packet) { ... }
}

// VContainer auto-discovers via reflection
builder.RegisterType<WorldInitProcessor>().As<IPacketProcessor<WorldInitPacket>>();
```

**Benefits:**
- Add new packet type → create new `IPacketProcessor<T>` → no changes to `PacketHandler`
- Each processor is independently testable
- `PacketHandler` shrinks from 200 lines to 50

### 4.2 Phase 2: Managers → Domain Services (Week 2)

**Goal:** Single Responsibility for each manager

**Current `GameManager` responsibilities:**
1. Connection lifecycle
2. World initialization
3. UI state management
4. Inventory coordination
5. Robot lifecycle
6. Pack lifecycle
7. Audio event coordination
8. Mission management

**Target:**

```csharp
// GameManager becomes pure Facade
public class GameManager : MonoBehaviour
{
    [Inject] private IConnectionService _connection;
    [Inject] private IMapService _map;
    [Inject] private IPlayerService _player;
    [Inject] private IUIService _ui;
    
    public void OnWorldInitialized(WorldInitPacket packet)
    {
        _map.Initialize(packet);
        _player.SpawnLocalPlayer(packet);
        _ui.ShowHUD();
    }
}

// Each service is independently replaceable
public interface IConnectionService { ... }
public class ConnectionService : IConnectionService { ... }

public interface IMapService { ... }
public class MapService : IMapService { ... }
```

**Migration order:**
1. Extract `IConnectionService` from `ConnectionManager`
2. Extract `IMapService` from `MapManager` + `MapStorage`
3. Extract `IPlayerService` from `PlayerMovementController` + `PlayerInteractionController`
4. Extract `IUIService` from `MainMenu` + `PauseMenu` + `PlayerHUDView`
5. Reduce `GameManager` to orchestrator only

### 4.3 Phase 3: World/Rendering Isolation (Week 3)

**Goal:** Dependency Inversion — rendering doesn't know about networking

**Current coupling:**
```csharp
// MapStorage.cs — knows about terrain renderer
public void SetCell(int x, int y, CellType type)
{
    _cellLayer[x, y] = type;
    SingleMeshTerrainRenderer.OnCellChanged(x, y); // Direct dependency!
}
```

**Target:**

```csharp
// MapStorage raises events, doesn't know about renderer
public interface ICellStorage
{
    CellType GetCell(int x, int y);
    void SetCell(int x, int y, CellType type);
    event Action<int, int> OnCellChanged;
}

// Terrain renderer subscribes to events
public class SingleMeshTerrainRenderer : MonoBehaviour
{
    [Inject] private ICellStorage _cellStorage;
    
    private void OnEnable()
    {
        _cellStorage.OnCellChanged += MarkCellDirty;
    }
}
```

**Benefits:**
- Can swap `SingleMeshTerrainRenderer` for `TilemapRenderer` without changing `MapStorage`
- Can test `MapStorage` without graphics context
- `WorldTextureManager` depends on `ITextureProvider`, not `ClientAssetLoader` directly

### 4.4 Phase 4: Player Systems Composition (Week 4)

**Goal:** Component-based Robot and Player

**Current:**
```csharp
// Robot.cs — 500 lines of mixed concerns
public class Robot : MonoBehaviour
{
    // Network state
    // Animation
    // Skin rendering
    // Clan affiliation
    // Movement
    // Headlight
    // Audio
}
```

**Target:**

```csharp
// Robot is just a container
public class Robot : MonoBehaviour
{
    [Inject] private RobotNetworkComponent _network;
    [Inject] private RobotVisualComponent _visual;
    [Inject] private RobotSkinComponent _skin;
    [Inject] private RobotMotionComponent _motion;
    [Inject] private RobotHeadlightComponent _headlight;
}

// Each component is independently testable
public class RobotSkinComponent : MonoBehaviour
{
    [Inject] private IAssetLoader _assetLoader;
    
    public async UniTaskVoid LoadSkinAsync(string skinPath) { ... }
}

public class RobotMotionComponent : MonoBehaviour
{
    [Inject] private IPathfindingService _pathfinding;
    
    public void MoveTo(Vector2Int destination) { ... }
}
```

**Same for Player:**
```csharp
public class Player : MonoBehaviour
{
    [Inject] private PlayerMovementComponent _movement;
    [Inject] private PlayerInteractionComponent _interaction;
    [Inject] private PlayerStatsComponent _stats;
    [Inject] private PlayerInventoryComponent _inventory;
}
```

---

## 5. Risks and Mitigations

### 5.1 Unity Serialization Constraints

**Risk:** Changing MonoBehaviour fields breaks prefab/scene references

**Mitigation:**
- Use `[Inject]` for new dependencies, keep existing serialized fields
- Migrate gradually — don't refactor entire prefab at once
- Use `RegisterInstance(existingComponent)` in VContainer to preserve references

### 5.2 VContainer Vendored

**Risk:** Library updates overwrite changes

**Mitigation:**
- Document vendored version (`VContainer 1.19`) in `AGENTS.md`
- Don't modify VContainer source — use extension points
- Consider NuGet package reference instead of vendoring (Unity 6 supports it)

### 5.3 Performance

**Risk:** DI resolution in `Update()` is expensive

**Mitigation:**
- Cache resolved services in `[Inject]` fields (VContainer does this automatically)
- Use `IInitializable` for one-time setup, not `Update()`
- Profile with Unity Profiler after each phase

### 5.4 UniTask + VContainer Integration

**Risk:** Async initialization conflicts with VContainer lifecycle

**Mitigation:**
- Use `IAsyncStartable` for async init (VContainer supports it)
- Always pass `CancellationToken` from `LifetimeScope`
- Never `await` in `Awake()` — use `Start()` or `IAsyncStartable`

---

## 6. Quick Wins (Do This Week)

### 6.1 Extract `IPacketProcessor<T>` Auto-Discovery

**Effort:** 2 hours
**Impact:** `PacketHandler` becomes open for extension, closed for modification

```csharp
public interface IPacketProcessor<in T> where T : IPacket
{
    void Process(T packet);
}

// PacketHandler.cs
public void Initialize()
{
    var processors = TypeCache.GetTypesDerivedFrom(typeof(IPacketProcessor<>));
    foreach (var type in processors)
    {
        var processor = (dynamic)_resolver.Resolve(type);
        var packetType = type.GetGenericArguments()[0];
        _processors[packetType] = processor;
    }
}
```

### 6.2 Add `ICellStorage` Interface

**Effort:** 1 hour
**Impact:** Decouples `MapStorage` from `SingleMeshTerrainRenderer`

```csharp
public interface ICellStorage
{
    CellType GetCell(int x, int y);
    void SetCell(int x, int y, CellType type);
    bool IsReady { get; }
    event Action<int, int> OnCellChanged;
}

// MapStorage implements ICellStorage
// SingleMeshTerrainRenderer depends on ICellStorage, not MapStorage
```

### 6.3 Extract `IAssetProvider` from `ClientAssetLoader`

**Effort:** 1 hour
**Impact:** `PlayerHUDView` no longer depends on concrete asset loader

```csharp
public interface IAssetProvider
{
    UniTask<Texture2D> GetTextureAsync(string path, CancellationToken ct = default);
    event Action<string> OnTextureLoaded;
}

// ClientAssetLoader implements IAssetProvider
// PlayerHUDView depends on IAssetProvider
```

---

## 7. Metrics: Before and After

| Metric | Before | After (Target) |
|--------|--------|----------------|
| `PacketHandler` lines | 200 | 50 (router) + N × 30 (processors) |
| `GameManager` responsibilities | 8 | 1 (orchestrator only) |
| Direct `.Instance` calls | 150+ | <20 (only in DI setup) |
| `MapStorage` coupling | 3 dependencies | 1 (`ICellStorage`) |
| `Robot` responsibilities | 8 | 6 components |
| Testable classes | 5 | 50+ (all interfaces mockable) |
| New packet type LOC | 10 (in PacketHandler) + 30 (processor) | 30 (processor only) |

---

## 8. Conclusion

**Feasibility: 7/10**

The architecture is salvageable. VContainer is already installed, domains are separable, and Unity constraints are manageable. The main risks are:
1. **Scene/prefab breakage** during refactoring
2. **VContainer vendoring** (document version, don't modify)
3. **Performance** of DI in hot paths (profile early)

**Recommended approach:**
1. **Week 1-2:** Quick wins (IPacketProcessor, ICellStorage, IAssetProvider)
2. **Week 3-4:** PacketHandler decomposition
3. **Week 5-6:** Manager → Service extraction
4. **Week 7-8:** World/Rendering isolation
5. **Week 9-10:** Player/Robot composition

**Do NOT:**
- Full ECS migration (Unity serialization won't allow it)
- Pure C# architecture (breaks prefabs)
- Remove `DummyConnection` (it's essential)

**Next steps:**
1. Review this document
2. Prioritize phases
3. Start with Phase 1 Quick Wins (low effort, high impact)
4. Set up integration tests for `DummyConnection` before refactoring
