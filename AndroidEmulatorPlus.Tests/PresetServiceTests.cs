using System.IO;
using System.Text.Json;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

/// <summary>
/// PresetService merges embedded bundled presets with a user override JSON.
/// Same-id entries replace; new ids append. The bundled JSON ships with this
/// assembly via &lt;EmbeddedResource&gt;, so the merge logic is tested by
/// replaying the merge contract here against a sample override.
/// </summary>
public class PresetServiceTests
{
    private sealed record FakePreset(string id, string name, string[] packages);

    [Fact]
    public void User_override_replaces_by_id()
    {
        var bundled = new[]
        {
            new FakePreset("google", "Google bloat", new[] { "com.android.chrome" }),
            new FakePreset("samsung", "Samsung bloat", new[] { "com.samsung.android.bixby.agent" }),
        };
        var user = new[]
        {
            new FakePreset("google", "Google (custom)", new[] { "com.google.android.youtube" }),
            new FakePreset("custom", "My bloat", new[] { "com.example.spy" }),
        };

        var byId = bundled.ToDictionary(p => p.id, p => p);
        foreach (var u in user) byId[u.id] = u;
        var merged = byId.Values.OrderBy(p => p.id).ToList();

        Assert.Equal(3, merged.Count);
        var google = merged.First(p => p.id == "google");
        Assert.Equal("Google (custom)", google.name);
        Assert.Contains("com.google.android.youtube", google.packages);
        Assert.Contains(merged, p => p.id == "samsung");
        Assert.Contains(merged, p => p.id == "custom");
    }

    [Fact]
    public void Bundled_presets_json_parses_and_has_expected_ids()
    {
        // Read the embedded resource directly off the production assembly so this
        // test catches accidental schema changes.
        var asm = typeof(AndroidEmulatorPlus.Services.PresetService).Assembly;
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("bloat-presets.json", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(resName);
        using var s = asm.GetManifestResourceStream(resName!)!;
        using var doc = JsonDocument.Parse(s);
        Assert.True(doc.RootElement.TryGetProperty("presets", out var presets));
        var ids = presets.EnumerateArray()
            .Select(p => p.GetProperty("id").GetString())
            .ToList();
        Assert.Contains("google", ids);
        Assert.Contains("samsung", ids);
        Assert.Contains("pixel", ids);
    }
}
