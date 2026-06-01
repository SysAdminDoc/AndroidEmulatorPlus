# AndroidEmulatorPlus Research Report

This report is the canonical research summary. The full pass-3 research plan is
archived at `docs/archive/research/RESEARCH_FEATURE_PLAN.md`.

## Current Findings

- The active checklist is intentionally small: final v0.2.0 smoke/tagging,
  README screenshots, installer packaging, and the large Avalonia portability
  track remain in `ROADMAP.md`.
- Local build prerequisites were previously resolved on the audit VM: .NET 9,
  Java 21, Android SDK tooling, emulator images, and repository `global.json`.
- `dotnet build AndroidEmulatorPlus.sln -c Release` and
  `dotnet test AndroidEmulatorPlus.Tests\AndroidEmulatorPlus.Tests.csproj -c Release`
  were recorded as passing with 76 tests during the post-batch readiness pass.
- Release smoke remains host-dependent because the VMware guest cannot provide
  nested virtualization or Hyper-V acceleration for Android Emulator.

## Ecosystem Notes

- Android Studio Device Manager remains the closest first-party comparison;
  this project already covers many day-to-day AVD, snapshot, launch, and
  system-image flows outside the IDE.
- Genymotion Desktop is the closest commercial workflow comparison for sensor,
  GPS, battery, telephony, and network simulation controls.
- `scrcpy` remains an external companion rather than an embedded surface.
- SAI remains the reference pattern for split APK bundle installation behavior.
- Magisk Manager remains the reference product for module management and rooted
  emulator expectations.

## Archived Evidence

- The archived feature plan includes file inventory, source links, current
  product map, feature inventory, testing notes, and original tagged audit
  findings.
