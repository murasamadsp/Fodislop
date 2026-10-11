#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using Kern.Persistence;
using MinesServer.Data;
using UnityEditor;
using UnityEngine;

namespace Kern.Tests.World;

/// <summary>Disposable batch snapshot only: isolate persistent stores and provide a bounded, known world input.</summary>
public static class SurfaceSceneProofAuthoring
{
    public static void Prepare()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !Path.GetFileName(root).StartsWith("kern-surface-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Surface proof input may be authored only in a disposable kern-surface-* batch snapshot.");
        }

        PlayerSettings.companyName = "KernSurfaceProof";
        PlayerSettings.productName = "SurfaceProof20261007";
        using var map = new MemoryStream();
        using (var writer = new BinaryWriter(map, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(16);
            writer.Write(8);
            writer.Write(32);
            writer.Write(WorldLayerFileHeader.LegacyRLEFormatVersion);
            const long HeaderBytes = 16 + 16 * 8 * 8;
            for (int chunk = 0; chunk < 128; chunk++) { writer.Write(HeaderBytes + chunk * 3); }
            for (int chunk = 0; chunk < 128; chunk++)
            {
                writer.Write((ushort)1024);
                writer.Write((byte)CellType.BlackRock);
            }
        }

        string path = Path.Combine(Application.streamingAssetsPath, "WorldMaps/pallada_cells.zip");
        using (var archive = ZipFile.Open(path + ".surface-proof", ZipArchiveMode.Create))
        {
            using Stream entry = archive.CreateEntry("pallada_cells.mapb").Open();
            map.Position = 0;
            map.CopyTo(entry);
        }
        File.Copy(path + ".surface-proof", path, overwrite: true);
        File.Delete(path + ".surface-proof");
        AssetDatabase.Refresh();
        Debug.Log("[SurfaceSceneProof] Snapshot uses isolated persistent stores and a 512x256 BlackRock world fixture.");
    }
}
