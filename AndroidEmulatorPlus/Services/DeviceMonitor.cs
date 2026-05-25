using System.Windows;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

/// <summary>Polls `adb devices` every few seconds and exposes the current snapshot.</summary>
public sealed class DeviceMonitor
{
    private readonly AdbService _adb;
    private readonly LogService _log;
    private readonly SdkLocator _sdk;
    private CancellationTokenSource? _cts;

    public event Action<IReadOnlyList<Device>>? Changed;
    public IReadOnlyList<Device> Current { get; private set; } = Array.Empty<Device>();

    public DeviceMonitor(AdbService adb, LogService log, SdkLocator sdk)
    {
        _adb = adb;
        _log = log;
        _sdk = sdk;
    }

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_sdk.AdbExe is null)
                {
                    await Task.Delay(5000, ct);
                    continue;
                }
                var devs = await _adb.ListDevicesAsync(ct);
                var changed = !devs.SequenceEqual(Current);
                if (changed)
                {
                    Current = devs;
                    if (Application.Current?.Dispatcher is { } d)
                        _ = d.BeginInvoke(() => Changed?.Invoke(Current));
                    else
                        Changed?.Invoke(Current);
                }
            }
            catch { }
            try { await Task.Delay(3000, ct); } catch { }
        }
    }
}
