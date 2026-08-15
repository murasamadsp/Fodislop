#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Core
{
    public static class SharedMaterialCache
    {
        private static readonly Dictionary<Texture2D, Material> _materials = new();
        private static Shader? _shader;

        private static Shader Shader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find("Sprites/Default") ??
                        throw new InvalidOperationException(
                            "SharedMaterialCache requires the supported 'Sprites/Default' shader.");
                }

                return _shader;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Clear();
            _shader = null;
        }

        public static Material? GetForTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            if (_materials.TryGetValue(texture, out var mat))
            {
                return mat;
            }

            mat = new Material(Shader);
            mat.mainTexture = texture;
            _materials[texture] = mat;
            return mat;
        }

        public static void Clear()
        {
            foreach (var mat in _materials.Values)
            {
                if (mat != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(mat);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(mat);
                    }
                }
            }

            _materials.Clear();
        }
    }
}
