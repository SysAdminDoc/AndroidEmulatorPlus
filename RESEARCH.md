# Research - AndroidEmulatorPlus

## Executive Summary

AndroidEmulatorPlus is a Windows-only .NET 9 WPF workbench for building, rooting, operating, and migrating data into Android Emulator AVDs without Android Studio. Verified from source and recent history: v0.2.2 is strongest in rooted emulator setup, Magisk module handling, app-data migration, SDK/AVD management, supply-chain checks for Magisk/cmdline-tools, and local-first privacy. The highest-value direction is to harden data-handling and download trust before adding more emulator breadth. Priority opportunities: close remote-tar cleanup gaps, preflight untrusted archive extraction, verify Frida/module downloads, add wireless-ADB/patch-level warnings, add SDK package update management, verify Velopack release feeds, add migration dry-run estimates, support Android Emulator peer-networking workflows, add generic file push/pull, and create a repeatable WPF UI smoke harness.

## Product Map

- Core workflows: install SDK/cmdline-tools; create/launch/duplicate/snapshot AVDs; patch ramdisk with rootAVD/Magisk; install modules/CA cert/Frida; migrate APKs and app/internal/external/OBB data; manage apps/debloat; configure AVD hardware; stream logcat; drive emulator console.
- User personas: Windows Android power user; mobile security tester; app tester needing rooted Google Play AVDs; IT/operator migrating app state; maintainer producing local release artifacts.
- Platforms and distribution: Windows 10/11 x64, .NET 9 WPF, MIT, local builds, Velopack-installed updates from GitHub Releases, no remote build workflows.
- Key integrations and data flows: `adb`, `emulator`, `sdkmanager`, `avdmanager`, `qemu-img`, Git Bash/rootAVD, GitHub Releases APIs for Magisk/Frida/modules, Google cmdline-tools download, local `%LOCALAPPDATA%\AndroidEmulatorPlus` cache/log/settings/transfer folders.

## Competitive Landscape

- Android Studio Device Manager: first-party AVD lifecycle, system image install, snapshots, and new emulator peer-to-peer networking. Learn from its automatic multi-AVD networking and drag/drop file affordances; avoid requiring the full IDE or hiding root/migration operations behind manual CLI work.
- Genymotion Desktop: strong device simulation surface across battery, GPS, network, camera, and motion sensors. Learn from a consolidated "virtual device controls" model; avoid account-tied/cloud-first flows that conflict with the local-first promise.
- BlueStacks/LDPlayer/MEmu/Nox: mature multi-instance launch, clone, sync, operation recording, and batch layout flows. Learn from instance orchestration and backup/restore ergonomics; avoid gaming-first keymapping/ads/macros as the product center.
- rootAVD plus Magisk: canonical route for rooting Google Play AVDs with Magisk. Keep pinning and smoke-validating the script; avoid relying on `master` or old bundled Magisk versions.
- Universal Android Debloater NG: best OSS reference for ADB-driven non-root debloat and package risk presentation. Learn from categorized presets and explicit backup warnings; avoid copying phone-wide debloat decisions blindly into emulator defaults.
- scrcpy: best companion for low-latency external mirroring/control. Keep launching it externally and supporting current flags; avoid fragile WPF embedding.
- Velopack: good fit for installer/update delivery. Learn from feed/channel concepts and early startup integration; add release-feed verification/rollback around the current `UpdateService`.
- Frida/Burp/mobile-pentest guides: validate AEP's CA cert and Frida automation as real security-tester demand. Add integrity and lifecycle controls; avoid turning the root tab into an unbounded exploit toolkit.

## Security, Privacy, and Reliability

- Verified bug/risk: `AndroidEmulatorPlus/Services/MigrationService.cs` creates remote tar files in `/sdcard` and only removes them on some success paths. Early returns after pull, validation, push, UID, or extract failures can leave private app data on the source phone or emulator.
- Verified bug/risk: `AndroidEmulatorPlus/Services/AppService.cs` pushes `/sdcard/aep-import-<pkg>.tar` and removes it only after extraction succeeds; failures before that leave imported private data staged on the emulator.
- Verified bug/risk: `AndroidEmulatorPlus/Services/AppService.cs` uses `ZipFile.ExtractToDirectory` for `.apks`/`.xapk`/`.apkm` and import ZIPs before a repository-level archive preflight. Existing tar/module validators are strong; ZIP extraction should get the same traversal, size, entry-count, and compression-ratio guardrails.
- Verified bug/risk: `AndroidEmulatorPlus/Services/FridaService.cs` downloads latest `frida-server` assets without digest/manifest verification, unlike the Magisk and cmdline-tools paths.
- Verified bug/risk: `AndroidEmulatorPlus/Services/MagiskService.cs` resolves latest GitHub release assets for modules and validates ZIP structure, but it does not verify asset digests or maintainer-pinned hashes.
- Verified gap: wireless debugging is supported, but there is no ADB trust dashboard showing transport type, platform-tools version, device patch level, or warnings for wireless-ADB risk. Android security bulletins and ADB release notes make that a live trust issue.
- Verified gap: Velopack update checks run in `AndroidEmulatorPlus/Services/UpdateService.cs`, but the app does not expose a preflight that verifies expected release assets/feed availability before enabling auto-update on a release.
- Verified gap: untrusted archive handling has tests for tar import and Magisk module zip validation, but not bundle ZIP extraction, import ZIP extraction, ZIP bombs, or remote cleanup on failure.

