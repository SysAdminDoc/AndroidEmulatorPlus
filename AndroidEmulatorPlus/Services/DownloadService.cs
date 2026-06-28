using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AndroidEmulatorPlus.Services;

public sealed class DownloadService : IDisposable
{
    private readonly LogService _log;
    private readonly HttpClient _http;

    public DownloadService(LogService log, SettingsService settings)
    {
        _log = log;
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        // C-03: honor SettingsService.HttpProxy. The value is plumbed at ctor time
        // (singleton lifetime in DI), so changing the proxy in the Settings dialog
        // takes effect on next launch — the dialog already warns about that.
        var proxyUrl = settings.Current.HttpProxy;
        if (!string.IsNullOrWhiteSpace(proxyUrl)
            && Uri.TryCreate(proxyUrl, UriKind.Absolute, out var proxyUri))
        {
            handler.Proxy = new WebProxy(proxyUri) { UseDefaultCredentials = true };
            handler.UseProxy = true;
            _log.Info($"HTTP proxy configured: {proxyUri}");
        }
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidEmulatorPlus/0.2");
    }

    public async Task DownloadAsync(string url, string dest,
        IProgress<(long received, long? total)>? progress = null,
        CancellationToken ct = default)
    {
        var destPath = Path.GetFullPath(dest);
        _log.Info($"Downloading {Path.GetFileName(destPath)}…");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var tmp = destPath + ".download";
        var existingBytes = File.Exists(tmp) ? new FileInfo(tmp).Length : 0;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
        {
            req.Headers.Range = new RangeHeaderValue(existingBytes, null);
            _log.Info($"Resuming {Path.GetFileName(destPath)} from {existingBytes:N0} bytes.");
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existingBytes > 0 && resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            try { File.Delete(tmp); } catch { }
            await DownloadAsync(url, dest, progress, ct);
            return;
        }
        resp.EnsureSuccessStatusCode();

        var append = existingBytes > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
        if (existingBytes > 0 && !append)
        {
            _log.Warning($"{Path.GetFileName(destPath)} server ignored resume request; restarting download.");
            try { File.Delete(tmp); } catch { }
            existingBytes = 0;
        }

        var total = resp.Content.Headers.ContentRange?.Length
            ?? (resp.Content.Headers.ContentLength.HasValue
                ? existingBytes + resp.Content.Headers.ContentLength.Value
                : null);
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        try
        {
            await using (var output = new FileStream(tmp, append ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buf = new byte[81920];
                long received = existingBytes;
                progress?.Report((received, total));
                int n;
                while ((n = await input.ReadAsync(buf, ct)) > 0)
                {
                    await output.WriteAsync(buf.AsMemory(0, n), ct);
                    received += n;
                    progress?.Report((received, total));
                }
            }
            File.Move(tmp, destPath, overwrite: true);
        }
        catch
        {
            _log.Warning($"Download interrupted; kept partial file for resume: {tmp}");
            throw;
        }
    }

    public async Task<string> FetchTextAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public sealed record MagiskAsset(string Tag, string Url, string? GitHubDigestSha256);

    /// <summary>
    /// Returns metadata for the canonical Magisk APK asset in the latest GitHub release.
    /// GitHub publishes a per-asset <c>digest</c> field ("sha256:abc…") since 2024 — when
    /// present, callers should cross-check it against the downloaded file's hash for
    /// defense-in-depth on top of <see cref="HashVerificationService"/>'s in-tree manifest.
    /// </summary>
    public async Task<MagiskAsset?> LatestMagiskAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await FetchTextAsync("https://api.github.com/repos/topjohnwu/Magisk/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                // Prefer the canonical "Magisk-vNN.N.apk"; skip debug / stub / canary builds.
                if (name.StartsWith("Magisk-", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("debug", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("stub", StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    string? digest = null;
                    if (asset.TryGetProperty("digest", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                        digest = HashVerificationService.NormalizeSha256Digest(dProp.GetString());
                    return new MagiskAsset(tag, url, digest);
                }
            }
        }
        catch (Exception ex) { _log.Warning($"Magisk lookup failed: {ex.Message}"); }
        return null;
    }

    public const string CmdlineToolsFallbackUrl =
        "https://dl.google.com/android/repository/commandlinetools-win-14742923_latest.zip";

    public sealed record CmdlineToolsResolution(string Url, bool IsFallback, string? Reason);

    /// <summary>
    /// Scrapes developer.android.com/studio for the current Windows command-line-tools
    /// download URL. Returns a stable fallback URL if the scrape fails so first-launch
    /// installation can still proceed (at the cost of a slightly older build number).
    /// The returned record's <c>IsFallback</c> flag lets the UI surface this state.
    /// </summary>
    public async Task<CmdlineToolsResolution> LatestCmdlineToolsWindowsUrlAsync(CancellationToken ct = default)
    {
        try
        {
            var html = await FetchTextAsync("https://developer.android.com/studio", ct);
            var rx = new System.Text.RegularExpressions.Regex(
                @"https://dl\.google\.com/android/repository/commandlinetools-win-\d+_latest\.zip",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = rx.Match(html);
            if (match.Success) return new CmdlineToolsResolution(match.Value, IsFallback: false, Reason: null);
            _log.Warning("Could not parse cmdline-tools URL from developer.android.com — using fallback.");
            return new CmdlineToolsResolution(CmdlineToolsFallbackUrl, IsFallback: true, Reason: "URL not found in studio page");
        }
        catch (Exception ex)
        {
            _log.Warning($"cmdline-tools URL lookup failed: {ex.Message} — using fallback.");
            return new CmdlineToolsResolution(CmdlineToolsFallbackUrl, IsFallback: true, Reason: ex.Message);
        }
    }

    public void Dispose() => _http.Dispose();
}
