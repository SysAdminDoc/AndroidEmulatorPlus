namespace AndroidEmulatorPlus.Models;

public sealed record ScreenPreset(string Name, int Width, int Height, int Dpi)
{
    public override string ToString() => Name;

    public static readonly IReadOnlyList<ScreenPreset> All = new[]
    {
        new ScreenPreset("Pixel 7",          1080, 2400, 420),
        new ScreenPreset("Pixel 7 Pro",      1440, 3120, 560),
        new ScreenPreset("Pixel 8",          1080, 2400, 420),
        new ScreenPreset("Pixel 8 Pro",      1344, 2992, 490),
        new ScreenPreset("Pixel 9 Pro",      1280, 2856, 495),
        new ScreenPreset("Pixel 10",         1080, 2424, 420),
        new ScreenPreset("Pixel 10 Pro",     1344, 2992, 490),
        new ScreenPreset("Pixel 10 Pro XL",  1344, 2992, 490),
        new ScreenPreset("Pixel 10 Pro Fold",2076, 2152, 420),
        new ScreenPreset("Pixel Tablet",     2560, 1600, 320),
        new ScreenPreset("Pixel Fold (open)",   2208, 1840, 420),
        new ScreenPreset("Pixel Fold (closed)", 1080, 2092, 420),
        new ScreenPreset("Nexus 5X",         1080, 1920, 420),
        new ScreenPreset("Small phone",      720,  1280, 320),
        new ScreenPreset("1080p TV",         1920, 1080, 213),
    };

    public bool Matches(int w, int h, int dpi) => Width == w && Height == h && Dpi == dpi;
}
