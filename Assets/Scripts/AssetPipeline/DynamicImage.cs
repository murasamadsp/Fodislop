#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinae
{
    [RequireComponent(typeof(Image))]
    public class DynamicImage : MonoBehaviour
    {
        private Image? _image;
        private Sprite? _runtimeSprite;

        protected void Awake()
        {
            _image = GetComponent<Image>();
        }

        public void LoadImageFromServer(string assetFilename, string etag)
        {
            var cancellationToken = this.GetCancellationTokenOnDestroy();
            LoadAndApplyTextureAsync(assetFilename, cancellationToken).Forget();
        }

        private async UniTask LoadAndApplyTextureAsync(
            string assetFilename,
            CancellationToken cancellationToken)
        {
            IAssetLoader loader = ServiceLocator.Resolve<IAssetLoader>() ??
                throw new InvalidOperationException(
                    "DynamicImage loading requires a registered IAssetLoader.");
            Texture2D? texture;
            try
            {
                texture = await loader.GetTextureAsync(assetFilename, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[DynamicImage] Optional image '{assetFilename}' was skipped: {exception.Message}");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (texture == null)
            {
                Debug.LogWarning(
                    $"[DynamicImage] Optional image '{assetFilename}' returned no texture; skipped.");
                return;
            }

            Image image = _image ?? throw new InvalidOperationException(
                "DynamicImage image component was not initialized before loading.");
            if (_runtimeSprite != null)
            {
                Destroy(_runtimeSprite);
                _runtimeSprite = null;
            }

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                RenderingConstants.PIXELS_PER_UNIT);
            _runtimeSprite = sprite;
            image.sprite = sprite;
        }

        protected void OnDestroy()
        {
            if (_runtimeSprite != null)
            {
                Destroy(_runtimeSprite);
                _runtimeSprite = null;
            }
        }
    }
}
