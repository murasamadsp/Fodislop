#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Fodinae;

internal static class PersistentAssetCacheFormat
{
    internal const int CurrentSchemaVersion = 1;
    internal const string MarkerFileName = ".format-version";
    internal const string LegacyBackupFileName = ".format-version.v0.backup";
    internal const string MigrationStagingFileName = ".format-version.migrate.tmp";

    internal static void EnsureCurrent(string cachePath)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            throw new ArgumentException("Asset cache path is required.", nameof(cachePath));
        }

        string normalizedPath = Path.GetFullPath(cachePath);
        Directory.CreateDirectory(normalizedPath);

        string markerPath = Path.Combine(normalizedPath, MarkerFileName);
        string backupPath = Path.Combine(normalizedPath, LegacyBackupFileName);
        string stagingPath = Path.Combine(normalizedPath, MigrationStagingFileName);
        if (File.Exists(markerPath))
        {
            ValidateVersionMarker(markerPath);
            DeleteStaleStaging(stagingPath);
            return;
        }

        // Schema v0 had no marker and used the same payload/etag layout as v1.
        // Back up that format state, not every potentially multi-gigabyte asset:
        // payload files do not change, so copying them would only freeze startup.
        if (!File.Exists(backupPath))
        {
            WriteDurably(backupPath, "0\n", createNew: true);
        }
        else
        {
            ValidateLegacyBackup(backupPath);
        }

        // The staging marker is durable before the atomic rename. A crash at
        // any point leaves all cache payloads untouched; the next call can
        // safely repeat this metadata-only commit.
        DeleteStaleStaging(stagingPath);
        WriteDurably(
            stagingPath,
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + "\n",
            createNew: true);
        File.Move(stagingPath, markerPath);
    }

    private static void ValidateVersionMarker(string markerPath)
    {
        string text = File.ReadAllText(markerPath, Encoding.UTF8).Trim();
        if (!int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version) ||
            version != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported asset cache schema '{text}' in '{markerPath}'; " +
                $"expected {CurrentSchemaVersion}.");
        }
    }

    private static void ValidateLegacyBackup(string backupPath)
    {
        string text = File.ReadAllText(backupPath, Encoding.UTF8).Trim();
        if (!string.Equals(text, "0", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Invalid asset cache migration backup '{backupPath}'.");
        }
    }

    private static void WriteDurably(
        string path,
        string value,
        bool createNew)
    {
        byte[] payload = Encoding.UTF8.GetBytes(value);
        using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        stream.Write(payload, 0, payload.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteStaleStaging(string stagingPath)
    {
        if (File.Exists(stagingPath))
        {
            File.Delete(stagingPath);
        }
    }
}
