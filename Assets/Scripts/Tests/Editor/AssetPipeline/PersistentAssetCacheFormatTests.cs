#nullable enable

using System;
using System.IO;
using NUnit.Framework;

namespace Fodinae.Tests.AssetPipeline;

[TestFixture]
public sealed class PersistentAssetCacheFormatTests
{
    private string _testRoot = null!;
    private string _cachePath = null!;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"fodinae_asset_cache_format_{Guid.NewGuid():N}");
        _cachePath = Path.Combine(_testRoot, "AssetCache");
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Test]
    public void EnsureCurrent_MigratesV0AtomicallyAndPreservesBackup()
    {
        string relativeAsset = Path.Combine("Cells", "117.png");
        string assetPath = Path.Combine(_cachePath, relativeAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllBytes(assetPath, [1, 2, 3, 4]);
        File.WriteAllText(assetPath + ".etag", "legacy-etag");

        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);

        string backupPath = Path.Combine(
            _cachePath,
            PersistentAssetCacheFormat.LegacyBackupFileName);
        Assert.That(
            File.ReadAllText(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
        Assert.That(File.ReadAllBytes(Path.Combine(_cachePath, relativeAsset)), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(File.ReadAllText(Path.Combine(_cachePath, relativeAsset) + ".etag"), Is.EqualTo("legacy-etag"));
        Assert.That(File.ReadAllText(backupPath).Trim(), Is.EqualTo("0"));
    }

    [Test]
    public void EnsureCurrent_RecoversInterruptedMarkerCommitWithoutTouchingPayloads()
    {
        Directory.CreateDirectory(_cachePath);
        string payloadPath = Path.Combine(_cachePath, "legacy.bin");
        string backupPath = Path.Combine(
            _cachePath,
            PersistentAssetCacheFormat.LegacyBackupFileName);
        string stagingPath = Path.Combine(
            _cachePath,
            PersistentAssetCacheFormat.MigrationStagingFileName);
        File.WriteAllText(payloadPath, "legacy");
        File.WriteAllText(backupPath, "0");
        File.WriteAllText(stagingPath, "torn");

        PersistentAssetCacheFormat.EnsureCurrent(_cachePath);

        Assert.That(File.ReadAllText(payloadPath), Is.EqualTo("legacy"));
        Assert.That(File.ReadAllText(backupPath).Trim(), Is.EqualTo("0"));
        Assert.That(
            File.ReadAllText(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.MarkerFileName)).Trim(),
            Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion.ToString()));
        Assert.That(File.Exists(stagingPath), Is.False);
    }

    [Test]
    public void EnsureCurrent_RejectsUnknownSchemaWithoutMutatingCache()
    {
        Directory.CreateDirectory(_cachePath);
        string payloadPath = Path.Combine(_cachePath, "asset.bin");
        File.WriteAllText(payloadPath, "keep-me");
        File.WriteAllText(
            Path.Combine(_cachePath, PersistentAssetCacheFormat.MarkerFileName),
            "999");

        Assert.Throws<InvalidDataException>(
            () => PersistentAssetCacheFormat.EnsureCurrent(_cachePath));
        Assert.That(File.ReadAllText(payloadPath), Is.EqualTo("keep-me"));
        Assert.That(
            File.Exists(Path.Combine(
                _cachePath,
                PersistentAssetCacheFormat.LegacyBackupFileName)),
            Is.False);
    }
}
