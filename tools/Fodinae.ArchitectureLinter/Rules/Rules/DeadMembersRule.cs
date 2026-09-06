using Fodinae.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules.Rules;

/// <summary>
/// Detects dead (never called) public members in production code.
/// Dead members are not harmless: they read as part of the contract.
/// </summary>
public sealed class DeadMembersRule : IRule
{
    private static readonly Regex DeclarationRegex = new Regex(
        @"^(public|internal|protected)\s+" +
        @"(?:static|readonly|const|sealed|override|virtual|abstract|new|partial|async|extern|unsafe)*\s+" +
        @"(?:[\w<>\[\],.?]+)\s+(\w+)\s*(?:\{|\()|=|;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> UnityMessages = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable",
        "OnDestroy", "OnGUI", "OnDrawGizmos", "OnDrawGizmosSelected", "OnApplicationQuit",
        "OnApplicationPause", "OnApplicationFocus", "OnLowMemory", "OnValidate", "Reset",
        "OnPreRender", "OnPostRender", "OnRenderImage", "OnBecameVisible", "OnBecameInvisible",
        "Dispose", "Configure", "Construct", "Main", "Equals", "GetHashCode", "ToString",
        "Create", "AddRenderPasses", "RecordRenderGraph", "Execute", "OnCameraSetup",
        "OnCameraCleanup", "IsActive", "IsTileCompatible", "ApplyLocalizedText",
        "GetPostprocessOrder", "GetVersion", "OnPreprocessTexture", "OnPostprocessTexture",
        "OnPreprocessAsset", "OnPreprocessAllAssets", "OnPreprocessBuild", "OnPostprocessBuild",
        "callbackOrder"
    };

    private static readonly string[] ClassUnityAttrs = { "CustomEditor", "MenuItem", "BuildPlayerProcessor", "InitializeOnLoad" };

    public string Id => "FOD-DEAD-MEMBERS";
    public string Description => "Dead member detection";
    public RuleSeverity Severity => RuleSeverity.Warning;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");
        var editorRoot = Path.Combine(projectRoot, "Assets", "Editor");

