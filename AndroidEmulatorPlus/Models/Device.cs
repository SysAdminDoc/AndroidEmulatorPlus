namespace AndroidEmulatorPlus.Models;

public sealed record Device(string Serial, string State, string Model, string Product, bool IsEmulator)
{
    public bool IsOnline => State == "device";
    public string Display => string.IsNullOrEmpty(Model) ? Serial : $"{Model} ({Serial})";
}
