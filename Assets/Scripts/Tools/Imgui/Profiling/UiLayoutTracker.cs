#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Fodinae.Tools.Imgui.Profiling;

// Кто заставляет интерфейс игры перераскладываться.
//
// Маркер PanelSettings.ValidateLayout говорит «сколько», но не «из-за кого».
// Здесь на каждый элемент игровых UIDocument вешается GeometryChangedEvent:
// оно приходит после раскладки и только тем, у кого сменился размер или
// позиция. Изменения, накопленные между двумя Tick, относятся к одному кадру
// и сравниваются со временем раскладки этого кадра.
//
// Каждое изменение пишется в журнал Logs/ui_layout_*.tsv, а по кнопке из
// накопленного собирается список правок Logs/ui_layout_todo.md.
public sealed class UiLayoutTracker : IDisposable
{
    public const double SpikeMilliseconds = 2.0;
    private const float RescanSeconds = 1f;
    private const float FlushSeconds = 1f;
    private const int PathDepth = 4;
    private const int FullPathDepth = 64;

    public sealed class Stat
    {
        public string Label = string.Empty;
        public string FullPath = string.Empty;
        public string ElementType = string.Empty;
        public int Changes;
        public int SizeChanges;
        public int PositionOnlyChanges;
        public int SpikeChanges;
        public int FirstFrame;
        public int LastFrame;
        public Rect LastRect;
        public float MinWidth = float.MaxValue;
        public float MaxWidth;
        public float MinHeight = float.MaxValue;
        public float MaxHeight;
        public readonly List<string> TextSamples = [];
    }

    private readonly struct Change
    {
        public Change(VisualElement element, Rect oldRect, Rect newRect, string? text)
        {
            Element = element;
            OldRect = oldRect;
            NewRect = newRect;
            Text = text;
        }

        public VisualElement Element { get; }

        public Rect OldRect { get; }

        public Rect NewRect { get; }

        public string? Text { get; }
    }

    private readonly HashSet<VisualElement> _registered = [];
    private readonly Dictionary<VisualElement, Stat> _stats = [];
    private readonly List<Change> _pending = [];
    private readonly HashSet<VisualElement> _pendingSet = [];
    private readonly List<VisualElement> _scratch = [];
    private readonly List<Stat> _sorted = [];
    private readonly EventCallback<GeometryChangedEvent> _onGeometryChanged;
    private readonly StringBuilder _label = new(256);
    private readonly StringBuilder _line = new(512);

    private ProfilerRecorder _layoutRecorder;
    private StreamWriter? _log;
    private float _nextRescan;
    private float _nextFlush;
    private bool _running;

    public UiLayoutTracker()
    {
        _onGeometryChanged = OnGeometryChanged;
    }

    public static string LogDirectory =>
        Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs")
            : Path.Combine(Application.persistentDataPath, "Logs");

    public string? LogPath { get; private set; }

    public string? LastTodoPath { get; private set; }

    public int ElementCount => _registered.Count;

    public int Frames { get; private set; }

    public int SpikeFrames { get; private set; }

    public int ChangedInLastFrame { get; private set; }

    public int PeakChangedInFrame { get; private set; }

    public double LastLayoutMilliseconds { get; private set; }

    public double PeakLayoutMilliseconds { get; private set; }

    public long TotalChanges { get; private set; }

    public bool LayoutMarkerAvailable => _layoutRecorder.Valid;

    public IReadOnlyList<Stat> Sorted => _sorted;

    public void Start()
    {
        _running = true;
        _nextRescan = 0f;
        OpenLog();
    }

