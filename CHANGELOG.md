# Changelog

All notable changes to this project will be documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the version scheme is [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Premium UI polish pass: explicit empty/filter-empty states for Apps, Migrate,
  and Logcat; compact reusable button styling; shared field-label and microcopy
  styles; and count-based visibility converters for polished blank-list states.
- Focused hardening tests for shell quoting/package validation, AVD/snapshot name
  validation, Magisk module zip validation, and app-data tar import validation.
- Repository-level `global.json` pins builds to .NET 9 with feature-band
  roll-forward, matching the `net9.0-windows` target and CI setup.
- Quote-aware free-form Console command parsing. `adb emu` commands such as
  `sms send 5551234 "Hello, world"` now preserve the SMS body as one argument;
  malformed quotes produce a warning instead of a bad command. Covered by
  `ConsoleCommandParserTests`.
- `BundleInstallerService` (C-19) extracts the bundle-install + apksigner verify
  pipeline out of `AppsViewModel`. Slimmer view-model; service stays UI-agnostic
  via an `onSignerMismatch` callback.
- Live theme switching (C-12). All 216 `StaticResource …Brush` references swept
  to `DynamicResource`; new `ThemeService.Apply(theme)` swaps the palette
  dictionary in place. SettingsDialog applies the change immediately — no
  restart required.
- Magisk module manager (C-07 / R-03). Curated catalog (Shamiko / LSPosed /
  PlayIntegrityFork / Tricky Store / Zygisk Detach) backed by
  `Resources/magisk-modules.json`. Lists installed modules via
  `magisk module list` (fallback: walks `/data/adb/modules/`). Install /
  Toggle / Remove (mark-on-reboot) flows. Surfaced via `Modules…` button on
  the Root tab.
- "Auto-launch scrcpy after AVD boots" toggle in Settings (C-16). New
  `Settings.AutoScrcpy` flag; `MainViewModel.OnDevicesChanged` fires scrcpy
  when a new emulator serial comes online.
- `allowBackup=false` pre-flight (C-05). `MigrationService.AllowsBackupAsync`
  probes `pm dump` for each package after the phone list loads; flagged rows
  show a yellow ⚠ no-backup pill. The internal-data leg is skipped for them
  unless "Force-migrate no-backup apps" is set.
- Signer-mismatch warning (C-04). When apksigner verifies an APK and aapt2
  resolves the package id, the cert SHA is compared against the installed
  package's cert (via `pm dump`). Mismatch raises a ConfirmDialog listing both
  SHAs.
- HTTP proxy is now honored (C-03). `DownloadService` reads
  `SettingsService.Current.HttpProxy` at ctor time and wires it into
  `HttpClientHandler.Proxy` with `UseDefaultCredentials = true`. The Settings
  field was persisted but ignored before.
- "Show welcome wizard…" button on the Settings dialog (C-10). Flips
  `HasSeenWizard=false` and re-opens the wizard.
- Welcome wizard hides completed step cards by default (C-14); a "Show completed
  steps" toggle reveals them.
- Application icon at `Assets/aep.ico` (C-06). Multi-resolution
  (16/24/32/48/64/128/256 px) Catppuccin-themed Android-robot motif. Wired via
  `<ApplicationIcon>` in csproj + `Icon=` on MainWindow.
- New tests (C-08): `OrderBaseFirstTests` (5 cases), `DuplicateAvdTests`
  (.avd tree copy + ini rewrite + transient cleanup), `PresetServiceTests`
  (id-based merge + embedded JSON schema), `AllowBackupParsingTests`
  (4 known `pm dump` shapes).
- `ProcessRunner.RunWithStdinAsync` and `StreamAsync` helpers (C-09).
  `SdkmanagerService.AcceptLicenses`/`Install`, `AvdService.CreateAsync`,
  `RootService.PatchAsync`/`DryRunAsync`, `AdbService.PairAsync`, and
  `ScrcpyService.Launch` now route through them. The two remaining
  `Process.Start` sites (`LogcatService`, `ScreenRecordService`) legitimately
  hold the Process for explicit Stop control.

### Changed

- Main-window chrome is calmer and more spacious: concise SDK status with the
  full SDK path moved to a tooltip, professional text-only top-bar actions,
  cleaner sidebar labels, and a renamed "Activity log" panel.
- Shared WPF styling now has stronger keyboard focus rings, clearer text
  rendering, more consistent card padding, refined button hover/pressed states,
  a real danger-button interaction state, and consistent status-pill borders.
- Dense command rows in Root, Install, and Logcat wrap cleanly at narrower
  widths instead of crowding or clipping.
- User-facing status text was de-noised by replacing mixed emoji/symbol prefixes
  with calm text labels across the toolbar, AVD cards, onboarding, Root,
  Migrate, Logcat, Console, and secondary dialogs.
- `SdkLocator` now autodetects the app-managed Android SDK cache at
  `%USERPROFILE%\.cache\android-sdk`.
- `ProcessRunner` now waits for async stdout/stderr handlers to drain after the
  child exits, preventing truncated tail output from `adb`, `apksigner`,
  `sdkmanager`, and rootAVD commands.
- Settings loaded from `settings.json` are normalized defensively: invalid theme
  names fall back to Mocha, blank paths collapse to null, and proxies must be
  absolute `http://` or `https://` URLs. The Settings dialog now blocks invalid
  proxy input before saving.
- AVD, snapshot, package, and Magisk module identifiers are validated in service
  code, not just in UI prompts. Config values written to `config.ini` are clamped
  to practical ranges, and disk resize rejects malformed size strings.
- Apps and Migrate refreshes now cancel/stale-guard overlapping list refreshes;
  filtered list bindings are notified after refresh so active filters no longer
  show stale rows.
- `ProcessRunner` now throws `TimeoutException` for internal timeouts while
  preserving `OperationCanceledException` for user cancellation. SDK manager,
  AVD creation, rootAVD, and adb pairing log timeout vs cancellation separately.
- Logcat view updates are batched on a 100 ms dispatcher timer instead of
  dispatching every incoming line individually.
- v0.2.0 → 0.2.0 version pinned across csproj (`<Version>` / `<FileVersion>` /
  `<InformationalVersion>`), `MainWindow.xaml` Title + sidebar pill,
  `MainViewModel` startup log line, README badge.
- `CacheDiagnosticsService.Changed` event fires after Clear* and Apps tab
  Export/Import; `MigrateViewModel` subscribes (C-11) so the cache card
  auto-recalculates when other tabs write to `transfer/`.
- `AppService.OrderBaseFirst` (C-02) orders inner APKs base-first for
  `adb install-multiple`. Replaces the ascending-size sort that put the
  base APK last.
- Theme picker removed from the Install tab (C-15); Settings is the canonical
  home now.
- Apps tab "Compute sizes" iterates the full `Apps` collection, not just
  `FilteredApps` (C-13).

### Fixed

- Signature verification for `.apks` / `.xapk` / `.apkm` bundles now inspects
  every extracted split APK and rejects bundles with a bad or mismatched split
  certificate. Previously only the base APK was checked.
- Magisk module catalog installs now resolve GitHub `releases/latest` URLs to a
  real `.zip` release asset before downloading. Previously those catalog entries
  could download HTML and hand it to `magisk --install-module`.
- Downloads are written to a `.download` sibling and atomically moved into place,
  so cancelled or failed downloads no longer leave a corrupt destination file.
- Migration refreshes and background `allowBackup` probes no longer mutate the
  package list while a transfer is running. Each package also gets a just-in-time
  `allowBackup` check before internal data migration.
- Screen recording stop now clears its internal state and attempts remote cleanup
  even when the local media output directory is unavailable.
- `AppsView.xaml` and `RootView.xaml` now declare the shared converter namespace
  at the root, fixing XAML compile failures in the Apps and Root views.
- `AvdService.WriteIni` treats incoming update keys case-insensitively, matching
  the parser and avoiding duplicate INI keys such as `hw.ramSize` /
  `HW.RAMSIZE`.
- `ConfirmDialog` gives the typed-confirm input an automation name for screen
  readers, and the Migrate force-stop tooltip now states that the target
  emulator app is also stopped before restore.
- `SettingsService.Save` writes atomically (C-18): `settings.json.tmp` then
  `File.Replace` into place. A crash mid-write can no longer corrupt the file
  App.OnStartup reads before any view binds.
- Welcome wizard footer: corrected an inner StackPanel that was setting
  `Grid.Column="1"` against a non-Grid parent — replaced with a proper
  2-column Grid so the "Don't show again / Close" buttons right-align as
  intended.

### Security (supply chain)

- ADB shell commands that incorporate package names, module IDs, clipboard text,
  or remote paths now use a shared shell-quoting helper plus identifier
  validation where appropriate.
- App-data import and phone-to-emulator migration now validate tar archives
  locally before root-side extraction, rejecting absolute paths, `..` traversal,
  entries outside the declared package, symlinks, hardlinks, and device/FIFO
  entries.
- Magisk module zips are validated before pushing to the emulator: archive paths
  must be relative/non-traversing and `module.prop` must exist at archive root.
- Magisk catalog entries are filtered to safe module IDs and absolute HTTPS
  download URLs before being shown or installed.
- `Resources/known-hashes.json` populated with computed SHA-256 for
  `Magisk-v30.7.apk` (cross-checked against GitHub Releases API per-asset
  `digest` field) and the current `commandlinetools-win-14742923_latest.zip`.
- `RootService.RootAvdPinnedRef` pinned to
  `613caa44371f85e1a461bc030e07ddc2d71afe32` (newbit/rootAVD HEAD at
  2026-05-25). `ListAllAVDs` entry-point verified in the pinned revision.
- `RootService.DownloadLatestMagiskAsync` now cross-checks the downloaded
  Magisk APK against GitHub's per-asset `digest` field (defense-in-depth
  tier 1) before consulting the in-tree manifest (tier 2). New Magisk
  releases get automatic SHA-256 verification even before the manifest is
  updated.
