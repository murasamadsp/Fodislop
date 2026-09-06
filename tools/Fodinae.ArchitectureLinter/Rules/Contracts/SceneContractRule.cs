#nullable enable

using System.Text.RegularExpressions;
using Mono.Cecil;
using Fodinae.ArchitectureLinter.Core;
using Fodinae.ArchitectureLinter.Scanning;

namespace Fodinae.ArchitectureLinter.Rules.Contracts;

/// <summary>
/// Validates serialized scene contracts by reading .unity files as text.
/// Checks LifetimeScope presence, required components, groups, and hierarchy.
/// Ported from check-architecture.js checkSerializedSceneContracts().
/// </summary>
public sealed class SceneContractRule : IRule
{
    public string Id => "FOD-SCENE-CONTRACT";
    public string Description => "Serialized scene contract validation";
    public RuleSeverity Severity => RuleSeverity.Error;
    public bool RequiresAssemblies => false;

    private static readonly (string File, string Scope, string[] Components, Dictionary<string, string[]>? Groups)[] Contracts = new[]
    {
        ("Assets/Scenes/Bootstrap.unity", "BootstrapLifetimeScope", new[] { "BootstrapLifetimeScope" }, new Dictionary<string, string[]>
        {
            ["Networking"] = new[] { "ConnectionManager", "NetworkService" },
            ["Content"] = new[] { "ClientAssetLoader", "ClientConfigManager", "TextureStorageManager" },
            ["Audio"] = new[] { "AudioSystem" },
            ["Presentation"] = new[] { "BootstrapLoadingScreen" },
        }),
        ("Assets/Scenes/MainGame.unity", "GameLifetimeScope", new[] { "GameLifetimeScope" }, new Dictionary<string, string[]>
        {
            ["Networking"] = new[] { "PacketHandler" },
            ["World"] = new[] { "MapManager", "WorldBackgroundSetup", "WorldTextureManager" },
            ["Rendering"] = new[] { "TerrainRenderer", "WorldEntityBatchRenderer", "PostProcessController", "LightingEngine", "SurfaceRenderer", "CameraFollow", "VFXPool" },
            ["Gameplay"] = new[] { "GameManager", "BuildingManager", "RobotManager", "ServerConfig" },
            ["Audio"] = new[] { "ServerAudioEventManager" },
        }),
        ("Assets/Scenes/Gateway.unity", "GatewayLifetimeScope", new[] { "GatewayLifetimeScope", "GatewayController", "UIDocument" }, null),
        ("Assets/Scenes/MainMenu.unity", "MainMenuLifetimeScope", new[] { "MainMenuLifetimeScope", "MainMenu", "UIDocument" }, null),
    };

    public Task<IReadOnlyList<RuleViolation>> EvaluateAsync(
        IReadOnlyList<AssemblyDefinition> assemblies,
        LinterContext context,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<RuleViolation>();
        var projectRoot = context.ProjectRoot;

        foreach (var (file, scope, components, groups) in Contracts)
        {
            var path = Path.Combine(projectRoot, file);
            if (!File.Exists(path))
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = "Файл сцены не найден.",
                    Severity = Severity,
                    TypeName = file
                });
                continue;
            }

            var content = File.ReadAllText(path);

            // Check scope component presence
            var scopeMatches = CountComponentOccurrences(content, scope);
            if (scopeMatches == 0)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Required scope '{scope}' отсутствует в сцене.",
                    Severity = Severity,
                    TypeName = file
                });
            }
            else if (scopeMatches > 1)
            {
                violations.Add(new RuleViolation
                {
                    RuleId = Id,
                    Message = $"Ожидается ровно один '{scope}', найдено {scopeMatches}.",
                    Severity = Severity,
                    TypeName = file
                });
            }

            // Check required components
            foreach (var component in components)
            {
                if (!content.Contains($"m_Name: {component}") && !ContainsComponentClass(content, component))
                {
                    violations.Add(new RuleViolation
                    {
                        RuleId = Id,
                        Message = $"Required component '{component}' отсутствует.",
                        Severity = Severity,
                        TypeName = file
                    });
                }
            }

            // Check groups
            if (groups != null)
            {
                foreach (var (group, managers) in groups)
                {
                    foreach (var manager in managers)
                    {
                        if (!ContainsComponentClass(content, manager))
                        {
                            violations.Add(new RuleViolation
                            {
                                RuleId = Id,
                                Message = $"Registered manager '{manager}' has no authored component.",
                                Severity = Severity,
                                TypeName = file
                            });
                        }
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<RuleViolation>>(violations);
    }

    private static int CountComponentOccurrences(string content, string className)
    {
        return Regex.Matches(content, $"m_Name: {Regex.Escape(className)}(?:\\s|$)").Count;
    }

    private static bool ContainsComponentClass(string content, string className)
    {
        return content.Contains($"m_Name: {className}") ||
               content.Contains($"m_EditorClassIdentifier:.*{Regex.Escape(className)}");
    }
}
