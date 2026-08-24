#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

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
        /// Makes archived StreamingAssets available through ordinary file APIs.
        /// Android stores them inside the APK, while the texture decoders
        /// require seekable files. Other supported platforms need no copy.
        /// </summary>
        public static async UniTask EnsureReadyAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string extractedRoot = Path.Combine(
                Application.persistentDataPath,
                "BundledAssets",
                TexturesFolderName);
            string markerPath = Path.Combine(extractedRoot, ".manifest");
            string manifest = await DownloadTextAsync(
                CombineStreamingUri(Application.streamingAssetsPath, "Textures.manifest"));
            if (!File.Exists(markerPath) ||
                !string.Equals(await File.ReadAllTextAsync(markerPath), manifest, System.StringComparison.Ordinal))
            {
                if (Directory.Exists(extractedRoot))
                {
                    Directory.Delete(extractedRoot, recursive: true);
                }

                Directory.CreateDirectory(extractedRoot);
                string[] relativeFiles = manifest
                    .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => path.Length > 0)
                    .ToArray();
                foreach (string relativeFile in relativeFiles)
                {
                    string destination = Path.Combine(
                        extractedRoot,
                        relativeFile.Replace('/', Path.DirectorySeparatorChar));
                    string? directory = Path.GetDirectoryName(destination);
                    if (directory == null)
                    {
                        throw new InvalidDataException(
                            $"Bundled texture has no destination directory: {relativeFile}");
                    }

                    Directory.CreateDirectory(directory);
                    byte[] bytes = await DownloadBytesAsync(
                        CombineStreamingUri(
                            Application.streamingAssetsPath,
                            $"Textures/{relativeFile}"));
                    await File.WriteAllBytesAsync(destination, bytes);
                }

                await File.WriteAllTextAsync(markerPath, manifest);
            }

            _texturesRoot = extractedRoot;
            _resolved = true;
#else
            await UniTask.CompletedTask;
#endif
        }

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

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async UniTask<string> DownloadTextAsync(string uri)
        {
            byte[] bytes = await DownloadBytesAsync(uri);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static async UniTask<byte[]> DownloadBytesAsync(string uri)
        {
            using UnityWebRequest request = UnityWebRequest.Get(uri);
            await request.SendWebRequest().ToUniTask();
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException(
                    $"Failed to extract required bundled asset '{uri}': {request.error}");
            }

            return request.downloadHandler.data;
        }

        private static string CombineStreamingUri(string root, string relativePath)
        {
            string encodedPath = string.Join(
                "/",
                relativePath.Split('/').Select(System.Uri.EscapeDataString));
            return $"{root.TrimEnd('/')}/{encodedPath}";
        }
#endif
    }
}
