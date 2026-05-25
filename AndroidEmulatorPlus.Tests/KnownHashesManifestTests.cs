using System.Reflection;
using System.Text.Json;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

/// <summary>
/// Catches accidental schema regressions in the embedded Resources/known-hashes.json
/// and asserts that the v0.2.0 Magisk entry is populated (not TOFU-only).
/// </summary>
public class KnownHashesManifestTests
{
    [Fact]
    public void Manifest_parses_and_has_v30_7_magisk_hash()
    {
        var asm = typeof(AndroidEmulatorPlus.Services.HashVerificationService).Assembly;
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("known-hashes.json", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(resName);
        using var s = asm.GetManifestResourceStream(resName!)!;
        using var doc = JsonDocument.Parse(s);

        Assert.True(doc.RootElement.TryGetProperty("magisk", out var m));
        // v30.7 is the pinned hash at v0.2.0 release time. New entries can append,
        // but this entry must not be removed without a CHANGELOG note.
        Assert.True(m.TryGetProperty("Magisk-v30.7.apk", out var v307));
        Assert.False(string.IsNullOrWhiteSpace(v307.GetString()));
        Assert.Equal(64, v307.GetString()!.Length); // SHA-256 hex length.

        Assert.True(doc.RootElement.TryGetProperty("cmdlineTools", out var c));
        // The current Studio-page URL (14742923) must have a hash entry.
        Assert.True(c.TryGetProperty(
            "https://dl.google.com/android/repository/commandlinetools-win-14742923_latest.zip",
            out var cli));
        Assert.False(string.IsNullOrWhiteSpace(cli.GetString()));
        Assert.Equal(64, cli.GetString()!.Length);
    }
}
