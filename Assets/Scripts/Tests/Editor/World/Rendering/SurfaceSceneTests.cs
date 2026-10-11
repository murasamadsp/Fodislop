#nullable enable

using System;
using System.Linq;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using NUnit.Framework;
using UnityEditor;
using VContainer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kern.Tests.World;

public sealed class SurfaceSceneTests
{
    [Test]
    public void RockMeshes_AreDeterministicAndContinuousAcrossNegativeAndPositiveSegments()
    {
        foreach (bool middle in new[] { false, true })
        {
            Mesh left = SurfaceRockGeometry.Create(-1, 1132, 32f, middle);
            Mesh right = SurfaceRockGeometry.Create(0, 1132, 32f, middle);
            Mesh repeat = SurfaceRockGeometry.Create(-1, 1132, 32f, middle);
            try
            {
                Assert.That(left.vertices, Is.EqualTo(repeat.vertices));
                Assert.That(left.triangles.Length / 3, Is.EqualTo(640));
                Vector3[] a = left.vertices;
                Vector3[] b = right.vertices;
                foreach (Vector3 point in a)
                {
                    if (point.x != 32f) { continue; }
                    Assert.That(Array.Exists(b, candidate => candidate.x == 0f &&
                        Mathf.Abs(candidate.y - point.y) < 0.0001f && candidate.z == point.z), Is.True,
                        "Every right-edge vertex must meet the next segment at the same world point.");
                }
                Assert.That(left.bounds.min.y, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(left);
                Object.DestroyImmediate(right);
                Object.DestroyImmediate(repeat);
            }
        }
    }

    [TestCase(0L, 1132, 0, 0.7563002109527588f)]
    [TestCase(-1L, 1132, 0, 0.06766748428344727f)]
    [TestCase(0L, 1132, 1, 0.60765540599823f)]
    [TestCase(32L, 1132, 0, 0.31211209297180176f)]
    public void SpatialHash_MatchesIndependentUnsigned64BitOracle(long x, int seed, int channel, float expected)
    {
        Assert.That(SurfaceSpatialHash.Sample(x, seed, channel), Is.EqualTo(expected));
    }

    [Test]
    public void Massifs_ReproduceWorldCoordinateAndChangeWithCoordinateOrSeed()
    {
        Mesh original = SurfaceMassifGeometry.Create(-2, 1132, 32f, 1);
        Mesh repeat = SurfaceMassifGeometry.Create(-2, 1132, 32f, 1);
        Mesh adjacent = SurfaceMassifGeometry.Create(-1, 1132, 32f, 1);
        Mesh otherSeed = SurfaceMassifGeometry.Create(-2, 1133, 32f, 1);
        try
        {
            Assert.That(original.vertices, Is.EqualTo(repeat.vertices));
            Assert.That(original.vertices, Is.Not.EqualTo(adjacent.vertices));
            Assert.That(original.vertices, Is.Not.EqualTo(otherSeed.vertices));
            Assert.That(original.bounds.size.z, Is.GreaterThan(1f));
            Assert.That(original.bounds.size.y, Is.GreaterThan(10f));
        }
        finally
        {
            Object.DestroyImmediate(original);
            Object.DestroyImmediate(repeat);
            Object.DestroyImmediate(adjacent);
            Object.DestroyImmediate(otherSeed);
        }
    }

    [Test]
    public void Foreground_HasOnlyLowBasaltLedgesAcrossWorldCoordinates()
    {
        foreach (int seed in new[] { 1132, 1133, -17 })
        {
            for (int segment = -12; segment <= 12; segment++)
            {
                Mesh mesh = SurfaceMassifGeometry.Create(segment, seed, 32f, 2);
                try
                {
                    Assert.That(mesh.bounds.max.y, Is.LessThan(3.8f),
                        "Near-surface relief must remain low; mountains belong to farther layers.");
                    Assert.That(mesh.bounds.size.z, Is.GreaterThan(0.5f));
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }
    }

    [Test]
    public void AuthoredProfile_HasSupportedShaderAndExplicitSampling()
    {
        SurfaceSceneProfile profile = Resources.Load<SurfaceSceneProfile>(SurfaceSceneProfile.ResourcePath);
        Assert.That(profile, Is.Not.Null);
        Assert.DoesNotThrow(profile.Validate);
        Assert.That(ShaderUtil.ShaderHasError(profile.Shader), Is.False,
            string.Join("\n", ShaderUtil.GetShaderMessages(profile.Shader).Select(message => message.message)));
        foreach (string name in new[] { "Kern/World Surface", "Universal Render Pipeline/Custom/Terrain" })
        {
            Shader sharedShader = Shader.Find(name);
            Assert.That(sharedShader, Is.Not.Null);
            Assert.That(sharedShader.isSupported, Is.True);
            Assert.That(ShaderUtil.ShaderHasError(sharedShader), Is.False,
                string.Join("\n", ShaderUtil.GetShaderMessages(sharedShader).Select(message => message.message)));
        }
        Assert.That(profile.Basalt.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
        foreach (Texture2D ridge in profile.DistantRidges)
        {
            Assert.That(ridge.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(ridge.filterMode, Is.EqualTo(FilterMode.Trilinear));
        }
    }

    [Test]
    public void Coverage_StableFramesDoNotRebuild_ParallaxAndReloadAreExact()
    {
        var parent = EditorUtility.CreateGameObjectWithHideFlags("Surface test owner", HideFlags.DontSave);
        using var objects = new FixtureFactory(parent.transform);
        GameObject cameraObject = objects.Create("Surface test camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 30f;
        camera.aspect = 16f / 9f;
        camera.transform.position = new Vector3(100, 260, -10);
        using var renderer = new SurfaceSceneRenderer(objects, null!, new FixtureCamera(camera));
        SurfaceSceneProfile profile = Resources.Load<SurfaceSceneProfile>(SurfaceSceneProfile.ResourcePath);
        try
        {
            renderer.Initialize(parent.transform, profile);
            renderer.UpdateView(camera, 512, 256);
            int rebuilds = renderer.RebuildCount;
            int segments = renderer.ActiveSegmentCount;
            Transform root = parent.transform.Find("SurfaceScene");
            Transform middle = root.Find("Surface layer 1 segment 0");
            foreach (MeshRenderer band in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!band.gameObject.name.StartsWith("Surface layer", StringComparison.Ordinal)) { continue; }
                Mesh mesh = band.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(band.transform.localPosition.y, Is.EqualTo(4f),
                    "The original two-cell transit and two-cell perspective band owns the first four cells.");
                Assert.That(band.sharedMaterial.GetFloat("_GroundBandHeight"), Is.EqualTo(4f));
                Assert.That(band.transform.position.z + mesh.bounds.min.z, Is.GreaterThan(0f),
                    "Every decorative 3D vertex must be behind the gameplay sprite plane (z=0).");
            }
            float initial = middle.position.x;
            for (int frame = 0; frame < 100; frame++) { renderer.UpdateView(camera, 512, 256); }
            Assert.That(renderer.RebuildCount, Is.EqualTo(rebuilds));
            Assert.That(renderer.LastRebuildReason, Is.EqualTo("world dimensions"));
            Assert.That(renderer.ActiveSegmentCount, Is.EqualTo(segments));
            camera.transform.position += Vector3.right;
            renderer.UpdateView(camera, 512, 256);
            Assert.That(middle.position.x - initial, Is.EqualTo(0.75f).Within(0.0001f));
            camera.transform.position = new Vector3(700, 260, -10);
            renderer.UpdateView(camera, 512, 256);
            Assert.That(renderer.ActiveSegmentCount, Is.LessThan(24));
            renderer.Dispose();
            Assert.That(parent.transform.Find("SurfaceScene"), Is.Null);
            Assert.That(cameraObject, Is.Not.Null);
            renderer.Initialize(parent.transform, profile);
            renderer.UpdateView(camera, 512, 128);
            Assert.That(parent.transform.Find("SurfaceScene").position.y, Is.EqualTo(128));
            Assert.That(renderer.LastRebuildReason, Is.EqualTo("world dimensions"));
        }
        finally
        {
            renderer.Dispose();
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(parent);
        }
    }

    private sealed class FixtureCamera : IGameplayCamera
    {
        public FixtureCamera(Camera camera) => Camera = camera;

        public Camera Camera { get; }
    }

    private sealed class FixtureFactory : ISceneObjectFactory, IDisposable
    {
        private readonly IDisposable _container;
        private readonly SceneObjectFactory _factory;
        private SceneObjectFactory Factory => _factory;

        public FixtureFactory(Transform root)
        {
            var container = new ContainerBuilder().Build();
            _container = container;
            _factory = new SceneObjectFactory(root, root, root, root, root, root, container);
        }
        public Transform GetOwner(RuntimeOwner owner = RuntimeOwner.General) => Factory.GetOwner(owner);
        public GameObject Create(string name, RuntimeOwner owner = RuntimeOwner.General)
        {
            return Factory.Create(name, owner);
        }

        public T Create<T>(string name, RuntimeOwner owner = RuntimeOwner.General) where T : MonoBehaviour =>
            Factory.Create<T>(name, owner);

        public void Dispose() => _container.Dispose();
    }
}
