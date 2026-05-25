using System.IO;
using System.IO.Compression;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class MagiskModuleZipValidationTests
{
    [Fact]
    public void TryValidateModuleZip_accepts_module_prop_at_archive_root()
    {
        var zip = WriteZip(("module.prop", "id=test\nname=Test\nversion=1\nversionCode=1\n"));
        try
        {
            Assert.True(MagiskService.TryValidateModuleZip(zip, out var detail), detail);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateModuleZip_rejects_missing_module_prop()
    {
        var zip = WriteZip(("common/service.sh", "#!/system/bin/sh\n"));
        try
        {
            Assert.False(MagiskService.TryValidateModuleZip(zip, out var detail));
            Assert.Contains("module.prop", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateModuleZip_rejects_module_prop_directory()
    {
        var zip = WriteZip(("module.prop/", ""));
        try
        {
            Assert.False(MagiskService.TryValidateModuleZip(zip, out var detail));
            Assert.Contains("module.prop", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateModuleZip_rejects_path_traversal_entries()
    {
        var zip = WriteZip(
            ("module.prop", "id=test\nname=Test\nversion=1\nversionCode=1\n"),
            ("../evil.sh", "touch /data/local/tmp/pwned\n"));
        try
        {
            Assert.False(MagiskService.TryValidateModuleZip(zip, out var detail));
            Assert.Contains("traversal", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    private static string WriteZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aep-module-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (name.EndsWith("/", StringComparison.Ordinal)) continue;
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return path;
    }
}