        if (!Directory.Exists(scriptsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        var allFiles = SourceScanner.EnumerateAllCsFiles(scriptsRoot, editorRoot).ToList();
        var productionFiles = allFiles
            .Select(f => (File: f, Relative: SourceScanner.GetProjectRelativePath(projectRoot, f)))
            .Where(t => !t.Relative.Split('/').Contains("Tests") &&
                        !t.Relative.Split('/').Contains("Plugins") &&
                        !t.Relative.Split('/').Contains("VContainer") &&
                        !t.Relative.Contains("/Editor/"))
            .ToList();

        // Build interface method map from ALL files
        var interfaceMethods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var classInterfaces = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in allFiles)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            bool inInterface = false;
            string? currentInterface = null;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var ifaceDecl = Regex.Match(trimmed, @"\binterface\s+(\w+)");
                if (ifaceDecl.Success)
                {
                    inInterface = true;
                    currentInterface = ifaceDecl.Groups[1].Value;
                }
                if (inInterface && Regex.IsMatch(trimmed, @"^(?:(?:public|internal|protected)\s+)?(?:static\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\("))
                {
                    var m = Regex.Match(trimmed, @"^(?:(?:public|internal|protected)\s+)?(?:static\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\(");
                    if (m.Success)
                    {
                        if (!interfaceMethods.ContainsKey(m.Groups[1].Value))
                            interfaceMethods[m.Groups[1].Value] = new List<string>();
                        interfaceMethods[m.Groups[1].Value].Add(currentInterface!);
                    }
                }
                if (inInterface && trimmed == "}" && !trimmed.Contains("{"))
                {
                    inInterface = false;
                    currentInterface = null;
                }
            }
            foreach (Match m in Regex.Matches(content, @"class\s+(\w+)\s*:\s*(.+)"))
            {
                var cls = m.Groups[1].Value;
                var bases = m.Groups[2].Value.Split(',');
                var ifaces = bases.Select(b => b.Trim().Split('<')[0].Trim()).Where(b => !string.IsNullOrEmpty(b)).ToList();
                classInterfaces[cls] = ifaces;
            }
        }

        // Collect declarations from production files
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, List<(string File, int Line)>>(StringComparer.Ordinal);

        foreach (var (file, relative) in productionFiles)
        {
            var raw = File.ReadAllText(file);
            var stripped = Regex.Replace(raw, @"/\*[\s\S]*?\*/", "");
            stripped = Regex.Replace(stripped, @"^\s*//.*$", "", RegexOptions.Multiline);
            stripped = Regex.Replace(stripped, @"//.*$", "");
            sources[relative] = stripped;

            var lines = raw.Split('\n');
            foreach (Match m in DeclarationRegex.Matches(stripped))
            {
                var name = m.Groups[2].Value;
                if (name.Length < 3 || UnityMessages.Contains(name))
                    continue;

                var lineIndex = stripped.Substring(0, m.Index).Split('\n').Length - 1;

                // Skip overrides
                if (Regex.IsMatch(lines[lineIndex] ?? "", @"^(public|internal|protected)\s+(?:[\w]+\s+)*override\s"))
                    continue;

                // Skip inherits (inheritdoc)
                bool inherited = false;
                var rawLines = raw.Split('\n');
                for (var j = Math.Max(0, lineIndex - 3); j <= lineIndex; j++)
                {
                    if ((rawLines[j] ?? "").Contains("<inheritdoc"))
                    {
                        inherited = true;
                        break;
                    }
                }
                if (inherited)
                    continue;

                // Skip attributes above
                bool guarded = false;
                for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 4; j--)
                {
                    var above = (lines[j] ?? "").Trim();
                    if (string.IsNullOrEmpty(above) || above.StartsWith("///") || above.StartsWith("//"))
                        continue;
                    if (above.StartsWith("["))
                    {
                        guarded = true;
                        break;
                    }
                }
                if (guarded)
                    continue;

                // Skip class-level Unity attributes
                if (!guarded)
                {
                    var rawLines2 = raw.Split('\n');
                    string? declaringClass = null;
                    for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 30; j--)
                    {
                        var m2 = Regex.Match(rawLines2[j], @"^(?:\s*)(?:public|internal|protected|sealed|static)\s+.*class\s+(\w+)");
                        if (m2.Success)
                        {
                            declaringClass = m2.Groups[1].Value;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(declaringClass))
                    {
                        var classStart = Array.FindIndex(rawLines2, l => Regex.IsMatch(l, $@"\bclass\s+{declaringClass}\b"));
                        if (classStart >= 0)
                        {
                            for (var j = classStart; j < Math.Min(rawLines2.Length, classStart + 5); j++)
                            {
                                var s = rawLines2[j].Trim();
                                foreach (var attr in ClassUnityAttrs)
                                {
                                    if (s.StartsWith($"[{attr}"))
                                    {
                                        guarded = true;
                                        break;
                                    }
                                }
                                if (guarded) break;
                            }
                        }
                    }
                }
                if (guarded)
                    continue;

                // Skip interface implementations
                if (!guarded && interfaceMethods.ContainsKey(name))
                {
                    var rawLines3 = raw.Split('\n');
                    string? declaringClass2 = null;
                    for (var j = lineIndex - 1; j >= 0 && j >= lineIndex - 30; j--)
                    {
                        var m2 = Regex.Match(rawLines3[j], @"^(?:\s*)(?:public|internal|protected|sealed|static)\s+.*class\s+(\w+)");
                        if (m2.Success)
                        {
                            declaringClass2 = m2.Groups[1].Value;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(declaringClass2) && classInterfaces.ContainsKey(declaringClass2))
                    {
                        var ifaces = classInterfaces[declaringClass2];
                        foreach (var iface in ifaces)
                        {
                            if (interfaceMethods.TryGetValue(name, out var list) && list.Contains(iface))
                            {
                                guarded = true;
                                break;
                            }
                        }
                    }
                }
                if (guarded)
                    continue;

                if (!declarations.ContainsKey(name))
                    declarations[name] = new List<(string, int)>();
                declarations[name].Add((relative, lineIndex + 1));
            }
        }

        // Build haystack
        var haystack = string.Join("\n", sources.Values);

        foreach (var (name, places) in declarations)
        {
            var uses = Regex.Matches(haystack, $@"(?<!\w){name}(?!\w)").Count;
            const string attributeSuffix = "Attribute";
            if (name.EndsWith(attributeSuffix, StringComparison.Ordinal))
            {
                string shortAttributeName = name[..^attributeSuffix.Length];
                uses += Regex.Matches(
                    haystack,
                    $@"\[\s*{Regex.Escape(shortAttributeName)}(?:\s|\(|\])").Count;
            }

            if (uses <= places.Count)
            {
                var first = places[0];
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Dead member '{name}' declared at {first.File}:{first.Line} is never referenced outside its declaration.",
                    Severity = Severity,
                    AssemblyName = first.File,
                    TypeName = first.File,
                    Line = first.Line
                });
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
