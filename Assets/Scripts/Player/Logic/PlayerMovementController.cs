#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game;
using Fodinae.Game.Managers;
using Fodinae.Networking;
using Fodinae.Networking.Connection;
using Fodinae.Player.Input;
using Fodinae.Player.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.Actions;
using MinesServer.Networking.Client.Packets.Movement;
using MinesServer.Networking.Connection.Client;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace Fodinae.Player.Logic
{
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField]
        private float _moveSpeed = 15f;

        public uint BotId { get; private set; }
        public Vector2Int Position { get; private set; }
        public Direction LastDirection => _lastSentDirection ?? Direction.Up;
        public event Action<Vector2Int, Vector2Int>? OnPlayerMoved;

        private Robot? _robot;
        private IPlayerInput? _input;

        private bool _autoDig = false;
        private bool _aggression = false;
        private bool _ignoreCollision = false;
        private float _lastMoveTime;
        private float _lastDigTime;
        private Direction? _lastSentDirection;
        [Inject]
        private IWorldDataStorage? _storage;

        [Inject]
        private IServerConfig? _serverConfig;

        [Inject]
        private INetworkService? _networkService;

        [Inject]
        private IMapDataProvider? _mapDataProvider;

        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker? _inputBlocker;

        public static PlayerMovementController? LocalPlayer { get; private set; }
        public static event Action<PlayerMovementController>? OnLocalPlayerSpawned;

        protected void Awake()
        {
            LocalPlayer = this;
            OnLocalPlayerSpawned?.Invoke(this);

            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }

            _robot = GetComponent<Robot>();
            if (_robot is not null)
            {
                _robot.MoveSpeed = _moveSpeed;
            }

            _input = GetComponent<IPlayerInput>() ?? gameObject.AddComponent<PlayerInputHandler>();

            if (!TryGetComponent<RobotHeadlight>(out var headlight))
            {
                headlight = gameObject.AddComponent<RobotHeadlight>();
            }

            bool useLight2D = PlayerPrefs.GetInt("UseLight2D", 0) == 1;
            headlight.SetEnabled(useLight2D);
        }

        protected void OnDestroy()
        {
            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }
        }

        protected void Start()
        {
            Vector3 targetGridPos = new(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z);
            transform.position = targetGridPos;
            if (_robot is not null)
            {
                _robot.TargetPosition = targetGridPos;
            }

            if (_mapDataProvider != null)
            {
                Position = CoordinateUtils.UnityToServerPos(transform.position, _mapDataProvider.WorldHeight);
            }

            _lastSentDirection = null;
        }

        protected void Update()
        {
            if (_input == null || (_inputBlocker != null && _inputBlocker.IsInputBlocked))
            {
                return;
            }

            ApplyMovement();
            HandleDigInput();

            if (_input.WantsToToggleAutoDig)
            {
                _networkService?.SendAction(new ToggleAutoDigPacket());
            }

            if (_input.WantsToToggleAggression)
            {
                ToggleAggression();
            }

            if (_input.WantsToGeo)
            {
                _networkService?.SendAction(new GeoPacket());
            }

            if (_input.WantsToHeal)
            {
                _networkService?.SendAction(new HealPacket());
            }

            if (_input.WantsToBuildCyan)
            {
                _networkService?.SendAction(new BuildCyanPacket());
            }

            if (_input.WantsToBuildGray)
            {
                _networkService?.SendAction(new BuildGrayPacket());
            }

            if (_input.WantsToBuildGreen)
            {
                _networkService?.SendAction(new BuildGreenPacket());
            }

            if (_input.WantsToBuildWhite)
            {
                _networkService?.SendAction(new BuildWhitePacket());
            }
        }

        public void Initialize(uint botId)
        {
            BotId = botId;
            if (_robot != null)
            {
                _robot.Initialize(botId);
            }
        }

        public bool AutoDig
        {
            get => _autoDig;
            set
            {
                _autoDig = value;
                OnAutoDigChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnAutoDigChanged;

        public bool Aggression
        {
            get => _aggression;
            set
            {
                _aggression = value;
                OnAggressionChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnAggressionChanged;

        public void ToggleAggression()
        {
            _aggression = !_aggression;
            _networkService?.SendAction(new ToggleAgressionPacket());
            OnAggressionChanged?.Invoke(_aggression);
        }

        public bool IsMoving => _input != null && _input.MoveInput != Vector2.zero;

        public bool IgnoreCollision
        {
            get => _ignoreCollision;
            set
            {
                _ignoreCollision = value;
                DummyConnection.IgnoreCollision = value;
                OnCollisionChanged?.Invoke(value);
            }
        }

        public event Action<bool>? OnCollisionChanged;

        public void UpdateServerPosition(Vector2Int position)
        {
            Vector2Int oldPos = Position;
            Position = position;

            int worldHeight = _mapDataProvider != null ? _mapDataProvider.WorldHeight : 128;
            Vector3 targetWorldPos = CoordinateUtils.ServerToUnityPos(position.x, position.y, worldHeight, transform.position.z);
            transform.position = targetWorldPos;
            if (_robot is not null)
            {
                _robot.TargetPosition = targetWorldPos;
            }

            OnPlayerMoved?.Invoke(oldPos, Position);
        }

        private void ApplyMovement()
        {
            // _robot может быть null если PlayerMovementController создан VContainer'ом
            // отдельно от префаба Player. Пробуем получить его лениво.
            if (_robot is null)
            {
                _robot = GetComponent<Robot>();
                if (_robot is not null)
                {
                    _robot.MoveSpeed = _moveSpeed;
                }
            }

            _serverConfig ??= Fodinae.Core.ServiceLocator.Resolve<IServerConfig>();
            _mapDataProvider ??= Fodinae.Core.ServiceLocator.Resolve<IMapDataProvider>();
            _storage ??= Fodinae.Core.ServiceLocator.Resolve<IWorldDataStorage>();
            _inputBlocker ??= Fodinae.Core.ServiceLocator.Resolve<Fodinae.Core.Interfaces.IInputBlocker>();
            _networkService ??= Fodinae.Core.ServiceLocator.Resolve<INetworkService>();

            if (_robot is null || _input is null)
            {
                return;
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            Vector2 moveInput = _input.MoveInput;
            if (moveInput != Vector2.zero)
            {
                Vector2Int direction = Vector2Int.zero;
                if (Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y))
                {
                    direction.x = moveInput.x > 0 ? 1 : -1;
                }
                else
                {
                    direction.y = moveInput.y > 0 ? 1 : -1;
                }

                if (direction != Vector2Int.zero)
                {
                    Direction packetDirection = direction.x switch
                    {
                        1 => Direction.Right,
                        -1 => Direction.Left,
                        _ => direction.y > 0 ? Direction.Up : Direction.Down,
                    };

                    ushort currentX = (ushort)Mathf.Clamp(Position.x, 0, ushort.MaxValue);
                    ushort currentServerY = (ushort)Mathf.Clamp(Position.y, 0, ushort.MaxValue);

                    var storage = _storage;
                    if (storage == null)
                    {
                        return;
                    }

                    var currentCellType = storage.GetCell(currentX, currentServerY);
                    float cooldown = _mapDataProvider != null ? _mapDataProvider.GetMoveCooldown(currentCellType) : 0.2f;
                    if (_input.IsCtrlPressed && _mapDataProvider != null)
                    {
                        cooldown = _mapDataProvider.GetMoveCooldown(CellType.Empty);
                    }

                    if (cooldown > 0)
                    {
                        _robot.MoveSpeed = 1f / cooldown;
                    }

                    if (Time.time - _lastMoveTime < cooldown)
                    {
                        return;
                    }

                    if (_lastSentDirection != packetDirection)
                    {
                        _networkService?.SendAction(new RotatePacket(packetDirection));
                        _lastSentDirection = packetDirection;
                        _lastMoveTime = Time.time;
                    }

                    if (direction.x != 0)
                    {
                        _robot.TargetAngle = direction.x > 0 ? 0f : 180f;
                    }
                    else
                    {
                        _robot.TargetAngle = direction.y > 0 ? 90f : 270f;
                    }

                    if (_input.IsShiftPressed)
                    {
                        return;
                    }

                    int deltaServerX = direction.x;
                    int deltaServerY = direction.y > 0 ? -1 : (direction.y < 0 ? 1 : 0);

                    int targetServerXInt = Position.x + deltaServerX;
                    int targetServerYInt = Position.y + deltaServerY;

                    var layer = storage.CellLayer;
                    if (layer == null)
                    {
                        return;
                    }

                    if (_mapDataProvider == null)
                    {
                        return;
                    }

                    int mapWidth = _mapDataProvider.WorldWidth;
                    int mapHeight = _mapDataProvider.WorldHeight;

                    if (targetServerXInt < 0 || targetServerXInt >= mapWidth || targetServerYInt < 0 || targetServerYInt >= mapHeight)
                    {
                        return;
                    }

                    ushort targetServerX = (ushort)targetServerXInt;
                    ushort targetServerY = (ushort)targetServerYInt;

                    var cellType = storage.GetCell(targetServerX, targetServerY);
                    var cellConfig = _mapDataProvider.GetCellConfig(cellType);

                    bool isPassable = cellType == CellType.Empty || ((CellConfigProperties)cellConfig.Properties).HasFlag(CellConfigProperties.Passable);

                    if (isPassable || _ignoreCollision)
                    {
                        _robot.TargetPosition = CoordinateUtils.ServerToUnityPos(targetServerX, targetServerY, _mapDataProvider.WorldHeight, transform.position.z);
                        Vector2Int oldPos = Position;
                        Position = new Vector2Int(targetServerX, targetServerY);
                        OnPlayerMoved?.Invoke(oldPos, Position);
                        _lastMoveTime = Time.time;
                        _networkService?.SendAction(new MovePacket(targetServerX, targetServerY));
                    }
                    else if (_autoDig)
                    {
                        _networkService?.Send(new ActionClientPacket(targetServerX, targetServerY, new BzPacket()));
                        _lastMoveTime = Time.time;
                        _lastDigTime = Time.time;
                    }
                }
            }
        }

        public void ResetDirection()
        {
            _lastSentDirection = null;
        }

        public void SetMovementInput(Vector2 input)
        {
            if (_input != null)
            {
                _input.SetMovementInput(input);
            }
        }

        private void HandleDigInput()
        {
            if (_input == null)
            {
                return;
            }

            float digCooldown = _serverConfig != null ? _serverConfig.DigCooldown : 0.2f;
            if (!_input.WantsToDig || Time.time - _lastDigTime < digCooldown)
            {
                return;
            }

            Direction dir = _lastSentDirection ?? Direction.Up;
            Vector2Int digOffset = dir switch
            {
                Direction.Down => new Vector2Int(0, 1),
                Direction.Up => new Vector2Int(0, -1),
                Direction.Left => new Vector2Int(-1, 0),
                Direction.Right => new Vector2Int(1, 0),
                _ => Vector2Int.zero,
            };

            ushort serverX = (ushort)(Position.x + digOffset.x);
            ushort serverY = (ushort)(Position.y + digOffset.y);

            _networkService?.Send(new ActionClientPacket(serverX, serverY, new BzPacket()));
            _lastDigTime = Time.time;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            int worldHeight = _mapDataProvider != null ? _mapDataProvider.WorldHeight : 0;
            Vector3 gridPos = CoordinateUtils.ServerToUnityPos(Position.x, Position.y, worldHeight, transform.position.z);
            Gizmos.DrawWireCube(gridPos, new Vector3(1f, 1f, 0.1f));

            if (Application.isPlaying && _robot != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, _robot.TargetPosition);
                Gizmos.DrawWireSphere(_robot.TargetPosition, 0.2f);
                FodinaeGizmos.DrawLabel(gridPos + (Vector3.down * 0.7f), $"Grid: {Position.x}, {Position.y}", Color.cyan);
            }
        }
#endif
    }
}
