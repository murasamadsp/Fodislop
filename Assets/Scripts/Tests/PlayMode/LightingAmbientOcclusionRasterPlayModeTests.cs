#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces.WorldLighting;
using Kern.World.Lighting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Kern.Tests.PlayMode;

/// <summary>Production surface pass and AO recorder; terrain digging still needs a game capture.</summary>
[TestFixture]
[Category("GPU")]
public sealed class LightingAmbientOcclusionRasterPlayModeTests
{
    [UnityTest]
    public IEnumerator RemovedUpperCell_MultiplePatchesPreserveLowerCell_AndMatchFullRaster()
    {
        Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True);
        var resources = new LightingResourceManager();
        var registry = new LightingGeometryRegistry();
        var solver = new GeometryLightingSolver(resources);
        using var commands = new CommandBuffer { name = "AO regional regression" };
        var mesh = new Mesh { name = "AO production surface cells" };
        Shader shader = Shader.Find("Kern/World Surface");
        Assert.That(shader, Is.Not.Null);
        var material = new Material(shader);
        Texture2D white = RuntimeTextureFactory.CreateRGBA32NoMip(1, 1, "AO solid atlas",
            RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
        white.SetPixel(0, 0, Color.white);
        white.Apply(false);
        material.EnableKeyword("KERN_SURFACE_TRANSIT");
        material.SetTexture("_BaseMap", white);
        material.SetVector("_BaseMapTileCount", Vector4.one);
        material.SetVector("_WorldSize", Vector4.one);
        material.SetFloat("_Occupancy", 1f);
        var contributor = new SurfaceCells(mesh, material);
        float previousThreshold = Shader.GetGlobalFloat("_SurfaceFieldThreshold");
        commands.SetGlobalFloat("_SurfaceFieldThreshold", 0.5f);
        Vector4 worldRect = new(-8f, -8f, 16f, 16f);
        // Deliberately asymmetric in Y, with a retained cell far from the patch.
        // The patch is a render-target rectangle, so its row origin follows the
        // field; on a top-down target the same world cells sit at the mirrored row.
        RectInt patch = new(
            64,
            LightingFieldOrientation.RowsTopDown ? (16 * 32) - 352 - 32 : 352,
            32,
            32);
        try
        {
            resources.EnsureAmbientOcclusionOnlyResources(16, 16);
            RenderTexture field = resources.AmbientOcclusionField!;
            SetCells(mesh, includeUpper: true);
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            byte[]? before = null;
            yield return Read(field, values => before = values);
            Assert.That(field.format, Is.EqualTo(RenderTextureFormat.R8));
            int upperIndex = LightingFieldOrientation.MemoryRow(368, field.height) * field.width + 80;
            int lowerIndex = LightingFieldOrientation.MemoryRow(80, field.height) * field.width + 368;
            Assert.That(before![upperIndex], Is.EqualTo(255), "Known upper solid cell must occupy its world texel.");
            Assert.That(before[lowerIndex], Is.EqualTo(255), "Known lower solid cell must occupy its world texel.");

            SetCells(mesh, includeUpper: false);
            RectInt retainedPatch = new(240, 240, 32, 32);
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect, [patch, retainedPatch]);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            byte[]? partial = null;
            yield return Read(field, values => partial = values);
            Assert.That(partial![upperIndex], Is.Zero, "Removed geometry must not survive Max blending.");
            Assert.That(partial[lowerIndex], Is.EqualTo(255), "Partial clearing must preserve distant geometry.");
            int changedOutside = 0;
            for (int y = 0; y < field.height; y++)
            {
                int memoryRow = LightingFieldOrientation.MemoryRow(y, field.height);
                int row = memoryRow * field.width;
                for (int x = 0; x < field.width; x++)
                {
                    bool outsideBothPatches =
                        (x < patch.xMin || x >= patch.xMax || memoryRow < patch.yMin || memoryRow >= patch.yMax) &&
                        (x < retainedPatch.xMin || x >= retainedPatch.xMax ||
                         memoryRow < retainedPatch.yMin || memoryRow >= retainedPatch.yMax);
                    if (outsideBothPatches &&
                        before[row + x] != partial[row + x])
                    {
                        changedOutside++;
                    }
                }
            }

            Assert.That(changedOutside, Is.Zero, "Every AO texel outside both patches must remain unchanged.");

            // Independent update oracle: discard the retained field and redraw
            // the same production geometry in full, with no regional policy.
            solver.RecordAmbientOcclusionField(commands, contributor, registry, worldRect);
            Graphics.ExecuteCommandBuffer(commands);
            byte[]? full = null;
            yield return Read(field, values => full = values);
            Assert.That(partial, Is.EqualTo(full), "Partial output must equal a fresh full production raster.");
        }
        finally
        {
            resources.ReleaseResources();
            resources.LightingCommandBuffer?.Release();
            Shader.SetGlobalFloat("_SurfaceFieldThreshold", previousThreshold);
            Object.Destroy(mesh);
            Object.Destroy(material);
            Object.Destroy(white);
        }
    }

    private static IEnumerator Read(RenderTexture field, System.Action<byte[]> receive)
    {
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(field, 0);
        float started = Time.realtimeSinceStartup;
        while (!request.done)
        {
            Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(10f), "AO readback timed out.");
            yield return null;
        }

        Assert.That(request.hasError, Is.False);
        receive(request.GetData<byte>().ToArray());
    }

    private static void SetCells(Mesh mesh, bool includeUpper)
    {
        mesh.Clear();
        mesh.vertices =
        [
            new(3f, -6f), new(4f, -6f), new(4f, -5f), new(3f, -5f),
            new(-6f, 3f), new(-5f, 3f), new(-5f, 4f), new(-6f, 4f),
        ];
        mesh.uv = [Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
            Vector2.zero, Vector2.right, Vector2.one, Vector2.up];
        mesh.uv2 = new Vector2[8];
        mesh.triangles = includeUpper ? [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7] : [0, 1, 2, 0, 2, 3];
    }

    private sealed class SurfaceCells(Mesh mesh, Material material) : ILightingGeometryContributor
    {
        public ulong LightingGeometryRevision => 1;

        public void RenderMaterialGlowFields(CommandBuffer commands, in LightingMaterialGlowContext context)
        {
        }

        public void RenderAmbientOcclusionField(CommandBuffer commands, in LightingAmbientOcclusionContext context)
        {
            LightingFieldOrientation.BindRaster(commands, context.WorldRect, Matrix4x4.identity);
            int pass = material.FindPass("LightingAmbientOcclusionField");
            Assert.That(pass, Is.GreaterThanOrEqualTo(0));
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
        }
    }
}
