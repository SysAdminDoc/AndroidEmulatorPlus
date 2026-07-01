using System.Collections.ObjectModel;
using AndroidEmulatorPlus.Models;
using AndroidEmulatorPlus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidEmulatorPlus.ViewModels;

public sealed partial class MigrateViewModel : ObservableObject
{
    private readonly AdbService _adb;
    private readonly MigrationService _mig;
    private readonly DeviceMonitor _monitor;
    private readonly LogService _log;
    private readonly CacheDiagnosticsService _cache;
    private readonly ToastService _toast;

    public ObservableCollection<AndroidApp> Packages { get; } = new();
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _doApk = true;
    [ObservableProperty] private bool _doInternal = true;
    [ObservableProperty] private bool _doExternal = true;
    [ObservableProperty] private bool _doObb;
    [ObservableProperty] private bool _forceStopOnPhone;
    /// <summary>C-05: override the allowBackup=false skip and migrate data anyway.</summary>
    [ObservableProperty] private bool _forceDataForNoBackup;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _stepText = "";
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string _phoneStatus = "no phone";
    [ObservableProperty] private string _emuStatus = "no emulator";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _cacheSummary = "—";
    [ObservableProperty] private bool _hasCache;
    [ObservableProperty] private string _pairHost = "";
    [ObservableProperty] private string _pairCode = "";
    [ObservableProperty] private string _connectHost = "";
    [ObservableProperty] private string _pairStatus = "";
    [ObservableProperty] private bool _hasFailedReceipt;
    [ObservableProperty] private string _failedReceiptText = "";
    [ObservableProperty] private string _dryRunText = "";
    [ObservableProperty] private bool _hasDryRunResult;
    [ObservableProperty] private bool _dryRunBlocked;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _refreshCts;
    private int _refreshGeneration;

