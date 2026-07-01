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
  pass locally (237 tests).
- Release smoke requiring accelerated API 35/API 36 AVDs is tracked in
  `Roadmap_Blocked.md` for a host with nested virtualization/Hyper-V available.

## Active Checklist

### Phase 2 - Documentation and Packaging


### Phase 3 - Platform Stretch

## Research-Driven Additions (2026-06-09)

Research evidence and rationale in `RESEARCH.md`.

### Tier 2 -- Medium Impact, Medium Effort

### Tier 3 -- Lower Impact or Higher Effort









### Test Coverage Expansion


## Planning Archive

- `docs/archive/research/RESEARCH_FEATURE_PLAN.md` preserves the pass-3 feature
  research and audit evidence that preceded this consolidated planning set.

## Research-Driven Additions

### P2 - Workflow Breadth and Maintainability



### P3 - Future Workflow Expansion






