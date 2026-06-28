using System.IO;
using System.Text.Json;
using SharpCompress.Compressors.Xz;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Downloads the matching frida-server build, pushes it to the emulator, and
/// starts it through Magisk root.
/// </summary>
public sealed class FridaService
{
    private readonly AdbService _adb;
    private readonly DownloadService _dl;
    private readonly LogService _log;
    private readonly HashVerificationService _hash;

    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "cache", "frida");

    public sealed record FridaReleaseAsset(string Tag, string Url, string Name, string? GitHubDigestSha256);

    public FridaService(AdbService adb, DownloadService dl, LogService log, HashVerificationService hash)
    {
        _adb = adb;
        _dl = dl;
        _log = log;
        _hash = hash;
    }

    public async Task<string?> DetectArchAsync(string serial, CancellationToken ct = default)
    {
        var r = await _adb.ShellAsync(serial, "getprop ro.product.cpu.abi", ct);
        return MapAbiToFridaArch(r.StdOut.Trim().Replace("\r", "", StringComparison.Ordinal));
    }

    public static string? MapAbiToFridaArch(string abi)
        => abi switch
        {
            "x86_64" => "x86_64",
            "x86" => "x86",
            "arm64-v8a" => "arm64",
            "armeabi-v7a" => "arm",
            _ when string.IsNullOrWhiteSpace(abi) => null,
            _ => abi,
        };

    public async Task<FridaReleaseAsset?> ResolveLatestAsync(string arch,
        CancellationToken ct = default)
    {
        try
        {
            var json = await _dl.FetchTextAsync(
                "https://api.github.com/repos/frida/frida/releases/latest", ct);
            var selected = TrySelectReleaseAsset(json, arch);
            if (selected is not null) return selected;
            _log.Warning($"No frida-server asset found for arch={arch}.");
        }
        catch (Exception ex)
        {
            _log.Warning($"Frida release lookup failed: {ex.Message}");
        }
        return null;
    }

    public static FridaReleaseAsset? TrySelectReleaseAsset(string releaseJson, string arch)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var pattern = $"frida-server-{tag.TrimStart('v')}-android-{arch}.xz";
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.Equals(pattern, StringComparison.OrdinalIgnoreCase)) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(url)) return null;
            string? digest = null;
            if (asset.TryGetProperty("digest", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                digest = HashVerificationService.NormalizeSha256Digest(dProp.GetString());
            return new FridaReleaseAsset(tag, url, name, digest);
        }
        return null;
    }

    public async Task<bool> DeployAsync(string serial,
        IProgress<string>? progress = null,
        IProgress<(long received, long? total)>? downloadProgress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Detecting emulator architecture...");
        var arch = await DetectArchAsync(serial, ct);
        if (arch is null)
        {
            _log.Error("Could not detect emulator CPU architecture.");
            return false;
        }
        _log.Info($"Emulator architecture: {arch}");

        progress?.Report($"Looking up latest Frida release for {arch}...");
        var release = await ResolveLatestAsync(arch, ct);
        if (release is null) return false;

        var tag = release.Tag;
        var url = release.Url;
        _log.Info($"Frida {tag} for android-{arch}");

        Directory.CreateDirectory(CacheDir);
        var version = tag.TrimStart('v');
        var xzPath = Path.Combine(CacheDir, $"frida-server-{version}-android-{arch}.xz");
        var binPath = Path.Combine(CacheDir, $"frida-server-{version}-android-{arch}");

        if (!File.Exists(binPath))
        {
            if (!File.Exists(xzPath))
            {
                progress?.Report($"Downloading frida-server {tag}...");
                await _dl.DownloadAsync(url, xzPath, downloadProgress, ct);
            }

            if (!VerifyDownloadedAsset("Frida server XZ", release.Name, xzPath, release.GitHubDigestSha256))
                return false;

            progress?.Report("Decompressing frida-server...");
            try
            {
                await using var input = File.OpenRead(xzPath);
                await using var xz = new XZStream(input);
                await using var output = File.Create(binPath);
                await xz.CopyToAsync(output, ct);
            }
            catch (Exception ex)
            {
                _log.Error($"XZ decompression failed: {ex.Message}");
                try { File.Delete(binPath); } catch { }
                return false;
            }
        }

        progress?.Report("Pushing frida-server to emulator...");
        const string remotePath = "/data/local/tmp/frida-server";
        var push = await _adb.PushAsync(serial, binPath, remotePath, ct);
        if (!push.Success)
        {
            _log.Error("adb push failed: " + push.Combined.Trim());
            return false;
        }

        progress?.Report("Setting permissions and starting frida-server...");
        var chmod = await _adb.RootShellAsync(serial, $"chmod 755 {remotePath}", ct);
        if (!chmod.Success)
        {
            _log.Error("chmod failed: " + chmod.Combined.Trim());
            return false;
        }

        await _adb.RootShellAsync(serial, "pkill -f frida-server 2>/dev/null; sleep 1", ct);
        var start = await _adb.RootShellAsync(serial,
            $"nohup {remotePath} >/data/local/tmp/frida-server.log 2>&1 &", ct);
        if (!start.Success)
        {
            _log.Warning("frida-server launch command returned non-zero: " + start.Combined.Trim());
        }

        await Task.Delay(1500, ct);
        var check = await _adb.RootShellAsync(serial, "pgrep -f frida-server", ct);
        if (check.Success && !string.IsNullOrWhiteSpace(check.StdOut))
        {
            _log.Success($"frida-server {tag} is running (PID {check.StdOut.Trim().Split('\n')[0]}).");
            return true;
        }

        _log.Warning("frida-server was pushed but may not have started. Check /data/local/tmp/frida-server.log.");
        return true;
    }

    public async Task<bool> IsRunningAsync(string serial, CancellationToken ct = default)
    {
        var r = await _adb.RootShellAsync(serial, "pgrep -f frida-server", ct);
        return r.Success && !string.IsNullOrWhiteSpace(r.StdOut);
    }

    public async Task StopAsync(string serial, CancellationToken ct = default)
    {
        await _adb.RootShellAsync(serial, "pkill -f frida-server 2>/dev/null", ct);
        _log.Info("frida-server stopped.");
    }

    private bool VerifyDownloadedAsset(string label, string key, string path, string? expectedSha256)
    {
        var check = expectedSha256 is null
            ? _hash.RecordTrustOnFirstUse(label, key, path)
            : _hash.VerifyExpectedSha256(label, key, path, expectedSha256);
        if (check.Ok) return true;

        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".download"); } catch { }
        return false;
    }
}
