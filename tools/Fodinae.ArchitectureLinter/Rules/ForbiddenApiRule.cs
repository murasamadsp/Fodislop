using Mono.Cecil;
using Mono.Cecil.Cil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class ForbiddenApiRule : IRule
{
    private static readonly (string DeclaringType, string MethodName, string Description)[] ForbiddenApis =
    {
        ("UnityEngine.Camera", "get_main", "Camera.main is forbidden — use IGameplayCamera injection instead"),
        ("UnityEngine.Object", "FindAnyObjectByType", "FindAnyObjectByType is forbidden — use DI or serialized references"),
        ("UnityEngine.Object", "FindObjectsByType", "FindObjectsByType is forbidden — use DI or serialized references"),
        ("UnityEngine.GameObject", "Find", "GameObject.Find is forbidden — use DI or serialized references"),
        ("UnityEngine.GameObject", "FindWithTag", "GameObject.FindWithTag is forbidden — use DI or serialized references"),
        ("UnityEngine.Texture2D", ".ctor", "new Texture2D is forbidden — use RuntimeTextureFactory"),
    };

    public string Id => "FOD-FORBIDDEN-API";
    public string Description => "Forbidden API usage detection";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        foreach (var assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
                continue;

            if (IsEditorAssembly(assembly.Name.Name) || IsTestAssembly(assembly.Name.Name))
                continue;

            foreach (var type in assembly.MainModule.Types)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanType(type, violations);
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static bool IsEditorAssembly(string assemblyName)
    {
        return assemblyName.EndsWith(".Editor", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.EndsWith("-Editor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("Fodinae.Tests.", StringComparison.OrdinalIgnoreCase);
    }

    private void ScanType(TypeDefinition type, List<RuleViolation> violations)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var forbidden in ForbiddenApis)
            {
                if (IsAllowedOwner(type, forbidden.DeclaringType, forbidden.MethodName))
                {
                    continue;
                }

                if (CecilAssemblyScanner.CallsMethod(method, forbidden.DeclaringType, forbidden.MethodName))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = forbidden.Description,
                        Severity = Severity,
                        AssemblyName = type.Module.Assembly.Name.Name,
                        TypeName = type.FullName,
                        MemberName = method.Name
                    });
                }
            }
        }

        foreach (var nested in type.NestedTypes)
            ScanType(nested, violations);
    }

    private static bool IsAllowedOwner(
        TypeDefinition type,
        string forbiddenDeclaringType,
        string forbiddenMethodName)
    {
        if (forbiddenDeclaringType == "UnityEngine.Camera" &&
            forbiddenMethodName == "get_main" &&
            type.FullName == "Fodinae.Core.GameplayCamera")
        {
            return true;
        }

        return forbiddenDeclaringType == "UnityEngine.Texture2D" &&
               forbiddenMethodName == ".ctor" &&
               type.FullName == "Fodinae.RuntimeTextureFactory";
    }
}
