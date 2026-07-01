using System.Net.Http;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace AndroidEmulatorPlus.Services;

public sealed record UpdateCheckResult(bool UpdateFound, bool Downloaded, bool Restarting, string Message);

public sealed record ReleaseFeedHealth(
    bool Ok,
    string LatestVersion,
    string AppVersion,
    bool HasFeed,
    bool HasFullPackage,
    IReadOnlyList<string> Assets,
    IReadOnlyList<string> Missing,
    string Summary);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/SysAdminDoc/AndroidEmulatorPlus";

    private readonly LogService _log;

    public UpdateService(LogService log)
    {
        _log = log;
    }

    public async Task<ReleaseFeedHealth> ValidateFeedAsync(string appVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AndroidEmulatorPlus/0.2");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var url = $"https://api.github.com/repos/SysAdminDoc/AndroidEmulatorPlus/releases/latest";
            var resp = await http.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                return new ReleaseFeedHealth(
                    Ok: false, LatestVersion: "unknown", AppVersion: appVersion,
                    HasFeed: false, HasFullPackage: false,
                    Assets: [], Missing: ["releases.win.json"],
                    Summary: $"GitHub API returned {(int)resp.StatusCode}. Release feed unreachable.");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tagName = root.TryGetProperty("tag_name", out var tn) ? tn.GetString() ?? "" : "";
            var latestVersion = tagName.TrimStart('v', 'V');

            var assets = new List<string>();
            if (root.TryGetProperty("assets", out var assetsArr))
            {
                foreach (var asset in assetsArr.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var n))
                        assets.Add(n.GetString() ?? "");
                }
            }

            var hasFeed = assets.Any(a => a.Equals("releases.win.json", StringComparison.OrdinalIgnoreCase));
            var hasFullPkg = assets.Any(a =>
                a.Contains("-full", StringComparison.OrdinalIgnoreCase)
                && a.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));

            var missing = new List<string>();
            if (!hasFeed) missing.Add("releases.win.json");
            if (!hasFullPkg) missing.Add("full .nupkg package");

            var ok = hasFeed && hasFullPkg;
            var versionMatch = latestVersion.Equals(appVersion, StringComparison.OrdinalIgnoreCase);

            var summary = ok
                ? $"Feed healthy. Latest: v{latestVersion}, App: v{appVersion}" +
                  (versionMatch ? " (current)" : " (update available)") +
                  $". {assets.Count} asset(s)."
                : $"Feed incomplete — missing: {string.Join(", ", missing)}. Latest: v{latestVersion}. {assets.Count} asset(s).";

            return new ReleaseFeedHealth(ok, latestVersion, appVersion, hasFeed, hasFullPkg, assets, missing, summary);
        }
        catch (Exception ex)
        {
            return new ReleaseFeedHealth(
                Ok: false, LatestVersion: "unknown", AppVersion: appVersion,
                HasFeed: false, HasFullPackage: false,
                Assets: [], Missing: [],
                Summary: $"Feed check failed: {ex.Message}");
        }
    }

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(bool restart)
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            _log.Info("Checking GitHub Releases for application updates...");

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                const string message = "AndroidEmulatorPlus is up to date.";
                _log.Success(message);
                return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
            }

            if (restart)
            {
                var health = await ValidateFeedAsync(update.TargetFullRelease.Version.ToString());
                if (!health.Ok)
                {
                    var message = $"Update blocked: {health.Summary}";
                    _log.Warning(message);
                    return new UpdateCheckResult(UpdateFound: true, Downloaded: false, Restarting: false, message);
                }
            }

            _log.Info("Update found. Downloading Velopack package...");
            await manager.DownloadUpdatesAsync(update);

            if (restart)
            {
                _log.Info("Applying update and restarting...");
                manager.ApplyUpdatesAndRestart(update);
                return new UpdateCheckResult(UpdateFound: true, Downloaded: true, Restarting: true,
                    "Update downloaded. Restarting to apply it...");
            }

            const string pendingMessage = "Update downloaded. It will be applied on the next app launch.";
            _log.Success(pendingMessage);
            return new UpdateCheckResult(UpdateFound: true, Downloaded: true, Restarting: false, pendingMessage);
        }
        catch (Exception ex) when (ex.GetType().Name == "NotInstalledException")
        {
            const string message = "Update checks require a Velopack-installed copy.";
            _log.Detail(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
        catch (Exception ex) when (ex.GetType().Name == "AcquireLockFailedException")
        {
            const string message = "Another update operation is already running.";
            _log.Warning(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
        catch (Exception ex)
        {
            var message = "Update check failed: " + ex.Message;
            _log.Warning(message);
            return new UpdateCheckResult(UpdateFound: false, Downloaded: false, Restarting: false, message);
        }
    }
}
