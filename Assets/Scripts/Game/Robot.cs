#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Lighting;
using Fodinae.World.Terrain;
using TMPro;
using UnityEngine;
using VContainer;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using InvalidOperationException = System.InvalidOperationException;
using OperationCanceledException = System.OperationCanceledException;

namespace Fodinae.Game
{
    public class Robot : MonoBehaviour
    {
        private const string TAG = "[Robot]";
        private static int _nextDynamicLightId;

        [SerializeField]
        private uint _botId;
        [SerializeField]
        private int _playerId;
        [SerializeField]
        private byte _clanId;
        [SerializeField]
        private SpriteRenderer? _spriteRenderer;
        private SpriteRenderer? _clanRenderer;
        private TextMeshPro? _nicknameText;
        [SerializeField]
        private string _nickname = string.Empty;
        [SerializeField]
        private string _skinPath = string.Empty;
        [SerializeField]
        private string _tailPath = string.Empty;
        [SerializeField]
        private float _rotationSpeed = ProjectRuntimeContracts.RobotRotationSpeed;
        [Header("Dynamic Emission")]
        [SerializeField]
        [Tooltip("Разрешает Robot регистрировать dynamic emission source в TerrariaLightingEngine.")]
        private bool _emitsDynamicLight = true;
        [SerializeField]
        [Range(0f, 4f)]
        [Tooltip("Интенсивность dynamic emission. HDR-значение выше 1 усиливает источник.")]
        private float _dynamicLightIntensity;
        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        [Tooltip("HDR-цвет dynamic emission источника Robot.")]
        private Color _dynamicLightColor;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private const float MinimumSmoothTime = 0.05f;
        private const float MaximumSmoothTime = 0.15f;
        private const float DynamicLightPositionEpsilon = 0.00390625f;
        private bool _isMetadataLoaded = false;
        private CancellationTokenSource? _cts;
        private float _targetAngle = 0f;
        private float _smoothAngle = 0f;
        private Vector3 _targetPosition;
        private Vector3 _serverPosition;
        private Vector3 _smoothPosition;
        private Vector3 _currentVelocity;
        private float _currentAngularVelocity;
        [SerializeField]
        private float _moveSpeed = ProjectRuntimeContracts.RobotMoveSpeed;
        private float _tremor = 0f;

        [Inject]
        private IRobotService _robotService = null!;
        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        private Tentacle[]? _tentacles;
        private Sprite? _skinSprite;
        private Sprite? _clanSprite;
        private bool _dynamicLightEnabled;
        private int _dynamicLightId;
        private TerrariaLightingEngine? _lastDynamicLightEngine;
        private uint _lastDynamicLightGeneration;
        private Vector2 _lastDynamicLightPosition;
        private Color _lastDynamicLightColor;
        private float _lastDynamicLightIntensity;
        private bool _hasSubmittedDynamicLight;
        private bool _tentaclesSettled;
        private Vector3 _lastTentacleRootPosition;
        private float _lastTentacleRotation;
        private bool _hasUpdatedLabels;
        private Vector3 _lastLabelsPosition;
        private bool _dynamicLightSettingsLoaded;
        private bool _hasPendingServerPosition;
        private ushort _pendingServerX;
        private ushort _pendingServerY;
        [Inject]
        private TentacleBatchRenderer _tentacleBatchRenderer = null!;

        public uint BotId => _botId;
        public int PlayerId => _playerId;
        public byte ClanId => _clanId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;
        public bool IsLocalPlayer => gameObject.CompareTag("Player");

        public float DynamicLightIntensity => _dynamicLightIntensity;

        public Color DynamicLightColor => _dynamicLightColor;

        /// <summary>
        /// The logical facing angle in Unity degrees (raw <c>_targetAngle</c>),
        /// without visual smoothing or tremor. Use this when positioning effects
        /// that need to align with the bot's true facing direction at creation.
        /// </summary>
        public float LogicalFacingAngle => _targetAngle;

