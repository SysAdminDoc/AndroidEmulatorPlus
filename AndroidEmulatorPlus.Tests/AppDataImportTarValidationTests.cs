using System.IO;
using System.Formats.Tar;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class AppDataImportTarValidationTests
{
    [Fact]
    public void TryValidateDataTarForImport_accepts_entries_under_declared_package()
    {
        var tar = WriteTar(
            new PaxTarEntry(TarEntryType.Directory, "com.example.app"),
            FileEntry("com.example.app/files/prefs.xml", "ok"));
        try
        {
            Assert.True(AppService.TryValidateDataTarForImport(tar, "com.example.app", out var detail), detail);
        }
        finally { File.Delete(tar); }
    }

    [Fact]
    public void TryValidateDataTarForImport_rejects_path_traversal_entries()
    {
        var tar = WriteTar(FileEntry("com.example.app/../../data/system/users.xml", "nope"));
        try
        {
            Assert.False(AppService.TryValidateDataTarForImport(tar, "com.example.app", out var detail));
            Assert.Contains("traversal", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(tar); }
    }

    [Fact]
    public void TryValidateDataTarForImport_rejects_entries_outside_package()
    {
        var tar = WriteTar(FileEntry("com.other.app/files/prefs.xml", "nope"));
        try
        {
            Assert.False(AppService.TryValidateDataTarForImport(tar, "com.example.app", out var detail));
            Assert.Contains("outside package", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(tar); }
    }

    [Fact]
    public void TryValidateDataTarForImport_rejects_symlinks()
    {
        var tar = WriteTar(new PaxTarEntry(TarEntryType.SymbolicLink, "com.example.app/files/link")
        {
            LinkName = "/data/system/users.xml",
        });
        try
        {
            Assert.False(AppService.TryValidateDataTarForImport(tar, "com.example.app", out var detail));
            Assert.Contains("unsafe entry type", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(tar); }
    }

    private static PaxTarEntry FileEntry(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(bytes),
        };
    }

    private static string WriteTar(params TarEntry[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aep-tar-{Guid.NewGuid():N}.tar");
        using var fs = File.Create(path);
        using var writer = new TarWriter(fs, TarEntryFormat.Pax, leaveOpen: false);
        foreach (var entry in entries)
            writer.WriteEntry(entry);
        return path;
    }
}
