#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Fodinae.Effekseer;
using Fodinae.Game.Managers;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;
// Протокол по-прежнему называет это Pack: PackType живёт во внешней сборке
// MinesServer.Data, исходников которой в проекте нет. Алиас держит границу —
// наш домен говорит Building, провод остаётся Pack.
using BuildingType = MinesServer.Data.PackType;

namespace Fodinae.Game
{
    public class Building : MonoBehaviour
    {
        private Transform? _clanTransform;
        private BuildingType? _buildingType;
        private byte _variant;
        private byte _linkedClan;
        private CancellationTokenSource? _cts;
        private Sprite? _buildingSprite;
        private Sprite? _clanSprite;
        private WorldEntityBatchRenderer.SpriteHandle? _buildingBatchHandle;
        private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;

        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private IAsyncOperationSupervisor _operations = null!;

        private EffekseerHandle _effekseerHandle;
        private EffekseerEffectAsset? _effekseerAsset;
        private bool _hasEffekseerEffect;

        protected void Awake()
        {
            if (TryGetComponent<SpriteRenderer>(out var obsoleteRenderer))
            {
                obsoleteRenderer.enabled = false;
            }

            Transform? existingClan = transform.Find("ClanIcon");
            GameObject clanGo = existingClan != null
                ? existingClan.gameObject
                : (_sceneObjects != null
                    ? _sceneObjects.Create("ClanIcon", RuntimeOwner.Buildings)
                    : throw new InvalidOperationException(
                        "Building requires injected ISceneObjectFactory before creating ClanIcon."));
            clanGo.transform.SetParent(transform, worldPositionStays: false);
            clanGo.transform.localPosition = new Vector3(0.6f, -0.5f, 0);
            _clanTransform = clanGo.transform;
        }

        public void Initialize(BuildingType buildingType, byte variant, byte linkedClan)
        {
            if (_buildingType == buildingType && _variant == variant && _linkedClan == linkedClan && _cts != null)
            {
                return;
            }

            // Clean up previous Effekseer effect if any
            StopEffekseerEffect();

            _buildingType = buildingType;
            _variant = variant;
            _linkedClan = linkedClan;

            LoadAssets();
        }

        private void LoadAssets()
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            CancellationToken entityToken = _cts.Token;

            _operations.Run(
                "load_building_visual",
                supervisorToken => RunWithLinkedCancellationAsync(
                    LoadBuildingAsync,
                    entityToken,
                    supervisorToken));
            _operations.Run(
                "load_building_clan_badge",
                supervisorToken => RunWithLinkedCancellationAsync(
                    LoadClanAsync,
                    entityToken,
                    supervisorToken));
        }

        private async UniTask LoadBuildingAsync(CancellationToken token)
        {
            string buildingName = _buildingType.ToString();
            string buildingPath = $"Pack/{buildingName}/{_variant}";

            // 1. Try loading as a texture (existing behavior — static or animated sprite)
            if (_assetLoader is not ClientAssetLoader loader)
            {
                return;
            }

            Texture2D? buildingTexture = await TryLoadOptionalTextureAsync(
                loader,
                buildingPath,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (buildingTexture != null)
            {
                if (_buildingSprite != null)
                {
                    Destroy(_buildingSprite);
                }

                // Use central PIXELS_PER_UNIT for consistency
                _buildingSprite = Sprite.Create(buildingTexture, new Rect(0, 0, buildingTexture.width, buildingTexture.height), new Vector2(0.5f, 0.5f), RenderingConstants.PIXELS_PER_UNIT);
                EnsureBatchHandles();
                _entityBatchRenderer.SetSprite(_buildingBatchHandle!, _buildingSprite);
                _buildingBatchHandle!.SetEnabled(true);

                UpdateClanPosition();
                return;
            }

            // 2. Texture not found — try loading as Effekseer effect (.efk data)
            var efkBytes = await loader.GetAssetBytesAsync(buildingPath, timeoutSeconds: 10);
            if (token.IsCancellationRequested || efkBytes == null || efkBytes.Length < 4)
            {
                return;
            }

            // Verify EFKE header
            if (efkBytes[0] != 'E' || efkBytes[1] != 'F' || efkBytes[2] != 'K' || efkBytes[3] != 'E')
            {
                Debug.LogWarning($"[Building] File at '{buildingPath}' is not a valid Effekseer effect (no EFKE header)");
                return;
            }

            var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                efkBytes,
                $"Pack_{buildingName}_{_variant}",
                loader,
                texturePathMapper: path => $"{buildingPath}/{path}",
                textureTimeoutSeconds: 10);

            if (token.IsCancellationRequested || effectAsset == null)
            {
                return;
            }

            _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, transform.position);
            _hasEffekseerEffect = true;
            _effekseerAsset = effectAsset;

