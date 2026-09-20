#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

// Отладочные виды террейна, отдельно от световых.
//
// Световой вид показывает, сколько света пришло в пиксель. Он не отвечает на
// вопрос, почему пиксель тёмный: гасить может кайма рельефа, силуэт клетки,
// контактное затенение или просто не тот слой. Здесь каждый вид возвращает
// ровно один терм, без света и без атласа поверх, — и тогда видно, какой
// именно множитель работает.
//
// Раскладка обязана совпадать с TerrainDebugView.hlsl: номера едут в шейдер
// как есть.
public enum TerrainDebugView
{
    Off = 0,
    ForeignSides = 2,
    Coverage = 3,
    Layer = 4,
    Anchored = 5,
    CellLocal = 6,
    ReliefGroup = 7,
    ContinuousSheet = 8,
    AmbientOcclusion = 9,
}

public static class TerrainDebugViewState
{
    private static readonly int _terrainDebugViewID = Shader.PropertyToID("_TerrainDebugView");

    // Глобаль шейдера живёт в нативной части и переживает доменную
    // перезагрузку, а статическое поле — нет. Без публикации на старте они
    // расходятся: C# считает, что вид выключен, а кадр рисуется прошлым
    // выбранным видом, которого может уже и не быть в перечислении.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void PublishOnLoad() => Publish();

    public static TerrainDebugView Active { get; private set; } = TerrainDebugView.Off;

    public static string Describe(TerrainDebugView view) => view switch
    {
        TerrainDebugView.Off => "Обычный вид",
        TerrainDebugView.ForeignSides => "Чужие стороны",
        TerrainDebugView.Coverage => "Силуэт клетки",
        TerrainDebugView.Layer => "Слой",
        TerrainDebugView.Anchored => "Смещённые клетки",
        TerrainDebugView.CellLocal => "Координата в клетке",
        TerrainDebugView.ReliefGroup => "Рельефная группа",
        TerrainDebugView.ContinuousSheet => "Сплошной лист",
        TerrainDebugView.AmbientOcclusion => "Контактное затенение",
        _ => view.ToString(),
    };

    public static string Legend(TerrainDebugView view) => view switch
    {
        TerrainDebugView.Off => "Термы террейна не подменяются.",
        TerrainDebugView.ForeignSides =>
            "Красный — чужой сосед сверху, зелёный — снизу, синий — слева, " +
            "жёлтый — справа. Серое — клетка без рельефной группы.",
        TerrainDebugView.Coverage =>
            "Белое — пиксель принадлежит клетке, чёрное — вырезан. " +
            "Здесь видно скругление и силуэт смещённой клетки.",
        TerrainDebugView.Layer =>
            "Зелёное — передний план, синее — подложка. Синее там, где ждёшь " +
            "блок, значит блок не нарисован.",
        TerrainDebugView.Anchored =>
            "Жёлтое — у клетки смещён хотя бы один угол, и она растеризуется " +
            "через несущий прямоугольник.",
        TerrainDebugView.CellLocal =>
            "Красный — X внутри клетки, зелёный — Y. Плоский угол канала " +
            "означает выход за пределы клетки.",
        TerrainDebugView.ReliefGroup =>
            "Оттенок — код рельефа. Чёрно-синее — клетка без группы.",
        TerrainDebugView.ContinuousSheet =>
            "Бирюзовое — клетка адресует лист целиком по мировой координате; " +
            "тёмное — тайл на клетку.",
        TerrainDebugView.AmbientOcclusion =>
            "Белое — затенения нет, чёрное — полное. Тёмные полосы здесь " +
            "означают, что гасит затенение, а не кайма.",
        _ => string.Empty,
    };

    public static void Set(TerrainDebugView view)
    {
        Active = view;
        Publish();
    }

    // Глобаль переживает смену сцены и доменную перезагрузку, поэтому её
    // публикуют заново, а не полагаются на прошлое значение.
    public static void Publish() =>
        Shader.SetGlobalInteger(_terrainDebugViewID, (int)Active);

    public static void Reset() => Set(TerrainDebugView.Off);
}
