#nullable enable

using System.Collections.Generic;
using Kern.Core.Bootstrap.WorldLighting;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World.Terrain;

public sealed class TerrainLightingFramePublisherTests
{
    private static readonly List<RectInt> s_noRegions = new();

    // Регрессия: сброс из-за текстуры уходил в кадре её прихода, свет
    // перестраивал поля и AO целиком по старым клеткам, а публикация шага с
    // текстурой перестраивала всё ещё раз.
    [Test]
    public void TextureResetWaitsUntilTexturedCellsArePublished()
    {
        var exchange = new TerrainLightingExchange();
        var publisher = new TerrainLightingFramePublisher(exchange);
        publisher.BeginWorldGeneration();
        publisher.PublishCommittedChanges(1, s_noRegions, textureRefreshOutstanding: false);
        Assert.That(exchange.TryReadNextTerrainChange(1, 0, out TerrainLightingChange worldReset), Is.True);
        Assert.That(worldReset.FullResetReason, Is.EqualTo(TerrainLightingFullResetReason.WorldReplaced));

        publisher.RequestFullReset(TerrainLightingFullResetReason.LightingVisibleTextureChanged);
        publisher.PublishCommittedChanges(1, s_noRegions, textureRefreshOutstanding: true);
        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out _), Is.False,
            "Cells with the new texture are not published yet.");

        publisher.PublishCommittedChanges(2, s_noRegions, textureRefreshOutstanding: false);
        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange textureReset), Is.True);
        Assert.That(textureReset.Kind, Is.EqualTo(TerrainLightingChangeKind.FullReset));
        Assert.That(textureReset.FullResetReason,
            Is.EqualTo(TerrainLightingFullResetReason.LightingVisibleTextureChanged));
        Assert.That(textureReset.TerrainGeometryRevision, Is.EqualTo(2UL));
    }

    [Test]
    public void StrongerResetIsNotHeldBehindPendingTexture()
    {
        var exchange = new TerrainLightingExchange();
        var publisher = new TerrainLightingFramePublisher(exchange);
        publisher.BeginWorldGeneration();
        publisher.PublishCommittedChanges(1, s_noRegions, textureRefreshOutstanding: false);

        publisher.RequestFullReset(TerrainLightingFullResetReason.LightingVisibleTextureChanged);
        publisher.RequestFullReset(TerrainLightingFullResetReason.TerrainConfigurationChanged);
        publisher.PublishCommittedChanges(1, s_noRegions, textureRefreshOutstanding: true);

        Assert.That(exchange.TryReadNextTerrainChange(1, 1, out TerrainLightingChange reset), Is.True);
        Assert.That(reset.FullResetReason, Is.EqualTo(TerrainLightingFullResetReason.TerrainConfigurationChanged));
    }
}