- New `KnownHashesManifestTests` guards the manifest schema and asserts the
  v30.7 hash + the 14742923 cmdline-tools hash stay populated.

### Docs

- Consolidated planning docs: active work remains in `ROADMAP.md`, shipped
  roadmap history is summarized in `COMPLETED.md`, and research evidence is
  summarized in `RESEARCH_REPORT.md` with the previous feature plan archived
  under `docs/archive/research/`.
- README rewritten for v0.2.0 (C-17 partial). Now reflects the 8-tab surface
  (Logcat ⑦ + Console ⑧ added), the supply-chain hardening tiers, keyboard
  shortcuts, the persistence-paths table, and the live theme swap. Includes
  an ASCII layout sketch since binary screenshots require running the app on
  a desktop with the .NET 9 SDK.

## [0.2.0] — 2026-05-25

Major iteration on the v0.1.0 baseline. ~5000 lines, 8 tabs (added Logcat ⑦ and
Console ⑧), 23 services, 9 view-models. Cancel buttons, multi-AVD process
tracking, SHA-256 supply-chain verification, snapshot manager, Wi-Fi pairing,
APK signature verification, OBB transfer, per-app data export/import, Catppuccin
Latte theme, settings persistence, first-launch wizard, sensor/GPS/telephony
console, scrcpy launcher, and a GitHub Actions release pipeline. Full surface
listed below.

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
- New ⚙ Settings button on the top bar opens a dialog covering theme, SDK root override (autodetect when blank), media output dir (defaults to `~/Pictures/AndroidEmulatorPlus`), and HTTP proxy. SdkLocator honors the override; screenshot/record helpers write to the configured media dir. The card includes an explicit "no telemetry" statement.
- First-launch wizard (R-02): a Welcome dialog opens once when no settings.json/no SDK/no AVDs are present, walking through the four workflow steps with Go-to buttons per step. "Don't show again" persists `hasSeenWizard` in settings.json. The app also auto-navigates to Install on startup if the SDK isn't detected.
- New ⑧ Console tab simulates GPS (geo fix), battery (capacity + status), telephony (gsm call, sms send), and network conditions (speed + delay presets) via `adb emu …`. Free-form command field and manual clipboard pull/push (`cmd clipboard get-primary` / `set-primary`).
- Top-bar "🖥 scrcpy" button launches external `scrcpy.exe` against the attached emulator. Looks in PATH and `%LOCALAPPDATA%\AndroidEmulatorPlus\scrcpy\`. winget install Genymobile.scrcpy.
- Root tab "Dry run (LISTONLY)" button (B-08) runs rootAVD's `ListAllAVDs` entry point — enumerates what would be patched without modifying any ramdisks.
- Root tab footer mentions KernelSU as an alternative root path with the 3-step manual procedure; full integration is out of scope for this rootAVD-based flow.

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
