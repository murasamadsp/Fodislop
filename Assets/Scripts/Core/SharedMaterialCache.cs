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
                    _shader = Shader.Find("Fodinae/World Entity") ??
                        throw new InvalidOperationException(
                            "SharedMaterialCache requires the supported 'Fodinae/World Entity' shader.");
                }

                return _shader;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            // Runtime renderers survive script-domain reloads and keep their
            // sharedMaterial references. Destroying those materials here leaves
            // the restored renderers bound to Unity fake-null objects.
            _materials.Clear();
            _shader = null;
        }

        public static Material GetForTexture(Texture2D texture)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }

            if (_materials.TryGetValue(texture, out var mat))
            {
                return mat;
            }

            mat = new Material(Shader)
            {
                name = $"Shared Sprite Material ({texture.name})",
                hideFlags = HideFlags.DontSave,
                mainTexture = texture,
            };
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
