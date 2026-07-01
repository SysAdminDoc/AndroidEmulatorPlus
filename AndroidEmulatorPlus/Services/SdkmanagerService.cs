using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed record SdkInstalledPackage(string Path, string Version, string Description, string Location);
public sealed record SdkPackageUpdate(string Path, string InstalledVersion, string AvailableVersion, string Description);
public sealed record SdkPackageInventory(
    IReadOnlyList<SdkInstalledPackage> Installed,
    IReadOnlyList<SdkPackageUpdate> Updates);

public sealed class SdkUpdateReceipt
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; init; } = "";
    [JsonPropertyName("requestedPackages")] public List<string> RequestedPackages { get; init; } = [];
    [JsonPropertyName("before")] public List<SdkUpdateReceiptEntry> Before { get; init; } = [];
    [JsonPropertyName("after")] public List<SdkUpdateReceiptEntry> After { get; init; } = [];
    [JsonPropertyName("changed")] public List<SdkUpdateReceiptChange> Changed { get; init; } = [];
    [JsonPropertyName("rollbackCommands")] public List<string> RollbackCommands { get; init; } = [];
}

public sealed class SdkUpdateReceiptEntry
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("version")] public string Version { get; init; } = "";
}

public sealed class SdkUpdateReceiptChange
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("from")] public string From { get; init; } = "";
    [JsonPropertyName("to")] public string To { get; init; } = "";
}

