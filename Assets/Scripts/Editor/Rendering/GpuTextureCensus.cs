#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Fodinae.Editor;

// Перепись всех живых текстур процесса редактора.
//
// Графика редактора пухнет до гигабайт одинаковыми пачками: что-то создаётся
// заново на каждый вход в Play или перезагрузку домена и не уничтожается.
// FindObjectsOfTypeAll видит и осиротевшие объекты, на которые уже никто не
// ссылается, поэтому группировка по имени, размеру и формату сразу называет
// утёкший ресурс и число его копий.
internal static class GpuTextureCensus
{
    private readonly record struct Key(string Type, string Name, int Width, int Height, string Format, bool Asset, HideFlags Flags);

    [MenuItem("Fodinae/Diagnostics/Dump Live Textures")]
    private static void Dump()
    {
        var groups = new Dictionary<Key, (int Count, long Bytes)>();
        long total = 0;
        int objects = 0;
        // Не только текстуры: вершинные и индексные буферы мешей тоже лежат
        // в графической памяти и в переписи текстур не видны.
        foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
        {
            int width = 0;
            int height = 0;
            string format = "";
            switch (item)
            {
                case RenderTexture rt:
                    (width, height, format) = (rt.width, rt.height, $"{rt.graphicsFormat}/d{rt.depthStencilFormat}");
                    break;
                case Texture texture:
                    (width, height, format) = (texture.width, texture.height, texture.graphicsFormat.ToString());
                    break;
                case Mesh mesh:
                    (width, height, format) = (mesh.vertexCount, (int)mesh.GetIndexCount(0), $"streams {mesh.vertexBufferCount}");
                    break;
            }

            var key = new Key(
                item.GetType().Name,
                string.IsNullOrEmpty(item.name) ? "(без имени)" : item.name,
                width,
                height,
                format,
                EditorUtility.IsPersistent(item),
                item.hideFlags);
            long bytes = Profiler.GetRuntimeMemorySizeLong(item);
            groups.TryGetValue(key, out (int Count, long Bytes) group);
            groups[key] = (group.Count + 1, group.Bytes + bytes);
            total += bytes;
            objects++;
        }

        var sorted = new List<KeyValuePair<Key, (int Count, long Bytes)>>(groups);
        sorted.Sort(static (left, right) => right.Value.Bytes.CompareTo(left.Value.Bytes));

        var report = new StringBuilder(16384);
        report.Append("Живые текстуры, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(EditorApplication.isPlaying ? ", Play" : ", редактор")
            .Append(": ").Append(objects).Append(" объектов, ").Append((total / 1048576d).ToString("F0")).AppendLine(" МБ")
            .AppendLine("МБ всего | копий | тип | имя | размер | формат | ассет | hideFlags");
        foreach ((Key key, (int count, long bytes)) in sorted)
        {
            if (bytes < 1048576 && count < 20)
            {
                continue;
            }

            report.Append((bytes / 1048576d).ToString("F1")).Append(" | ").Append(count).Append(" | ")
                .Append(key.Type).Append(" | ").Append(key.Name).Append(" | ")
                .Append(key.Width).Append('×').Append(key.Height).Append(" | ")
                .Append(key.Format).Append(" | ").Append(key.Asset ? "да" : "нет").Append(" | ")
                .Append(key.Flags).AppendLine();
        }

        string directory = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, $"textures_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Debug.Log($"[GpuTextureCensus] Готово: {path}");
    }
}
