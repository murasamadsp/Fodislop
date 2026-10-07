using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Kern.TerrainRasterTests;

internal static class Program
{
    private static readonly string[] Mutations =
    [
        "double-quantize-fragments",
        "ao-flat-contact",
        "ao-to-black",
        "rim-merges-any-masses",
        "background-ignores-occlusion",
        "wall-variant-ignores-bottom",
        "node-edge-jitter-sign",
        "organic-noise-truncates",
        "round-rim-follows-cell-sides",
    ];

    public static int Main()
    {
        string root = FindProjectRoot();
        string shim = Read(root, "tools/Kern.LightingTests/NativeTransportShim.cpp");
        string format = Read(root, "Assets/Shaders/Terrain/TerrainCellFormat.hlsl");
        string loader = Read(root, "Assets/Shaders/Terrain/TerrainCellData.hlsl");
        string terrainContour = Read(root, "Assets/Shaders/Terrain/TerrainContour.hlsl");
        string terrainGeometryContract = Read(root, "Assets/Shaders/Terrain/TerrainGeometryContract.hlsl");
        string terrainGeometry = Read(root, "Assets/Shaders/Terrain/TerrainGeometry.hlsl");
        string terrainShader = Read(root, "Assets/Shaders/Terrain/Terrain.shader");
        string scenario = Read(root, "tools/Kern.TerrainRasterTests/scenario.cpp");
        string aoShim = Read(root, "tools/Kern.TerrainRasterTests/ao-field.cpp");
        string ao = Read(root, "Assets/Shaders/Terrain/TerrainAmbientOcclusion.hlsl");
        string terrainSampling = Read(root, "Assets/Shaders/Terrain/TerrainSampling.hlsl");
        string lighting = Read(root, "Assets/Shaders/Terrain/TerrainLightingData.hlsl");
        string terrainGeometryUv = SliceBetween(
                terrainSampling,
                "float TerrainUvCross(",
                "void TerrainSetResolvedTileIdentity(")
            .Replace("[unroll]", string.Empty, StringComparison.Ordinal);

        if (!terrainGeometryUv.Contains("TerrainResolveGeometryTileUV(", StringComparison.Ordinal))
        {
            return Fail("Production terrain geometry UV remap was not found");
        }

        if (terrainShader.Contains("TerrainRimRaw(", StringComparison.Ordinal) ||
            terrainShader.Contains("TerrainRim(surface", StringComparison.Ordinal))
        {
            return Fail("Terrain.shader must not apply the retired rim to visible albedo");
        }

        if (Regex.Matches(terrainShader, "BuildTerrainSurfaceInputs\\(").Count != 3)
        {
            return Fail("Expected exactly one surface parse in each terrain pass");
        }

        if (Regex.Matches(terrainShader + Read(root, "Assets/Shaders/Terrain/TerrainLightingFieldCommon.hlsl"), "PixelArtSampleUV\\(").Count != 2)
        {
            return Fail("Visible and field albedo paths must share the pixel-grid UV correction");
        }

        int materialFieldPassAt = terrainShader.IndexOf("Name \"LightingMaterialField\"", StringComparison.Ordinal);
        int ambientOcclusionPassAt = terrainShader.IndexOf("Name \"LightingAmbientOcclusionField\"", StringComparison.Ordinal);
        if (materialFieldPassAt < 0 || ambientOcclusionPassAt < 0 || materialFieldPassAt > ambientOcclusionPassAt)
        {
            return Fail("Terrain material and AO fields must have distinct ordered shader passes");
        }

        string materialFieldPass = terrainShader[materialFieldPassAt..ambientOcclusionPassAt];
        string ambientOcclusionPass = terrainShader[ambientOcclusionPassAt..];
        if (materialFieldPass.Contains("TerrainGeometryCoverageForField", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("TerrainGeometrySignedDistance", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("if (exteriorDistance >= _TerrainAmbientOcclusionDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("_TerrainAmbientOcclusionDistance, exteriorDistance)", StringComparison.Ordinal) ||
            !ambientOcclusionPass.Contains("ColorMask R", StringComparison.Ordinal) ||
            ambientOcclusionPass.Contains("TerrainAnimationSampling.hlsl", StringComparison.Ordinal) ||
            ambientOcclusionPass.Contains("_PrismaticFlowMap", StringComparison.Ordinal))
        {
            return Fail("AO must write its single-channel red field without transport or flow-map dependencies");
        }

        int debugAt = terrainShader.IndexOf("if (KernTerrainDebugActive())", StringComparison.Ordinal);
        int debugClipAt = terrainShader.IndexOf("clip(cellCoverage - 0.5)", debugAt, StringComparison.Ordinal);
        if (debugAt < 0 || debugClipAt < debugAt)
        {
            return Fail("The terrain debug silhouette must be clipped before its AO/rim output");
        }

        int rawAoDebugAt = terrainShader.IndexOf("_WorldLightDebugView == 9)", StringComparison.Ordinal);
        int rawAoClipAt = terrainShader.IndexOf("clip(cellCoverage - 0.5)", rawAoDebugAt, StringComparison.Ordinal);
        int rawAoSampleAt = terrainShader.IndexOf("KernSampleTerrainAmbientOcclusion(", rawAoDebugAt, StringComparison.Ordinal);
        if (rawAoDebugAt < 0 || rawAoClipAt < rawAoDebugAt || rawAoSampleAt < rawAoClipAt)
        {
            return Fail("The raw AO debug output must use the displaced foreground silhouette");
        }

        string rim = SliceBetween(terrainContour, "// Выключатель каймы.", "// The visible terrain");
        string contour = terrainContour[..terrainContour.IndexOf("float2 QuantizeTerrainFaceUV(", StringComparison.Ordinal)];
        const string quantizeUv = """
            float2 QuantizeTerrainFaceUV(float2 uv)
            {
                float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
                return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
            }
            """;
        const string extra = """
            float distance(float2 a, float2 b) { return length(a - b); }
            float lerp(float a, float b, float t) { return a + (b-a)*t; }
            float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
            float4 round(float4 a) { return {std::round(a.x),std::round(a.y),std::round(a.z),std::round(a.w)}; }
            float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
            float _TestFwidth = 0.0f;
            float fwidth(float) { return _TestFwidth; }
            using uint4 = unsigned __attribute__((ext_vector_type(4)));
            uint asuint(int value) { return (uint)value; }
            uint asuint(float value) { uint result; std::memcpy(&result, &value, 4); return result; }
            int min(int a, int b) { return a < b ? a : b; }
            uint min(uint a, uint b) { return a < b ? a : b; }
            int asint(uint value) { return (int)value; }
            bool any(int4 a) { return a.x || a.y || a.z || a.w; }
            float asfloat(uint value) { float result; std::memcpy(&result, &value, 4); return result; }
            long bufferReads = 0;
            template <typename T> struct ShimBuffer {
                std::vector<T> data;
                void reset(int count) { data.assign(count, T{}); }
                T operator[](int index) const {
                    ++bufferReads;
                    if (index < 0 || index >= (int)data.size())
                        throw std::runtime_error("out-of-bounds buffer read " + std::to_string(index));
                    return data[index];
                }
            };
            """;
        const string terrainUniforms = """
            float _OrganicBendStrength = 1.0;
            float _OrganicBendPivot = 0.35;
            float _RoundableCornerRadius = 0.51;
            float _RimQuantizationEnabled = 0.0;
            float _TerrainAmbientOcclusionDistance = 0.25;
            float _RimDistanceScale = 2.0;
            float _RimFalloff = 0.5;
            float4 _ShimAtlasTexelSize[8] = {};
            float4 TerrainMaterialAtlasTexelSize(int slot) { return _ShimAtlasTexelSize[slot]; }
            """;

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"kern-terrain-raster-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            // Миры стенда: клетки, строки типов и вершины FillQuad, с которыми
            // сверяется настоящий шейдер.
            string worldDirectory = Path.Combine(temporaryDirectory, "worlds");
            ProcessResult export = Run(
                "dotnet",
                ["test", Path.Combine(root, "tools/Kern.TerrainTests"), "--filter", "FullyQualifiedName~ExportWorldsForHlslShim"],
                root,
                ("KERN_TERRAIN_SHIM_FIXTURES", worldDirectory));
            if (export.ExitCode != 0 || !Directory.Exists(worldDirectory))
            {
                Console.Error.Write(export.StandardOutput);
                return Fail("Terrain stand did not export the shim worlds");
            }

            string cppPath = Path.Combine(temporaryDirectory, "test.cpp");
            string executablePath = Path.Combine(temporaryDirectory, "test");
            foreach (string? mutation in new string?[] { null }.Concat(Mutations))
            {
                string candidateLoader = loader;
                string candidateGeometry = terrainGeometry;
                string candidateAo = ao;
                string candidateRim = rim;

                if (mutation == "double-quantize-fragments")
                {
                    string before = """
                            return TerrainPolygonContains(
                                samplePosition,
                                cornersX,
                                cornersY,
                                packedEdges,
                                true) ? 1.0 : 0.0;
                        """;
                    string after = """
                            float2 fragmentGridSample = (floor(samplePosition * 32.0) + 0.5) / 32.0;
                            return TerrainPolygonContains(
                                fragmentGridSample,
                                cornersX,
                                cornersY,
                                packedEdges,
                                true) ? 1.0 : 0.0;
                        """;
                    candidateGeometry = candidateGeometry.Replace(before, after, StringComparison.Ordinal);
                    if (candidateGeometry == terrainGeometry) return Fail("double-quantize-fragments mutation is stale");
                }

                if (mutation is "rim-merges-any-masses" or "background-ignores-occlusion" or "wall-variant-ignores-bottom"
                    or "node-edge-jitter-sign" or "organic-noise-truncates")
                {
                    (string before, string after) = mutation switch
                    {
                        "rim-merges-any-masses" => (
                            "return otherMass != 0u && TerrainTypeRimMass(own) == otherMass;",
                            "return otherMass != 0u;"),
                        "background-ignores-occlusion" => (
                            "        if (occluded)\n        {\n            return v;\n        }",
                            "        if (false)\n        {\n            return v;\n        }"),
                        "wall-variant-ignores-bottom" => (
                            "bool hasBottom = (cornerSideMask & 8u) != 0u;",
                            "bool hasBottom = false;"),
                        "node-edge-jitter-sign" => (
                            "if (ctl && ctr) return int2(0, -ry);",
                            "if (ctl && ctr) return int2(0, ry);"),
                        _ => (
                            "return ((value << 1) + (d * d) / 2u) / (d * d);",
                            "return (value << 1) / (d * d);"),
                    };
                    candidateLoader = candidateLoader.Replace(before, after, StringComparison.Ordinal);
                    if (candidateLoader == loader) return Fail($"{mutation} mutation is stale");
                }

                if (mutation == "round-rim-follows-cell-sides")
                {
                    candidateRim = candidateRim.Replace(
                        "    if (KernTerrainIsRoundable(surface.packedContour))\n    {\n        // У круглого блока",
                        "    if (false)\n    {\n        // У круглого блока",
                        StringComparison.Ordinal);
                    if (candidateRim == rim) return Fail("round-rim-follows-cell-sides mutation is stale");
                }

                if (mutation == "ao-flat-contact")
                {
                    candidateAo = candidateAo.Replace("contact * _TerrainAmbientOcclusionStrength", "_TerrainAmbientOcclusionStrength", StringComparison.Ordinal);
                    if (candidateAo == ao) return Fail("ao-flat-contact mutation is stale");
                }

                if (mutation == "ao-to-black")
                {
                    candidateAo = candidateAo.Replace(
                        "1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor))",
                        "1.0 - occlusion",
                        StringComparison.Ordinal);
                    if (candidateAo == ao) return Fail("ao-to-black mutation is stale");
                }

                candidateAo = candidateAo
                    .Replace("Texture2D<float4>", "AoTexture", StringComparison.Ordinal)
                    .Replace("Texture2D<float>", "AoTexture", StringComparison.Ordinal)
                    .Replace("SamplerState", "int", StringComparison.Ordinal);
                string source = shim + extra + aoShim + terrainUniforms + Translate(
                    terrainGeometryContract + candidateGeometry + format + candidateLoader + lighting +
                    contour + quantizeUv + candidateRim + terrainGeometryUv + candidateAo) + scenario;
                File.WriteAllText(cppPath, source);

                ProcessResult compile = Run("clang++", ["-std=c++20", "-O2", "-ffp-contract=off", cppPath, "-o", executablePath], temporaryDirectory);
                Console.Out.Write(compile.StandardOutput);
                Console.Error.Write(compile.StandardError);
                if (compile.ExitCode != 0) return compile.ExitCode;

                ProcessResult result = Run(executablePath, [worldDirectory], temporaryDirectory);
                if (mutation is null)
                {
                    Console.Out.Write(result.StandardOutput);
                    if (result.ExitCode != 0) return Fail(result.StandardError);
                }
                else if (result.ExitCode == 0)
                {
                    return Fail($"Regression check accepted mutation: {mutation}");
                }
                else
                {
                    Console.WriteLine($"Mutation {mutation} rejected: {result.StandardError.Trim()}");
                }
            }

            return 0;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string Translate(string source)
    {
        source = Regex.Replace(source, "^#.*$", string.Empty, RegexOptions.Multiline)
            .Replace("[unroll]", string.Empty, StringComparison.Ordinal)
            .Replace("[branch]", string.Empty, StringComparison.Ordinal)
            .Replace("[loop]", string.Empty, StringComparison.Ordinal)
            .Replace("inout TerrainTileUvResult tile", "TerrainTileUvResult& tile", StringComparison.Ordinal)
            .Replace("out float2 nearestPosition", "float2& nearestPosition", StringComparison.Ordinal)
            .Replace("Texture2D<float4>", "Texture", StringComparison.Ordinal)
            .Replace("StructuredBuffer<", "ShimBuffer<", StringComparison.Ordinal)
            .Replace("(TerrainCellVertex)0", "TerrainCellVertex{}", StringComparison.Ordinal);
        source = Regex.Replace(source, @"\(int2\)round\(([^)]+)\)", "make_int2(round($1))", RegexOptions.CultureInvariant);
        return Regex.Replace(source, @"\b(float[234]|int[23]|uint[23])\(", "make_$1(", RegexOptions.CultureInvariant);
    }

    private static string SliceBetween(string source, string start, string end)
    {
        int startIndex = source.IndexOf(start, StringComparison.Ordinal);
        int endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0) throw new InvalidDataException($"Could not locate expected source section: {start}");
        return source[startIndex..endIndex];
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Assets/Shaders/Terrain/Terrain.shader"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        params (string Name, string Value)[] environment)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach ((string name, string value) in environment) startInfo.Environment[name] = value;
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
