#nullable enable

namespace Fodinae.Rendering.PostProcessing;

public enum ColorGradeColorSpace
{
    Rec709 = 0,
    DisplayP3 = 1,
    Rec2020 = 2,
}

public enum ColorGradeTransferFunction
{
    Linear = 0,
    Srgb = 1,
    Pq = 2,
    Hlg = 3,
}

public enum ColorGradeReferenceMode
{
    SceneReferred = 0,
    DisplayReferred = 1,
}

public enum ColorGradeDynamicRangeMode
{
    Sdr = 0,
    Hdr = 1,
}

public sealed class ColorGradeColorManagement
{
    public ColorGradeColorSpace InputColorSpace { get; set; } = ColorGradeColorSpace.Rec709;
    public ColorGradeColorSpace WorkingColorSpace { get; set; } = ColorGradeColorSpace.Rec709;
    public ColorGradeColorSpace OutputColorSpace { get; set; } = ColorGradeColorSpace.Rec709;
    public ColorGradeTransferFunction InputTransfer { get; set; } = ColorGradeTransferFunction.Linear;
    public ColorGradeTransferFunction OutputTransfer { get; set; } = ColorGradeTransferFunction.Srgb;
    public ColorGradeReferenceMode ReferenceMode { get; set; } = ColorGradeReferenceMode.SceneReferred;
    public ColorGradeDynamicRangeMode DynamicRange { get; set; } = ColorGradeDynamicRangeMode.Sdr;

    public ColorGradeColorManagement Clone() => new()
    {
        InputColorSpace = InputColorSpace,
        WorkingColorSpace = WorkingColorSpace,
        OutputColorSpace = OutputColorSpace,
        InputTransfer = InputTransfer,
        OutputTransfer = OutputTransfer,
        ReferenceMode = ReferenceMode,
        DynamicRange = DynamicRange,
    };

    public void Sanitize()
    {
        if (!System.Enum.IsDefined(typeof(ColorGradeColorSpace), InputColorSpace))
        {
            InputColorSpace = ColorGradeColorSpace.Rec709;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeColorSpace), WorkingColorSpace))
        {
            WorkingColorSpace = ColorGradeColorSpace.Rec709;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeColorSpace), OutputColorSpace))
        {
            OutputColorSpace = ColorGradeColorSpace.Rec709;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeTransferFunction), InputTransfer))
        {
            InputTransfer = ColorGradeTransferFunction.Linear;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeTransferFunction), OutputTransfer))
        {
            OutputTransfer = ColorGradeTransferFunction.Srgb;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeReferenceMode), ReferenceMode))
        {
            ReferenceMode = ColorGradeReferenceMode.SceneReferred;
        }

        if (!System.Enum.IsDefined(typeof(ColorGradeDynamicRangeMode), DynamicRange))
        {
            DynamicRange = ColorGradeDynamicRangeMode.Sdr;
        }
    }
}
