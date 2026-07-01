# Roadmap

Single source of truth for outstanding work. Completed implementation history lives
in [COMPLETED.md](COMPLETED.md) and [CHANGELOG.md](CHANGELOG.md). Historical audit
evidence and feature research live in [RESEARCH_REPORT.md](RESEARCH_REPORT.md),
but that report no longer owns a separate checklist.

Tag legend:

- `C-NN` - 2026-05-25 pass-3 audit items
- `D-NN` - 2026-05-25 local reliability/build-readiness pass
- `R-NN` - original release-roadmap item
- Priorities: **P0** release-blocking, **P1** high value, **P2** polish,
  **P3** future/stretch

## Current Status

- Local .NET SDK gap is resolved for this VM. .NET SDK `9.0.314` is installed
  machine-wide via winget, with a matching user-local SDK also present in
  `C:\Users\Xray\.dotnet`.
- Java gap is resolved for Android SDK tooling. Microsoft OpenJDK 21 is installed
  at `C:\Program Files\Microsoft\jdk-21.0.11.10-hotspot`.
- Android SDK tooling is present in `C:\Users\Xray\.cache\android-sdk`; emulator
  `36.5.11`, API 35 Google Play x86_64, and API 36 Google Play x86_64 images are
  installed.
- `global.json` pins repository builds to .NET 9 with feature-band roll-forward.
- `dotnet build AndroidEmulatorPlus.sln -c Release` and
  `dotnet test AndroidEmulatorPlus.Tests\AndroidEmulatorPlus.Tests.csproj -c Release`
  pass locally (122 tests).
- Release smoke requiring accelerated API 35/API 36 AVDs is tracked in
  `Roadmap_Blocked.md` for a host with nested virtualization/Hyper-V available.

## Active Checklist

### Phase 2 - Documentation and Packaging

- [ ] **P2 C-17-screenshots** - Capture real README screenshots for each tab in
  Mocha/Latte themes after the app can be launched on a desktop test machine.
- [ ] **P3 C-20** - Add an installer path (MSIX or NSIS) for users who do not
  want a raw self-contained ZIP.

### Phase 3 - Platform Stretch

- [ ] **P3 R-07** - Linux and macOS builds via an Avalonia port. This is a major
  UI-platform migration, not a WPF patch; define acceptance criteria before
  implementation.

## Research-Driven Additions (2026-06-09)

Research evidence and rationale in `RESEARCH.md`.

### Tier 2 -- Medium Impact, Medium Effort

- [ ] **P2 R-17** - Accessibility pass. Add AutomationProperties.AutomationId and
  Name to all interactive controls. Add live-region announcements for the log
  panel and progress steps. Test with Narrator.

- [ ] **P2 R-18** - System image download progress. Parse `sdkmanager` percentage
  output and surface in a progress bar during "Browse online" installs.

- [ ] **P2 R-19** - Toast notifications for long-running ops. Fire a Windows toast
  when root, migrate, or system-image install completes so the user can Alt-Tab
  back.

- [ ] **P2 R-20** - Emulator performance card. Surface `adb shell dumpsys gfxinfo`
  frame stats or CPU/memory metrics in a lightweight card on the Configure tab.

- [ ] **P2 R-21** - Headless batch mode. Add a CLI entry point for CI/CD use:
  `--headless --create-avd <name> --root --install-apps <apks>`.

- [ ] **P2 R-22** - Multi-select migration columns. Add per-package checkboxes:
  APK / Internal Data / External Data / OBB to give granular transfer control.

### Tier 3 -- Lower Impact or Higher Effort

- [ ] **P3 R-25** - MSIX packaging. Windows App Certification Kit compliant
  installer with auto-update via MSIX AppInstaller (alternative to Velopack).


- [ ] **P3 R-27** - Bidirectional clipboard sync. Background polling between
  host and emulator clipboard (currently manual push/pull).

- [ ] **P3 R-28** - Multi-display launch. Pass `--multi-display` flag for
  foldable/multi-screen testing scenarios.

- [ ] **P3 R-29** - Sensor simulation (accelerometer, gyroscope). Extend the
  Console tab with motion sensor controls via `adb emu sensor set`.

