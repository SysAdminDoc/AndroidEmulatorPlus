using AndroidEmulatorPlus.Models;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// Owns the user-selected phone and emulator serials used by every device-facing
/// workflow. A single connected device is selected implicitly; multiple devices
/// require an explicit top-bar selection so an operation cannot target the wrong
/// transport.
/// </summary>
public sealed class ActiveDeviceContext
{
    private string? _phoneSerial;
    private string? _emulatorSerial;

    public event Action? Changed;

    public string? SelectedPhoneSerial => _phoneSerial;
    public string? SelectedEmulatorSerial => _emulatorSerial;

    /// <summary>
    /// Resolves the selected phone. If there is exactly one phone, it is safe to
    /// use without an explicit combo-box selection. Multiple phones return null
    /// until the user selects one.
    /// </summary>
    public Device? ResolvePhone(IEnumerable<Device> devices)
        => Resolve(devices.Where(static d => !d.IsEmulator), _phoneSerial);

    /// <summary>Resolves the selected emulator using the same ambiguity rule as phones.</summary>
    public Device? ResolveEmulator(IEnumerable<Device> devices)
        => Resolve(devices.Where(static d => d.IsEmulator), _emulatorSerial);

    public Device? ResolveOnlinePhone(IEnumerable<Device> devices)
    {
        var phone = ResolvePhone(devices);
        return phone?.IsOnline == true ? phone : null;
    }

    public Device? ResolveOnlineEmulator(IEnumerable<Device> devices)
    {
        var emulator = ResolveEmulator(devices);
        return emulator?.IsOnline == true ? emulator : null;
    }

    public string ExplainPhoneSelection(IEnumerable<Device> devices)
        => Explain(devices.Where(static d => !d.IsEmulator), _phoneSerial, "phone");

    public string ExplainEmulatorSelection(IEnumerable<Device> devices)
        => Explain(devices.Where(static d => d.IsEmulator), _emulatorSerial, "emulator");

    public void SelectPhone(Device? device)
        => Set(ref _phoneSerial, device?.Serial);

    public void SelectEmulator(Device? device)
        => Set(ref _emulatorSerial, device?.Serial);

    private static Device? Resolve(IEnumerable<Device> candidates, string? selectedSerial)
    {
        var list = candidates.ToList();
        if (selectedSerial is not null)
            return list.FirstOrDefault(d => string.Equals(d.Serial, selectedSerial, StringComparison.Ordinal));
        return list.Count == 1 ? list[0] : null;
    }

    private static string Explain(IEnumerable<Device> candidates, string? selectedSerial, string kind)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return $"No {kind} is connected.";
        if (selectedSerial is not null)
        {
            var selected = list.FirstOrDefault(d => string.Equals(d.Serial, selectedSerial, StringComparison.Ordinal));
            if (selected is null) return $"Selected {kind} '{selectedSerial}' is no longer connected; choose another target.";
            if (!selected.IsOnline) return $"Selected {kind} '{selectedSerial}' is not online (state: {selected.State}); resolve ADB trust first.";
            return $"Selected {kind} '{selectedSerial}' is unavailable.";
        }
        if (list.Count > 1) return $"Select an active {kind} in the top bar before running this operation.";
        return $"The connected {kind} is not online (state: {list[0].State}); resolve ADB trust first.";
    }

    private void Set(ref string? field, string? value)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        Changed?.Invoke();
    }
}
