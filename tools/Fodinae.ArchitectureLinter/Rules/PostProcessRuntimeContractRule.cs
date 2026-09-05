using Fodinae.ArchitectureLinter.Core;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Fodinae.ArchitectureLinter.Rules;

public sealed class PostProcessRuntimeContractRule : IRule
{
    private static readonly MethodContract[] Contracts =
    [
        new(
            "Fodinae.Tools.Imgui.ToolWindows",
            "Unregister",
            [new("Fodinae.Tools.Imgui.ToolWindows", "ReleaseInputCapture", 1)]),
        new(
            "Fodinae.Rendering.PostProcessing.PostProcessController",
            "OnDisable",
            [
                new("Fodinae.Rendering.PostProcessing.PostProcessRuntimeState", "set_BypassPostProcessEffects", 1),
                new("Fodinae.Rendering.PostProcessing.PostProcessRuntimeState", "SetAdvancedSettings", 1),
            ]),
        new(
            "Fodinae.Rendering.PostProcessing.PostProcessRendererFeature",
            "AddRenderPasses",
            [
                new("Fodinae.Rendering.PostProcessing.PostProcessRuntimeState", "get_MainCamera", 1),
                new("Fodinae.Rendering.PostProcessing.PostProcessRuntimeState", "SetMainCamera", 1),
                new("UnityEngine.Object", "op_Inequality", 1),
            ]),
        new(
            "Fodinae.Rendering.PostProcessing.Scopes.ScopesRenderPass",
            "RecordRenderGraph",
            [
                new("Fodinae.Rendering.PostProcessing.PostProcessRuntimeState", "get_MainCamera", 1),
                new("UnityEngine.Object", "op_Inequality", 1),
            ]),
        new(
            "Fodinae.Rendering.DisplayManager",
            "SetHDREnabled",
            [new("Fodinae.Rendering.HDROutput", "SetEnabled", 2)]),
    ];

    public string Id => "FOD-POSTPROCESS-RUNTIME";
    public string Description => "Post-process and IMGUI runtime lifecycle contracts";
    public RuleSeverity Severity => RuleSeverity.Error;

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();

        foreach (MethodContract contract in Contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeDefinition? type = FindType(assemblies, context, contract.TypeName);
            if (type == null)
            {
                violations.Add(CreateViolation(
                    contract,
                    $"Required runtime type '{contract.TypeName}' was not found in loaded assemblies."));
                continue;
            }

            MethodDefinition? method = type.Methods.FirstOrDefault(candidate => candidate.Name == contract.MethodName);
            if (method == null || !method.HasBody)
            {
                violations.Add(CreateViolation(
                    contract,
                    $"Required method '{contract.TypeName}.{contract.MethodName}' has no inspectable body.",
                    type.Module.Assembly.Name.Name));
                continue;
            }

            foreach (CallRequirement requirement in contract.RequiredCalls)
            {
                int callCount = CountCalls(method, requirement.DeclaringType, requirement.MethodName);
                if (callCount >= requirement.MinimumCount)
                {
                    continue;
                }

                violations.Add(CreateViolation(
                    contract,
                    $"Method must call '{requirement.DeclaringType}.{requirement.MethodName}' " +
                    $"at least {requirement.MinimumCount} time(s); found {callCount}.",
                    type.Module.Assembly.Name.Name));
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private RuleViolation CreateViolation(
        MethodContract contract,
        string message,
        string? assemblyName = null)
    {
        return new RuleViolation
        {
            RuleId = Id,
            Message = message,
            Severity = Severity,
            AssemblyName = assemblyName,
            TypeName = contract.TypeName,
            MemberName = contract.MethodName,
        };
    }

    private static TypeDefinition? FindType(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        string fullName)
    {
        foreach (AssemblyDefinition assembly in assemblies)
        {
            if (context.ShouldExclude(assembly.Name.Name))
            {
                continue;
            }

            TypeDefinition? found = FindType(assembly.MainModule.Types, fullName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static TypeDefinition? FindType(IEnumerable<TypeDefinition> types, string fullName)
    {
        foreach (TypeDefinition type in types)
        {
            if (type.FullName == fullName)
            {
                return type;
            }

            TypeDefinition? nested = FindType(type.NestedTypes, fullName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static int CountCalls(
        MethodDefinition method,
        string declaringType,
        string methodName)
    {
        return method.Body.Instructions.Count(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
            instruction.Operand is MethodReference reference &&
            reference.DeclaringType.FullName == declaringType &&
            reference.Name == methodName);
    }

    private readonly record struct MethodContract(
        string TypeName,
        string MethodName,
        CallRequirement[] RequiredCalls);

    private readonly record struct CallRequirement(
        string DeclaringType,
        string MethodName,
        int MinimumCount);
}
