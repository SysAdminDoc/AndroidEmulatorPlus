using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace AndroidEmulatorPlus.Services;

public sealed class DownloadService : IDisposable
{
    private readonly LogService _log;
    private readonly HttpClient _http;

    public DownloadService(LogService log)
    {
        _log = log;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidEmulatorPlus/0.1");
    }

    public async Task DownloadAsync(string url, string dest,
        IProgress<(long received, long? total)>? progress = null,
        CancellationToken ct = default)
    {
        _log.Info($"Downloading {Path.GetFileName(dest)}…");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(dest);
        var buf = new byte[81920];
        long received = 0;
        int n;
        while ((n = await input.ReadAsync(buf, ct)) > 0)
        {
            await output.WriteAsync(buf.AsMemory(0, n), ct);
            received += n;
            progress?.Report((received, total));
        }
    }

    public async Task<string> FetchTextAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Returns (tagName, browserDownloadUrl) for the first .apk asset in the latest GitHub release.</summary>
    public async Task<(string tag, string apkUrl)?> LatestMagiskAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await FetchTextAsync("https://api.github.com/repos/topjohnwu/Magisk/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("Magisk-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".apk"))
                    return (tag, asset.GetProperty("browser_download_url").GetString() ?? "");
            }
        }
        catch (Exception ex) { _log.Warning($"Magisk lookup failed: {ex.Message}"); }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
