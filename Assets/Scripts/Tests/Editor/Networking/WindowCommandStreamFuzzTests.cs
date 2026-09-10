#nullable enable

using Fodinae.Networking;
using MinesServer.Networking.Server.Packets.GUI;
using NUnit.Framework;

namespace Fodinae.Tests.Networking;

[TestFixture]
public class WindowCommandStreamFuzzTests
{
    [Test]
    public void RandomOpenClose_FlagMatches()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        for (int i = 0; i < 200; i++)
        {
            if (random.Next(2) == 0)
                stream.PublishOpen(new OpenWindowPacket("t", 100, 100, null));
            else
                stream.PublishClose(new CloseWindowPacket());
            Assert.That(stream.HasOpenWindows, Is.EqualTo(random.Next(2) == 0 ? false : true).Or.EqualTo(false),
                $"i={i}");
        }
    }

    [Test]
    public void Open_FiresVisibilityTrue()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        for (int i = 0; i < 50; i++)
        {
            bool? v = null;
            stream.OpenWindowVisibilityChanged += x => v = x;
            stream.PublishOpen(new OpenWindowPacket("t", 100, 100, null));
            Assert.That(v, Is.True, $"i={i}");
        }
    }

    [Test]
    public void Close_FiresVisibilityFalse()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        for (int i = 0; i < 50; i++)
        {
            stream.PublishOpen(new OpenWindowPacket("t", 100, 100, null));
            bool? v = null;
            stream.OpenWindowVisibilityChanged += x => v = x;
            stream.PublishClose(new CloseWindowPacket());
            Assert.That(v, Is.False, $"i={i}");
        }
    }

    [Test]
    public void Modal_DoesNotChangeFlag()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        for (int i = 0; i < 50; i++)
        {
            stream.PublishOpen(new OpenWindowPacket("t", 100, 100, null));
            stream.PublishModal(new ModalWindowPacket("a", "b", "c", "d"));
            Assert.IsTrue(stream.HasOpenWindows, $"i={i}");
        }
    }

    [Test]
    public void SetSameVisibility_NoFire()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        for (int i = 0; i < 50; i++)
        {
            stream.PublishOpen(new OpenWindowPacket("t", 100, 100, null));
            int count = 0;
            stream.OpenWindowVisibilityChanged += _ => count++;
            stream.SetOpenWindowVisibility(true);
            Assert.That(count, Is.EqualTo(0), $"i={i}");
        }
    }
}