            _buildingBatchHandle?.SetEnabled(false);

            Debug.Log($"[Building] Playing Effekseer effect for building '{buildingName}' variant {_variant} at {transform.position}");
        }

        private async UniTask LoadClanAsync(CancellationToken token)
        {
            if (_linkedClan == 0)
            {
                if (_clanBatchHandle != null)
                {
                    _entityBatchRenderer.SetSprite(_clanBatchHandle, null);
                }

                return;
            }

            if (_assetLoader is not ClientAssetLoader loader)
            {
                return;
            }

            Texture2D? clanTexture = await TryLoadOptionalTextureAsync(
                loader,
                $"Clan/{_linkedClan}",
                token);
            if (token.IsCancellationRequested || clanTexture == null || _clanTransform == null)
            {
                return;
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanTransform.localScale = Vector3.one * 0.8f;
            EnsureBatchHandles();
            _entityBatchRenderer.SetSprite(_clanBatchHandle!, _clanSprite);
            _clanBatchHandle!.SetEnabled(true);

            UpdateClanPosition();
        }

        private static async UniTask<Texture2D?> TryLoadOptionalTextureAsync(
            ClientAssetLoader loader,
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
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[Building] Optional texture '{filename}' was skipped: {exception.Message}");
                return null;
            }
        }

        private static async UniTask RunWithLinkedCancellationAsync(
            Func<CancellationToken, UniTask> operation,
            CancellationToken entityToken,
            CancellationToken supervisorToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                entityToken,
                supervisorToken);
            await operation(linkedCancellation.Token);
        }

        private void UpdateClanPosition()
        {
            if (_clanTransform == null)
            {
                return;
            }

            // Position to the right and slightly below the center
            float packWidth = _buildingSprite != null ? _buildingSprite.texture.width : RenderingConstants.PIXELS_PER_UNIT;
            float xOffset = (packWidth / (RenderingConstants.PIXELS_PER_UNIT * 2)) + 0.1f; // Right edge + 0.1 gap
            _clanTransform.localPosition = new Vector3(xOffset, -0.5f, 0);
        }

        protected void Update()
        {
            if (_hasEffekseerEffect && !_effekseerHandle.exists)
            {
                // Effect has finished playing — clean up
                _hasEffekseerEffect = false;
                RuntimeEffekseerLoader.DestroyEffect(_effekseerAsset);
                _effekseerAsset = null;

                _buildingBatchHandle?.SetEnabled(_buildingSprite != null);
            }
        }

        private void StopEffekseerEffect()
        {
            if (_hasEffekseerEffect)
            {
                _effekseerHandle.Stop();
                _hasEffekseerEffect = false;
                RuntimeEffekseerLoader.DestroyEffect(_effekseerAsset);
                _effekseerAsset = null;
            }
        }

        private void EnsureBatchHandles()
        {
            if (_entityBatchRenderer == null)
            {
                return;
            }

            _buildingBatchHandle ??= _entityBatchRenderer.RegisterSprite(transform, 0);
            if (_clanTransform != null)
            {
                _clanBatchHandle ??=
                    _entityBatchRenderer.RegisterSprite(_clanTransform, 10);
            }
        }

        protected void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            StopEffekseerEffect();

            _entityBatchRenderer?.UnregisterSprite(_buildingBatchHandle);
            _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);

            if (_buildingSprite != null)
            {
                Destroy(_buildingSprite);
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }
        }
    }
}
