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
- Downloaded Magisk APK and cmdline-tools ZIP are now SHA-256 verified against an embedded `Resources/known-hashes.json` manifest. Known-key mismatches hard-fail and delete the partial file. Unknown keys log the computed hash so future manifest updates have a record (trust-on-first-use).
- Cache card on the Migrate tab reports total cache usage (migration tarballs + bundle extracts + rootAVD/Magisk clone). Two clear actions: "Clear migration cache" (safe, recoverable) and "Clear root cache" (forces re-clone of rootAVD next root). Auto-recalculates after every migration.
- Reusable `ConfirmDialog` for destructive actions. Resize+Wipe Data now requires typing `WIPE` and shows every snapshot + qcow2 overlay about to be destroyed. Delete AVD shows the on-disk folder + size; Un-Root shows the ramdisk path and the backup path it will restore from.
- Rename AVD action lands in the AVD card's overflow menu via a new reusable `PromptDialog` (regex-validated, blocks name collisions, refuses to rename a running AVD).
- Root tab now shows an inline "Launch & root" card when an AVD is selected but no emulator is attached. The button launches the AVD, polls adb for up to 2 minutes for the device, waits for `sys.boot_completed`, and re-enters the root flow — no more dead-end "Launch the AVD first" warning.
- New Logcat tab (sidebar ⑦) streams `adb logcat -v threadtime` from the attached emulator with priority + package filter. Buttons: Start / Stop, Clear buffer (`logcat -c`), Clear view, Save to .log file. Auto-scrolls and caps the in-view ring at 5000 lines.
- New top-bar "🎥 Record" toggle drives `adb shell screenrecord`. Stops, pulls the mp4 to `%USERPROFILE%\Pictures\AndroidEmulatorPlus\` and opens the folder. (Android caps `screenrecord` at 3 minutes; this is intentional and surfaced in the tooltip.)
- Apps tab now offers Include system / Include disabled toggles. Each row shows a `user` / `system` / `disabled` tag derived from `pm list packages -3/-s/-d`.
- Apps tab "Compute sizes" button populates per-app data sizes from `du -sb /data/data/<pkg>` (root required; the tab warns and skips when no root is available).
- New uninstall mode toggle on the Apps tab: choose between "Uninstall (adb)" and "Disable for user 0 (reversible)" — the second uses `pm uninstall --user 0`, which can remove preinstalled OEM apps that plain `adb uninstall` refuses. A companion "Re-enable selected (user 0)" button uses `cmd package install-existing` to restore them.
- Configure tab gained a screen preset picker (Pixel 7/7 Pro/8/8 Pro/9 Pro/Tablet/Fold open & closed/Nexus 5X/Small phone/1080p TV) and a GPU mode picker (`hw.gpu.mode`: host / swiftshader_indirect / angle_indirect / guest / off) with inline guidance for which mode to pick when running inside a VM or over RDP.
- AVD card overflow now includes "Launch with options…" — a dialog that exposes cold boot, wipe data, headless (`-no-window`), `-no-audio`, `-http-proxy`, `-dns-server`, and front/back camera selection. All emit standard emulator flags through `EmulatorService.LaunchOptions`.
- "Accept all SDK licenses" button on the Install tab pipes `y\n` × 60 into `sdkmanager --licenses` so subsequent `sdkmanager install …` runs unattended (capped at 3 minutes).
- Accel-check card surfaces a remediation panel on failure: Windows Hypervisor Platform docs, "Turn Windows features on/off" (launches optionalfeatures.exe), Android emulator-acceleration docs.
- "Duplicate…" entry on the AVD card overflow menu: file-level copy of `<name>.avd/` and the matching `.ini`, with transient files (hardware-qemu.ini, multiinstance.lock, running.lock) removed so the clone boots cleanly. Validates the new name (regex + collision check) via PromptDialog and refuses when the source AVD is currently running.
- Device profile dropdown gained Wear OS (`wearos_small_round`, `wearos_large_round`, `wearos_square`, `wearos_rect`), Android TV (`tv_720p`, `tv_1080p`, `tv_4k`), Android Automotive (`automotive_1024p_landscape`, `automotive_distant_display`), and `pixel_tablet`/`pixel_c` tablet profiles.
- Keyboard shortcuts: **F5** refreshes the active tab, **Ctrl+1..7** switches sections (Install/AVDs/Root/Migrate/Apps/Config/Logcat), **Ctrl+L** clears the log panel, **Ctrl+R** captures a screenshot.
- GitHub Actions workflow (`.github/workflows/build.yml`): dotnet restore / build / test on `windows-latest`, uploads framework-dependent and self-contained single-file artifacts; on `v*` tag, attaches the self-contained ZIP to a generated Release.
- Catppuccin Latte (light) theme added alongside the existing Mocha (dark); theme picker on the Install tab persists to `%LOCALAPPDATA%\AndroidEmulatorPlus\settings.json` and applies on next launch. Themes split into `Themes/Mocha.xaml` and `Themes/Latte.xaml` (palette only) plus `Themes/Styles.xaml` (shared control styles).
- New `SettingsService` reads/writes `settings.json` (theme; placeholder fields for SDK root override, media dir, HTTP proxy reserved for the upcoming Settings flyout).
- Cancel button shipped on Root, Migrate, and Install (cmdline-tools download). Each long-running flow owns a `CancellationTokenSource` plumbed into the underlying service calls; cancelled flows log a warning and roll back partial state.
- `EmulatorService` now tracks every emulator child in a ConcurrentDictionary keyed by AVD name. Closing the app calls `KillAll()` so orphaned emulator windows no longer outlive the parent process.
- Debloat presets moved into `Resources/bloat-presets.json` (embedded). Ships Google / Samsung / Pixel-extras / Xiaomi-MIUI / OnePlus-OxygenOS presets. Users can override or extend by dropping `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json` with the same schema — same `id` replaces, new ids append. The Apps tab now renders one button per preset via `ItemsControl`.
- Wi-Fi pairing card on the Migrate tab (Expander, collapsed by default). Calls `adb pair host:port + code` and `adb connect host:port`; works around older platform-tools that prompt for the code via stdin. Once connected, the phone shows up in the device monitor and the migration flow works exactly like a USB phone.
- New `AndroidEmulatorPlus.Tests` xunit project: `ParseIni`/`WriteIni` round-trip (incl. case-insensitive overwrites), `ConfigViewModel.ParseSizeGb` (incl. raw-byte branch fixed in 3c7b738), `MigrationService.ParseFailReason`, `AvdViewModel.SystemImageSortKey`, `HashVerificationService.ComputeSha256`. CI already picks it up when present.
- "Browse online…" button on the AVDs tab opens a new `SystemImagePickerDialog`: filterable list from `sdkmanager --list`, Accept-all-licenses helper, install via `sdkmanager <pkg>` with auto-`y` spam. On success the just-installed image lands selected in the Create form.
- AVD card overflow now includes "Snapshots…" — dialog lists `<avd>.avd/snapshots/` with sizes + modified time, Save / Load (require the AVD to be running, go through `adb emu avd snapshot save|load`), Delete (works any time, confirmation dialog).
- APK signature verification with `apksigner.bat`: the Apps tab gained a "Verify signatures" toggle (default on); each install runs `apksigner verify --print-certs` first and fail-stops mismatches. `.apks` / `.xapk` / `.apkm` bundles are unzipped to inspect the inner APK.
- Migrate tab now offers an opt-in "OBB (game data — can be huge)" checkbox that tars `/sdcard/Android/obb/<pkg>` from the phone and replays it on the emulator.
- Apps tab "Export data…" / "Import from ZIP…" buttons (R-05) round-trip `/data/data/<pkg>` between machines. Export writes a ZIP per selected app with the tar + a metadata.json (package, original UID, timestamp); Import re-maps to the emulator's current UID via `chown -R` + `restorecon -R`.
- Migration phone-side tar now probes `tar --help` once per session and falls back to a `find … -prune | tar -T -` pipeline for tar builds that don't support `--exclude=` (older toybox, non-standard ROMs).
- Migrate tab "Force-stop on source phone before tar" checkbox closes the running app on the phone immediately before tarring `/data/data`, so the SQLite DBs aren't captured mid-write.

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
