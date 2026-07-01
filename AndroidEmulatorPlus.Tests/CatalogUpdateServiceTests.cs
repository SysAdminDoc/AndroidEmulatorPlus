using System.Text.Json;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class CatalogUpdateServiceTests
{
    [Fact]
    public void CatalogManifest_round_trips_through_json()
    {
        var manifest = new CatalogManifest
        {
            Version = 2,
            UpdatedUtc = "2026-07-01T00:00:00Z",
            Files =
            {
                new CatalogFileEntry
                {
                    Name = "bloat-presets.json",
                    Sha256 = "abc123def456",
                    Url = "https://example.com/bloat-presets.json",
                    Size = 4096,
                },
            },
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<CatalogManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Version);
        Assert.Single(deserialized.Files);
        Assert.Equal("bloat-presets.json", deserialized.Files[0].Name);
        Assert.Equal("abc123def456", deserialized.Files[0].Sha256);
    }

    [Theory]
    [InlineData("../../../etc/passwd", true)]
    [InlineData("bloat-presets.json", false)]
    [InlineData("sub/dir.json", true)]
    [InlineData("normal.json", false)]
    public void CatalogFileEntry_path_traversal_detection(string name, bool shouldReject)
    {
        var hasTraversal = name.Contains("..") || name.Contains('/') || name.Contains('\\');
        Assert.Equal(shouldReject, hasTraversal);
    }
}
