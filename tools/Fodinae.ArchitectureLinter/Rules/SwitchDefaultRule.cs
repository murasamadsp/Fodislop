using Fodinae.ArchitectureLinter.Core;
using Mono.Cecil;
using System.Text.RegularExpressions;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class SwitchDefaultRule : IRule
{
    private static readonly Regex SwitchRegex = new Regex(@"^\s*switch\s*\(\s*[\w.]+\s*\)\s*$", RegexOptions.Compiled);

    public string Id => "FOD-SWITCH-DEFAULT";
    public string Description => "Switch statements with 3+ cases must have default";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;
        var scriptsRoot = Path.Combine(projectRoot, "Assets", "Scripts");

        if (!Directory.Exists(scriptsRoot))
            return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);

        foreach (var file in SourceScanner.EnumerateCsFiles(scriptsRoot, "Tests", "Plugins", "VContainer", "Editor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SwitchRegex.IsMatch(lines[i]))
                    continue;

                var body = new List<string>();
                var depth = 0;
                var started = false;
                for (var j = i + 1; j < lines.Length && j < i + 140; j++)
                {
                    depth += (lines[j].Split('{').Length - 1);
                    depth -= (lines[j].Split('}').Length - 1);
                    body.Add(lines[j]);
                    if (lines[j].Trim() == "{" || (started && depth <= 0))
                    {
                        started = true;
                        if (depth <= 0 && started)
                            break;
                    }
                }

                var text = string.Join("\n", body);
                var cases = Regex.Matches(text, @"^\s*case\s", RegexOptions.Multiline).Count;
                if (cases >= 3 && !Regex.IsMatch(text, @"^\s*default\s*:", RegexOptions.Multiline))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"switch with {cases} cases has no default.",
                        Severity = Severity,
                        AssemblyName = SourceScanner.GetProjectRelativePath(projectRoot, file),
                        Line = i + 1
                    });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }
}
