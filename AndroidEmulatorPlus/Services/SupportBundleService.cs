using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace AndroidEmulatorPlus.Services;

public sealed class SupportBundleService
{
    private readonly LogService _log;
    private readonly SdkLocator _sdk;
    private readonly SdkmanagerService _sdkman;
    private readonly SettingsService _settings;
    private readonly CacheDiagnosticsService _cache;

    public SupportBundleService(LogService log, SdkLocator sdk, SdkmanagerService sdkman,
        SettingsService settings, CacheDiagnosticsService cache)
    {
        _log = log;
        _sdk = sdk;
        _sdkman = sdkman;
        _settings = settings;
        _cache = cache;
    }

    public async Task<string> ExportAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidEmulatorPlus");
        var zipName = $"support-bundle-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var zipPath = Path.Combine(dir, zipName);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddText(zip, "version.txt", BuildVersionInfo());
        AddText(zip, "settings-redacted.json", RedactSettings());
        AddText(zip, "cache-usage.txt", BuildCacheInfo());

        try
        {
            var inventory = await _sdkman.ListPackageInventoryAsync(ct);
            var sb = new StringBuilder();
            sb.AppendLine("Installed SDK packages:");
            foreach (var p in inventory.Installed)
                sb.AppendLine($"  {p.Path}  {p.Version}  {p.Description}");
            if (inventory.Updates.Count > 0)
            {
                sb.AppendLine("\nAvailable updates:");
                foreach (var u in inventory.Updates)
                    sb.AppendLine($"  {u.Path}  {u.InstalledVersion} → {u.AvailableVersion}");
            }
            AddText(zip, "sdk-inventory.txt", sb.ToString());
        }
        catch
        {
            AddText(zip, "sdk-inventory.txt", "sdkmanager not available");
        }

        var crashLog = Path.Combine(dir, "crash.log");
        if (File.Exists(crashLog))
        {
            try
            {
                var content = Redact(File.ReadAllText(crashLog));
                AddText(zip, "crash.log", content);
            }
            catch { }
        }

        var logsDir = Path.Combine(dir, "logs");
        if (Directory.Exists(logsDir))
        {
            var logFiles = Directory.EnumerateFiles(logsDir, "app-*.log")
                .OrderByDescending(f => f)
                .Take(3);
            foreach (var f in logFiles)
            {
                try
                {
                    var content = Redact(File.ReadAllText(f));
                    AddText(zip, "logs/" + Path.GetFileName(f), content);
                }
                catch { }
            }
        }

        _log.Success($"Support bundle exported: {zipPath}");
        return zipPath;
    }

    private string BuildVersionInfo()
    {
        var asm = typeof(SupportBundleService).Assembly;
        var sb = new StringBuilder();
        sb.AppendLine($"App version: {asm.GetName().Version}");
        sb.AppendLine($"Informational: {asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"SDK root: {(_sdk.SdkRoot is not null ? "present" : "not found")}");
        sb.AppendLine($"adb: {(_sdk.AdbExe is not null ? "present" : "not found")}");
        sb.AppendLine($"emulator: {(_sdk.EmulatorExe is not null ? "present" : "not found")}");
        sb.AppendLine($"sdkmanager: {(_sdk.SdkManagerBat is not null ? "present" : "not found")}");
        return sb.ToString();
    }

    private string RedactSettings()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"theme: {_settings.Current.Theme}");
        sb.AppendLine($"sdkRootOverride: {(string.IsNullOrEmpty(_settings.Current.SdkRootOverride) ? "(none)" : "[redacted]")}");
        sb.AppendLine($"mediaDir: {(string.IsNullOrEmpty(_settings.Current.MediaDir) ? "(none)" : "[redacted]")}");
        sb.AppendLine($"httpProxy: {(string.IsNullOrEmpty(_settings.Current.HttpProxy) ? "(none)" : "[redacted]")}");
        sb.AppendLine($"autoScrcpy: {_settings.Current.AutoScrcpy}");
        sb.AppendLine($"autoUpdateChecks: {_settings.Current.AutoUpdateChecks}");
        sb.AppendLine($"hasSeenWizard: {_settings.Current.HasSeenWizard}");
        return sb.ToString();
    }

    private string BuildCacheInfo()
    {
        var usage = _cache.Measure();
        return $"Transfer: {usage.Human(usage.TransferBytes)}\n" +
               $"Bundles: {usage.Human(usage.BundleBytes)}\n" +
               $"Root cache: {usage.Human(usage.RootBytes)}\n" +
               $"Total: {usage.Human(usage.Total)}";
    }

    private static void AddText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    public static string Redact(string input)
    {
        var result = input;
        result = Regex.Replace(result, @"C:\\Users\\[^\\]+", @"C:\Users\[REDACTED]", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"/home/[^/\s]+", "/home/[REDACTED]");
        result = Regex.Replace(result, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?\b", "[REDACTED_IP]");
        result = Regex.Replace(result, @"\b\d{6}\b(?=.*pair)", "[REDACTED_CODE]");
        return result;
    }
}
