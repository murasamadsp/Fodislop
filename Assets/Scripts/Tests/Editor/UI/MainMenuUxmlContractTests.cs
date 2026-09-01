#nullable enable

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Tests.UI;

[TestFixture]
public sealed class MainMenuUxmlContractTests
{
    private static readonly string[] RequiredLoaderElements =
    [
        "LoaderContainer",
        "LoaderContent",
        "LoaderProgressFill",
        "LoaderPhaseLabel",
        "LoaderPhaseCount",
        "LoaderPhaseList",
        "CancelDescentButton",
    ];

    [Test]
    public void MainMenuResourceContainsCompleteLoadingUi()
    {
        VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/MainMenu");
        Assert.That(asset, Is.Not.Null);

        TemplateContainer tree = asset.CloneTree();
        foreach (string elementName in RequiredLoaderElements)
        {
            Assert.That(tree.Q(elementName), Is.Not.Null, $"Missing #{elementName}");
        }
    }
}