        public float TargetAngle
        {
            get => _targetAngle - VISUAL_ROTATION_OFFSET;
            set => _targetAngle = value + VISUAL_ROTATION_OFFSET;
        }

        public Vector3 TargetPosition
        {
            get => _targetPosition;
            set => _targetPosition = value;
        }

        public void SetClanBadge(ushort clanId)
        {
            _clanId = (byte)clanId;
            if (_clanId == 0)
            {
                ClearClanBadge();
                return;
            }

            if (_cts != null)
            {
                LoadClanAsync(_cts.Token).Forget();
            }
        }

        public void ClearClanBadge()
        {
            _clanId = 0;
            if (_clanRenderer != null)
            {
                _clanRenderer.sprite = null;
            }

            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
                _clanSprite = null;
            }
        }

        public float MoveSpeed
        {
            get => _moveSpeed;
            set => _moveSpeed = value;
        }

        protected void Awake()
        {
            _dynamicLightId = Interlocked.Increment(ref _nextDynamicLightId);

            // Dynamic emission is a property of this Robot source. Terrain
            // lighting owns the global lighting toggle.
            _dynamicLightEnabled = _emitsDynamicLight;

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (!IsLocalPlayer && !TryGetComponent<MotionBlurTag>(out _))
            {
                gameObject.AddComponent<MotionBlurTag>();
            }

            transform.localScale = Vector3.one;
            _targetPosition = transform.position;
            _serverPosition = transform.position;
            _smoothPosition = transform.position;
            _smoothAngle = transform.eulerAngles.z;

            if (TryGetComponent<Rigidbody2D>(out var rb))
            {
                rb.freezeRotation = true;
                rb.simulated = false;
            }

            InitializeVisualElements();
        }

        protected void OnEnable()
        {
            ApplyWorldUiLayer();
            _tentaclesSettled = false;
            SetTentaclesActive(true);
        }

        protected void OnDisable()
        {
            TerrariaLightingEngine.Instance?.RemoveDynamicLight(_dynamicLightId);
            _hasSubmittedDynamicLight = false;
            SetTentaclesActive(false);
        }

        private void InitializeVisualElements()
        {
            var textGo = new GameObject("Nickname");
            textGo.transform.SetParent(transform);
            _nicknameText = textGo.AddComponent<TextMeshPro>();
            _nicknameText.alignment = TextAlignmentOptions.TopLeft;
            _nicknameText.rectTransform.pivot = new Vector2(0f, 1f);
            _nicknameText.fontSize = 6.4f;
            _nicknameText.textWrappingMode = TextWrappingModes.NoWrap;
            _nicknameText.overflowMode = TextOverflowModes.Overflow;
            _nicknameText.color = Color.white;

            var textRenderer = textGo.GetComponent<MeshRenderer>();
            UnityRenderLayerContracts.ApplyWorldUI(textRenderer, 100);

            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            UnityRenderLayerContracts.ApplyWorldUI(_clanRenderer, 100);
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;
        }

        private void ApplyWorldUiLayer()
        {
            if (_nicknameText == null)
            {
                _nicknameText = transform.Find("Nickname")?.GetComponent<TextMeshPro>();
            }

            if (_clanRenderer == null)
            {
                _clanRenderer = transform.Find("ClanIcon")?.GetComponent<SpriteRenderer>();
            }

            if (_nicknameText != null)
            {
                MeshRenderer? nicknameRenderer = _nicknameText.GetComponent<MeshRenderer>();
                if (nicknameRenderer != null)
                {
                    UnityRenderLayerContracts.ApplyWorldUI(nicknameRenderer, 100);
                }
            }

            if (_clanRenderer != null)
            {
                UnityRenderLayerContracts.ApplyWorldUI(_clanRenderer, 100);
            }
        }

        protected void Start()
        {
            TryInitializeDynamicLightSettings();

            Vector3 snappedPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z);
            transform.position = snappedPos;
            _targetPosition = snappedPos;
            _serverPosition = snappedPos;
            _smoothPosition = snappedPos;
            _smoothAngle = transform.eulerAngles.z;

