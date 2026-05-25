using System.IO;
using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

/// <summary>Edits AVD config.ini and resizes the userdata partition.</summary>
public sealed class ConfigService
{
    private readonly LogService _log;
    private readonly QemuImgService _qemu;

    public ConfigService(LogService log, QemuImgService qemu)
    {
        _log = log;
        _qemu = qemu;
    }

    public void UpdateConfig(Avd avd, IDictionary<string, string> updates)
    {
        AvdService.WriteIni(avd.ConfigPath, updates);
        _log.Info($"AVD config updated ({updates.Count} keys).");
    }

    public async Task<bool> ResizeDiskAsync(Avd avd, string size, bool wipeData, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(avd.ConfigPath)!;
        var qcow2 = Path.Combine(dir, "userdata-qemu.img.qcow2");
        if (!File.Exists(qcow2))
        {
            _log.Warning("No userdata-qemu.img.qcow2 yet — AVD has never booted.");
            UpdateConfig(avd, new Dictionary<string, string> { ["disk.dataPartition.size"] = size });
            return true;
        }
        var r = await _qemu.ResizeAsync(qcow2, size, ct);
        if (!r.Success)
        {
            _log.Error("qemu-img resize failed: " + r.Combined);
            return false;
        }
        UpdateConfig(avd, new Dictionary<string, string> { ["disk.dataPartition.size"] = size });
        if (wipeData)
        {
            // The filesystem inside the partition won't expand on its own. Wipe to recreate.
            var snapshotsDir = Path.Combine(dir, "snapshots");
            if (Directory.Exists(snapshotsDir))
            {
                foreach (var snap in Directory.EnumerateDirectories(snapshotsDir))
                    _log.Warning($"Destroying snapshot: {Path.GetFileName(snap)}");
            }
            try { File.Delete(qcow2); } catch { }
            try { File.Delete(Path.Combine(dir, "userdata.img.qcow2")); } catch { }
            try { File.Delete(Path.Combine(dir, "cache.img.qcow2")); } catch { }
            try { Directory.Delete(snapshotsDir, true); } catch { }
            _log.Info("Wiped qcow2 overlays. AVD will recreate at the new partition size on next launch.");
        }
        else
        {
            _log.Warning("Disk grew, but the inner ext4 partition won't auto-expand. Use Wipe Data to apply.");
        }
        return true;
    }

    /// <summary>
    /// Lists snapshot names and qcow2 overlay paths that <see cref="ResizeDiskAsync"/>
    /// would destroy when called with wipeData=true. Used by the UI to show a typed
    /// confirmation listing exactly what will be lost.
    /// </summary>
    public static (List<string> Snapshots, List<string> Overlays) PreviewWipe(Avd avd)
    {
        var dir = Path.GetDirectoryName(avd.ConfigPath)!;
        var snapshotsDir = Path.Combine(dir, "snapshots");
        var snaps = new List<string>();
        if (Directory.Exists(snapshotsDir))
        {
            foreach (var s in Directory.EnumerateDirectories(snapshotsDir))
                snaps.Add(Path.GetFileName(s));
        }
        var overlays = new List<string>();
        foreach (var name in new[] { "userdata-qemu.img.qcow2", "userdata.img.qcow2", "cache.img.qcow2" })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p))
                try { overlays.Add($"{name} ({new FileInfo(p).Length / 1024 / 1024} MB)"); } catch { overlays.Add(name); }
        }
        return (snaps, overlays);
    }
}
