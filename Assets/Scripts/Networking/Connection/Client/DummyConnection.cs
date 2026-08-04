#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.CompilerServices;
using Fodinae;
using Fodinae.Audio;
using Fodinae.Core;
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
using Newtonsoft.Json;
using UnityEngine;

namespace MinesServer.Networking.Connection.Client
{
    public class DummyConnection : IServerConnection
    {
        private ConnectionStatus _status = ConnectionStatus.Disconnected;

        public ConnectionStatus ConnectionStatus => _status;

        public event Action<ServerPacket>? OnReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action? OnDisconnecting;
        public event Action? OnConnecting;

        public static bool IgnoreCollision = false;

        private static readonly string TokenStorePath =
            Path.Combine(Application.temporaryCachePath, "server_tokens.json");

        private static readonly HashSet<string> _validTokens = LoadTokensFromFile();
        private bool _awaitingAuth;

        private const ushort _mockBotId = 456;
        private ushort _x = 0;
        private ushort _y = 0;
        private Direction _rot = Direction.Up;
        private bool _aggression;
        private bool _autoDig;
        private System.Drawing.Color _chatColor = System.Drawing.Color.FromArgb(255, 200, 180, 100);
        private ItemType? _selectedItemType;
        private readonly Dictionary<ItemType, long> _inventory = new();
        private int _bonusCountdown;
        private volatile bool _bonusClaimed;
        private ItemType _pendingBonusItem;
        private int _pendingBonusAmount;
        private readonly List<(ushort X, ushort Y)> _teleportPositions = new();
        private List<(ushort X, ushort Y)> _teleportDestinations = new();
        private bool _teleportWindowOpen;
        private readonly Dictionary<string, long> _activeBuffs = new();
        private bool _buffLoopStarted;
        private CancellationTokenSource? _pathCts;
        private ushort _clanId;
        private static readonly (ushort Id, string Name, string Desc)[] _mockClans =
        {
            (1, "Альфа", "Старейший клан на сервере"),
        };
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

        private const int _maxDepth = 200;
        private bool _depthWarningActive;

        private byte _clientMasterVolume = 255;
        private readonly Dictionary<string, byte> _clientSoundVolumes = new();
        private RendererMode _clientRenderer = RendererMode.Default;
        private readonly List<StringPairPacket> _clientKeybinds = new();
        private readonly List<string> _clientUnrenderedTextures = new();
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

        private struct MissionDef
        {
            public int Id;
            public string Title;
            public string Description;
            public long Target;
            public ItemType RewardItem;
            public long RewardAmount;
        }

        private static readonly MissionDef[] _missions = new[]
        {
            new MissionDef { Id = 0, Title = "Копатель-ученик", Description = "Сломайте 50 блоков", Target = 50, RewardItem = ItemType.Cred, RewardAmount = 25 },
            new MissionDef { Id = 1, Title = "Опытный копатель", Description = "Сломайте 200 блоков", Target = 200, RewardItem = ItemType.Cred, RewardAmount = 100 },
            new MissionDef { Id = 2, Title = "Мастер-копатель", Description = "Сломайте 500 блоков", Target = 500, RewardItem = ItemType.Cred, RewardAmount = 300 },
        };

        private int _activeMissionId = -1;
        private long _missionProgress;
        private readonly bool[] _missionCompleted = new bool[_missions.Length];

        public void Connect()
        {
            if (_status != ConnectionStatus.Disconnected)
            {
                return;
            }

            _status = ConnectionStatus.Connecting;
            OnConnecting?.Invoke();

            // Run asynchronously, but stay on the Unity Main Thread
            ConnectAsync().Forget();
        }

        private async UniTaskVoid ConnectAsync()
        {
            await UniTask.Yield();

            _status = ConnectionStatus.Connected;
            OnConnected?.Invoke();
        }

        public void Disconnect()
        {
            if (_status != ConnectionStatus.Connected)
            {
                return;
            }

            _worldLayer?.Dispose();
            _worldLayer = null;

            _status = ConnectionStatus.Disconnecting;
            OnDisconnecting?.Invoke();
            DisconnectAsync().Forget();
        }

        private async UniTaskVoid DisconnectAsync()
        {
            await UniTask.Delay(100);
            _status = ConnectionStatus.Disconnected;
            OnDisconnected?.Invoke();
        }

