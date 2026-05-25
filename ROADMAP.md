# Roadmap

Single, prioritized work list. Items already in `CHANGELOG.md` as shipped are not repeated here. Tag legend:

- `R-NN` — original release-roadmap item (kept stable for reference)
- `A-NN` — addition from the 2026-05-24 deep-audit pass
- `B-NN` — addition from the 2026-05-25 pass-2 audit (see [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md) for full evidence + verification plans)
- Priorities: **P0** (correctness / data-loss risk), **P1** (high value), **P2** (nice to have), **P3** (future / large bet)

Build constraint: this VMware VM has no .NET SDK; changes here are best-effort and should be `dotnet build`-verified on the main desktop PC before tagging a release.

## Phase 1 — P0 hardening

- [x] **P0 B-01** — Fix `IsEnabled` mis-binding on busy buttons (`NotBoolConverter` introduced; InstallView/RootView updated).
- [x] **P0 B-02** — `.apks` / `.xapk` / `.apkm` bundles are now unzipped before install; inner splits go through `adb install-multiple` and any `*.obb` lands in `/sdcard/Android/obb/<pkg>/` (package resolved via manifest.json or `main.<ver>.<pkg>.obb` pattern).
- [x] **P0 A-04** — SHA-256 verification against embedded `Resources/known-hashes.json`. Hard-fail for known keys, trust-on-first-use log entry for unknown keys. Manifest ships with empty `magisk` table and one cmdlineTools URL placeholder; maintainer appends entries after smoke-test.
- [ ] **P0 A-03** — Replace the placeholder `"master"` ref in `RootService.RootAvdPinnedRef` with a verified rootAVD SHA after smoke-test on API 35 + API 36 Google Play AVDs. _Hook landed; the constant currently still resolves to `master` until a verified SHA is recorded._
- [x] **P0 A-05** — Cache card on the Migrate tab shows transfer + bundle + root cache sizes; Clear migration / Clear root cache buttons. Auto-refreshes after every migration.
- [x] **P0 A-07** — Resize+Wipe Data now opens a typed-confirm dialog (`WIPE`) listing every snapshot + overlay that's about to be destroyed.
- [x] **P0 A-08-CONFIRM** — Delete AVD overflow menu now shows a confirmation dialog with AVD folder + size before calling `avdmanager delete avd`. Un-Root also gained a confirmation showing the ramdisk path + backup path.

## Phase 2 — P1 workflow gaps

- [x] **P1 R-01** — "Browse online…" button on the AVDs tab opens a system-image picker dialog backed by `sdkmanager --list`. Auto-license-accept available; install runs via `sdkmanager <pkg>` with `y\n` spam; refreshes AvailableImages on success.
- [x] **P1 R-02** — `WelcomeDialog` shown on first launch when no settings.json / no SDK / no AVDs. Lists the four phases of the workflow with a Go-to button per step; "Don't show again" persists `hasSeenWizard=true`. App also auto-navigates to Install when the SDK isn't detected.
- [x] **P1 R-06** — Snapshots… overflow entry opens a dialog listing `&lt;avd&gt;.avd/snapshots/` with sizes and modified time. Save / Load go through `adb emu avd snapshot save|load` (requires the AVD to be running); Delete works any time and uses a typed confirmation.
- [x] **P1 R-08** — `ApkSignerService` runs `apksigner verify --print-certs` before each install (toggle on the Apps tab); bundle formats are unzipped first so the inner APK is inspected. Mismatched / unverified APKs fail-stop before adb install.
- [x] **P1 R-09 (partial)** — Per-package progress is now logged for APK / data / ext / OBB legs of the migration, plus the cache card on the Migrate tab reports byte totals after every run. Live byte-rate inside an adb push/pull is upstream-limited; bandwidth-aware streaming is deferred.
- [x] **P1 A-01 cancel** — Cancel button shipped on Root, Migrate, and Install (cmdline-tools download). Each viewmodel owns a `CancellationTokenSource` plumbed into the long-running service calls; `OperationCanceledException` reports cleanly.
- [x] **P1 A-14** — Top-bar Record toggle drives `adb shell screenrecord`; on stop pulls the mp4 to `~/Pictures/AndroidEmulatorPlus/` and opens Explorer.
- [x] **P1 A-15** — Dedicated Logcat tab (sidebar ⑦). Streams `adb logcat -v threadtime`, supports priority + package filter, Clear buffer (`logcat -c`), Clear view, Save to file.
- [x] **P1 A-16** — `SdkmanagerService.AcceptLicensesAsync` pipes `y\n` × 60 into `sdkmanager --licenses` with a 3-minute timeout; surfaced as a button on the Install tab.
- [x] **P1 A-18** — Apps tab now offers Include system + Include disabled toggles and a `user`/`system`/`disabled` tag per row.
- [ ] **P1 A-19** — Detect `allowBackup=false` per package and warn before attempting data migration.
- [x] **P1 A-25** — Rename AVD popup in the AVD card overflow menu (new reusable `PromptDialog` with regex+collision validation).
- [x] **P1 A-36** — `.github/workflows/build.yml`: dotnet restore/build/test on `windows-latest`; publishes framework-dependent + self-contained single-file artifacts; on tag push (`v*`) creates a GitHub Release with the self-contained ZIP attached.
- [x] **P1 B-10** — Cancel pattern landed on Root / Migrate / Install. Same CTS/CT plumbing is now the template for future long-running flows.
- [x] **P1 B-11** — `EmulatorService` uses `ConcurrentDictionary<string, Process>` keyed by AVD name; each child's `Exited` event prunes the entry; closing the app kills any orphans.

