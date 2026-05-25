using System.IO;

namespace AndroidEmulatorPlus.Services;

/// <summary>Locates the Android SDK and the binaries inside it.</summary>
public sealed class SdkLocator
{
    public string? SdkRoot { get; private set; }
    public string? AdbExe { get; private set; }
    public string? EmulatorExe { get; private set; }
    public string? QemuImgExe { get; private set; }
    public string? AvdManagerBat { get; private set; }
    public string? SdkManagerBat { get; private set; }
    public string? AvdHome { get; private set; }
    public string? ApkSignerBat { get; private set; }

    public SdkLocator()
    {
        Refresh();
    }

    public bool IsReady => SdkRoot is not null && File.Exists(EmulatorExe) && File.Exists(AdbExe);

    public void Refresh()
    {
        SdkRoot = FindSdkRoot();
        AdbExe = FindFile(
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Google.PlatformTools_Microsoft.Winget.Source_8wekyb3d8bbwe\platform-tools\adb.exe"),
            SdkRoot is null ? null : Path.Combine(SdkRoot, "platform-tools", "adb.exe"));

        if (SdkRoot is not null)
        {
            EmulatorExe = First(Path.Combine(SdkRoot, "emulator", "emulator.exe"));
            QemuImgExe = First(Path.Combine(SdkRoot, "emulator", "qemu-img.exe"));
            AvdManagerBat = FindFirstExisting(new[]
            {
                Path.Combine(SdkRoot, "cmdline-tools", "latest", "bin", "avdmanager.bat"),
                Path.Combine(SdkRoot, "cmdline-tools", "bin", "avdmanager.bat"),
                Path.Combine(SdkRoot, "tools", "bin", "avdmanager.bat"),
            });
            SdkManagerBat = FindFirstExisting(new[]
            {
                Path.Combine(SdkRoot, "cmdline-tools", "latest", "bin", "sdkmanager.bat"),
                Path.Combine(SdkRoot, "cmdline-tools", "bin", "sdkmanager.bat"),
                Path.Combine(SdkRoot, "tools", "bin", "sdkmanager.bat"),
            });

            // apksigner.bat lives under build-tools/<version>/; pick the highest-versioned
            // entry that actually has the script. build-tools are SemVer-ish so a simple
            // string-descending sort works.
            var buildToolsRoot = Path.Combine(SdkRoot, "build-tools");
            if (Directory.Exists(buildToolsRoot))
            {
                ApkSignerBat = Directory.EnumerateDirectories(buildToolsRoot)
                    .Select(d => Path.Combine(d, "apksigner.bat"))
                    .Where(File.Exists)
                    .OrderByDescending(p => Path.GetFileName(Path.GetDirectoryName(p)), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
        }

        AvdHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".android", "avd");
    }

    private static string? FindFile(params string?[] candidates)
        => candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

    private static string? First(string path) => File.Exists(path) ? path : null;

    private static string? FindFirstExisting(IEnumerable<string> paths)
        => paths.FirstOrDefault(File.Exists);

    private static string? FindSdkRoot()
    {
        var env = Environment.GetEnvironmentVariable("ANDROID_HOME")
               ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk"),
            @"C:\Android\Sdk",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Android", "Sdk"),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }

    public string SdkRootRequired => SdkRoot ?? throw new InvalidOperationException("Android SDK not located. Use the Install section to set it up.");
    public string EmulatorRequired => EmulatorExe ?? throw new InvalidOperationException("emulator.exe not found in the SDK.");
    public string AdbRequired => AdbExe ?? throw new InvalidOperationException("adb.exe not found.");
}
