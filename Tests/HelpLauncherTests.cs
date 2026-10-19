using System;
using System.IO;
using RegCompare.Services;
using Xunit;

namespace RegCompare.Tests;

public class HelpLauncherTests
{
    // The page ships inside the executable, in both languages, and loads nothing from the network.
    [Fact]
    public void WritePage_WritesTheEmbeddedBilingualOfflinePage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rfc help " + Guid.NewGuid().ToString("N"));
        try
        {
            var path = HelpLauncher.WritePage(dir);

            Assert.True(File.Exists(path));
            var html = File.ReadAllText(path);
            Assert.StartsWith("<!doctype html>", html);
            Assert.Contains("data-l=\"en\"", html);
            Assert.Contains("data-l=\"fr\"", html);
            Assert.DoesNotContain("src=\"http", html);
            Assert.DoesNotContain("@LOGO@", html);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
