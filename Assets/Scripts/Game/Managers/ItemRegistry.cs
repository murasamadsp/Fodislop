using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public static class ItemRegistry
    {
        private static readonly Dictionary<ItemType, Texture2D> _iconCache = new();

        public static string GetName(ItemType type) => type.ToString();

        public static string GetDescription(ItemType type) => string.Empty;

        public static IEnumerable<ItemType> AllTypes => (ItemType[])System.Enum.GetValues(typeof(ItemType));

        public static Texture2D GetIcon(ItemType type)
        {
            if (_iconCache.TryGetValue(type, out var t)) return t;
            var path = Application.dataPath + "/Textures/items/" + type.ToString().ToLower() + ".png";
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            _iconCache[type] = tex;
            return tex;
        }
    }
}
