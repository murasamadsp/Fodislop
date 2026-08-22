#nullable enable

using System;
using System.IO;
using NUnit.Framework;

namespace Fodinae.Tests.World
{
    [TestFixture]
    public class WorldLayerRleTests
    {
        private string _tempFilePath = null!;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"world_layer_test_{Guid.NewGuid():N}.mapb");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
            {
                try
                {
                    File.Delete(_tempFilePath);
                }
                catch
                {
                    // Ignored in cleanup
                }
            }
        }

        [Test]
        public void SetAndGet_SingleCell_ReturnsWrittenValue()
        {
            using (var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, CHUNK_SIZE: 32))
            {
                layer.SetCell(5, 5, 42);
                Assert.AreEqual(42, layer.GetCellSync(5, 5));
                Assert.AreEqual(0, layer.GetCellSync(0, 0));
            }
        }

        [Test]
        public void FlushAndReopen_PersistsRleEncodedData()
        {
            const ushort tileTypeA = 101;
            const ushort tileTypeB = 202;

            // Write and flush
            using (var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, CHUNK_SIZE: 32))
            {
                // Write a uniform block
                for (int x = 0; x < 32; x++)
                {
                    for (int y = 0; y < 16; y++)
                    {
                        layer.SetCell(x, y, tileTypeA);
                    }

                    for (int y = 16; y < 32; y++)
                    {
                        layer.SetCell(x, y, tileTypeB);
                    }
                }

                layer.Flush();
            }

            // Reopen and verify
            using (var reopenedLayer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, CHUNK_SIZE: 32))
            {
                for (int x = 0; x < 32; x++)
                {
                    for (int y = 0; y < 16; y++)
                    {
                        Assert.AreEqual(tileTypeA, reopenedLayer.GetCellSync(x, y), $"Mismatch at ({x}, {y})");
                    }

                    for (int y = 16; y < 32; y++)
                    {
                        Assert.AreEqual(tileTypeB, reopenedLayer.GetCellSync(x, y), $"Mismatch at ({x}, {y})");
                    }
                }
            }
        }

        [Test]
        public void OutOfBounds_ThrowsArgumentOutOfRangeException()
        {
            using var layer = new WorldLayer<ushort>(_tempFilePath, WIDTH_CHUNKS: 2, HEIGHT_CHUNKS: 2, CHUNK_SIZE: 32);

            Assert.Throws<ArgumentOutOfRangeException>(() => layer.GetCellSync(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => layer.GetCellSync(64, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetCell(0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetCell(0, 64, 1));
        }
    }
}