- [ ] **P3 R-30** - AVD template/profile system. Save a complete AVD config
  (image + root + modules + debloat + config.ini) as a reusable JSON template.


- [ ] **P3 R-32** - Localization framework. Extract all UI strings to resource
  files for future i18n.


- [ ] **P3 R-34** - ARM64 Windows build. Add `win-arm64` RID to CI publish
  matrix. Blocked on Android emulator ARM64 host support maturity.

### Test Coverage Expansion

- [ ] **P2 R-35** - Unit tests for DeviceMonitor polling, MigrationService
  transfer pipeline (mock adb), EmulatorService lifecycle, ThemeService swap,
  SettingsService normalize, ConfigService resize, and SnapshotService CRUD.

## Planning Archive

- `docs/archive/research/RESEARCH_FEATURE_PLAN.md` preserves the pass-3 feature
  research and audit evidence that preceded this consolidated planning set.

## Research-Driven Additions

### P0 - Data Safety and Trust

### P1 - Reliability and Release Readiness

- [ ] P1 - Validate Velopack release feed and artifact contract
  Why: Auto-update exists, but releases need a local preflight proving `releases.win.json` and expected Velopack assets are present before users enable update checks.
  Evidence: `AndroidEmulatorPlus/Services/UpdateService.cs`; Velopack UpdateManager/GitHub source docs; README release guidance.
  Touches: `UpdateService`, `SettingsDialog`, release packaging script or local release checklist, update tests.
  Acceptance: Settings update panel reports feed health, current channel/version, missing-assets diagnostics, and refuses restart/apply when the release feed is incomplete or mismatched.
  Complexity: M

- [ ] P1 - Add migration dry-run with storage and root feasibility estimates
  Why: Large migrations can take minutes and consume GBs; users need a preflight showing APK count, internal/external/OBB size estimates, root requirements, `allowBackup=false`, and free-space risk.
  Evidence: `AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs`; `AndroidEmulatorPlus/Services/MigrationService.cs`; Universal Android Debloater warning-first UX.
  Touches: `MigrationService`, `AdbService`, `MigrateViewModel`, `MigrateView.xaml`, migration tests.
  Acceptance: Before Start Migration, a dry-run summary lists selected package scopes, estimated transfer size, source/target root status, no-backup rows, and blocks obvious no-space/no-root failures.
  Complexity: L

### P2 - Workflow Breadth and Maintainability

- [ ] P2 - Support Android Emulator peer-networking workflows
  Why: Recent Android Emulator releases add peer-to-peer virtual networking; AEP can make multi-AVD app testing easier without requiring Android Studio.
  Evidence: Android Emulator release notes; Android developer tools 2026 updates; existing multi-AVD launch/process tracking.
  Touches: `EmulatorService`, `AvdViewModel`, `AvdView.xaml`, `ConsoleService`, networking tests.
  Acceptance: Users can launch two or more AVDs into a documented peer-networking scenario, view assigned serials/addresses, and copy/test connectivity commands from the UI.
  Complexity: L

- [ ] P2 - Add generic file push/pull manager for emulator storage
  Why: Android Studio and commercial emulators expose drag/drop file workflows; AEP only handles APK/bundle/media capture, not arbitrary `/sdcard/Download` transfer.
  Evidence: Android emulator file install/add docs; `AndroidEmulatorPlus/Services/AdbService.cs`; Apps tab drag/drop implementation.
  Touches: `AdbService`, `AppsViewModel` or new file-transfer view-model, `AppsView.xaml`, log/progress UI.
  Acceptance: Users can drag files to push into `/sdcard/Download`, pull selected remote files/folders, see progress, and open the local/remote destination guidance without using raw adb.
  Complexity: M

- [ ] P2 - Create repeatable WPF UI smoke harness
  Why: README screenshots and accessibility checks remain manual; a harness should launch the app, visit every tab/dialog, and catch blank/clipped XAML before release.
  Evidence: Existing C-17-screenshots and R-17 accessibility items; WPF surface in `MainWindow.xaml` and `Views/*.xaml`.
  Touches: new UI test project or test helpers, `AndroidEmulatorPlus.Tests`, screenshot capture script, release checklist.
  Acceptance: One local command opens the app in a controlled profile, captures all major tabs in Mocha and Latte, verifies nonblank rendered content, and stores screenshots for README review.
  Complexity: L

