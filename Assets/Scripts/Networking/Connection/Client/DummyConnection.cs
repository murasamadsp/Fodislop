#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.CompilerServices;
using Fodinae;
using Fodinae.Audio;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.UI;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Chat;
using MinesServer.Networking.Client.Packets.Connection;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Client.Packets.Utilities;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Chat;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.Information.StatusPanel;
using MinesServer.Networking.Server.Packets.Inventory;
using MinesServer.Networking.Server.Packets.Mission;
using MinesServer.Networking.Server.Packets.Movement;
using MinesServer.Networking.Server.Packets.Utilities;
using MinesServer.Networking.Server.Packets.World;
using MinesServer.Networking.Shared;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client
{
    public class DummyConnection : IServerConnection, IOfflineConnection
    {
        private readonly ISessionContainer _session;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private int _lifecycleVersion;

        public DummyConnection(ISessionContainer session)
        {
            _session = session;
            _validTokens = _tokenStore.Load();
            _missionRunner = new DummyMissionRunner(SendPacket);
            _buffManager = new DummyBuffManager(SendPacket, _lifecycleVersion);
            _teleportManager = new DummyTeleportManager(SendPacket, _teleportPositions);
            _chatSimulator = new DummyChatSimulator(SendPacket, _lifecycleVersion);
            _clanManager = new DummyClanManager(SendPacket);
            _pathFinder = new DummyPathFinder(SendPacket, _session);
        }

        public ConnectionStatus ConnectionStatus => _status;

        public event Action<ServerPacket>? OnReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnDisconnecting;
        public event Action? OnConnecting;

        public static bool IgnoreCollision = false;

        private readonly DummyTokenStore _tokenStore = new();
        private readonly HashSet<string> _validTokens;
        private bool _awaitingAuth;
        private readonly DummyMissionRunner _missionRunner;
        private readonly DummyBuffManager _buffManager;
        private readonly DummyTeleportManager _teleportManager;
        private readonly DummyChatSimulator _chatSimulator;
        private readonly DummyClanManager _clanManager;
        private readonly DummyPathFinder _pathFinder;

        private const ushort _mockBotId = 456;
        private ushort _x = 0;
        private ushort _y = 0;
        private Direction _rot = Direction.Up;
        private bool _aggression;
        private bool _autoDig;
        private System.Drawing.Color _chatColor = System.Drawing.Color.FromArgb(255, 200, 180, 100);
        private ItemType? _selectedItemType;
        private readonly Dictionary<ItemType, long> _inventory = new();
        private readonly List<(ushort X, ushort Y)> _teleportPositions = new();
        private CancellationTokenSource? _pathCts;
        private static readonly ChatMessagePacket[] _seedMessages = CreateSeedMessages();

        private static ChatMessagePacket[] CreateSeedMessages()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var gray = System.Drawing.Color.FromArgb(255, 120, 120, 120);
            var green = System.Drawing.Color.FromArgb(255, 80, 220, 80);
            var blue = System.Drawing.Color.FromArgb(255, 80, 140, 255);
            var red = System.Drawing.Color.FromArgb(255, 255, 80, 80);
            var orange = System.Drawing.Color.FromArgb(255, 255, 180, 60);
            var cyan = System.Drawing.Color.FromArgb(255, 60, 255, 255);
            var magenta = System.Drawing.Color.FromArgb(255, 220, 60, 220);
            var yellow = System.Drawing.Color.FromArgb(255, 255, 220, 60);
            var white = System.Drawing.Color.White;

            return new[]
            {
                new ChatMessagePacket(1, now - 300000, 0, 0, gray, "System", gray, "Добро пожаловать на Fodinae!"),
                new ChatMessagePacket(2, now - 270000, 1, 1, green, "Miner77", white, "привет всем!"),
                new ChatMessagePacket(3, now - 240000, 2, 0, blue, "DeepDrill", white, "кто на сервере?"),
                new ChatMessagePacket(4, now - 210000, 3, 2, red, "CrystalMage", white, "иду копать алмазы"),
                new ChatMessagePacket(5, now - 180000, 4, 0, orange, "RockBreaker", white, "нужна помощь с мобом"),
                new ChatMessagePacket(6, now - 150000, 5, 1, cyan, "OreTrader", white, "продам редкий блок"),
                new ChatMessagePacket(7, now - 120000, 6, 0, magenta, "NightMiner", white, "всем удачной шахты!"),
                new ChatMessagePacket(8, now - 90000, 1, 1, green, "Miner77", white, "кто-нибудь на базе?"),
                new ChatMessagePacket(9, now - 60000, 7, 0, yellow, "Newbie42", white, "я только зашел"),
                new ChatMessagePacket(10, now - 30000, 3, 2, red, "CrystalMage", white, "сервер лагает?"),
            };
        }

        // Depth warning/damage feature disabled in DummyConnection
        // private const int _maxDepth = 200;
        // private bool _depthWarningActive;

        private float _digCooldown = 0.3f;
        private int _maxGlobalChatLength = 50;
        private int _maxLocalChatLength = 20;

        private static readonly System.Random _rng = new();

        private WorldLayer<CellType>? _worldLayer;
        private readonly HashSet<int> _sentMapChunks = new();
        private CellConfigurationPacket[]? _cellConfigs;
        private long[] _basketContents = new long[6];
        private readonly Stack<CellType> _geoStack = new();

        public bool UsePrebakedMap = true;
        public string PrebakedWorldCodeName = "pallada";

        private int _health = 500;

        public void Connect()
        {
            if (_status != ConnectionStatus.Disconnected)
            {
                return;
            }

            _status = ConnectionStatus.Connecting;
            OnConnecting?.Invoke();

            // Run asynchronously, but stay on the Unity Main Thread
            ConnectAsync(++_lifecycleVersion).Forget();
        }

        private async UniTaskVoid ConnectAsync(int lifecycleVersion)
        {
            await UniTask.Yield();

            if (_status != ConnectionStatus.Connecting || lifecycleVersion != _lifecycleVersion)
            {
                return;
            }

            _status = ConnectionStatus.Connected;
            OnConnected?.Invoke();
        }

        public void Disconnect()
        {
            if (_status == ConnectionStatus.Disconnected)
            {
                _worldLayer?.Dispose();
                _worldLayer = null;
                return;
            }

            _lifecycleVersion++;
            _worldLayer?.Dispose();
            _worldLayer = null;

            // Cleared so the buff loop can start again on the next connection.
            // It was never reset, so after one disconnect StartBuffLoop's guard
            // stayed latched and the loop never came back - the mirror image of
            // the other four loops, which had no guard at all and duplicated.
            _buffManager.ResetLoopGuard();

            _status = ConnectionStatus.Disconnecting;
            OnDisconnecting?.Invoke();
            DisconnectAsync(_lifecycleVersion).Forget();
        }

        private async UniTaskVoid DisconnectAsync(int lifecycleVersion)
        {
            await UniTask.Delay(100);

            if (lifecycleVersion != _lifecycleVersion || _status != ConnectionStatus.Disconnecting)
            {
                return;
            }

            _status = ConnectionStatus.Disconnected;
            OnDisconnected?.Invoke();
        }

        private async UniTaskVoid UpdatePosition()
        {
            await UniTask.Delay(IgnoreCollision ? 20 : 200);
            DummyMapStreamer.SendMapChunksAround(_worldLayer, _sentMapChunks, _x, _y, SendPacket);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new RobotPositionPacket(_mockBotId, _x, _y, (byte)_rot) })));
        }

        /// <summary>
        /// Whether a background mock loop started at
        /// <paramref name="lifecycleVersion"/> should still be running.
        /// </summary>
        /// <remarks>
        /// The loops used to test <c>_status == Connected</c> and nothing else,
        /// which made them immortal. Dispose did not touch _status, so every
        /// loop on a disposed instance kept running forever - and since a new
        /// DummyConnection is built for each connection, a menu-game-menu-game
        /// cycle left a full set of them behind each time. RunCircularBots
        /// alone allocates a List, an array and six position packets every
        /// 100ms, so each leaked set is a permanent fixed-rate garbage source
        /// that nothing can ever stop.
        ///
        /// Comparing the captured lifecycle version as well ties every loop to
        /// the connection that started it: one bump retires all of them at
        /// once, whether the trigger was a disconnect, a reconnect or a
        /// dispose.
        /// </remarks>
        private bool LoopAlive(int lifecycleVersion)
        {
            return _status == ConnectionStatus.Connected &&
                lifecycleVersion == _lifecycleVersion;
        }

        public void Dispose()
        {
            // Retires every background loop belonging to this instance. Without
            // this the loops outlive the object that owns them.
            _lifecycleVersion++;
            _status = ConnectionStatus.Disconnected;
            _buffManager.ResetLoopGuard();

            _worldLayer?.Dispose();
            _worldLayer = null;
        }

        public void TriggerDisconnect(string reason)
        {
            OnReceived?.Invoke(new ServerPacket(new MinesServer.Networking.Server.Packets.Connection.DisconnectPacket(reason)));
        }

        public void TriggerReconnect(string reason)
        {
            OnReceived?.Invoke(new ServerPacket(new MinesServer.Networking.Server.Packets.Connection.ReconnectPacket(reason)));
        }

        private void SendPacket(ServerPacket packet)
        {
            OnReceived?.Invoke(packet);
        }

        public void SendAsync(ClientPacket packet)
        {
            if (packet.Data is ActionClientPacket actionPacket)
            {
                if (actionPacket.Payload is MovePacket move)
                {
                    if (_teleportManager.WindowOpen)
                    {
                        return;
                    }

                    int dx = Math.Abs(move.X - _x);
                    int dy = Math.Abs(move.Y - _y);
                    bool isAdjacent = (dx == 1 && dy == 0) || (dx == 0 && dy == 1);

                    if (!isAdjacent)
                    {
                        OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                        {
                            new RobotPositionPacket(_mockBotId, _x, _y, (byte)_rot),
                        })));
                        return;
                    }

                    if (_worldLayer != null)
                    {
                        CellType cellType = GetServerCell(move.X, move.Y);
                        var cellConfig = _session.TryResolve<MapManager>()?.GetCellConfig(cellType);
                        if (cellConfig.HasValue)
                        {
                            bool isPassable = cellType == CellType.Empty || ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Passable);
                            if (!isPassable && !IgnoreCollision)
                            {
                                OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                                {
                                    new RobotPositionPacket(_mockBotId, _x, _y, (byte)_rot),
                                })));
                                return;
                            }
                        }
                    }

                    _x = move.X;
                    _y = move.Y;
                    _pathCts?.Cancel();
                    UpdatePosition().Forget();
                    _teleportManager.CheckTeleportEntry(_x, _y);
                }
                else if (actionPacket.Payload is RotatePacket rotate)
                {
                    _rot = rotate.Direction;
                    UpdatePosition().Forget();
                }
                else if (actionPacket.Payload is UnmappedKeyPacket)
                {
                    // intentionally left blank — unmapped keys are ignored
                }
                else if (actionPacket.Payload is ToggleAutoDigPacket)
                {
                    _autoDig = !_autoDig;
                    OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(_autoDig)));
                }
                else if (actionPacket.Payload is ToggleAgressionPacket)
                {
                    _aggression = !_aggression;
                    OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(_aggression)));
                }
                else if (actionPacket.Payload is BzPacket)
                {
                    ushort cellX = actionPacket.X;
                    ushort cellY = actionPacket.Y;

                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                    {
                        new AudioPacket(SFX.Bz, _mockBotId, cellX, cellY, Array.Empty<StringPairPacket>()),
                    })));

                    if (_worldLayer != null)
                    {
                        CellType cellType = GetServerCell(cellX, cellY);
                        if (cellType == CellType.Empty)
                        {
                            return;
                        }
                    }

                    if (_worldLayer != null)
                    {
                        CellType cellType = GetServerCell(cellX, cellY);
                        int crystalIdx = DummyCellConfigurationUtilities.GetCrystalBasketIndex(cellType);
                        var mm = _session.TryResolve<MapManager>();
                        var cellConfig = mm?.GetCellConfig(cellType);
                        bool isBreakable = cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Breakable);

                        if (!isBreakable && cellType != CellType.Empty)
                        {
                            return;
                        }

                        SetServerCell(cellX, cellY, CellType.Empty);

                        if (crystalIdx >= 0)
                        {
                            var stats = _session.TryResolve<IPlayerStats>();
                            if (stats != null && stats.BasketContents != null && stats.BasketContents.Length > crystalIdx)
                            {
                                var newContents = new long[stats.BasketContents.Length];
                                Array.Copy(stats.BasketContents, newContents, newContents.Length);
                                newContents[crystalIdx] += UnityEngine.Random.Range(1, 101);
                                OnReceived?.Invoke(new ServerPacket(new BasketPacket(stats.BasketCapacity, newContents)));
                            }
                        }

                        OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                        {
                            new MapRegionPacket(cellX, cellY, 0, 0, new[] { CellType.Empty }),
                            new AudioPacket(SFX.Destroy, _mockBotId, cellX, cellY, Array.Empty<StringPairPacket>()),
                        })));
                    }

                    _missionRunner.OnBlockMined(_inventory);
                }
                else if (actionPacket.Payload is SuicidePacket)
                {
                    const ushort SPAWN_X = 25;
                    const ushort SPAWN_Y = 50;
                    var effectX = _x;
                    var effectY = _y;
                    _x = SPAWN_X;
                    _y = SPAWN_Y;
                    _rot = Direction.Up;
                    _health = 500;
                    _pathCts?.Cancel();

                    DummyMapStreamer.SendMapChunksAround(_worldLayer, _sentMapChunks, _x, _y, SendPacket);
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(500, 500)));
                    OnReceived?.Invoke(new ServerPacket(new TeleportPacket(SPAWN_X, SPAWN_Y, false)));
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                    {
                        new RobotPositionPacket(_mockBotId, SPAWN_X, SPAWN_Y, (byte)_rot),
                        new AudioPacket(SFX.Death, _mockBotId, effectX, effectY, Array.Empty<StringPairPacket>()),
                    })));
                }
                else if (actionPacket.Payload is GeoPacket)
                {
                    Vector2Int frontOffset = _rot switch
                    {
                        Direction.Down => new Vector2Int(0, 1),
                        Direction.Up => new Vector2Int(0, -1),
                        Direction.Left => new Vector2Int(-1, 0),
                        Direction.Right => new Vector2Int(1, 0),
                        _ => Vector2Int.zero,
                    };
                    ushort fx = (ushort)(_x + frontOffset.x);
                    ushort fy = (ushort)(_y + frontOffset.y);

                    if (_worldLayer != null)
                    {
                        CellType cellType = GetServerCell(fx, fy);
                        var mm = _session.TryResolve<MapManager>();
                        var cellConfig = mm?.GetCellConfig(cellType);
                        bool isBreakable = cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Breakable);

                        if (cellType != CellType.Empty && isBreakable)
                        {
                            _geoStack.Push(cellType);
                            SetServerCell(fx, fy, CellType.Empty);
                            OnReceived?.Invoke(new ServerPacket(new GeologyPacket(_geoStack.Count, 10, cellType, cellType.ToString())));
                            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                            {
                                new MapRegionPacket(fx, fy, 0, 0, new[] { CellType.Empty }),
                                new AudioPacket(SFX.Geology, _mockBotId, fx, fy, Array.Empty<StringPairPacket>()),
                            })));
                        }
                        else if (_geoStack.Count > 0)
                        {
                            var placeType = _geoStack.Pop();
                            SetServerCell(fx, fy, placeType);
                            OnReceived?.Invoke(new ServerPacket(new GeologyPacket(_geoStack.Count, 10, placeType, placeType.ToString())));
                            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                            {
                                new MapRegionPacket(fx, fy, 0, 0, new[] { placeType }),
                                new AudioPacket(SFX.Geology, _mockBotId, fx, fy, Array.Empty<StringPairPacket>()),
                            })));
                        }
                    }
                }
                else if (actionPacket.Payload is HealPacket)
                {
                    _health = Math.Min(500, _health + 50);
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(_health, 500)));
                }
                else if (actionPacket.Payload is BuildCyanPacket)
                {
                    var front = GetFrontCell();
                    DummyBuildHandler.TryBuild(_worldLayer, (x, y) => GetServerCell(x, y), (x, y, t) => SetServerCell(x, y, t), SendPacket, front.X, front.Y, CellType.MilitaryBlock);
                }
                else if (actionPacket.Payload is BuildGrayPacket)
                {
                    var front = GetFrontCell();
                    if (_worldLayer != null &&
                        GetServerCell(front.X, front.Y) == CellType.Road)
                    {
                        SetServerCell(front.X, front.Y, CellType.Empty);
                        OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new MapRegionPacket(front.X, front.Y, 0, 0, new[] { CellType.Empty }) })));
                    }
                    else
                    {
                        DummyBuildHandler.TryBuild(_worldLayer, (x, y) => GetServerCell(x, y), (x, y, t) => SetServerCell(x, y, t), SendPacket, front.X, front.Y, CellType.Road);
                    }
                }
                else if (actionPacket.Payload is BuildGreenPacket)
                {
                    var front = GetFrontCell();
                    DummyBuildHandler.TryUpgradeBuild(_worldLayer, (x, y) => GetServerCell(x, y), (x, y, t) => SetServerCell(x, y, t), SendPacket, front.X, front.Y,
                        new (CellType From, CellType To)[] { (CellType.Empty, CellType.GreenBlock), (CellType.GreenBlock, CellType.YellowBlock), (CellType.YellowBlock, CellType.RedBlock) });
                }
                else if (actionPacket.Payload is BuildWhitePacket)
                {
                    var front = GetFrontCell();
                    DummyBuildHandler.TryUpgradeBuild(_worldLayer, (x, y) => GetServerCell(x, y), (x, y, t) => SetServerCell(x, y, t), SendPacket, front.X, front.Y,
                        new (CellType From, CellType To)[] { (CellType.Empty, CellType.Support), (CellType.Support, CellType.QuadBlock) });
                }
                else if (actionPacket.Payload is ClickCellPacket click)
                {
                    _pathCts?.Cancel();
                    _pathCts?.Dispose();
                    _pathCts = null;
                    var path = _pathFinder.FindPath(_x, _y, click.X, click.Y, GetServerCell);
                    if (path.Count > 0)
                    {
                        _pathCts = new CancellationTokenSource();
                        WalkPathAsync(path, _pathCts.Token).Forget();
                    }
                }

                return;
            }

            switch (packet.Data)
            {
                case ClientHelloPacket clientHello:
                    string receivedToken = clientHello.AuthToken;
                    bool isTokenValid = !string.IsNullOrEmpty(receivedToken) && _validTokens.Contains(receivedToken);

                    if (!isTokenValid)
                    {
                        _awaitingAuth = true;
                        OnReceived?.Invoke(DummyWindowBuilder.BuildAuthWindow());
                        return;
                    }

                    _awaitingAuth = false;

                    if (clientHello.ClientVersion < 1)
                    {
                        OnReceived?.Invoke(new ServerPacket(new OutdatedClientPacket(
                            2, "Mines 3", "Ваша версия устарела. Скачайте новую!",
                            "https://minesgame.ru/download", string.Empty)));
                        return;
                    }

                    OnReceived?.Invoke(new ServerPacket(new AuthTokenPacket(receivedToken)));

                    InitWorld();
                    break;
                case RuntimeAssetRequestPacket runtimeAssets:
                    DummyAssetHandler.HandleAssetRequest(runtimeAssets, _session, SendPacket).Forget();
                    break;
                case OpenHelpClickPacket:
                    break;
                case OpenSettingsClickPacket:
                    break;
                case ChangeChatColorPacket colorChange:
                    _chatColor = colorChange.Color;
                    break;
                case OpenClanClickPacket:
                    _clanManager.HandleOpenClanClick();
                    break;
                case QueryChatHistoryPacket qh:
                    long startFrom = (long)qh.StartFrom;
                    var filtered = _seedMessages.Where(m => startFrom == 0 || m.Timestamp >= startFrom).ToArray();
                    OnReceived?.Invoke(new ServerPacket(new ChatMessageListPacket(qh.Tag, filtered)));
                    break;
                case SendLocalChatMessagePacket localMsg:
                    OnReceived?.Invoke(new ServerPacket(new LocalChatMessagePacket(_mockBotId, _x, _y, localMsg.Message)));
                    break;

                case SendChatMessagePacket globalMsg:
                    var chatMsg = new ChatMessagePacket(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        999, 1,
                        _chatColor,
                        "You",
                        _chatColor,
                        globalMsg.Message);
                    OnReceived?.Invoke(new ServerPacket(new ChatMessageListPacket("global", new[] { chatMsg })));
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.SelectItemPacket selectItem:
                    _selectedItemType = selectItem.Item;
                    OnReceived?.Invoke(new ServerPacket(GetItemInfoPacket(selectItem.Item)));
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.DeselectItemPacket:
                    _selectedItemType = null;
                    OnReceived?.Invoke(new ServerPacket(default(MinesServer.Networking.Server.Packets.Inventory.DeselectItemPacket)));
                    break;
                case MinesServer.Networking.Client.Packets.Inventory.UseItemPacket:
                    HandleUseItem();
                    break;
                case ElementClickPacket elementClick:
                    HandleElementClick(elementClick);
                    break;
                default:
                    break;
            }
        }

        private static MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket GetItemInfoPacket(ItemType item)
        {
            var (name, desc) = DummyItemInfo.GetItemInfo(item);
            return new MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket(
                item, name, desc, 1, 1, 3, false, new BitArray(0));
        }

        private void HandleUseItem()
        {
            if (_selectedItemType == null)
            {
                return;
            }

            var selectedType = _selectedItemType.Value;
            if (DummyItemInfo.IsBuildingPack(selectedType))
            {
                var packType = DummyItemInfo.ItemTypeToPackType(selectedType);
                if (packType == PackType.None)
                {
                    return;
                }

                ushort frontX = _x;
                ushort frontY = _y;
                switch (_rot)
                {
                    case Direction.Up: frontY--; break;
                    case Direction.Down: frontY++; break;
                    case Direction.Left: frontX--; break;
                    case Direction.Right: frontX++; break;
                }

                OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                {
                    new PackPacket(frontX, frontY, packType, 0, 0),
                })));
                if (packType == PackType.Teleport)
                {
                    _teleportPositions.Add((frontX, frontY));
                }

                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else if (selectedType == ItemType.Rem)
            {
                _health = 500;
                OnReceived?.Invoke(new ServerPacket(new HealthPacket(500, 500)));
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else if (selectedType == ItemType.UpgradeBooster)
            {
                _buffManager.ActivateBuff("xp3", 86400, System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3");
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else if (selectedType == ItemType.FreeUp)
            {
                _buffManager.ActivateBuff("freeup", 43200, System.Drawing.Color.Cyan, "Freeup");
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else if (selectedType == ItemType.MineBooster)
            {
                _buffManager.ActivateBuff("x4", 43200, System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4");
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else if (selectedType == ItemType.Battery)
            {
                _buffManager.ActivateBuff("battery", 3600, System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор");
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
            else
            {
                DummyItemInfo.ConsumeItem(_inventory, selectedType, 1);
            }
        }

        private void HandleElementClick(ElementClickPacket packet)
        {
            if (packet.WindowTag == "daily_bonus")
            {
                _buffManager.HandleDailyBonusClaim(_inventory);
            }
            else if (packet.WindowTag == "teleport")
            {
                if (!_teleportManager.WindowOpen)
                {
                    return;
                }

                if (packet.ElementIndex == 0)
                {
                    _teleportManager.WindowOpen = false;
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else
                {
                    _teleportManager.HandleTeleportClick(packet.ElementIndex - 1);
                }
            }
            else if (packet.WindowTag == "test_modal")
            {
                OnReceived?.Invoke(DummyWindowBuilder.BuildTestModalWindow());
            }
            else if (packet.WindowTag is "join_clan" or "leave_clan" or "clan_list" or "clan_info")
            {
                _clanManager.HandleElementClick(packet);
            }
            else if (packet.WindowTag == "open_missions")
            {
                _missionRunner.SendMissionWindow(_x, _y);
            }
            else if (packet.WindowTag == "missions")
            {
                if (packet.ElementIndex == 0)
                {
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else if (packet.ElementIndex <= _missionRunner.MissionCount)
                {
                    _missionRunner.StartMission(packet.ElementIndex - 1, _x, _y);
                }
                else
                {
                    _missionRunner.CancelMission();
                }
            }
            else if (packet.WindowTag == "open_url_test")
            {
                OnReceived?.Invoke(DummyWindowBuilder.BuildOpenUrlPacket("https://vk.ru/mines4reborn"));
            }
            else if (packet.WindowTag == "test_mission_arrow")
            {
                OnReceived?.Invoke(DummyWindowBuilder.BuildTestMissionArrowPacket(_x, _y));
            }
            else if (packet.WindowTag == "auth")
            {
                if (!_awaitingAuth)
                {
                    return;
                }

                _awaitingAuth = false;
                OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));

                string newToken = Guid.NewGuid().ToString("N");
                _validTokens.Add(newToken);
                _tokenStore.Save(_validTokens);
                OnReceived?.Invoke(new ServerPacket(new AuthTokenPacket(newToken)));

                InitWorld();
            }
        }

        private void InitWorld()
        {
            _cellConfigs = DummyCellConfigurationUtilities.CreateCellConfigurations();
            _worldLayer?.Dispose();
            _worldLayer = null;

            string mapbPath = DummyWorldMapArchive.ResolveMapFile(PrebakedWorldCodeName);

            (int worldWidth, int worldHeight) = DummyWorldMapArchive.ReadDimensions(mapbPath);
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidDataException(
                    $"Prebaked map file '{mapbPath}' has invalid dimensions ({worldWidth}x{worldHeight}).");
            }

            int widthChunks = (worldWidth + 31) / 32;
            int heightChunks = (worldHeight + 31) / 32;
            _worldLayer = new WorldLayer<CellType>(
                mapbPath,
                widthChunks,
                heightChunks,
                32,
                36);
            _sentMapChunks.Clear();

            OnReceived?.Invoke(new ServerPacket(new WorldInitPacket(
                PrebakedWorldCodeName,
                "Pallada",
                (ushort)worldWidth,
                (ushort)worldHeight,
                _cellConfigs,
                new byte[][]
                {
                    new byte[] { 37, 38, 106 },
                })));

            OnReceived?.Invoke(new ServerPacket(new PlayerInfoPacket(999, _mockBotId, "Darkar25")));
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(
                _mockBotId,
                999,
                1,
                "Skin/bee.png",
                "Tail/default.png",
                string.Empty)));
            var robotPos = new RobotPositionPacket(_mockBotId, 25, 50, 0);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { robotPos })));
            DummyBotRunner.RunCircularBots(6, _lifecycleVersion, SendPacket, () => LoopAlive(_lifecycleVersion)).Forget();
            _x = 25;
            _y = 50;
            DummyMapStreamer.SendMapChunksAround(_worldLayer, _sentMapChunks, _x, _y, SendPacket);
            OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(false)));
            OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(false)));
            OnReceived?.Invoke(new ServerPacket(new DailyBonusStatePacket(false)));
            _buffManager.ResetDailyBonus();
            OnReceived?.Invoke(new ServerPacket(new CurrencyPacket(123456, 1234)));
            _health = 250;
            OnReceived?.Invoke(new ServerPacket(new HealthPacket(250, 500)));
            _basketContents = new long[6];
            OnReceived?.Invoke(new ServerPacket(new BasketPacket(50000, _basketContents)));
            OnReceived?.Invoke(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
            OnReceived?.Invoke(new ServerPacket(new LevelPacket(12345)));

            SendSkillProgressMock();
            _chatSimulator.SendChatMock(_lifecycleVersion);

            OnReceived?.Invoke(new ServerPacket(new OnlinePacket(42, 3)));
            OnReceived?.Invoke(new ServerPacket(default(ClearStatusPacket)));
            _buffManager.SendStatusPackets();

            _buffManager.StartBuffLoop(_lifecycleVersion);
            SendPingMock(_lifecycleVersion).Forget();
            _buffManager.SendDailyBonusMock(_lifecycleVersion).Forget();

            OnReceived?.Invoke(new ServerPacket(
                new MovementSpeedPacket(
                    DummyCellConfigurationUtilities.CreateMovementSpeeds(_cellConfigs!))));

            // Depth warning/damage feature disabled in DummyConnection
            // OnReceived?.Invoke(new ServerPacket(new MaxDepthPacket(200)));

            var inventoryData = new Dictionary<ItemType, long>();
            foreach (var type in ItemRegistry.AllTypes)
            {
                inventoryData[type] = 1;
            }

            inventoryData[ItemType.Battery] = 2;
            _inventory.Clear();
            foreach (var kvp in inventoryData)
            {
                _inventory[kvp.Key] = kvp.Value;
            }

            OnReceived?.Invoke(new ServerPacket(new InventoryPacket(inventoryData)));

            var placeholderMsg = new ChatMessagePacket(0, 0, 0, 0,
            System.Drawing.Color.White, string.Empty, System.Drawing.Color.White, string.Empty);
            OnReceived?.Invoke(new ServerPacket(new ChatListPacket(new[] { ("global", "Global", placeholderMsg) })));

            // Send test packs
            _teleportPositions.Clear();
            _teleportPositions.Add((27, 50));
            _teleportPositions.Add((227, 50));
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
            {
                new PackPacket(27, 50, PackType.Teleport, 0, 1),
                new PackPacket(227, 50, PackType.Teleport, 0, 1),
                new PackPacket(25, 48, PackType.Market, 0, 0),
            })));

            var serverConfig = _session.TryResolve<ServerConfig>();
            serverConfig?.ApplyValues(_digCooldown, _maxGlobalChatLength, _maxLocalChatLength);
        }







        private CellType GetServerCell(ushort serverX, ushort serverY)
        {
            return _worldLayer?.GetCellSync(serverX, serverY) ?? CellType.Unloaded;
        }

        private void SetServerCell(ushort serverX, ushort serverY, CellType type)
        {
            if (_worldLayer != null)
            {
                _worldLayer[serverX, serverY] = type;
            }
        }

        private void SendSkillProgressMock()
        {
            var skills = new (SkillType type, long current, long max)[]
            {
                (SkillType.MineGeneral, 75, 100),
                (SkillType.Extraction, 120, 100),
                (SkillType.Health, 40, 100),
                (SkillType.Movement, 10, 100),
            };

            foreach (var s in skills)
            {
                OnReceived?.Invoke(new ServerPacket(new SkillProgressPacket(s.type, s.current, s.max)));
            }
        }

        private async UniTaskVoid SendPingMock(int lifecycleVersion)
        {
            await UniTask.Delay(2000);
            while (LoopAlive(lifecycleVersion))
            {
                OnReceived?.Invoke(new ServerPacket(new PingPacket(DateTimeOffset.UtcNow.Ticks, _rng.Next(15, 60))));
                await UniTask.Delay(5000);
            }
        }


        private (ushort X, ushort Y) GetFrontCell()
        {
            Vector2Int offset = _rot switch
            {
                Direction.Down => new Vector2Int(0, 1),
                Direction.Up => new Vector2Int(0, -1),
                Direction.Left => new Vector2Int(-1, 0),
                Direction.Right => new Vector2Int(1, 0),
                _ => Vector2Int.zero,
            };
            return ((ushort)(_x + offset.x), (ushort)(_y + offset.y));
        }

        private async UniTaskVoid WalkPathAsync(List<(ushort X, ushort Y)> path, CancellationToken ct)
        {
            try
            {
                ushort prevX = _x;
                ushort prevY = _y;

                for (int i = 0; i < path.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var (nextX, nextY) = path[i];
                    Direction dir = nextY > prevY ? Direction.Down
                        : nextY < prevY ? Direction.Up
                        : nextX < prevX ? Direction.Left
                        : Direction.Right;

                    (_x, _y) = (nextX, nextY);
                    prevX = nextX;
                    prevY = nextY;

                    DummyMapStreamer.SendMapChunksAround(_worldLayer, _sentMapChunks, _x, _y, SendPacket);
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                    {
                        new RobotPositionPacket(_mockBotId, _x, _y, (byte)dir),
                    })));

                    await UniTask.Delay(100, cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                // path walk cancelled — expected when a new click or move cancels the walk
            }
        }
    }
}
