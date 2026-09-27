#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

/// <summary>
/// Контракт облака локального чата.
///
/// Рамка рисуется стилями в стиле HUD, а не спрайтом: 9-slice в UI Toolkit
/// здесь не работает, и спрайт с рамкой превращался в полосу, не растущую по
/// высоте бокса, пока текст оказывался под полосой. С подложкой-цветом бокс по
/// определению совпадает с текстовым.
///
/// Числа раскладки проверяет WorldLabelChatLayoutPlayModeTests: в EditMode
/// resolvedStyle нельзя читать до прохода стилей, поэтому здесь только
/// контракт исходников — то, что не должно тихо поехать при правках.
///
/// Отдельно охраняется порядок импортов в KernTheme: при равной специфичности
/// выигрывает объявленное позже, и .unity-label из базовой темы перебивал
/// white-space у .world-label-chat. Облако не переносило текст и не росло.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatStyleTests
{
    private const string BubbleRulesPath = "Assets/Resources/Styles/WorldLabels.uss";
    private const string BubbleClass = ".world-label-chat";
    private const string ThemePath = "Assets/UI Toolkit/KernTheme.tss";
    private const string BaseThemeImport = "unity-theme://default";
    private const string WorldLabelsImport = "Styles/WorldLabels.uss";

    [Test]
    public void BubbleStyle_HasNoSpriteBackground()
    {
        // Спрайт с 9-slice не доходит до отрисовки и рвёт рамку с текстом.
        // Облако обязано держаться на подложке-цвете и border-* из темы.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Not.Contain("background-image"));
        Assert.That(rules, Does.Not.Contain("-unity-slice-"));
        Assert.That(rules, Does.Contain("background-color: var(--surface-panel)"));
        Assert.That(rules, Does.Contain("border-color: var(--border-line)"));
        Assert.That(rules, Does.Contain("border-radius: var(--radius-md)"));
    }

    [Test]
    public void BubbleStyle_UsesHudPaletteWithLightText()
    {
        // Тёмная подложка HUD и светлый текст: чёрный текст на тёмной рамке
        // сливался с игровым фоном.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Contain("color: var(--text-primary)"));
        Assert.That(rules, Does.Not.Contain("color: rgb(0, 0, 0)"));
        Assert.That(rules, Does.Not.Contain("color: black"));
    }

    [Test]
    public void BubbleStyle_LetsHeightFollowTextAndClampsWidth()
    {
        // Облако обязано переноситься и расти по высоте, но не шире экрана.
        // Любая заданная высота заморозила бы рамку на одном тексте.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Contain("white-space: normal"));
        Assert.That(rules, Does.Match(@"max-width:\s*\d+px"));
        Assert.That(rules, Does.Not.Match(@"(?<!max-)height:"));
        Assert.That(rules, Does.Not.Contain("min-height:"));
    }

    [Test]
    public void BubbleStyle_HasNoTailPadding()
    {
        // 22px снизу держали место под хвост спрайта. Хвоста больше нет, и
        // именно это поле было единственным, что поднимало бокс над текстом.
        string rules = StripComments(ReadBubbleRules());
        Assert.That(rules, Does.Not.Contain("22px"));
        Assert.That(rules, Does.Not.Match(@"padding-bottom:"));
        Assert.That(rules, Does.Match(@"padding:\s*var\(--space-2\)\s+var\(--space-5\)"));
    }

    [Test]
    public void Theme_ImportsBaseThemeBeforeProjectStyleSheets()
    {
        // Настоящая причина, почему облако не переносило текст. Базовая тема
        // должна идти раньше проектных листов, иначе .unity-label перебивает
        // правила с тем же весом классового селектора.
        string theme = ReadSource(ThemePath);
        int baseTheme = theme.IndexOf(BaseThemeImport, StringComparison.Ordinal);
        int worldLabels = theme.IndexOf(WorldLabelsImport, StringComparison.Ordinal);

        Assert.That(baseTheme, Is.GreaterThanOrEqualTo(0), $"{BaseThemeImport} is not imported.");
        Assert.That(worldLabels, Is.GreaterThanOrEqualTo(0), $"{WorldLabelsImport} is not imported.");
        Assert.That(
            baseTheme,
            Is.LessThan(worldLabels),
            "The base theme must be imported before project style sheets, otherwise its " +
            ".unity-label rules win over project classes of the same specificity.");
    }

    [Test]
    public void NoCodeReferencesTheBubbleSprite()
    {
        // Спрайт выведен из обращения. Пока он лежит в проекте, следующая
        // правка обязана знать, что он не используется.
        string worldLabels = ReadSource("Assets/Scripts/UI/Overlays/WorldLabels.cs");
        Assert.That(worldLabels, Does.Not.Contain("LocalChatBubble"));
        Assert.That(
            ReadSource("Assets/Scripts/Core/Interfaces/Contracts/ProjectRuntimeContracts.cs"),
            Does.Not.Contain("LocalChatBubble"));
    }

    [Test]
    public void BubbleAnchor_IsBottomCenterOfTheRobotsCellTop()
    {
        // Облако вешается нижним центром в верхнюю грань клетки. Прежний
        // горизонтальный сдвиг в полклетки был подбором на глаз: центрировал
        // только облако шириной ровно в клетку, и узкое уезжало влево.
        string bubble = ReadSource("Assets/Scripts/UI/Chat/Floating/FloatingChatBubble.cs");
        Assert.That(bubble, Does.Contain("CellTopOffset"));
        Assert.That(bubble, Does.Contain("_target.position.y + CellTopOffset"));
        Assert.That(bubble, Does.Not.Contain("TargetOffsetX"));
        Assert.That(bubble, Does.Contain("_labels.Create(WorldLabelKind.ChatBubble)"));
    }

    [Test]
    public void WorldLabels_PositionsChatBubbleByBottomCenterAndNicknameByTopLeft()
    {
        // style.translate двигает бокс целиком, поэтому угол привязки
        // вычитается из размера бокса. Без этого облако уезжает вниз от точки
        // и перекрывает робота вместо того, чтобы стоять над ним.
        string worldLabels = ReadSource("Assets/Scripts/UI/Overlays/WorldLabels.cs");
        Assert.That(worldLabels, Does.Contain("WorldLabelKind.ChatBubble"));
        Assert.That(worldLabels, Does.Contain("position.x - (size.x * 0.5f)"));
        Assert.That(worldLabels, Does.Contain("position.y - size.y"));
        Assert.That(
            worldLabels,
            Does.Contain("Vector3 offset = kind == WorldLabelKind.ChatBubble"));
        Assert.That(worldLabels, Does.Contain(": position;"));
    }

    [Test]
    public void WorldLabelKind_HasNicknameAndChatBubble()
    {
        // Метка создаётся по виду, а не по bool-флагу: двум состояниям
        // хватало флага, но флаг не различал якорь, из-за чего облако уезжало.
        Assert.That(
            Enum.GetNames(typeof(WorldLabelKind)),
            Is.EquivalentTo(new[] { "Nickname", "ChatBubble" }));
        Assert.That(
            ReadSource("Assets/Scripts/Core/Interfaces/Contracts/World/IWorldLabels.cs"),
            Does.Contain("IWorldLabel Create(WorldLabelKind kind)"));
    }

    private static string StripComments(string stylesheet) =>
        Regex.Replace(stylesheet, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string ReadSource(string assetPath) =>
        File.ReadAllText(Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length)));

    private static string ReadBubbleRules()
    {
        string rules = ReadSource(BubbleRulesPath);
        int start = rules.IndexOf(BubbleClass, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"{BubbleClass} not found in {BubbleRulesPath}.");
        return rules[start..];
    }
}
