#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Kern.Editor;

// Снимок того, куда уходит кадр, без окон игры.
//
// Пункт меню на RecordSeconds включает встроенный профайлер, затем усредняет
// дерево главного потока по всем записанным кадрам: собственное и полное
// время каждого маркера. Файл пишется в Logs/frame_profile_*.txt. Большое
// ожидание вывода при малой работе потока значит, что кадр держит видеокарта.
internal static class FrameProfileCapture
{
    private const double RecordSeconds = 3;
    private const int TopMarkers = 45;
    private const int MaxDepth = 12;

    private static double _stopAt = -1;

    [MenuItem("Kern/Diagnostics/Capture Frame Profile (3 s)")]
    private static void Start()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[FrameProfileCapture] Нужен Play.");
            return;
        }

        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.enabled = true;
        _stopAt = EditorApplication.timeSinceStartup + RecordSeconds;
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        Debug.Log($"[FrameProfileCapture] Пишу {RecordSeconds:F0} с…");
    }

    private static void Update()
    {
        if (_stopAt < 0 || EditorApplication.timeSinceStartup < _stopAt)
        {
            return;
        }

        _stopAt = -1;
        EditorApplication.update -= Update;
        ProfilerDriver.enabled = false;

        string report;
        try
        {
            report = Analyze();
        }
        catch (Exception exception)
        {
            report = $"Разбор упал: {exception}";
        }

        string directory = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(directory);
        string path = Path.GetFullPath(Path.Combine(directory, $"frame_profile_{DateTime.Now:yyyyMMdd_HHmmss}.txt"));
        File.WriteAllText(path, report, new UTF8Encoding(false));
        Debug.Log($"[FrameProfileCapture] Готово: {path}");
    }

    private static string Analyze()
    {
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        var self = new Dictionary<string, double>();
        var total = new Dictionary<string, double>();
        var children = new List<int>();
        double frameSum = 0;
        int frames = 0;

        for (int frame = first; frame >= 0 && frame <= last; frame++)
        {
            using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(
                frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                HierarchyFrameDataView.columnTotalTime, false);
            if (!view.valid)
            {
                continue;
            }

            frames++;
            frameSum += view.frameTimeMs;
            Accumulate(view, view.GetRootItemID(), 0, self, total, children);
        }

        var report = new StringBuilder(8192);
        report.Append("Профиль кадра, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height).AppendLine();
        if (frames == 0)
        {
            report.AppendLine("Кадров не записано.");
            return report.ToString();
        }

        report.Append("Кадров ").Append(frames).Append(", средний кадр главного потока ")
            .Append((frameSum / frames).ToString("F2")).AppendLine(" мс").AppendLine();

        report.AppendLine("== ПО СОБСТВЕННОМУ ВРЕМЕНИ (мс на кадр) ==");
        foreach (KeyValuePair<string, double> entry in self.OrderByDescending(e => e.Value).Take(TopMarkers))
        {
            report.Append((entry.Value / frames).ToString("F3")).Append("  ").AppendLine(entry.Key);
        }

        report.AppendLine().AppendLine("== ПО ПОЛНОМУ ВРЕМЕНИ (мс на кадр) ==");
        foreach (KeyValuePair<string, double> entry in total.OrderByDescending(e => e.Value).Take(TopMarkers))
        {
            report.Append((entry.Value / frames).ToString("F3")).Append("  ").AppendLine(entry.Key);
        }

        return report.ToString();
    }

    private static void Accumulate(
        HierarchyFrameDataView view,
        int item,
        int depth,
        Dictionary<string, double> self,
        Dictionary<string, double> total,
        List<int> scratch)
    {
        scratch.Clear();
        view.GetItemChildren(item, scratch);
        int[] children = scratch.ToArray();
        foreach (int child in children)
        {
            string name = view.GetItemName(child);
            self[name] = self.GetValueOrDefault(name) + view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
            total[name] = total.GetValueOrDefault(name) + view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            if (depth < MaxDepth)
            {
                Accumulate(view, child, depth + 1, self, total, scratch);
            }
        }
    }
}
