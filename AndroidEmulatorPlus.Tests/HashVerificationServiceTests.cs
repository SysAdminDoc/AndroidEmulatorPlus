using System.IO;
using System.Reflection;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class HashVerificationServiceTests
{
    [Fact]
    public void ComputeSha256_matches_known_value()
    {
        // SHA-256 of the literal string "hello".
        var path = Path.Combine(Path.GetTempPath(), $"aep-hash-{System.Guid.NewGuid():N}.bin");
        File.WriteAllText(path, "hello");
        try
        {
            var h = HashVerificationService.ComputeSha256(path);
            Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", h);
        }
        finally { File.Delete(path); }
    }
}
