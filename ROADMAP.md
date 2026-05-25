# Roadmap

Single source of truth for outstanding work. Shipped items live in
[CHANGELOG.md](CHANGELOG.md); deep evidence + verification plans for each open item
live in [RESEARCH_FEATURE_PLAN.md](RESEARCH_FEATURE_PLAN.md).

Tag legend:

- `R-NN` — original release-roadmap item
- `A-NN` — 2026-05-24 deep-audit pass
- `B-NN` — 2026-05-25 pass-2 audit
- `C-NN` — 2026-05-25 pass-3 audit (post autonomous-loop)
- Priorities: **P0** (correctness / release-blocking), **P1** (high value),
  **P2** (nice to have), **P3** (future / large bet)

Build constraint: this VMware VM has no .NET SDK; changes are best-effort and must
be `dotnet build`-verified on a host with the SDK before tagging a release.

## Phase 1 — Ship v0.2.0

- [x] **P0 C-01** (code side) — Version bumped to 0.2.0 across csproj, MainWindow,
  startup log, README badge, CHANGELOG. CHANGELOG `[0.2.0]` block carved.
- [ ] **P0 C-01-release** — Outstanding maintainer steps (requires real Android
  SDK + an emulator on a desktop):
  1. Populate `Resources/known-hashes.json` with verified Magisk +
     cmdline-tools SHA-256 entries (currently empty / placeholder).
  2. Lock `RootService.RootAvdPinnedRef` to a verified rootAVD SHA
     (closes A-03; verify `LISTONLY` entry-point too — see open Q).
  3. Run the AndroidEmulatorPlus.Tests suite on a desktop with .NET 9 SDK.
  4. `git tag v0.2.0 && git push --tags` to trigger the release workflow.
- [x] **P0 C-02** — `AppService.OrderBaseFirst` puts the literal `base.apk` (or the
  largest entry) first; splits follow. Replaces the ascending-size sort that
  put the base last.

## Phase 2 — Plug the loose ends

- [x] **P1 C-03** — `DownloadService` ctor reads `SettingsService.Current.HttpProxy`
  and wires it into `HttpClientHandler.Proxy` with `UseDefaultCredentials = true`.
  Applies on next launch (consistent with the theme picker).
- [x] **P1 C-04** — `aapt2 dump packagename` resolves the package id; the
  installed cert SHA (from `pm dump <pkg>`) is compared against the APK's
  signer SHA; mismatch raises a typed ConfirmDialog showing both SHAs.
  Skip-and-continue when aapt2 is missing or the package isn't installed.
- [x] **P1 C-05** — `MigrationService.AllowsBackupAsync` probes `pm dump` for each
  package after the phone list loads; flagged rows show a yellow ⚠ no-backup
  pill. The migration loop skips the data leg for them unless
  "Force-migrate no-backup apps" is set.
- [x] **P1 C-06** — `Assets/aep.ico` shipped (multi-resolution 16/24/32/48/64/128/256,
  Catppuccin Mocha base + Lavender ring + green Android-robot motif, generated
  via System.Drawing). csproj `<ApplicationIcon>` + `<Resource>` entry; MainWindow
  `Icon="Assets/aep.ico"`.

## Phase 3 — Polish + parity

- [x] **P2 C-07** — Magisk module manager (R-03). `MagiskService` drives
  `magisk --install-module` from a curated catalog (Shamiko / LSPosed /
  PlayIntegrityFork / Tricky Store / Zygisk Detach) or arbitrary local zip.
  Lists installed modules via `magisk module list` (fallback: walks
  `/data/adb/modules/`). Toggle-enabled and Remove (mark-on-reboot) flows.
  Surfaced via `Modules…` button on the Root tab.
- [x] **P2 C-08** — New tests: `OrderBaseFirstTests` (5 cases on the C-02
  reorder helper), `DuplicateAvdTests` (replays the duplicate contract against
  a temp `.android/avd` tree and verifies the ini rewrites + transient cleanup),
  `PresetServiceTests` (id-based merge + embedded JSON schema), and
  `AllowBackupParsingTests` (4 known `pm dump` shapes).
