# Roadmap

Single source of truth for outstanding work. Completed implementation history lives
in [CHANGELOG.md](CHANGELOG.md). Historical audit evidence and feature research live
in [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md), but that file no longer
owns a separate checklist.

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
- `dotnet build AndroidEmulatorPlus.sln -c Release` and `dotnet test` pass
  locally.
- Release smoke is blocked on this VMware guest: `emulator -accel-check` reports
  "Virtualization extension is not supported." The remaining C-01 smoke needs a
  host with nested virtualization/Hyper-V acceleration available.

## Active Checklist

### Phase 1 - Ship v0.2.0

- [ ] **P0 C-01-release** - Final maintainer release steps on a desktop with
  a real emulator:
  1. Smoke-test the Magisk-v30.7 root flow on API 35 and API 36 Google Play AVDs.
  2. Re-run the full `AndroidEmulatorPlus.Tests` suite on the smoke host.
  3. `git tag v0.2.0 && git push --tags` to trigger the release workflow.

### Phase 2 - Documentation and Packaging

- [ ] **P2 C-17-screenshots** - Capture real README screenshots for each tab in
  Mocha/Latte themes after the app can be launched on a desktop test machine.
- [ ] **P3 C-20** - Add an installer path (MSIX or NSIS) for users who do not
  want a raw self-contained ZIP.

### Phase 3 - Platform Stretch

- [ ] **P3 R-07** - Linux and macOS builds via an Avalonia port. This is a major
  UI-platform migration, not a WPF patch; define acceptance criteria before
  implementation.

## Recently Completed

- 2026-05-25: v0.2.0 code-side release prep, rootAVD pinning, known-hash
  manifest, bundle base-APK ordering, proxy wiring, signer mismatch warning,
  no-backup migration preflight, application icon, Magisk module manager, test
  coverage, process-runner consolidation, live theme switching, welcome wizard
  improvements, auto-scrcpy, atomic settings writes, and bundle installer
  extraction. See [CHANGELOG.md](CHANGELOG.md) for details.
