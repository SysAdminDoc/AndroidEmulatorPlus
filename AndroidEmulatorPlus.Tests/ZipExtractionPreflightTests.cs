using System.IO;
using System.IO.Compression;
using AndroidEmulatorPlus.Helpers;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class ZipExtractionPreflightTests
{
    [Fact]
    public void TryValidateZipForExtraction_accepts_safe_bundle_archive()
    {
        var zip = WriteZip(("base.apk", "apk"), ("manifest.json", """{"package_name":"com.example.app"}"""));
        try
        {
            Assert.True(AppService.TryValidateZipForExtraction(zip, out var detail), detail);
        }
        finally { File.Delete(zip); }
    }

    [Theory]
    [InlineData("../evil.apk", "traversal")]
    [InlineData("/sdcard/evil.apk", "absolute")]
    [InlineData("C:/Temp/evil.apk", "absolute")]
    [InlineData("safe/..\\evil.apk", "traversal")]
    public void TryValidateZipForExtraction_rejects_unsafe_paths(string name, string expected)
    {
        var zip = WriteZip((name, "apk"));
        try
        {
            Assert.False(AppService.TryValidateZipForExtraction(zip, out var detail));
            Assert.Contains(expected, detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateZipForExtraction_rejects_entry_count_limit()
    {
        var zip = WriteZip(("one.apk", "apk"), ("two.apk", "apk"));
        try
        {
            Assert.False(AppService.TryValidateZipForExtraction(zip, out var detail, maxEntries: 1));
            Assert.Contains("too many entries", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateZipForExtraction_rejects_uncompressed_size_limit()
    {
        var zip = WriteZip(("base.apk", "0123456789"));
        try
        {
            Assert.False(AppService.TryValidateZipForExtraction(zip, out var detail, maxTotalUncompressedBytes: 5));
            Assert.Contains("size exceeds", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void TryValidateZipForExtraction_rejects_extreme_compression_ratio()
    {
        var zip = WriteZip(("base.apk", new string('A', 2 * 1024 * 1024)));
        try
        {
            Assert.False(AppService.TryValidateZipForExtraction(zip, out var detail, maxCompressionRatio: 2));
            Assert.Contains("compression ratio", detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void ExtractBundle_rejects_zip_slip_before_creating_bundle_work_result()
    {
        var service = new AppService(new AdbService(new SdkLocator(), new LogService()), new LogService());
        var zip = WriteZip(("../base.apk", "apk"));
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => service.ExtractBundle(zip));
            Assert.Contains("rejected before extraction", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void ExtractBundle_accepts_valid_zip_after_preflight()
    {
        var service = new AppService(new AdbService(new SdkLocator(), new LogService()), new LogService());
        var zip = WriteZip(("base.apk", "apk"));
        AppService.BundleExtractResult? result = null;
        try
        {
            result = service.ExtractBundle(zip);

            Assert.Single(result.Apks);
            Assert.Equal("base.apk", Path.GetFileName(result.Apks[0]));
        }
        finally
        {
            if (result is not null)
                Directory.Delete(result.WorkDir, true);
            File.Delete(zip);
        }
    }

    [Fact]
    public async Task ImportAppData_rejects_zip_slip_before_adb_work()
    {
        var adb = new CountingAdb();
        var service = new AppService(adb, new LogService());
        var zip = WriteZip(("../metadata.json", "{}"));
        try
        {
            var ok = await service.ImportAppDataAsync("emu", zip);

            Assert.False(ok);
            Assert.Equal(0, adb.Calls);
        }
        finally { File.Delete(zip); }
    }

    private static string WriteZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aep-zip-preflight-{Guid.NewGuid():N}.zip");
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

    private sealed class CountingAdb : AdbService
    {
        public CountingAdb() : base(new SdkLocator(), new LogService()) { }

        public int Calls { get; private set; }

        public override Task<ProcessResult> PushAsync(string serial, string local, string remote, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ProcessResult(0, "ok", ""));
        }

        public override Task<ProcessResult> ShellAsync(string serial, string command, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ProcessResult(0, "ok", ""));
        }

        public override Task<ProcessResult> RootShellAsync(string serial, string command, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ProcessResult(0, "ok", ""));
        }
    }
}
