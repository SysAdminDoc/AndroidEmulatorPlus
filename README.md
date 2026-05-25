# AndroidEmulatorPlus

[![Version](https://img.shields.io/badge/version-0.2.0-blue.svg)](https://github.com/SysAdminDoc/AndroidEmulatorPlus/releases)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-9.0-blueviolet.svg)](#)

A single Windows app that handles the full Android-on-PC story end-to-end:
install the SDK, manage AVDs, **root the emulator with Magisk**, **migrate apps + app data from a USB-connected phone**, debloat, and tune the AVD's hardware.

No more juggling `adb`, `emulator`, `qemu-img`, `rootAVD`, and a pile of bash scripts. Click buttons instead.

---

## What it does

| Section | What you can do |
|---|---|
| **① Install / SDK** | Detect Android SDK at standard locations. If missing, download the command-line tools (~150 MB) and lay them out at `cmdline-tools/latest`. |
| **② AVDs** | List existing AVDs (with their resolution, RAM, disk, Play Store status). Launch / cold-boot. Delete. Create new from any installed system image. |
| **③ Root** | Patch the AVD's `ramdisk.img` with the latest Magisk via [rootAVD](https://gitlab.com/newbit/rootAVD). Auto-downloads Magisk from GitHub releases, clones rootAVD on demand, backs up the stock ramdisk. Persists `shell→allow` policy in Magisk DB so `adb shell su` works headlessly thereafter. |
| **④ Migrate from Phone** | Detects a USB-connected phone, lists its user apps, pulls each APK (split-aware) and installs on the emulator, then `tar`s `/data/data/<pkg>` over to the emulator and re-owns it to the new UID. Optionally also copies `/sdcard/Android/data/<pkg>`. |
| **⑤ Apps / Debloat** | Inventory installed apps on the running emulator. Multi-select uninstall. Preset bloat lists (Google, Samsung). Batch APK install from file picker. |
| **⑥ Configure** | Edit `config.ini`: RAM, vCPUs, screen, DPI, disk size. Resize the qcow2 partition with `qemu-img`, optionally wiping data so the inner ext4 actually grows. |

A live device-monitor in the top bar shows whether a phone and an emulator are currently attached over `adb`.

## Why this exists

Setting up an Android emulator with a real Google Play Store, rooted with Magisk, populated with your phone's apps and data is a ~30 step process touching half a dozen CLIs. This collapses it into a few clicks.

## Requirements

- Windows 10/11 x64
- [.NET 9 Runtime](https://dotnet.microsoft.com/download) (or build from source)
- A hypervisor (Windows Hypervisor Platform / Hyper-V) for the emulator
- For Root flow: **Git for Windows** (rootAVD is a bash script)
- For Migrate flow: a USB-connected Android phone with USB debugging enabled. Internal data copy requires the phone to be rooted (Magisk).

## Build from source

```bash
git clone https://github.com/SysAdminDoc/AndroidEmulatorPlus.git
cd AndroidEmulatorPlus
dotnet build AndroidEmulatorPlus/AndroidEmulatorPlus.csproj -c Release
```

The output sits in `AndroidEmulatorPlus/bin/Release/net9.0-windows/AndroidEmulatorPlus.exe`.

For a framework-dependent publish:

```bash
dotnet publish AndroidEmulatorPlus/AndroidEmulatorPlus.csproj -c Release -r win-x64 --self-contained false
```

## Typical workflow

```
1. ① Install   →  ensures SDK + emulator are present
2. ② AVDs      →  create "MyEmulator" from a Google Play system image
3. ② AVDs      →  Launch it; let Play Store sign in
4. ③ Root      →  Root with Latest Magisk → cold-boot → Verify
5. ④ Migrate   →  pick your phone's apps → start (3-10 minutes)
6. ⑤ Apps      →  remove anything you don't want
7. ⑥ Configure →  bump RAM/disk if needed
```

## Privacy & network

The app only reaches out to:

- `https://dl.google.com/android/repository/…` — Android command-line tools ZIP.
- `https://developer.android.com/studio` — to discover the current cmdline-tools URL.
- `https://api.github.com/repos/topjohnwu/Magisk/…` — to discover the latest Magisk release.
- `https://gitlab.com/newbit/rootAVD.git` — `git clone` of the patcher.

All traffic is HTTPS. No telemetry is sent, no accounts are required, and no data
ever leaves your machine. Crash details (unhandled exceptions only) are written
locally to `%LOCALAPPDATA%\AndroidEmulatorPlus\crash.log`.

## Acknowledgements

- [rootAVD](https://gitlab.com/newbit/rootAVD) by NewBit — the actual Magisk ramdisk patcher.
- [Magisk](https://github.com/topjohnwu/Magisk) by topjohnwu.
- [Catppuccin](https://github.com/catppuccin/catppuccin) Mocha palette.

## License

MIT — see [LICENSE](LICENSE).
