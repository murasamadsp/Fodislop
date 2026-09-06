using System.Globalization;
using System.Text.RegularExpressions;
using Fodinae.ArchitectureLinter.Core;
using Mono.Cecil;

namespace Fodinae.ArchitectureLinter.Rules.Rendering;

/// <summary>
/// Validates display transform shader and Render Graph invariants.
/// Checks matrix reciprocity and gamma curve application order.
/// </summary>
public sealed class DisplayTransformRule : IRule
{
    private const RegexOptions Invariant = RegexOptions.CultureInvariant;

    public string Id => "FOD-DISPLAY-TRANSFORM";
    public string Description => "Display transform shader and Render Graph invariants";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        string shaderRoot = Path.Combine(
            context.ProjectRoot,
            "Assets",
            "Resources",
            "Shaders",
            "PostProcessing");

        CheckFile(violations, Path.Combine(shaderRoot, "ColorGrading.hlsl"), CheckColorGrading);
        CheckFile(violations, Path.Combine(shaderRoot, "PostProcess.compute"), CheckPostProcessShader);
        CheckFile(violations, Path.Combine(shaderRoot, "Scopes.compute"), CheckScopesShader);
        CheckFile(
            violations,
            Path.Combine(
                context.ProjectRoot,
                "Assets",
                "Scripts",
                "Rendering",
                "PostProcessing",
                "PostProcessRenderPass.cs"),
            CheckRenderPassSource);

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private void CheckFile(
        ICollection<RuleViolation> violations,
        string path,
        Action<ICollection<RuleViolation>, string, string> check)
    {
        if (!File.Exists(path))
        {
            AddViolation(violations, path, $"Required display-pipeline file '{Path.GetFileName(path)}' was not found.");
            return;
        }

        check(violations, path, File.ReadAllText(path));
    }

    private void CheckColorGrading(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        if (!source.TrimEnd().EndsWith(
                "#endif // FODINAE_COLOR_GRADING_INCLUDED",
                StringComparison.Ordinal))
        {
            AddViolation(
                violations,
                path,
                "Include guard must close with #endif // FODINAE_COLOR_GRADING_INCLUDED.");
        }

        foreach (string matrixName in new[] { "rec709ToDisplayP3", "rec709ToRec2020" })
        {
            CheckWhitePreservingMatrix(violations, path, source, matrixName);
        }

        Dictionary<string, float[]> matrices = ReadMatrices(source);
        if (!matrices.TryGetValue("toLms", out float[]? toLms) ||
            !matrices.TryGetValue("fromLms", out float[]? fromLms))
        {
            AddViolation(violations, path, "White balance requires both toLms and fromLms matrices.");
        }
        else
        {
            float worstDeviation = 0f;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    float value = 0f;
                    for (int index = 0; index < 3; index++)
                    {
                        value += fromLms[(row * 3) + index] * toLms[(index * 3) + column];
                    }

                    float expected = row == column ? 1f : 0f;
                    worstDeviation = Math.Max(worstDeviation, Math.Abs(value - expected));
                }
            }

