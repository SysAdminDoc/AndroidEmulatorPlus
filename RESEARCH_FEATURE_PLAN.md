# Project Research and Feature Plan — Pass 3 (2026-05-25, post-batch-25)

Supersedes the pass-2 plan (in git history at commit `b0525db`). Pass 3 audits the
much larger codebase that resulted from the autonomous implementation loop in commits
`5676326..8502f22`; most of pass-2's `B-NN` items have shipped. New items in this pass
use the `C-NN` tag prefix.

## Executive Summary

AndroidEmulatorPlus is now a 5000-line, 8-tab Windows WPF emulator manager whose
v0.2.0-track surface area is competitive with Android Studio's Device Manager + scrcpy
launcher + Magisk rootAVD wrapper combined. Of the original 41 audit items (R/A/B
tags), **36 are checked**, the remaining 5 are either pending external validation
(A-03 rootAVD SHA, R-03 Magisk modules, R-07 Avalonia) or partial (R-09
bandwidth-aware progress). What's now most valuable is **shipping a v0.2.0
release** — none of this work is reachable to a user yet because the version pin,
release tag, manifest population, and signed binary are all outstanding.

Top opportunities, in order:

1. **C-01** Cut a v0.2.0 release — version bump across the 5 places CLAUDE.md mandates, populate `Resources/known-hashes.json` with verified Magisk + cmdline-tools hashes, lock the rootAVD SHA, then `git tag v0.2.0` to trigger the existing CI release workflow.
2. **C-02** Audit + fix the bundle-install ordering bug: `AppService.ExtractBundle` orders inner APKs by ascending size, but the base APK is typically *larger* than its config splits — install-multiple may put a split first and fail signature checks on some devices.
3. **C-03** Wire `SettingsService.HttpProxy` into `DownloadService`'s `HttpClient` — the field is persisted and surfaced in the UI but never honored.
4. **C-04** Wire `ApkSignerService.InstalledCertShaAsync` into the verify-before-install path so users actually get the "this is a different signer than what's installed" warning the feature was sold as.
5. **C-05** A-19 `allowBackup=false` pre-flight in the Migrate pipeline (only P1 item never shipped).
6. **C-06** Add an "App icon" to the WPF window + installer manifest — currently uses the .NET default icon and SmartScreen warns on first run.
7. **C-07** R-03 Magisk module manager — install Zygisk modules (Shamiko, LSPosed, PlayIntegrityFork) from inside the tool against the rooted emulator.
8. **C-08** Tests project coverage gaps: `AvdService.Duplicate` (file copy + ini rewrite — highest data-loss risk in the recently-added code is not tested), `AppService.ExtractBundle`, `ConfigService.PreviewWipe`, `PresetService` merge.
9. **C-09** Lift `Process.Start` re-implementations in `RootService.PatchAsync`, `RootService.DryRunAsync`, `AvdService.CreateAsync`, `SdkmanagerService.AcceptLicensesAsync`, `SdkmanagerService.InstallAsync` into a `ProcessRunner.RunWithStdinAsync` helper. CLAUDE.md says `Helpers/ProcessRunner` is the sole `Process.Start` site; that invariant is broken in 5 places.
10. **C-10** Add a Show-wizard-again entry to Settings — the first-launch wizard is one-shot and there's no path back.

The rest is polish, documentation, and stretch goals (R-07 Avalonia port, R-03 Magisk modules, screenshot-driven README).

---

## Evidence Reviewed

### Local files inspected

- All 23 service files in [Services/](AndroidEmulatorPlus/Services/) (line counts:
  `wc -l Services/*.cs` returns 2603 lines total).
- All 9 view-models in [ViewModels/](AndroidEmulatorPlus/ViewModels/) (2045 lines).
- All 32 XAML + xaml.cs files in [Views/](AndroidEmulatorPlus/Views/).
- [App.xaml](AndroidEmulatorPlus/App.xaml) + [App.xaml.cs](AndroidEmulatorPlus/App.xaml.cs).
- [MainWindow.xaml](AndroidEmulatorPlus/MainWindow.xaml) + [MainWindow.xaml.cs](AndroidEmulatorPlus/MainWindow.xaml.cs).
- [AndroidEmulatorPlus.csproj](AndroidEmulatorPlus/AndroidEmulatorPlus.csproj), [app.manifest](AndroidEmulatorPlus/app.manifest).
- [Themes/Mocha.xaml](AndroidEmulatorPlus/Themes/Mocha.xaml), [Themes/Latte.xaml](AndroidEmulatorPlus/Themes/Latte.xaml), [Themes/Styles.xaml](AndroidEmulatorPlus/Themes/Styles.xaml).
- All 5 test files in [AndroidEmulatorPlus.Tests/](AndroidEmulatorPlus.Tests/).
- [.github/workflows/build.yml](.github/workflows/build.yml).
- All embedded resources: [Resources/known-hashes.json](AndroidEmulatorPlus/Resources/known-hashes.json), [Resources/bloat-presets.json](AndroidEmulatorPlus/Resources/bloat-presets.json).
- [README.md](README.md), [CHANGELOG.md](CHANGELOG.md), [ROADMAP.md](ROADMAP.md), [CLAUDE.md](CLAUDE.md), [LICENSE](LICENSE), [.gitignore](.gitignore).

### Git history range

`b0525db..8502f22` (24 commits since the pass-2 plan landed). No branches besides
`main`. Single remote `SysAdminDoc/AndroidEmulatorPlus`. Push works from this VM.

### External / primary sources

- Android emulator console docs — `developer.android.com/studio/run/emulator-console`
  (verifies the `geo fix`, `power capacity`, `gsm call`, `sms send`, `network speed/delay`
  commands consumed by [ConsoleService.cs](AndroidEmulatorPlus/Services/ConsoleService.cs)).