            // This is an intentional offline/DummyConnection identity skin for
            // the local player, not an implicit rendering fallback. Keep it in
            // the asset contract; it must never be removed during default cleanup.
            if (string.IsNullOrEmpty(_skinPath) && IsLocalPlayer)
            {
                _skinPath = "Skin/bee.png";
                _tailPath = "Tail/default.png";
            }

            if (!string.IsNullOrEmpty(_skinPath))
            {
                LoadMetadataAssets();
            }

            _targetAngle = transform.eulerAngles.z;

            if (gameObject.CompareTag("Player"))
            {
                _robotService?.RegisterRobot(this);
            }
        }

        protected void Update()
        {
            TryInitializeDynamicLightSettings();
            ApplyPendingServerPosition();

            if (_tentacles == null)
            {
                _tentaclesSettled = true;
            }

            bool bodySettled =
                (_smoothPosition - _targetPosition).sqrMagnitude <= 1e-8f &&
                _currentVelocity.sqrMagnitude <= 1e-8f &&
                Mathf.Abs(Mathf.DeltaAngle(_smoothAngle, _targetAngle)) <= 0.001f &&
                Mathf.Abs(_currentAngularVelocity) <= 0.001f &&
                _tremor <= 0.01f &&
                _tentaclesSettled;
            if (bodySettled)
            {
                // Tentacles contain a time-based idle animation even when the
                // robot itself is stationary. Keep their render mesh current
                // while the body transform can take the cheap early-out path.
                UpdateTentacles(transform.position, transform.eulerAngles.z, 0f, Time.deltaTime);
                UpdateLabelsPosition();
                UpdateDynamicLight();
                return;
            }

            float renderDistance = (_smoothPosition - _targetPosition).magnitude;

            // 1. Base smooth time now scales PROPORTIONALLY with speed.
            // Slower = snappier/tighter (low smooth time). Faster = momentum/curves (higher smooth time).
            float speedRatio = Mathf.Clamp01(
                _moveSpeed / GameConstants.Movement.ReferenceMoveSpeed);
            float targetSmoothTime = Mathf.Lerp(MinimumSmoothTime, MaximumSmoothTime, speedRatio);

            // 2. Distance factor: get extra snappy when very close to the target (e.g. moving exactly 1 cell and stopping)
            float distanceRatio = Mathf.Clamp01(renderDistance / 2f);
            float smoothTime = Mathf.Lerp(MinimumSmoothTime, targetSmoothTime, distanceRatio);

            if (renderDistance > 28f)
            {
                _smoothPosition = _targetPosition;
                _smoothAngle = _targetAngle;
                _currentVelocity = Vector3.zero;
                _currentAngularVelocity = 0f;

                if (_tentacles != null)
                {
                    foreach (var tentacle in _tentacles)
                    {
                        tentacle.Snap(_smoothPosition);
                    }
                }
            }
            else
            {
                // 3. Max Visual Speed limits the catch-up rate.
                // Setting it to 1.25x of logical speed allows it to easily catch up without wildly slingshotting,
                // bridging the gaps between server "ticks" smoothly when running continuously.
                float maxVisualSpeed = Mathf.Max(_moveSpeed * 1.25f, 5f);
                _smoothPosition = Vector3.SmoothDamp(_smoothPosition, _targetPosition, ref _currentVelocity, smoothTime, maxVisualSpeed, Time.deltaTime);
            }

            // Apply tremor logic
            Vector3 finalPosition = _smoothPosition;
            if (_tremor > 0.01f)
            {
                _tremor *= Mathf.Pow(0.8f, Time.deltaTime / 0.016f);
                finalPosition.x += _tremor * (Random.value - 0.5f);
                finalPosition.y += _tremor * (Random.value - 0.5f);
            }

            transform.position = finalPosition;

            // Apply rotation smoothing (now limits turning rate using your previously unused _rotationSpeed field)
            float targetAngle = _targetAngle;
            _smoothAngle = Mathf.SmoothDampAngle(_smoothAngle, targetAngle, ref _currentAngularVelocity, smoothTime, _rotationSpeed, Time.deltaTime);

            float nowRotationAngle = _smoothAngle;

            transform.rotation = Quaternion.Euler(0, 0, nowRotationAngle);

            float movementFactor = Mathf.Clamp01(_currentVelocity.magnitude / 5f);
            bool tentacleStateChanged =
                !_tentaclesSettled ||
                (finalPosition - _lastTentacleRootPosition).sqrMagnitude > 1e-8f ||
                Mathf.Abs(Mathf.DeltaAngle(_lastTentacleRotation, nowRotationAngle)) > 0.001f ||
                movementFactor > 0.0001f;
            if (tentacleStateChanged)
            {
                UpdateTentacles(finalPosition, nowRotationAngle, movementFactor, Time.deltaTime);
                _tentaclesSettled = AreTentaclesSettled();
                _lastTentacleRootPosition = finalPosition;
                _lastTentacleRotation = nowRotationAngle;
            }

            UpdateLabelsPosition();
            UpdateDynamicLight();
        }

        private void UpdateDynamicLight()
        {
            TerrariaLightingEngine? lighting = TerrariaLightingEngine.Instance;
            if (!_dynamicLightEnabled || lighting == null || !lighting.IsRuntimeConfigReady)
            {
                if (_hasSubmittedDynamicLight)
                {
                    lighting?.RemoveDynamicLight(_dynamicLightId);
                }

                _hasSubmittedDynamicLight = false;
                return;
            }

            if (!_dynamicLightSettingsLoaded)
            {
                _dynamicLightIntensity = lighting.DynamicLightIntensity;
                _dynamicLightColor = lighting.DynamicLightColor;
                _dynamicLightSettingsLoaded = true;
            }

            Vector2 position = new(_smoothPosition.x, _smoothPosition.y);
            uint generation = lighting.DynamicLightGeneration;
            if (_hasSubmittedDynamicLight &&
                ReferenceEquals(_lastDynamicLightEngine, lighting) &&
                _lastDynamicLightGeneration == generation &&
                (_lastDynamicLightPosition - position).sqrMagnitude <=
                    DynamicLightPositionEpsilon * DynamicLightPositionEpsilon &&
                _lastDynamicLightColor == _dynamicLightColor &&
                Mathf.Approximately(_lastDynamicLightIntensity, _dynamicLightIntensity))
            {
                return;
            }

            // Lighting follows the interpolated render position. Using
            // _targetPosition here made the sprite move smoothly while
            // its emission snapped between server cells.
            lighting.SetDynamicLight(
                _dynamicLightId,
                position,
                _dynamicLightColor,
                _dynamicLightIntensity);
            _lastDynamicLightEngine = lighting;
            _lastDynamicLightGeneration = generation;
            _lastDynamicLightPosition = position;
            _lastDynamicLightColor = _dynamicLightColor;
            _lastDynamicLightIntensity = _dynamicLightIntensity;
            _hasSubmittedDynamicLight = true;
        }

        public void SetDynamicLightIntensity(float intensity)
        {
            _dynamicLightIntensity = Mathf.Clamp(intensity, 0f, 4f);
            TerrariaLightingEngine.Instance?.SetDynamicLightSettings(
                _dynamicLightIntensity,
                _dynamicLightColor);
        }

        public void SetDynamicLightColor(Color color)
        {
            _dynamicLightColor = new Color(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                1f);
            TerrariaLightingEngine.Instance?.SetDynamicLightSettings(
                _dynamicLightIntensity,
                _dynamicLightColor);
        }

        public void ResetDynamicLightPreferences()
        {
            if (!IsLocalPlayer)
            {
                return;
            }

            TerrariaLightingEngine? lighting = TerrariaLightingEngine.Instance;
            _dynamicLightIntensity = lighting?.DynamicLightIntensity ??
                _projectDefaults.Lighting.DynamicLightIntensity;
            _dynamicLightColor = lighting?.DynamicLightColor ??
                _projectDefaults.Lighting.DynamicLightColor;
            _dynamicLightSettingsLoaded = true;
        }

        private void InitializeDynamicLightSettings()
        {
            LightingDefaultsSnapshot defaults = _projectDefaults.Lighting;
            TerrariaLightingEngine? lighting = TerrariaLightingEngine.Instance;
            _dynamicLightIntensity = lighting?.IsRuntimeConfigReady == true
                ? lighting.DynamicLightIntensity
                : defaults.DynamicLightIntensity;
            _dynamicLightColor = lighting?.IsRuntimeConfigReady == true
                ? lighting.DynamicLightColor
                : defaults.DynamicLightColor;
            _dynamicLightSettingsLoaded = lighting?.IsRuntimeConfigReady == true;
        }

        private void TryInitializeDynamicLightSettings()
        {
            if (_dynamicLightSettingsLoaded)
            {
                return;
            }

            if (_projectDefaults == null)
            {
                return;
            }

            InitializeDynamicLightSettings();
        }

        private void CreateTentacles(Texture2D tailTexture)
        {
            ClearTentacles();
            _tentacles = new Tentacle[4];
            _tentaclesSettled = false;
            float[] offsets = { -45f, -15f, 15f, 45f };
            for (int i = 0; i < 4; i++)
            {
                _tentacles[i] = new Tentacle(
                    _tentacleBatchRenderer,
                    tailTexture,
                    transform.position,
                    offsets[i],
                    i,
                    4);
            }
        }

        private void ClearTentacles()
        {
            if (_tentacles != null)
            {
                foreach (var tentacle in _tentacles)
                {
                    tentacle?.Destroy();
                }

                _tentacles = null;
            }
        }

        private void SetTentaclesActive(bool active)
        {
            if (_tentacles == null)
            {
                return;
            }

            foreach (Tentacle tentacle in _tentacles)
            {
                tentacle.SetActive(active);
            }
        }

        private bool AreTentaclesSettled()
        {
            if (_tentacles == null)
            {
                return true;
            }

            foreach (Tentacle tentacle in _tentacles)
            {
                if (!tentacle.IsSettled)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateTentacles(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
        {
            if (_tentacles == null)
            {
                return;
            }

            foreach (var tentacle in _tentacles)
            {
                tentacle.Update(rootPosition, rotationAngle, movementFactor, deltaTime);
            }
        }

        private void UpdateLabelsPosition()
        {
            Vector3 position = transform.position;
            if (_hasUpdatedLabels &&
                (position - _lastLabelsPosition).sqrMagnitude <= 1e-8f)
            {
                return;
            }

            if (_nicknameText != null)
            {
                SpriteRenderer spriteRenderer = _spriteRenderer ??
                    throw new InvalidOperationException(
                        $"{TAG} SpriteRenderer is required to anchor nickname for bot {_botId}.");
                Bounds spriteBounds = spriteRenderer.bounds;
                Vector3 topRight = new(spriteBounds.max.x, spriteBounds.max.y, position.z);

                // World-space labels use the actual rendered sprite bounds, not a
                // fixed offset from the robot pivot. TopLeft alignment makes the
                // nickname grow rightward from the robot's top-right corner.
                _nicknameText.transform.SetPositionAndRotation(
                    topRight,
                    Quaternion.identity);
                _nicknameText.transform.localScale = new Vector3(1f, -1f, 1f);
            }

            if (_clanRenderer != null)
            {
                _clanRenderer.transform.SetPositionAndRotation(position + new Vector3(0.6f, -0.5f, 0), Quaternion.identity);
            }

            _lastLabelsPosition = position;
            _hasUpdatedLabels = true;
        }

        public void Initialize(uint botId)
        {
            TryInitializeDynamicLightSettings();
            _botId = botId;
            if (ServiceLocator.IsInitialized)
            {
                ServiceLocator.Resolve<RobotManager>()?.RegisterRobot(this);
            }

            _isMetadataLoaded = false;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = new Color(1, 1, 1, 0.5f);
            }

            if (_nicknameText != null)
            {
                _nicknameText.text = string.Empty;
            }

            if (_clanRenderer != null)
            {
                _clanRenderer.sprite = null;
            }
        }

        public void SetMetadata(int playerId, byte clanid, string nickname, string skinPath, string tailPath)
        {
            if (_isMetadataLoaded &&
                _playerId == playerId &&
                _clanId == clanid &&
                string.Equals(_nickname, nickname, global::System.StringComparison.Ordinal) &&
                string.Equals(_skinPath, skinPath, global::System.StringComparison.Ordinal) &&
                string.Equals(_tailPath, tailPath, global::System.StringComparison.Ordinal))
            {
                return;
            }

            _playerId = playerId;
            _clanId = clanid;
            _nickname = nickname;
            _skinPath = skinPath;
            _tailPath = tailPath;
            _isMetadataLoaded = true;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            if (_nicknameText != null)
            {
                _nicknameText.text = nickname;
            }

            LoadMetadataAssets();
        }

        public void SetPosition(ushort x, ushort y)
        {
            if (!ServiceLocator.IsInitialized)
            {
                _pendingServerX = x;
                _pendingServerY = y;
                _hasPendingServerPosition = true;
                return;
            }

            ApplyServerPosition(x, y);
        }

        private void ApplyPendingServerPosition()
        {
            if (!_hasPendingServerPosition || !ServiceLocator.IsInitialized)
            {
                return;
            }

            ApplyServerPosition(_pendingServerX, _pendingServerY);
            _hasPendingServerPosition = false;
        }

        private void ApplyServerPosition(ushort x, ushort y)
        {
            MapManager mm = ServiceLocator.Resolve<MapManager>() ??
                throw new InvalidOperationException(
                    $"{TAG} MapManager is required to apply position for bot {_botId}.");
            _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, mm.WorldHeight);

            // Only update target position from server for remote robots.
            // Local player manages its own target position via PlayerMovementController.
            // If the local player is too far from server position, we should snap.
            if (!IsLocalPlayer || Vector3.Distance(_targetPosition, _serverPosition) > 2.0f)
            {
                _targetPosition = _serverPosition;
            }
        }

        public void SetRotation(byte rotation)
        {
            TargetAngle = rotation switch
            {
                0 => 270f,
                1 => 180f,
                2 => 90f,
                3 => 0f,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(rotation),
                    rotation,
                    $"[{TAG}] Unsupported robot rotation value for bot {_botId}."),
            };
        }

        private void LoadMetadataAssets()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            if (!ServiceLocator.IsInitialized)
            {
                WaitForServicesAndLoadMetadataAsync(_cts.Token).Forget();
                return;
            }

            LoadMetadataAssetsAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid WaitForServicesAndLoadMetadataAsync(CancellationToken token)
        {
            try
            {
                await UniTask.WaitUntil(
                    () => ServiceLocator.IsInitialized,
                    cancellationToken: token);
                if (!token.IsCancellationRequested)
                {
                    LoadMetadataAssetsAsync(token).Forget();
                }
            }
            catch (OperationCanceledException)
            {
                // Object teardown or domain reload cancelled the deferred load.
            }
        }

        private async UniTaskVoid LoadMetadataAssetsAsync(CancellationToken token)
        {
            LoadSkinAsync(token).Forget();
            LoadTailAsync(token).Forget();
            if (!IsLocalPlayer)
            {
                LoadClanAsync(token).Forget();
            }

            await UniTask.CompletedTask;
        }

        private async UniTaskVoid LoadSkinAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_skinPath) || !ServiceLocator.IsInitialized)
            {
                return;
            }

            IAssetLoader loader = ServiceLocator.Resolve<IAssetLoader>() ??
                throw new InvalidOperationException(
                    $"{TAG} Asset loader is required for skin load on bot {_botId}.");
            Texture2D? skinTexture = await TryLoadOptionalTextureAsync(
                loader,
                _skinPath,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            SpriteRenderer spriteRenderer = _spriteRenderer ?? throw new InvalidOperationException(
                $"{TAG} SpriteRenderer is missing for bot {_botId} skin load.");
            if (skinTexture == null)
            {
                return;
            }

            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            _skinSprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
            spriteRenderer.sprite = _skinSprite;
        }

        private async UniTaskVoid LoadTailAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_tailPath) || !ServiceLocator.IsInitialized)
            {
                ClearTentacles();
                return;
            }

            IAssetLoader loader = ServiceLocator.Resolve<IAssetLoader>() ??
                throw new InvalidOperationException(
                    $"{TAG} Asset loader is required for tail load on bot {_botId}.");
            Texture2D? tailTexture = await TryLoadOptionalTextureAsync(
                loader,
                _tailPath,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (tailTexture == null)
            {
                ClearTentacles();
                return;
            }

            CreateTentacles(tailTexture);
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_clanId == 0 || !ServiceLocator.IsInitialized)
            {
                return;
            }

            IAssetLoader loader = ServiceLocator.Resolve<IAssetLoader>() ??
                throw new InvalidOperationException(
                    $"{TAG} Asset loader is required for clan load on bot {_botId}.");
            string clanPath = $"/Clan/{_clanId}";
            Texture2D? clanTexture = await TryLoadOptionalTextureAsync(
                loader,
                clanPath,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            SpriteRenderer clanRenderer = _clanRenderer ?? throw new InvalidOperationException(
                $"{TAG} Clan SpriteRenderer is missing for bot {_botId}.");
            if (clanTexture == null)
            {
                return;
            }


            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            clanRenderer.sprite = _clanSprite;
        }

        private static async UniTask<Texture2D?> TryLoadOptionalTextureAsync(
            IAssetLoader loader,
            string filename,
            CancellationToken cancellationToken)
        {
            try
            {
                return await loader.GetTextureAsync(filename, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"{TAG} Optional texture '{filename}' was skipped: {exception.Message}");
                return null;
            }
        }

