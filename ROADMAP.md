# Roadmap

Single, prioritized work list. Items already in `CHANGELOG.md` as shipped are not repeated here. Tag legend:

- `R-NN` — original release-roadmap item (kept stable for reference)
- `A-NN` — addition from the 2026-05-24 deep-audit pass
- Priorities: **P0** (correctness / data-loss risk), **P1** (high value), **P2** (nice to have), **P3** (future / large bet)

Build constraint: this VMware VM has no .NET SDK; changes here are best-effort and should be `dotnet build`-verified on the main desktop PC before tagging a release.

## Now / Next

- [ ] **P1 R-01** — System-image picker that can `sdkmanager` new images on demand (today: Create AVD only enumerates images already on disk; user can't bootstrap from a fresh SDK).
- [ ] **P1 R-02** — First-launch wizard: install SDK → create AVD → root → migrate, all guided.
- [ ] **P2 R-03** — Magisk module manager view (install Zygisk DenyList, Shamiko, LSPosed, PlayIntegrityFork from inside the tool).
- [ ] **P2 R-04** — OBB transfer pass during migration (opt-in toggle — game OBBs can be huge).
- [ ] **P2 R-05** — Per-app data export to ZIP for cold archival + "Restore from ZIP" flow.
- [ ] **P1 R-06** — Snapshot manager — list / pick / load / delete saved emulator states (boot snapshots vs. user-named).
- [ ] **P3 R-07** — Linux + macOS builds (Avalonia port).
- [ ] **P1 R-08** — APK signature verification before install (warn on mismatch with already-installed package); use `apksigner.bat` from build-tools.
- [ ] **P1 R-09** — Bandwidth-aware adb push/pull with progress per-package, not just per-batch.
- [x] **P2 R-10** — Optional desktop shortcut creation right from the AVDs tab.

## Audit additions (2026-05-24)

### Reliability / correctness (P0)

- [x] **P0 A-01** — `RootService.PatchAsync` has no timeout — if `rootAVD.sh` hangs (commonly seen when emulator deadlocks during ramdisk swap), the WPF UI is stuck until the user kills the process tree by hand. Wrap with a `CancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(10))` and surface a Cancel button while busy. _Cancel button still TODO; timeout shipped._
- [x] **P0 A-02** — `AvdService.CreateAsync` writes "no" to `avdmanager.bat` stdin then waits forever; no timeout. Same fix.
- [x] **P0 A-03** — Pin rootAVD to a known-good revision in `RootService.EnsureRootAvdAsync` (currently `git clone --depth 1` of `master`). When newbit pushes a breaking commit, the entire root flow breaks silently. Store the pinned SHA in a constant and `git checkout <sha>` after clone. _Hook landed; the constant currently still resolves to `master` until a verified SHA is recorded._
- [ ] **P0 A-04** — Verify SHA-256 of downloaded Magisk APK and the cmdline-tools ZIP against expected values before use. Today the app trusts whatever GitHub releases / dl.google.com returns. Supply-chain risk on a tool that patches a ramdisk.
- [ ] **P0 A-05** — `MigrationService` does `try { Directory.Delete(work, true); } catch { }` and `try { File.Delete(local); } catch { }` in `finally`. On a cancellation or crash mid-flow, the transfer cache at `%LOCALAPPDATA%\AndroidEmulatorPlus\transfer\` can grow to many GB. Add an "orphaned cache size" indicator in the UI plus a "Clear cache" button.
- [x] **P0 A-06** — `ConfigViewModel.ParseSizeGb` only handles `*G` and `*M`. Many Android AVDs persist `disk.dataPartition.size` as raw bytes (e.g. `8589934592`). Today the slider snaps back to the default 16 GB and "Save config" then writes that incorrect value, shrinking the partition. Parse plain integer bytes too.
- [x] **P0 A-07** — `ConfigService.ResizeDiskAsync` deletes `snapshots/` recursively without confirmation. Add a confirmation dialog or at minimum a verbose log line listing every snapshot about to be destroyed (snapshot names are user-meaningful). _Logging shipped; an interactive confirmation dialog is still TODO._

### Workflow gaps (P1)

- [x] **P1 A-08** — No "Stop emulator" UI command. `AdbService.EmuKillAsync` is wired but no view binds to it. Add a Stop button on each AVD card that is enabled when that AVD is the currently-attached emulator.
- [x] **P1 A-09** — No way to tell from the AVD list which AVD is currently running. Cross-reference `DeviceMonitor.Current` with `Avd.Name` (the emulator serial is `emulator-NNNN` not the AVD name — fetch `getprop ro.boot.qemu.avd_name` once per emulator and cache).
- [x] **P1 A-10** — Crash log viewer. `crash.log` accumulates at `%LOCALAPPDATA%\AndroidEmulatorPlus\crash.log` and the user never sees it unless they open Explorer. Add an Install / SDK panel section that surfaces last-N crashes with a "Clear" button.
- [x] **P1 A-11** — Save log to file. The in-memory log buffer is 2000 entries and lost on close. Write a rolling `app.log` mirror to `%LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log`.
- [x] **P1 A-12** — APK drag-and-drop install. Drop one or many APKs anywhere on the Apps view to invoke the batch install path.
- [x] **P1 A-13** — Screenshot button (`adb exec-out screencap -p > <ts>.png`), opens the resulting file in the default viewer. Available from a top-bar button when an emulator is attached. _Implemented as a shell-screencap + pull + cleanup dance because the existing ProcessRunner pipes through UTF-8 StringBuilders; using `exec-out` would require a binary-safe runner._
- [ ] **P1 A-14** — Screen record button (`adb shell screenrecord /sdcard/<ts>.mp4` + pull on stop) with start/stop toggle.
- [ ] **P1 A-15** — Logcat viewer (filter by package + level), with a "Clear" and "Save" button. Useful when migration appears to fail and the cause is in app-side logcat.
- [ ] **P1 A-16** — Auto-accept SDK licenses. When running `sdkmanager --licenses` during the first-launch wizard or system-image install, pipe `y` repeatedly (Google's recommended automation pattern).
- [ ] **P1 A-17** — Cmdline-tools URL is hard-coded as `commandlinetools-win-13114758_latest.zip`. Resolve the latest stable from `https://developer.android.com/studio#command-line-tools-only` (HTML scrape) or maintain a manifest in-repo and refresh on a CI cron.
- [ ] **P1 A-18** — `pm list packages -3` (user-only) excludes pre-installed user apps that the OEM marked as system but the user still cares about (Samsung Wallet, OEM stuff). Add a `-s` (system) toggle and a `-d` (disabled-only) toggle and show package source (system vs. user) per row.
- [ ] **P1 A-19** — Detect `allowBackup=false` per package and warn before attempting data migration — those apps will reject the restored data even if tar succeeds.
- [x] **P1 A-20** — Add an "Open AVD folder" / "Show on disk" action per AVD card (Android Studio Device Manager parity).

### UX polish (P2)

- [ ] **P2 A-21** — Screen W / H / DPI in Configure tab are free-form TextBoxes. Replace with a preset picker (Pixel 7: 1080×2400@420, Pixel 7 Pro: 1440×3120@560, Pixel Tablet: 2560×1600@320, Pixel Fold open/closed, Custom…).
- [ ] **P2 A-22** — Expose GPU mode picker in Configure tab (`hw.gpu.mode`: host | swiftshader_indirect | angle_indirect). Default is `host`; users on remote desktop or VM often need `swiftshader_indirect`.
- [ ] **P2 A-23** — Expose `-http-proxy`, `-dns-server`, `-no-window` (headless), `-noaudio`, `-camera-front/back` as launch flags on the AVD card's "Launch…" overflow menu.
- [ ] **P2 A-24** — Run `emulator -accel-check` on the Install panel and show pass/fail with remediation links (Windows Hypervisor Platform feature, Intel HAXM, AMD Hyper-V).
- [ ] **P2 A-25** — Rename AVD (`avdmanager move avd -n <old> -r <new>`). _Backend (`AvdService.RenameAsync` + `AvdViewModel.RenameCommand`) ready; needs an inline rename popup on the AVD card._
- [ ] **P2 A-26** — Duplicate AVD (copy `<name>.avd` directory + `<name>.ini`, rewrite `path=` references). Matches Android Studio Device Manager.
- [ ] **P2 A-27** — Keyboard shortcuts: F5 = Refresh active tab, Ctrl+1..6 = switch sections, Ctrl+L = clear log.
- [ ] **P2 A-28** — Microcopy: in `RootViewModel.RootAsync`, when "Launch the AVD first" warning fires, the message dead-ends. Offer an inline `Launch <name>` button that immediately calls `EmulatorService.Launch` and re-enters the root flow once boot completes.
- [ ] **P2 A-29** — `MigrationService.TransferInternalDataAsync` calls `tar --exclude=` with three excludes. Some Android tar implementations don't support `--exclude=`. Detect tar flavor (`tar --version` returns "toybox" on modern Android) and fall back to a `find … -prune` pipeline.
- [ ] **P2 A-30** — `am force-stop` is silently invoked on the emulator before tar extract, but not on the source phone. If the user has the app open on the phone the tar may capture a torn DB. Force-stop on the phone too (with consent in a settings flag).

### Architecture / hygiene (P2)

- [x] **P2 A-31** — Remove unused `Microsoft.Extensions.Logging` + `Microsoft.Extensions.Logging.Abstractions` package references — `LogService` is custom and nothing in the project actually uses ILogger.
- [x] **P2 A-32** — `EmulatorService.ListAvdsAsync` is wired but never called — `AvdService.List()` already walks `.ini` files for the same result. Delete the unused method or repurpose it as a sanity check.
- [ ] **P2 A-33** — `EmulatorService._current` only tracks the most recent launch — multiple AVDs running concurrently are not tracked, and Stop only kills the latest. Switch to `Dictionary<string AvdName, Process>`.
- [ ] **P2 A-34** — App close should attempt to gracefully kill any emulator children launched in this session (today they survive as orphans because `_current` may not even be the right one).
- [ ] **P2 A-35** — Add a basic unit-test project (`AndroidEmulatorPlus.Tests`) — at minimum cover `AvdService.ParseIni`/`WriteIni` round-trip, `DownloadService.LatestMagiskAsync` asset-name filter, and `MigrationService.ParseFailReason`.
- [ ] **P2 A-36** — Add a `.github/workflows/build.yml` that builds the project on `windows-latest` and uploads the published `.exe` as a release artifact. Today there is no CI.
- [ ] **P2 A-37** — `Dark.xaml` defines `Catppuccin Mocha` only. Add `Latte` (light) palette + theme switcher in a Settings flyout. Many corporate users need to use the app on screen-share with light theme.

### Cross-cutting (P3 / larger bets)

- [ ] **P3 A-38** — Wear OS / Android Auto / Android TV AVD profiles in the Create AVD dropdown — `pixel_watch`, `wear_round`, `android_tv_1080p`, `automotive_1024p`.
- [ ] **P3 A-39** — Integrated scrcpy launcher (when scrcpy.exe is on PATH or bundled) for non-Android-Studio-style mirroring of the attached phone or emulator with clipboard sync and drag-drop install.
- [ ] **P3 A-40** — Sensor / GPS / battery / telephony simulation panel — emulator console accepts `geo fix`, `power capacity`, `gsm call`, `sms send` via `telnet localhost 5554`. Surface these in a dedicated tab.
- [ ] **P3 A-41** — Alternative-root path: KernelSU APK installer + custom-kernel boot (for AVDs that ship with a KernelSU-patched system image).
