# Changelog

All notable changes to this project will be documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version scheme is [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Pinning hook for rootAVD: `RootService.RootAvdPinnedRef` constant lets future releases pin the script to a verified SHA. When set, the clone uses full depth and `git checkout --detach` lands on the pin.
- Rolling daily log file at `%LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log`. Mirrors every UI log entry so a failed root/migrate can be post-mortemed even after the in-memory ring fills. Logs older than 14 days are pruned on startup. Session header now records the active rootAVD pin so incident reports can correlate.
- Diagnostics card on the Install / SDK panel surfaces the last 50 lines of `crash.log` with "Open logs folder" and "Clear crash.log" actions. Card hides itself when there is nothing to show.
- AVD cards now show a green "● Running" pill when that AVD is the currently attached emulator. Detection runs `getprop ro.kernel.qemu.avd_name` (with `ro.boot.qemu.avd_name` fallback) on every `adb devices` snapshot.
- Top-bar "📷 Screenshot" button captures a PNG from the attached emulator (shell `screencap -p` → pull → cleanup) into `%USERPROFILE%\Pictures\AndroidEmulatorPlus\` and opens it in the system default viewer. Disabled when no emulator is attached.
- Apps tab accepts drag-and-drop of `.apk` / `.apks` / `.xapk` files (one or many) and installs them on the attached emulator via the same batch path used by "Install APK…".
- Hardware-acceleration check on the Install / SDK panel runs `emulator -accel-check` and shows a one-line verdict (✓/✗ + summary). Useful when the emulator silently falls back to software rendering after a Windows Hypervisor / Hyper-V regression.
- Cmdline-tools URL is now resolved at install time from `https://developer.android.com/studio` instead of a hard-coded `commandlinetools-win-NNNN_latest.zip`. Falls back to the previous hard-coded URL if the scrape fails (so first-launch still works offline-ish); a yellow notice surfaces in the UI when the fallback is in use.
- Empty-state card on the AVDs tab when no AVDs exist — links the user to the create form.
- `.apks` / `.xapk` / `.apkm` bundles are now extracted before install. Inner splits go through `adb install-multiple`; any `*.obb` files are pushed to `/sdcard/Android/obb/<pkg>/` (package name resolved from a `manifest.json` / `info.json` inside the bundle, or the `main.<ver>.<pkg>.obb` filename convention). Previously the dialog filter advertised these formats but `adb install` refused them.

### Changed

- `Detail` log entries now use `SubtextBrush` instead of `OverlayBrush` so the text meets WCAG AA contrast on the log panel.
- System-image dropdown on the AVDs tab orders entries by parsed API level descending (Play Store > Google APIs > default), so Create AVD defaults to the newest installed image instead of the alphabetically last one.

### Fixed

- "Download command-line tools" and "Root with Latest Magisk" buttons now disable correctly while their `IsBusy` flag is set; the previous binding routed a `Visibility` value into `IsEnabled` and silently fell back to `True`, allowing re-entry into long-running downloads/patches.
- `DownloadService.LatestMagiskAsync` now skips `-debug` and `-stub` Magisk APKs when picking the asset, defending against future Magisk releases that publish those before the canonical APK.
- "■ Stop" button replaces "▶ Launch" on running AVD cards (calls `adb -s <serial> emu kill`).
- "⋯" overflow menu on each AVD card: **Show on disk**, **Create desktop shortcut**, **Delete AVD**. Desktop shortcut writes `Emulator - <Name>.cmd` to the user Desktop (no Shell32 / COM dependency).
- `AvdService.RenameAsync` (`avdmanager move avd -n … -r …`) and `AvdViewModel.RenameCommand` are in place — the AVD-card popup that drives them lands in the next release.
- `ConfigViewModel.ParseSizeGb` now accepts raw-byte `disk.dataPartition.size` values (e.g. `8589934592`). Previously such AVDs were misread as "size unknown" → the slider defaulted to 16 GB, and a subsequent "Resize disk only" or "Resize + Wipe Data" silently shrank the partition.
- `RootService.PatchAsync` now caps `rootAVD.sh` at 10 min and kills the process tree on timeout. Previously a deadlocked emulator could hang the WPF UI indefinitely.
- `AvdService.CreateAsync` caps `avdmanager create avd` at 5 min and kills it on timeout, with the same rationale.
- `ConfigService.ResizeDiskAsync` now logs each snapshot it destroys before wiping `snapshots/`. Previously the user only saw "Wiped qcow2 overlays" with no record of which named snapshots were lost.

### Removed

- Unused `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Abstractions` package references. `LogService` is custom and nothing in the project consumes `ILogger`.
- Unused `EmulatorService.ListAvdsAsync` — `AvdService.List()` already walks the AVD home directory for the same result.
- Dead `MigrationService.TransferOptions` record — never referenced.

## [0.1.0] — 2026-05-24

Initial release. End-to-end workflow for managing rooted Android emulators on Windows.

### Added

- Install / SDK detection panel with cmdline-tools auto-installer (downloads latest from `dl.google.com`).
- AVD management (list / launch / cold-boot / delete / create from any installed system image).
- Magisk root via [rootAVD](https://gitlab.com/newbit/rootAVD); auto-downloads the latest Magisk APK from GitHub releases and substitutes the bundled (older) version before patching. Backs up stock `ramdisk.img` automatically. Persists Magisk's shell-uid policy via `magisk --sqlite`.
- Phone-to-emulator migration: split-APK aware install on the target plus `tar` of `/data/data/<pkg>` (excluding caches) with UID remapping and SELinux context restore. Optional `/sdcard/Android/data/<pkg>` second pass.
- App management on the emulator (inventory, bulk uninstall, batch APK install, Google/Samsung debloat presets).
- AVD config editor: RAM, vCPU, screen, DPI, fastboot flags. Disk resize through `qemu-img` with optional partition wipe.
- Live device monitor: top-bar pills update as `adb devices` changes.
- Catppuccin Mocha dark theme. Rounded-rect surfaces only (no stadium/pill backdrops anywhere).
- Embedded log panel with colour-coded levels and clear button.

### Known limitations

- Internal app-data migration requires **both** sides to be rooted.
- Apps with strong device-attestation (banking, Bitwarden keystore-bound vaults) will still ask for re-login after data restore — that's a Magisk/Play-Integrity issue, not this tool's.
- `avdmanager create avd` runs synchronously and can take a minute on first system-image use.