            if (worstDeviation > 0.002f)
            {
                AddViolation(
                    violations,
                    path,
                    $"toLms and fromLms are not inverses (worst deviation {worstDeviation:F6}).");
            }
        }

        Require(
            violations,
            path,
            source,
            @"result\s*=\s*color\s*\*\s*\(mapped\s*/\s*norm\)",
            "Curve must preserve hue by scaling color with mapped / norm.");
        Require(
            violations,
            path,
            source,
            @"headStops\s*=\s*-log2\(max\(greyOut",
            "Curve headroom must be derived from greyOut.");

        if (Regex.IsMatch(source, @"pow\(\s*color\s*,\s*max\(\s*displayGamma", Invariant) ||
            Regex.IsMatch(source, @"2\.2\s*/\s*_Gamma", Invariant))
        {
            AddViolation(violations, path, "The display-linear curve must not apply display gamma.");
        }

        Require(
            violations,
            path,
            source,
            @"unitPower\s*=\s*1\.0\s*-\s*step\([^;]*abs\(power\s*-\s*1\.0\)\)",
            "Neutral ASC CDL power must preserve negative log values.");
        Require(
            violations,
            path,
            source,
            @"return\s+lerp\(powered,\s*graded,\s*unitPower\)",
            "ASC CDL must select the sign-preserving neutral-power path.");
    }

    private void CheckPostProcessShader(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        Require(
            violations,
            path,
            source,
            @"headroom\s*\*\s*excess\s*/\s*\(headroom\s*\+\s*excess\)",
            "HDR peak must use a soft shoulder.");
        Require(
            violations,
            path,
            source,
            @"color\s*\*=\s*mapped\s*/\s*max\(norm",
            "HDR shoulder must scale by the maximum channel to preserve hue.");

        if (Regex.IsMatch(source, @"min\(\s*color\s*,\s*_HdrPeakBrightnessScale\s*\)", Invariant))
        {
            AddViolation(violations, path, "HDR peak must not hard-clip with min(color, peak).");
        }

        int gamutPosition = source.IndexOf("color = ConvertOutputGamut", StringComparison.Ordinal);
        int temporalPosition = source.IndexOf("if (_Temporal.x > 0.001", StringComparison.Ordinal);
        if (gamutPosition < 0 || temporalPosition < 0 || gamutPosition > temporalPosition)
        {
            AddViolation(
                violations,
                path,
                "Output gamut and HDR shoulder must execute before temporal accumulation.");
        }

        Require(
            violations,
            path,
            source,
            @"_HdrPeakBrightnessScale\s*<=\s*0\.01[^}]*2\.2\s*/\s*max\(_Gamma",
            "SDR gamma must calibrate linear color before URP FinalBlit.",
            RegexOptions.Singleline);
        Require(
            violations,
            path,
            source,
            @"centeredUv\s*=\s*\(screenUv\s*-\s*_VignetteCenter\)",
            "Vignette must use stable screenUv rather than heat-distorted sample UV.");
    }

    private void CheckScopesShader(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        Require(
            violations,
            path,
            source,
            @"_ScopeSignalScale",
            "Scopes must map the HDR signal against the configured display peak.");
        Require(
            violations,
            path,
            source,
            @"WaveformBuffer[^\n]*_ScopeParams\.z",
            "Waveform must use its own density normalization.");
        Require(
            violations,
            path,
            source,
            @"VectorscopeBuffer[^\n]*_ScopeParams\.w",
            "Vectorscope must use its own density normalization.");
    }

    private void CheckRenderPassSource(
        ICollection<RuleViolation> violations,
        string path,
        string source)
    {
        Require(
            violations,
            path,
            source,
            @"DisplayPeakBrightnessNits\s*/\s*nativePaperWhite",
            "Display peak brightness must reach the HDR pass relative to native paper white.");
        Require(
            violations,
            path,
            source,
            @"OutputGamut\s*=\s*cameraData\.isHDROutputActive\s*\?\s*\(int\)DisplayGamutKind\.Rec709",
            "HDR custom pass must leave output in Rec.709 for URP FinalBlit conversion.");
        Require(
            violations,
            path,
            source,
            @"TextureDesc\s+activeColorDesc\s*=\s*activeColor\.GetDescriptor\(renderGraph\)",
            "Render Graph temporary targets must inherit TextureDesc from activeColor.");
        Require(
            violations,
            path,
            source,
            @"TextureHandle\s+intermediateTexture\s*=\s*renderGraph\.CreateTexture\(desc\)",
            "Render Graph intermediate color must use the active-color-derived descriptor.");
        Require(
            violations,
            path,
            source,
            @"temporalActive\s*=\s*PostProcessRuntimeState\.DebugView\s*==\s*PostProcessDebugView\.None",
            "Debug views must disable temporal history.");
    }

    private void CheckWhitePreservingMatrix(
        ICollection<RuleViolation> violations,
        string path,
        string source,
        string matrixName)
    {
        Match block = Regex.Match(
            source,
            Regex.Escape(matrixName) + @"\s*=\s*float3x3\(([^)]*)\)",
            Invariant);
        if (!block.Success || !TryReadFloatList(block.Groups[1].Value, out float[] values) || values.Length != 9)
        {
            AddViolation(violations, path, $"Matrix {matrixName} must contain exactly 9 numbers.");
            return;
        }

        for (int row = 0; row < 3; row++)
        {
            float sum = values[row * 3] + values[(row * 3) + 1] + values[(row * 3) + 2];
            if (Math.Abs(sum - 1f) > 0.002f)
            {
                AddViolation(
                    violations,
                    path,
                    $"Matrix {matrixName} row {row + 1} sums to {sum:F6} instead of 1.0.");
            }
        }
    }

    private static Dictionary<string, float[]> ReadMatrices(string source)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     source,
                     @"const\s+float3x3\s+(\w+)\s*=\s*float3x3\(([^)]*)\)",
                     Invariant))
        {
            if (TryReadFloatList(match.Groups[2].Value, out float[] values) && values.Length == 9)
            {
                result[match.Groups[1].Value] = values;
            }
        }

        return result;
    }

    private static bool TryReadFloatList(string source, out float[] values)
    {
        MatchCollection matches = Regex.Matches(
            source,
            @"-?\d+(?:\.\d+)?(?:e[+-]?\d+)?",
            RegexOptions.IgnoreCase | Invariant);
        values = new float[matches.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            if (!float.TryParse(
                    matches[index].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out values[index]))
            {
                values = [];
                return false;
            }
        }

        return true;
    }

    private void Require(
        ICollection<RuleViolation> violations,
        string path,
        string source,
        string pattern,
        string message,
        RegexOptions options = RegexOptions.None)
    {
        if (Regex.IsMatch(source, pattern, options | Invariant))
        {
            return;
        }

        AddViolation(violations, path, message);
    }

    private void AddViolation(
        ICollection<RuleViolation> violations,
        string path,
        string message)
    {
        violations.Add(new RuleViolation
        {
            RuleId = Id,
            Message = message,
            Severity = Severity,
            AssemblyName = path,
        });
    }
}
