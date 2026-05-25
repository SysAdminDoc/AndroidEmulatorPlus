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

- [ ] **P1 R-01** — System-image picker that can `sdkmanager` new images on demand (today: Create AVD only enumerates images already on disk).
- [ ] **P1 R-02** — First-launch wizard: install SDK → create AVD → root → migrate, all guided.
- [ ] **P1 R-06** — Snapshot manager — list / pick / load / delete saved emulator states (boot snapshots vs. user-named).
- [ ] **P1 R-08** — APK signature verification before install (warn on mismatch with already-installed package); use `apksigner.bat` from build-tools.
- [ ] **P1 R-09** — Bandwidth-aware adb push/pull with progress per-package, not just per-batch.
- [ ] **P1 A-01 cancel** — Cancel button on long-running ops (rootAVD patch, AVD create, cmdline-tools download, migration). Timeout shipped; user-visible Cancel did not.
- [x] **P1 A-14** — Top-bar Record toggle drives `adb shell screenrecord`; on stop pulls the mp4 to `~/Pictures/AndroidEmulatorPlus/` and opens Explorer.
- [x] **P1 A-15** — Dedicated Logcat tab (sidebar ⑦). Streams `adb logcat -v threadtime`, supports priority + package filter, Clear buffer (`logcat -c`), Clear view, Save to file.
- [ ] **P1 A-16** — Auto-accept SDK licenses. Pipe `y\n` repeatedly to `sdkmanager --licenses`.
- [x] **P1 A-18** — Apps tab now offers Include system + Include disabled toggles and a `user`/`system`/`disabled` tag per row.
- [ ] **P1 A-19** — Detect `allowBackup=false` per package and warn before attempting data migration.
- [x] **P1 A-25** — Rename AVD popup in the AVD card overflow menu (new reusable `PromptDialog` with regex+collision validation).
- [ ] **P1 A-36** — `.github/workflows/build.yml` that builds the project on `windows-latest` and uploads the published `.exe` as a release artifact.
- [ ] **P1 B-10** — Centralize `CancellationTokenSource` plumbing across long-running viewmodels (umbrella for A-01 cancel).
- [ ] **P1 B-11** — Multi-AVD `Process` tracking via `ConcurrentDictionary` (sub-task of A-33).

## Phase 3 — P2 polish

- [ ] **P2 R-03** — Magisk module manager view (Zygisk DenyList, Shamiko, LSPosed, PlayIntegrityFork).
- [ ] **P2 R-04** — OBB transfer pass during migration (opt-in toggle — game OBBs can be huge).
- [ ] **P2 R-05** — Per-app data export to ZIP + "Restore from ZIP" flow.
- [x] **P2 A-21** — Configure tab ships a ScreenPreset picker (Pixel 7/7 Pro/8/8 Pro/9 Pro/Tablet/Fold open & closed/Nexus 5X/Small phone/TV) that fills in Width/Height/DPI.
- [x] **P2 A-22** — Configure tab ships a `hw.gpu.mode` picker (host / swiftshader_indirect / angle_indirect / guest / off) with inline guidance.
- [x] **P2 A-23** — AVD overflow menu adds "Launch with options…": cold boot, wipe, headless, no-audio, http-proxy, dns-server, front/back camera.
- [ ] **P2 A-24 remediation** — Pass/fail shipped; remediation-link UI on accel-check failure (Windows Hypervisor Platform feature, Intel HAXM, AMD Hyper-V) is still TODO.
- [ ] **P2 A-26** — Duplicate AVD (copy `<name>.avd` directory + `<name>.ini`, rewrite `path=` references).
- [ ] **P2 A-27** — Keyboard shortcuts: F5 = Refresh active tab, Ctrl+1..6 = switch sections, Ctrl+L = clear log.
- [x] **P2 A-28** — Inline "Launch & root" CTA on the Root tab when no emulator is attached; launches selected AVD, waits for boot, then re-enters the root flow.
- [ ] **P2 A-29** — Detect tar flavor on the phone and fall back to a `find … -prune` pipeline for tar implementations without `--exclude=`.
- [ ] **P2 A-30** — `am force-stop` on the source phone before tar (with consent flag).
- [ ] **P2 A-33** — `EmulatorService` should track running children in `Dictionary<string AvdName, Process>` instead of a single `_current`.
- [ ] **P2 A-34** — App close should kill any emulator children launched in this session.
- [ ] **P2 A-35** — Add `AndroidEmulatorPlus.Tests` xunit project (`ParseIni/WriteIni` round-trip, `LatestMagiskAsync` filter, `ParseFailReason`, `ParseSizeGb`, etc).
- [ ] **P2 A-37** — Catppuccin Latte palette + theme switcher.
- [ ] **P2 B-04** — Versioned debloat preset JSON (embedded default + `%LOCALAPPDATA%\…\presets\bloat.json` override).
- [x] **P2 B-05** — Compute sizes button on Apps tab fills the size column from `du -sb /data/data/<pkg>` (root required; graceful warning otherwise).
- [ ] **P2 B-06** — `adb pair host:port` Wi-Fi pairing card on the Migrate tab.
- [ ] **P2 B-09** — Settings flyout (SDK / cache / screenshot paths, theme, network proxy).
- [x] **P2 B-13** — Apps tab gained a "Disable for user 0 (reversible)" uninstall mode (`pm uninstall --user 0`) + re-enable button (`cmd package install-existing`).

## Phase 4 — P3 / larger bets

- [ ] **P3 R-07** — Linux + macOS builds (Avalonia port).
- [ ] **P3 A-38** — Wear OS / Android Auto / Android TV AVD device profiles.
- [ ] **P3 A-39** — Integrated scrcpy launcher.
- [ ] **P3 A-40** — Sensor / GPS / battery / telephony simulation tab (via `telnet localhost 5554` console).
- [ ] **P3 A-41** — KernelSU alternative-root path.
- [ ] **P3 B-07** — Bidirectional clipboard sync (off by default).
- [ ] **P3 B-08** — Dry-run root preview via rootAVD `LISTONLY` mode.

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
