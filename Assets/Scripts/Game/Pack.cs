#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Effekseer;
using Fodinae.Game.Managers;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;
using VContainer;

namespace Fodinae.Game
{
    public class Pack : MonoBehaviour
    {
        private Transform? _clanTransform;
        private PackType? _packType;
        private byte _variant;
        private byte _linkedClan;
        private CancellationTokenSource? _cts;
        private Sprite? _packSprite;
        private Sprite? _clanSprite;
        private WorldEntityBatchRenderer.SpriteHandle? _packBatchHandle;
        private WorldEntityBatchRenderer.SpriteHandle? _clanBatchHandle;

        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;

        [Inject]
        private IAssetLoader _assetLoader = null!;

        private EffekseerHandle _effekseerHandle;
        private EffekseerEffectAsset? _effekseerAsset;
        private bool _hasEffekseerEffect;

        protected void Awake()
        {
            if (TryGetComponent<SpriteRenderer>(out var obsoleteRenderer))
            {
                obsoleteRenderer.enabled = false;
            }

            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            clanGo.transform.localPosition = new Vector3(0.6f, -0.5f, 0);
            _clanTransform = clanGo.transform;
        }

        public void Initialize(PackType packType, byte variant, byte linkedClan)
        {
            if (_packType == packType && _variant == variant && _linkedClan == linkedClan && _cts != null)
            {
                return;
            }

            // Clean up previous Effekseer effect if any
            StopEffekseerEffect();

            _packType = packType;
            _variant = variant;
            _linkedClan = linkedClan;

            LoadAssets();
        }

        private void LoadAssets()
        {
            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

            LoadPackAsync(_cts.Token).Forget();
            LoadClanAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid LoadPackAsync(CancellationToken token)
        {
            string packName = _packType.ToString();
            string packPath = $"Pack/{packName}/{_variant}";

            // 1. Try loading as a texture (existing behavior — static or animated sprite)
            if (_assetLoader is not ClientAssetLoader loader)
            {
                return;
            }

            Texture2D? packTexture = await TryLoadOptionalTextureAsync(
                loader,
                packPath,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (packTexture != null)
            {
                if (_packSprite != null)
                {
                    Destroy(_packSprite);
                }

                // Use central PIXELS_PER_UNIT for consistency
                _packSprite = Sprite.Create(packTexture, new Rect(0, 0, packTexture.width, packTexture.height), new Vector2(0.5f, 0.5f), RenderingConstants.PIXELS_PER_UNIT);
                EnsureBatchHandles();
                _entityBatchRenderer.SetSprite(_packBatchHandle!, _packSprite);
                _packBatchHandle!.SetEnabled(true);

                UpdateClanPosition();
                return;
            }

            // 2. Texture not found — try loading as Effekseer effect (.efk data)
            var efkBytes = await loader.GetAssetBytesAsync(packPath, timeoutSeconds: 10);
            if (token.IsCancellationRequested || efkBytes == null || efkBytes.Length < 4)
            {
                return;
            }

            // Verify EFKE header
            if (efkBytes[0] != 'E' || efkBytes[1] != 'F' || efkBytes[2] != 'K' || efkBytes[3] != 'E')
            {
                Debug.LogWarning($"[Pack] File at '{packPath}' is not a valid Effekseer effect (no EFKE header)");
                return;
            }

            var effectAsset = await RuntimeEffekseerLoader.LoadEffectAsync(
                efkBytes,
                $"Pack_{packName}_{_variant}",
                loader,
                texturePathMapper: path => $"{packPath}/{path}",
                textureTimeoutSeconds: 10);

            if (token.IsCancellationRequested || effectAsset == null)
            {
                return;
            }

            _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, transform.position);
            _hasEffekseerEffect = true;
            _effekseerAsset = effectAsset;

            _packBatchHandle?.SetEnabled(false);

            Debug.Log($"[Pack] Playing Effekseer effect for pack '{packName}' variant {_variant} at {transform.position}");
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
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
                    $"[Pack] Optional texture '{filename}' was skipped: {exception.Message}");
                return null;
            }
        }

        private void UpdateClanPosition()
        {
            if (_clanTransform == null)
            {
                return;
            }

            // Position to the right and slightly below the center
            float packWidth = _packSprite != null ? _packSprite.texture.width : RenderingConstants.PIXELS_PER_UNIT;
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

                _packBatchHandle?.SetEnabled(_packSprite != null);
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
                throw new InvalidOperationException(
                    "Pack requires WorldEntityBatchRenderer for batched world rendering.");
            }

            _packBatchHandle ??= _entityBatchRenderer.RegisterSprite(transform, 0);
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

            _entityBatchRenderer?.UnregisterSprite(_packBatchHandle);
            _entityBatchRenderer?.UnregisterSprite(_clanBatchHandle);

            if (_packSprite != null)
            {
                Destroy(_packSprite);
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }
        }
    }
}
