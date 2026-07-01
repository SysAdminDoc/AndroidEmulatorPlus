using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidEmulatorPlus.Services;

public sealed class ReleaseArtifactCheck
{
    [JsonPropertyName("file")] public string File { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; init; }
    [JsonPropertyName("authenticode")] public string? Authenticode { get; init; }
}

public sealed class ReleasePreflightResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("appVersion")] public string AppVersion { get; init; } = "";
    [JsonPropertyName("csprojVersion")] public string? CsprojVersion { get; init; }
    [JsonPropertyName("versionMatch")] public bool VersionMatch { get; init; }
    [JsonPropertyName("feedHealth")] public ReleaseFeedHealth? FeedHealth { get; init; }
    [JsonPropertyName("artifacts")] public List<ReleaseArtifactCheck> Artifacts { get; init; } = [];
    [JsonPropertyName("issues")] public List<string> Issues { get; init; } = [];
}

public sealed class ReleasePreflightService
{
    private readonly UpdateService _update;
    private readonly LogService _log;

    public ReleasePreflightService(UpdateService update, LogService log)
    {
        _update = update;
        _log = log;
    }

    public async Task<ReleasePreflightResult> RunAsync(string? artifactDir = null, CancellationToken ct = default)
    {
        var issues = new List<string>();
        var artifacts = new List<ReleaseArtifactCheck>();

        var appVersion = typeof(ReleasePreflightService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var csprojVersion = ReadCsprojVersion();
        var versionMatch = csprojVersion is not null
            && csprojVersion.Equals(appVersion, StringComparison.OrdinalIgnoreCase);
        if (csprojVersion is not null && !versionMatch)
            issues.Add($"Version mismatch: assembly={appVersion}, csproj={csprojVersion}");

        ReleaseFeedHealth? feedHealth = null;
        try
        {
            feedHealth = await _update.ValidateFeedAsync(appVersion, ct);
            if (!feedHealth.Ok)
                issues.Add($"Feed: {feedHealth.Summary}");
        }
        catch (Exception ex)
        {
            issues.Add($"Feed check failed: {ex.Message}");
        }

        if (artifactDir is not null && Directory.Exists(artifactDir))
        {
            var files = Directory.EnumerateFiles(artifactDir)
                .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var hash = HashVerificationService.ComputeSha256(file);
                string? authStatus = null;
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    authStatus = CheckAuthenticode(file);
                    if (authStatus != "Valid")
                        issues.Add($"{info.Name}: Authenticode={authStatus}");
                }

                artifacts.Add(new ReleaseArtifactCheck
                {
                    File = info.Name,
                    Sha256 = hash,
                    SizeBytes = info.Length,
                    Authenticode = authStatus,
                });
            }
        }

        var ok = issues.Count == 0;
        var result = new ReleasePreflightResult
        {
            Ok = ok,
            AppVersion = appVersion,
            CsprojVersion = csprojVersion,
            VersionMatch = versionMatch,
            FeedHealth = feedHealth,
            Artifacts = artifacts,
            Issues = issues,
        };

        if (ok)
            _log.Success($"Release preflight passed. v{appVersion}, {artifacts.Count} artifact(s).");
        else
        {
            foreach (var issue in issues)
                _log.Warning($"Release preflight: {issue}");
        }

        return result;
    }

    public string WriteReport(ReleasePreflightResult result)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus", "logs");
        Directory.CreateDirectory(dir);
        var name = $"release-preflight-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(dir, name);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        _log.Info($"Release preflight report: {path}");
        return path;
    }

    private static string? ReadCsprojVersion()
    {
        var csproj = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "AndroidEmulatorPlus.csproj");
        if (!File.Exists(csproj))
        {
            csproj = Path.Combine(AppContext.BaseDirectory, "AndroidEmulatorPlus.csproj");
            if (!File.Exists(csproj)) return null;
        }
        try
        {
            var xml = File.ReadAllText(csproj);
            var match = System.Text.RegularExpressions.Regex.Match(xml, @"<Version>([^<]+)</Version>");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    private static string CheckAuthenticode(string filePath)
    {
        try
        {
            var script = $"(Get-AuthenticodeSignature -LiteralPath $args[0]).Status";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
            psi.ArgumentList.Add(filePath);
            using var proc = Process.Start(psi);
            if (proc is null) return "Unknown";
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10_000);
            return string.IsNullOrWhiteSpace(output) ? "Unknown" : output;
        }
        catch
        {
            return "Unknown";
        }
    }
}
