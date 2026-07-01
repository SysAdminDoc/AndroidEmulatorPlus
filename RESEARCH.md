# Research — AndroidEmulatorPlus

## Executive Summary
AndroidEmulatorPlus is a Windows-first .NET 9 WPF control surface for the official Android SDK and Emulator: SDK bootstrap/update, AVD create/launch/configure/snapshot, rootAVD/Magisk/Frida/CA setup, phone-to-emulator migration, app/debloat management, console controls, logcat, and media capture. Its strongest current shape is local-first trust tooling: pinned downloads, tar/zip safety checks, ADB trust diagnostics, and broad workflows without requiring Android Studio. Highest-value direction: make privileged and large-data operations auditable, retryable, and explainable. Priority opportunities: migration integrity manifests; SDK update transaction receipts; ADB Wi-Fi 2.0/mDNS pairing diagnostics; multi-device session selection; redacted support bundle export; Windows release provenance/signing preflight; post-restore validation reports; and security-tool compatibility handoffs.

## Product Map
- Core workflows: install/update SDK tooling; create/launch/tune AVDs; root/emulator security setup; migrate APK/internal/external/OBB data; manage installed apps/debloat; inspect logcat/console/media.
- User personas: Android developer avoiding full IDE overhead; QA engineer managing local test devices; mobile security analyst building rooted test environments; operator migrating app data from a phone to an emulator.
- Platforms and distribution: Windows x64 WPF (`net9.0-windows`), GitHub Releases plus Velopack, official Android SDK/Emulator/ADB, optional external scrcpy/Git/rootAVD/Magisk/Frida.
- Key integrations and data flows: local `%LOCALAPPDATA%\AndroidEmulatorPlus` logs/cache/settings; ADB USB/TCP transfers; SDK Manager package metadata; GitHub release metadata; no telemetry per `AndroidEmulatorPlus/Views/SettingsDialog.xaml:120`.

## Competitive Landscape
- Android Studio Device Manager/Emulator does official SDK/image management, Wi-Fi pairing by QR/pairing code, snapshots, console controls, and new peer-networking well. Learn: expose mDNS/server-status/QR pairing diagnostics and modern emulator networking without turning AEP into an IDE. Avoid: IDE-scale project coupling.
- Genymotion Desktop does polished device templates, sensor/camera/network widgets, cloud/local device integration, storage/update troubleshooting, and compatibility notes well. Learn: dedicated diagnostics/storage/update pages and widget-like environment controls. Avoid: account/cloud dependency for core local workflows.
- BlueStacks/LDPlayer gaming emulators do multi-instance create/clone/batch launch, backup/restore, macro replay, and synchronized actions well. Learn: batch instance UX and repeatable setup automation. Avoid: game-farming features, ads, telemetry, and opaque emulator stacks.
- rootAVD, Magisk, AlwaysTrustUserCerts, and Frida form the current rooted security-testing ecosystem. Learn: version/architecture/Android compatibility probes and explicit rollback logs for root/module operations. Avoid: blind latest-download mutation without a recorded compatibility result.
- scrcpy does lightweight screen/control/recording with no device app, no account, and low latency. Learn: keep scrcpy optional and discoverable with clear diagnostics. Avoid: bundling or reimplementing a media stack.
- STF, Appium Device Farm, Maestro, and LAMDA show that multi-device work needs a session model, active-device ownership, operation receipts, and automation handoffs. Learn: selectable device/session inventory and exportable environment descriptors. Avoid: server/fleet orchestration as a default local workflow.
- MobSF provides static/dynamic Android analysis, runtime data, network traffic, REST APIs, and CLI integration. Learn: produce handoff bundles for external analysis tools. Avoid: embedding a full mobile-security platform.
- redroid proves containerized Android is useful for cloud/automation, but its Linux kernel/binder prerequisites do not fit the current Windows WPF official-AVD baseline.

## Security, Privacy, and Reliability
- (Verified) `AndroidEmulatorPlus/Services/MigrationService.cs:116`, `:190`, and `:237` validate package names and tar imports, clean remote temp files, and delete local staging, but transfers are one-shot `adb pull/push` flows with no durable manifest, hash receipt, retry point, or post-restore validation.
- (Verified) `AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs:258`, `:267`, and `:273` collapse per-leg results into log lines and a summary. Users cannot later prove which APK/data/OBB legs succeeded or re-run only failed legs.
- (Verified) `AndroidEmulatorPlus/Services/SdkmanagerService.cs:72` installs selected packages and `AndroidEmulatorPlus/ViewModels/InstallViewModel.cs:142` refreshes afterward, but there is no before/after package inventory, rollback guidance, or update receipt.
- (Verified) `AndroidEmulatorPlus/Services/AdbService.cs:277` supports pairing-code Wi-Fi debugging, but the app does not surface Android's QR workflow, ADB Wi-Fi 2.0 readiness, `adb server-status`, or mDNS diagnostics described by current Android docs.
- (Verified) `AndroidEmulatorPlus/ViewModels/MainViewModel.cs:108` selects the first phone and first emulator from the device monitor. Multi-device and multi-AVD workflows need an explicit active source/target model.
- (Verified) `AndroidEmulatorPlus/Services/LogService.cs:26` writes local rolling logs and `AndroidEmulatorPlus/ViewModels/InstallViewModel.cs:174` shows crash diagnostics, but there is no redacted support bundle with versions, settings, command transcripts, SDK/package inventory, and device trust summaries.
- (Verified) `dotnet list package --vulnerable --include-transitive` reported no vulnerable NuGet packages for app or tests on 2026-07-01. `SharpCompress` remains a sensitive dependency class because CVE-2026-44788 affects older `WriteToDirectory()` usage; keep manual archive bounds checks and tests.
- (Likely) The existing Velopack release-feed roadmap item is correct; add release signing/provenance as a separate preflight because feed integrity and Windows installer trust are different failure modes.

