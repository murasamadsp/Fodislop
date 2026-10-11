#nullable enable

using System.Collections;
using System.Collections.Generic;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Kern.Tests.PlayMode;

/// <summary>Independent known albedo/emission/occupancy oracle through the real surface field pass.</summary>
public sealed class SurfaceSceneMaterialFieldPlayModeTests
{
    [UnityTest]
    public IEnumerator SurfaceScene_WeatheredAlbedoKeepsPhysicalMassAndHdrEmission() => RunFieldOracle(false);

    [UnityTest]
    public IEnumerator OriginalSurface_ColorHasThirtyTwoSamplesPerWorldCell() => RunFieldOracle(true);

    private static IEnumerator RunFieldOracle(bool raster)
    {
        var manager = new SurfaceMaterialManager();
        Material material = manager.CreateSurfaceMaterial(Texture2D.whiteTexture, Color.white, raster ? 0f : 2f, 1f,
            Vector2.one, new Vector2(512f, 256f), SurfaceMaterialManager.SurfaceKind.Transit, "Surface geology oracle");
        Texture2D ground = RuntimeTextureFactory.CreateRGBAFloatNoMip(raster ? 64 : 2, raster ? 64 : 2,
            "Independent surface red rock albedo", RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        var knownAlbedo = new Color(0.64f, 0.16f, 0.08f, 1f);
        var authored = new Color[ground.width * ground.height];
        for (int y = 0; y < ground.height; y++)
        {
            for (int x = 0; x < ground.width; x++)
            {
                authored[y * ground.width + x] = raster
                    ? new Color((x + 0.5f) / 64f, (y + 0.5f) / 64f, 0.08f, 1f)
                    : knownAlbedo;
            }
        }
        ground.SetPixels(authored);
        ground.Apply();
        material.SetTexture("_BaseMap", ground);
        var mesh = new Mesh { name = "Production surface field oracle" };
        mesh.vertices = new[]
        {
            new Vector3(253f, 256f, 0f), new Vector3(253f, 258f, 0f),
            new Vector3(259f, 256f, 0f), new Vector3(259f, 258f, 0f),
        };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
        mesh.SetUVs(1, new List<Vector4> { Vector4.one, Vector4.one, Vector4.one, Vector4.one });
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateBounds();
        var albedo = new RenderTexture(384, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var glow = new RenderTexture(384, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        Assert.That(albedo.Create(), Is.True);
        Assert.That(glow.Create(), Is.True);
        using var commands = new CommandBuffer { name = "Production surface geology known-value test" };
        float previousThreshold = Shader.GetGlobalFloat("_SurfaceFieldThreshold");
        Matrix4x4 previousView = Shader.GetGlobalMatrix("_KernLightingFieldView");
        Matrix4x4 previousProjection = Shader.GetGlobalMatrix("_KernLightingFieldProjection");
        Matrix4x4 previousObject = Shader.GetGlobalMatrix("_KernLightingFieldObjectToWorld");
        try
        {
            LightingFieldOrientation.BindRaster(commands, new Vector4(253f, 255f, 6f, 4f), Matrix4x4.identity);
            commands.SetGlobalFloat("_SurfaceFieldThreshold", 0.5f);
            commands.SetRenderTarget(new[] { new RenderTargetIdentifier(albedo), new RenderTargetIdentifier(glow) },
                new RenderTargetIdentifier(albedo));
            commands.SetViewport(new Rect(0, 0, 384, 256));
            commands.EnableScissorRect(new Rect(0, 0, 384, 256));
            commands.ClearRenderTarget(false, true, Color.clear);
            int pass = material.FindPass("LightingMaterialField");
            Assert.That(pass, Is.GreaterThanOrEqualTo(0));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
            commands.DisableScissorRect();
            commands.SetRenderTarget(BuiltinRenderTextureType.None);
            Graphics.ExecuteCommandBuffer(commands);
            AsyncGPUReadbackRequest materialRead = AsyncGPUReadback.Request(albedo, 0, TextureFormat.RGBAFloat);
            AsyncGPUReadbackRequest glowRead = AsyncGPUReadback.Request(glow, 0, TextureFormat.RGBAFloat);
            for (int frame = 0; frame < 60 && (!materialRead.done || !glowRead.done); frame++) { yield return null; }
            Assert.That(materialRead.done && glowRead.done, Is.True);
            Assert.That(materialRead.hasError || glowRead.hasError, Is.False);
            // Known world point (256, 256.5), independent 64-pixel/cell output mapping.
            int row = SystemInfo.graphicsUVStartsAtTop ? 255 - 96 : 96;
            int index = row * 384 + 192;
            var fieldValues = materialRead.GetData<Color>();
            int nonzero = 0;
            int minPixel = fieldValues.Length;
            int maxPixel = -1;
            for (int pixel = 0; pixel < fieldValues.Length; pixel++)
            {
                if (fieldValues[pixel].a > 0.5f)
                {
                    nonzero++;
                    minPixel = Mathf.Min(minPixel, pixel);
                    maxPixel = Mathf.Max(maxPixel, pixel);
                }
            }
            TestContext.WriteLine($"Surface field coverage: {nonzero} pixels; expected 49152; sampled row={row}; covered indices={minPixel}..{maxPixel}; first={(minPixel < fieldValues.Length ? fieldValues[minPixel] : Color.clear)}.");
            Color reflectance = fieldValues[index];
            Color emission = glowRead.GetData<Color>()[index];
            Assert.That(reflectance.r, Is.EqualTo(raster ? 0.9921875f : 0.64f).Within(0.006f));
            Assert.That(reflectance.g, Is.EqualTo(raster ? 0.09796875f : 0.0608f).Within(0.006f));
            Assert.That(reflectance.b, Is.EqualTo(0.024f).Within(0.006f));
            Assert.That(reflectance.a, Is.EqualTo(1f).Within(0.001f));
            Assert.That(emission.r, Is.EqualTo(raster ? 0f : 1.28f).Within(0.001f));
            Assert.That(emission.g, Is.EqualTo(raster ? 0f : 0.32f).Within(0.001f));
            Assert.That(emission.b, Is.EqualTo(raster ? 0f : 0.16f).Within(0.001f));
            Assert.That(emission.a, Is.EqualTo(raster ? 0f : 2f).Within(0.001f));
            if (raster)
            {
                // At 64 output pixels/cell, each authored 32-grid color sample
                // must cover two pixels, then change on the next grid boundary.
                Color next = fieldValues[index + 1];
                Color changed = fieldValues[index + 2];
                Assert.That(next.r, Is.EqualTo(reflectance.r).Within(0.0001f));
                Assert.That(next.g, Is.EqualTo(reflectance.g).Within(0.0001f));
                Assert.That(Mathf.Abs(changed.r - reflectance.r), Is.GreaterThan(0.01f));
                int nextRow = SystemInfo.graphicsUVStartsAtTop ? row - 1 : row + 1;
                int changedRow = SystemInfo.graphicsUVStartsAtTop ? row - 2 : row + 2;
                Assert.That(fieldValues[nextRow * 384 + 192].g,
                    Is.EqualTo(reflectance.g).Within(0.0001f));
                Assert.That(Mathf.Abs(fieldValues[changedRow * 384 + 192].g - reflectance.g),
                    Is.GreaterThan(0.003f));
            }
            TestContext.WriteLine($"Surface geology field: albedo={reflectance}; emission={emission}; occupancy=1.");
        }
        finally
        {
            Shader.SetGlobalFloat("_SurfaceFieldThreshold", previousThreshold);
            Shader.SetGlobalMatrix("_KernLightingFieldView", previousView);
            Shader.SetGlobalMatrix("_KernLightingFieldProjection", previousProjection);
            Shader.SetGlobalMatrix("_KernLightingFieldObjectToWorld", previousObject);
            albedo.Release();
            glow.Release();
            Object.Destroy(albedo);
            Object.Destroy(glow);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(ground);
        }
    }
}