- `adb pair` syntax — `developer.android.com/tools/adb` (verifies the `host:port code`
  positional form used by [AdbService.PairAsync](AndroidEmulatorPlus/Services/AdbService.cs)).
- `apksigner verify --print-certs` output format — primary docs at
  `developer.android.com/tools/apksigner` confirm the "Signer #N certificate SHA-256
  digest" regex used in [ApkSignerService.ExtractCertSha](AndroidEmulatorPlus/Services/ApkSignerService.cs).
- rootAVD — `gitlab.com/newbit/rootAVD` — the `LISTONLY` mode is documented as
  `LISTONLY=1` env var in the project README. **Likely:** the bash function name
  `ListAllAVDs` used in `RootService.DryRunAsync` may not be the canonical entry
  — needs live validation against the current rootAVD revision.
- Catppuccin Latte palette hex values — verified against
  `github.com/catppuccin/catppuccin/blob/main/docs/style-guide.md`.

### Verification gaps

- **No .NET SDK on this VM** — none of the code added since pass-2 has been
  `dotnet build`-verified locally. The CI workflow on `windows-latest` is the
  authoritative build, but it can't run yet (memory note:
  SysAdminDoc-org GitHub Actions billing is locked).
- **No runtime exercise** — every "the dialog opens / the button works" claim
  is from reading the XAML, not from a running app. A 30-minute smoke test on a
  desktop with .NET 9 SDK + Android SDK should cover all 8 tabs.
- **rootAVD command surface** — `DryRunAsync` calls bash with `ListAllAVDs` as
  the function name; that's my reading of newbit's script. Verify before merging
  C-NN tasks that rely on it.
- **`Magisk` hash entries** — `Resources/known-hashes.json` ships with an empty
  `magisk` table. Every Magisk install today is trust-on-first-use until those
  hashes are populated.

---

## Current Product Map

### Tabs / sidebar workflow

1. **① Install / SDK** — SDK detection, cmdline-tools auto-installer (with SHA-256
   verify + fallback-URL surface), accel-check (with remediation links), crash log
   viewer, accept-all-licenses, theme picker.
2. **② AVDs** — list + create AVDs from local images, "Browse online…" picker
   (sdkmanager UI), per-card actions: ▶ Launch / ■ Stop / Cold Boot / overflow
   menu (Launch with options, Show on disk, Rename, Duplicate, Desktop shortcut,
   Snapshots, Delete). Empty-state card when no AVDs.
3. **③ Root** — Inline "Launch & root" CTA when no emulator attached, Root with
   Latest Magisk (Cancel-able), Verify+Persist, Un-Root (with confirm), Dry-run
   LISTONLY, KernelSU manual-procedure note.
4. **④ Migrate from Phone** — Source/target status, Wi-Fi pair expander, scope
   toggles (APK / internal / external / OBB), force-stop-on-phone, package list,
   progress bar, cache usage card with clear buttons.
5. **⑤ Apps / Debloat** — Filter, Include system/disabled toggles, Verify
   signatures toggle, Compute sizes, source/disabled tags per row, preset
   ItemsControl (JSON-driven), uninstall mode radio (adb / user 0), Export data
   / Import from ZIP, drag-drop install.
6. **⑥ Configure** — Target AVD, RAM/cores/disk sliders, Screen preset picker,
   W/H/DPI text boxes, GPU mode picker, fastboot toggles, Resize disk only /
   Resize + Wipe Data (typed confirm).
7. **⑦ Logcat** — Priority + package filter, Start/Stop, Clear buffer / view,
   Save to file. Virtualizing 5000-line ring.
8. **⑧ Console** — GPS, battery (capacity + status), telephony (gsm call / sms
   send), network speed/delay, manual clipboard pull/push, free-form `adb emu`
   command.

### Top bar (left → right)

- App name + version pill (`v0.1.0` — stale).
- SDK status pill, phone status pill, emulator status pill.
- 📷 Screenshot, 🎥 Record toggle, 🖥 scrcpy, ⚙ Settings.

### Cross-cutting infrastructure

- **DI** — `Microsoft.Extensions.DependencyInjection` in `App.OnStartup`. 23
  singletons across services + view-models; 1 transient `MainWindow`.
