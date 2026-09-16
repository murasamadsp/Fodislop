#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using Kern.Rendering;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Core;

// Всё, что клиент хранит в persistentDataPath, проверяется вместе на одной
// папке: конфиг, кэш ассетов и карта мира. Клиент выпускается целиком, и
// обновление должно проходить для всего набора файлов прошлого релиза, а не
// для каждого формата по отдельности.
//
// Два прошлых релиза:
//   N-1 — конфиг схемы 27, кэш ассетов v1, карта v1;
//   N-2 — конфиг схемы 26 (ещё с полями сжатия диапазона), кэш ассетов v0
//         без маркера, карта v0.
[TestFixture]
public sealed class InstallUpgradeTests
{
    private const string WorldCode = "install_test";
    private const int WorldWidth = 64;
    private const int WorldHeight = 32;
    private const string CachedAsset = "Cells/117.png";
    private static readonly CellType _StoredCell = (CellType)123;
    private static readonly byte[] _CachedPayload = [1, 2, 3, 4];

    private string _dataRoot = null!;
    private GraphicsQualityProfile _profile = null!;

    private string ConfigPath => Path.Combine(_dataRoot, "Config", "client_config.json");

    private string CachePath => Path.Combine(_dataRoot, "AssetCache");

    private string MapPath => Path.Combine(_dataRoot, WorldCode + ".map");

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"kern_install_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        _profile = GraphicsQualityProfileLoader.LoadRequired();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [Test]
    public void CleanInstall_CreatesCurrentFormatsWithoutBackups()
    {
        ClientConfigLoader.Result config = LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(_StoredCell)));

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.CreatedDefaults));
        Assert.That(new ClientConfigRepository(ConfigPath).Load().Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void SecondLaunch_AfterCleanInstall_RewritesNothing()
    {
        LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        byte[] configBytes = File.ReadAllBytes(ConfigPath);

        ClientConfigLoader.Result second = LoadConfig();
        _ = new PersistentAssetCache(CachePath);

        Assert.That(second.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
        Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(configBytes));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void Upgrade_FromPreviousRelease_MigratesEveryFormatAndKeepsPlayerData()
    {
        string previousConfig = WriteConfig(PreviousReleaseConfig(schemaVersion: 27, PixelSamplingMode.PixelPerfect));
        WriteCache(markerVersion: 1);
        WriteMap(formatVersion: 1);

        ClientConfigLoader.Result config = LoadConfig();
        var cache = new PersistentAssetCache(CachePath);

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(config.SourceSchemaVersion, Is.EqualTo(27));
        Assert.That(config.Config.Display.PixelSampling, Is.EqualTo(PixelSamplingMode.PixelPerfect));
        Assert.That(File.ReadAllText(ConfigBackupPath(27)), Is.EqualTo(previousConfig));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(File.Exists(Path.Combine(CachePath, PersistentAssetCacheFormat.VersionOneBackupFileName)), Is.True);

        // Запись v1 без манифеста инвалидируется лениво, при первом чтении:
        // старый файл не выдаётся за проверенный.
        Assert.That(cache.GetAsset(CachedAsset), Is.Null);
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.EqualTo(_StoredCell)));
        Assert.That(File.Exists(MapPath + ".v0.backup"), Is.False);
    }

    [Test]
    public void Upgrade_FromReleaseBeforePrevious_MigratesEveryFormatAndKeepsPlayerData()
    {
        string previousConfig = WriteConfig(WithRemovedToneMappingFields(
            PreviousReleaseConfig(schemaVersion: 26, PixelSamplingMode.Raw)));
        WriteCache(markerVersion: null);
        WriteMap(formatVersion: 0);

        ClientConfigLoader.Result config = LoadConfig();
        _ = new PersistentAssetCache(CachePath);

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(config.SourceSchemaVersion, Is.EqualTo(26));
        Assert.That(config.Config.Display.PixelSampling, Is.EqualTo(PixelSamplingMode.Raw));
        Assert.That(File.ReadAllText(ConfigBackupPath(26)), Is.EqualTo(previousConfig));
        Assert.That(File.ReadAllText(ConfigPath), Does.Not.Contain("ToneMapping"));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(File.Exists(Path.Combine(CachePath, PersistentAssetCacheFormat.LegacyBackupFileName)), Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(CachePath, CachedAsset)), Is.EqualTo(_CachedPayload));

        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.EqualTo(_StoredCell)));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(File.Exists(MapPath + ".v0.backup"), Is.True);
    }

    [Test]
    public void Upgrade_IsIdempotentAcrossRepeatedLaunches()
    {
        WriteConfig(PreviousReleaseConfig(schemaVersion: 26, PixelSamplingMode.Raw));
        WriteCache(markerVersion: null);
        WriteMap(formatVersion: 0);

        LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        WithMap(_ => { });
        byte[] configBytes = File.ReadAllBytes(ConfigPath);
        byte[] configBackup = File.ReadAllBytes(ConfigBackupPath(26));
        byte[] mapBackup = File.ReadAllBytes(MapPath + ".v0.backup");

        ClientConfigLoader.Result second = LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.EqualTo(_StoredCell)));

        Assert.That(second.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
        Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(configBytes));
        Assert.That(File.ReadAllBytes(ConfigBackupPath(26)), Is.EqualTo(configBackup));
        Assert.That(File.ReadAllBytes(MapPath + ".v0.backup"), Is.EqualTo(mapBackup));
    }

    [Test]
    public void DataFromNewerClient_IsRejectedAndLeftUntouched()
    {
        string newerConfig = WriteConfig(PreviousReleaseConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion + 1,
            PixelSamplingMode.SmoothFiltered));
        WriteCache(markerVersion: PersistentAssetCacheFormat.CurrentSchemaVersion + 1);
        WriteMap(formatVersion: WorldLayerFileHeader.CurrentFormatVersion + 1);
        byte[] mapBytes = File.ReadAllBytes(MapPath);

        Assert.Throws<InvalidDataException>(() => LoadConfig());
        Assert.Throws<InvalidDataException>(() => _ = new PersistentAssetCache(CachePath));
        Assert.That(() => WithMap(_ => { }), Throws.InstanceOf<IOException>());

        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(newerConfig));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion + 1));
        Assert.That(File.ReadAllBytes(MapPath), Is.EqualTo(mapBytes));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void CorruptConfig_IsRejectedAndLeftUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ \"SchemaVersion\": 27, ");

        Assert.Throws<InvalidDataException>(() => LoadConfig());

        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo("{ \"SchemaVersion\": 27, "));
        Assert.That(File.Exists(ConfigBackupPath(27)), Is.False);
    }

    private ClientConfigLoader.Result LoadConfig() =>
        new ClientConfigLoader(new ClientConfigRepository(ConfigPath), _profile).LoadOrCreate();

    private string ConfigBackupPath(int schemaVersion) =>
        ClientConfigLoader.MigrationBackupPath(ConfigPath, schemaVersion);

    // Файл прошлого релиза: те же секции, что пишет текущий клиент, но со
    // старым номером схемы. Поля между схемами 26 и 29 не добавлялись, только
    // удалялись (см. WithRemovedToneMappingFields).
    private string PreviousReleaseConfig(int schemaVersion, PixelSamplingMode pixelSampling)
    {
        ClientConfig config = ClientConfigDefaults.Create(_profile);
        config.GraphicsPreset = GraphicsPreset.Custom;
        config.Display.PixelSampling = pixelSampling;
        string json = JsonUtility.ToJson(config, prettyPrint: true);
        return Regex.Replace(
            json,
            "\"SchemaVersion\"\\s*:\\s*\\d+",
            $"\"SchemaVersion\": {schemaVersion}");
    }

    private static string WithRemovedToneMappingFields(string json)
    {
        json = InsertIntoSection(json, "Effects", "\"ToneMappingEnabled\": true");
        return InsertIntoSection(json, "PostProcess", "\"ToneMappingWhitePoint\": 1.0");
    }

    private static string InsertIntoSection(string json, string section, string field)
    {
        var pattern = new Regex($"\"{section}\"\\s*:\\s*\\{{");
        Match match = pattern.Match(json);
        Assert.That(match.Success, Is.True, $"Section {section} is missing from the serialized config.");
        return json.Insert(match.Index + match.Length, $"\n        {field},");
    }

    private string WriteConfig(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);
        return json;
    }

    // Кэш прошлых форматов: v0 — полезная нагрузка без маркера и манифеста,
    // v1 — то же с маркером «1».
    private void WriteCache(int? markerVersion)
    {
        string assetPath = Path.Combine(CachePath, CachedAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllBytes(assetPath, _CachedPayload);
        if (markerVersion.HasValue)
        {
            File.WriteAllText(
                Path.Combine(CachePath, PersistentAssetCacheFormat.MarkerFileName),
                markerVersion.Value + "\n");
        }
    }

    private int ReadCacheMarker() =>
        int.Parse(File.ReadAllText(Path.Combine(CachePath, PersistentAssetCacheFormat.MarkerFileName)).Trim());

    // Карта прошлого формата отличается от текущей только номером версии в
    // заголовке: пишем её текущим клиентом и подменяем номер.
    private void WriteMap(int formatVersion)
    {
        WithMap(storage =>
        {
            storage.SetCell(0, 0, _StoredCell);
            storage.Flush(durable: true);
        });

        using (var stream = new FileStream(MapPath, FileMode.Open, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Seek(WorldLayerFileHeader.FormatVersionOffset, SeekOrigin.Begin);
            writer.Write(formatVersion);
        }

        foreach (string backup in Directory.GetFiles(_dataRoot, WorldCode + ".backup.map"))
        {
            File.Delete(backup);
        }
    }

    private int ReadMapFormatVersion()
    {
        using var stream = new FileStream(MapPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        stream.Seek(WorldLayerFileHeader.FormatVersionOffset, SeekOrigin.Begin);
        return reader.ReadInt32();
    }

    private void WithMap(Action<MapStorage> use)
    {
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations, _dataRoot);
        try
        {
            storage.InitWorld(WorldCode, WorldWidth, WorldHeight);
            use(storage);
        }
        finally
        {
            storage.Dispose();
        }
    }
}