## Architecture Assessment
- `Services/` boundaries are mostly clean and injectable; `ProcessRunner` centralizes process execution and cancellation. Keep new work in services plus thin view-model orchestration.
- Add `MigrationReceiptService` around `MigrationService` to write per-run JSON receipts with source/target serials, package scopes, remote temp paths, byte counts, SHA-256 hashes where feasible, failed legs, and retry commands.
- Split SDK update handling into parse/install/transaction responsibilities: `SdkmanagerService` keeps command execution while a new transaction layer snapshots `ListPackageInventoryAsync()` before/after and writes a human-readable rollback note.
- Extend `AdbService` with `ServerStatusAsync`, `MdnsTrackServicesAsync`, and typed parsing tests; use `MigrateViewModel` for a pairing doctor rather than raw text-only failure handling.
- Introduce an active-device/session model shared by `MainViewModel`, `MigrateViewModel`, `AppsViewModel`, `LogcatViewModel`, and `ConsoleViewModel`; current first-device selection is not enough for multi-device workflows.
- Add a `SupportBundleService` that collects local logs, crash data, app version, SDK paths, `adb version`, device diagnostics, selected settings, and package inventories with redaction for hostnames, IPs, paths, and pairing codes.
- Test gaps: parser tests for ADB server/mDNS output, SDK update receipt generation, migration receipt/retry selection, support-bundle redaction, and active-device selection. Existing roadmap already covers WPF UI smoke, accessibility, localization, and broader test expansion.
- Needs live validation: accelerated API 35/36 release smoke remains blocked in `Roadmap_Blocked.md`; no rendered emulator behavior was proven during this research-only pass.

## Rejected Ideas
- Full cloud device farm or browser streaming server (STF/LAMDA/redroid): useful ecosystem signal, but too heavy for the local-first Windows WPF app.
- Bundled scrcpy/WebRTC renderer: scrcpy already solves mirroring well as an optional external binary; bundling increases update and codec surface.
- KernelSU-first rooting: KernelSU targets GKI/kernel-integrated environments and containers more than official AVD ramdisk workflows; keep Magisk/rootAVD primary and revisit only when AVD compatibility changes.
- Game-emulator keymapping, farming, and multi-instance sync: BlueStacks/LDPlayer prove demand, but AEP should keep operation replay limited to test setup and diagnostics.
- Unrestricted plugin marketplace: high supply-chain risk for a rooted-device tool; signed preset/catalog updates already on the roadmap are the safer near-term extension point.
- Automatic CA/module installation by default: Android security testing needs explicit operator intent; keep CA/Magisk/Frida actions opt-in with compatibility checks.
- Dockerized Android as a first-class Windows workflow: redroid is valuable for Linux/container farms, but it requires kernel capabilities outside the official Windows AVD baseline.

## Sources
Official Android and platform:
- https://developer.android.com/studio/releases/emulator
- https://developer.android.com/tools/releases/platform-tools
- https://developer.android.com/tools/adb
- https://developer.android.com/studio/run/device
- https://developer.android.com/studio/run/emulator-commandline
- https://developer.android.com/studio/run/emulator-console
- https://developer.android.com/studio/command-line/sdkmanager
- https://developer.android.com/studio/run/managing-avds
- https://source.android.com/docs/security/bulletin/2026/2026-06-01

Competitors and comparable OSS:
- https://www.genymotion.com/product-desktop/
- https://docs.genymotion.com/changelog/desktop/current/
- https://www.bluestacks.com/features/multi-instance.html
- https://www.bluestacks.com/features/multi-instance-sync.html
- https://www.ldplayer.net/blog/introduction-to-ldmultiplayer.html
- https://www.ldplayer.net/blog/how-to-use-operation-recorder.html
- https://github.com/newbit1/rootAVD
- https://github.com/topjohnwu/Magisk
- https://github.com/NVISOsecurity/AlwaysTrustUserCerts
- https://frida.re/docs/android/
- https://github.com/genymobile/scrcpy
- https://github.com/mobsf/mobile-security-framework-mobsf
- https://github.com/AppiumTestDistribution/appium-device-farm
- https://github.com/mobile-dev-inc/maestro
- https://github.com/openstf/stf
- https://github.com/firerpa/lamda
- https://github.com/remote-android/redroid-doc

Dependencies and distribution:
- https://docs.velopack.io/integrating/overview
- https://docs.velopack.io/distributing/overview
- https://github.com/advisories/GHSA-6c8g-7p36-r338
- https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages

## Open Questions
None that block the prioritized roadmap additions.
