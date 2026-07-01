using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndroidEmulatorPlus.Services;

public sealed class CatalogManifest
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("updatedUtc")] public string UpdatedUtc { get; init; } = "";
    [JsonPropertyName("files")] public List<CatalogFileEntry> Files { get; init; } = [];
}

public sealed class CatalogFileEntry
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
}

public sealed record CatalogUpdateResult(bool Updated, int FilesUpdated, List<string> Log);

public sealed class CatalogUpdateService
{
    private readonly LogService _log;
    private readonly DownloadService _dl;

    public CatalogUpdateService(LogService log, DownloadService dl)
    {
        _log = log;
        _dl = dl;
    }

    public static string CatalogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "catalogs");

    public static string BackupDir => Path.Combine(CatalogDir, "backup");

    public async Task<CatalogManifest?> FetchManifestAsync(string manifestUrl, CancellationToken ct = default)
    {
        try
        {
            var json = await _dl.FetchTextAsync(manifestUrl, ct);
            return JsonSerializer.Deserialize<CatalogManifest>(json);
        }
        catch (Exception ex)
        {
            _log.Warning($"Catalog manifest fetch failed: {ex.Message}");
            return null;
        }
    }

    public async Task<CatalogUpdateResult> ApplyUpdatesAsync(
        CatalogManifest manifest,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(CatalogDir);
        Directory.CreateDirectory(BackupDir);
        var log = new List<string>();
        int updated = 0;

        foreach (var entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Url))
            {
                log.Add($"SKIP: {entry.Name} — missing name or URL");
                continue;
            }

            if (entry.Name.Contains("..") || entry.Name.Contains('/') || entry.Name.Contains('\\'))
            {
                log.Add($"SKIP: {entry.Name} — path traversal rejected");
                continue;
            }

            var destPath = Path.Combine(CatalogDir, entry.Name);

            if (File.Exists(destPath))
            {
                var existingHash = HashVerificationService.ComputeSha256(destPath);
                if (existingHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    log.Add($"CURRENT: {entry.Name}");
                    continue;
                }
                var backupPath = Path.Combine(BackupDir, $"{entry.Name}.{DateTime.UtcNow:yyyyMMddHHmmss}.bak");
                File.Copy(destPath, backupPath, overwrite: true);
                log.Add($"BACKUP: {entry.Name} → {Path.GetFileName(backupPath)}");
            }

            var tmpPath = destPath + ".tmp";
            try
            {
                await _dl.DownloadAsync(entry.Url, tmpPath, ct: ct);

                var downloadHash = HashVerificationService.ComputeSha256(tmpPath);
                if (!downloadHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    log.Add($"REJECT: {entry.Name} — SHA-256 mismatch (expected {entry.Sha256[..12]}, got {downloadHash[..12]})");
                    try { File.Delete(tmpPath); } catch { }
                    continue;
                }

                File.Move(tmpPath, destPath, overwrite: true);
                updated++;
                log.Add($"UPDATED: {entry.Name} ({entry.Size / 1024} KB)");
                _log.Info($"Catalog updated: {entry.Name}");
            }
            catch (Exception ex)
            {
                log.Add($"ERROR: {entry.Name} — {ex.Message}");
                try { File.Delete(tmpPath); } catch { }
            }
        }

        return new CatalogUpdateResult(updated > 0, updated, log);
    }

    public void Rollback(string fileName)
    {
        var destPath = Path.Combine(CatalogDir, fileName);
        var backups = Directory.Exists(BackupDir)
            ? Directory.EnumerateFiles(BackupDir, $"{fileName}.*.bak")
                .OrderByDescending(f => f)
                .ToList()
            : new List<string>();

        if (backups.Count == 0)
        {
            _log.Warning($"No backup found for {fileName}");
            return;
        }

        File.Copy(backups[0], destPath, overwrite: true);
        _log.Success($"Rolled back {fileName} from {Path.GetFileName(backups[0])}");
    }

    public IReadOnlyList<string> ListAvailableOverrides()
    {
        if (!Directory.Exists(CatalogDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(CatalogDir, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .ToList()!;
    }
}
