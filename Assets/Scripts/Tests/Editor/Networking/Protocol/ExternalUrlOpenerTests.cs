#nullable enable

using Kern.Networking;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class ExternalUrlOpenerTests
{
    [TestCase("https://example.com/update")]
    [TestCase("http://example.com/download?v=2")]
    [TestCase("HTTPS://Example.com")]
    public void AllowsWebAddresses(string url)
    {
        Assert.That(ExternalUrlOpener.IsAllowed(url), Is.True);
    }

    [TestCase("file:///C:/Windows/System32/calc.exe")]
    [TestCase("file:///Applications/Calculator.app")]
    [TestCase(@"\\attacker\share\payload.exe")]
    [TestCase("ms-msdt:/id PCWDiagnostic")]
    [TestCase("search-ms:query=x&crumb=location:\\\\attacker\\share")]
    [TestCase("javascript:alert(1)")]
    [TestCase("ftp://example.com/file")]
    [TestCase("example.com/no-scheme")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void RejectsEverythingElse(string? url)
    {
        Assert.That(ExternalUrlOpener.IsAllowed(url), Is.False);
    }
}
