#nullable enable

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fodinae.Core
{
    /// <summary>
    /// Единственное место, где решается, откуда игра читает текстуры с диска.
    ///
    /// Часть текстур загружается не через AssetDatabase, а файлами в рантайме:
    /// иконки предметов, тайлы клеток, ассеты, присланные сервером. Путь к ним
    /// в редакторе и в собранном плеере разный, а раньше его независимо угадывали
    /// три подсистемы — TextureStorageManager, MainMenu и ItemRegistry, — каждая
    /// своим списком кандидатов. Из-за этого сборка раскладывала копию каталога
    /// Textures сразу в четыре места, чтобы попасть хоть в один из списков: около
    /// 29 МБ × 4 в каждом билде.
    ///
    /// Теперь корень один и вычисляется один раз. Сборка кладёт текстуры ровно в
    /// StreamingAssets, редактор читает их прямо из Assets.
    /// </summary>
    public static class RuntimeAssetPaths
    {
        private const string TexturesFolderName = "Textures";

        private static string? _texturesRoot;
        private static bool _resolved;

        /// <summary>
        /// Каталог с текстурами, либо null, если его нет ни по одному пути.
        /// Null здесь — не ошибка: часть текстур приходит с сервера, и
        /// вызывающий код обязан уметь работать без локальных файлов.
        /// </summary>
        public static string? TexturesRoot
        {
            get
            {
                if (_resolved)
                {
                    return _texturesRoot;
                }

                _texturesRoot = Resolve();
                _resolved = true;

                if (_texturesRoot == null)
                {
                    Debug.LogWarning(
                        "[RuntimeAssetPaths] Каталог Textures не найден ни по одному пути. " +
                        "В собранном плеере это означает, что сборка не скопировала его в " +
                        "StreamingAssets — проверь BuildScript.CopyRuntimeTextures.");
                }

                return _texturesRoot;
            }
        }

        /// <summary>Путь к подкаталогу внутри корня текстур, либо null.</summary>
        public static string? TexturesSubfolder(string name)
        {
            string? root = TexturesRoot;
            if (root == null)
            {
                return null;
            }

            string path = Path.Combine(root, name);
            return Directory.Exists(path) ? path : null;
        }

        private static string? Resolve()
        {
            // Порядок значим. StreamingAssets стоит первым, потому что это
            // единственное место, куда кладёт файлы сборка; остальные пункты —
            // редактор и совместимость со старыми билдами, где каталог лежал
            // рядом с данными плеера.
            IEnumerable<string> candidates = new[]
            {
                Path.Combine(Application.streamingAssetsPath, TexturesFolderName),
                Path.Combine(Application.dataPath, TexturesFolderName),
                Path.Combine(Application.dataPath, "Resources", "Data", TexturesFolderName),
                Path.Combine(Application.dataPath, "..", TexturesFolderName),
            };

            foreach (string candidate in candidates)
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // Невалидный относительный путь — просто следующий кандидат.
                }
            }

            return null;
        }
    }
}