/// <summary>
/// Wraps <c>sdkmanager.bat</c>. License acceptance is automated by piping a stream
/// of 'y' lines into stdin (Google's official one-liner for unattended installs).
/// Install / list / update are also exposed here so other view-models can call them
/// without re-implementing the cmd.exe /c plumbing.
/// </summary>
public sealed class SdkmanagerService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public SdkmanagerService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    private static readonly Dictionary<string, string?> NoPathConv = new()
    {
        ["MSYS_NO_PATHCONV"] = "1",
        ["MSYS2_ARG_CONV_EXCL"] = "*",
    };

    /// <summary>
    /// Runs <c>sdkmanager --licenses</c> and answers 'y' to each prompt up to a generous
    /// cap. Returns true if the process exits 0.
    /// </summary>
    public async Task<bool> AcceptLicensesAsync(IProgress<string>? status, CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null)
        {
            _log.Error("sdkmanager.bat not found. Install cmdline-tools first.");
            return false;
        }
        status?.Report("Running sdkmanager --licenses…");

        try
        {
            var args = new[] { "/c", _sdk.SdkManagerBat, "--licenses" };
            var ys = Enumerable.Repeat("y", 60); // covers all current Google + Android licenses with headroom
            var r = await ProcessRunner.RunWithStdinAsync("cmd.exe", args, ys,
                extraEnv: NoPathConv,
                timeout: TimeSpan.FromMinutes(3),
                ct: ct);
            if (r.Success) { _log.Success("All SDK licenses accepted."); return true; }
            _log.Warning($"sdkmanager --licenses exited {r.ExitCode}: {r.Combined.Trim()}");
            return false;
        }
        catch (TimeoutException)
        {
            _log.Error("sdkmanager --licenses timed out.");
            return false;
        }
        catch (OperationCanceledException)
        {
            _log.Error("sdkmanager --licenses cancelled.");
            return false;
        }
    }

    /// <summary>Installs one or more SDK packages with `y` piped in for any prompts.</summary>
    public async Task<bool> InstallAsync(IEnumerable<string> packages, IProgress<string>? status, CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null)
        {
            _log.Error("sdkmanager.bat not found. Install cmdline-tools first.");
            return false;
        }
        var pkgs = packages.ToList();
        if (pkgs.Count == 0) return true;

        status?.Report($"sdkmanager install: {string.Join(", ", pkgs)}");

        try
        {
            var args = new List<string> { "/c", _sdk.SdkManagerBat };
            args.AddRange(pkgs);
            var ys = Enumerable.Repeat("y", 30);
            var r = await ProcessRunner.RunWithStdinAsync("cmd.exe", args, ys,
                extraEnv: NoPathConv,
                timeout: TimeSpan.FromMinutes(15),
                ct: ct);
            return r.Success;
        }
        catch (TimeoutException)
        {
            _log.Error($"sdkmanager install timed out ({string.Join(", ", pkgs)}).");
            return false;
        }
        catch (OperationCanceledException)
        {
            _log.Error($"sdkmanager install cancelled ({string.Join(", ", pkgs)}).");
            return false;
        }
    }

    /// <summary>
    /// Returns the list of "Available Packages" reported by <c>sdkmanager --list --no_https</c>
    /// (system-images and emulator targets). Items are full package paths like
    /// <c>system-images;android-36;google_apis_playstore;x86_64</c>.
    /// </summary>
    public async Task<List<string>> ListAvailableSystemImagesAsync(CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null) return new();
        var r = await ProcessRunner.RunAsync("cmd.exe",
            new[] { "/c", _sdk.SdkManagerBat, "--list" },
            extraEnv: NoPathConv,
            timeout: TimeSpan.FromMinutes(3),
            ct: ct);
        var list = new List<string>();
        bool inAvailable = false;
        foreach (var raw in r.StdOut.Split('\n'))
        {
            var line = raw.Trim().Replace("\r", "");
            if (line.StartsWith("Available Packages", StringComparison.OrdinalIgnoreCase)) { inAvailable = true; continue; }
            if (line.StartsWith("Available Updates", StringComparison.OrdinalIgnoreCase)) { inAvailable = false; continue; }
            if (!inAvailable) continue;
            // sdkmanager output rows look like:  system-images;android-36;google_apis_playstore;x86_64 | 1 | Google Play …
            if (!line.StartsWith("system-images;", StringComparison.OrdinalIgnoreCase)) continue;
            var pkg = line.Split('|')[0].Trim();
            if (pkg.Length > 0) list.Add(pkg);
        }
        return list;
    }

    public async Task<SdkPackageInventory> ListPackageInventoryAsync(CancellationToken ct = default)
    {
        if (_sdk.SdkManagerBat is null) return new SdkPackageInventory([], []);
        var r = await ProcessRunner.RunAsync("cmd.exe",
            new[] { "/c", _sdk.SdkManagerBat, "--list" },
            extraEnv: NoPathConv,
            timeout: TimeSpan.FromMinutes(3),
            ct: ct);
        return ParsePackageInventory(r.StdOut);
    }

    public static SdkPackageInventory ParsePackageInventory(string stdout)
    {
        var installed = new List<SdkInstalledPackage>();
        var updates = new List<SdkPackageUpdate>();
        var section = "";

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.Trim().Replace("\r", "");
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("Installed packages", StringComparison.OrdinalIgnoreCase))
            {
                section = "installed";
                continue;
            }
            if (line.StartsWith("Available Updates", StringComparison.OrdinalIgnoreCase))
            {
                section = "updates";
                continue;
            }
            if (line.StartsWith("Available Packages", StringComparison.OrdinalIgnoreCase))
            {
                section = "available";
                continue;
            }
            if (line.StartsWith("Path", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ID", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("-", StringComparison.Ordinal))
                continue;

            var cells = line.Split('|').Select(static cell => cell.Trim()).ToArray();
            if (section == "installed" && cells.Length >= 4 && !string.IsNullOrWhiteSpace(cells[0]))
            {
                installed.Add(new SdkInstalledPackage(cells[0], cells[1], cells[2], cells[3]));
            }
            else if (section == "updates" && cells.Length >= 3 && !string.IsNullOrWhiteSpace(cells[0]))
            {
                updates.Add(new SdkPackageUpdate(cells[0], cells[1], cells[2], Description: ""));
            }
        }

        var descriptions = installed.ToDictionary(static p => p.Path, static p => p.Description, StringComparer.OrdinalIgnoreCase);
        updates = updates
            .Select(update => update with
            {
                Description = descriptions.GetValueOrDefault(update.Path) ?? ""
            })
            .ToList();
        return new SdkPackageInventory(installed, updates);
    }

    public static bool IsUpdateManagedByAep(SdkPackageUpdate update)
        => update.Path.Equals("emulator", StringComparison.OrdinalIgnoreCase)
           || update.Path.Equals("platform-tools", StringComparison.OrdinalIgnoreCase)
           || update.Path.StartsWith("system-images;", StringComparison.OrdinalIgnoreCase);

    public static string ReceiptDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus", "logs");

    public static SdkUpdateReceipt BuildReceipt(
        IReadOnlyList<string> requestedPackages,
        SdkPackageInventory before,
        SdkPackageInventory after)
    {
        var beforeLookup = before.Installed.ToDictionary(
            static p => p.Path, static p => p.Version, StringComparer.OrdinalIgnoreCase);
        var afterLookup = after.Installed.ToDictionary(
            static p => p.Path, static p => p.Version, StringComparer.OrdinalIgnoreCase);

        var changed = new List<SdkUpdateReceiptChange>();
        foreach (var pkg in requestedPackages)
        {
            var had = beforeLookup.GetValueOrDefault(pkg, "not installed");
            var now = afterLookup.GetValueOrDefault(pkg, "not installed");
            if (!had.Equals(now, StringComparison.OrdinalIgnoreCase))
                changed.Add(new SdkUpdateReceiptChange { Path = pkg, From = had, To = now });
        }

        var rollback = changed
            .Where(c => !c.From.Equals("not installed", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"sdkmanager \"{c.Path}\"")
            .ToList();

        return new SdkUpdateReceipt
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            RequestedPackages = requestedPackages.ToList(),
            Before = before.Installed.Select(p => new SdkUpdateReceiptEntry { Path = p.Path, Version = p.Version }).ToList(),
            After = after.Installed.Select(p => new SdkUpdateReceiptEntry { Path = p.Path, Version = p.Version }).ToList(),
            Changed = changed,
            RollbackCommands = rollback,
        };
    }

    public string WriteReceipt(SdkUpdateReceipt receipt)
    {
        Directory.CreateDirectory(ReceiptDirectory);
        var name = $"sdk-update-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(ReceiptDirectory, name);
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    public void LogReceiptSummary(SdkUpdateReceipt receipt, string receiptPath)
    {
        if (receipt.Changed.Count == 0)
        {
            _log.Info($"SDK update receipt written to {receiptPath} (no version changes detected).");
            return;
        }
        foreach (var c in receipt.Changed)
            _log.Info($"SDK updated: {c.Path}  {c.From} → {c.To}");
        if (receipt.RollbackCommands.Count > 0)
            _log.Detail($"Rollback: reinstall previous versions with: {string.Join(" && ", receipt.RollbackCommands)}");
        _log.Info($"SDK update receipt: {receiptPath}");
    }
}
