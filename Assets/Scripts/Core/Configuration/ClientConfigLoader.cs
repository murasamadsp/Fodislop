#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;

// Путь конфига на старте: чистая установка, обновление или обычный запуск.
//
// Отделён от ClientConfigManager, чтобы установку и обновление можно было
// проверить на временной папке, без MonoBehaviour и persistentDataPath.
internal sealed class ClientConfigLoader
{
    private readonly ClientConfigRepository _repository;
    private readonly ClientConfigMigration _migration;
    private readonly ClientConfigValidator _validator;
    private readonly GraphicsQualityProfile _graphicsQualityProfile;

    public ClientConfigLoader(ClientConfigRepository repository, GraphicsQualityProfile graphicsQualityProfile)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _graphicsQualityProfile = graphicsQualityProfile ??
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        _migration = new ClientConfigMigration(graphicsQualityProfile);
        _validator = new ClientConfigValidator(graphicsQualityProfile);
    }

    public enum Outcome
    {
        CreatedDefaults,
        Loaded,
        Migrated,
    }

    public readonly record struct Result(ClientConfig Config, Outcome Outcome, int SourceSchemaVersion);

    public static string MigrationBackupPath(string configPath, int sourceSchemaVersion) =>
        $"{configPath}.v{sourceSchemaVersion}.backup";

    // Файл, который нельзя прочитать, провалидировать или который новее
    // клиента, не переписывается: исключение уходит наверх, диск не тронут.
    public Result LoadOrCreate()
    {
        if (!_repository.Exists)
        {
            ClientConfig defaults = ClientConfigDefaults.Create(_graphicsQualityProfile);
            _validator.Validate(defaults);
            _repository.Save(defaults);
            return new Result(defaults, Outcome.CreatedDefaults, ClientConfig.CurrentSchemaVersion);
        }

        ClientConfigRepository.LoadedConfig loaded = _repository.Load();
        int sourceSchemaVersion = loaded.Config.SchemaVersion;
        bool migrated = _migration.Migrate(loaded.Config, loaded.Json);
        _validator.Validate(loaded.Config);
        if (!migrated)
        {
            return new Result(loaded.Config, Outcome.Loaded, sourceSchemaVersion);
        }

        _repository.Save(loaded.Config, MigrationBackupPath(_repository.ConfigPath, sourceSchemaVersion));
        return new Result(loaded.Config, Outcome.Migrated, sourceSchemaVersion);
    }
}