- **Persistence** — `%LOCALAPPDATA%\AndroidEmulatorPlus\`:
  `settings.json` (theme, paths, proxy, hasSeenWizard),
  `crash.log` (unhandled exceptions),
  `logs/app-YYYYMMDD.log` (rolling 14 days),
  `cache/` (rootAVD clone + Magisk APK),
  `transfer/` (migration tarballs + bundle extracts).
- **Theme** — runtime-swappable palette (Mocha/Latte). Live-swap requires app
  restart because Styles.xaml uses StaticResource brush references.
- **Keyboard shortcuts** — Ctrl+1..8 to navigate, F5 refresh, Ctrl+L clear log,
  Ctrl+R screenshot.

### Platforms / distribution

- Windows 10/11 x64, .NET 9 WPF (`net9.0-windows`, x64 explicit).
- No installer; no signed binary; no current release artifact (the CI pipeline
  is wired but hasn't run — billing dependency).
- Source clone + `dotnet build` is the only working install path.

---

## Feature Inventory

Format: feature → entry → maturity → notes. (Truncated to the items where pass-3
research surfaced *new* information.)

| Feature | Entry | Maturity | Notes / new findings |
|---|---|---|---|
| `.apks` / `.xapk` / `.apkm` bundle install | Apps / drag-drop or file picker | **Likely buggy** | `ExtractBundle` sorts inner APKs by ascending size; with split bundles the base APK is usually *larger* (manifest + main resources) and ends up last. Some `install-multiple` consumers (PackageManager v34+) reject when base isn't first. **C-02**. |
| APK signature verification | Apps / Verify-signatures toggle | **Partial** | `ApkSignerService.InspectAsync` runs and logs the cert SHA-256. The companion `InstalledCertShaAsync` is defined but never called, so the "warn on mismatch with already-installed package" half (R-08) is missing. **C-04**. |
| HTTP proxy (Settings) | Settings → Network | **Dead** | `SettingsService.HttpProxy` is read+saved but `DownloadService` ignores it. **C-03**. |
| First-launch wizard | Auto on startup | **One-shot** | No "show wizard again" entry in Settings; once `HasSeenWizard=true`, the only way back is to edit settings.json. **C-10**. |
| Welcome wizard "Open settings.json" | Settings dialog → "Open settings.json" | Complete | Works — useful for the C-10 workaround. |
| Theme switcher | Install tab + Settings | Restart required | Confirmed: StaticResource brushes can't live-swap. Restart prompt is shown. DynamicResource sweep is C-12 below. |
| Snapshot manager | AVD overflow → Snapshots… | Complete | Read-list always works; Save/Load require the emulator to be running (status text says so). |
| Migrate cache card | Migrate tab | Mostly complete | Doesn't refresh after Apps-tab Export/Import which also writes to `transfer/`. **C-11**. |
| Multi-AVD process tracking | EmulatorService | Complete | One race noted: setting `EnableRaisingEvents=true` AFTER `Process.Start` means a sub-second-lived emulator could exit before the subscription, leaving a stale entry. Theoretical; not a real concern for emulators. |
| Console / `adb emu` | Console tab | Complete | Free-form command splits on whitespace — quoted strings with spaces would break. Document or use a real tokenizer. Minor. |
| OBB transfer | Migrate / OBB toggle | Complete | `tar /sdcard/Android/obb/<pkg>` — works without root for /sdcard. |
| ZIP export / import | Apps tab | Complete | Import requires the package to already be installed on the target. Microcopy already says so. |
| Apps "Compute sizes" | Apps tab | Complete | Computes only for `FilteredApps`. After clearing the filter, previously-non-visible rows still show "—". **C-13**. |
| Logcat tab | Sidebar ⑦ | Complete | Auto-scrolls; 5000-line virtualized ring. Doesn't persist filter across sessions — fine. |
| Force-stop on phone (A-30) | Migrate scope | Complete | Logs run before the tar; A-30 satisfied. |
| Phone tar flavor probe (A-29) | Auto on first internal-data leg | Complete | Cached per serial. Good. |
| Welcome dialog (R-02) | First launch | Complete | Modal blocking; doesn't pre-check SDK install or AVD creation status visually — only via the status TextBlocks per step. Could autoadvance through completed steps. **C-14**. |
| `ApkSignerService.InstalledCertShaAsync` | — | **Dead code** | Defined, never called. **C-04** uses it. |
| `MigrationService.TransferOptions` | — | Previously removed | Confirmed deleted in batch-1. |
| Theme on Settings dialog | Settings → Appearance | Duplicate UI | Theme picker also lives on the Install tab (batch-14). One canonical home is enough; pick Settings, remove the Install tab card. **C-15** (cleanup). |

### Tests project (`AndroidEmulatorPlus.Tests/`)

- 5 test classes, 13 test methods (counted manually).
- Covered: `ParseIni/WriteIni`, `ParseSizeGb`, `ParseFailReason`, `SystemImageSortKey`, `ComputeSha256`.
- **Uncovered**: `AvdService.Duplicate` (highest-risk new code — full file-tree copy + ini rewrite), `AppService.ExtractBundle` (zip + obb detect), `AppService.PushObbAsync`, `ConfigService.PreviewWipe`, `HashVerificationService.VerifyMagisk` / `VerifyCmdlineTools` (manifest plumbing). **C-08**.

---

## Competitive and Ecosystem Research

Brief because pass-2 covered the landscape; here I focus on what's *changed* since
pass-2 or that the now-mature feature set re-opens.

### Android Studio Device Manager

- **Now-covered by this app**: snapshot manager (R-06), AVD duplicate (A-26),
  device profile picker (Wear / TV / Auto via A-38), system-image install on
  demand (R-01), launch flags (A-23).
- **Still distinctive in Studio**: device mirroring (renders the emulator window
  into the IDE), pair-over-Wi-Fi UX with QR code, resizable preview AVD, Studio
  Bot. Out of scope.

### Genymotion Desktop

- Now-covered: GPS / battery / telephony / network panels (A-40).
- Still distinctive: cloud-hosted variants, account-tied licensing.

### scrcpy

- **Now-launched as external tool** (A-39). Embedding the SDL surface is out of
  scope, but a "auto-launch scrcpy after AVD boot" toggle would land cleanly —
  **C-16**.

### SAI (Split APKs Installer, Android)

- The bundle install path (B-02) is modeled on SAI's `.apks` and `.xapk`
  conventions. Pass-2 already noted this. Pass-3 finds the base-APK-first
  ordering issue (**C-02**).

### Magisk Manager (the Android app)

- R-03 (module manager) is still the canonical gap. With root flow now stable
  and Verify-Persist policy working, this is the highest-value P2 left.

### WSA (sunset)

- Pattern reference for the focused single-EXE shape. The product has held that
  identity — good.

---

## Highest-Value New Features

### C-01 — Cut v0.2.0 release (P0, M)

- **User problem**: nothing the autonomous loop shipped is reachable to a user
  yet. No tag, no signed binary, no download.
- **Evidence**: [AndroidEmulatorPlus.csproj:12](AndroidEmulatorPlus/AndroidEmulatorPlus.csproj#L12)
  still shows `<Version>0.1.0</Version>`; [MainWindow.xaml:6](AndroidEmulatorPlus/MainWindow.xaml#L6)
  shows `Title="AndroidEmulatorPlus v0.1.0"`. [CHANGELOG.md](CHANGELOG.md)
  `[Unreleased]` has 25+ entries.
- **Behavior**: Bump in the 5 places CLAUDE.md mandates (csproj `<Version>`, `<FileVersion>`,
  `<InformationalVersion>`, `MainViewModel.cs` startup log, `MainWindow.xaml`
  `Title=`, sidebar version pill, `README.md` shields-io badge, `CHANGELOG.md`).
  Populate `Resources/known-hashes.json` with the current Magisk + cmdline-tools
  SHAs after smoke-test. Lock `RootService.RootAvdPinnedRef` to a real SHA
  (A-03). Tag `v0.2.0` to trigger the release workflow.
- **Touches**: csproj, MainWindow.xaml, MainViewModel.cs, README.md, CHANGELOG.md,
  RootService.cs, Resources/known-hashes.json.
- **Risk**: needs a desktop with .NET SDK + a clean Android emulator install for
  smoke-test.
- **Verify**: GitHub Releases page shows a `v0.2.0` tag with the win-x64
  self-contained ZIP attached.
- **Complexity**: M, P0.

### C-02 — Base APK first in `install-multiple` (P0, S)

- **Problem**: `AppService.ExtractBundle` returns APKs ordered by ascending file
  size. For `.apks` / `.xapk` bundles where the base APK contains the manifest +
  main resources, the base is typically *larger* than the per-config splits and
  ends up last. `pm install-multiple` ordering is significant for some
  validators (notably APK signature scheme V2 verification of `pm install-create`
  sessions on API 33+).
- **Evidence**: [AppService.cs:62](AndroidEmulatorPlus/Services/AppService.cs#L62)
  `.OrderBy(static p => p.Length)`.
- **Behavior**: Reorder so the entry literally named `base.apk` (or, failing
  that, the largest) comes first, and config splits follow.
- **Touches**: `AppService.ExtractBundle`. New helper to pick the base.
- **Risk**: None — strict ordering change.
- **Verify**: Drop a SAI-export `.apks` produced from a modern app with config
  splits; install succeeds.
- **Complexity**: S, P0.

### C-03 — Apply `SettingsService.HttpProxy` to `DownloadService` (P1, S)

- **Problem**: Proxy field is editable in Settings but `DownloadService`
  instantiates `new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })`
  with no proxy configuration. Field is dead.
- **Evidence**: [DownloadService.cs:15](AndroidEmulatorPlus/Services/DownloadService.cs#L15)
  vs [SettingsService.cs:29](AndroidEmulatorPlus/Services/SettingsService.cs#L29).
- **Behavior**: `DownloadService` ctor takes a `SettingsService`, reads the
  proxy URL, and configures the handler:
  ```csharp
  var handler = new HttpClientHandler { AllowAutoRedirect = true };
  if (Uri.TryCreate(settings.Current.HttpProxy, UriKind.Absolute, out var p))
      handler.Proxy = new WebProxy(p) { UseDefaultCredentials = true };
  _http = new HttpClient(handler);
  ```
- **Touches**: `DownloadService`, App.xaml.cs DI bootstrap.
- **Risk**: Stale `HttpClient` reuses old proxy if settings change at runtime —
  document that the proxy applies on next launch (consistent with theme).
- **Verify**: Set an HTTP proxy that logs requests; click Download
  command-line tools; proxy log shows the request.
- **Complexity**: S, P1.

### C-04 — Cross-check signed cert against installed package (P1, M)

- **Problem**: The Verify-signatures toggle already runs apksigner but never
  reads the installed app's cert. The "warn on signer mismatch" half of R-08 —
  which is the actually-useful half, because it catches re-signed APKs trying
  to upgrade a Play-Store-installed app — is missing.
- **Evidence**: [ApkSignerService.InstalledCertShaAsync](AndroidEmulatorPlus/Services/ApkSignerService.cs#L50)
  is defined but unused. `AppsViewModel.VerifyBeforeInstallAsync` has a TODO
  comment about needing aapt to derive the package name.
- **Behavior**: Use `aapt2 dump packagename <apk>` (or fall back to `apksigner
  verify --print-certs --verbose` + AndroidManifest extraction) to get the
  package id, then `pm dump <pkg>` on the device, then string-compare the
  cert SHAs. On mismatch raise a `ConfirmDialog` "This APK is signed by a
  different developer than the version installed on your device. Continue?"
- **Touches**: `ApkSignerService`, `AppsViewModel.VerifyBeforeInstallAsync`.
  `aapt2` lives in `build-tools/<ver>/aapt2.exe` (add to `SdkLocator`).
- **Risk**: aapt2 output format varies between build-tools versions; pin a regex
  + fall back gracefully.
- **Verify**: Re-sign a known APK with a new dev cert; install attempt warns.
- **Complexity**: M, P1.

### C-05 — `allowBackup=false` pre-flight (P1, M, was A-19)

- **Problem**: Apps that explicitly opt out of backup will refuse the restored
  data after migration. Pass-2's A-19 never shipped. This is the only P1 item
  still open.
- **Evidence**: ROADMAP A-19 unchecked.
- **Behavior**: Before each package's `TransferInternalDataAsync`, run
  `adb shell pm dump <pkg> | grep -i allowBackup` (or `dumpsys package <pkg>`).
  When false, mark the row with ⚠ and skip the data leg unless the user opted
  "Force migrate all". Persist the per-row decision on the model.
- **Touches**: `MigrationService` (probe helper), `MigrateViewModel`,
  `MigrateView` (column for ⚠).
- **Verify**: Add `com.discord` (allowBackup=false) to the source list; row shows
  ⚠; data leg is skipped by default; checkbox to force shows in the row.
- **Complexity**: M, P1.

### C-06 — Application icon + window icon (P1, S)

- **Problem**: WPF default icon means SmartScreen warns on first run and the
  alt-tab card looks unbranded.
- **Evidence**: No `<ApplicationIcon>` in csproj; `MainWindow.xaml` has no
  `Icon=`; no `.ico` in repo.
- **Behavior**: Add `AndroidEmulatorPlus/Assets/aep.ico` (multi-size: 16, 24,
  32, 48, 64, 256), set `<ApplicationIcon>Assets\aep.ico</ApplicationIcon>` in
  csproj, set `Icon="Assets/aep.ico"` on `MainWindow`.
- **Touches**: csproj, MainWindow.xaml, new Assets folder.
- **Risk**: Icon design.
- **Verify**: alt-tab card shows icon; exe in Explorer shows icon.
- **Complexity**: S, P1.

### C-07 — R-03 Magisk module manager (P2, L)

- **Problem**: Last remaining big feature from the original R-roadmap. With
  Verify+Persist working, the rooted emulator is a stable target for module
  install.
- **Behavior**: New "③.b Modules" sub-tab on the Root tab (or expander).
  Pre-curated module list (Shamiko, LSPosed, PlayIntegrityFork, Zygisk
  DenyList) with their GitHub release URLs. Install flow:
  `adb push <zip> /sdcard/`, then `magisk --install-module /sdcard/<zip>`
  (or `magisk -Z install`). Reboot prompt afterward. List installed modules
  via `magisk module list`.
- **Touches**: new `MagiskService`, new `ModulesViewModel` + View.
- **Risk**: Curated list maintenance — ship as JSON like B-04 to keep updates
  out of code.
- **Verify**: Install Shamiko on a fresh API 35 Google Play AVD; reboot; check
  `magisk module list` returns it.
- **Complexity**: L, P2.

### C-08 — Test coverage for high-risk new code (P2, M)

- **Problem**: `AvdService.Duplicate` does a recursive file copy + ini path
  rewrite. Bugs here trash AVDs. Untested.
- **Evidence**: [AvdService.cs:184-247](AndroidEmulatorPlus/Services/AvdService.cs#L184-L247).
- **Behavior**: New test class:
  - `Duplicate_copies_avd_dir_and_rewrites_ini` — create a fake AVD layout
    under a temp dir, run Duplicate, assert config.ini's AvdId is rewritten and
    the .avd folder is byte-identical otherwise.
  - `Duplicate_refuses_collision` — assert InvalidOperationException when the
    target name exists.
  - `ExtractBundle_returns_base_then_splits` — fixture .apks with base+splits;
    assert base.apk is first after C-02 fix.
  - `PreviewWipe_lists_snapshots_and_overlays`.
  - `PresetService_user_override_replaces_by_id` — fixture user JSON, assert
    the id-based merge.
- **Touches**: `AndroidEmulatorPlus.Tests/` (new test files).
- **Verify**: `dotnet test`.
- **Complexity**: M, P2.

### C-09 — Lift remaining `Process.Start` into `ProcessRunner` (P2, M)

- **Problem**: CLAUDE.md says `Helpers/ProcessRunner` is the only place
  `Process.Start` lives. After this pass the invariant is violated in 5 places
  (each with stdin reasons): `RootService.PatchAsync` (lines 159-178),
  `RootService.DryRunAsync` (lines 113-136), `AvdService.CreateAsync` (lines
  109-141), `SdkmanagerService.AcceptLicensesAsync` (lines 42-69),
  `SdkmanagerService.InstallAsync` (lines 96-122), `ScreenRecordService.Start`
  (lines 44-58), `LogcatService.Start` (lines 25-60), `AdbService.PairAsync`
  (lines 145-165).
- **Behavior**: Add to `ProcessRunner`:
  - `RunWithStdinAsync(exe, args, IEnumerable<string> stdinLines, …)` —
    consumed by SdkmanagerService and AvdService.
  - `StreamAsync(exe, args, Action<string> onLine, …)` — consumed by
    RootService and LogcatService and ScreenRecordService.
- **Touches**: `ProcessRunner.cs`, 8 callers.
- **Risk**: Subtle behavior changes — guard with the existing tests + per-VM
  smoke.
- **Complexity**: M, P2.

### C-10 — Show-wizard-again in Settings (P2, S)

- **Problem**: First-launch wizard is one-shot. Settings dialog has no way to
  reopen it.
- **Behavior**: Add a "Show welcome wizard…" button to Settings; clicking sets
  `HasSeenWizard=false`, saves, and immediately opens the wizard.
- **Touches**: SettingsDialog.xaml + xaml.cs, SettingsService.
- **Complexity**: S, P2.

### C-11 — Refresh Migrate cache after Apps tab Export/Import (P2, S)

- **Problem**: ExportAppDataAsync / ImportAppDataAsync write to `transfer/`. The
  Migrate tab's cache card doesn't auto-recalculate after these operations, so
  the user doesn't see the impact.
- **Evidence**: `MigrateViewModel.RefreshCache` is called in `OnAppliedMigrate`
  but not from the Apps tab.
- **Behavior**: Either expose `RefreshCache` via the shared `CacheDiagnosticsService`
  state, or have `AppsViewModel` `_ = MigrateVm.RefreshCacheCommand.Execute(null)`.
- **Touches**: `AppsViewModel`, `MigrateViewModel` (publish a cache-changed event,
  or expose `CacheDiagnosticsService` invalidate via singleton).
- **Complexity**: S, P2.

### C-12 — DynamicResource brush sweep for live theme swap (P2, M)

- **Problem**: Theme requires app restart because Styles.xaml binds via
  StaticResource.
- **Behavior**: Replace `StaticResource …Brush` with `DynamicResource …Brush`
  in `Themes/Styles.xaml` and in every view that references brushes (~60
  sites). Then add a `ThemeService.ApplyAsync(theme)` that swaps
  `Application.Resources.MergedDictionaries[0]` between Mocha/Latte. Persist
  the choice still; remove the "restart required" warning.
- **Touches**: Themes/Styles.xaml + every view file + new ThemeService.
- **Risk**: A few view styles bind via Setter, which may not auto-refresh
  DynamicResource — verify per-element.
- **Complexity**: M, P2.

### C-13 — Compute sizes covers all rows (P3, S)

- **Problem**: Apps tab "Compute sizes" iterates only `FilteredApps`. If the
  user filters first, computes sizes, then clears the filter, the unfiltered
  rows show "—".
- **Behavior**: Iterate the full `Apps` collection.
- **Touches**: `AppsViewModel.ComputeSizesAsync`.
- **Complexity**: S, P3.

### C-14 — Welcome wizard auto-skips completed steps (P3, S)

- **Problem**: The wizard's status TextBlocks show "✓ SDK detected" when SDK is
  present, but the user still has to scroll past Step 1 to find what they need.
- **Behavior**: Hide completed-step cards by default; offer a "Show all steps"
  toggle.
- **Touches**: `WelcomeDialog`.
- **Complexity**: S, P3.

### C-15 — Remove duplicate Theme picker on Install tab (P3, S)

- **Problem**: Theme picker lives in BOTH the Install tab (batch-14) and
  Settings (batch-23). Two homes for one setting is a maintenance and UI risk.
- **Behavior**: Delete the Install tab's Appearance card; keep only Settings.
  Settings is now the canonical place.
- **Touches**: InstallView.xaml, InstallViewModel.cs.
- **Complexity**: S, P3.

### C-16 — "Auto-launch scrcpy after boot" toggle (P3, S)

- **Problem**: Power users who prefer scrcpy's input handling over the emulator
  window currently launch it manually after each AVD launch.
- **Behavior**: Add a `LaunchOptions.AutoScrcpy` flag (`Settings.AutoScrcpy`
  default in Settings dialog). When set, after `Launch` returns and adb reports
  the device online, call `ScrcpyService.Launch(serial)`.
- **Touches**: SettingsService, EmulatorService, LaunchOptionsDialog,
  AvdViewModel.LaunchCommand.
- **Complexity**: S, P3.

### C-17 — README screenshots + landing image (P2, M)

- **Problem**: README has no visual content; first-impression is a wall of
  text. Comparable tools (Genymotion, BlueStacks) lead with screenshots.
- **Behavior**: Add `docs/screenshots/` with one PNG per tab; embed inline in
  README. Use the new Mocha + Latte themes to show both palettes.
- **Touches**: README.md, new docs/screenshots/ folder.
- **Complexity**: M, P2.

---

## Existing Feature Improvements

### Free-form `adb emu` command tokenizer (Console tab)

- **Current**: `FreeFormArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)`
  ([ConsoleViewModel.cs:91](AndroidEmulatorPlus/ViewModels/ConsoleViewModel.cs#L91)).
- **Problem**: Quoted strings break: `sms send 5551234 "Hello, world"` becomes
  4 tokens.
- **Recommended**: Tiny quote-aware split. Microsoft.Win32 has no built-in; a
  10-line regex `("([^"]*)")|(\S+)` does it.
