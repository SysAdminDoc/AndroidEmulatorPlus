namespace AndroidEmulatorPlus.Models;

public sealed record Device(string Serial, string State, string Model, string Product, bool IsEmulator)
{
    public bool IsOnline => State == "device";
    public string Display => string.IsNullOrEmpty(Model) ? Serial : $"{Model} ({Serial})";
}

public sealed record DeviceDiagnostics(
    string Serial,
    string Transport,
    string? ApiLevel,
    string? SecurityPatch,
    string? PlatformToolsVersion,
    bool IsPatchUnknown,
    bool IsPatchStale,
    string Summary);
