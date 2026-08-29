#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    public static class ItemRegistry
    {
        private const string TAG = "[ItemRegistry]";
        private static readonly Dictionary<ItemType, Texture2D> _iconCache = new();
        private static readonly HashSet<ItemType> _missingIconWarned = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            Clear();
        }

        public static string GetName(ItemType type) => type.ToString();

        public static string GetDescription(ItemType type) => string.Empty;

        public static IEnumerable<ItemType> AllTypes => (ItemType[])System.Enum.GetValues(typeof(ItemType));

        public static Texture2D? GetIcon(ItemType type)
        {
            if (_iconCache.TryGetValue(type, out var t))
            {
                return t;
            }

            var typeName = type.ToString();
            var camelName = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
            // Раньше здесь стоял Application.dataPath напрямую — в редакторе это
            // Assets/, а в плеере каталог данных, куда сборка каталог Textures
            // не кладёт. Иконки предметов молча пропадали именно в билде.
            string? itemsDir = Fodinae.Core.RuntimeAssetPaths.TexturesSubfolder("Items");
            if (itemsDir == null)
            {
                return null;
            }

            var path = Path.Combine(itemsDir, camelName + ".png");
            if (!File.Exists(path))
            {
                path = Path.Combine(itemsDir, typeName.ToLowerInvariant() + ".png");
            }

            if (!File.Exists(path))
            {
                if (_missingIconWarned.Add(type))
                {
                    Debug.Log($"{TAG} No local icon for item type '{type}' (searched {camelName}.png), will use server texture if available");
                }

                return null;
            }

            Texture2D tex;
            try
            {
                tex = RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                    File.ReadAllBytes(path),
                    $"ItemIcon_{type}",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    makeNoLongerReadable: true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{TAG} Local icon '{path}' for item type '{type}' is corrupt; " +
                    $"will use the server texture if available. {exception.Message}");
                return null;
            }

            _iconCache[type] = tex;
            return tex;
        }

        public static void Clear()
        {
            // Inventory views can retain these runtime textures across a domain
            // reload. Reset lookup state without destroying live UI resources.
            _iconCache.Clear();
            _missingIconWarned.Clear();
        }
    }
}