## Phase 3 — P2 polish

- [ ] **P2 R-03** — Magisk module manager view (Zygisk DenyList, Shamiko, LSPosed, PlayIntegrityFork).
- [x] **P2 R-04** — Opt-in "OBB (game data — can be huge)" checkbox on the Migrate tab; `MigrationService.TransferObbAsync` tars `/sdcard/Android/obb/<pkg>` and replays it on the emulator.
- [x] **P2 R-05** — Export Data… / Import from ZIP… buttons on the Apps tab. Export wraps `tar /data/data/<pkg>` + metadata.json into a ZIP per selection. Import re-maps the recorded UID to whatever the package's UID is on the target emulator, then `chown` + `restorecon`.
- [x] **P2 A-21** — Configure tab ships a ScreenPreset picker (Pixel 7/7 Pro/8/8 Pro/9 Pro/Tablet/Fold open & closed/Nexus 5X/Small phone/TV) that fills in Width/Height/DPI.
- [x] **P2 A-22** — Configure tab ships a `hw.gpu.mode` picker (host / swiftshader_indirect / angle_indirect / guest / off) with inline guidance.
- [x] **P2 A-23** — AVD overflow menu adds "Launch with options…": cold boot, wipe, headless, no-audio, http-proxy, dns-server, front/back camera.
- [x] **P2 A-24 remediation** — On accel-check failure the Install card now exposes three buttons: Windows Hypervisor Platform docs, "Turn Windows features on/off" (optionalfeatures.exe), Android Studio emulator-acceleration docs.
- [x] **P2 A-26** — Duplicate AVD entry on overflow menu: file-level copy of `<name>.avd/` + ini rewrite + transient-file cleanup so the duplicate boots clean.
- [x] **P2 A-27** — Keyboard shortcuts wired in MainWindow.InputBindings: F5 refreshes the active tab, Ctrl+1..7 switches sections (incl. Logcat), Ctrl+L clears the log panel, Ctrl+R takes a screenshot.
- [x] **P2 A-28** — Inline "Launch & root" CTA on the Root tab when no emulator is attached; launches selected AVD, waits for boot, then re-enters the root flow.
- [x] **P2 A-29** — `MigrationService` probes phone tar for `--exclude=` once per session and falls back to `find … -prune | tar -T -` when the flag isn't supported (older toybox / non-standard ROMs).
- [x] **P2 A-30** — "Force-stop on source phone before tar" checkbox on the Migrate tab; when set, `am force-stop <pkg>` runs on the phone immediately before tarring `/data/data`.
- [x] **P2 A-33** — `EmulatorService._children` ConcurrentDictionary tracks every AVD launched this session; `TryKill(name)` kills a single child.
- [x] **P2 A-34** — `App.OnExit` calls `EmulatorService.KillAll()` (alongside Logcat/ScreenRecord dispose) so closing the app doesn't orphan emulator processes.
- [x] **P2 A-35** — `AndroidEmulatorPlus.Tests` xunit project with ParseIni/WriteIni round-trip, ParseSizeGb (incl. raw-byte branch), MigrationService.ParseFailReason, AvdViewModel.SystemImageSortKey, and HashVerificationService.ComputeSha256 coverage. CI workflow already picks it up.
- [x] **P2 A-37** — Catppuccin Latte palette added; theme picker on the Install tab persists to `settings.json` and applies on next launch. Themes split into `Mocha.xaml` / `Latte.xaml` (palette only) + `Styles.xaml` (shared).
- [x] **P2 B-04** — Embedded `Resources/bloat-presets.json` ships Google/Samsung/Pixel/Xiaomi/OnePlus presets. User overrides at `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json` replace by id or append new ids. AppsView now renders buttons via `ItemsControl`.
- [x] **P2 B-05** — Compute sizes button on Apps tab fills the size column from `du -sb /data/data/<pkg>` (root required; graceful warning otherwise).
- [x] **P2 B-06** — Expander on the Migrate tab pairs over Wi-Fi (`adb pair host:port + code`) and connects (`adb connect host:port`); both forms tolerate older platform-tools that prompt for the code via stdin.
- [x] **P2 B-09** — ⚙ Settings button on the top-bar opens a dialog: theme, SDK root override (autodetect when empty), media dir for screenshots/recordings, HTTP proxy. SdkLocator honors SdkRootOverride; MainViewModel's screenshot/record helpers honor MediaDir. Telemetry-off statement included on the card.
- [x] **P2 B-13** — Apps tab gained a "Disable for user 0 (reversible)" uninstall mode (`pm uninstall --user 0`) + re-enable button (`cmd package install-existing`).

