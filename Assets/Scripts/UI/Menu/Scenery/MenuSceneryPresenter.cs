#nullable enable

using System;
using System.IO;
using Kern.Core;
using Kern.Core.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class MenuSceneryPresenter(IRuntimeAssetPaths runtimeAssetPaths)
{
    private readonly IRuntimeAssetPaths _runtimeAssetPaths = runtimeAssetPaths;
    private const float DescentAnimationSeconds = 2.6f;

    private VisualElement? _tree;
    private Image? _spaceBgImage;
    private Image? _sceneryImage;
    private Image? _loaderShade;
    private Image? _logoIcon;
    private VisualElement? _beacon;
    private VisualElement? _beaconPing;
    private VisualElement? _stationBadge;
    private VisualElement? _sidebar;
    private MenuSceneryController? _scenery;
    private MenuStarfield? _starfield;
    private float _descentCameraProgress;
    private float _descentCameraTarget;
    private bool _uiTexturesReady;

    public float DescentTarget
    {
        get => _descentCameraTarget;
        set => _descentCameraTarget = value;
    }

    public bool IsSceneryReady =>
        _uiTexturesReady &&
        _sceneryImage?.image != null &&
        _spaceBgImage?.image != null;

    /// <summary>
    /// Почему сценка не готова — по каждому условию отдельно.
    /// </summary>
    ///
    /// Готовность складывается из пяти независимых вещей: объекты сцены
    /// привязаны, элементы разметки найдены, текстуры интерфейса разложены, и
    /// у обеих картинок появилось содержимое. Пока отказ говорил просто «не
    /// готово за три секунды», из строки нельзя было понять ни одну из пяти, и
    /// следующий шаг назначался гаданием. Здесь они названы поимённо вместе с
    /// разрешённым размером: обе картинки берут содержимое только после
    /// раскладки, и нулевой размер — самый частый ответ.
    public string DescribeReadiness()
    {
        // KERN-HARDCODED-TEXT: диагностика, см. пояснение у return.
        string sceneryImageSize = _sceneryImage == null
            ? "элемента нет"
            : $"{_sceneryImage.resolvedStyle.width:F0}×{_sceneryImage.resolvedStyle.height:F0}";
        // KERN-HARDCODED-TEXT: диагностика, см. пояснение у return.
        string spaceImageSize = _spaceBgImage == null
            ? "элемента нет"
            : $"{_spaceBgImage.resolvedStyle.width:F0}×{_spaceBgImage.resolvedStyle.height:F0}";
        // KERN-HARDCODED-TEXT: диагностика — строка читается человеком в логе и
        // в дев-панели; ключа у неё нет и перевода она не требует.
        return
            $"текстуры интерфейса={_uiTexturesReady}, " +
            $"MenuStarfield={(_starfield != null ? "привязан" : "НЕТ")}, " +
            $"MenuSceneryController={(_scenery != null ? "привязан" : "НЕТ")}, " +
            $"MainMenuSceneryImage: содержимое={(_sceneryImage?.image != null ? "есть" : "НЕТ")}, размер={sceneryImageSize}, " +
            $"SpaceBgImage: содержимое={(_spaceBgImage?.image != null ? "есть" : "НЕТ")}, размер={spaceImageSize}, " +
            $"MenuStarfield.Texture={(_starfield?.Texture != null ? "есть" : "НЕТ")}, " +
            $"MenuSceneryController.OutputTexture={(_scenery?.OutputTexture != null ? "есть" : "НЕТ")}";
    }

    public void Tick(ref Texture2D? spaceBgTexture)
    {
        TryApplyStarfieldTexture(ref spaceBgTexture);
        TryApplySceneryTexture();
        Animate();
    }

    public void BindScene(MenuStarfield? starfield, MenuSceneryController? scenery)
    {
        _starfield = starfield;
        _scenery = scenery;
    }

    public void Bind(VisualElement tree)
    {
        _tree = tree;
        _spaceBgImage = tree.Q<Image>("SpaceBgImage");
        _sceneryImage = tree.Q<Image>("MainMenuSceneryImage");
        _loaderShade = tree.Q<Image>("LoaderShade");
        _logoIcon = tree.Q<Image>("MainMenuLogoIcon");
        _beacon = tree.Q<VisualElement>("MainMenuBeacon");
        _beaconPing = tree.Q<VisualElement>("BeaconPing");
        _stationBadge = tree.Q<VisualElement>("StationBadge");
        _sidebar = tree.Q<VisualElement>(className: "mm-sidebar");
    }

    public void MarkUIBuilt()
    {
    }

    public void ResumeRenderers()
    {
        _scenery?.gameObject.SetActive(true);
        _starfield?.gameObject.SetActive(true);
    }

    public void ApplyTextures(ref Texture2D? shadeTexture, ref Texture2D? spaceBgTexture)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TryApplyStarfieldTexture(ref spaceBgTexture);
        TryApplySceneryTexture();
        ApplyImageTexture(_loaderShade, ref shadeTexture, "Assets/Textures/UI/mm_shade.png", nameof(_loaderShade));

        Texture2D? logoCache = null;
        ApplyImageTexture(_logoIcon, ref logoCache, "Assets/Textures/UI/mm_logo.png", nameof(_logoIcon));

        ApplyIconTexture("SideChronicleIcon", "Assets/Textures/UI/mm_icon_chronicle.png");
        ApplyIconTexture("SideSettingsIcon", "Assets/Textures/UI/mm_icon_settings.png");
        ApplyIconTexture("SideRepairIcon", "Assets/Textures/UI/mm_icon_repair.png");
        ApplyIconTexture("SideUpdateIcon", "Assets/Textures/UI/mm_icon_update.png");
        ApplyIconTexture("SideDiscordIcon", "Assets/Textures/UI/mm_icon_discord.png");
        ApplyIconTexture("SideTelegramIcon", "Assets/Textures/UI/mm_icon_telegram.png");
        ApplyIconTexture("SideVkIcon", "Assets/Textures/UI/mm_icon_vk.png");
        ApplyIconTexture("SideExitIcon", "Assets/Textures/UI/mm_icon_exit.png");

        _uiTexturesReady = true;
    }

    private void ApplyImageTexture(Image? image, ref Texture2D? cache, string assetPath, string debugName)
    {
        if (image == null)
        {
            return;
        }

        cache ??= LoadDirectTexture(assetPath);
        if (cache != null)
        {
            image.image = cache;
        }
        else
        {
            Debug.LogWarning($"[MainMenu] {debugName}: texture failed to load from '{assetPath}'.");
        }
    }

    private void ApplyIconTexture(string elementName, string assetPath)
    {
        if (_tree == null)
        {
            return;
        }

        VisualElement? element = _tree.Q<VisualElement>(elementName);
        Texture2D? iconTexture = LoadDirectTexture(assetPath);
        if (element != null && iconTexture != null)
        {
            element.style.backgroundImage = new StyleBackground(iconTexture);
        }
    }

    private void TryApplyStarfieldTexture(ref Texture2D? spaceBgTexture)
    {
        if (_spaceBgImage == null)
        {
            return;
        }

        if (_starfield != null)
        {
            float width = _spaceBgImage.resolvedStyle.width;
            float height = _spaceBgImage.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 1f || float.IsNaN(height) || height <= 1f)
            {
                return;
            }

            float scale = _spaceBgImage.panel?.scaledPixelsPerPoint ?? 1f;
            _starfield.SetDisplaySize(Mathf.RoundToInt(width * scale), Mathf.RoundToInt(height * scale));
            if (_starfield.Texture != null)
            {
                _spaceBgImage.image = _starfield.Texture;
            }

            return;
        }

        ApplyImageTexture(_spaceBgImage, ref spaceBgTexture, "Assets/Textures/UI/mm_space_bg.png", nameof(_spaceBgImage));
    }

    private void TryApplySceneryTexture()
    {
        if (_sceneryImage == null || _scenery == null)
        {
            return;
        }

        if (_scenery.OutputTexture != null)
        {
            _sceneryImage.image = _scenery.OutputTexture;
        }

        float width = _sceneryImage.resolvedStyle.width;
        float height = _sceneryImage.resolvedStyle.height;
        if (float.IsNaN(width) || width <= 1f || float.IsNaN(height) || height <= 1f)
        {
            return;
        }

        float scale = _sceneryImage.panel?.scaledPixelsPerPoint ?? 1f;
        _scenery.SetDisplaySize(Mathf.RoundToInt(width * scale), Mathf.RoundToInt(height * scale));
    }

    private Texture2D? LoadDirectTexture(string assetPath)
    {
        string relativePath = assetPath.StartsWith("Assets/Textures/", StringComparison.Ordinal)
            ? assetPath["Assets/Textures/".Length..]
            : assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? assetPath["Assets/".Length..]
                : assetPath;

        // The editor and the player can have the same authored UI asset in
        // different roots: the editor reads Assets/Textures, while a previous
        // run may have already persisted the extracted copy. Resolve the
        // canonical file through the public combined lookup instead of assuming
        // the bundled root is the only valid runtime location.
        string? absolutePath = _runtimeAssetPaths.FindTextureFile(relativePath);
        if (absolutePath == null && Application.isEditor)
        {
            // The editor can enter MainMenu before RuntimeAssetPaths has
            // resolved its bundled root. Keep authored UI textures available
            // during that transition; player builds still use the managed
            // bundled/persistent lookup above.
            string editorPath = Path.Combine(
                Application.dataPath,
                "Textures",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(editorPath))
            {
                absolutePath = editorPath;
            }
        }
        if (absolutePath == null)
        {
            Debug.LogError(
                $"[MainMenu] Required UI texture is missing: '{relativePath}'. " +
                $"BundledRoot='{_runtimeAssetPaths.BundledTexturesRoot}'.");
            return null;
        }

        try
        {
            byte[] fileData = File.ReadAllBytes(absolutePath);
            return RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
                fileData,
                Path.GetFileNameWithoutExtension(assetPath),
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                makeNoLongerReadable: false);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MainMenu] Failed to load texture '{absolutePath}': {exception.Message}");
            return null;
        }
    }

    private void UpdateDescentCamera()
    {
        if (Mathf.Approximately(_descentCameraProgress, _descentCameraTarget))
        {
            return;
        }

        _descentCameraProgress = Mathf.MoveTowards(
            _descentCameraProgress,
            _descentCameraTarget,
            Time.unscaledDeltaTime / DescentAnimationSeconds);

        _scenery?.SetDescentFraming(_descentCameraProgress, Vector3.back);
    }

    private void Animate()
    {
        UpdateDescentCamera();
        MenuSceneryMarkers.Animate(
            Time.time,
            _beacon,
            _beaconPing,
            _stationBadge,
            _sidebar,
            _sceneryImage,
            _scenery);
    }
}