    public MigrateViewModel(AdbService adb, MigrationService mig, DeviceMonitor monitor, LogService log, CacheDiagnosticsService cache, ToastService toast)
    {
        _adb = adb;
        _mig = mig;
        _monitor = monitor;
        _log = log;
        _cache = cache;
        _toast = toast;
        _monitor.Changed += devs => { _ = RefreshAsync(); };
        // C-11: re-measure when other tabs (Apps Export/Import) touch the cache.
        _cache.Changed += () =>
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d)
                _ = d.BeginInvoke(RefreshCache);
            else RefreshCache();
        };
        RefreshCache();
    }

    [RelayCommand]
    private void RefreshCache()
    {
        var u = _cache.Measure();
        if (u.Total <= 0)
        {
            HasCache = false;
            CacheSummary = "Cache is empty.";
            return;
        }
        HasCache = true;
        var parts = new List<string>();
        if (u.TransferBytes > 0) parts.Add($"migration {u.Human(u.TransferBytes)}");
        if (u.BundleBytes > 0) parts.Add($"bundles {u.Human(u.BundleBytes)}");
        if (u.RootBytes > 0) parts.Add($"root {u.Human(u.RootBytes)}");
        CacheSummary = $"Cache total {u.Human(u.Total)}  ·  " + string.Join(" · ", parts);
    }

    [RelayCommand]
    private void ClearTransferCache()
    {
        _cache.ClearTransfer();
        RefreshCache();
    }

    [RelayCommand]
    private void ClearRootCache()
    {
        _cache.ClearRootCache();
        RefreshCache();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            _log.Detail("Migration refresh skipped while a transfer is running.");
            return;
        }

        var generation = Interlocked.Increment(ref _refreshGeneration);
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);

        try
        {
            if (phone is null) PhoneStatus = "no phone connected";
            else
            {
                var rooted = await _adb.IsRootedAsync(phone.Serial, ct);
                if (generation != _refreshGeneration) return;
                PhoneStatus = $"{phone.Display}{(rooted ? "  rooted" : "  not rooted - data copy unavailable")}";
            }

            if (emu is null) EmuStatus = "no emulator running";
            else
            {
                var rooted = await _adb.IsRootedAsync(emu.Serial, ct);
                if (generation != _refreshGeneration) return;
                EmuStatus = $"{emu.Serial}{(rooted ? "  rooted" : "  not rooted - data restore unavailable")}";
            }

            if (phone is null)
            {
                Packages.Clear();
                NotifyPackageListStateChanged();
                return;
            }
            var list = await _adb.ListPackagesAsync(phone.Serial, ct: ct);
            if (generation != _refreshGeneration) return;

            Packages.Clear();
            foreach (var p in list)
                Packages.Add(new AndroidApp { Package = p, IsSelected = true });
            NotifyPackageListStateChanged();

            // C-05: probe each package's allowBackup flag in the background.
            // Sequential because `pm dump` is comparatively cheap and a thousand
            // parallel adb shells flood the bridge. The generation guard prevents
            // stale probes from mutating rows after a newer refresh.
            var snapshot = Packages.ToList();
            _ = Task.Run(() => ProbeAllowBackupAsync(phone.Serial, snapshot, generation, ct), ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProbeAllowBackupAsync(string serial, IReadOnlyList<AndroidApp> snapshot, int generation, CancellationToken ct)
    {
        try
        {
            foreach (var app in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                var ok = await _mig.AllowsBackupAsync(serial, app.Package, ct);
                if (ok || generation != _refreshGeneration) continue;

                if (System.Windows.Application.Current?.Dispatcher is { } d)
                {
                    _ = d.BeginInvoke(() =>
                    {
                        if (generation == _refreshGeneration)
                            app.AllowBackup = false;
                    });
                }
                else
                {
                    app.AllowBackup = false;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == _refreshGeneration)
                _log.Warning("allowBackup probe failed: " + ex.Message);
        }
    }

    public IEnumerable<AndroidApp> FilteredPackages => string.IsNullOrWhiteSpace(Filter)
        ? Packages
        : Packages.Where(p => p.Package.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    public bool HasPackages => Packages.Count > 0;
    public bool HasFilteredPackages => FilteredPackages.Any();
    public bool IsPackageFilterEmpty => HasPackages && !HasFilteredPackages;

    private void NotifyPackageListStateChanged()
    {
        OnPropertyChanged(nameof(FilteredPackages));
        OnPropertyChanged(nameof(HasPackages));
        OnPropertyChanged(nameof(HasFilteredPackages));
        OnPropertyChanged(nameof(IsPackageFilterEmpty));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var p in Packages) p.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var p in Packages) p.IsSelected = false;
    }

    [RelayCommand]
    private async Task DryRunAsync()
    {
        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (phone is null || emu is null) { _log.Warning("Need both a phone and an emulator connected."); return; }
        var sel = Packages.Where(p => p.IsSelected).Select(p => p.Package).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one package."); return; }

        IsBusy = true;
        StepText = "Running dry-run preflight...";
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _mig.DryRunAsync(phone.Serial, emu.Serial, sel,
                DoApk, DoInternal, DoExternal, DoObb, ForceDataForNoBackup, _cts.Token);
            HasDryRunResult = true;
            DryRunBlocked = !result.CanProceed;
            DryRunText = result.Summary;
            _log.Info("Dry-run: " + result.Summary);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DryRunText = "Dry-run failed: " + ex.Message;
            HasDryRunResult = true;
            DryRunBlocked = false;
        }
        finally
        {
            IsBusy = false;
            StepText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (phone is null || emu is null) { _log.Warning("Need both a phone and an emulator connected."); return; }

        var sel = Packages.Where(p => p.IsSelected).ToList();
        if (sel.Count == 0) { _log.Warning("Select at least one package."); return; }

        await RunMigrationAsync(phone.Serial, emu.Serial, sel);
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        var receipt = MigrationService.ReadLatestReceipt();
        if (receipt is null || receipt.FailedPackages.Count == 0)
        {
            _log.Info("No failed packages to retry.");
            return;
        }

        var phone = _monitor.Current.FirstOrDefault(d => !d.IsEmulator);
        var emu = _monitor.Current.FirstOrDefault(d => d.IsEmulator);
        if (phone is null || emu is null) { _log.Warning("Need both a phone and an emulator connected."); return; }

        var failedSet = new HashSet<string>(receipt.FailedPackages, StringComparer.Ordinal);
        var toRetry = Packages.Where(p => failedSet.Contains(p.Package)).ToList();
        if (toRetry.Count == 0)
        {
            _log.Warning("Failed packages from last receipt not found in current package list.");
            return;
        }

        _log.Info($"Retrying {toRetry.Count} failed package(s) from last migration.");
        await RunMigrationAsync(phone.Serial, emu.Serial, toRetry);
    }

    private async Task RunMigrationAsync(string phoneSerial, string emuSerial, IReadOnlyList<AndroidApp> sel)
    {
        IsBusy = true;
        _refreshCts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int ok = 0, fail = 0; long totalBytes = 0;
        _mig.ForceStopOnPhone = ForceStopOnPhone;
        var packageReceipts = new List<MigrationPackageReceipt>();
        var scopes = new List<string>();
        if (DoApk) scopes.Add("apk");
        if (DoInternal) scopes.Add("internal");
        if (DoExternal) scopes.Add("external");
        if (DoObb) scopes.Add("obb");
        bool cancelled = false;
        try
        {
            int idx = 0;
            foreach (var p in sel)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }
                idx++;
                StepText = $"{idx}/{sel.Count}  {p.Package}";
                ProgressFraction = (double)idx / sel.Count;

                var legs = new List<MigrationLegReceipt>();
                bool pkgOk = true;

                if (DoApk)
                {
                    var r = await _mig.TransferApkAsync(phoneSerial, emuSerial, p.Package, ct);
                    legs.Add(new MigrationLegReceipt { Leg = "apk", Success = r.Success, SizeBytes = r.SizeBytes, Detail = r.Detail });
                    if (r.Success) _log.Info($"APK ok {p.Package} ({r.Detail})");
                    else { _log.Warning($"APK fail {p.Package}: {r.Detail}"); fail++; pkgOk = false; }
                }

                if (pkgOk && DoInternal)
                {
                    if (p.AllowBackup && !ForceDataForNoBackup)
                        p.AllowBackup = await _mig.AllowsBackupAsync(phoneSerial, p.Package, ct);

                    if (!p.AllowBackup && !ForceDataForNoBackup)
                    {
                        _log.Warning($"data skip {p.Package}: allowBackup=false (toggle 'Force-migrate no-backup apps' to override)");
                        legs.Add(new MigrationLegReceipt { Leg = "internal", Success = true, SizeBytes = 0, Detail = "skipped: allowBackup=false" });
                    }
                    else
                    {
                        var r = await _mig.TransferInternalDataAsync(phoneSerial, emuSerial, p.Package, ct);
                        totalBytes += r.SizeBytes;
                        legs.Add(new MigrationLegReceipt { Leg = "internal", Success = r.Success, SizeBytes = r.SizeBytes, Detail = r.Detail });
                        if (r.Success) _log.Info($"data ok {p.Package} ({r.SizeBytes / 1024 / 1024} MB, {r.Detail})");
                        else { _log.Warning($"data fail {p.Package}: {r.Detail}"); pkgOk = false; }
                    }
                }

                if (DoExternal)
                {
                    var r = await _mig.TransferExternalDataAsync(phoneSerial, emuSerial, p.Package, ct);
                    totalBytes += r.SizeBytes;
                    legs.Add(new MigrationLegReceipt { Leg = "external", Success = r.Success, SizeBytes = r.SizeBytes, Detail = r.Detail });
                    if (r.Success && r.SizeBytes > 0) _log.Info($"ext ok {p.Package} ({r.SizeBytes / 1024 / 1024} MB)");
                }
                if (DoObb)
                {
                    var r = await _mig.TransferObbAsync(phoneSerial, emuSerial, p.Package, ct);
                    totalBytes += r.SizeBytes;
                    legs.Add(new MigrationLegReceipt { Leg = "obb", Success = r.Success, SizeBytes = r.SizeBytes, Detail = r.Detail });
                    if (r.Success && r.SizeBytes > 0) _log.Info($"obb ok {p.Package} ({r.SizeBytes / 1024 / 1024} MB)");
                }

                packageReceipts.Add(new MigrationPackageReceipt
                {
                    Package = p.Package,
                    Success = pkgOk && legs.All(l => l.Success),
                    Legs = legs,
                });
                if (pkgOk) ok++;
            }

            cancelled = cancelled || ct.IsCancellationRequested;
            Summary = $"{ok} ok, {fail} fail, {totalBytes / 1024 / 1024} MB data" + (cancelled ? "  (cancelled)" : "");
            _log.Success("Migration " + (cancelled ? "cancelled. " : "finished. ") + Summary);
            _toast.Show("Migration complete", Summary);

            var receipt = MigrationService.BuildReceipt(phoneSerial, emuSerial, scopes, packageReceipts, cancelled);
            try { _mig.WriteReceipt(receipt); }
            catch (Exception ex) { _log.Warning("Failed to write migration receipt: " + ex.Message); }

            HasFailedReceipt = receipt.FailCount > 0;
            FailedReceiptText = HasFailedReceipt
                ? $"{receipt.FailCount} package(s) failed. Use 'Retry failed' to re-run."
                : "";
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Migration cancelled by user.");
        }
        finally
        {
            IsBusy = false;
            StepText = "";
            ProgressFraction = 0;
            RefreshCache();
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    /// <summary>
    /// B-06: Pair with an Android 11+ phone over Wi-Fi. The user supplies the
    /// pairing host:port + 6-digit code from "Wireless debugging → Pair using
    /// pairing code"; if successful, they then enter the connect host:port (the
    /// other line on the same Wireless debugging screen).
    /// </summary>
    [RelayCommand]
    private async Task PairAsync()
    {
        var host = (PairHost ?? "").Trim();
        var code = (PairCode ?? "").Trim();
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(code))
        {
            PairStatus = "Both host:port and the 6-digit code are required.";
            return;
        }
        PairStatus = "Pairing…";
        var r = await _adb.PairAsync(host, code);
        if (r.Success && r.Combined.Contains("Successfully", StringComparison.OrdinalIgnoreCase))
        {
            PairStatus = "Paired. Enter the connect host:port below.";
            _log.Success($"Paired with {host}.");
        }
        else
        {
            PairStatus = "Pair failed - " + r.Combined.Trim();
            _log.Error("adb pair: " + r.Combined.Trim());
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var host = (ConnectHost ?? "").Trim();
        if (string.IsNullOrEmpty(host)) { PairStatus = "Enter the connect host:port."; return; }
        PairStatus = "Connecting…";
        var r = await _adb.ConnectAsync(host);
        if (r.Combined.Contains("connected", StringComparison.OrdinalIgnoreCase)
            && !r.Combined.Contains("cannot", StringComparison.OrdinalIgnoreCase))
        {
            PairStatus = "Connected. " + r.StdOut.Trim();
            _log.Success($"adb connect {host}: " + r.StdOut.Trim());
        }
        else
        {
            PairStatus = "Connect failed - " + r.Combined.Trim();
            _log.Error("adb connect: " + r.Combined.Trim());
        }
    }

    partial void OnFilterChanged(string value) => NotifyPackageListStateChanged();
}
