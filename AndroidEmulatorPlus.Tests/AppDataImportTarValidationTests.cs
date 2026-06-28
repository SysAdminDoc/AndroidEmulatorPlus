using System.IO;
using System.Formats.Tar;
using System.IO.Compression;
using AndroidEmulatorPlus.Helpers;
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

    [Fact]
    public async Task ImportAppData_CleansRemoteTar_WhenUidLookupFails()
    {
        var adb = new ImportCleanupFakeAdb();
        var service = new AppService(adb, new LogService());
        var zip = WriteImportZip("com.example.app");
        try
        {
            var ok = await service.ImportAppDataAsync("emu", zip);

            Assert.False(ok);
            Assert.Contains("/sdcard/aep-import-com.example.app.tar", adb.Cleanup);
        }
        finally
        {
            try { File.Delete(zip); } catch { }
        }
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

    private static string WriteImportZip(string pkg)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aep-importzip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var zip = Path.Combine(Path.GetTempPath(), $"aep-importzip-{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(Path.Combine(root, "metadata.json"),
                $$"""{ "package": "{{pkg}}", "uid": 10001, "exported": "2026-01-01T00:00:00Z", "version": 1 }""");
            var tar = Path.Combine(root, $"{pkg}.tar");
            using (var fs = File.Create(tar))
            using (var writer = new TarWriter(fs, TarEntryFormat.Pax, leaveOpen: false))
            {
                writer.WriteEntry(FileEntry($"{pkg}/files/prefs.xml", "ok"));
            }
            ZipFile.CreateFromDirectory(root, zip);
            return zip;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private sealed class ImportCleanupFakeAdb : AdbService
    {
        public ImportCleanupFakeAdb() : base(new SdkLocator(), new LogService()) { }

        public List<string> Cleanup { get; } = new();

        public override Task<ProcessResult> PushAsync(string serial, string local, string remote, CancellationToken ct = default)
            => Task.FromResult(new ProcessResult(0, "ok", ""));

        public override Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default)
            => Task.FromResult(new ProcessResult(0, "ok", ""));

        public override Task<ProcessResult> RootShellAsync(string serial, string command, CancellationToken ct = default)
        {
            if (command.Contains("rm -f ", StringComparison.Ordinal))
            {
                Cleanup.Add(command[(command.IndexOf("rm -f ", StringComparison.Ordinal) + "rm -f ".Length)..].Trim().Trim('\''));
                return Task.FromResult(new ProcessResult(0, "ok", ""));
            }
            if (command.Contains("stat -c %u", StringComparison.Ordinal))
                return Task.FromResult(new ProcessResult(1, "", "missing"));
            return Task.FromResult(new ProcessResult(0, "ok", ""));
        }
    }
}
