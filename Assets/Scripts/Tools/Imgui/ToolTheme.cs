#nullable enable

using System.Collections.Generic;
using Fodinae;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.Tools.Imgui;

/// <summary>Shared, cached visual language for runtime tools.</summary>
public static class ToolTheme
{
    public const float HeaderHeight = 28f;

    public static readonly Color Accent = new(0.20f, 0.78f, 0.92f, 1f);
    public static readonly Color Warning = new(1f, 0.70f, 0.25f, 1f);
    public static readonly Color Success = new(0.42f, 0.90f, 0.62f, 1f);
    public static readonly Color Error = new(1f, 0.42f, 0.38f, 1f);
    public static readonly Color FrameGraphColor = new(0.24f, 0.78f, 0.95f, 0.95f);
    public static readonly Color AllocationGraphColor = new(1f, 0.55f, 0.24f, 0.95f);
    private static readonly Color Text = new(0.91f, 0.94f, 0.97f, 1f);
    private static readonly Color MutedText = new(0.58f, 0.64f, 0.70f, 1f);
    private static readonly List<Texture2D> Textures = [];

    private static GUISkin? _sourceSkin;
    private static GUISkin? _skin;
    private static Texture2D? _accentTexture;
    private static GUIStyle? _sectionLabel;
    private static GUIStyle? _richLabel;
    private static GUIStyle? _wrappedLabel;
    private static GUIStyle? _mutedLabel;
    private static GUIStyle? _metricLabel;
    private static GUIStyle? _fieldLabel;
    private static GUIStyle? _activeButton;
    private static GUIStyle? _secondaryButton;
    private static GUIStyle? _dangerButton;
    private static GUIStyle? _segmentedButton;
    private static GUIStyle? _closeButton;
    private static GUIStyle? _warningLabel;
    private static GUIStyle? _successLabel;
    private static GUIStyle? _errorLabel;
    private static GUIStyle? _card;
    private static GUIStyle? _graph;
    private static GUIStyle? _scope;
    public static GUIStyle SectionLabel => _sectionLabel!;
    public static GUIStyle RichLabel => _richLabel!;
    public static GUIStyle WrappedLabel => _wrappedLabel!;
    public static GUIStyle MutedLabel => _mutedLabel!;
    public static GUIStyle MetricLabel => _metricLabel!;
    public static GUIStyle FieldLabel => _fieldLabel!;
    public static GUIStyle ActiveButton => _activeButton!;
    public static GUIStyle SecondaryButton => _secondaryButton!;
    public static GUIStyle DangerButton => _dangerButton!;
    public static GUIStyle SegmentedButton => _segmentedButton!;
    public static GUIStyle CloseButton => _closeButton!;
    public static GUIStyle WarningLabel => _warningLabel!;
    public static GUIStyle SuccessLabel => _successLabel!;
    public static GUIStyle ErrorLabel => _errorLabel!;
    public static GUIStyle Card => _card!;
    public static GUIStyle Graph => _graph!;
    public static GUIStyle Scope => _scope!;

    /// <summary>Returns the themed clone associated with the current Unity skin.</summary>
    public static GUISkin ResolveSkin(GUISkin source)
    {
        if (_skin != null && ReferenceEquals(_sourceSkin, source))
        {
            return _skin;
        }

        ReleaseResources();
        _sourceSkin = source;
        Build(source);
        return _skin!;
    }

    public static void Reset()
    {
        ReleaseResources();
        _sourceSkin = null;
    }

    public static void DrawHeaderRule(float width)
    {
        if (Event.current.type != EventType.Repaint || _accentTexture == null)
        {
            return;
        }

        GUI.DrawTexture(new Rect(1f, HeaderHeight - 2f, Mathf.Max(0f, width - 2f), 1f), _accentTexture);
    }

