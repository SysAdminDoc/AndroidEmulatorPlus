using AndroidEmulatorPlus.Helpers;

namespace AndroidEmulatorPlus.Services;

public sealed class QemuImgService
{
    private readonly SdkLocator _sdk;
    private readonly LogService _log;

    public QemuImgService(SdkLocator sdk, LogService log)
    {
        _sdk = sdk;
        _log = log;
    }

    public Task<ProcessResult> ResizeAsync(string qcow2Path, string size, CancellationToken ct = default)
    {
        if (_sdk.QemuImgExe is null) throw new InvalidOperationException("qemu-img.exe not found.");
        _log.Info($"Resizing {System.IO.Path.GetFileName(qcow2Path)} → {size}");
        return ProcessRunner.RunAsync(_sdk.QemuImgExe, new[] { "resize", qcow2Path, size }, ct: ct);
    }

    public Task<ProcessResult> InfoAsync(string qcow2Path, CancellationToken ct = default)
    {
        if (_sdk.QemuImgExe is null) throw new InvalidOperationException("qemu-img.exe not found.");
        return ProcessRunner.RunAsync(_sdk.QemuImgExe, new[] { "info", qcow2Path }, ct: ct);
    }
}