- [x] **P2 C-09** — New `ProcessRunner.RunWithStdinAsync` and `StreamAsync`
  helpers; refactored `SdkmanagerService.AcceptLicenses`/`Install`,
  `AvdService.CreateAsync`, `RootService.PatchAsync`/`DryRunAsync`,
  `AdbService.PairAsync`, and `ScrcpyService.Launch` through them. The two
  remaining `Process.Start` sites (`LogcatService.Start`,
  `ScreenRecordService.Start`) legitimately hold the Process for explicit
  Stop control and are documented in their headers.
- [x] **P2 C-10** — "Show welcome wizard…" button on the Settings dialog flips
  `HasSeenWizard=false` and re-opens the WelcomeDialog (DI services resolved
  from `App.Services`).
- [x] **P2 C-11** — `CacheDiagnosticsService.Changed` event fires after Clear*,
  Apps tab Export/Import; `MigrateViewModel` subscribes and re-measures on
  the UI thread.
- [x] **P2 C-12** — All 216 `StaticResource …Brush` references across Styles.xaml,
  MainWindow.xaml, and 17 view/dialog XAML files swept to `DynamicResource`.
  New `ThemeService.Apply(theme)` swaps `Application.Resources.MergedDictionaries[0]`
  in place; SettingsDialog calls it on Save. Theme now switches without restart.
- [ ] **P2 C-17** — README screenshots + landing image (one PNG per tab, Mocha + Latte).
- [x] **P2 R-03** — Covered by C-07 (above).

## Phase 4 — P3 polish & stretch

- [x] **P3 C-13** — Apps tab "Compute sizes" iterates the full `Apps` collection,
  not just `FilteredApps` — rows hidden by the filter no longer stay at "—".
- [x] **P3 C-14** — Welcome wizard hides completed step cards by default;
  "Show completed steps" toggle reveals them.
- [x] **P3 C-15** — Theme picker removed from the Install tab; Settings is the
  canonical home. `InstallViewModel` no longer holds `_settings`.
- [x] **P3 C-16** — "Auto-launch scrcpy after AVD boots" toggle on the Settings
  dialog persists `AutoScrcpy=true` in settings.json; `MainViewModel.OnDevicesChanged`
  fires scrcpy when a new emulator serial comes online.
- [x] **P3 C-18** — `SettingsService.Save` writes to `settings.json.tmp` then
  `File.Replace` into place, so a crash mid-write can't corrupt the file
  App.OnStartup reads.
- [x] **P3 C-19** (partial) — `BundleInstallerService` extracts the bundle
  install + apksigner verify pipeline out of `AppsViewModel`. The cert-mismatch
  prompt remains in the view-model via a callback so the service is UI-agnostic.
  AvdViewModel refactor deferred (its dialog plumbing already lives in
  per-dialog xaml.cs files).
- [ ] **P3 R-07** — Linux + macOS builds (Avalonia port).

## Pending external validation

- [ ] **P0 A-03** — rootAVD pin still resolves to `"master"`. Set to a verified
  SHA after smoke-test on API 35 + API 36 Google Play AVDs. Tracked together
  with C-01 (release prep).

## Open questions (block C-01)

1. **rootAVD SHA** — needs a verified revision (smoke-test on API 35 + API 36
   Google Play AVDs).
2. **rootAVD LISTONLY entry-point name** — `RootService.DryRunAsync` calls
   `bash rootAVD.sh ListAllAVDs`; needs validation against newbit's current
   script (the canonical form might be `LISTONLY=1` env var).
3. **Magisk hash policy** — ship empty TOFU table or bake in one known-good
   Magisk hash at release time?
4. **Icon design** — placeholder generated via System.Drawing shipped in
   `Assets/aep.ico`. Refine with a designer-provided source asset before
   v1.0.
5. **GitHub Actions billing** — SysAdminDoc runners blocked at allocation;
   does v0.2.0 release via CI or local `dotnet publish`?