#if UNITY_EDITOR
        protected void OnDrawGizmos()
        {
            if (!Application.isPlaying || !RobotManager.ShowDebugVisuals)
            {
                return;
            }

            // Server Position: Red Square
            Fodinae.World.FodinaeGizmos.DrawBounds(_serverPosition, Vector2.one * 1.0f, Color.red);

            // Client/Target Position: Blue Square
            Fodinae.World.FodinaeGizmos.DrawBounds(_targetPosition, Vector2.one * 0.9f, Color.blue);

            // Visual Position: Cyan Square
            Fodinae.World.FodinaeGizmos.DrawBounds(transform.position, Vector2.one * 0.8f, Color.cyan);

            // Draw Rotation Arrow
            float angleRad = (transform.eulerAngles.z + VISUAL_ROTATION_OFFSET) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0);
            Fodinae.World.FodinaeGizmos.DrawArrow(transform.position, direction, Color.yellow, 1.2f);

            // Metadata Status
            string status = $"ID: {_botId}\n{(IsLocalPlayer ? "LOCAL PLAYER" : "REMOTE ROBOT")}\n" +
                            $"Meta: {(_isMetadataLoaded ? "OK" : "PENDING")}\n" +
                            $"Speed: {_moveSpeed:F1}";
            Fodinae.World.FodinaeGizmos.DrawLabel(transform.position + (Vector3.up * 1.5f), status, _isMetadataLoaded ? Color.green : Color.orange);

            if (!IsLocalPlayer)
            {
                // Draw line to server position if it's lagging
                float lag = Vector3.Distance(_serverPosition, transform.position);
                if (lag > 0.5f)
                {
                    Fodinae.World.FodinaeGizmos.DrawDottedLine(transform.position, _serverPosition, Color.red, 4f);
                }
            }
        }
#endif

        protected void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _robotService?.UnregisterRobot(_botId, this);

            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            ClearTentacles();
        }
    }
}