    private void OpenLog()
    {
        if (_log != null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            LogPath = Path.Combine(LogDirectory, $"ui_layout_{DateTime.Now:yyyyMMdd_HHmmss}.tsv");
            _log = new StreamWriter(LogPath, append: false, new UTF8Encoding(false));
            _log.WriteLine("frame\ttime_s\tlayout_ms\tspike\tchanged_in_frame\tkind\tpath\told_x\told_y\told_w\told_h\tnew_x\tnew_y\tnew_w\tnew_h\ttext");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UiLayoutTracker] Журнал раскладки не открыт: {exception.Message}");
            _log = null;
            LogPath = null;
        }
    }

    // Каждый кадр, пока окно открыто.
    public void Tick()
    {
        if (!_running)
        {
            return;
        }

        if (!_layoutRecorder.Valid && MarkerDirectory.TryGet("PanelSettings.ValidateLayout", out MarkerInfo info))
        {
            _layoutRecorder = new ProfilerRecorder(
                info.Handle,
                1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
            _layoutRecorder.Start();
        }

        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + RescanSeconds;
            Rescan();
        }

        CloseFrame();

        if (_log != null && Time.unscaledTime >= _nextFlush)
        {
            _nextFlush = Time.unscaledTime + FlushSeconds;
            _log.Flush();
        }
    }

    // Изменения, пришедшие после прошлого Tick, раскладывались в прошлом кадре;
    // рекордер к этому моменту тоже отдаёт прошлый кадр.
    private void CloseFrame()
    {
        double layout = _layoutRecorder.Valid ? _layoutRecorder.LastValue * 1e-6 : 0d;
        bool spike = layout >= SpikeMilliseconds;
        int frame = Time.frameCount - 1;
        Frames++;
        LastLayoutMilliseconds = layout;
        PeakLayoutMilliseconds = Math.Max(PeakLayoutMilliseconds, layout);
        ChangedInLastFrame = _pending.Count;
        PeakChangedInFrame = Math.Max(PeakChangedInFrame, _pending.Count);
        if (spike)
        {
            SpikeFrames++;
        }

        foreach (Change change in _pending)
        {
            if (!_stats.TryGetValue(change.Element, out Stat? stat))
            {
                continue;
            }

            if (spike)
            {
                stat.SpikeChanges++;
            }

            WriteLine(frame, layout, spike, _pending.Count, change, stat);
        }

        _pending.Clear();
        _pendingSet.Clear();
    }

    private void WriteLine(int frame, double layout, bool spike, int changedInFrame, in Change change, Stat stat)
    {
        if (_log == null)
        {
            return;
        }

        CultureInfo invariant = CultureInfo.InvariantCulture;
        bool sized = change.OldRect.size != change.NewRect.size;
        _line.Clear();
        _line.Append(frame).Append('\t')
            .Append(Time.unscaledTime.ToString("F3", invariant)).Append('\t')
            .Append(layout.ToString("F3", invariant)).Append('\t')
            .Append(spike ? 1 : 0).Append('\t')
            .Append(changedInFrame).Append('\t')
            .Append(sized ? "size" : "position").Append('\t')
            .Append(stat.FullPath).Append('\t');
        AppendRect(change.OldRect, invariant);
        AppendRect(change.NewRect, invariant);
        _line.Append(Sanitize(change.Text));
        _log.WriteLine(_line.ToString());
    }

    private void AppendRect(Rect rect, CultureInfo invariant)
    {
        _line.Append(rect.x.ToString("F1", invariant)).Append('\t')
            .Append(rect.y.ToString("F1", invariant)).Append('\t')
            .Append(rect.width.ToString("F1", invariant)).Append('\t')
            .Append(rect.height.ToString("F1", invariant)).Append('\t');
    }

    private static string Sanitize(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.target is not VisualElement element)
        {
            return;
        }

        if (!_stats.TryGetValue(element, out Stat? stat))
        {
            stat = new Stat
            {
                Label = Describe(element, PathDepth),
                FullPath = Describe(element, FullPathDepth),
                ElementType = element.GetType().Name,
                FirstFrame = Time.frameCount,
            };
            _stats[element] = stat;
        }

        Rect newRect = evt.newRect;
        bool sized = evt.oldRect.size != newRect.size;
        stat.Changes++;
        if (sized)
        {
            stat.SizeChanges++;
        }
        else
        {
            stat.PositionOnlyChanges++;
        }

        stat.LastFrame = Time.frameCount;
        stat.LastRect = newRect;
        stat.MinWidth = Mathf.Min(stat.MinWidth, newRect.width);
        stat.MaxWidth = Mathf.Max(stat.MaxWidth, newRect.width);
        stat.MinHeight = Mathf.Min(stat.MinHeight, newRect.height);
        stat.MaxHeight = Mathf.Max(stat.MaxHeight, newRect.height);

        string? text = element is TextElement textElement ? textElement.text : null;
        if (!string.IsNullOrEmpty(text) && stat.TextSamples.Count < 5 && !stat.TextSamples.Contains(text!))
        {
            stat.TextSamples.Add(text!);
        }

        TotalChanges++;
        if (_pendingSet.Add(element))
        {
            _pending.Add(new Change(element, evt.oldRect, newRect, text));
        }
    }

    private void Rescan()
    {
        // Отцепиться от ушедших из панели элементов, чтобы не держать их.
        _scratch.Clear();
        foreach (VisualElement element in _registered)
        {
            if (element.panel == null)
            {
                _scratch.Add(element);
            }
        }

        foreach (VisualElement element in _scratch)
        {
            element.UnregisterCallback(_onGeometryChanged);
            _registered.Remove(element);
        }

        foreach (UIDocument document in CollectDocuments())
        {
            if (document.rootVisualElement != null)
            {
                Register(document.rootVisualElement);
            }
        }

        CollectRenderStats();
    }

    // ── Состав видимого интерфейса ────────────────────────────────────────
    //
    // Отрисовка UI Toolkit стоит по числу видимых элементов, текстов и
    // разрывов пакетов: разных текстур (слотов 8, в атлас идут только
    // картинки до 64 px) и обрезающих контейнеров. Счёт идёт по живому
    // дереву раз в секунду, спрятанные поддеревья (display: none) не
    // заходятся.

    // Классы, у которых в USS стоит overflow: hidden. resolvedStyle не
    // отдаёт overflow, поэтому обрезка узнаётся по классу; видимая область
    // ScrollView обрезает всегда.
    private static readonly HashSet<string> _ClipClasses = new(StringComparer.Ordinal)
    {
        "ui-panel", "ui-scroll-viewport", "hud-minimap-container", "sci-fi-bar-track",
        "world-labels", "chat-message", "gchat-message", "fit-clip", "fit-clamp", "fit-shrink",
        "mm-root", "mm-loader-progress-track", "mm-ticker-text", "mm-settings-layout",
        "unity-scroll-view__content-viewport",
    };

    public sealed class RenderStats
    {
        public int Visible;
        public int Texts;
        public int TextCharacters;
        public int OutlinedTexts;
        public int ShadowedTexts;
        public int Clips;
        public int RoundedClips;
        public int Translucent;
        public int Translated;
        public int Images;
        public int AtlasLimit = 64;
        public readonly Dictionary<Texture, int> Textures = [];
        public readonly Dictionary<string, int> VisibleBySubtree = new(StringComparer.Ordinal);
    }

    private readonly List<(VisualElement Element, string Subtree)> _statsStack = [];

    public RenderStats Stats { get; private set; } = new();

    private readonly List<GameObject> _sceneRoots = [];
    private readonly List<UIDocument> _documents = [];
    private readonly List<UIDocument> _documentScratch = [];

    // Документы собираются обходом корней загруженных сцен: поиск по всем
    // объектам (FindObjectsByType) запрещён (FOD-FORBIDDEN-API).
    private List<UIDocument> CollectDocuments()
    {
        _documents.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }

            scene.GetRootGameObjects(_sceneRoots);
            foreach (GameObject root in _sceneRoots)
            {
                root.GetComponentsInChildren(includeInactive: false, _documentScratch);
                foreach (UIDocument document in _documentScratch)
                {
                    if (document.isActiveAndEnabled)
                    {
                        _documents.Add(document);
                    }
                }
            }
        }

        return _documents;
    }

    private void CollectRenderStats()
    {
        var stats = new RenderStats();
        foreach (UIDocument document in CollectDocuments())
        {
            if (document.panelSettings != null)
            {
                stats.AtlasLimit = Math.Max(stats.AtlasLimit, document.panelSettings.dynamicAtlasSettings.maxSubTextureSize);
            }

            VisualElement? root = document.rootVisualElement;
            if (root == null)
            {
                continue;
            }

            _statsStack.Clear();
            _statsStack.Add((root, "(корень)"));
            while (_statsStack.Count > 0)
            {
                (VisualElement element, string subtree) = _statsStack[^1];
                _statsStack.RemoveAt(_statsStack.Count - 1);

                IResolvedStyle style = element.resolvedStyle;
                if (style.display == DisplayStyle.None ||
                    style.visibility == Visibility.Hidden ||
                    style.opacity <= 0f)
                {
                    continue;
                }

                stats.Visible++;
                stats.VisibleBySubtree.TryGetValue(subtree, out int inSubtree);
                stats.VisibleBySubtree[subtree] = inSubtree + 1;

                if (style.opacity < 1f)
                {
                    stats.Translucent++;
                }

                if (style.translate.x != 0f || style.translate.y != 0f)
                {
                    stats.Translated++;
                }

                if (element is TextElement text && !string.IsNullOrEmpty(text.text))
                {
                    stats.Texts++;
                    stats.TextCharacters += text.text.Length;
                    if (style.unityTextOutlineWidth > 0f)
                    {
                        stats.OutlinedTexts++;
                    }

                    if (style.textShadow.color.a > 0f)
                    {
                        stats.ShadowedTexts++;
                    }
                }

                Texture? texture = style.backgroundImage.texture != null
                    ? style.backgroundImage.texture
                    : style.backgroundImage.sprite != null
                        ? style.backgroundImage.sprite.texture
                        : style.backgroundImage.renderTexture;
                if (element is Image image && image.image != null)
                {
                    texture = image.image;
                }

                if (texture != null)
                {
                    stats.Images++;
                    stats.Textures.TryGetValue(texture, out int uses);
                    stats.Textures[texture] = uses + 1;
                }

                foreach (string className in element.GetClasses())
                {
                    if (!_ClipClasses.Contains(className))
                    {
                        continue;
                    }

                    stats.Clips++;
                    if (style.borderTopLeftRadius > 0f || style.borderTopRightRadius > 0f ||
                        style.borderBottomLeftRadius > 0f || style.borderBottomRightRadius > 0f)
                    {
                        stats.RoundedClips++;
                    }

                    break;
                }

                for (int i = 0; i < element.hierarchy.childCount; i++)
                {
                    VisualElement child = element.hierarchy[i];
                    // Обёртки без своего имени проходятся насквозь: панель
                    // называется по первому осмысленному элементу под ними.
                    bool wrapper = element == root ||
                        subtree == "(корень)" ||
                        subtree.StartsWith("TemplateContainer", StringComparison.Ordinal) ||
                        subtree == "#UIDocument-container";
                    string childSubtree = wrapper ? Short(child) : subtree;
                    _statsStack.Add((child, childSubtree));
                }
            }
        }

        Stats = stats;
    }

    private void Register(VisualElement root)
    {
        _scratch.Clear();
        _scratch.Add(root);
        while (_scratch.Count > 0)
        {
            VisualElement element = _scratch[^1];
            _scratch.RemoveAt(_scratch.Count - 1);
            if (_registered.Add(element))
            {
                element.RegisterCallback(_onGeometryChanged);
            }

            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                _scratch.Add(element.hierarchy[i]);
            }
        }
    }

    // Строки собираются в Tick окна, а не в OnGUI.
    public void Rebuild(int keep)
    {
        SortInto(_sorted);
        if (_sorted.Count > keep)
        {
            _sorted.RemoveRange(keep, _sorted.Count - keep);
        }
    }

    private void SortInto(List<Stat> target)
    {
        target.Clear();
        foreach (Stat stat in _stats.Values)
        {
            target.Add(stat);
        }

        target.Sort(static (left, right) =>
        {
            int bySpike = right.SpikeChanges.CompareTo(left.SpikeChanges);
            return bySpike != 0 ? bySpike : right.Changes.CompareTo(left.Changes);
        });
    }

    public string? WriteTodo()
    {
        _log?.Flush();
        var all = new List<Stat>(_stats.Count);
        SortInto(all);

        var md = new StringBuilder(16384);
        md.AppendLine("# Перераскладки интерфейса — что править")
            .AppendLine()
            .Append("Снято: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append(Application.isEditor ? ", редактор" : ", сборка")
            .Append(", ").Append(Screen.width).Append('×').Append(Screen.height).AppendLine()
            .AppendLine()
            .Append("- кадров под наблюдением: ").Append(Frames).AppendLine()
            .Append("- кадров с раскладкой ≥ ").Append(SpikeMilliseconds.ToString("F0")).Append(" мс: ").Append(SpikeFrames).AppendLine()
            .Append("- пик раскладки: ").Append(PeakLayoutMilliseconds.ToString("F2")).AppendLine(" мс")
            .Append("- элементов под наблюдением: ").Append(ElementCount).AppendLine()
            .Append("- изменений геометрии: ").Append(TotalChanges).AppendLine()
            .Append("- полный журнал: `").Append(LogPath ?? "не записан").AppendLine("`")
            .AppendLine();

        if (all.Count == 0)
        {
            md.AppendLine("Ни один элемент не менял геометрию.");
        }

        int index = 0;
        foreach (Stat stat in all)
        {
            index++;
            double perFrame = Frames > 0 ? (double)stat.Changes / Frames : 0d;
            md.Append("## ").Append(index).Append(". ").AppendLine(stat.Label)
                .AppendLine()
                .Append("- [ ] ").AppendLine(Advice(stat))
                .Append("- путь: `").Append(stat.FullPath).AppendLine("`")
                .Append("- тип: ").AppendLine(stat.ElementType)
                .Append("- изменений: ").Append(stat.Changes)
                .Append(" (").Append(perFrame.ToString("P1")).Append(" кадров), размер ").Append(stat.SizeChanges)
                .Append(", только позиция ").Append(stat.PositionOnlyChanges)
                .Append(", в пиковых кадрах ").Append(stat.SpikeChanges).AppendLine()
                .Append("- ширина ").Append(stat.MinWidth.ToString("F0")).Append("…").Append(stat.MaxWidth.ToString("F0"))
                .Append(", высота ").Append(stat.MinHeight.ToString("F0")).Append("…").Append(stat.MaxHeight.ToString("F0"))
                .AppendLine();
            if (stat.TextSamples.Count > 0)
            {
                md.Append("- тексты: ");
                for (int i = 0; i < stat.TextSamples.Count; i++)
                {
                    md.Append(i > 0 ? ", " : string.Empty).Append('«').Append(Sanitize(stat.TextSamples[i])).Append('»');
                }

                md.AppendLine();
            }

            md.AppendLine();
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, "ui_layout_todo.md");
            File.WriteAllText(path, md.ToString(), new UTF8Encoding(false));
            LastTodoPath = path;
            return path;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UiLayoutTracker] TODO раскладки не записан: {exception.Message}");
            return null;
        }
    }

    private static string Advice(Stat stat)
    {
        if (stat.SizeChanges > 0 && stat.TextSamples.Count > 0)
        {
            return "Размер зависит от текста: задать фиксированную ширину или min-width под самый длинный текст, " +
                "цифры — моноширинными, чтобы смена значения не перераскладывала соседей.";
        }

        if (stat.SizeChanges == 0)
        {
            return "Меняется только позиция: двигать через style.translate, а не left/top/margin — " +
                "translate не запускает раскладку.";
        }

        return "Меняется размер без текста: найти, кто пишет width/height/display/flex этому элементу " +
            "или его детям, и писать только при реальном изменении значения.";
    }

    public void ResetStats()
    {
        _stats.Clear();
        _sorted.Clear();
        _pending.Clear();
        _pendingSet.Clear();
        Frames = 0;
        SpikeFrames = 0;
        PeakChangedInFrame = 0;
        PeakLayoutMilliseconds = 0d;
        TotalChanges = 0;
    }

    private string Describe(VisualElement element, int depthLimit)
    {
        _label.Clear();
        VisualElement? current = element;
        for (int depth = 0; depth < depthLimit && current != null; depth++)
        {
            if (depth > 0)
            {
                _label.Insert(0, " / ");
            }

            _label.Insert(0, Short(current));
            current = current.hierarchy.parent;
        }

        if (current != null)
        {
            _label.Insert(0, "… / ");
        }

        return _label.ToString();
    }

    private static string Short(VisualElement element)
    {
        if (!string.IsNullOrEmpty(element.name))
        {
            return "#" + element.name;
        }

        foreach (string className in element.GetClasses())
        {
            return element.GetType().Name + "." + className;
        }

        return element.GetType().Name;
    }

    public void Stop()
    {
        _running = false;
        foreach (VisualElement element in _registered)
        {
            element.UnregisterCallback(_onGeometryChanged);
        }

        _registered.Clear();
        _pending.Clear();
        _pendingSet.Clear();
        if (_layoutRecorder.Valid)
        {
            _layoutRecorder.Dispose();
        }

        _layoutRecorder = default;
        if (_log != null)
        {
            if (TotalChanges > 0)
            {
                WriteTodo();
            }

            _log.Dispose();
            _log = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
