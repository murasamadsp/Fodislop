#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using UnityEngine;

namespace Kern.Game;

// Shared native sprite geometry, UVs and Animator timelines. Loaded once at world entry.
public sealed class SpriteAnimationLibrary : IDisposable
{
    private readonly Dictionary<string, Clip> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material> _materials = new(StringComparer.Ordinal);
    private readonly List<Texture2D> _textures = new();
    private readonly Mesh[] _meshes;
    private readonly Material[] _meshMaterials;

    public SpriteAnimationLibrary()
    {
        TextAsset asset = Resources.Load<TextAsset>("VFX/SpriteAnimations/Catalog")
            ?? throw new InvalidOperationException("The sprite animation catalog is missing.");
        Catalog data = JsonUtility.FromJson<Catalog>(asset.text)
            ?? throw new InvalidOperationException("The sprite animation catalog is invalid.");
        Shader shader = Resources.Load<Shader>("Shaders/CellDestruction")
            ?? throw new InvalidOperationException("The world-grid effect shader is missing.");
        _meshes = new Mesh[data.Meshes.Length];
        _meshMaterials = new Material[data.Meshes.Length];
        for (int index = 0; index < data.Meshes.Length; index++)
        {
            MeshData source = data.Meshes[index];
            if (!_materials.TryGetValue(source.Texture, out Material material))
            {
                Texture2D texture = ImportTexture(source.Texture);
                material = new Material(shader) { name = source.Texture, mainTexture = texture };
                material.SetVector("_SourceTextureSize", new Vector4(texture.width, texture.height, 0, 0));
                _materials.Add(source.Texture, material);
            }

            var colors = new Color32[source.Vertices.Length];
            for (int vertex = 0; vertex < colors.Length; vertex++) { colors[vertex] = new Color32(255, 255, 255, 255); }
            var mesh = new Mesh { name = source.Name, vertices = source.Vertices, uv = source.Uv, colors32 = colors, triangles = source.Triangles };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: true);
            _meshes[index] = mesh;
            _meshMaterials[index] = material;
        }

        foreach (Clip clip in data.Clips)
        {
            if (clip.Duration <= 0f || clip.Frames.Length == 0) { throw new InvalidOperationException($"Invalid sprite animation clip: {clip.Id}."); }
            _clips.Add(clip.Id, clip);
        }
    }

    internal Material GunLineMaterial => GetGunMaterial("GunLine", Texture2D.whiteTexture);
    internal Material GunGlowMaterial
    {
        get
        {
            if (_materials.TryGetValue("GunGlow", out Material material))
            {
                return material;
            }

            return GetGunMaterial("GunGlow", ImportTexture("VFX/SpriteAnimations/GunGlow"));
        }
    }

    private Material GetGunMaterial(string key, Texture2D texture)
    {
        if (_materials.TryGetValue(key, out Material material))
        {
            return material;
        }

        Shader shader = Resources.Load<Shader>("Shaders/CellDestruction")
            ?? throw new InvalidOperationException("The world-grid effect shader is missing.");
        material = new Material(shader) { name = key, mainTexture = texture };
        material.SetVector("_SourceTextureSize", new Vector4(texture.width, texture.height, 0f, 0f));
        _materials.Add(key, material);
        return material;
    }

    internal Clip Get(string id) => _clips[id];
    internal Mesh MeshAt(int index) => _meshes[index];
    internal Material MaterialAt(int index) => _meshMaterials[index];

    private Texture2D ImportTexture(string resource)
    {
        TextAsset image = Resources.Load<TextAsset>(resource)
            ?? throw new InvalidOperationException($"Missing sprite animation texture: {resource}.");
        Texture2D source = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            image.bytes, resource, RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp,
            makeNoLongerReadable: false)
            ?? throw new InvalidOperationException($"Cannot decode sprite animation texture: {resource}.");
        try
        {
            Color32[] encoded = source.GetPixels32();
            var pixels = new Color[encoded.Length];
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 value = encoded[index];
                float alpha = value.a / 255f;
                pixels[index] = new Color(Mathf.GammaToLinearSpace(value.r / 255f * alpha),
                    Mathf.GammaToLinearSpace(value.g / 255f * alpha), Mathf.GammaToLinearSpace(value.b / 255f * alpha), alpha);
            }

            Texture2D texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(source.width, source.height,
                resource, RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _textures.Add(texture);
            return texture;
        }
        finally { UnityEngine.Object.Destroy(source); }
    }

    public void Dispose()
    {
        foreach (Mesh mesh in _meshes) { UnityEngine.Object.Destroy(mesh); }
        foreach (Material material in _materials.Values) { UnityEngine.Object.Destroy(material); }
        foreach (Texture2D texture in _textures) { UnityEngine.Object.Destroy(texture); }
    }

    [Serializable]
    internal sealed class Clip
    {
        public string Id = string.Empty;
        public float Duration = 0f;
        public bool Loop = false;
        public int SortingOrder = 0;
        public Color Color = Color.white;
        public Vector3 Scale = Vector3.one;
        public Frame[] Frames = Array.Empty<Frame>();
    }

    [Serializable]
    internal sealed class Frame
    {
        public float Time = 0f;
        public int Mesh = 0;
    }

    [Serializable]
    private sealed class MeshData
    {
        public string Name = string.Empty;
        public string Texture = string.Empty;
        public Vector3[] Vertices = Array.Empty<Vector3>();
        public Vector2[] Uv = Array.Empty<Vector2>();
        public int[] Triangles = Array.Empty<int>();
    }

    [Serializable]
    private sealed class Catalog
    {
        public MeshData[] Meshes = Array.Empty<MeshData>();
        public Clip[] Clips = Array.Empty<Clip>();
    }
}
