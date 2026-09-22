#nullable enable

namespace Kern.Rendering.PostProcessing;

// Калибровочный узор дисплея. Числа совпадают с _CalibrationPattern в
// PostProcess.compute и менять их в отрыве от шейдера нельзя.
public enum CalibrationPattern
{
    Off = 0,

    // Одно белое поле ровно в paper white: белая точка сверяется с бумагой
    // при том освещении, в котором человек играет.
    PaperWhite = 1,

    // Логарифмическая лестница яркостей с рамкой на выбранном пике: верхняя
    // ещё различимая ступень и есть пик дисплея.
    PeakLadder = 2,
}
