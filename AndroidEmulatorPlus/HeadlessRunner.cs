using AndroidEmulatorPlus.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidEmulatorPlus;

public sealed class HeadlessRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<LogService>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<SdkLocator>();
        sc.AddSingleton<AdbService>();
        sc.AddSingleton<EmulatorService>();
        sc.AddSingleton<AvdService>();
        sc.AddSingleton<SdkmanagerService>();
        sc.AddSingleton<RootService>();
        sc.AddSingleton<MagiskService>();
        sc.AddSingleton<DownloadService>();
        sc.AddSingleton<HashVerificationService>();
        sc.AddSingleton<CacheDiagnosticsService>();
        sc.AddSingleton<MigrationService>();
        sc.AddSingleton<AppService>();
        sc.AddSingleton<ConfigService>();
        sc.AddSingleton<QemuImgService>();
        sc.AddSingleton<DeviceMonitor>();
        sc.AddSingleton<ApkSignerService>();
        sc.AddSingleton<BundleInstallerService>();
        sc.AddSingleton<NetworkProfileService>();
        sc.AddSingleton<PresetService>();
        sc.AddSingleton<SnapshotService>();
        sc.AddSingleton<UpdateService>();
        sc.AddSingleton<ReleasePreflightService>();

        await using var sp = sc.BuildServiceProvider();
        var log = sp.GetRequiredService<LogService>();
        var sdk = sp.GetRequiredService<SdkLocator>();

        log.EntryAdded += entry => Console.WriteLine(entry.Display);

        var opts = ParseArgs(args);
        if (opts.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        sdk.Refresh();
        if (!sdk.IsReady)
        {
            Console.Error.WriteLine("SDK not found or incomplete. Run the GUI to install cmdline-tools first.");
            return 1;
        }

        var avdSvc = sp.GetRequiredService<AvdService>();
        var emuSvc = sp.GetRequiredService<EmulatorService>();
        var adb = sp.GetRequiredService<AdbService>();

        if (opts.CreateAvd is not null)
        {
            log.Info($"Creating AVD '{opts.CreateAvd}'...");
            var image = opts.Image ?? "system-images;android-35;google_apis_playstore;x86_64";
            var device = opts.Device ?? "pixel_7";
            var r = await avdSvc.CreateAsync(opts.CreateAvd, image, device);
            if (!r.Success)
            {
                Console.Error.WriteLine("AVD creation failed: " + r.Combined.Trim());
                return 1;
            }
            log.Success($"AVD '{opts.CreateAvd}' created.");
        }

        var targetAvd = opts.CreateAvd ?? opts.TargetAvd;

        if (targetAvd is not null && (opts.Root || opts.InstallApks.Count > 0))
        {
            log.Info($"Launching AVD '{targetAvd}'...");
            emuSvc.Launch(targetAvd, coldBoot: true);

            log.Info("Waiting for emulator boot...");
            var devs = await adb.ListDevicesAsync();
            var emu = devs.FirstOrDefault(d => d.IsEmulator);
            var deadline = DateTime.UtcNow.AddMinutes(3);
            while (emu is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(3000);
                devs = await adb.ListDevicesAsync();
                emu = devs.FirstOrDefault(d => d.IsEmulator);
            }
            if (emu is null)
            {
                Console.Error.WriteLine("Emulator did not appear within 3 minutes.");
                return 1;
            }
            var booted = await adb.WaitForBootAsync(emu.Serial, TimeSpan.FromMinutes(3));
            if (!booted)
            {
                Console.Error.WriteLine("Emulator did not finish booting.");
                return 1;
            }
            log.Success($"Emulator booted: {emu.Serial}");

            if (opts.Root)
            {
                log.Info("Rooting...");
                var rootSvc = sp.GetRequiredService<RootService>();
                var ramdisk = rootSvc.FindRamdiskFor(targetAvd);
                if (ramdisk is null)
                {
                    Console.Error.WriteLine("Ramdisk not found for this AVD.");
                    return 1;
                }
                var rel = rootSvc.RelativeRamdiskPath(ramdisk);
                if (rel is null)
                {
                    Console.Error.WriteLine("Cannot resolve relative ramdisk path.");
                    return 1;
                }
                var orig = ramdisk + ".original";
                if (!System.IO.File.Exists(orig)) System.IO.File.Copy(ramdisk, orig);
                var progress = new Progress<string>(s => log.Info(s));
                var ok = await rootSvc.PatchAsync(rel, progress, l => log.Detail(l), CancellationToken.None);
                if (!ok)
                {
                    Console.Error.WriteLine("Root patching failed.");
                    return 1;
                }
                log.Success("Ramdisk patched.");
            }

            if (opts.InstallApks.Count > 0)
            {
                foreach (var apk in opts.InstallApks)
                {
                    if (!System.IO.File.Exists(apk))
                    {
                        log.Warning($"APK not found: {apk}");
                        continue;
                    }
                    log.Info($"Installing {apk}...");
                    var r = await adb.InstallAsync(emu.Serial, new[] { apk });
                    if (r.Success || r.Combined.Contains("Success"))
                        log.Success($"Installed {System.IO.Path.GetFileName(apk)}");
                    else
                        log.Warning($"Install failed: {r.Combined.Trim()}");
                }
            }
        }

        log.Success("Headless run complete.");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            AndroidEmulatorPlus --headless [options]

            Options:
              --create-avd <name>    Create a new AVD with the given name
              --image <path>         System image package (default: android-35 Google Play x86_64)
              --device <profile>     Device profile (default: pixel_7)
              --avd <name>           Target an existing AVD (for --root / --install-apps)
              --root                 Root the AVD with Magisk after creating/launching
              --install-apps <apks>  Install one or more APK files (space-separated, must be last)
              --help                 Show this help
            """);
    }

    private sealed class HeadlessOptions
    {
        public bool ShowHelp;
        public string? CreateAvd;
        public string? Image;
        public string? Device;
        public string? TargetAvd;
        public bool Root;
        public List<string> InstallApks = [];
    }

    private static HeadlessOptions ParseArgs(string[] args)
    {
        var opts = new HeadlessOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--help" or "-h":
                    opts.ShowHelp = true;
                    break;
                case "--headless":
                    break;
                case "--create-avd":
                    if (i + 1 < args.Length) opts.CreateAvd = args[++i];
                    break;
                case "--image":
                    if (i + 1 < args.Length) opts.Image = args[++i];
                    break;
                case "--device":
                    if (i + 1 < args.Length) opts.Device = args[++i];
                    break;
                case "--avd":
                    if (i + 1 < args.Length) opts.TargetAvd = args[++i];
                    break;
                case "--root":
                    opts.Root = true;
                    break;
                case "--install-apps":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                        opts.InstallApks.Add(args[++i]);
                    break;
            }
        }
        return opts;
    }
}
