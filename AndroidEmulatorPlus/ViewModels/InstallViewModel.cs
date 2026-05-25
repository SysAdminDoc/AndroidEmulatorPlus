using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class InstallViewModel : ObservableObject
{
    private readonly SdkLocator _sdk;
    private readonly DownloadService _dl;
    private readonly EmulatorService _emu;
    private readonly HashVerificationService _hash;
    private readonly LogService _log;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _sdkPathText = "";
    [ObservableProperty] private bool _hasPlatformTools;
    [ObservableProperty] private bool _hasEmulator;
    [ObservableProperty] private bool _hasCmdlineTools;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _step = "";
    [ObservableProperty] private string _diagnosticsText = "";
    [ObservableProperty] private bool _hasDiagnostics;
    [ObservableProperty] private string _accelText = "Not checked.";
    [ObservableProperty] private bool _accelOk;
    [ObservableProperty] private string _cmdlineToolsNote = "";
    [ObservableProperty] private bool _hasCmdlineToolsNote;

    private static string DiagnosticsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroidEmulatorPlus");
    private static string CrashLogPath => Path.Combine(DiagnosticsRoot, "crash.log");
    private static string DailyLogPath => Path.Combine(DiagnosticsRoot, "logs", $"app-{DateTime.Now:yyyyMMdd}.log");

    public InstallViewModel(SdkLocator sdk, DownloadService dl, EmulatorService emu, HashVerificationService hash, LogService log)
    {
        _sdk = sdk;
        _dl = dl;
        _emu = emu;
        _hash = hash;
        _log = log;
    }

    [RelayCommand]
    private void Refresh()
    {
        _sdk.Refresh();
        if (_sdk.SdkRoot is null)
        {
            StatusText = "SDK not located.";
            SdkPathText = "(no SDK detected)";
            HasPlatformTools = HasEmulator = HasCmdlineTools = false;
        }
        else
        {
            SdkPathText = _sdk.SdkRoot;
            HasPlatformTools = _sdk.AdbExe != null;
            HasEmulator = _sdk.EmulatorExe != null;
            HasCmdlineTools = _sdk.SdkManagerBat != null;
            StatusText = _sdk.IsReady ? "SDK looks good." : "SDK is missing pieces.";
        }
        LoadDiagnostics();
    }

    private void LoadDiagnostics()
    {
        var hasCrash = File.Exists(CrashLogPath);
        HasDiagnostics = hasCrash || File.Exists(DailyLogPath);
        if (!HasDiagnostics) { DiagnosticsText = "No diagnostics on file."; return; }

        try
        {
            var sb = new System.Text.StringBuilder();
            if (hasCrash)
            {
                var lines = File.ReadAllLines(CrashLogPath);
                sb.AppendLine($"--- crash.log ({lines.Length} lines) ---");
                foreach (var l in lines.Reverse().Take(50)) sb.AppendLine(l);
            }
            DiagnosticsText = sb.ToString();
        }
        catch (Exception ex) { DiagnosticsText = $"(could not read diagnostics: {ex.Message})"; }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        var dir = Path.Combine(DiagnosticsRoot, "logs");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearCrashLog()
    {
        try { if (File.Exists(CrashLogPath)) File.Delete(CrashLogPath); } catch (Exception ex) { _log.Warning("Clear crash.log failed: " + ex.Message); }
        LoadDiagnostics();
    }

    [RelayCommand]
    private async Task CheckAccelAsync()
    {
        if (_sdk.EmulatorExe is null) { AccelText = "emulator.exe not found — install platform-tools + emulator first."; AccelOk = false; return; }
        IsBusy = true;
        Step = "Running emulator -accel-check…";
        try
        {
            var status = await _emu.AccelCheckAsync();
            AccelOk = status.Ok;
            AccelText = status.Ok ? $"✓ {status.Summary}" : $"✗ {status.Summary}";
            _log.Info("Accel: " + status.Summary);
        }
        catch (Exception ex) { AccelOk = false; AccelText = "✗ " + ex.Message; }
        finally { IsBusy = false; Step = ""; }
    }

    [RelayCommand]
    private async Task DownloadCmdlineToolsAsync()
    {
        IsBusy = true;
        Step = "Resolving latest command-line-tools URL…";
        try
        {
            var sdkRoot = _sdk.SdkRoot
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
            Directory.CreateDirectory(sdkRoot);

            // Scrape developer.android.com/studio for the current Windows build; falls back
            // to a stable known-good URL if the scrape fails.
            var res = await _dl.LatestCmdlineToolsWindowsUrlAsync();
            _log.Info($"Using cmdline-tools URL: {res.Url}");
            if (res.IsFallback)
            {
                HasCmdlineToolsNote = true;
                CmdlineToolsNote = $"⚠ Using fallback cmdline-tools URL ({res.Reason ?? "scrape failed"}). " +
                    "Build number may be older than what's published today.";
            }
            else
            {
                HasCmdlineToolsNote = false;
                CmdlineToolsNote = "";
            }
            Step = "Downloading command-line tools…";
            var zip = Path.Combine(Path.GetTempPath(), "android-cmdline-tools.zip");
            await _dl.DownloadAsync(url: res.Url, dest: zip);

            Step = "Verifying download…";
            var check = _hash.VerifyCmdlineTools(res.Url, zip);
            if (!check.Ok)
            {
                try { File.Delete(zip); } catch { }
                _log.Error($"cmdline-tools download SHA-256 mismatch: {check.Detail}. Partial download deleted.");
                return;
            }

            Step = "Extracting…";
            var staging = Path.Combine(sdkRoot, "cmdline-tools-staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zip, staging);

            // Move to the canonical layout: cmdline-tools/latest
            var latestDir = Path.Combine(sdkRoot, "cmdline-tools", "latest");
            Directory.CreateDirectory(Path.GetDirectoryName(latestDir)!);
            if (Directory.Exists(latestDir)) Directory.Delete(latestDir, true);
            var unzippedRoot = Path.Combine(staging, "cmdline-tools");
            Directory.Move(unzippedRoot, latestDir);
            try { Directory.Delete(staging, true); } catch { }
            try { File.Delete(zip); } catch { }

            _log.Success($"Installed command-line tools at {latestDir}");
            Refresh();
        }
        catch (Exception ex)
        {
            _log.Error("cmdline-tools install failed: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            Step = "";
        }
    }

    [RelayCommand]
    private void OpenStudioDownloadPage()
    {
        Process.Start(new ProcessStartInfo("https://developer.android.com/studio") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenSdkFolder()
    {
        if (_sdk.SdkRoot is null || !Directory.Exists(_sdk.SdkRoot)) return;
        Process.Start(new ProcessStartInfo(_sdk.SdkRoot) { UseShellExecute = true });
    }
}