- [ ] P2 - Plan .NET/test dependency modernization after v0.2.x
  Why: NuGet shows newer Microsoft.Extensions.DependencyInjection and test stack releases; modernization should be deliberate because WPF .NET 10 changes XAML capabilities and package baselines.
  Evidence: `dotnet list package --outdated`; `AndroidEmulatorPlus/AndroidEmulatorPlus.csproj`; `AndroidEmulatorPlus.Tests/AndroidEmulatorPlus.Tests.csproj`; stack note on WPF Grid shorthand.
  Touches: `global.json`, app/test csproj files, build/test docs, XAML build verification.
  Acceptance: Dependency bump branchless pass documents target framework choice, updates packages together, runs build/test/UI smoke, and only adopts .NET 10 WPF XAML shorthand after the TFM migration.
  Complexity: M

### P3 - Future Workflow Expansion

- [ ] P3 - Add operation replay for repeatable emulator setup
  Why: Commercial multi-instance tools expose operation recording; AEP can use a narrower version for test setup without becoming a gaming macro tool.
  Evidence: LDPlayer operation recorder; BlueStacks multi-instance sync; existing Console and AVD launch actions.
  Touches: command/event model around `ConsoleService`, `AvdViewModel`, `RootViewModel`, `MigrateViewModel`, settings/preset storage.
  Acceptance: Users can save a named sequence of AEP-native actions such as launch AVD, apply network profile, install APK, push file, and run console command, then replay it with logging and cancellation.
  Complexity: XL

- [ ] P3 - Add signed preset/catalog update channel
  Why: Debloat presets, network profiles, Magisk modules, and hash manifests will age faster than app releases.
  Evidence: `Resources/bloat-presets.json`; `Resources/network-profiles.json`; `Resources/magisk-modules.json`; `Resources/known-hashes.json`.
  Touches: `PresetService`, `NetworkProfileService`, `MagiskService`, `HashVerificationService`, Settings update UI.
  Acceptance: Maintainer-published catalog updates are signature-verified, previewed, reversible, and stored separately from user overrides.
  Complexity: XL

### P1 - Research Follow-up: Data Safety and Release Trust

- [ ] P1 — Add migration integrity receipts and retryable transfer staging
  Why: Migration currently validates tar safety and cleans temp files, but large `adb pull/push` legs have no durable manifest, hash receipt, or failed-leg retry target.
  Evidence: `AndroidEmulatorPlus/Services/MigrationService.cs:116`; `AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs:258`; Android `adb` docs; LDPlayer backup/restore baseline.
  Touches: `MigrationService`, `MigrateViewModel`, `AdbService`, `CacheDiagnosticsService`, migration tests.
  Acceptance: Each migration run writes a local receipt with source/target serials, selected scopes, per-leg byte counts, hashes where feasible, remote temp cleanup status, failed legs, and a UI action to retry only failed legs.
  Complexity: L

- [ ] P1 — Add SDK update transaction receipts and rollback guidance
  Why: SDK package updates are now discoverable and installable, but users need before/after inventory and recovery instructions when emulator/platform-tools/system-image updates regress.
  Evidence: `AndroidEmulatorPlus/Services/SdkmanagerService.cs:72`; `AndroidEmulatorPlus/ViewModels/InstallViewModel.cs:142`; Android SDK Manager docs.
  Touches: `SdkmanagerService`, `InstallViewModel`, `InstallView.xaml`, `LogService`, SDK manager tests.
  Acceptance: Updating SDK packages records pre/post package inventories, changed package versions, command output location, and a rollback/reinstall note visible from the Install tab and logs.
  Complexity: M

- [ ] P1 — Add Windows release provenance and signing preflight
  Why: Existing Velopack feed validation covers update discovery, but install trust also needs a local check for artifact hashes, GitHub release attachment parity, and Authenticode/signing status.
  Evidence: `AndroidEmulatorPlus/Services/UpdateService.cs:23`; README release guidance; Velopack distributing docs; Windows installer trust requirements.
  Touches: release packaging script/checklist, README release section, `UpdateService` diagnostics, local release tests.
  Acceptance: One local release check reports app version parity, Velopack assets/feed parity, SHA-256 hashes, Authenticode status for EXE/MSI artifacts when a cert is configured, and blocks publication on mismatches.
  Complexity: M

