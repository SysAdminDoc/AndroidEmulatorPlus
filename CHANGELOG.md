# Changelog

All notable changes to this project will be documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version scheme is [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Pinning hook for rootAVD: `RootService.RootAvdPinnedRef` constant lets future releases pin the script to a verified SHA. When set, the clone uses full depth and `git checkout --detach` lands on the pin.
- Rolling daily log file at `%LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log`. Mirrors every UI log entry so a failed root/migrate can be post-mortemed even after the in-memory ring fills. Logs older than 14 days are pruned on startup.
- Diagnostics card on the Install / SDK panel surfaces the last 50 lines of `crash.log` with "Open logs folder" and "Clear crash.log" actions. Card hides itself when there is nothing to show.

### Fixed

- `ConfigViewModel.ParseSizeGb` now accepts raw-byte `disk.dataPartition.size` values (e.g. `8589934592`). Previously such AVDs were misread as "size unknown" → the slider defaulted to 16 GB, and a subsequent "Resize disk only" or "Resize + Wipe Data" silently shrank the partition.
- `RootService.PatchAsync` now caps `rootAVD.sh` at 10 min and kills the process tree on timeout. Previously a deadlocked emulator could hang the WPF UI indefinitely.
- `AvdService.CreateAsync` caps `avdmanager create avd` at 5 min and kills it on timeout, with the same rationale.
- `ConfigService.ResizeDiskAsync` now logs each snapshot it destroys before wiping `snapshots/`. Previously the user only saw "Wiped qcow2 overlays" with no record of which named snapshots were lost.

### Removed

- Unused `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Logging.Abstractions` package references. `LogService` is custom and nothing in the project consumes `ILogger`.
- Unused `EmulatorService.ListAvdsAsync` — `AvdService.List()` already walks the AVD home directory for the same result.

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
