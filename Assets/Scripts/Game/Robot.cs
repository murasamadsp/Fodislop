#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Game.Managers;
using Fodinae.Player.Logic;
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
        private Transform? _clanTransform;
        private TextMeshPro? _nicknameText;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [SerializeField]
        private string _nickname = string.Empty;
        [SerializeField]
        private string _skinPath = string.Empty;
        [SerializeField]
        private string _tailPath = string.Empty;
        [SerializeField]
        private float _rotationSpeed = ProjectRuntimeContracts.Movement.RobotRotationSpeed;
        [Header("Dynamic Emission")]
        [SerializeField]
        [Tooltip("Разрешает Robot регистрировать dynamic emission source в LightingEngine.")]
        private bool _emitsDynamicLight;
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
        private float _moveSpeed = ProjectRuntimeContracts.Movement.RobotMoveSpeed;
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
        private LightingEngine? _lastDynamicLightEngine;
        private WorldEntityBatchRenderer.SpriteHandle? _bodyBatchHandle;
        private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;
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
        private bool _hasReceivedInitialPosition;
        private bool _isCulled;
        private const float OffscreenCullDistance = 35f;
        private const float OffscreenCullSqrDistance = OffscreenCullDistance * OffscreenCullDistance;
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;
        [Inject]
        private IObjectResolver _resolver = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;

        public uint BotId => _botId;
        public int PlayerId => _playerId;
        public byte ClanId => _clanId;
        public string Nickname => _nickname;
        public bool IsMetadataLoaded => _isMetadataLoaded;
        public bool IsVisualsLoaded => _isMetadataLoaded && (_skinSprite != null || string.IsNullOrEmpty(_skinPath));
        public bool IsLocalPlayer => gameObject.CompareTag("Player");

        public float DynamicLightIntensity => _dynamicLightIntensity;

        public Color DynamicLightColor => _dynamicLightColor;

        [Inject]
        private void InitializeEntityBatch(WorldEntityBatchRenderer entityBatchRenderer)
        {
            _entityBatchRenderer = entityBatchRenderer;
            EnsureBatchHandles();
        }

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
            if (_clanBatchHandle != null)
            {
                _entityBatchRenderer.SetSprite(_clanBatchHandle, null);
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

            if (_spriteRenderer != null && Application.isPlaying)
            {
                _spriteRenderer.enabled = false;
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
            ApplyWorldUILayer();
            _tentaclesSettled = false;
            if (!Application.isPlaying || (IsLocalPlayer ? PlayerMovementController.LocalPlayer is { HasServerPosition: true } : _isMetadataLoaded && _hasReceivedInitialPosition))
            {
                SetTentaclesActive(true);
            }
            else
            {
                SetTentaclesActive(false);
            }
        }

        protected void OnDisable()
        {
            _lightingEngine?.RemoveDynamicLight(_dynamicLightId);
            _hasSubmittedDynamicLight = false;
            SetTentaclesActive(false);
        }

        private void InitializeVisualElements()
        {
            Transform? existingNickname = transform.Find("Nickname");
            if (IsLocalPlayer)
            {
                if (existingNickname != null)
                {
                    existingNickname.gameObject.SetActive(false);
                }
            }
            else
            {
                GameObject textGo;
                if (existingNickname != null)
                {
                    textGo = existingNickname.gameObject;
                }
                else if (_sceneObjects != null)
                {
                    textGo = _sceneObjects.Create("Nickname", RuntimeOwner.FloatingUI);
                }
                else
                {
                    textGo = new GameObject("Nickname");
                }

                // Nicknames are world-space UI. They follow the robot position,
                // but must not inherit the robot's facing rotation, sprite flip,
                // or non-uniform scale.
                Transform? floatingOwner = _sceneObjects?.GetOwner(RuntimeOwner.FloatingUI);
                if (floatingOwner != null)
                {
                    textGo.transform.SetParent(floatingOwner, worldPositionStays: true);
                }

                _nicknameText = textGo.GetComponent<TextMeshPro>() ??
                    textGo.AddComponent<TextMeshPro>();
                textGo.SetActive(true);
                _nicknameText.alignment = TextAlignmentOptions.TopLeft;
                _nicknameText.rectTransform.pivot = new Vector2(0f, 1f);
                _nicknameText.fontSize = 6.4f;
                _nicknameText.textWrappingMode = TextWrappingModes.NoWrap;
                _nicknameText.overflowMode = TextOverflowModes.Overflow;
                _nicknameText.color = Color.white;

                if (_nicknameText.font == null)
                {
                    var font = Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono_SDF") ??
                               Resources.Load<TMP_FontAsset>("Fonts/Exo2_SDF") ??
                               TMP_Settings.defaultFontAsset;
                    if (font != null)
                    {
                        _nicknameText.font = font;
                    }
                }

                _nicknameText.text = !string.IsNullOrEmpty(_nickname) && !IsLocalPlayer
                    ? _nickname
                    : string.Empty;

                MeshRenderer textRenderer = _nicknameText.GetComponent<MeshRenderer>() ??
                    throw new InvalidOperationException(
                        $"{TAG} Nickname MeshRenderer is missing for bot {_botId}.");
                UnityRenderLayerContracts.ApplyWorldUI(textRenderer, 100);
            }

            if (_clanTransform == null)
            {
                Transform? existingClan = transform.Find("ClanIcon");
                GameObject clanGo = existingClan != null
                    ? existingClan.gameObject
                    : (_sceneObjects != null
                        ? _sceneObjects.Create("ClanIcon", RuntimeOwner.Robots)
                        : new GameObject("ClanIcon"));
                clanGo.transform.SetParent(transform, worldPositionStays: false);
                _clanTransform = clanGo.transform;
                _clanTransform.localScale = Vector3.one * 0.8f;
            }
        }

        private void EnsureBatchHandles()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_entityBatchRenderer == null)
            {
                return;
            }

            _bodyBatchHandle ??= _entityBatchRenderer.RegisterSprite(transform, 0);
            if (_clanTransform != null)
            {
                _clanBatchHandle ??=
                    _entityBatchRenderer.RegisterSprite(_clanTransform, 100);
            }
        }

        private static Bounds TransformSpriteBounds(Transform spriteTransform, Sprite sprite)
        {
            Bounds local = sprite.bounds;
            Vector3 minimum = spriteTransform.TransformPoint(local.min);
            Vector3 maximum = spriteTransform.TransformPoint(local.max);
            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        public void SetBatchedBodyVisible(bool visible)
        {
            EnsureBatchHandles();
            _bodyBatchHandle?.SetEnabled(visible);
        }

        private void ApplyWorldUILayer()
        {
            if (_clanTransform == null)
            {
                _clanTransform = transform.Find("ClanIcon");
            }

            if (_nicknameText != null)
            {
                MeshRenderer? nicknameRenderer = _nicknameText.GetComponent<MeshRenderer>();
                if (nicknameRenderer != null)
                {
                    UnityRenderLayerContracts.ApplyWorldUI(nicknameRenderer, 100);
                }
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

            // In editor preview (outside Play Mode), populate fallback visuals.
            // In Play Mode, wait for authoritative server metadata via SetMetadata.
            if (string.IsNullOrEmpty(_skinPath) && IsLocalPlayer && !Application.isPlaying)
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
            if (Application.isPlaying)
            {
                if (IsLocalPlayer && PlayerMovementController.LocalPlayer is not { HasServerPosition: true })
                {
                    return;
                }

                if (!IsLocalPlayer && (!_isMetadataLoaded || !_hasReceivedInitialPosition))
                {
                    return;
                }
            }

            TryInitializeDynamicLightSettings();
            ApplyPendingServerPosition();

            if (!IsLocalPlayer)
            {
                Camera? cam = GameplayCamera.Resolve();
                Vector3 camPos = cam != null ? cam.transform.position : transform.position;
                float sqrDistToCam = (transform.position - camPos).sqrMagnitude;
                bool shouldCull = sqrDistToCam > OffscreenCullSqrDistance;

                if (shouldCull)
                {
                    if (!_isCulled)
                    {
                        _isCulled = true;
                        _bodyBatchHandle?.SetEnabled(false);
                        _clanBatchHandle?.SetEnabled(false);
                        if (_nicknameText != null)
                        {
                            _nicknameText.enabled = false;
                        }

                        SetTentaclesActive(false);
                        if (_hasSubmittedDynamicLight && _lightingEngine != null)
                        {
                            _lightingEngine.RemoveDynamicLight(_dynamicLightId);
                            _hasSubmittedDynamicLight = false;
                        }
                    }

                    transform.position = _targetPosition;
                    _smoothPosition = _targetPosition;
                    _smoothAngle = _targetAngle;
                    transform.rotation = Quaternion.Euler(0, 0, _targetAngle);
                    return;
                }

                if (_isCulled)
                {
                    _isCulled = false;
                    _bodyBatchHandle?.SetEnabled(_spriteRenderer == null || _spriteRenderer.enabled);
                    if (_clanSprite != null)
                    {
                        _clanBatchHandle?.SetEnabled(true);
                    }

                    if (_nicknameText != null)
                    {
                        _nicknameText.enabled = true;
                    }

                    SetTentaclesActive(true);
                    if (_tentacles != null)
                    {
                        foreach (Tentacle? tentacle in _tentacles)
                        {
                            tentacle?.Snap(transform.position);
                        }
                    }
                }
            }

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
                _moveSpeed / ProjectRuntimeContracts.Movement.ReferenceMoveSpeed);
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
            LightingEngine? lighting = _lightingEngine;
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
            _lightingEngine?.SetDynamicLightSettings(
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
            _lightingEngine?.SetDynamicLightSettings(
                _dynamicLightIntensity,
                _dynamicLightColor);
        }

        public void ResetDynamicLightPreferences()
        {
            if (!IsLocalPlayer)
            {
                return;
            }

            LightingEngine? lighting = _lightingEngine;
            _dynamicLightIntensity = lighting?.DynamicLightIntensity ??
                _projectDefaults.Lighting.DynamicLightIntensity;
            _dynamicLightColor = lighting?.DynamicLightColor ??
                _projectDefaults.Lighting.DynamicLightColor;
            _dynamicLightSettingsLoaded = true;
        }

        private void InitializeDynamicLightSettings()
        {
            LightingDefaultsSnapshot defaults = _projectDefaults.Lighting;
            LightingEngine? lighting = _lightingEngine;
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
            if (_entityBatchRenderer == null && _resolver != null)
            {
                _entityBatchRenderer = _resolver.Resolve<WorldEntityBatchRenderer>();
            }

            if (_entityBatchRenderer == null)
            {
                return;
            }

            _tentacles = new Tentacle[4];
            _tentaclesSettled = false;
            float[] offsets = { -45f, -15f, 15f, 45f };
            for (int i = 0; i < 4; i++)
            {
                _tentacles[i] = new Tentacle(
                    _entityBatchRenderer,
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

            foreach (Tentacle? tentacle in _tentacles)
            {
                tentacle?.SetActive(active);
            }
        }

        private bool AreTentaclesSettled()
        {
            if (_tentacles == null)
            {
                return true;
            }

            foreach (Tentacle? tentacle in _tentacles)
            {
                if (tentacle != null && !tentacle.IsSettled)
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
                tentacle?.Update(rootPosition, rotationAngle, movementFactor, deltaTime);
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
                Bounds spriteBounds = _skinSprite != null
                    ? TransformSpriteBounds(transform, _skinSprite)
                    : new Bounds(position, Vector3.one);
                Vector3 topRight = new(spriteBounds.max.x, spriteBounds.max.y + 0.5f, position.z);

                // World-space labels use the actual rendered sprite bounds, not a
                // fixed offset from the robot pivot. TopLeft alignment makes the
                // nickname grow rightward from the robot's top-right corner.
                _nicknameText.transform.SetPositionAndRotation(
                    topRight,
                    Quaternion.identity);
            }

            if (_clanTransform != null)
            {
                _clanTransform.SetPositionAndRotation(position + new Vector3(0.6f, -0.5f, 0), Quaternion.identity);
            }

            _lastLabelsPosition = position;
            _hasUpdatedLabels = true;
        }

        public void Initialize(uint botId)
        {
            TryInitializeDynamicLightSettings();
            _botId = botId;
            if (_resolver != null)
            {
                _resolver.Resolve<RobotManager>()?.RegisterRobot(this);
            }

            _isMetadataLoaded = false;
            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = Color.white;
            }

            _bodyBatchHandle?.SetColor(Color.white);

            if (_nicknameText != null)
            {
                _nicknameText.text = string.Empty;
            }

            if (_clanBatchHandle != null)
            {
                _entityBatchRenderer.SetSprite(_clanBatchHandle, null);
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

            _bodyBatchHandle?.SetColor(Color.white);

            if (_nicknameText == null && !IsLocalPlayer)
            {
                InitializeVisualElements();
            }

            if (_nicknameText != null)
            {
                _nicknameText.text = IsLocalPlayer ? string.Empty : nickname;
            }

            _hasUpdatedLabels = false;
            UpdateLabelsPosition();

            LoadMetadataAssets();
        }

        public void SetPosition(ushort x, ushort y)
        {
            if (_resolver == null)
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
            if (!_hasPendingServerPosition || _resolver == null)
            {
                return;
            }

            ApplyServerPosition(_pendingServerX, _pendingServerY);
            _hasPendingServerPosition = false;
        }

        private void ApplyServerPosition(ushort x, ushort y)
        {
            MapManager mm = _resolver.Resolve<MapManager>() ??
                throw new InvalidOperationException(
                    $"{TAG} MapManager is required to apply position for bot {_botId}.");
            _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, mm.WorldHeight);

            if (!_hasReceivedInitialPosition)
            {
                _hasReceivedInitialPosition = true;
                _smoothPosition = _serverPosition;
                _targetPosition = _serverPosition;
                transform.position = _serverPosition;
                _currentVelocity = Vector3.zero;
                if (_tentacles != null)
                {
                    foreach (var tentacle in _tentacles)
                    {
                        tentacle?.Snap(_smoothPosition);
                    }

                    SetTentaclesActive(true);
                }

                return;
            }

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

            if (_resolver == null)
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
                    () => _resolver != null,
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
            if (string.IsNullOrEmpty(_skinPath) || _resolver == null)
            {
                return;
            }

            IAssetLoader loader = _resolver.Resolve<IAssetLoader>() ??
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

            if (skinTexture == null)
            {
                return;
            }

            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            _skinSprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
            EnsureBatchHandles();
            _entityBatchRenderer.SetSprite(_bodyBatchHandle!, _skinSprite);
            if (!IsLocalPlayer)
            {
                _bodyBatchHandle!.SetEnabled(true);
            }

            _hasUpdatedLabels = false;
            UpdateLabelsPosition();
        }

        public void EnsureEditorPreviewVisual()
        {
            if (_spriteRenderer != null && _spriteRenderer.sprite == null)
            {
                var botTex = Resources.Load<Texture2D>("Textures/bot") ?? Resources.Load<Texture2D>("bot");
                if (botTex != null)
                {
                    _skinSprite = Sprite.Create(botTex, new Rect(0, 0, botTex.width, botTex.height), new Vector2(0.5f, 0.5f), 16);
                }
                else
                {
                    var tex = Texture2D.whiteTexture;
                    _skinSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 16);
                }

                _spriteRenderer.sprite = _skinSprite;
                _spriteRenderer.color = new Color(0.2f, 0.65f, 0.95f, 1f);
                _spriteRenderer.enabled = true;
            }
        }

        private async UniTaskVoid LoadTailAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_tailPath) || _resolver == null)
            {
                ClearTentacles();
                return;
            }

            IAssetLoader loader = _resolver.Resolve<IAssetLoader>() ??
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
            if (_clanId == 0 || _resolver == null)
            {
                return;
            }

            IAssetLoader loader = _resolver.Resolve<IAssetLoader>() ??
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

            if (clanTexture == null)
            {
                return;
            }


            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            EnsureBatchHandles();
            _entityBatchRenderer.SetSprite(_clanBatchHandle!, _clanSprite);
            _clanBatchHandle!.SetEnabled(true);
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

            _robotService?.UnregisterRobot(_botId);

            _entityBatchRenderer?.UnregisterSprite(_bodyBatchHandle);
            _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);
            _bodyBatchHandle = null;
            _clanBatchHandle = null;

            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            if (_nicknameText != null)
            {
                Object.Destroy(_nicknameText.gameObject);
                _nicknameText = null;
            }

            ClearTentacles();
        }
    }
}