        private async UniTaskVoid UpdatePosition()
        {
            await UniTask.Delay(200);
            SendMapChunksAround(_x, _y);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new RobotPositionPacket(_mockBotId, _x, _y, (byte)_rot) })));
        }

        private static HashSet<string> LoadTokensFromFile()
        {
            try
            {
                if (!File.Exists(TokenStorePath))
                {
                    return new HashSet<string>();
                }

                string json = File.ReadAllText(TokenStorePath);
                var tokens = JsonConvert.DeserializeObject<List<string>>(json);
                if (tokens != null && tokens.Count > 0)
                {
                    return new HashSet<string>(tokens);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DummyConnection] Failed to load tokens: {ex.Message}");
            }

            return new HashSet<string>();
        }

        private static void SaveTokensToFile()
        {
            try
            {
                string json = JsonConvert.SerializeObject(new List<string>(_validTokens));
                File.WriteAllText(TokenStorePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DummyConnection] Failed to save tokens: {ex.Message}");
            }
        }

        public void Dispose()
        {
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

        public void SendAsync(ClientPacket packet)
        {
            if (packet.Data is ActionClientPacket actionPacket)
            {
                if (actionPacket.Payload is MovePacket move)
                {
                    if (_teleportWindowOpen)
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
                        var cellConfig = Fodinae.Core.ServiceLocator.Resolve<MapManager>()?.GetCellConfig(cellType);
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
                    CheckTeleportEntry();
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
                        int crystalIdx = GetCrystalBasketIndex(cellType);
                        var mm = Fodinae.Core.ServiceLocator.Resolve<MapManager>();
                        var cellConfig = mm?.GetCellConfig(cellType);
                        bool isBreakable = cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Breakable);

                        if (!isBreakable && cellType != CellType.Empty)
                        {
                            return;
                        }

                        SetServerCell(cellX, cellY, CellType.Empty);

                        if (crystalIdx >= 0)
                        {
                            var stats = Fodinae.Core.ServiceLocator.Resolve<IPlayerStats>();
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

                    if (_activeMissionId >= 0)
                    {
                        _missionProgress++;
                        OnReceived?.Invoke(new ServerPacket(new MissionProgressPacket(_missionProgress, _missions[_activeMissionId].Target)));
                        if (_missionProgress >= _missions[_activeMissionId].Target)
                        {
                            CompleteMission();
                        }
                    }
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

                    SendMapChunksAround(_x, _y);
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
                        var mm = ServiceLocator.Resolve<MapManager>();
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
                    TryBuild(front.X, front.Y, CellType.MilitaryBlock);
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
                        TryBuild(front.X, front.Y, CellType.Road);
                    }
                }
                else if (actionPacket.Payload is BuildGreenPacket)
                {
                    var front = GetFrontCell();
                    TryUpgradeBuild(front.X, front.Y,
                        (CellType.Empty, CellType.GreenBlock),
                        (CellType.GreenBlock, CellType.YellowBlock),
                        (CellType.YellowBlock, CellType.RedBlock));
                }
                else if (actionPacket.Payload is BuildWhitePacket)
                {
                    var front = GetFrontCell();
                    TryUpgradeBuild(front.X, front.Y,
                        (CellType.Empty, CellType.Support),
                        (CellType.Support, CellType.QuadBlock));
                }
                else if (actionPacket.Payload is ClickCellPacket click)
                {
                    _pathCts?.Cancel();
                    _pathCts?.Dispose();
                    _pathCts = null;
                    var path = FindPath(_x, _y, click.X, click.Y);
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
                        SendAuthWindow();
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
                    HandleAssetRequest(runtimeAssets).Forget();
                    break;
                case OpenHelpClickPacket:
                    break;
                case OpenSettingsClickPacket:
                    break;
                case ChangeChatColorPacket colorChange:
                    _chatColor = colorChange.Color;
                    break;
                case OpenClanClickPacket:
                    if (_clanId == 0)
                    {
                        SendClanListWindow();
                    }
                    else
                    {
                        SendClanInfoWindow();
                    }

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
            var (name, desc) = GetItemInfo(item);
            return new MinesServer.Networking.Server.Packets.Inventory.SelectItemPacket(
                item, name, desc, 1, 1, 3, false, new BitArray(0));
        }

        private static (string name, string desc) GetItemInfo(ItemType i) => i switch
        {
            ItemType.Teleport => ("Телепорт", "Строительный пак, который позволяет игрокам телепортироваться на другой телепорт"),
            ItemType.Resp => ("Респаун", "Строительный пак, который позволяет возрождаться и ремонтировать робота"),
            ItemType.Up => ("UP", "Строительный пак, который позволяет игрокам устанавливать и прокачивать умения"),
            ItemType.Market => ("Маркет", "Строительный пак, который позволяет игрокам покупать и продавать кристаллы, а также обмениваться друг с другом"),
            ItemType.Clans => ("Кланс", "Строительный пак, который позволяет просмотреть список кланов и вступить в один из кланов"),
            ItemType.PlasmBomb => ("Плазменная бомба", "Предмет, который позволяет взорвать блоки в радиусе 3 клеток(Красноскал с 1% шансом)"),
            ItemType.ProtonBomb => ("Протонная бомба", "Предмет, который позволяет взорвать блоки 3х3 от центра(Красноскал с 100% шансом)"),
            ItemType.RazBomb => ("Бомба-разряд", "Предмет, который позволяет нанести урон игрокам (500 HP) и Строительным пакам(10 HP)"),
            ItemType.Cred => ("Кредиты", "Валюта, которая позволяет увеличивать слоты роботов, создавать кланы и покупать скины для роботов"),
            ItemType.Rem => ("Ремонтный бот", "Предмет, который позволяет полностью восстановить здоровье робота"),
            ItemType.Geopack => ("Геопак", "Предмет, который позволяет упаковать живой кристалл в инвентарь"),
            ItemType.GeoCyan => ("Голубая жива", "Живка, которая даёт плод голубыми кристаллами"),
            ItemType.GeoRed => ("Красная жива", "Живка, которая даёт плод красными кристаллами, если поблизости есть черноскал"),
            ItemType.GeoViolet => ("Фиолетовая жива", "Живка, которая даёт плод фиолетовыми кристаллами, если поблизости есть черноскал"),
            ItemType.GeoBlack => ("Чёрная жива", "Живка, которая даёт плод голубыми и красными кристаллами, если стоит вплотную к такой же живке"),
            ItemType.GeoWhite => ("Белая жива", "Живка, которая даёт плод белыми кристаллами, если сверху стоит магма"),
            ItemType.GeoBlue => ("Синяя жива", "Живка, которая даёт плод синими кристаллами, если есть место для передвижения живки"),
            ItemType.VulkanRadar => ("Радар вулканов", "Предмет, который позволяет обнаружить вулканы"),
            ItemType.AliveRadar => ("Радар живок", "Предмет, который позволяет обнаружить живые кристаллы в радиусе 200 блоков"),
            ItemType.RobotRadar => ("Радар роботов", "Предмет, который позволяет обнаружить роботов в радиусе 300 блоков"),
            ItemType.PortableTeleporter => ("ТПР", "Предмет, который позволяет игроку телепортироваться на Респаун без потери кристаллов"),
            ItemType.ConstructionBot => ("Конструкционный бот", "Предмет, увеличивающий вместимость кристаллов в строительных паках"),
            ItemType.Generator => ("Боевой Генератор", "Предмет, увеличивающий урон пушки"),
            ItemType.Charge => ("Заряд защиты", "Предмет, увеличивающий здоровье строительных паков"),
            ItemType.Craft => ("Крафт", "Строительный пак, в котором можно создать паки и предметы"),
            ItemType.BombShop => ("Магазин бомб", "Строительный пак, в котором продаются бомбы за кредиты"),
            ItemType.Gun => ("Клановая Пушка", "Строительный клановый пак, позволяющий защитить территорию клана"),
            ItemType.Gate => ("Ворота", "Строительный клановый пак, через который могут пройти только участники клана"),
            ItemType.Disassembler => ("Диззассемблер", "Предмет, позволяющий собрать строительный пак в инвентарь"),
            ItemType.Storage => ("Склад", "Строительный пак, в котором можно хранить кристаллы"),
            ItemType.Scanner => ("Сканер паков", "Предмет, при использовании которого показываются характеристики строительного пака"),
            ItemType.UpgradeBooster => ("Прокачка x3", "Предмет, который ускоряет прокачку в 3 раза (24ч)"),
            ItemType.FreeUp => ("Freeup", "Предмет, который увеличивает оптимизацию до 75% на прокачку (12ч)"),
            ItemType.MineBooster => ("Добыча x4", "Предмет, который увеличивает добычу кристалла в 4 раза (12ч)"),
            ItemType.GeoHypno => ("Гипноскал", "Блок, который защищает вместе с пушкой территорию клана"),
            ItemType.Poly => ("Полимер", "Компонент/Предмет используемый в крафтинге и при помощи которого можно строить полимерную дорогу"),
            ItemType.Nano => ("Нано бот", "Компонент/Предмет используемый в крафтинге и при помощи которого можно восстановить здоровье робота на 50 HP"),
            ItemType.Battery => ("Аккумулятор", "Компонент/Предмет используемый в крафтинге и при помощи которого можно увеличить скорость робота"),
            ItemType.Trans => ("Транслятор", "Компонент/Предмет используемый в крафтинге и при помощи которого можно между своими роботами переключаться и передавать кристаллы"),
            ItemType.Compressor => ("Компрессор", "Компонент/Предмет используемый в крафтинге"),
            ItemType.C190 => ("С-190", "Компонент/Предмет используемый в крафтинге и при помощи которого можно наносить урон другим игрокам"),
            ItemType.FED => ("Fed база", "Предмет, который позволяет ставить золотую дорогу"),
            ItemType.GeoBlackRock => ("Чёрная скала", "Предмет, который мгновенно ставит черноскал на пустоте"),
            ItemType.GeoRedRock => ("Красная скала", "Предмет, который мгновенно ставит красноскал на пустоте"),
            ItemType.Auto => ("Автоматизатор", "Предмет, который пополняет кристаллами из ближайшего кланового/личного склада"),
            ItemType.EMI => ("ЭМИ", "Предмет, который запрещает игрокам в радиусе 20 блоков использовать инвентарь/копать"),
            ItemType.GeoRainbow => ("Радужная жива", "Живка, которая даёт плод любым блоком, если с одной из сторон по горизонтали или вертикали не пусто"),
            ItemType.BotSpot => ("Спот", "Предмет, который создаёт робота-клона"),
            ItemType.ScienceCentre => ("Научный центр", "Строительный пак, в котором можно изучить мир, и ознакомиться со списком лучших игроков/кланов"),
            ItemType.Currency => ("Валюта", "Валюта, которая является основной для торговли и прокачки умений."),
            ItemType.OPP => ("ОПП", "Очки, которые дают возможность купить другие умения, которые лучше чем начальные"),
            _ => (i.ToString(), string.Empty),
        };

        private void HandleUseItem()
        {
            if (_selectedItemType == null)
            {
                return;
            }

            var selectedType = _selectedItemType.Value;
            if (IsBuildingPack(selectedType))
            {
                var packType = ItemTypeToPackType(selectedType);
                if (packType == PackType.None)
                {
                    return;
                }

                ushort frontX = _x;
                ushort frontY = _y;
                switch (_rot)
                {
                    case Direction.Up: frontY++; break;
                    case Direction.Down: frontY--; break;
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

                ConsumeItem(selectedType, 1);
            }
            else if (selectedType == ItemType.Rem)
            {
                _health = 500;
                OnReceived?.Invoke(new ServerPacket(new HealthPacket(500, 500)));
                ConsumeItem(selectedType, 1);
            }
            else if (selectedType == ItemType.UpgradeBooster)
            {
                StartBuffLoop();
                const string tag = "xp3";
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expiry = Math.Max(_activeBuffs.GetValueOrDefault(tag), now) + 86400;
                _activeBuffs[tag] = expiry;
                OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(0, System.Drawing.Color.FromArgb(0, 200, 0), tag, new[] { "Прокачка x3", expiry.ToString() })));
                ConsumeItem(selectedType, 1);
            }
            else if (selectedType == ItemType.FreeUp)
            {
                StartBuffLoop();
                const string tag = "freeup";
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expiry = Math.Max(_activeBuffs.GetValueOrDefault(tag), now) + 43200;
                _activeBuffs[tag] = expiry;
                OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(0, System.Drawing.Color.Cyan, tag, new[] { "Freeup", expiry.ToString() })));
                ConsumeItem(selectedType, 1);
            }
            else if (selectedType == ItemType.MineBooster)
            {
                StartBuffLoop();
                const string tag = "x4";
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expiry = Math.Max(_activeBuffs.GetValueOrDefault(tag), now) + 43200;
                _activeBuffs[tag] = expiry;
                OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(0, System.Drawing.Color.FromArgb(255, 165, 0), tag, new[] { "Добыча x4", expiry.ToString() })));
                ConsumeItem(selectedType, 1);
            }
            else if (selectedType == ItemType.Battery)
            {
                StartBuffLoop();
                const string tag = "battery";
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expiry = Math.Max(_activeBuffs.GetValueOrDefault(tag), now) + 3600;
                _activeBuffs[tag] = expiry;
                OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(0, System.Drawing.Color.FromArgb(65, 105, 225), tag, new[] { "Аккумулятор", expiry.ToString() })));
                ConsumeItem(selectedType, 1);
            }
            else
            {
                ConsumeItem(selectedType, 1);
            }
        }

        private void ConsumeItem(ItemType type, long count)
        {
            if (!_inventory.TryGetValue(type, out long current) || current <= 0)
            {
                return;
            }

            long remaining = Math.Max(0, current - count);
            _inventory[type] = remaining;
            OnReceived?.Invoke(new ServerPacket(new InventoryPacket(
                new Dictionary<ItemType, long> { { type, remaining } })));
        }

        private static bool IsBuildingPack(ItemType type) => type switch
        {
            ItemType.Teleport or ItemType.Resp or ItemType.Up or ItemType.Market or
            ItemType.Clans or ItemType.Craft or ItemType.BombShop or ItemType.Gun or
            ItemType.Storage or ItemType.ScienceCentre => true,
            _ => false,
        };

        private static PackType ItemTypeToPackType(ItemType type) => type switch
        {
            ItemType.Teleport => PackType.Teleport,
            ItemType.Resp => PackType.Resp,
            ItemType.Up => PackType.Up,
            ItemType.Market => PackType.Market,
            ItemType.Clans => PackType.Clans,
            ItemType.Craft => PackType.Craft,
            ItemType.BombShop => PackType.BombShop,
            ItemType.Gun => PackType.Gun,
            ItemType.Storage => PackType.Storage,
            ItemType.ScienceCentre => PackType.Science,
            _ => PackType.None,
        };

        private void HandleElementClick(ElementClickPacket packet)
        {
            if (packet.WindowTag == "daily_bonus")
            {
                HandleDailyBonusClaim();
            }
            else if (packet.WindowTag == "teleport")
            {
                if (!_teleportWindowOpen)
                {
                    return;
                }

                if (packet.ElementIndex == 0)
                {
                    _teleportWindowOpen = false;
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else
                {
                    HandleTeleportClick(packet.ElementIndex - 1);
                }
            }
            else if (packet.WindowTag == "test_modal")
            {
                OnReceived?.Invoke(new ServerPacket(new ModalWindowPacket(
                    "Тестовое окно",
                    "Это модальное окно вызывается из HUD.\n\nНажмите OK чтобы продолжить.",
                    "OK",
                    string.Empty)));
            }
            else if (packet.WindowTag == "join_clan")
            {
                _clanId = 1;
                OnReceived?.Invoke(new ServerPacket(new ShowClanPacket(1)));
            }
            else if (packet.WindowTag == "leave_clan")
            {
                _clanId = 0;
                OnReceived?.Invoke(new ServerPacket(new HideClanPacket()));
            }
            else if (packet.WindowTag == "clan_list")
            {
                if (packet.ElementIndex == 0)
                {
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else
                {
                    int idx = packet.ElementIndex - 1;
                    if (idx >= 0 && idx < _mockClans.Length)
                    {
                        _clanId = _mockClans[idx].Id;
                        OnReceived?.Invoke(new ServerPacket(new ShowClanPacket(_clanId)));
                        OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                    }
                }
            }
            else if (packet.WindowTag == "clan_info")
            {
                if (packet.ElementIndex == 0)
                {
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else
                {
                    _clanId = 0;
                    OnReceived?.Invoke(new ServerPacket(new HideClanPacket()));
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
            }
            else if (packet.WindowTag == "open_missions")
            {
                SendMissionWindow();
            }
            else if (packet.WindowTag == "missions")
            {
                if (packet.ElementIndex == 0)
                {
                    OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                }
                else if (packet.ElementIndex <= _missions.Length)
                {
                    StartMission(packet.ElementIndex - 1);
                }
                else
                {
                    CancelMission();
                }
            }
            else if (packet.WindowTag == "open_url_test")
            {
                OnReceived?.Invoke(new ServerPacket(new OpenURLPacket("https://vk.ru/mines4reborn")));
            }
            else if (packet.WindowTag == "test_mission_arrow")
            {
                OnReceived?.Invoke(new ServerPacket(new MissionArrowPacket((ushort)_x, (ushort)_y)));
            }
            else if (packet.WindowTag == "save_client_config")
            {
                foreach (var kv in packet.Context)
                {
                    switch (kv.Key)
                    {
                        case "master_volume":
                            if (byte.TryParse(kv.Value, out var masterVol))
                            {
                                _clientMasterVolume = masterVol;
                            }

                            break;
                        case string key when key.EndsWith("_volume") && key.Length > 7:
                            string soundKey = key.Substring(0, key.Length - 7);
                            if (byte.TryParse(kv.Value, out var soundVol))
                            {
                                _clientSoundVolumes[soundKey] = soundVol;
                            }

                            break;
                        case "renderer":
                            if (Enum.TryParse<RendererMode>(kv.Value, out var renderer))
                            {
                                _clientRenderer = renderer;
                            }

                            break;
                        case "keybind":
                            _clientKeybinds.Add(new StringPairPacket("unknown", kv.Value));
                            break;
                    }
                }

                SendClientConfigPacket();
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
                SaveTokensToFile();
                OnReceived?.Invoke(new ServerPacket(new AuthTokenPacket(newToken)));

                InitWorld();
            }
        }

        private void SendAuthWindow()
        {
            _awaitingAuth = true;

            var titleText = new TextPacket
            {
                Text = "<color=#B2A680>Авторизация</color>",
                AttachedProperties = new StringPairPacket[]
                {
                    new("DockPanel.Dock", "Top"),
                },
            };

            var descriptionText = new TextPacket
            {
                Text = "<color=white>Нажмите «Авторизоваться» чтобы начать игру</color>",
                Style = new GUIStylePacket
                {
                    Margin = new Margins(0, 0, 20, 0),
                },

                // Кнопка и текст теперь жестко привязаны к сетке сверху вниз
                AttachedProperties = new StringPairPacket[]
                 {
            new("DockPanel.Dock", "Top"),
                 },
            };

            var authButton = new TextPacket
            {
                Text = "<color=white>Авторизоваться</color>",
                OnClickContext = ".",
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 40, 167, 69),
                    Border = System.Drawing.Color.FromArgb(255, 60, 200, 100),
                    BorderWidth = 2,
                    Padding = new Margins(10, 10, 6, 6),
                    Margin = new Margins(0, 0, 0, 0),
                },

                // Кнопка встанет строго под описанием внутри темного окна
                AttachedProperties = new StringPairPacket[]
                {
            new("DockPanel.Dock", "Top"),
                },
            };
            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(10, 10, 10, 10),
                },
                Children = new List<IGUIComponentPacket>
                {
                    titleText,
                    descriptionText,
                    authButton,
                },
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("auth", 300, 160, root)));
        }

        private void InitWorld()
        {
            _cellConfigs = CreateTestCellConfigurations();
            _worldLayer?.Dispose();
            _worldLayer = null;

            string? mapbPath = GetProjectServerMapFile(PrebakedWorldCodeName);
            if (string.IsNullOrEmpty(mapbPath) || !File.Exists(mapbPath))
            {
                Debug.LogError($"[DummyConnection] Prebaked map file for '{PrebakedWorldCodeName}' not found! Fail-fast without fallbacks.");
                TriggerDisconnect($"Prebaked map file for '{PrebakedWorldCodeName}' not found");
                return;
            }

            (int worldWidth, int worldHeight) = ReadPrebakedWorldDimensions(mapbPath);
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                Debug.LogError($"[DummyConnection] Prebaked map file '{mapbPath}' has invalid dimensions ({worldWidth}x{worldHeight}). Fail-fast!");
                TriggerDisconnect("Invalid prebaked map dimensions");
                return;
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
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(_mockBotId, 999, 1, "Skin/bee.png", "Tail/default.png", "Darkar25")));
            var robotPos = new RobotPositionPacket(_mockBotId, 25, 50, 0);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { robotPos })));
            HandleRobotInfoMock(_mockBotId).Forget();
            RunCircularBots(0).Forget();
            _x = 25;
            _y = 50;
            SendMapChunksAround(_x, _y);
            OnReceived?.Invoke(new ServerPacket(new AggressionStatePacket(false)));
            OnReceived?.Invoke(new ServerPacket(new AutoMineStatePacket(false)));
            OnReceived?.Invoke(new ServerPacket(new DailyBonusStatePacket(false)));
            _bonusCountdown = 10;
            _bonusClaimed = false;
            OnReceived?.Invoke(new ServerPacket(new CurrencyPacket(123456, 1234)));
            _health = 250;
            OnReceived?.Invoke(new ServerPacket(new HealthPacket(250, 500)));
            _basketContents = new long[6];
            OnReceived?.Invoke(new ServerPacket(new BasketPacket(50000, _basketContents)));
            OnReceived?.Invoke(new ServerPacket(new GeologyPacket(5, 10, CellType.Lava, "Lava")));
            OnReceived?.Invoke(new ServerPacket(new LevelPacket(12345)));

            SendSkillProgressMock();
            SendChatMock().Forget();

            OnReceived?.Invoke(new ServerPacket(new OnlinePacket(42, 3)));
            OnReceived?.Invoke(new ServerPacket(default(ClearStatusPacket)));
            foreach (var kvp in _activeBuffs)
            {
                var (color, name) = kvp.Key switch
                {
                    "xp3" => (System.Drawing.Color.FromArgb(0, 200, 0), "Прокачка x3"),
                    "freeup" => (System.Drawing.Color.Cyan, "Freeup"),
                    "x4" => (System.Drawing.Color.FromArgb(255, 165, 0), "Добыча x4"),
                    "battery" => (System.Drawing.Color.FromArgb(65, 105, 225), "Аккумулятор"),
                    _ => (System.Drawing.Color.White, kvp.Key),
                };
                OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(0, color, kvp.Key, new[] { name, kvp.Value.ToString() })));
            }

            StartBuffLoop();
            SendPingMock().Forget();
            SendDailyBonusMock().Forget();

            OnReceived?.Invoke(new ServerPacket(new MovementSpeedPacket(new Dictionary<CellType, ushort>
            {
                [CellType.Empty] = 100,
                [CellType.Road] = 20,
            })));
            OnReceived?.Invoke(new ServerPacket(new MaxDepthPacket(200)));

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

            SendClientConfigPacket();
        }

        private void SendClientConfigPacket()
        {
            OnReceived?.Invoke(new ServerPacket(new ClientConfigPacket(
                new SoundConfigPacket(_clientMasterVolume, _clientSoundVolumes),
                _clientRenderer,
                _clientKeybinds,
                _clientUnrenderedTextures)));

            var serverConfig = ServiceLocator.Resolve<ServerConfig>();
            serverConfig?.ApplyValues(_digCooldown, _maxGlobalChatLength, _maxLocalChatLength);
        }

        private void SendMissionWindow()
        {
            var rows = new List<IGUIComponentPacket>();
            for (int i = 0; i < _missions.Length; i++)
            {
                var m = _missions[i];
                string status = _activeMissionId == m.Id
                    ? $"<color=yellow>Активно: {_missionProgress}/{m.Target}</color>"
                    : _missionCompleted[m.Id]
                        ? "<color=lime>✓ Выполнено</color>"
                        : "<color=#B2A680>Выбрать</color>";
                rows.Add(new TextPacket
                {
                    Text = $"<color=white>{m.Title}</color>\n<color=#B2A680>{m.Description}</color>  {status}",
                    OnClickContext = ".",
                    Style = new GUIStylePacket
                    {
                        Background = System.Drawing.Color.FromArgb(242, 26, 26, 26),
                        Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                        BorderWidth = 2,
                        Padding = new Margins(8, 12, 8, 12),
                        Margin = new Margins(0, 0, 4, 0),
                    },
                });
            }

            var scrollViewer = new ScrollViewerPacket
            {
                VerticalScrollBar = ScrollbarVisibility.Auto,
                HorizontalScrollBar = ScrollbarVisibility.Auto,
                Children = rows.ToArray(),
            };

            var rootChildren = new List<IGUIComponentPacket>
            {
                new DockPanelPacket
                {
                    AttachedProperties = new StringPairPacket[]
                    {
                        new("DockPanel.Dock", "Top"),
                    },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 10, 0),
                        Padding = new Margins(0, 0, 0, 0),
                    },
                    Children = new List<IGUIComponentPacket>
                    {
                        new TextPacket
                        {
                            Text = "<color=#B2A680>Миссии</color>",
                            AttachedProperties = new StringPairPacket[]
                            {
                                new("DockPanel.Dock", "Left"),
                            },
                        },
                        new TextPacket
                        {
                            Text = "<color=#B3B3B3>×</color>",
                            OnClickContext = "missions_close",
                            AttachedProperties = new StringPairPacket[]
                            {
                                new("DockPanel.Dock", "Right"),
                            },
                        },
                    },
                },
                scrollViewer,
            };

            if (_activeMissionId >= 0)
            {
                rootChildren.Add(new TextPacket
                {
                    Text = "<color=#B08050>Отменить миссию</color>",
                    OnClickContext = "mission_cancel",
                    AttachedProperties = new StringPairPacket[]
                    {
                        new("DockPanel.Dock", "Bottom"),
                    },
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 10, 0),
                        Padding = new Margins(6, 6, 6, 6),
                        Background = System.Drawing.Color.FromArgb(242, 30, 20, 20),
                        Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                        BorderWidth = 2,
                    },
                });
            }

            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(2, 8, 2, 8),
                },
                Children = rootChildren,
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("missions", 400, 300, root)));
        }

        private void StartMission(int missionId)
        {
            if (missionId < 0 || missionId >= _missions.Length)
            {
                return;
            }

            if (_missionCompleted[missionId])
            {
                return;
            }

            var m = _missions[missionId];
            _activeMissionId = missionId;
            _missionProgress = 0;
            OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
            OnReceived?.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, m.Title, m.Description)));
            OnReceived?.Invoke(new ServerPacket(new MissionProgressPacket(0, m.Target)));
            OnReceived?.Invoke(new ServerPacket(new MissionArrowPacket((ushort)(_x + 2), (ushort)(_y + 2))));
        }

        private void CancelMission()
        {
            if (_activeMissionId < 0)
            {
                OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
                return;
            }

            _activeMissionId = -1;
            _missionProgress = 0;
            OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
            OnReceived?.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
        }

        private void CompleteMission()
        {
            if (_activeMissionId < 0)
            {
                return;
            }

            var m = _missions[_activeMissionId];

            _inventory.TryGetValue(m.RewardItem, out long current);
            _inventory[m.RewardItem] = current + m.RewardAmount;
            OnReceived?.Invoke(new ServerPacket(new InventoryPacket(
                new Dictionary<ItemType, long> { { m.RewardItem, current + m.RewardAmount } })));

            _missionCompleted[_activeMissionId] = true;
            _activeMissionId = -1;
            _missionProgress = 0;

            OnReceived?.Invoke(new ServerPacket(new MissionInitPacket(string.Empty, 0, 0, string.Empty, string.Empty)));
            OnReceived?.Invoke(new ServerPacket(new ModalWindowPacket(
                "Миссия выполнена!",
                $"Вы завершили миссию \"{m.Title}\"!\n\nНаграда: {m.RewardAmount} кредитов.",
                "OK",
                string.Empty)));
        }

        private void HandleDailyBonusClaim()
        {
            var rewardItem = _pendingBonusItem;
            var rewardAmount = _pendingBonusAmount;

            _inventory.TryGetValue(rewardItem, out long current);
            long newQty = current + rewardAmount;
            _inventory[rewardItem] = newQty;

            OnReceived?.Invoke(new ServerPacket(new InventoryPacket(
                new Dictionary<ItemType, long> { { rewardItem, newQty } })));

            _bonusClaimed = true;
        }

        private void StartBuffLoop()
        {
            if (_buffLoopStarted)
            {
                return;
            }

            _buffLoopStarted = true;
            CheckBuffsLoop().Forget();
        }

        private async UniTaskVoid CheckBuffsLoop()
        {
            while (_status == ConnectionStatus.Connected)
            {
                await UniTask.Delay(1000);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var expired = _activeBuffs.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
                foreach (var tag in expired)
                {
                    _activeBuffs.Remove(tag);
                    OnReceived?.Invoke(new ServerPacket(new ClearStatusLinePacket(tag)));
                }

                // Depth warning check
                if (_y > _maxDepth)
                {
                    if (!_depthWarningActive)
                    {
                        _depthWarningActive = true;
                        OnReceived?.Invoke(new ServerPacket(new AddStatusLinePacket(
                            0, System.Drawing.Color.Red, "depth_warning", new[] { "⚠ Критическая глубина!" })));
                    }
                }
                else
                {
                    if (_depthWarningActive)
                    {
                        _depthWarningActive = false;
                        OnReceived?.Invoke(new ServerPacket(new ClearStatusLinePacket("depth_warning")));
                    }
                }

                // Depth damage
                if (_y > _maxDepth)
                {
                    int blocksBelow = _y - _maxDepth;
                    int damage = (((blocksBelow - 1) / 10) + 1) * 10;
                    _health = Math.Max(0, _health - damage);
                    OnReceived?.Invoke(new ServerPacket(new HealthPacket(_health, 500)));
                    if (_health <= 0)
                    {
                        const ushort SPAWN_X = 25;
                        const ushort SPAWN_Y = 50;
                        var deathX = _x;
                        var deathY = _y;
                        _x = SPAWN_X;
                        _y = SPAWN_Y;
                        _rot = Direction.Up;
                        _health = 500;
                        OnReceived?.Invoke(new ServerPacket(new HealthPacket(500, 500)));
                        OnReceived?.Invoke(new ServerPacket(new TeleportPacket(SPAWN_X, SPAWN_Y, false)));
                        OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                        {
                            new RobotPositionPacket(_mockBotId, SPAWN_X, SPAWN_Y, (byte)_rot),
                            new AudioPacket(SFX.Death, _mockBotId, deathX, deathY, Array.Empty<StringPairPacket>()),
                        })));
                    }
                }
            }
        }

        private void CheckTeleportEntry()
        {
            if (!_teleportPositions.Contains((_x, _y)))
            {
                return;
            }

            SendTeleportWindow();
        }

        private void SendTeleportWindow()
        {
            _teleportDestinations = _teleportPositions
                .Where(tp => tp.X != _x || tp.Y != _y)
                .ToList();

            if (_teleportDestinations.Count == 0)
            {
                SendTeleportWindowNoDestinations();
                return;
            }

            var rows = new IGUIComponentPacket[_teleportDestinations.Count];
            for (int i = 0; i < _teleportDestinations.Count; i++)
            {
                var (destX, destY) = _teleportDestinations[i];
                rows[i] = new TextPacket
                {
                    Text = $"<color=white>Телепорт на ({destX,5}, {destY,5})</color>   <color=#B2A680>[ТП]</color>",
                    OnClickContext = ".",
                    Style = new GUIStylePacket
                    {
                        Background = System.Drawing.Color.FromArgb(242, 26, 26, 26),
                        Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                        BorderWidth = 2,
                        Padding = new Margins(8, 12, 8, 12),
                        Margin = new Margins(0, 0, 4, 0),
                    },
                };
            }

            var scrollViewer = new ScrollViewerPacket
            {
                VerticalScrollBar = ScrollbarVisibility.Auto,
                HorizontalScrollBar = ScrollbarVisibility.Auto,
                Children = rows,
            };

            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(2, 8, 2, 8),
                },
                Children = new List<IGUIComponentPacket>
                {
                    new DockPanelPacket
                    {
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Top"),
                        },
                        Style = new GUIStylePacket
                        {
                            Margin = new Margins(0, 0, 10, 0),
                            Padding = new Margins(0, 0, 0, 0),
                        },
                        Children = new List<IGUIComponentPacket>
                        {
                    new TextPacket
                    {
                        Text = "<color=#B2A680>Телепорты</color>",
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Left"),
                        },
                    },
                    new TextPacket
                            {
                    Text = "<color=#B3B3B3>×</color>",
                    OnClickContext = "teleport_close",
                    AttachedProperties = new StringPairPacket[]
                    {
                        new("DockPanel.Dock", "Right"),
                    },
                            },
                        },
                    },
                    scrollViewer,
                },
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 300, root)));
            _teleportWindowOpen = true;
        }

        private void SendTeleportWindowNoDestinations()
        {
            var text = new TextPacket
            {
                Text = "<color=gray>Нет доступных телепортов</color>",
            };

            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(0, 0, 0, 0),
                },
                Children = new List<IGUIComponentPacket>
                {
                    new DockPanelPacket
                    {
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Top"),
                        },
                        Style = new GUIStylePacket
                        {
                            Margin = new Margins(0, 0, 0, 0),
                            Padding = new Margins(0, 0, 0, 0),
                        },
                        Children = new List<IGUIComponentPacket>
                        {
                    new TextPacket
                    {
                        Text = "<color=#B2A680>Телепорты</color>",
                        AttachedProperties = new StringPairPacket[]
                        {
                            new("DockPanel.Dock", "Left"),
                        },
                    },
                    new TextPacket
                            {
                    Text = "<color=#B3B3B3>×</color>",
                    OnClickContext = "teleport_close",
                    AttachedProperties = new StringPairPacket[]
                    {
                        new("DockPanel.Dock", "Right"),
                    },
                            },
                        },
                    },
                    text,
                },
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("teleport", 400, 200, root)));
            _teleportWindowOpen = true;
        }

        private void SendClanListWindow()
        {
            var items = new List<IGUIComponentPacket>();
            foreach (var clan in _mockClans)
            {
                items.Add(new DockPanelPacket
                {
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 4, 0),
                        Padding = new Margins(4, 6, 4, 4),
                        Background = System.Drawing.Color.FromArgb(30, 60, 60, 60),
                        Border = System.Drawing.Color.FromArgb(60, 80, 80, 80),
                        BorderWidth = 1,
                    },
                    Children = new List<IGUIComponentPacket>
                    {
                        new ImagePacket
                        {
                            URI = $"clan/{clan.Id}.png",
                            Width = 16,
                            Height = 16,
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                        new TextPacket
                        {
                            Text = $"<color=white><b>Клан «{clan.Name}»</b>  <color=#888888>(ID: {clan.Id})</color></color>",
                            OnClickContext = ".",
                            AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                        },
                    },
                });
                items.Add(new TextPacket
                {
                    Text = $"<color=#999999>{clan.Desc}</color>",
                    Style = new GUIStylePacket
                    {
                        Margin = new Margins(0, 0, 8, 0),
                        Padding = new Margins(0, 10, 0, 0),
                    },
                });
            }

            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(8, 8, 8, 8),
                },
                Children = new List<IGUIComponentPacket>
                {
                    new DockPanelPacket
                    {
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                        Children = new List<IGUIComponentPacket>
                        {
                            new TextPacket
                            {
                                Text = "<color=#B2A680><b>Доступные кланы</b></color>",
                                AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                            },
                            new TextPacket
                            {
                                Text = "<color=#B3B3B3>×</color>",
                                OnClickContext = "clan_close",
                                AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Right") },
                            },
                        },
                    },
                    new ScrollViewerPacket
                    {
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                        Style = new GUIStylePacket
                        {
                            Margin = new Margins(6, 0, 0, 0),
                        },
                        Children = items,
                    },
                },
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("clan_list", 320, 260, root)));
        }

        private void SendClanInfoWindow()
        {
            string clanName = _clanId.ToString();
            string clanDesc = string.Empty;
            foreach (var c in _mockClans)
            {
                if (c.Id == _clanId)
                {
                    clanName = c.Name;
                    clanDesc = c.Desc;
                    break;
                }
            }

            var root = new DockPanelPacket
            {
                Style = new GUIStylePacket
                {
                    Background = System.Drawing.Color.FromArgb(242, 20, 20, 20),
                    Border = System.Drawing.Color.FromArgb(255, 89, 89, 89),
                    BorderWidth = 2,
                    Padding = new Margins(8, 8, 8, 8),
                },
                Children = new List<IGUIComponentPacket>
                {
                    new DockPanelPacket
                    {
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                        Children = new List<IGUIComponentPacket>
                        {
                            new TextPacket
                            {
                                Text = "<color=#B2A680><b>Мой клан</b></color>",
                                AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Left") },
                            },
                            new TextPacket
                            {
                                Text = "<color=#B3B3B3>×</color>",
                                OnClickContext = "clan_close",
                                AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Right") },
                            },
                        },
                    },
                    new TextPacket
                    {
                        Text = $"<color=white><b>Клан «{clanName}»</b></color>\n<color=#888888>ID: {_clanId}</color>\n<color=#999999>{clanDesc}</color>",
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                        Style = new GUIStylePacket
                        {
                            Margin = new Margins(8, 0, 8, 0),
                        },
                    },
                    new TextPacket
                    {
                        Text = "<color=#FF6666>Покинуть клан</color>",
                        OnClickContext = ".",
                        AttachedProperties = new[] { new StringPairPacket("DockPanel.Dock", "Top") },
                        Style = new GUIStylePacket
                        {
                            Padding = new Margins(6, 10, 6, 6),
                            Background = System.Drawing.Color.FromArgb(40, 80, 40, 40),
                            Border = System.Drawing.Color.FromArgb(60, 120, 60, 60),
                            BorderWidth = 1,
                            Margin = new Margins(0, 0, 0, 0),
                        },
                    },
                },
            };

            OnReceived?.Invoke(new ServerPacket(new OpenWindowPacket("clan_info", 300, 200, root)));
        }

        private void HandleTeleportClick(int index)
        {
            if (index < 0 || index >= _teleportDestinations.Count)
            {
                return;
            }

            var (destX, destY) = _teleportDestinations[index];

            _x = destX;
            _y = destY;

            _teleportWindowOpen = false;
            OnReceived?.Invoke(new ServerPacket(new TeleportPacket(destX, destY, false)));
            OnReceived?.Invoke(new ServerPacket(new CloseWindowPacket()));
            UpdatePosition().Forget();
        }

        private static ItemType PickRandomBonusItem()
        {
            var items = new[]
            {
                ItemType.Teleport, ItemType.Compressor, ItemType.C190, ItemType.Trans,
                ItemType.Nano, ItemType.Battery, ItemType.ConstructionBot, ItemType.PortableTeleporter,
                ItemType.Scanner, ItemType.GeoBlackRock, ItemType.GeoRedRock, ItemType.Cred,
                ItemType.GeoCyan, ItemType.GeoHypno, ItemType.Rem, ItemType.Charge,
                ItemType.Geopack, ItemType.Poly, ItemType.RazBomb, ItemType.ProtonBomb,
            };
            return items[_rng.Next(items.Length)];
        }

        private static long PickRandomAmount(ItemType item)
        {
            return item switch
            {
                ItemType.Teleport or ItemType.PortableTeleporter => 1,
                ItemType.Cred => _rng.Next(5, 11),
                ItemType.Rem => _rng.Next(50, 101),
                ItemType.Geopack => _rng.Next(10, 16),
                _ => _rng.Next(5, 20),
            };
        }

        private async UniTaskVoid SendDailyBonusMock()
        {
            while (_status == ConnectionStatus.Connected)
            {
                _bonusClaimed = false;
                _bonusCountdown = Math.Max(_bonusCountdown, 10);

                while (_bonusCountdown > 0 && !_bonusClaimed && _status == ConnectionStatus.Connected)
                {
                    await UniTask.Delay(1000);
                    _bonusCountdown--;
                }

                if (_status != ConnectionStatus.Connected)
                {
                    break;
                }

                _pendingBonusItem = PickRandomBonusItem();
                _pendingBonusAmount = (int)PickRandomAmount(_pendingBonusItem);
                OnReceived?.Invoke(new ServerPacket(new DailyBonusStatePacket(true)));

                while (!_bonusClaimed && _status == ConnectionStatus.Connected)
                {
                    await UniTask.Delay(500);
                }

                if (_status != ConnectionStatus.Connected)
                {
                    break;
                }

                _bonusCountdown = 10;
                OnReceived?.Invoke(new ServerPacket(new DailyBonusStatePacket(false)));
            }
        }

        private CellConfigurationPacket[] CreateTestCellConfigurations()
        {
            var configs = new CellConfigurationPacket[256];
            for (int i = 0; i < 256; i++)
            {
                configs[i] = new CellConfigurationPacket
                {
                    Animation = CellAnimationType.None,
                    AnimationSpeed = 0,
                    Color = unchecked((int)0xFF808080),
                    FrameOffset = 0,
                    Properties = CellConfigProperties.None,
                    ReliefGroup = 0,
                    Distortion = (CellDistortionType)0,
                };
            }

            const CellConfigProperties ROAD_PROPS = CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow;
            const CellConfigProperties SAND_BOULDER_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
            const CellConfigProperties ARTIFICIAL_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing;
            const CellConfigProperties ROCK_CRYSTAL_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
            const CellConfigProperties GLOWING_CRYSTAL_PROPS = ROCK_CRYSTAL_PROPS | CellConfigProperties.Glowing;
            const CellConfigProperties INDESTRUCTIBLE_PROPS = CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow;
            const CellConfigProperties BOX_PROPS = CellConfigProperties.Breakable | CellConfigProperties.DropsShadow | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing;

            // === ROADS: ReliefGroup = 0 ===
            SetConfig(configs, CellType.BuildingRoad, ROAD_PROPS | CellConfigProperties.Glowing, 0, color: unchecked((int)0xFFCCCCCC));
            SetConfig(configs, CellType.VolcanoBackground, ROAD_PROPS | CellConfigProperties.Glowing, 0);
            SetConfig(configs, CellType.Empty, ROAD_PROPS, 0, color: unchecked((int)0xFF808080));
            SetConfig(configs, CellType.Road, ROAD_PROPS, 0, color: unchecked((int)0xFFCCCCCC));
            SetConfig(configs, CellType.GoldenRoad, ROAD_PROPS, 0, color: unchecked((int)0xFFCCCC00));
            SetConfig(configs, CellType.PolymerRoad, ROAD_PROPS, 0);

            // === BOX: ReliefGroup = 0 ===
            SetConfig(configs, CellType.Box, BOX_PROPS, 0, distortion: CellDistortionType.Block);

            // === SANDS & BOULDERS: ReliefGroup = 1 ===
            SetConfig(configs, CellType.BlackBoulder1, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF000000), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.BlackBoulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.BlackBoulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.MetalBoulder1, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.MetalBoulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.MetalBoulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.WhiteSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFFFFF00), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkWhiteSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFCCCC00), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.RustySand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFCD853F), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkRustySand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF8B4513), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.BlackSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF2F2F2F), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkBlackSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF1A1A1A), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.BlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF4169E1), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkBlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF00008B), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.YellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFFFD700), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkYellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFB8860B), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepMagmaBoulder, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.MilitaryBlockSand, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Lava, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFFFF4500),
                animation: (CellAnimationType)4, animationSpeed: 10, frameOffset: 0, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Boulder1, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF000000), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Boulder2, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Boulder3, SAND_BOULDER_PROPS, 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.BlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF4169E1), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkBlueSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFF00008B), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.YellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFFFD700), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DarkYellowSand, SAND_BOULDER_PROPS, 1, color: unchecked((int)0xFFB8860B), distortion: CellDistortionType.Cause);

            // === ACIDS (keep existing animations): ReliefGroup = 1 ===
            SetConfig(configs, CellType.GrayAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF00FF00),
                animation: CellAnimationType.Blinking, animationSpeed: 5, frameOffset: 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.PurpleAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF800080),
                animation: CellAnimationType.Shimmer, animationSpeed: 50, frameOffset: 1, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.PassiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF8A2BE2), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.LivingActiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF66FF22), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.CorrosiveActiveAcid, SAND_BOULDER_PROPS | CellConfigProperties.Glowing, 1, color: unchecked((int)0xFF9AFF22), distortion: CellDistortionType.Cause);

            // === ARTIFICIAL: ReliefGroup = 2 ===
            SetConfig(configs, CellType.BuildingDoor, ARTIFICIAL_PROPS, 2, color: unchecked((int)0xFF8B4513), distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.BuildingCorner, ARTIFICIAL_PROPS, 2, color: unchecked((int)0xFF555555), distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.QuadBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.Support, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.MilitaryBlockFrame, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.MilitaryBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.GreenBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.YellowBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.FedBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.RedBlock, ARTIFICIAL_PROPS, 2, distortion: CellDistortionType.Block);
            SetConfig(configs, CellType.BuildingWall, ARTIFICIAL_PROPS, 2, color: unchecked((int)0xFF666666), distortion: CellDistortionType.Block);

            // === ROCKS & CRYSTALS: ReliefGroup = 3 ===
            SetConfig(configs, CellType.XGreen, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF00FF3D), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.XBlue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.XRed, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.XCyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.XViolet, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepObsidianRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepTurquoiseRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepRainbowRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepStripedRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Rock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Green, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF00FF00), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Red, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Blue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Violet, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.White, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Cyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.HeavyRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AcidRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.GoldenRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.GRock, ROCK_CRYSTAL_PROPS, 3, distortion: CellDistortionType.Cause);

            // === LIVING CRYSTALS & SPECIAL LUMINOUS MINERALS ===
            SetConfig(configs, CellType.AliveCyan, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF20C7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveRed, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF2920), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveViol, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveNigger, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF802EB8), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveWhite, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveRainbow, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.AliveBlue, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.Pearl, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFF2F7FF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.DeepLazuriteSand, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFF295FFF), distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.SuperRainbow, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFFF59E6));
            SetConfig(configs, CellType.HypnoRock, GLOWING_CRYSTAL_PROPS, 3, color: unchecked((int)0xFFBF20EB), distortion: CellDistortionType.Cause);

            // === INDESTRUCTIBLE ROCKS: ReliefGroup = 4 (NO Breakable!) ===
            SetConfig(configs, CellType.NiggerRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.LivingBlackRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);
            SetConfig(configs, CellType.RedRock, INDESTRUCTIBLE_PROPS, 4, distortion: CellDistortionType.Cause);

            // === GATE & TELEPORT BLOCK (passable but not roads) ===
            SetConfig(configs, CellType.Gate, CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing, 0);
            SetConfig(configs, CellType.TeleportBlock, CellConfigProperties.Passable | CellConfigProperties.ReceivesShadow | CellConfigProperties.Glowing, 0);

            return configs;
        }

        private static void SetConfig(CellConfigurationPacket[] configs, CellType type, CellConfigProperties props, byte reliefGroup,
            int color = unchecked((int)0xFF808080), CellAnimationType animation = CellAnimationType.None,
            byte animationSpeed = 0, byte frameOffset = 0, CellDistortionType distortion = (CellDistortionType)0)
        {
            configs[(int)type] = new CellConfigurationPacket
            {
                Properties = props,
                ReliefGroup = reliefGroup,
                Color = color,
                Animation = animation,
                AnimationSpeed = animationSpeed,
                FrameOffset = frameOffset,
                Distortion = distortion,
            };
        }

        private static string? GetProjectServerMapFile(string worldCodeName)
        {
            string streamingDirectory = Path.Combine(
                Application.streamingAssetsPath,
                "WorldMaps");
            string projectMapPath = Path.Combine(
                streamingDirectory,
                $"{worldCodeName}_cells.mapb");
            if (File.Exists(projectMapPath))
            {
                return projectMapPath;
            }

            string projectArchivePath = Path.Combine(
                streamingDirectory,
                $"{worldCodeName}_cells.zip");
            if (!File.Exists(projectArchivePath))
            {
                return null;
            }

            try
            {
                string serverCacheDirectory = Path.Combine(
                    Application.temporaryCachePath,
                    "DummyServerMaps");
                Directory.CreateDirectory(serverCacheDirectory);
                string serverMapPath = Path.Combine(
                    serverCacheDirectory,
                    $"{worldCodeName}_cells.mapb");
                using ZipArchive archive = ZipFile.OpenRead(projectArchivePath);
                ZipArchiveEntry? mapEntry = archive.GetEntry($"{worldCodeName}_cells.mapb");
                if (mapEntry == null)
                {
                    Debug.LogError(
                        $"[DummyConnection] Project server archive '{projectArchivePath}' " +
                        $"does not contain '{worldCodeName}_cells.mapb'.");
                    return null;
                }

                var cachedInfo = new FileInfo(serverMapPath);
                if (!cachedInfo.Exists ||
                    cachedInfo.Length != mapEntry.Length ||
                    cachedInfo.LastWriteTimeUtc != mapEntry.LastWriteTime.UtcDateTime)
                {
                    mapEntry.ExtractToFile(serverMapPath, overwrite: true);
                }

                return serverMapPath;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[DummyConnection] Failed to open project server map: {ex.Message}");
                return null;
            }
        }

        private void SendMapChunksAround(ushort serverX, ushort serverY)
        {
            const int ChunkSize = 32;
            const int StreamingRadiusChunks = 4;
            if (_worldLayer == null)
            {
                return;
            }

            int centerChunkX = serverX / ChunkSize;
            int centerChunkY = serverY / ChunkSize;
            int minimumChunkX = Math.Max(0, centerChunkX - StreamingRadiusChunks);
            int maximumChunkX = Math.Min(
                _worldLayer.WidthChunks - 1,
                centerChunkX + StreamingRadiusChunks);
            int minimumChunkY = Math.Max(0, centerChunkY - StreamingRadiusChunks);
            int maximumChunkY = Math.Min(
                _worldLayer.HeightChunks - 1,
                centerChunkY + StreamingRadiusChunks);
            for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
            {
                for (int chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
                {
                    int chunkIndex = chunkY + (chunkX * _worldLayer.HeightChunks);
                    if (_sentMapChunks.Contains(chunkIndex))
                    {
                        continue;
                    }

                    CellType[]? source = _worldLayer.GetChunk(
                        chunkIndex,
                        createIfMissing: true,
                        touchLru: true);
                    if (source == null)
                    {
                        continue;
                    }

                    var payload = new CellType[ChunkSize * ChunkSize];
                    for (int localY = 0; localY < ChunkSize; localY++)
                    {
                        for (int localX = 0; localX < ChunkSize; localX++)
                        {
                            payload[(localY * ChunkSize) + localX] =
                                source[localY + (localX * ChunkSize)];
                        }
                    }

                    _sentMapChunks.Add(chunkIndex);
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[]
                    {
                        new MapRegionPacket(
                            (ushort)(chunkX * ChunkSize),
                            (ushort)(chunkY * ChunkSize),
                            ChunkSize - 1,
                            ChunkSize - 1,
                            payload),
                    })));
                }
            }
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

        private static (int width, int height) ReadPrebakedWorldDimensions(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                int WIDTH_CHUNKS = br.ReadInt32();
                int HEIGHT_CHUNKS = br.ReadInt32();
                int CHUNK_SIZE = br.ReadInt32();
                br.ReadInt32(); // reserved

                if (WIDTH_CHUNKS > 0 && HEIGHT_CHUNKS > 0 && CHUNK_SIZE > 0 && CHUNK_SIZE <= 1024)
                {
                    int w = WIDTH_CHUNKS * CHUNK_SIZE;
                    int h = HEIGHT_CHUNKS * CHUNK_SIZE;
                    return (w, h);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DummyConnection] Failed reading map dimensions: {ex.Message}");
            }

            return (0, 0);
        }

        private async UniTaskVoid HandleRobotInfoMock(ushort botId)
        {
            await UniTask.Delay(2000);
            OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 999, 1, "Skin/bee.png", "Tail/default.png", "BeeBot")));
        }

        private async UniTaskVoid RunCircularBots(int count)
        {
            const int BASE_ID = 1000;

            var bots = new List<(ushort id, float cx, float cy, float r, float a, float speed)>();
            for (int i = 0; i < count; i++)
            {
                ushort botId = (ushort)(BASE_ID + i);
                OnReceived?.Invoke(new ServerPacket(new RobotInfoPacket(botId, 1000, 0,
                    "Skin/bee.png", "Tail/default.png", $"")));

                float radius = (float)((_rng.NextDouble() * 4.5) + 0.5);
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float speed = 0.3f + (float)((_rng.NextDouble() * 0.2) - 0.1);
                bots.Add((botId, 50f, 50f, radius, angle, speed));
            }

            while (_status == ConnectionStatus.Connected)
            {
                var positions = new List<IHBPacket>(bots.Count);
                for (int i = 0; i < bots.Count; i++)
                {
                    var b = bots[i];
                    int x = (int)Math.Round(b.cx + (Math.Cos(b.a) * b.r), MidpointRounding.AwayFromZero);
                    int y = (int)Math.Round(b.cy + (Math.Sin(b.a) * b.r), MidpointRounding.AwayFromZero);
                    double deg = ((Math.Atan2(Math.Sin(b.a), Math.Cos(b.a)) * (180.0 / Math.PI)) + 360) % 360;
                    byte rot = deg switch
                    {
                        > 225 and <= 315 => 0,
                        > 135 and <= 225 => 1,
                        > 45 and <= 135 => 2,
                        _ => 3,
                    };
                    positions.Add(new RobotPositionPacket(b.id, (ushort)x, (ushort)y, rot));
                    bots[i] = (b.id, b.cx, b.cy, b.r, b.a + (b.speed * 0.1f), b.speed);
                }

                OnReceived?.Invoke(new ServerPacket(new HBPacket(positions.ToArray())));
                await UniTask.Delay(20);
            }
        }

        private async UniTaskVoid HandleAssetRequest(RuntimeAssetRequestPacket runtimeAssets)
        {
            foreach (var assetEntry in runtimeAssets.Assets)
            {
                var tsm = Fodinae.Core.ServiceLocator.Resolve<ITextureStorageService>();
                var data = tsm != null ? await tsm.GetTextureData(assetEntry.Filename.TrimStart('/')) : null;

                RuntimeAssetPacket response;
                if (data != null)
                {
                    response = new RuntimeAssetPacket(assetEntry.Filename, Guid.NewGuid().ToString(), data);
                }
                else
                {
                    response = new RuntimeAssetPacket(assetEntry.Filename, string.Empty, System.Array.Empty<byte>());
                }

                OnReceived?.Invoke(new ServerPacket(response));
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

        private async UniTaskVoid SendChatMock()
        {
            var names = new[] { "Alice", "Bob", "Charlie", "Darkar25", "Eve" };
            var messages = new[]
            {
                "gg", "welcome!", "как дела?", "lol", "nice",
                "gl hf", "куда бежать?", "фармим)", "👋", "подскажите кто знает",
            };
            var rng = new System.Random();

            while (_status == ConnectionStatus.Connected)
            {
                await UniTask.Delay(8000 + rng.Next(4000));

                string name = names[rng.Next(names.Length)];
                string msg = messages[rng.Next(messages.Length)];
                System.Drawing.Color nickColor = System.Drawing.Color.FromArgb(
                    255, rng.Next(100, 256), rng.Next(100, 256), rng.Next(100, 256));

                var chatMsg = new ChatMessagePacket(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    rng.Next(100, 999), (byte)rng.Next(0, 3),
                    nickColor, name,
                    System.Drawing.Color.White, msg);
                OnReceived?.Invoke(new ServerPacket(new ChatMessageListPacket("global", new[] { chatMsg })));
            }
        }

        private async UniTaskVoid SendPingMock()
        {
            await UniTask.Delay(2000);
            while (_status == ConnectionStatus.Connected)
            {
                OnReceived?.Invoke(new ServerPacket(new PingPacket(DateTimeOffset.UtcNow.Ticks, _rng.Next(15, 60))));
                await UniTask.Delay(5000);
            }
        }

        private static int GetCrystalBasketIndex(CellType cell)
        {
            return cell switch
            {
                CellType.Green => 0,
                CellType.Blue => 1,
                CellType.Red => 2,
                CellType.Violet => 3,
                CellType.White => 4,
                CellType.Cyan => 5,
                _ => -1,
            };
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

        private void TryBuild(ushort x, ushort y, CellType placeType)
        {
            if (_worldLayer == null)
            {
                return;
            }

            CellType current = GetServerCell(x, y);
            if (current != CellType.Empty && current != CellType.Road)
            {
                return;
            }

            SetServerCell(x, y, placeType);
            OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { placeType }) })));
        }

        private void TryUpgradeBuild(ushort x, ushort y, params (CellType From, CellType To)[] upgrades)
        {
            if (_worldLayer == null)
            {
                return;
            }

            CellType current = GetServerCell(x, y);

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (current == upgrades[i].From || (current == CellType.Road && i == 0 && upgrades[i].From == CellType.Empty))
                {
                    SetServerCell(x, y, upgrades[i].To);
                    OnReceived?.Invoke(new ServerPacket(new HBPacket(new IHBPacket[] { new MapRegionPacket(x, y, 0, 0, new[] { upgrades[i].To }) })));
                    return;
                }
            }
        }

        private List<(ushort X, ushort Y)> FindPath(ushort startX, ushort startY, ushort targetX, ushort targetY)
        {
            if (_worldLayer == null)
            {
                return new List<(ushort, ushort)>();
            }

            var dirs = new (int dx, int dy)[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
            var visited = new HashSet<(ushort, ushort)>();
            var cameFrom = new Dictionary<(ushort, ushort), (ushort, ushort)>();
            var queue = new Queue<(ushort X, ushort Y)>();
            queue.Enqueue((startX, startY));
            visited.Add((startX, startY));
            int cellsChecked = 0;
            bool found = false;

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                cellsChecked++;
                if (cur.X == targetX && cur.Y == targetY)
                {
                    found = true;
                    break;
                }

                foreach (var (dx, dy) in dirs)
                {
                    int nx = cur.X + dx;
                    int ny = cur.Y + dy;
                    if (nx < 0 || ny < 0 || nx > ushort.MaxValue || ny > ushort.MaxValue)
                    {
                        continue;
                    }

                    var next = ((ushort)nx, (ushort)ny);
                    if (visited.Contains(next))
                    {
                        continue;
                    }

                    CellType cellType = GetServerCell((ushort)nx, (ushort)ny);
                    var cellConfig = ServiceLocator.Resolve<MapManager>()?.GetCellConfig(cellType);
                    bool isPassable = cellType == CellType.Empty || (cellConfig.HasValue && ((CellConfigProperties)cellConfig.Value.Properties).HasFlag(CellConfigProperties.Passable));
                    if (!isPassable)
                    {
                        continue;
                    }

                    visited.Add(next);
                    cameFrom[next] = cur;
                    queue.Enqueue(next);
                }
            }

            if (!found)
            {
                return new List<(ushort, ushort)>();
            }

            var path = new List<(ushort, ushort)>();
            var current = (targetX, targetY);
            while (current != (startX, startY))
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Reverse();
            return path;
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

                    SendMapChunksAround(_x, _y);
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