- **Complexity**: S, P3.

### `MainWindow.Loaded` wizard timing

- **Current**: WelcomeDialog opens from `MainWindow_Loaded`. Loaded fires
  *after* the window renders, so the wizard appears on top of an already-drawn
  main window.
- **Recommended**: ContentRendered or post-Loaded with a short Dispatcher post
  to ensure transitions look smooth on slow machines.
- **Complexity**: S, P3.

### `Apps` tab uninstall mode radio buttons

- **Current**: The radios use `EqualsConverter` to bind `IsChecked`. On click
  they trigger separate commands `SetUninstallModeUser` / `SetUninstallModeUser0`.
- **Recommended**: Bind `IsChecked` two-way directly using a `BooleanInverter`
  pattern or use `RadioButton.CommandParameter` with one command.
- **Complexity**: S, P3.

### `ProcessRunner.RunAsync` cancellation semantics

- **Current**: When `timeout` fires, throws OperationCanceledException through
  the caller's `ct.IsCancellationRequested` check is bypassed because the
  inner timeout CTS is linked but its own cancel doesn't surface as
  `ct.IsCancellationRequested == true`.
- **Evidence**: [ProcessRunner.cs:62-75](AndroidEmulatorPlus/Helpers/ProcessRunner.cs#L62-L75).
- **Recommended**: Throw a more specific exception (`TimeoutException`) on
  timeout so callers can distinguish user-cancel vs timeout. Currently the
  Root flow can't tell.
- **Complexity**: S, P3.

### `LogcatService.Start` flushes lines on UI thread

- **Current**: Each line raises `LineReceived` from a background thread;
  `LogcatViewModel.OnLine` dispatches each to UI via `BeginInvoke`. With high
  log rates (thousands/sec on busy apps) the UI thread can stall.
- **Recommended**: Batch lines in a 100ms timer; flush on tick.
- **Complexity**: M, P2.

---

## Reliability, Security, Privacy, and Data Safety

### Real bugs found in pass-3

- **C-02 base-APK-last** — see above.
- **C-03 dead HttpProxy** — see above.
- **C-04 mismatched signer warning never fires** — see above.
- **A-03 still open** — rootAVD pin is `"master"`; one breaking newbit push
  silently bricks the root flow. Lock the SHA as part of C-01.
- **Magisk SHA-256 manifest is empty** — every Magisk install today is
  trust-on-first-use. C-01 populates it.

### Process tree races

- **EmulatorService.Launch** sets `EnableRaisingEvents=true` after Process.Start.
  Theoretical race: process exits before subscription; stale dictionary entry
  forever. Practical risk near zero (emulators don't exit in microseconds).
  Worth a 1-line fix: hand the Process to ProcessRunner.StartDetached which
  sets the flag at create time.

### Settings file safety

- `SettingsService.Save` overwrites the file in place with a single
  `File.WriteAllText`. A crash mid-write corrupts settings.json. Mitigation:
  write to `.tmp` and `File.Move` atomically. **C-18** (low priority).

### Privacy / network

- Outbound domains documented in README. After C-03 (proxy) ships, also
  document that the proxy applies on next launch.

---

## UX, Accessibility, and Trust

### Wizard auto-advance

- C-14 above.

### Theme on multiple cards

- C-15 above.

### Detail log brush

- Already fixed in batch-1 (B-16). Confirmed on
  [MainWindow.xaml:148-150](AndroidEmulatorPlus/MainWindow.xaml#L148-L150).

### Microcopy gaps

- The Apps tab "Compute sizes" button has a tooltip but no inline note that root
  is required. Users who click without root see only a one-line log warning.
- "Export data…" / "Import from ZIP…" buttons on the Apps tab also silently
  fail when not rooted; same surfaced log warning. Add an inline disabled
  state when no rooted emulator is attached.

### Screen-reader / keyboard

- Most controls are vanilla WPF and have implicit accessibility names. The
  custom `ConfirmDialog` typed-confirm field could use `AutomationProperties.Name`
  for screen-reader clarity.

---

## Architecture and Maintainability

### Module bloat

- `AppsViewModel` is now 367 lines — pretty big. Extract `BundleInstaller`
  (the ExtractBundle + OBB push + cleanup orchestration) into its own service
  or static helper to keep the view-model focused on UI state.
- `AvdViewModel` is 350 lines and growing — same treatment for the rename /
  duplicate / launch-with-options dialog plumbing. **C-19** (refactor).

### Dead / partial code

- `ApkSignerService.InstalledCertShaAsync` — dead (use it in C-04).
- `SettingsService.HttpProxy` — dead until C-03.
- `EmulatorService.RunningAvdNames` — public but no consumer; could become
  the data source for a "Running emulators" status panel later.

### Test gaps

- See C-08 above.

### Release / build / deployment

- CI workflow is wired (`.github/workflows/build.yml`) but **has not actually
  been observed running**. With SysAdminDoc billing locked the workflow
  commits but never executes. Verifying it works on a clean GitHub Account
  + fork is part of C-01's smoke.
- No GitHub Releases yet — also addressed by C-01.
- No installer (MSIX or NSIS). Tracked as C-20 for future. The self-contained
  single-file EXE that CI produces is fine for v0.2.

---

## Prioritized Roadmap

Each item uses the C-NN tag introduced in this pass.

### Phase 1 — Ship v0.2.0

- [ ] **P0 C-01** — Cut v0.2.0 release
  - Why: 24 unreleased commits, no downloadable binary.
  - Evidence: `csproj <Version>0.1.0</Version>`; `CHANGELOG [Unreleased]` has 25+ entries.
  - Touches: csproj, MainWindow.xaml, MainViewModel.cs (startup log), README.md badge,
    CHANGELOG.md, RootService.RootAvdPinnedRef, Resources/known-hashes.json.
  - Acceptance: `git tag v0.2.0 && git push --tags` triggers the release workflow;
    a `.zip` artifact appears on GitHub Releases; downloaded EXE launches on a clean
    Windows 11 VM with .NET 9 Runtime installed and v0.2.0 in its title bar.
  - Verify: open the Releases page; download; run.
- [ ] **P0 C-02** — Order base APK before splits in `ExtractBundle`
  - Why: install-multiple ordering matters for some validators; ascending-size
    puts the base last because the base is typically larger than per-config splits.
  - Evidence: `AppService.cs:62` `.OrderBy(static p => p.Length)`.
  - Touches: `AppService.cs`.
  - Acceptance: a SAI-export `.apks` from a modern Play Store app installs cleanly
    without "INSTALL_FAILED_INVALID_APK".
  - Verify: drop a real `.apks` (e.g. an export of WhatsApp); Apps tab reports success.

### Phase 2 — Plug the loose ends

- [ ] **P1 C-03** — HTTP proxy applied to DownloadService
  - Why: Settings field is persisted but ignored.
  - Touches: DownloadService ctor takes SettingsService; HttpClientHandler.Proxy
    is set from the override.
  - Acceptance: set a proxy in Settings, restart, run the cmdline-tools download;
    the proxy log shows the request.
- [ ] **P1 C-04** — Cert-mismatch warning before install
  - Why: R-08 was half-implemented — verifies but never compares against installed.
  - Touches: ApkSignerService.InstalledCertShaAsync wired into
    AppsViewModel.VerifyBeforeInstallAsync; aapt2 path added to SdkLocator;
    new ConfirmDialog on mismatch.
  - Acceptance: re-sign a known APK with a new cert; install attempt raises a
    confirm dialog showing both SHAs.
- [ ] **P1 C-05** — `allowBackup=false` pre-flight (was A-19)
  - Why: only P1 item never shipped.
  - Touches: MigrationService probe + MigrateView column.
  - Acceptance: a com.discord-style package is marked ⚠ and its data leg is
    skipped by default.
- [ ] **P1 C-06** — Application icon + window icon
  - Why: branding + SmartScreen first impression.
  - Touches: Assets/aep.ico, csproj, MainWindow.xaml.
  - Acceptance: alt-tab card and Explorer thumbnail show a non-default icon.

### Phase 3 — Polish + parity

- [ ] **P2 C-07** — R-03 Magisk module manager
  - Touches: new MagiskService, ModulesViewModel + View, sub-tab or expander on Root.
  - Acceptance: install Shamiko on a rooted API 35 AVD; `magisk module list` returns it.
- [ ] **P2 C-08** — Tests for Duplicate, ExtractBundle, PreviewWipe, PresetService
  merge, HashVerificationService manifest plumbing
  - Touches: `AndroidEmulatorPlus.Tests/` new files + Fixtures.
  - Acceptance: `dotnet test` reports the 5 new fixtures green.
- [ ] **P2 C-09** — Lift Process.Start callers into ProcessRunner helpers
  - Touches: ProcessRunner.RunWithStdinAsync + StreamAsync; 8 callers.
  - Acceptance: only ProcessRunner contains `Process.Start` (grep verifies).
- [ ] **P2 C-10** — Show-wizard-again button in Settings
  - Touches: SettingsDialog.
  - Acceptance: clicking the button re-opens the welcome wizard.
- [ ] **P2 C-11** — Migrate cache refresh after Apps tab export/import
  - Touches: AppsViewModel calls a shared cache-changed event.
- [ ] **P2 C-12** — DynamicResource sweep for live theme swap
  - Touches: Themes/Styles.xaml + every view brush reference + ThemeService.
- [ ] **P2 C-17** — README screenshots + landing image
  - Touches: docs/screenshots/, README.md.

### Phase 4 — P3 polish

- [ ] **P3 C-13** — Compute sizes covers all rows
- [ ] **P3 C-14** — Welcome wizard auto-skips completed steps
- [ ] **P3 C-15** — Remove duplicate Theme picker on Install tab
- [ ] **P3 C-16** — "Auto-launch scrcpy after boot" toggle
- [ ] **P3 C-18** — Atomic write for settings.json (write-tmp + rename)
- [ ] **P3 C-19** — Refactor AppsViewModel / AvdViewModel into smaller services
- [ ] **P3 C-20** — MSIX or NSIS installer
- [ ] **P3 R-07** — Avalonia port (Linux + macOS)

---

## Quick Wins

These items can ship inside C-01 (the version bump) or in a single dedicated commit:

- Reconcile `ROADMAP.md`'s "Quick wins" section — entries B-12..B-20 are listed as
  unchecked but all 8 actually shipped in batch-1 (commit `5676326`).
- Bump `MainWindow.xaml` title and sidebar version pill alongside the csproj
  `<Version>` (CLAUDE.md mandates this).
- Add `<ApplicationManifest>` icon to make Explorer pretty even before C-06's
  full icon set lands.
- Trim the bundle staging dir in `AppService.ExtractBundle`'s exception path — it
  currently leaves the work dir if the zip extraction itself throws. The finally
  block only catches the success path.
- Fix `MigrateView.xaml` "Force-stop on source phone" tooltip wording — currently
  reads "Closes the app on the phone …" but the implementation also runs on the
  *emulator* side (already in place via existing `am force-stop`). Minor.

---

## Larger Bets

- **C-07 Magisk module manager** — touches Root flow, list-of-modules curation,
  install/uninstall lifecycle.
- **C-12 DynamicResource sweep** — touches every brush reference in the
  codebase, ~60 sites.
- **R-07 Avalonia port** — XAML mostly compatible, but `Process` plumbing,
  `Microsoft.Win32.OpenFileDialog`, `OpenFolderDialog`, `SaveFileDialog`,
  `System.Windows.Clipboard` all need shims. Defer until v0.3+.

---

## Explicit Non-Goals

- **Embedding the scrcpy SDL surface** — A-39 launches scrcpy externally; that's
  the right boundary.
- **Custom kernel / forked emulator** — BlueStacks-style. Out of mission.
- **Telemetry of any kind** — including crash telemetry. README's Privacy
  section is the project's stake in the ground.
- **macOS / Linux first-class support** — Avalonia is tracked but deferred.
- **App-store distribution (Microsoft Store)** — out of scope for v0.2; winget
  manifest could land in v0.3+.

---

## Open Questions

These genuinely block prioritization or implementation:

1. **rootAVD SHA**: A-03 needs a verified rootAVD revision to lock the pin.
   Smoke-test on API 35 + 36 Google Play AVDs is the gating step.
2. **rootAVD LISTONLY entry-point name**: `RootService.DryRunAsync` calls
   `bash rootAVD.sh ListAllAVDs`. Verify this matches newbit's current
   script — the README likely says `LISTONLY=1` as an env var; if so, change
   the implementation to set that env var instead.
3. **Magisk APK hash policy**: TOFU mode is shipping; should v0.2 ship with
   *one* known-good Magisk hash baked in (the latest as of release) so the
   manifest isn't empty? Or stay TOFU and expect users to populate?
4. **Icon design**: who designs / commissions the AEP icon? Pixel-art Android
   robot variant is the obvious shape; need a source.
5. **GitHub Actions billing**: SysAdminDoc org runners are blocked at
   allocation per the memory note. Does v0.2.0 release through the CI
   pipeline, or as a one-off local `dotnet publish` on the maintainer's
   desktop?
