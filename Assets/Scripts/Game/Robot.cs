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
        private float _rotationSpeed = 1080f;
        [Header("Dynamic Emission")]
        [SerializeField]
        [Tooltip("Разрешает Robot регистрировать dynamic emission source в TerrariaLightingEngine.")]
        private bool _emitsDynamicLight = true;
        [SerializeField]
        [Range(0f, 4f)]
        [Tooltip("Интенсивность dynamic emission. HDR-значение выше 1 усиливает источник.")]
        private float _dynamicLightIntensity = LightingDefaults.DynamicLightIntensity;
        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        [Tooltip("HDR-цвет dynamic emission источника Robot.")]
        private Color _dynamicLightColor = LightingDefaults.DynamicLightColor;

        private const float VISUAL_ROTATION_OFFSET = -90f;

        private const float MIN_SMOOTH_TIME = 0.05f;
        private const float MAX_SMOOTH_TIME = 0.15f;
        private const float REFERENCE_MOVE_SPEED = 25f;
        private const float DYNAMIC_LIGHT_POSITION_EPSILON = 0.00390625f;

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
        private float _moveSpeed = 15f;
        private float _tremor = 0f;

        [Inject]
        private IRobotService _robotService = null!;
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
        [Inject]
        private TentacleBatchRenderer _tentacleBatchRenderer = null!;
        private float _inspectorDynamicLightIntensity;
        private Color _inspectorDynamicLightColor;

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
            _inspectorDynamicLightIntensity = _dynamicLightIntensity;
            _inspectorDynamicLightColor = _dynamicLightColor;

            // Dynamic emission is a property of this Robot source. Terrain
            // lighting owns the global lighting toggle.
            _dynamicLightEnabled = _emitsDynamicLight;
            TerrariaLightingEngine? lighting = TerrariaLightingEngine.Instance;
            if (lighting?.IsRuntimeConfigReady == true)
            {
                _dynamicLightIntensity = lighting.DynamicLightIntensity;
                _dynamicLightColor = lighting.DynamicLightColor;
                _dynamicLightSettingsLoaded = true;
            }

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
            textGo.layer = LayerMask.NameToLayer(PostProcessRendererFeature.WorldUiLayerName);
            textGo.transform.SetParent(transform);
            _nicknameText = textGo.AddComponent<TextMeshPro>();
            _nicknameText.alignment = TextAlignmentOptions.Center;
            _nicknameText.fontSize = 6.4f;
            _nicknameText.textWrappingMode = TextWrappingModes.NoWrap;
            _nicknameText.overflowMode = TextOverflowModes.Overflow;
            _nicknameText.color = Color.white;

            var textRenderer = textGo.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 100;

            var clanGo = new GameObject("ClanIcon");
            clanGo.layer = LayerMask.NameToLayer(PostProcessRendererFeature.WorldUiLayerName);
            clanGo.transform.SetParent(transform);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            _clanRenderer.sortingOrder = 100;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;
        }

        private void ApplyWorldUiLayer()
        {
            int uiLayer = LayerMask.NameToLayer(PostProcessRendererFeature.WorldUiLayerName);

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
                _nicknameText.gameObject.layer = uiLayer;
            }

            if (_clanRenderer != null)
            {
                _clanRenderer.gameObject.layer = uiLayer;
            }
        }

        protected void Start()
        {
            Vector3 snappedPos = new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f,
                transform.position.z);
            transform.position = snappedPos;
            _targetPosition = snappedPos;
            _serverPosition = snappedPos;
            _smoothPosition = snappedPos;
            _smoothAngle = transform.eulerAngles.z;

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
                UpdateLabelsPosition();
                UpdateDynamicLight();
                return;
            }

            float renderDistance = (_smoothPosition - _targetPosition).magnitude;

            // 1. Base smooth time now scales PROPORTIONALLY with speed.
            // Slower = snappier/tighter (low smooth time). Faster = momentum/curves (higher smooth time).
            float speedRatio = Mathf.Clamp01(_moveSpeed / REFERENCE_MOVE_SPEED);
            float targetSmoothTime = Mathf.Lerp(MIN_SMOOTH_TIME, MAX_SMOOTH_TIME, speedRatio);

            // 2. Distance factor: get extra snappy when very close to the target (e.g. moving exactly 1 cell and stopping)
            float distanceRatio = Mathf.Clamp01(renderDistance / 2f);
            float smoothTime = Mathf.Lerp(MIN_SMOOTH_TIME, targetSmoothTime, distanceRatio);

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
                    DYNAMIC_LIGHT_POSITION_EPSILON * DYNAMIC_LIGHT_POSITION_EPSILON &&
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
            _dynamicLightIntensity = lighting?.DynamicLightIntensity ?? _inspectorDynamicLightIntensity;
            _dynamicLightColor = lighting?.DynamicLightColor ?? _inspectorDynamicLightColor;
            _dynamicLightSettingsLoaded = true;
        }

        private void CreateTentacles(Texture2D tailTexture)
        {
            ClearTentacles();
            _tentacles = new Tentacle[4];
            _tentaclesSettled = false;
            float[] offsets = { 0f, 1.5f, 3.0f, 4.5f };
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
                _nicknameText.transform.SetPositionAndRotation(position + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
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
            _botId = botId;
            ServiceLocator.Resolve<RobotManager>()?.RegisterRobot(this);

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
            var mm = ServiceLocator.Resolve<MapManager>();
            if (mm != null)
            {
                _serverPosition = CoordinateUtils.ServerToUnityPos(x, y, mm.WorldHeight);
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
                _ => 0f,
            };
        }

        private void LoadMetadataAssets()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadMetadataAssetsAsync(_cts.Token).Forget();
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
            if (string.IsNullOrEmpty(_skinPath))
            {
                return;
            }

            var loader = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
            if (loader == null)
            {
                Debug.LogWarning($"{TAG} ClientAssetLoader not available for skin load on bot {_botId}");
                return;
            }

            var skinTexture = await loader.GetTextureAsync(_skinPath, token);
            if (token.IsCancellationRequested || skinTexture == null || _spriteRenderer == null)
            {
                return;
            }


            if (_skinSprite != null)
            {
                Object.Destroy(_skinSprite);
            }

            _skinSprite = Sprite.Create(skinTexture, new Rect(0, 0, skinTexture.width, skinTexture.height), new Vector2(0.5f, 0.5f), skinTexture.width);
            _spriteRenderer.sprite = _skinSprite;
        }

        private async UniTaskVoid LoadTailAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_tailPath))
            {
                ClearTentacles();
                return;
            }

            var loader = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
            if (loader == null)
            {
                Debug.LogWarning($"{TAG} ClientAssetLoader not available for tail load on bot {_botId}");
                return;
            }

            var tailTexture = await loader.GetTextureAsync(_tailPath, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (tailTexture != null)
            {
                CreateTentacles(tailTexture);
            }
            else
            {
                Debug.LogWarning($"{TAG} Tail texture not found for bot {_botId}: {_tailPath}");
                ClearTentacles();
            }
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_clanId == 0)
            {
                return;
            }

            var loader = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
            if (loader == null)
            {
                Debug.LogWarning($"{TAG} ClientAssetLoader not available for clan load on bot {_botId}");
                return;
            }

            var clanTexture = await loader.GetTextureAsync($"/Clan/{_clanId}", token);
            if (token.IsCancellationRequested || clanTexture == null || _clanRenderer == null)
            {
                return;
            }


            if (_clanSprite != null)
            {
                Object.Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanRenderer.sprite = _clanSprite;
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
