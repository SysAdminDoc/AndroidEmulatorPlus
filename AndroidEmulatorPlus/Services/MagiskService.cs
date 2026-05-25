using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed class MagiskModuleCatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = "";
}

internal sealed class MagiskCatalogFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("modules")] public List<MagiskModuleCatalogEntry> Modules { get; set; } = new();
}

public sealed record InstalledMagiskModule(string Id, string Name, string Version, bool Enabled);

/// <summary>
/// C-07 / R-03: drives `magisk --install-module` on a rooted emulator. The
/// curated module catalog lives at <c>Resources/magisk-modules.json</c>
/// (embedded). User overrides at
/// <c>%LOCALAPPDATA%\AndroidEmulatorPlus\presets\magisk-modules.json</c> merge
/// by id.
/// </summary>
public sealed class MagiskService
{
    private readonly AdbService _adb;
    private readonly DownloadService _dl;
    private readonly LogService _log;

    public IReadOnlyList<MagiskModuleCatalogEntry> Catalog { get; }

    public static string UserOverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "presets", "magisk-modules.json");

    public MagiskService(AdbService adb, DownloadService dl, LogService log)
    {
        _adb = adb;
        _dl = dl;
        _log = log;
        Catalog = LoadCatalog();
    }

    private static List<MagiskModuleCatalogEntry> LoadCatalog()
    {
        var byId = new Dictionary<string, MagiskModuleCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("magisk-modules.json", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                using var s = asm.GetManifestResourceStream(resName)!;
                var f = JsonSerializer.Deserialize<MagiskCatalogFile>(s);
                if (f is not null)
                    foreach (var m in f.Modules.Where(IsSafeCatalogEntry))
                        byId[m.Id] = m;
            }
        }
        catch { /* fall through to user overrides */ }

        try
        {
            if (File.Exists(UserOverridePath))
            {
                var u = JsonSerializer.Deserialize<MagiskCatalogFile>(File.ReadAllText(UserOverridePath));
                if (u is not null)
                    foreach (var m in u.Modules.Where(IsSafeCatalogEntry))
                        byId[m.Id] = m;
            }
        }
        catch { }

        return byId.Values.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsSafeCatalogEntry(MagiskModuleCatalogEntry entry)
        => AdbService.IsSafeMagiskModuleId(entry.Id)
           && !string.IsNullOrWhiteSpace(entry.Name)
           && Uri.TryCreate(entry.DownloadUrl, UriKind.Absolute, out var uri)
           && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lists modules installed on the emulator via `magisk module list`.
    /// Output looks like:
    ///   id=shamiko name=Shamiko version=v0.7.7 versionCode=216 enabled=true
    /// One per line. Available since Magisk 27.x; older builds may return
    /// nothing and this method falls back to walking /data/adb/modules/.
    /// </summary>
    public async Task<List<InstalledMagiskModule>> ListInstalledAsync(string serial, CancellationToken ct = default)
    {
        var list = new List<InstalledMagiskModule>();
        // Primary: magisk module list
        var r = await _adb.RootShellAsync(serial, "magisk module list 2>/dev/null", ct);
        if (r.Success && !string.IsNullOrWhiteSpace(r.StdOut))
        {
            foreach (var raw in r.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                string id = "", name = "", ver = "";
                bool enabled = true;
                foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = tok.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = tok[..eq]; var v = tok[(eq + 1)..];
                    switch (k)
                    {
                        case "id": id = v; break;
                        case "name": name = v; break;
                        case "version": ver = v; break;
                        case "enabled": enabled = v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    }
                }
                if (!string.IsNullOrEmpty(id)) list.Add(new InstalledMagiskModule(id, name, ver, enabled));
            }
            if (list.Count > 0) return list;
        }

        // Fallback: walk /data/adb/modules/
        var walk = await _adb.RootShellAsync(serial,
            "for d in /data/adb/modules/*/; do " +
            "n=\"$(basename \"$d\")\"; " +
            "e=$([ -f \"$d/disable\" ] && echo false || echo true); " +
            "v=$(awk -F= '$1==\"version\"{print $2}' \"$d/module.prop\" 2>/dev/null); " +
            "nm=$(awk -F= '$1==\"name\"{print $2}' \"$d/module.prop\" 2>/dev/null); " +
            "echo \"id=$n name=$nm version=$v enabled=$e\"; done", ct);
        if (walk.Success)
        {
            foreach (var raw in walk.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("id=", StringComparison.Ordinal)) continue;
                string id = "", name = "", ver = ""; bool enabled = true;
                foreach (var tok in line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = tok.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = tok[..eq]; var v = tok[(eq + 1)..];
                    switch (k)
                    {
                        case "id": id = v; break;
                        case "name": name = v; break;
                        case "version": ver = v; break;
                        case "enabled": enabled = v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    }
                }
                if (!string.IsNullOrEmpty(id)) list.Add(new InstalledMagiskModule(id, name, ver, enabled));
            }
        }
        return list;
    }

    /// <summary>
    /// Downloads a module zip locally, pushes it to /sdcard/, runs
    /// <c>magisk --install-module &lt;path&gt;</c>, then removes the staged zip.
    /// </summary>
    public async Task<bool> InstallFromUrlAsync(string serial, string url, string suggestedFileName, CancellationToken ct = default)
    {
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus", "cache", "magisk-modules");
        Directory.CreateDirectory(cacheRoot);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            _log.Error("Module download URL must be an absolute HTTPS URL.");
            return false;
        }

        var resolved = await ResolveModuleDownloadAsync(uri, suggestedFileName, ct);
        if (resolved is null) return false;

        var safeName = Path.GetFileName(resolved.Value.FileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = Path.GetFileName(suggestedFileName);
        if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            safeName += ".zip";

        var localZip = Path.Combine(cacheRoot, safeName);
        _log.Info($"Downloading module {safeName}…");
        try { await _dl.DownloadAsync(resolved.Value.Url, localZip, ct: ct); }
        catch (Exception ex) { _log.Error("Module download failed: " + ex.Message); return false; }
        return await InstallFromZipAsync(serial, localZip, ct);
    }

    public Task<bool> InstallCatalogEntryAsync(string serial, MagiskModuleCatalogEntry entry, CancellationToken ct = default)
    {
        if (!IsSafeCatalogEntry(entry))
        {
            _log.Error($"Module catalog entry '{entry.Name}' is invalid or uses an unsafe URL.");
            return Task.FromResult(false);
        }
        return InstallFromUrlAsync(serial, entry.DownloadUrl, $"{entry.Id}.zip", ct);
    }

    private async Task<(string Url, string FileName)?> ResolveModuleDownloadAsync(Uri uri, string suggestedFileName, CancellationToken ct)
    {
        if (IsGitHubLatestReleaseUrl(uri, out var owner, out var repo))
        {
            var api = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            try
            {
                using var doc = JsonDocument.Parse(await _dl.FetchTextAsync(api, ct));
                var assets = doc.RootElement.GetProperty("assets")
                    .EnumerateArray()
                    .Select(asset => new
                    {
                        Name = asset.GetProperty("name").GetString() ?? "",
                        Url = asset.GetProperty("browser_download_url").GetString() ?? "",
                        Size = asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var size) ? size : 0L,
                    })
                    .Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                    && Uri.TryCreate(asset.Url, UriKind.Absolute, out var assetUri)
                                    && assetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(asset => ScoreAssetName(asset.Name, suggestedFileName))
                    .ThenByDescending(asset => asset.Size)
                    .ToList();

                var best = assets.FirstOrDefault();
                if (best is not null) return (best.Url, best.Name);
                _log.Error($"No .zip asset found on latest GitHub release for {owner}/{repo}.");
                return null;
            }
            catch (Exception ex)
            {
                _log.Error($"GitHub latest-release lookup failed for {owner}/{repo}: {ex.Message}");
                return null;
            }
        }

        return (uri.ToString(), Path.GetFileName(uri.LocalPath));
    }

    private static int ScoreAssetName(string assetName, string suggestedFileName)
    {
        var asset = Normalize(assetName);
        var suggested = Normalize(Path.GetFileNameWithoutExtension(suggestedFileName));
        return asset.Contains(suggested, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsGitHubLatestReleaseUrl(Uri uri, out string owner, out string repo)
    {
        owner = "";
        repo = "";
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("latest", StringComparison.OrdinalIgnoreCase))
            return false;
        owner = parts[0];
        repo = parts[1];
        return IsGitHubSlug(owner) && IsGitHubSlug(repo);
    }

    private static bool IsGitHubSlug(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 100
           && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    public async Task<bool> InstallFromZipAsync(string serial, string localZip, CancellationToken ct = default)
    {
        if (!File.Exists(localZip)) { _log.Error("Module zip not found: " + localZip); return false; }
        if (!TryValidateModuleZip(localZip, out var zipDetail))
        {
            _log.Error("Module zip rejected: " + zipDetail);
            return false;
        }
        var remote = "/sdcard/aep-module.zip";
        var push = await _adb.PushAsync(serial, localZip, remote, ct);
        if (!push.Success) { _log.Error("module push failed: " + push.Combined.Trim()); return false; }
        try
        {
            var inst = await _adb.RootShellAsync(serial, $"magisk --install-module {AdbService.ShellQuote(remote)}", ct);
            // magisk prints '- Done' on success and exits 0.
            if (inst.Success && (inst.Combined.Contains("Done", StringComparison.OrdinalIgnoreCase)
                              || inst.Combined.Contains("installed", StringComparison.OrdinalIgnoreCase)))
            {
                _log.Success($"Module installed. Reboot the emulator (Cold Boot) to activate.");
                return true;
            }
            _log.Error("magisk --install-module: " + inst.Combined.Trim());
            return false;
        }
        finally
        {
            try { await _adb.RootShellAsync(serial, $"rm -f {AdbService.ShellQuote(remote)}", ct); } catch { }
        }
    }

    /// <summary>Toggles a module by writing/removing /data/adb/modules/&lt;id&gt;/disable.</summary>
    public async Task<bool> SetEnabledAsync(string serial, string moduleId, bool enabled, CancellationToken ct = default)
    {
        if (!AdbService.IsSafeMagiskModuleId(moduleId))
        {
            _log.Error("Module toggle skipped: invalid module id.");
            return false;
        }
        var cmd = enabled
            ? $"rm -f {AdbService.ShellQuote($"/data/adb/modules/{moduleId}/disable")} && echo OK"
            : $"touch {AdbService.ShellQuote($"/data/adb/modules/{moduleId}/disable")} && echo OK";
        var r = await _adb.RootShellAsync(serial, cmd, ct);
        if (r.Combined.Contains("OK")) { _log.Info($"Module {moduleId} → {(enabled ? "enabled" : "disabled")}. Reboot required."); return true; }
        _log.Warning(r.Combined.Trim()); return false;
    }

    /// <summary>Removes a module by writing /data/adb/modules/&lt;id&gt;/remove.</summary>
    public async Task<bool> RemoveAsync(string serial, string moduleId, CancellationToken ct = default)
    {
        if (!AdbService.IsSafeMagiskModuleId(moduleId))
        {
            _log.Error("Module removal skipped: invalid module id.");
            return false;
        }
        var r = await _adb.RootShellAsync(serial, $"touch {AdbService.ShellQuote($"/data/adb/modules/{moduleId}/remove")} && echo OK", ct);
        if (r.Combined.Contains("OK")) { _log.Info($"Module {moduleId} marked for removal. Reboot the emulator to apply."); return true; }
        _log.Warning(r.Combined.Trim()); return false;
    }

    public static bool TryValidateModuleZip(string localZip, out string detail)
    {
        try
        {
            using var zip = ZipFile.OpenRead(localZip);
            if (zip.Entries.Count == 0)
            {
                detail = "archive is empty";
                return false;
            }

            var hasModuleProp = false;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/').Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var isDirectory = name.EndsWith("/", StringComparison.Ordinal);
                if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\0'))
                {
                    detail = $"absolute or invalid path '{entry.FullName}'";
                    return false;
                }

                var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Any(static part => part == ".."))
                {
                    detail = $"path traversal entry '{entry.FullName}'";
                    return false;
                }

                if (!isDirectory
                    && parts.Length == 1
                    && parts[0].Equals("module.prop", StringComparison.OrdinalIgnoreCase))
                    hasModuleProp = true;
            }

            if (!hasModuleProp)
            {
                detail = "module.prop missing at archive root";
                return false;
            }

            detail = "ok";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