## Architecture Assessment

- Boundaries are mostly healthy: external tools route through services and `ProcessRunner`; view-models own UI state; XAML code-behind is small.
- `MigrationService` needs a cleanup abstraction for remote temp artifacts so every success, failure, and cancellation path deletes source and target staging files.
- `AppService` should centralize safe ZIP extraction instead of using raw `ZipFile.ExtractToDirectory` in multiple import/bundle flows.
- Download trust should be normalized across `RootService`, `FridaService`, and `MagiskService`: resolve asset, verify digest or pinned manifest, cache with versioned metadata, then install.
- `SdkmanagerService` lists and installs images, but does not manage installed package updates. Android Emulator/platform-tools release notes are now important enough to surface update status inside the app.
- UI accessibility is partially started with `AutomationProperties.Name`, but many buttons, list boxes, checkboxes, status text, and progress updates still lack stable automation IDs, help text, and live-region announcements. Existing roadmap item R-17 remains valid.
- Distribution docs and code now align on local releases, but release packaging needs a machine-checkable Velopack feed/artifact contract before updates are trusted by users.
- Test gaps with highest payoff: migration cleanup failure matrix, safe ZIP preflight, Frida/module hash verification, ADB trust-state parsing, SDK update parsing, UI smoke tab rendering, and Velopack release-feed validation.

## Rejected Ideas

- Full custom emulator fork or BlueStacks-style app player: commercial competitors prove demand, but it conflicts with the SDK/AVD/root workflow and would multiply maintenance.
- KernelSU integration now: KernelSU supports x86_64 in principle, but current project notes and upstream issues show emulator integration remains fragile; keep the manual note until a smoke-tested kernel path exists.
- Embedded scrcpy surface: scrcpy is excellent as an external companion; WPF embedding would add brittle SDL/window-hosting risk for little product gain.
- Cloud device farm integration: useful for teams, but conflicts with the no-telemetry/local-data promise and would require accounts/credentials.
- Reintroducing remote build workflows: the repository explicitly moved to local builds and release uploads; local artifact verification is the right direction.
- Gaming macro/keymapping as a primary feature: commercial emulators cover it well; AEP should only borrow repeatable operation patterns where they help testing and migration.

## Sources

Official Android:
- https://developer.android.com/studio/releases/emulator
- https://android-developers.googleblog.com/2026/05/whats-new-android-developer-tools.html
- https://developer.android.com/studio/run/emulator-commandline
- https://developer.android.com/studio/run/emulator-install-add-files
- https://developer.android.com/tools/adb
- https://developer.android.com/tools/releases/platform-tools
- https://developer.android.com/develop/sensors-and-location/sensors/sensors_overview
- https://source.android.com/docs/security/bulletin/2026/2026-06-01
- https://source.android.com/docs/security/bulletin/2026/2026-05-01

OSS and dependencies:
- https://gitlab.com/newbit/rootAVD
- https://github.com/topjohnwu/Magisk/releases
- https://github.com/tiann/KernelSU
- https://github.com/tiann/KernelSU/issues/2944
- https://github.com/Universal-Debloater-Alliance/universal-android-debloater-next-generation
- https://github.com/Genymobile/scrcpy/releases
- https://docs.velopack.io/integrating/overview
- https://docs.velopack.io/reference/cs/Velopack/UpdateManager
- https://github.com/advisories/GHSA-6c8g-7p36-r338

Commercial and adjacent:
- https://docs.genymotion.com/tools/desktop/genyshell/
- https://docs.genymotion.com/changelog/desktop/current/
- https://www.bluestacks.com/features/multi-instance.html
- https://www.bluestacks.com/features/multi-instance-sync.html
- https://www.ldplayer.net/blog/introduction-to-ldmultiplayer.html
- https://www.ldplayer.net/blog/introduction-to-synchronizer.html
- https://www.ldplayer.net/blog/how-to-use-operation-recorder.html
- https://www.memuplay.com/blog/set-up-multiple-instances-multi-memu.html
- https://www.bignox.com/blog/new-features-optimization-noxplayer-6-2-6-3/

Security testing:
- https://8ksec.io/rooting-an-android-emulator-for-mobile-security-testing/
- https://secra.es/en/blog/root-android-avd-burp-suite-pentesting
- https://github.com/frida/frida/releases

## Open Questions

- Which host will run the accelerated API 35/API 36 rooted smoke matrix that is blocked in `Roadmap_Blocked.md`?
- Should Frida/module trust use a strict curated manifest only, or allow trust-on-first-use with loud logging like unknown Magisk/cmdline-tools entries?