    public static void Separator(float spaceBefore = 8f, float spaceAfter = 8f)
    {
        GUILayout.Space(spaceBefore);
        Rect rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint && _accentTexture != null)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.24f);
            GUI.DrawTexture(rect, _accentTexture);
            GUI.color = previousColor;
        }

        GUILayout.Space(spaceAfter);
    }

    private static void Build(GUISkin source)
    {
        _skin = Object.Instantiate(source);
        _skin.name = "Fodinae Runtime Tools";

        Texture2D window = CreateBordered(
            "ToolTheme.Window",
            new Color32(17, 22, 29, 248),
            new Color32(61, 76, 91, 255));
        Texture2D card = CreateBordered(
            "ToolTheme.Card",
            new Color32(24, 31, 40, 244),
            new Color32(48, 61, 74, 255));
        Texture2D graph = CreateBordered(
            "ToolTheme.Graph",
            new Color32(10, 14, 19, 245),
            new Color32(48, 64, 78, 255));
        Texture2D control = CreateBordered(
            "ToolTheme.Control",
            new Color32(31, 40, 50, 255),
            new Color32(65, 80, 94, 255));
        Texture2D controlHover = CreateBordered(
            "ToolTheme.ControlHover",
            new Color32(39, 52, 64, 255),
            new Color32(76, 104, 122, 255));
        Texture2D controlPressed = CreateBordered(
            "ToolTheme.ControlPressed",
            new Color32(20, 72, 88, 255),
            new Color32(70, 190, 218, 255));
        Texture2D selected = CreateBordered(
            "ToolTheme.Selected",
            new Color32(24, 80, 96, 255),
            new Color32(64, 192, 220, 255));
        Texture2D danger = CreateBordered(
            "ToolTheme.Danger",
            new Color32(83, 37, 37, 255),
            new Color32(182, 75, 68, 255));
        Texture2D dangerHover = CreateBordered(
            "ToolTheme.DangerHover",
            new Color32(108, 43, 42, 255),
            new Color32(225, 91, 82, 255));
        Texture2D field = CreateBordered(
            "ToolTheme.Field",
            new Color32(9, 13, 18, 255),
            new Color32(57, 70, 82, 255));
        Texture2D fieldFocused = CreateBordered(
            "ToolTheme.FieldFocused",
            new Color32(10, 20, 26, 255),
            new Color32(62, 182, 207, 255));
        Texture2D sliderTrack = CreateBordered(
            "ToolTheme.SliderTrack",
            new Color32(12, 17, 22, 255),
            new Color32(46, 59, 70, 255));
        Texture2D sliderThumb = CreateBordered(
            "ToolTheme.SliderThumb",
            new Color32(49, 177, 204, 255),
            new Color32(121, 226, 244, 255));
        _accentTexture = CreateSolid("ToolTheme.Accent", new Color32(52, 196, 224, 255));

        ConfigureWindow(_skin.window, window);
        ConfigureButton(_skin.button, control, controlHover, controlPressed, selected);
        ConfigureLabel(_skin.label);
        ConfigureTextField(_skin.textField, field, fieldFocused);
        ConfigureTextField(_skin.textArea, field, fieldFocused, fixedHeight: 0f);
        ConfigureToggle(_skin.toggle);
        ConfigureBox(_skin.box, card);
        ConfigureSlider(_skin.horizontalSlider, _skin.horizontalSliderThumb, sliderTrack, sliderThumb);
        ConfigureScrollbars(_skin, control, controlHover, sliderThumb);

        _sectionLabel = new GUIStyle(_skin.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Accent },
            margin = new RectOffset(0, 0, 5, 4),
        };
        _richLabel = new GUIStyle(_skin.label)
        {
            richText = true,
        };
        _wrappedLabel = new GUIStyle(_skin.label)
        {
            wordWrap = true,
        };
        _mutedLabel = new GUIStyle(_wrappedLabel)
        {
            fontSize = 10,
            normal = { textColor = MutedText },
        };
        _metricLabel = new GUIStyle(_skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Text },
            margin = new RectOffset(0, 0, 2, 5),
        };
        _fieldLabel = new GUIStyle(_skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fixedHeight = 24f,
            normal = { textColor = new Color(0.78f, 0.83f, 0.88f, 1f) },
        };
        _activeButton = new GUIStyle(_skin.button);
        SetButtonBackgrounds(_activeButton, selected, controlPressed, controlPressed, selected);
        SetAllTextColors(_activeButton, Color.white);
        _secondaryButton = new GUIStyle(_skin.button)
        {
            fontStyle = FontStyle.Normal,
        };
        _dangerButton = new GUIStyle(_skin.button);
        SetButtonBackgrounds(_dangerButton, danger, dangerHover, dangerHover, danger);
        SetAllTextColors(_dangerButton, new Color(1f, 0.88f, 0.86f, 1f));
        _segmentedButton = new GUIStyle(_skin.button);
        SetButtonBackgrounds(_segmentedButton, control, controlHover, controlPressed, selected);
        SetOnTextColors(_segmentedButton, Color.white);
        _closeButton = new GUIStyle(_skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 2),
            fixedWidth = 24f,
            fixedHeight = 22f,
        };
        SetButtonBackgrounds(_closeButton, control, dangerHover, danger, danger);
        _warningLabel = CreateSemanticLabel(Warning);
        _successLabel = CreateSemanticLabel(Success);
        _errorLabel = CreateSemanticLabel(Error);
        _card = new GUIStyle(_skin.box);
        _graph = new GUIStyle(_skin.box)
        {
            normal = { background = graph },
            border = Border(),
            padding = new RectOffset(5, 5, 5, 5),
        };
        _scope = new GUIStyle(_graph)
        {
            padding = new RectOffset(7, 7, 7, 7),
        };
    }

    private static void ConfigureWindow(GUIStyle style, Texture2D background)
    {
        SetAllBackgrounds(style, background);
        SetAllTextColors(style, Text);
        style.border = Border();
        style.padding = new RectOffset(12, 12, 32, 12);
        style.alignment = TextAnchor.UpperLeft;
        style.contentOffset = new Vector2(10f, 7f);
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;
    }

    private static void ConfigureButton(
        GUIStyle style,
        Texture2D normal,
        Texture2D hover,
        Texture2D pressed,
        Texture2D selected)
    {
        SetButtonBackgrounds(style, normal, hover, pressed, selected);
        SetAllTextColors(style, Text);
        SetOnTextColors(style, Color.white);
        style.border = Border();
        style.padding = new RectOffset(9, 9, 4, 5);
        style.margin = new RectOffset(2, 2, 2, 2);
        style.fixedHeight = 26f;
        style.alignment = TextAnchor.MiddleCenter;
    }

    private static void ConfigureLabel(GUIStyle style)
    {
        SetAllTextColors(style, Text);
        style.fontSize = 12;
        style.padding = new RectOffset(1, 1, 1, 1);
        style.margin = new RectOffset(1, 1, 1, 1);
    }

    private static void ConfigureTextField(
        GUIStyle style,
        Texture2D normal,
        Texture2D focused,
        float fixedHeight = 24f)
    {
        SetButtonBackgrounds(style, normal, normal, focused, focused);
        SetAllTextColors(style, Text);
        style.border = Border();
        style.padding = new RectOffset(7, 7, 4, 4);
        style.fixedHeight = fixedHeight;
    }

    private static void ConfigureToggle(GUIStyle style)
    {
        SetAllTextColors(style, Text);
        SetOnTextColors(style, Color.white);
        style.fontSize = 12;
        style.fixedHeight = 22f;
    }

    private static void ConfigureBox(GUIStyle style, Texture2D background)
    {
        SetAllBackgrounds(style, background);
        SetAllTextColors(style, Text);
        style.border = Border();
        style.padding = new RectOffset(10, 10, 9, 10);
        style.margin = new RectOffset(1, 1, 4, 5);
    }

    private static void ConfigureSlider(
        GUIStyle track,
        GUIStyle thumb,
        Texture2D trackTexture,
        Texture2D thumbTexture)
    {
        SetAllBackgrounds(track, trackTexture);
        track.border = Border();
        track.fixedHeight = 7f;
        track.margin = new RectOffset(5, 5, 9, 8);

        SetAllBackgrounds(thumb, thumbTexture);
        thumb.border = Border();
        thumb.fixedWidth = 13f;
        thumb.fixedHeight = 19f;
    }

    private static void ConfigureScrollbars(
        GUISkin skin,
        Texture2D normal,
        Texture2D hover,
        Texture2D thumb)
    {
        ConfigureScrollbar(skin.verticalScrollbar, normal, vertical: true);
        ConfigureScrollbar(skin.horizontalScrollbar, normal, vertical: false);
        ConfigureScrollbarThumb(skin.verticalScrollbarThumb, thumb, hover, vertical: true);
        ConfigureScrollbarThumb(skin.horizontalScrollbarThumb, thumb, hover, vertical: false);
        ConfigureScrollbarButton(skin.verticalScrollbarUpButton, normal, hover);
        ConfigureScrollbarButton(skin.verticalScrollbarDownButton, normal, hover);
        ConfigureScrollbarButton(skin.horizontalScrollbarLeftButton, normal, hover);
        ConfigureScrollbarButton(skin.horizontalScrollbarRightButton, normal, hover);
        skin.scrollView.normal.background = null;
    }

    private static void ConfigureScrollbar(GUIStyle style, Texture2D background, bool vertical)
    {
        SetAllBackgrounds(style, background);
        style.border = Border();
        style.fixedWidth = vertical ? 12f : 0f;
        style.fixedHeight = vertical ? 0f : 12f;
    }

    private static void ConfigureScrollbarThumb(GUIStyle style, Texture2D normal, Texture2D hover, bool vertical)
    {
        SetButtonBackgrounds(style, normal, hover, hover, normal);
        style.border = Border();
        style.fixedWidth = vertical ? 12f : 0f;
        style.fixedHeight = vertical ? 0f : 12f;
    }

    private static void ConfigureScrollbarButton(GUIStyle style, Texture2D normal, Texture2D hover)
    {
        SetButtonBackgrounds(style, normal, hover, hover, normal);
        style.border = Border();
        style.fixedWidth = 12f;
        style.fixedHeight = 12f;
    }

    private static GUIStyle CreateSemanticLabel(Color color) => new(_wrappedLabel!)
    {
        normal = { textColor = color },
    };

    private static Texture2D CreateSolid(string name, Color32 color)
    {
        Texture2D texture = RuntimeTextureFactory.CreateRgba32NoMip(
            1,
            1,
            name,
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        Textures.Add(texture);
        return texture;
    }

    private static Texture2D CreateBordered(string name, Color32 fill, Color32 border)
    {
        Texture2D texture = RuntimeTextureFactory.CreateRgba32NoMip(
            3,
            3,
            name,
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        texture.hideFlags = HideFlags.HideAndDontSave;
        Color32[] pixels =
        [
            border, border, border,
            border, fill, border,
            border, border, border,
        ];
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        Textures.Add(texture);
        return texture;
    }

    private static void SetButtonBackgrounds(
        GUIStyle style,
        Texture2D normal,
        Texture2D hover,
        Texture2D active,
        Texture2D selected)
    {
        style.normal.background = normal;
        style.hover.background = hover;
        style.active.background = active;
        style.focused.background = hover;
        style.onNormal.background = selected;
        style.onHover.background = selected;
        style.onActive.background = active;
        style.onFocused.background = selected;
    }

    private static void SetAllBackgrounds(GUIStyle style, Texture2D background)
    {
        style.normal.background = background;
        style.hover.background = background;
        style.active.background = background;
        style.focused.background = background;
        style.onNormal.background = background;
        style.onHover.background = background;
        style.onActive.background = background;
        style.onFocused.background = background;
    }

    private static void SetAllTextColors(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
        SetOnTextColors(style, color);
    }

    private static void SetOnTextColors(GUIStyle style, Color color)
    {
        style.onNormal.textColor = color;
        style.onHover.textColor = color;
        style.onActive.textColor = color;
        style.onFocused.textColor = color;
    }

    private static RectOffset Border() => new(1, 1, 1, 1);

    private static void ReleaseResources()
    {
        if (_skin != null)
        {
            CoreUtils.Destroy(_skin);
            _skin = null;
        }

        foreach (Texture2D texture in Textures)
        {
            CoreUtils.Destroy(texture);
        }

        Textures.Clear();
        _accentTexture = null;
        _sectionLabel = null;
        _richLabel = null;
        _wrappedLabel = null;
        _mutedLabel = null;
        _metricLabel = null;
        _fieldLabel = null;
        _activeButton = null;
        _secondaryButton = null;
        _dangerButton = null;
        _segmentedButton = null;
        _closeButton = null;
        _warningLabel = null;
        _successLabel = null;
        _errorLabel = null;
        _card = null;
        _graph = null;
        _scope = null;
    }
}
