#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Effekseer;
using Fodinae.Core;
using Fodinae.Core.DI;
using Fodinae.Core.Interfaces;
using Fodinae.Effekseer;
using Fodinae.Game.Managers;
using Fodinae.Networking.Buildings;
using Fodinae.Rendering.PostProcessing;
using Fodinae.World;
using Fodinae.World.Terrain;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Game
{
    public class Pack : MonoBehaviour
    {
        private SpriteRenderer? _spriteRenderer;
        private SpriteRenderer? _clanRenderer;
        private PackType? _packType;
        private byte _variant;
        private byte _linkedClan;
        private CancellationTokenSource? _cts;
        private Sprite? _packSprite;
        private Sprite? _clanSprite;

        private EffekseerHandle _effekseerHandle;
        private EffekseerEffectAsset? _effekseerAsset;
        private bool _hasEffekseerEffect;
        private Vector3 _appliedRoofOffset;

        protected void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
            {
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            // Roofs must draw above the terrain door-overlay mesh so doorway
            // cells stay under the building roof texture.
            _spriteRenderer.sortingOrder = RenderingConstants.PACK_ROOF_SORTING_ORDER;

            if (Application.isPlaying)
            {
                _spriteRenderer.enabled = false;
            }

            var clanGo = new GameObject("ClanIcon");
            clanGo.transform.SetParent(transform);
            clanGo.transform.localPosition = new Vector3(0.6f, -0.5f, 0);
            _clanRenderer = clanGo.AddComponent<SpriteRenderer>();
            if (Application.isPlaying)
            {
                _clanRenderer.enabled = false;
            }

            UnityRenderLayerContracts.ApplyWorldUI(_clanRenderer, 10);
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
            var loader = SessionAccess.Resolve()?.TryResolve<IAssetLoader>() as ClientAssetLoader;
            if (loader == null)
            {
                return;
            }

            Texture2D? packTexture = await TryLoadOptionalTextureAsync(
                loader,
                packPath,
                token);
            if (token.IsCancellationRequested || _spriteRenderer == null)
            {
                return;
            }

            if (packTexture != null)
            {
                if (_packSprite != null)
                {
                    Destroy(_packSprite);
                }

                // Use terrain tile density (CELL_SIZE px per unit) so roofs
                // authored in cell pixels render at their authored world size.
                _packSprite = Sprite.Create(packTexture, new Rect(0, 0, packTexture.width, packTexture.height), new Vector2(0.5f, 0.5f), RenderingConstants.CELL_SIZE);
                _spriteRenderer.sprite = _packSprite;
                _spriteRenderer.enabled = true;

                // Центрируем крышу по строевой части сигнатуры (стены/углы/
                // двери, без дорожных хвостов): якорь пака не всегда совпадает
                // с центром здания. Вычитание _appliedRoofOffset делает сдвиг
                // идемпотентным при повторных загрузках варианта.
                if (_packType != null &&
                    BuildingTemplates.TryGet(_packType.Value, out PackBuilding? packBuilding))
                {
                    // Roof center is declared manually per pack template
                    // (anchor-relative, server axes; world Y is flipped).
                    var center = packBuilding.RoofCenterOffsetCells;
                    var desiredOffset = new Vector3(
                        center.x * GameConstants.World.CellSize,
                        -center.y * GameConstants.World.CellSize,
                        0f);
                    _spriteRenderer.transform.position += desiredOffset - _appliedRoofOffset;
                    _appliedRoofOffset = desiredOffset;
                }

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
                texturePathMapper: path => $"{packPath}/{path}",
                textureTimeoutSeconds: 10);

            if (token.IsCancellationRequested || effectAsset == null)
            {
                return;
            }

            _effekseerHandle = EffekseerSystem.PlayEffect(effectAsset, transform.position);
            _hasEffekseerEffect = true;
            _effekseerAsset = effectAsset;

            // Hide sprite renderer — the Effekseer effect handles visuals
            if (_spriteRenderer != null)
            {
                _spriteRenderer.enabled = false;
            }

            Debug.Log($"[Pack] Playing Effekseer effect for pack '{packName}' variant {_variant} at {transform.position}");
        }

        private async UniTaskVoid LoadClanAsync(CancellationToken token)
        {
            if (_linkedClan == 0)
            {
                if (_clanRenderer != null)
                {
                    _clanRenderer.sprite = null;
                }

                return;
            }

            var loader = SessionAccess.Resolve()?.TryResolve<IAssetLoader>() as ClientAssetLoader;
            if (loader == null)
            {
                return;
            }

            Texture2D? clanTexture = await TryLoadOptionalTextureAsync(
                loader,
                $"Clan/{_linkedClan}",
                token);
            if (token.IsCancellationRequested || clanTexture == null || _clanRenderer == null)
            {
                return;
            }

            if (_clanSprite != null)
            {
                Destroy(_clanSprite);
            }

            _clanSprite = Sprite.Create(clanTexture, new Rect(0, 0, clanTexture.width, clanTexture.height), new Vector2(0f, 0.5f), clanTexture.width);
            _clanRenderer.sprite = _clanSprite;
            _clanRenderer.enabled = true;
            _clanRenderer.transform.localScale = Vector3.one * 0.8f;

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
            if (_clanRenderer == null)
            {
                return;
            }

            // Position to the right and slightly below the center
            float packWidth = _packSprite != null ? _packSprite.texture.width : RenderingConstants.PIXELS_PER_UNIT;
            float xOffset = (packWidth / (RenderingConstants.PIXELS_PER_UNIT * 2)) + 0.1f; // Right edge + 0.1 gap
            _clanRenderer.transform.localPosition = new Vector3(xOffset, -0.5f, 0);
        }

        protected void Update()
        {
            if (_hasEffekseerEffect && !_effekseerHandle.exists)
            {
                // Effect has finished playing — clean up
                _hasEffekseerEffect = false;
                RuntimeEffekseerLoader.DestroyEffect(_effekseerAsset);
                _effekseerAsset = null;

                if (_spriteRenderer != null)
                {
                    _spriteRenderer.enabled = true;
                }
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

        protected void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            StopEffekseerEffect();

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
