#nullable enable

using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using NUnit.Framework;

namespace Fodinae.Tests.Core;

public sealed class ClientConfigRepositoryTests
{
    private string _directory = null!;
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"fodinae-config-repository-{System.Guid.NewGuid():N}");
        _configPath = Path.Combine(_directory, "client_config.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void SaveThenLoad_RoundTripsConfigAndRemovesTemporaryFile()
    {
        var repository = new ClientConfigRepository(_configPath);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ServerHost = "example.test",
            ServerPort = 4242,
        };

        repository.Save(config);
        ClientConfig loaded = repository.Load();

        Assert.That(loaded.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(loaded.ServerHost, Is.EqualTo("example.test"));
        Assert.That(loaded.ServerPort, Is.EqualTo(4242));
        Assert.That(File.Exists(_configPath + ".tmp"), Is.False);
    }

    [Test]
    public void Load_RenamesLegacyUiKeysBeforeDeserialization()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _configPath,
            "{\"SchemaVersion\":15,\"UiScale\":1.25,\"UiVolume\":0.75}");
        var repository = new ClientConfigRepository(_configPath);

        ClientConfig loaded = repository.Load();

        Assert.That(loaded.UIScale, Is.EqualTo(1.25f));
        Assert.That(loaded.UIVolume, Is.EqualTo(0.75f));
    }

    [Test]
    public void Save_WithBackup_ReplacesExistingFileAndPreservesPreviousPayload()
    {
        var repository = new ClientConfigRepository(_configPath);
        var first = new ClientConfig { ServerPort = 1001 };
        var second = new ClientConfig { ServerPort = 1002 };
        string backupPath = _configPath + ".v14.backup";
        repository.Save(first);

        repository.Save(second, backupPath);

        Assert.That(repository.Load().ServerPort, Is.EqualTo(1002));
        var backupRepository = new ClientConfigRepository(backupPath);
        Assert.That(backupRepository.Load().ServerPort, Is.EqualTo(1001));
    }
}
