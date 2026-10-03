#nullable enable

namespace Kern.Tests.World;

using System;
using System.IO;
using Kern.Persistence;
using NUnit.Framework;

[TestFixture]
public sealed class WorldLayerFileVisitorTests
{
    [Test]
    public void VisitChunkRuns_VisitorInvalidDataExceptionIsNotReportedAsChunkCorruption()
    {
        string filePath = Path.Combine(
            Path.GetTempPath(),
            "kern_visitor_exception_" + Guid.NewGuid().ToString("N") + ".map");
        var lifetime = new WorldLayerLifetime();
        var layerFile = new WorldLayerFile<byte>(
            filePath,
            widthChunks: 1,
            heightChunks: 1,
            chunkSize: 2,
            openFile: path => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read),
            lifetime: lifetime);

        try
        {
            layerFile.Initialize();
            layerFile.Save(0, new byte[] { 7, 7, 7, 7 }, chunkArea: 4);

            int callbackCount = 0;
            bool corrupted = true;
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                layerFile.VisitChunkRuns(
                    0,
                    chunkArea: 4,
                    visitor: (_, _, _) =>
                    {
                        callbackCount++;
                        throw new InvalidDataException("visitor failure");
                    },
                    corrupted: out corrupted))!;

            Assert.That(exception.Message, Is.EqualTo("visitor failure"));
            Assert.That(callbackCount, Is.EqualTo(1));
            Assert.That(corrupted, Is.False);
        }
        finally
        {
            layerFile.DisposeStreams();
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
