#nullable enable

using System;
using UnityEngine;

namespace Kern.World
{
    [CreateAssetMenu(menuName = "Kern/World/Surface Scene")]
    public sealed class SurfaceSceneProfile : ScriptableObject
    {
        public const string ResourcePath = "Surface/SurfaceScene";

        [SerializeField] private Shader _shader = null!;
        [SerializeField] private Texture2D[] _distantRidges = Array.Empty<Texture2D>();
        [SerializeField] private Texture2D _basalt = null!;
        [SerializeField] private Texture2D _moon = null!;
        [SerializeField] private int _seed = 1132;
        [SerializeField] private float _segmentWidthCells = 32f;
        [SerializeField] private Color _atmosphereColor = new(0.56f, 0.51f, 0.23f, 1f);
        [SerializeField] private Color _rockColor = new(0.28f, 0.30f, 0.23f, 1f);
        [SerializeField] private Color _crackColor = new(1f, 0.075f, 0.012f, 1f);
        [SerializeField] private float _crackRadiance = 3f;
        [SerializeField] private float _atmosphereDensity = 0.6f;

        public Shader Shader => _shader;
        public Texture2D[] DistantRidges => _distantRidges;
        public Texture2D Basalt => _basalt;
        public Texture2D Moon => _moon;
        public int Seed => _seed;
        public float SegmentWidthCells => _segmentWidthCells;
        public Color AtmosphereColor => _atmosphereColor;
        public Color RockColor => _rockColor;
        public Color CrackColor => _crackColor;
        public float CrackRadiance => _crackRadiance;
        public float AtmosphereDensity => _atmosphereDensity;

        public void Validate()
        {
            if (_shader == null || !_shader.isSupported || _basalt == null || _moon == null ||
                _distantRidges.Length != 3 || Array.Exists(_distantRidges, texture => texture == null))
            {
                throw new InvalidOperationException("Surface scene requires its supported shader, three ridge textures, basalt and moon.");
            }

            if (!float.IsFinite(_segmentWidthCells) || _segmentWidthCells <= 0f ||
                !float.IsFinite(_crackRadiance) || _crackRadiance < 0f ||
                !float.IsFinite(_atmosphereDensity) || _atmosphereDensity < 0f)
            {
                throw new InvalidOperationException("Surface scene has invalid cell dimensions or radiance/density.");
            }
        }
    }
}