## Phase 4 — P3 / larger bets

- [ ] **P3 R-07** — Linux + macOS builds (Avalonia port).
- [x] **P3 A-38** — Wear OS (`wearos_*`), Android TV (`tv_*`), Android Automotive (`automotive_*`), and tablet (`pixel_tablet`, `pixel_c`) profiles added to the Create AVD device dropdown.
- [x] **P3 A-39** — Top-bar "🖥 scrcpy" button launches an external `scrcpy.exe` with `-s <serial>`; `ScrcpyService.FindExe` looks in PATH and `%LOCALAPPDATA%\AndroidEmulatorPlus\scrcpy\`.
- [x] **P3 A-40** — New "⑧ Console" tab drives the emulator console via `adb emu …`: GPS (geo fix), battery (power capacity/status), telephony (gsm call / sms send), network (speed/delay presets), free-form command, plus manual clipboard pull/push.
- [x] **P3 A-41** — Out-of-scope for the rootAVD flow; the Root tab now includes a KernelSU note with the 3-step manual procedure (custom kernel image swap + manager APK).
- [x] **P3 B-07** — Manual clipboard pull/push on the Console tab (no background poll, so no privacy cost). `cmd clipboard get-primary` and `set-primary` against the emulator.
- [x] **P3 B-08** — "Dry run (LISTONLY)" button on the Root tab runs rootAVD's `ListAllAVDs` entry point and logs the output without modifying any ramdisks.

## Quick wins (sub-30 min each)

- [ ] **B-12** — Delete dead `MigrationService.TransferOptions` record (or wire `ForceStop` to enable A-30).
- [ ] **B-14** — Collapse duplicate `### Fixed` blocks in CHANGELOG `Unreleased`.
- [ ] **B-15** — Add `[rootAVD pin: <ref>]` line to the LogService session header.
- [ ] **B-16** — Move `Detail` log lines from `OverlayBrush` to `SubtextBrush` (WCAG AA fail at current contrast).
- [ ] **B-17** — Empty-state TextBlock on AVDs tab when `Avds.Count == 0`.
- [ ] **B-18** — Surface "using fallback URL" in InstallView when the cmdline-tools scrape misses.
- [ ] **B-19** — Sort `AvailableImages` by parsed API level descending; default `NewImage` to the highest API.
- [ ] **B-20** — README Privacy / Network section (lists the 4 outbound domains the app contacts).

## Shipped (kept here for traceability)

All shipped items are tracked in [CHANGELOG.md](CHANGELOG.md). The `[x]` items below are
preserved for cross-referencing PRs/commits:

- [x] **R-10** — Optional desktop shortcut creation on AVDs tab.
- [x] **A-01** — `RootService.PatchAsync` timeout (10 min). _Cancel button → B-10._
- [x] **A-02** — `AvdService.CreateAsync` timeout (5 min).
- [x] **A-03 (partial)** — Pin hook landed in `RootService`. _SHA still pending → A-03 remains open._
- [x] **A-06** — `ConfigViewModel.ParseSizeGb` now accepts raw-byte values.
- [x] **A-07 (partial)** — Per-snapshot destruction logging. _Interactive confirm → A-07 remains open._
- [x] **A-08** — Stop button on running AVD cards.
- [x] **A-09** — Running indicator on AVD list (`getprop ro.kernel.qemu.avd_name`).
- [x] **A-10** — Crash log viewer on Install panel.
- [x] **A-11** — Rolling daily log file at `%LOCALAPPDATA%\…\logs\app-YYYYMMDD.log`.
- [x] **A-12** — APK drag-and-drop install on Apps tab.
- [x] **A-13** — Top-bar Screenshot button.
- [x] **A-17** — Cmdline-tools URL scrape from `developer.android.com/studio`.
- [x] **A-20** — "Show on disk" / overflow menu on AVD card.
- [x] **A-24 (partial)** — `emulator -accel-check` verdict on Install panel. _Remediation links → A-24 remediation remains open._
- [x] **A-31** — Removed unused `Microsoft.Extensions.Logging` package refs.
- [x] **A-32** — Removed unused `EmulatorService.ListAvdsAsync`.
