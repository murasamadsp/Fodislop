#nullable enable

namespace Kern.Tests.World;

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

[TestFixture]
public class TerrainAmbientOcclusionContractTests
{
    private static string FindRepositoryRoot()
    {
        string current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "Assets")) &&
                File.Exists(Path.Combine(current, "Assets/Shaders/Terrain.shader")))
            {
                return current;
            }

            string? parent = Path.GetDirectoryName(current);
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }

    [Test]
    public void TerrainShader_AppliesAmbientOcclusionToBackgroundLayerOnly()
    {
        string root = FindRepositoryRoot();
        string shaderPath = Path.Combine(root, "Assets/Shaders/Terrain.shader");
        Assert.That(File.Exists(shaderPath), Is.True, $"Terrain.shader must exist at {shaderPath}");

        string content = File.ReadAllText(shaderPath);

        // Find the lighting block where GetAmbientOcclusion is applied
        Match aoCallMatch = Regex.Match(
            content,
            @"if\s*\(([^)]*isForeground[^)]*)\)\s*\{[^}]*GetAmbientOcclusion",
            RegexOptions.Singleline);

        Assert.That(
            aoCallMatch.Success,
            Is.True,
            "Terrain.shader must have a layer guard condition containing 'isForeground' before calling GetAmbientOcclusion.");

        string condition = aoCallMatch.Groups[1].Value;

        // Background is layer 0 (isForeground == 0.0, i.e. < 0.5).
        // It must NEVER be guarded with > 0.5, which would erroneously target the foreground layer!
        Assert.That(
            condition.Contains("<"),
            Is.True,
            $"Ambient occlusion must be applied to the background layer (isForeground < 0.5). Actual condition: '{condition}'");

        Assert.That(
            condition.Contains(">"),
            Is.False,
            $"Ambient occlusion layer guard must NOT use '>' (which targets foreground). Actual condition: '{condition}'");
    }

    [Test]
    public void TerrainShader_DeclaresAmbientOcclusionUniforms()
    {
        string root = FindRepositoryRoot();
        string shaderPath = Path.Combine(root, "Assets/Shaders/Terrain.shader");
        string content = File.ReadAllText(shaderPath);

        Assert.That(content.Contains("_WorldAmbientOcclusionTexture"), Is.True, "Must declare _WorldAmbientOcclusionTexture");
        Assert.That(content.Contains("_WorldAmbientOcclusionTexelsPerCell"), Is.True, "Must declare _WorldAmbientOcclusionTexelsPerCell");
        Assert.That(content.Contains("_TerrainAmbientOcclusionStrength"), Is.True, "Must declare _TerrainAmbientOcclusionStrength");
    }

    [Test]
    public void AmbientOcclusionFormula_IsMonotonicAndBounded()
    {
        float strength = 1.0f;

        float EvaluateShadowFactor(float nearbyOccupancy)
        {
            float occlusion = Math.Clamp(MathF.Sqrt(nearbyOccupancy) * strength, 0.0f, 1.0f);
            return 1.0f - occlusion;
        }

        // Open air (no nearby geometry) -> 100% lit
        Assert.AreEqual(1.0f, EvaluateShadowFactor(0.0f), 1e-5f);

        // Under solid block -> strongly shadowed
        Assert.AreEqual(0.0f, EvaluateShadowFactor(1.0f), 1e-5f);

        // Monotonic decrease as occupancy increases
        float previous = 1.0f;
        for (float occupancy = 0.05f; occupancy <= 1.0f; occupancy += 0.05f)
        {
            float current = EvaluateShadowFactor(occupancy);
            Assert.That(current, Is.LessThanOrEqualTo(previous), $"Shadow factor must decrease monotonically at occupancy {occupancy}");
            previous = current;
        }
    }
}
