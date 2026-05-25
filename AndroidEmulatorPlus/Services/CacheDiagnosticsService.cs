using System.IO;

namespace AndroidEmulatorPlus.Services;

public sealed record CacheUsage(long TransferBytes, long BundleBytes, long RootBytes)
{
    public long Total => TransferBytes + BundleBytes + RootBytes;

    public string Human(long bytes) => bytes switch
    {
        < 1024L => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB",
    };
}

/// <summary>
/// Reports and clears the on-disk caches at
/// <c>%LOCALAPPDATA%\AndroidEmulatorPlus\{transfer,cache}\</c>.
///
/// Each long-running flow uses `try { ... } finally { Directory.Delete(...) }` for
/// cleanup, but a crash or cancellation can leave multi-GB tarballs / qcow2-like
/// artifacts on disk. The user used to have no visibility into this.
/// </summary>
public sealed class CacheDiagnosticsService
{
    private readonly LogService _log;

    public CacheDiagnosticsService(LogService log) => _log = log;

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus");

    public static string TransferDir => Path.Combine(Root, "transfer");
    public static string BundleDir => Path.Combine(Root, "transfer", "bundle-extract");
    public static string RootAvdDir => Path.Combine(Root, "cache");

    public CacheUsage Measure()
    {
        return new CacheUsage(
            TransferBytes: MeasureFolder(TransferDir, excludeChild: BundleDir),
            BundleBytes: MeasureFolder(BundleDir),
            RootBytes: MeasureFolder(RootAvdDir));
    }

    private static long MeasureFolder(string dir, string? excludeChild = null)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (excludeChild is not null && f.StartsWith(excludeChild, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { /* permission or in-use file — best effort */ }
        return total;
    }

    /// <summary>
    /// Deletes contents of <see cref="TransferDir"/> and <see cref="BundleDir"/>.
    /// Leaves the rootAVD/Magisk cache alone because re-cloning is expensive.
    /// Returns the number of bytes freed.
    /// </summary>
    public long ClearTransfer()
    {
        var before = MeasureFolder(TransferDir);
        TryDelete(TransferDir);
        Directory.CreateDirectory(TransferDir);
        _log.Info($"Migration transfer cache cleared ({Human(before)}).");
        return before;
    }

    /// <summary>Deletes the rootAVD git clone and Magisk download (cache/).</summary>
    public long ClearRootCache()
    {
        var before = MeasureFolder(RootAvdDir);
        TryDelete(RootAvdDir);
        Directory.CreateDirectory(RootAvdDir);
        _log.Info($"Root cache cleared ({Human(before)}). Next root flow will re-clone rootAVD + re-download Magisk.");
        return before;
    }

    private static void TryDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try { Directory.Delete(dir, recursive: true); }
        catch
        {
            // Fall back to file-by-file (e.g. when an emulator process still has a handle).
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(f); } catch { }
            }
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        }
    }

    private static string Human(long bytes) => new CacheUsage(0, 0, 0).Human(bytes);
}