### P2 - Research Follow-up: Device Diagnostics and Operator Workflows

- [ ] P2 — Add ADB Wi-Fi 2.0, mDNS, and QR pairing diagnostics
  Why: Pairing-code support exists, but current Android docs add QR pairing, ADB Wi-Fi 2.0, `adb server-status`, and mDNS checks that explain most wireless-debugging failures.
  Evidence: `AndroidEmulatorPlus/Services/AdbService.cs:277`; `AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs:310`; Android ADB wireless-debugging docs; Stack Overflow adb-pair failure reports.
  Touches: `AdbService`, `MigrateViewModel`, `MigrateView.xaml`, `InstallViewModel`, ADB parser tests.
  Acceptance: The Migrate tab exposes a pairing doctor that shows platform-tools version, ADB Wi-Fi 2.0 readiness, mDNS status, detected TLS services, QR/pairing-code guidance, and safe restart-server actions.
  Complexity: M

- [ ] P2 — Add explicit multi-device session selector
  Why: The shell currently picks the first phone and first emulator, which is ambiguous once users connect multiple phones, wireless transports, or several AVDs.
  Evidence: `AndroidEmulatorPlus/ViewModels/MainViewModel.cs:108`; Appium Device Farm session model; STF remote-device inventory; Android Emulator peer-networking docs.
  Touches: `DeviceMonitor`, `MainViewModel`, `MigrateViewModel`, `AppsViewModel`, `LogcatViewModel`, `ConsoleViewModel`, main window UI.
  Acceptance: Users can choose active phone/source and emulator/target from all `adb devices -l` entries, see transport/API/security-patch status for each, and every tab uses the selected session consistently.
  Complexity: L

- [ ] P2 — Add redacted diagnostics support bundle export
  Why: Logs and crash details are local, but issue reports need a single redacted bundle containing versions, SDK/tool paths, device trust summaries, settings, cache size, and recent command output.
  Evidence: `AndroidEmulatorPlus/Services/LogService.cs:26`; `AndroidEmulatorPlus/ViewModels/InstallViewModel.cs:174`; Genymotion troubleshooting pages; MobSF/Appium diagnostic-heavy workflows.
  Touches: new `SupportBundleService`, `InstallViewModel`, `InstallView.xaml`, `SettingsService`, redaction tests.
  Acceptance: The Install tab exports a ZIP bundle with logs, crash.log, app/version info, SDK and package inventory, device diagnostics, and redacted host paths/IPs/pairing codes; tests cover redaction.
  Complexity: M

- [ ] P2 — Add post-restore validation report for migrated packages
  Why: A transfer can report success while the restored app still rejects data, lacks permissions, or fails on launch; users need a validation pass after restore.
  Evidence: `AndroidEmulatorPlus/Services/MigrationService.cs:116`; `AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs:258`; Android backup/allowBackup behavior; MobSF dynamic-analysis smoke workflows.
  Touches: `MigrationService`, `AdbService`, `MigrateViewModel`, `LogcatService`, migration tests.
  Acceptance: After migration, AEP can optionally force-stop/launch each restored package, capture install/data size/signature/permission status and filtered logcat errors, then attach that report to the migration receipt.
  Complexity: L

### P3 - Research Follow-up: External Tool Handoffs

- [ ] P3 — Add Appium/Maestro environment handoff export
  Why: The app already prepares AVDs, apps, root, network, and files; exporting a descriptor lets automation tools reuse that local environment without AEP becoming a test runner.
  Evidence: Maestro YAML flow model; Appium Device Farm device/session docs; `AndroidEmulatorPlus/Services/AdbService.cs`; `AndroidEmulatorPlus/ViewModels/AvdViewModel.cs`.
  Touches: `AvdService`, `AdbService`, `SettingsService`, export dialog/view-model, README automation notes.
  Acceptance: Users can export selected emulator/device metadata, ADB serial, installed package list, app path hints, proxy/network profile, and sample Maestro/Appium command snippets as a local JSON/YAML handoff.
  Complexity: M
