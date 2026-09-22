// C# backend invoked by tools/terrain-crystal-tests/lava.js.
// CPU execution of production UV addressing, not a GPU render test.

using System.Diagnostics;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

string shim = File.ReadAllText(Path.Combine(root, "tools/lighting-tests/NativeTransportShim.cpp"));

// Extra C++ helpers: scalar clamp and float2 overloads
const string Extra = """

float clamp(float a,float b,float c){return std::clamp(a,b,c);}
float2 clamp(float2 a,float2 b,float2 c){return {clamp(a.x,b.x,c.x),clamp(a.y,b.y,c.y)};}
float2 clamp(float2 a,float b,float2 c){return clamp(a,make_float2(b),c);}
float2 max(float2 a,float b){return max(a,make_float2(b));}

""";

// Concatenate production HLSL sources
string source = string.Join("\n",
    new[] { "TerrainTileAddressing.hlsl", "TerrainSampling.hlsl", "TerrainMoltenHeat.hlsl" }
        .Select(n => File.ReadAllText(Path.Combine(root, "Assets/Shaders", n))));

// Strip preprocessor directives (lines starting with #)
source = Regex.Replace(source, @"^#.*$", "", RegexOptions.Multiline);

// Replace HLSL constructor-style casts with make_* forms (float2/float3/float4)
source = Regex.Replace(source, @"\b(float[234])\(", "make_$1(");

// Scalar vector splats are implicit in HLSL; explicit with clang vectors
source = Regex.Replace(
    source,
    @"(res\.(?:tileOffsetUV|availableTileSize|finalUV|minTileUV|maxTileUV)) = 0\.0;",
    "$1 = make_float2(0.0);");

string lavaTestCpp = File.ReadAllText(Path.Combine(root, "tools/terrain-crystal-tests/lava.cpp"));

// Mutations: each replaces a production guard to confirm the guard is load-bearing
var mutations = new Dictionary<string, (string Old, string New)>
{
    ["lava"]  = ("if ((int)(animData.w + 0.5) == 2)", "if (false)"),
    // Continuous sheet back to per-cell tile: seam at every boundary.
    ["sheet"] = ("if (isContinuousSheet)", "if (false)"),
};

string tmpPrefix = Path.Combine(Path.GetTempPath(), $"kern-lava-{Path.GetRandomFileName()}");
Directory.CreateDirectory(tmpPrefix);

try
{
    foreach (string? mutation in new string?[] { null, "lava", "sheet" })
    {
        string candidate = source;

        if (mutation is not null)
        {
            var (old, replacement) = mutations[mutation];
            string mutated = candidate.Replace(old, replacement);

            if (mutated == candidate)
            {
                throw new InvalidOperationException(
                    $"Mutation '{mutation}' no longer matches the production source");
            }

            candidate = mutated;
        }

        string cppPath = Path.Combine(tmpPrefix, "test.cpp");
        string exePath = Path.Combine(tmpPrefix, "test");

        File.WriteAllText(cppPath, shim + Extra + candidate + lavaTestCpp);

        RunProcess("clang++", $"-std=c++20 -O2 {cppPath} -o {exePath}", checkSuccess: true);
        int exitCode = RunProcess(exePath, string.Empty, checkSuccess: false);

        bool expectedFailure = mutation is not null;
        bool didFail = exitCode != 0;

        if (expectedFailure != didFail)
        {
            throw new InvalidOperationException(
                mutation is null
                    ? $"Production binary failed (exit {exitCode})"
                    : $"Mutation '{mutation}' was NOT rejected (exit {exitCode})");
        }

        if (mutation is not null)
        {
            Console.WriteLine($"Cell-local addressing mutation rejected: {mutation}");
        }
    }
}
finally
{
    Directory.Delete(tmpPrefix, recursive: true);
}

// ---------------------------------------------------------------------------

static int RunProcess(string fileName, string arguments, bool checkSuccess)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
    };

    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to start: {fileName}");

    proc.WaitForExit();

    if (checkSuccess && proc.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"Process '{fileName} {arguments}' exited with code {proc.ExitCode}");
    }

    return proc.ExitCode;
}
