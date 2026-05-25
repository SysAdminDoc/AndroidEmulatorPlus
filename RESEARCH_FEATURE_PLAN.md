# Project Research and Feature Plan — Pass 2 (2026-05-25)

Companion research document to [ROADMAP.md](ROADMAP.md). Cross-references the existing
R-NN / A-NN tags and adds B-NN tags for new items surfaced in this audit. Where an item
duplicates ROADMAP wording, the entry here adds evidence, verification plan, and
acceptance criteria — it does **not** restate the action.

Build constraint: the VMware VM this repo lives on has **no .NET SDK**, so every code
change is best-effort and must be `dotnet build`-verified on a host with the SDK before
release. The user already has this documented; nothing in this plan assumes local build.

---

## Executive Summary

AndroidEmulatorPlus collapses a ~30-step CLI workflow (SDK install → AVD create → Magisk
root → phone-to-emulator data migration → debloat → tune) into a single WPF app. The
v0.1.0 release is small (12 services, 7 view-models, 6 views) but covers the full
loop, and the post-v0.1.0 audit pass already shipped 13 of the 41 audit items
(`Unreleased` block in [CHANGELOG.md](CHANGELOG.md)). The product's strongest shape is
**"one Windows EXE that owns the whole rooted-emulator-with-real-data workflow"** —
something Android Studio Device Manager, Genymotion, BlueStacks, and rootAVD itself
each only partially provide.

Highest-value next moves, in priority order:

1. **Verify a real rootAVD SHA and lock the pin** — finishes the supply-chain hardening
   half-started in [RootService.cs:12](AndroidEmulatorPlus/Services/RootService.cs#L12).
   (ROADMAP **A-03**, evidence: pinned constant currently still reads `"master"`.)
2. **SHA-256 verify Magisk APK + cmdline-tools ZIP** before use (ROADMAP **A-04**, P0).
3. **Fix the `NotBoolToVisibilityConverter` mis-binding on `IsEnabled`** — Download
   Cmdline Tools and Root buttons are never actually disabled while busy (new **B-01**,
   evidence: [InstallView.xaml:83](AndroidEmulatorPlus/Views/InstallView.xaml#L83),
   [RootView.xaml:25](AndroidEmulatorPlus/Views/RootView.xaml#L25)).
4. **Ship the rename popup that A-25's backend is waiting on** — the command exists,
   the XAML doesn't (ROADMAP **A-25**).
5. **Cancel button + transfer-cache size indicator** during long ops (ROADMAP **A-01
   cancel** + **A-05**).
6. **`.xapk` / `.apks` extractor** — the file-dialog filter and the drag-drop list
   advertise both, but `adb install` will reject them (new **B-02**).
7. **Snapshot manager** — Studio parity, low-risk read of `snapshots/` (ROADMAP **R-06**).
8. **First-launch wizard** that chains Install → Create AVD → Root → Migrate
   (ROADMAP **R-02**).
9. **Logcat viewer** (ROADMAP **A-15**) — the missing piece for diagnosing failed
   migrations from inside the app.
10. **GitHub Actions release build** (ROADMAP **A-36**) — without CI the only artifact
    today is `bin/Release/.../AndroidEmulatorPlus.exe` on the author's machine.

The product should stay scoped to **"manage emulator + migrate from phone, on Windows."**
Linux/macOS ports (R-07), scrcpy embedding (A-39), and full Wear/TV profile UIs (A-38)
should remain P3 until the P0/P1 hardening lands.

---

## Evidence Reviewed

### Local files inspected (full read)

- [README.md](README.md), [CHANGELOG.md](CHANGELOG.md), [ROADMAP.md](ROADMAP.md),
  [CLAUDE.md](CLAUDE.md), [LICENSE](LICENSE), [.gitignore](.gitignore)
- [AndroidEmulatorPlus.csproj](AndroidEmulatorPlus/AndroidEmulatorPlus.csproj),
  [App.xaml.cs](AndroidEmulatorPlus/App.xaml.cs),
  [app.manifest](AndroidEmulatorPlus/app.manifest),
  [MainWindow.xaml](AndroidEmulatorPlus/MainWindow.xaml),
  [MainWindow.xaml.cs](AndroidEmulatorPlus/MainWindow.xaml.cs)
- All 12 services in [Services/](AndroidEmulatorPlus/Services/)
- All 7 view-models in [ViewModels/](AndroidEmulatorPlus/ViewModels/)
- All 6 views + code-behinds in [Views/](AndroidEmulatorPlus/Views/) plus all 4 converters
- All 3 models in [Models/](AndroidEmulatorPlus/Models/)
- [Themes/Dark.xaml](AndroidEmulatorPlus/Themes/Dark.xaml) (full 292 lines)
- [Helpers/ProcessRunner.cs](AndroidEmulatorPlus/Helpers/ProcessRunner.cs)

### Git history range

`1b16465..aa213c6` (8 commits, branch `main` only, single remote
`SysAdminDoc/AndroidEmulatorPlus`). Uncommitted working-copy edits (Magisk asset
filter, `LatestCmdlineToolsWindowsUrlAsync`, accel-check on Install panel) inspected
via `git diff`. ROADMAP/CHANGELOG `[x]` entries verified against actual source.

### External references (primary docs only)

- Android SDK command-line tools download page — `https://developer.android.com/studio`
  (scraped at runtime by the new `LatestCmdlineToolsWindowsUrlAsync`).
- `emulator -accel-check` — documented at
  `https://developer.android.com/studio/run/emulator-acceleration`. Empirically prints
  one-line verdicts like "Hyper-V is enabled" or "HAXM is not installed".
- `avdmanager create avd / move avd / delete avd` syntax — `cmdline-tools/latest/bin/`.
- `adb pair host:port` + `adb connect host:port` (introduced API 30) — replaces USB-only
  debugging on modern phones.
- Magisk releases JSON — `https://api.github.com/repos/topjohnwu/Magisk/releases/latest`.
- rootAVD — `https://gitlab.com/newbit/rootAVD`.
- Catppuccin Mocha palette + Latte equivalents — `https://github.com/catppuccin/catppuccin`.

### Verification gaps

- **Cannot build locally on this VM** — `dotnet` SDK absent, so every "verify" step
  below assumes the user runs it on a desktop with .NET 9 SDK installed.
- **Cannot exercise the runtime flow** — no emulator/phone attached to the agent's
  environment; behavior claims about `getprop ro.kernel.qemu.avd_name`, `tar` flavor
  on phones, and `magisk --sqlite` are taken from primary docs / project notes, not
  observed at runtime here.
- **rootAVD SHA discovery** needs newbit's GitLab to be reachable and a manual
  smoke-test on a real AVD; not performed in this pass.

---

## Current Product Map

### Core workflow (numbered in the sidebar)

1. **Install / SDK** — detect SDK, install cmdline-tools, accel-check, surface crash
   diagnostics.
2. **AVDs** — list AVDs, create new from any locally-installed system image, launch /
   cold-boot / stop / delete / overflow menu (show on disk, desktop shortcut).
3. **Root** — clone rootAVD, fetch latest Magisk, patch ramdisk, persist Magisk shell
   policy, un-root via stock-ramdisk restore.
4. **Migrate from Phone** — list phone packages, transfer APK + `/data/data/<pkg>` +
   `/sdcard/Android/data/<pkg>` to emulator with UID remap + `restorecon`.
5. **Apps / Debloat** — list emulator packages, bulk uninstall, batch APK install
   (file picker + drag-drop), Google/Samsung bloat presets.
6. **Configure** — RAM/cores/disk sliders, screen W/H/DPI text-boxes, fastboot toggles,
   qcow2 resize (optionally with overlay wipe).

### Cross-cutting

- **Top bar** — SDK pill, phone pill, emulator pill, Screenshot button.
- **Device monitor** — 3-second `adb devices` poll on a background task
  ([DeviceMonitor.cs:57](AndroidEmulatorPlus/Services/DeviceMonitor.cs#L57)).
- **Log panel** — 2000-entry ring + rolling daily file at
  `%LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log`, pruned after 14 days.
- **Theme** — Catppuccin Mocha only; corner radii 4/6/8 (strictly no pills, per
  CLAUDE.md and [Dark.xaml:285-289](AndroidEmulatorPlus/Themes/Dark.xaml#L285-L289)).

### Platforms / distribution

- Windows 10/11 x64, .NET 9 WPF (`net9.0-windows`, x64 explicit).
- `asInvoker` manifest (no elevation), per-monitor v2 DPI, long-path aware.
- Distribution: source build only. No GitHub Releases, no installer, no Actions CI.
- Repo public; one remote (`SysAdminDoc/AndroidEmulatorPlus`).

### Storage / data flows

- AVDs: `%USERPROFILE%\.android\avd\<name>.avd\config.ini` (parsed by
  [AvdService.ParseIni](AndroidEmulatorPlus/Services/AvdService.cs#L41)).
- SDK probe order: `$ANDROID_HOME` → `$ANDROID_SDK_ROOT` → `%LOCALAPPDATA%\Android\Sdk`
  → `%USERPROFILE%\AppData\Local\Android\Sdk` → `C:\Android\Sdk` →
  `%USERPROFILE%\Android\Sdk` ([SdkLocator.cs:61-74](AndroidEmulatorPlus/Services/SdkLocator.cs#L61-L74)).
- App cache: `%LOCALAPPDATA%\AndroidEmulatorPlus\cache` (rootAVD clone, Magisk.apk).
- Migration scratch: `%LOCALAPPDATA%\AndroidEmulatorPlus\transfer\` — can grow to
  many GB; finally-block cleanup is best-effort.
- Diagnostics: `…\crash.log` + `…\logs\app-YYYYMMDD.log`.
- Network: only `dl.google.com`, `api.github.com/topjohnwu/Magisk`, `gitlab.com/newbit`,
  and now `developer.android.com/studio` (cmdline-tools URL scrape).

### User personas (inferred from README + ROADMAP)

- **Power user replicating their phone** — wants daily-driver app set + persistent
  logins on a desktop emulator without re-pairing every app.
- **Sideloading enthusiast** — installs LSPosed/Shamiko, needs a rooted Play-Store-
  capable AVD that doesn't trip Play Integrity.
- **QA / developer on Windows** — needs an alternative to the 1.2 GB Android Studio
  install when they only need the AVD lifecycle.

---

## Feature Inventory

Format: feature → entry point → main code → maturity → coverage → improvement hooks.

### ① Install / SDK

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| SDK detection | Auto on startup + Refresh button | [SdkLocator.cs](AndroidEmulatorPlus/Services/SdkLocator.cs) | Complete | None / [README.md](README.md) | Probe winget Android SDK package locations; show winget install hint when missing |
| Cmdline-tools auto-installer | "Download command-line tools" | [InstallViewModel.cs:117](AndroidEmulatorPlus/ViewModels/InstallViewModel.cs#L117) | Partial — no SHA-256, no progress UI | None | A-04 SHA-256, B-03 progress bar |
| Crash-log surface | Auto-shown on Install tab | [InstallViewModel.cs:64](AndroidEmulatorPlus/ViewModels/InstallViewModel.cs#L64) | Complete | None | Add per-entry stack-trace preview |
| accel-check | "Run accel-check" | [EmulatorService.cs:41](AndroidEmulatorPlus/Services/EmulatorService.cs#L41) | Partial — no remediation links | None | ROADMAP **A-24** remediation UI |
| Open SDK folder / Studio page | Buttons | InstallViewModel | Complete | None | — |

### ② AVDs

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| List AVDs | Auto on tab nav | [AvdService.List()](AndroidEmulatorPlus/Services/AvdService.cs#L19) | Complete | None | Show running pid + uptime |
| Create AVD | "Create" button | [AvdService.CreateAsync](AndroidEmulatorPlus/Services/AvdService.cs#L96) | Partial — only lists already-installed images | None | ROADMAP **R-01** sdkmanager picker |
| Launch / Cold boot | Per-card buttons | [EmulatorService.Launch](AndroidEmulatorPlus/Services/EmulatorService.cs#L18) | Partial — only 2 flags | None | ROADMAP **A-23**, **A-33** multi-AVD tracking |
| Stop | Per-card button on running AVDs | [AvdViewModel.StopAsync](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L112) | Complete | None | — |
| Running indicator | Auto via device monitor | [AvdViewModel.RefreshRunningStateAsync](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L65) | Complete | None | Cache `avd_name` per serial to avoid re-shell on every poll |
| Show on disk | Overflow menu | [AvdViewModel.ShowOnDisk](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L124) | Complete | None | — |
| Desktop shortcut | Overflow menu | [AvdService.CreateDesktopShortcut](AndroidEmulatorPlus/Services/AvdService.cs#L169) | Complete | None | Add Start-Menu shortcut option |
| Delete AVD | Overflow menu | [AvdService.DeleteAsync](AndroidEmulatorPlus/Services/AvdService.cs#L144) | Partial — no confirmation dialog | None | Confirm-before-delete; current behavior risks accidental loss |
| **Rename AVD** | **Backend only — no UI** | [AvdService.RenameAsync](AndroidEmulatorPlus/Services/AvdService.cs#L152), [AvdViewModel.RenameAsync](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L144), `RenameTarget` property | **Hidden / dead-bound** | None | ROADMAP **A-25** — XAML popup is the only missing piece |
| Duplicate AVD | Not implemented | — | Missing | — | ROADMAP **A-26** |

### ③ Root

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| rootAVD clone | Auto on "Root" | [RootService.EnsureRootAvdAsync](AndroidEmulatorPlus/Services/RootService.cs#L37) | Partial — pin constant defaults to `"master"` | None | ROADMAP **A-03** — record a verified SHA |
| Magisk download | Auto on "Root" | [DownloadService.LatestMagiskAsync](AndroidEmulatorPlus/Services/DownloadService.cs#L49) | Complete (debug/stub now filtered) | None | ROADMAP **A-04** SHA-256 verify |
| Patch ramdisk | "Root with Latest Magisk" | [RootService.PatchAsync](AndroidEmulatorPlus/Services/RootService.cs#L102) | Partial — 10 min timeout but no cancel button | None | ROADMAP **A-01** cancel button |
| Verify + persist shell policy | "Verify + Persist Policy" | [RootService.PersistShellPolicyAsync](AndroidEmulatorPlus/Services/RootService.cs#L174) | Complete | None | Surface DB write failure to UI |
| Un-Root | "Un-Root (Restore Ramdisk)" | [RootService.RestoreRamdisk](AndroidEmulatorPlus/Services/RootService.cs#L193) | Partial — destructive, no confirm | None | Add confirm dialog |
| Module manager | Not implemented | — | Missing | — | ROADMAP **R-03** |

### ④ Migrate from Phone

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| Phone package list | Auto on Refresh | [MigrateViewModel.RefreshAsync](AndroidEmulatorPlus/ViewModels/MigrateViewModel.cs#L38) | Partial — `-3` only | None | ROADMAP **A-18** |
| APK transfer | "Start Migration" | [MigrationService.TransferApkAsync](AndroidEmulatorPlus/Services/MigrationService.cs#L26) | Complete | None | Bandwidth-aware progress (R-09) |
| Internal data tar | "Start Migration" | [MigrationService.TransferInternalDataAsync](AndroidEmulatorPlus/Services/MigrationService.cs#L55) | Partial — `--exclude=` may fail on old toybox | None | ROADMAP **A-29**, **A-30** |
| External data tar | "Start Migration" | [MigrationService.TransferExternalDataAsync](AndroidEmulatorPlus/Services/MigrationService.cs#L104) | Complete | None | OBB pass (R-04) |
| `allowBackup=false` warning | Not implemented | — | Missing | — | ROADMAP **A-19** |
| Migration cache cleanup | Best-effort `finally` | [MigrationService.cs:51](AndroidEmulatorPlus/Services/MigrationService.cs#L51) | Risky — orphaned tarballs survive crashes | None | ROADMAP **A-05** |
| **Dead `TransferOptions` record** | — | [MigrationService.cs:6](AndroidEmulatorPlus/Services/MigrationService.cs#L6) | **Dead code** | — | Remove or wire up (the `ForceStop` field hints at A-30) |

### ⑤ Apps / Debloat

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| Package list | Auto / Refresh | [AppService.ListAsync](AndroidEmulatorPlus/Services/AppService.cs#L17) | Partial — `-3` toggle exists but no source column | None | A-18 |
| Bulk uninstall | "Uninstall Selected" | [AppsViewModel.UninstallSelectedAsync](AndroidEmulatorPlus/ViewModels/AppsViewModel.cs#L68) | Complete | None | Add `--user 0` mode (debloat preinstalled without uninstall) |
| Bloat presets | Buttons | [AppService.BloatPresetGoogle/Samsung](AndroidEmulatorPlus/Services/AppService.cs#L38-L62) | Stale — lists are static, won't track new OEM bloat | None | B-04 dynamic preset file shipped in-tree |
| File-picker install | "Install APK…" | [AppsViewModel.InstallApkAsync](AndroidEmulatorPlus/ViewModels/AppsViewModel.cs#L91) | **Buggy** — claims `.apks` / `.xapk` support but `adb install` rejects them | None | **B-02** — extract bundles before install |
| Drag-drop install | Drop on Apps tab | [AppsView.xaml.cs:30](AndroidEmulatorPlus/Views/AppsView.xaml.cs#L30) | Same bug as above | None | Same as B-02 |
| App label / size (`SizeText`) | Per-row | [AndroidApp.cs:19](AndroidEmulatorPlus/Models/AndroidApp.cs#L19) | **Hidden** — `DataSizeBytes` is never populated; column always shows "—" | None | B-05 — populate from `du /data/data/<pkg>` per row, on demand |

### ⑥ Configure

| Feature | Entry | Code | Maturity | Tests/docs | Improvement |
|---|---|---|---|---|---|
| RAM / cores / disk sliders | Configure tab | [ConfigViewModel](AndroidEmulatorPlus/ViewModels/ConfigViewModel.cs) | Complete (raw-byte parse now fixed) | None | Show "current vs new" diff before save |
| Screen W/H/DPI | Three TextBoxes | ConfigView | Partial — no presets | None | ROADMAP **A-21** |
| GPU mode | Not implemented | — | Missing | — | ROADMAP **A-22** |
| Boot flags | Two CheckBoxes | ConfigView | Complete | None | — |
| qcow2 resize | "Resize disk only" | [ConfigService.ResizeDiskAsync](AndroidEmulatorPlus/Services/ConfigService.cs#L24) | Partial — destructive without confirm | None | ROADMAP **A-07** confirmation dialog |
| Snapshot manager | Not implemented | — | Missing | — | ROADMAP **R-06** |

### Cross-cutting

| Feature | Entry | Code | Maturity | Improvement |
|---|---|---|---|---|
| Screenshot | Top-bar button | [MainViewModel.ScreenshotAsync](AndroidEmulatorPlus/ViewModels/MainViewModel.cs#L61) | Complete | Output to clipboard option |
| Screen record | Not implemented | — | Missing | ROADMAP **A-14** |
| Logcat viewer | Not implemented | — | Missing | ROADMAP **A-15** |
| Settings flyout (theme switch, paths) | Not implemented | — | Missing | ROADMAP **A-37** |
| Keyboard shortcuts | Not implemented | — | Missing | ROADMAP **A-27** |
| First-launch wizard | Not implemented | — | Missing | ROADMAP **R-02** |

---

## Competitive and Ecosystem Research

Comparators that share the project's surface area. Each lists what to **adopt** and
what to **avoid** to keep the product scoped.

### Android Studio Device Manager (`tools.android.com` / IDE bundled)

- **Notable capabilities:** SDK Manager UI, AVD Manager UI, hardware-profile editor,
  snapshot manager, cold-boot/wipe-data/duplicate AVD, Pair Devices over Wi-Fi
  (`adb pair`), Resizable preview device, Device Mirroring (mirrors a real device
  window into the IDE), AVD launch flags dialog (proxy, DNS, camera).
- **Learn:** snapshot manager (R-06), AVD duplicate (A-26), Pair over Wi-Fi (new
  **B-06**), launch-flags dialog (A-23), `sdkmanager` system-image install on demand
  (R-01), hardware-profile preset dropdown (A-21).
- **Avoid:** entire IDE shell, Project tooling, Studio Bot / Gemini panel, telemetry —
  AndroidEmulatorPlus's pitch is the *opposite* of "install 1.2 GB to manage AVDs".

### Genymotion Desktop (`genymotion.com`)

- **Notable capabilities:** sensor + GPS + battery + identifiers + network shaping
  panels, "Drag-drop OBB to install", Cloud-hosted variant (irrelevant here),
  per-device theming.
- **Learn:** sensor panel (A-40), OBB drop install (R-04, B-02).
- **Avoid:** their custom QEMU fork (vendor lock-in), licensing model, account/login
  flow.

### scrcpy (`github.com/Genymobile/scrcpy`)

- **Notable capabilities:** real-time mirror + control of phone or emulator,
  clipboard sync, file drop, audio forwarding (since v2), screen record, screenshot.
- **Learn:** drag-drop APK install pattern (already adopted), screen-record toggle
  pattern (A-14), clipboard sync from emulator to host (new **B-07**).
- **Avoid:** embedding scrcpy in-process (A-39 is fine as a *launcher*, but linking
  the renderer in WPF is out of scope and SDL-heavy).

### BlueStacks / LDPlayer / Nox

- **Notable capabilities:** macro recorder, key-mapping overlay, multi-instance,
  Google Play login pre-baked, "high-FPS gaming" presets.
- **Learn:** instance-management view that supports running >1 AVD concurrently
  (ROADMAP **A-33**), multi-AVD process tracking.
- **Avoid:** custom kernels, telemetry, ad-supported launchers, key-mapping macros
  (off-mission), modified Google Play sign-in.

### WSA-PacMan (`github.com/alesimula/wsa_pacman`, archived 2025)

- **Notable capabilities:** drag-drop APK install, package list, uninstall, version
  display. Single-purpose WPF/Flutter app for Windows Subsystem for Android.
- **Learn:** the focused single-EXE shape this project already mirrors — keep that
  identity.
- **Avoid:** the archived/abandoned path: don't accumulate features faster than
  hardening.

### Magisk Manager (the Android app, `topjohnwu/Magisk`)

- **Notable capabilities:** module browser, install-from-zip, deny-list, Zygisk
  toggle, log export.
- **Learn:** the module-manager view layout (R-03), the "Install from storage" zip
  flow.
- **Avoid:** reimplementing the patch engine — this project correctly delegates to
  rootAVD; keep that boundary.

### rootAVD (`gitlab.com/newbit/rootAVD`)

- **Notable capabilities:** the actual ramdisk patcher being wrapped.
- **Learn:** the script's `LISTONLY` and `EXTRACT_ONLY` flags can be re-used for a
  "Dry-run patch" preview (new **B-08**).
- **Avoid:** vendoring it (license + maintenance burden); the current `git clone` +
  pinned SHA approach is correct.

### Android-emulator-cli ecosystem (`emulator -accel-check`, `adb pair`, `sdkmanager`)

These are first-party CLIs already in the SDK. The pattern is: every feature this
project adds should map to an existing SDK CLI rather than reinventing logic. This
keeps the WPF surface a *driver*, not a *replacement*.

---

## Highest-Value New Features

Each entry lists user problem, evidence, behavior, code touch-points, risks,
verification, complexity (S/M/L/XL), priority. New IDs use **B-NN**.

### B-01 — Fix `IsEnabled` mis-binding on busy buttons (P0, S)

- **Problem:** "Download command-line tools" and "Root with Latest Magisk" stay
  clickable even while `IsBusy` is true. The user can re-enter a download or a
  rootAVD patch mid-flight.
- **Evidence:** [InstallView.xaml:83](AndroidEmulatorPlus/Views/InstallView.xaml#L83)
  and [RootView.xaml:25](AndroidEmulatorPlus/Views/RootView.xaml#L25) both do
  `IsEnabled="{Binding IsBusy, Converter=NotBoolToVisibilityConverter, ConverterParameter=invert}"`.
  `NotBoolToVisibilityConverter` returns `Visibility`, not `bool`, and `IsEnabled`
  doesn't coerce; WPF logs a binding error and falls back to `True`.
- **Behavior:** Add `NotBoolConverter` (returns `!bool`) and use it. Or expose
  `IsIdle => !IsBusy` on the VM and bind directly — simpler.
- **Touches:** [Views/Converters.cs](AndroidEmulatorPlus/Views/Converters.cs);
  InstallView.xaml, RootView.xaml.
- **Risk:** Trivial. Cosmetic regression risk only.
- **Verify:** With the app running, kick off the cmdline-tools download; the button
  should grey out per the `IsEnabled=False` template trigger
  ([Dark.xaml:111-114](AndroidEmulatorPlus/Themes/Dark.xaml#L111-L114)).

### B-02 — Bundle (`.apks` / `.xapk`) extractor before `adb install` (P0, M)

- **Problem:** Install dialog and drag-drop accept `.apks` / `.xapk`, but
  `adb install bundle.xapk` fails — these are ZIPs containing multiple APKs (and OBBs
  for `.xapk`).
- **Evidence:**
  [AppsViewModel.cs:95](AndroidEmulatorPlus/ViewModels/AppsViewModel.cs#L95) advertises
  the filter; [AppsView.xaml.cs:13](AndroidEmulatorPlus/Views/AppsView.xaml.cs#L13)
  whitelists the extensions; install code just calls `_apps.InstallApkAsync` per
  file ([AppsViewModel.cs:114](AndroidEmulatorPlus/ViewModels/AppsViewModel.cs#L114)).
- **Behavior:** When extension is `.apks` / `.xapk` / `.apkm`, unzip into a temp
  folder under `…\AndroidEmulatorPlus\transfer\extract-<rand>\`, collect all `*.apk`
  inside (including config splits), then `adb install-multiple` them.
  For `.xapk`, also push `*.obb` into `/sdcard/Android/obb/<pkg>/`.
- **Touches:** new `AppService.ExtractBundleAsync`, refactor
  `AppService.InstallApkAsync` to take an IEnumerable, update `AppsViewModel.InstallApkFilesAsync`,
  delete extracted dir in `finally`.
- **Risk:** Long-path / unicode-name APKs inside the ZIP; ensure
  `ZipFile.ExtractToDirectory` is allowed by the long-path manifest. OBB ownership
  belongs to the emulator's media uid; `adb push` runs as shell which can write to
  `/sdcard` regardless.
- **Verify:** Drop a public split-APK bundle (e.g. an `.apks` from SAI's export)
  onto Apps tab → all splits install, app launches.
- **Complexity:** M.

### B-03 — Progress bar for the cmdline-tools download (P1, S)

- **Problem:** The download stretches ~150 MB on a slow link; the user sees only a
  "Downloading…" label.
- **Evidence:** [InstallViewModel.cs:131-133](AndroidEmulatorPlus/ViewModels/InstallViewModel.cs#L131-L133)
  calls `_dl.DownloadAsync` without an `IProgress<>`; the service already supports
  one ([DownloadService.cs:20](AndroidEmulatorPlus/Services/DownloadService.cs#L20)).
- **Behavior:** Bind a `[ObservableProperty] double DownloadFraction` plus a
  `ProgressBar` on the Install card.
- **Touches:** InstallViewModel, InstallView.xaml.
- **Risk:** None.
- **Verify:** Throttle the network, observe bar fills.
- **Complexity:** S.

### B-04 — Versioned debloat preset file (P1, M)

- **Problem:** [AppService.cs:38-62](AndroidEmulatorPlus/Services/AppService.cs#L38-L62)
  hard-codes Google and Samsung lists. Pixel-only preinstalls (Pixel Stand, At a
  Glance, Wallpaper & style v9000) and OEM presets (Xiaomi, OnePlus, OPPO) aren't
  covered; the lists rot as Google renames packages.
- **Behavior:** Ship `Presets/bloat.json` as embedded resource; allow user override
  at `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json`. Each entry: `{ id, name,
  description, packages[] }`. Render preset buttons from JSON.
- **Touches:** new `PresetService`, `AppService.BloatPresetGoogle/Samsung` deleted,
  AppsView preset row becomes an `ItemsControl`.
- **Risk:** JSON parsing error must not crash the app — load via try/catch with
  fall-back to bundled defaults.
- **Verify:** Drop a custom preset JSON into `%LOCALAPPDATA%\…\presets\`, see new
  buttons appear after refresh.
- **Complexity:** M.

### B-05 — Per-app data size on Apps tab (P2, M)

- **Problem:** [AndroidApp.SizeText](AndroidEmulatorPlus/Models/AndroidApp.cs#L19)
  formats `DataSizeBytes`, but nothing ever writes to it — the "size" column always
  shows `—`. Dead UI surface that promises information.
- **Behavior:** On demand (per-row hover or a "Compute sizes" button), run
  `du -s /data/data/<pkg>` for selected rows (root required) and `dumpsys diskstats`
  fallback. Cache results in the VM.
- **Touches:** AppsViewModel, AppService.
- **Risk:** `du` on `/data/data/<pkg>` requires root; show "(root needed)" instead of
  `—` when not rooted.
- **Verify:** On a rooted emulator with one large app installed, click "Compute
  sizes" and confirm the value matches `adb shell su -c du -s /data/data/<pkg>`.
- **Complexity:** M.

### B-06 — `adb pair` Wi-Fi pairing for phones (P2, M)

- **Problem:** USB debugging is increasingly cumbersome (cable, sometimes elevated
  driver install). Modern Android phones support `adb pair host:port` (API 30+)
  followed by `adb connect host:port`. Once paired, the phone appears in
  `adb devices` just like a USB device — and the Migrate tab's whole flow works
  unchanged.
- **Evidence:** [README.md:38](README.md#L38) currently requires "a USB-connected
  Android phone".
- **Behavior:** Add a "Pair phone over Wi-Fi…" card on the Migrate tab. Prompts for
  `host:port` and 6-digit code from the phone, calls `adb pair` then `adb connect`.
  Persist the host in settings (when introduced) for next time.
- **Touches:** new `AdbService.PairAsync` + `ConnectAsync`, MigrateView card.
- **Risk:** Network reachability (firewalled hotel Wi-Fi). Surface adb errors verbatim
  to the log.
- **Verify:** On a phone with Developer Options → Wireless debugging → Pair using
  pairing code, enter the host:port and code; phone shows up in the top-bar pill.
- **Complexity:** M.

### B-07 — Bidirectional clipboard sync with the emulator (P3, M)

- **Problem:** Migrating account passwords / 2FA tokens from phone to emulator is
  awkward without a clipboard bridge. scrcpy users expect this.
- **Behavior:** Periodic `adb shell cmd clipboard get-primary` poll vs.
  `cmd clipboard set-primary "$text"`; only inject when the host clipboard text
  changes (debounce). Off by default; toggle in a future settings flyout.
- **Touches:** new `ClipboardService`, MainViewModel top-bar toggle.
- **Risk:** Privacy — the emulator could read host clipboard. Default off, never
  store, require explicit opt-in per session.
- **Verify:** Copy text on host → tap a text field on the emulator → paste
  contains host text. And vice-versa.
- **Complexity:** M.

### B-08 — "Dry-run root" preview using rootAVD's `LISTONLY` mode (P3, S)

- **Problem:** Today the only feedback before patching is a vague description.
  rootAVD has a `LISTONLY=1` mode that prints what it *would* patch, including
  ramdisk path resolution.
- **Behavior:** Add "Preview" button next to "Root with Latest Magisk" that runs
  `rootAVD.sh LISTONLY` and dumps to the log.
- **Touches:** RootService, RootViewModel, RootView.
- **Risk:** None — read-only.
- **Verify:** Click Preview, log shows resolved ramdisk path matching
  `image.sysdir.1` from config.ini.
- **Complexity:** S.

### B-09 — Settings flyout: paths, theme, network, telemetry-off statement (P2, L)

- **Problem:** Several preferences are scattered or hard-coded — SDK probe order,
  cache root, screenshot output dir, theme. There is no single place to override or
  inspect them.
- **Evidence:** `SdkLocator.FindSdkRoot`, `MainViewModel.ScreenshotAsync`'s
  `MyPictures` path, theme dictionary all hard-coded.
- **Behavior:** Modal-less side panel (corner-radius 8/10 per CLAUDE.md). Fields:
  SDK root override, cache root override, screenshot output, theme (Mocha/Latte,
  pending A-37), proxy for `_http`, "Reset to defaults". Persist in
  `%LOCALAPPDATA%\AndroidEmulatorPlus\settings.json`.
- **Touches:** new `SettingsService`, several services accept overrides via
  constructor, App.xaml.cs DI bootstrap, new `SettingsView`.
- **Risk:** JSON migration when settings schema changes — version the file.
- **Verify:** Move SDK to `D:\Android\Sdk`, set override, restart → app finds it.
- **Complexity:** L.

### B-10 — Cancel button on long-running ops (P1, M)

- **Problem:** ROADMAP **A-01** already added a timeout to rootAVD, but there is no
  user-visible Cancel. The same applies to AVD create (5 min timeout), cmdline-tools
  download, and migration.
- **Behavior:** A single shared `[ObservableProperty] CancellationTokenSource? _busyCts`
  on each VM that runs long ops; binding-controlled Cancel button replaces the
  primary action while `IsBusy`. Pass the token through to services (most already
  accept `CancellationToken`).
- **Touches:** RootViewModel, AvdViewModel (CreateAsync), InstallViewModel
  (DownloadCmdlineToolsAsync), MigrateViewModel (MigrateAsync). Several services
  already accept `CancellationToken ct` parameters that are currently passed
  `default`.
- **Risk:** Cleanup paths must `finally`-delete the in-flight cache dir + zip.
- **Verify:** Click Root → Cancel within 3 seconds → process tree killed (Task
  Manager), log says "cancelled".
- **Complexity:** M.

### B-11 — Per-AVD `Process` tracking (P1, S — derived from A-33)

- **Problem:** [EmulatorService._current](AndroidEmulatorPlus/Services/EmulatorService.cs#L10)
  is a single field. Launching a second AVD overwrites it; `TryKill` then orphans the
  first.
- **Behavior:** `ConcurrentDictionary<string AvdName, Process>` and a `KillFor(name)`
  helper. On `App.OnExit`, walk the dictionary.
- **Touches:** EmulatorService. (Most callers don't need to change — Stop is already
  done via `adb emu kill` not via Process tree.)
- **Risk:** Same `Process` object may stay alive after emulator self-exits; check
  `HasExited` and prune.
- **Verify:** Launch two AVDs from the AVDs tab; close the app; both emulator
  windows close.
- **Complexity:** S.

---

## Existing Feature Improvements

### Migration — phone-side tar fallback (ROADMAP A-29 expanded)

- **Current:** `MigrationService.TransferInternalDataAsync` always uses
  `tar --exclude=…/cache --exclude=…/code_cache --exclude=…/no_backup`. Modern
  Android ships toybox tar (since Android 6) and `--exclude=` is supported there,
  but older / non-standard phones (rare custom ROMs, very old devices) may not.
- **Recommended change:** Detect tar flavor via `tar --version` once per session,
  cache per-device, fall back to `find /data/data/<pkg> -type d \( -name cache -o
  -name code_cache -o -name no_backup \) -prune -o -print | tar -cf <out> -T -`.
- **Touches:** MigrationService, AdbService (cache device→tar-flavor map).
- **Backwards compat:** Pure additive; canonical happy path unchanged.
- **Verify:** Smoke-test against an older AOSP build (or a rooted Android 7 image);
  internal data still transfers.
- **Complexity:** M, P2.

### Migration — pre-flight `allowBackup=false` warning (ROADMAP A-19)

- **Current:** No warning before attempting data tar; many apps (banking,
  fingerprint-protected vaults, anything with `android:allowBackup="false"`) will
  refuse the restored data after migration.
- **Recommended change:** Before each package's data tar, `adb shell dumpsys package
  <pkg> | grep ALLOW_BACKUP` (or `pm dump`); if flag absent, mark the row with a ⚠
  badge and skip data transfer unless user opted "Force".
- **Touches:** MigrationService, MigrateViewModel.
- **Backwards compat:** Adds a column; safe.
- **Verify:** Add `com.discord` (allowBackup=false) → row shows ⚠ and only APK
  transfers by default.
- **Complexity:** M, P1.

### Configure — confirm-before-destroy disk wipe (ROADMAP A-07 expanded)

- **Current:** "Resize + Wipe Data" deletes all qcow2 overlays + the snapshots
  directory after a one-line warning subtitle ([ConfigView.xaml:74](AndroidEmulatorPlus/Views/ConfigView.xaml#L74)).
  Logging shipped, but no interactive confirmation.
- **Recommended change:** Custom `ConfirmDialog` (modal-less border styled to match
  cards) listing every snapshot name and the qcow2 file sizes, plus a typed
  `WIPE`-to-confirm field — mirrors GitHub's destructive-action pattern.
- **Touches:** new `ConfirmDialog` UserControl, ConfigViewModel.
- **Backwards compat:** Adds a step; safe.
- **Verify:** Try Resize + Wipe with two named snapshots → dialog lists both and
  is required.
- **Complexity:** M, P1.

### AVDs — confirm-before-delete

- **Current:** Overflow menu → "Delete AVD…" calls `_avds.DeleteAsync` immediately
  ([AvdViewModel.cs:97](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L97)). The
  ellipsis in the menu hints at a dialog but there isn't one.
- **Recommended:** Same `ConfirmDialog` as above, listing the AVD folder + size.
- **Complexity:** S, P1.

### Root — auto-launch missing emulator (ROADMAP A-28)

- **Current:** `RootViewModel.RootAsync` aborts with "Launch the AVD first" if no
  emulator is attached. Dead-end message.
- **Recommended:** Inline "Launch <name>" button on the warning; on click, call
  `EmulatorService.Launch` + `AdbService.WaitForBootAsync`, then re-enter `RootAsync`.
- **Complexity:** S, P2.

### Apps — `--user 0` mode for preinstalled debloat

- **Current:** Uninstall uses `adb uninstall <pkg>` (then `-k` for keep-data). For
  preinstalled OEM apps marked as system, `adb uninstall` fails with
  `DELETE_FAILED_INTERNAL_ERROR`. The community workaround is
  `pm uninstall --user 0 <pkg>` (per-user uninstall — survives until factory reset,
  reversible with `pm install-existing`).
- **Recommended:** Add a "Disable for current user (reversible)" checkbox alongside
  Uninstall; pipes through to `pm uninstall --user 0` or `pm disable-user --user 0`.
- **Touches:** AppService.UninstallAsync signature, AppsView controls.
- **Verify:** Try to uninstall `com.samsung.android.bixby.agent` on a Samsung
  system image → fails without flag, succeeds with flag, reappears after `pm
  install-existing`.
- **Complexity:** M, P2.

### Logging — colour-coded `Detail` is hard to read against `OverlayBrush`

- **Current:** `Detail` log level uses `#FF6C7086` (overlay) on `#FF11111B` (crust).
  Contrast ratio is ~3.0:1 — below WCAG AA for normal text.
- **Recommended:** Move Detail to `SubtextBrush` (`#FFA6ADC8`, ratio ~9.5:1) and
  reserve Overlay for timestamps/dim metadata only.
- **Touches:** [MainWindow.xaml:148-150](AndroidEmulatorPlus/MainWindow.xaml#L148-L150).
- **Complexity:** S, P2.

### Image selector — "latest" picks by lexical order

- **Current:** [AvdViewModel.RefreshAsync](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L58)
  defaults `NewImage` to `AvailableImages.LastOrDefault()`. The list is built by
  directory walk in [AvdService.ListSystemImagesAsync](AndroidEmulatorPlus/Services/AvdService.cs#L79)
  — alphabetical. `android-9` sorts before `android-25`, so a system with only API
  9 and API 25 installed will default to API 9.
- **Recommended:** Sort by parsed API level descending; tie-break by variant
  (`google_apis_playstore` > `google_apis` > `default`).
- **Complexity:** S, P2.

---

## Reliability, Security, Privacy, and Data Safety

### Real bugs / risks

- **B-01 IsEnabled mis-binding** — see above. P0.
- **B-02 bundle extractor missing** — `.apks` / `.xapk` fail silently. P0.
- **A-04 supply chain** — Magisk APK and cmdline-tools ZIP fetched over HTTPS but
  never hash-verified. A compromised mirror or GitHub release replacement could
  inject malware into a rooting flow. P0.
- **A-03 unpinned rootAVD** — pin constant still `"master"`; one bad push from
  newbit can brick every user's root flow with no detection here. P0.
- **A-05 migration cache leak** — `transfer\` can grow multi-GB and is never
  surfaced.
- **A-07 destructive resize without typed confirm** — silent snapshot destruction.
- **Process tree leaks** — closing the app doesn't kill running emulator children
  (B-11).
- **No re-entrancy guard** on the busy buttons (B-01 root cause).

### Permissions / network / filesystem

- No elevation required (`asInvoker`) — correct.
- Outbound: `dl.google.com`, `api.github.com/topjohnwu/Magisk`, `gitlab.com/newbit`,
  `developer.android.com/studio`. All HTTPS, all justified. Document this in
  README's Privacy section (currently absent).
- Reads `$ANDROID_HOME`, `%LOCALAPPDATA%\Android\Sdk`, `%USERPROFILE%\.android\avd`.
  Standard Android-developer paths. Long-path manifest is set, so deep AVD paths are
  safe.
- Writes only inside `%LOCALAPPDATA%\AndroidEmulatorPlus\` and `%USERPROFILE%\
  Pictures\AndroidEmulatorPlus\` and `%USERPROFILE%\Desktop\Emulator - <name>.cmd`.
  The desktop shortcut location is appropriate but should be configurable (B-09).

### Recovery / rollback

- **Stock ramdisk backup** is well-handled
  ([RootService.cs:66-67](AndroidEmulatorPlus/ViewModels/RootViewModel.cs#L66-L67),
  [RootService.RestoreRamdisk](AndroidEmulatorPlus/Services/RootService.cs#L193)).
- **AVD delete** has no recovery — should at minimum rename `.avd` dir to
  `.avd.deleted-<ts>` and let the user purge manually.
- **Migration partial-failure recovery** — if extract fails mid-flow, the emulator's
  `/data/data/<pkg>` may be half-restored. Force-stop is called pre-extract on the
  emulator but not on the phone (ROADMAP **A-30**).

### Logging / diagnostics

- `LogService` mirrors to `app-YYYYMMDD.log` ✓ (already shipped).
- `crash.log` for unhandled exceptions ✓ (already shipped).
- **Missing:** version pin for the running rootAVD SHA in the log header — a
  future incident report would need this. Add to `LogService` first-write line.

---

## UX, Accessibility, and Trust

### Onboarding

- First launch: no SDK → only "Install" tab is functionally useful; the user has to
  read the SubheaderText to know to go there. **Recommendation:** auto-navigate to
  Install when `_sdk.IsReady == false`; current code initializes `ActiveSection =
  "Avd"` ([MainViewModel.cs:20](AndroidEmulatorPlus/ViewModels/MainViewModel.cs#L20))
  which lands on an empty list and "Refresh" with no AVDs. ROADMAP **R-02** wizard
  covers this.

### Empty / loading / error states

- **AVDs tab** with no AVDs: empty ItemsControl renders zero cards, no guidance.
  Add an empty-state card "No AVDs yet — create one below or open the Install tab".
- **Apps tab** with no emulator: only a log warning "No emulator running." The
  Apps tab itself still shows the filter/preset bar as if it were ready. Disable
  the actions row + show an inline message.
- **Migrate tab**: similar — when phone is absent, the packages list is empty
  silently.
- **Install tab error path** for `LatestCmdlineToolsWindowsUrlAsync` — logs the
  warning but still proceeds with fallback; UI never says "using fallback URL"
  visibly. Surface a small Subtext note on the Install card.

### Destructive / irreversible actions

- **Resize + Wipe Data** — see A-07 above.
- **Delete AVD** — see "confirm-before-delete" above.
- **Un-Root** — destructive ramdisk overwrite, no confirm. Add one.
- **Clear crash.log** — minor but should confirm; current click instantly deletes.

### Settings clarity

- Configuration is split between `config.ini` (hardware) and undisplayed AVD `.ini`
  + missing app settings altogether. B-09 unifies this.

### Accessibility

- WPF Window has `MinWidth=980 MinHeight=640` — fine for typical desktop, slim for
  laptop 1366×768.
- Theme is dark-only; corporate users on screen-shares need Latte (ROADMAP **A-37**).
- Status pills use foreground `SubtextBrush` over `SurfaceBrush` — contrast ~6.6:1,
  passes AA.
- Drag-drop has visual `DragDropEffects.Copy` cursor only; no visible drop-target
  rectangle. Add a temporary highlighted border on `OnDragEnter`.
- **Detail log lines** below WCAG AA (see "Logging" above).
- No keyboard navigation between sidebar items (Tab works but no Ctrl+1..6 shortcut;
  ROADMAP **A-27**).
- TextBoxes for screen W/H/DPI accept arbitrary text and only fall back via
  `int.TryParse` ([ConfigViewModel.cs:51-53](AndroidEmulatorPlus/ViewModels/ConfigViewModel.cs#L51-L53)) —
  no inline validation, the slider mode (A-21) fixes this.

### Microcopy / trust

- "Persists `shell→allow` policy in Magisk DB so `adb shell su` works headlessly
  thereafter" — accurate.
- "(no SDK detected)" — fine.
- Install card's "If nothing is installed" — friendly.
- Migrate tab — clear about root requirements.
- **Add:** privacy footnote on Install tab: "AndroidEmulatorPlus contacts
  dl.google.com, api.github.com, gitlab.com, and developer.android.com only for
  downloads. No telemetry."

---

## Architecture and Maintainability

### Module / boundary improvements

- **ProcessRunner** is the right chokepoint, but RootService bypasses it for the
  rootAVD shell-out ([RootService.PatchAsync:121-141](AndroidEmulatorPlus/Services/RootService.cs#L121-L141))
  and AvdService bypasses it for `cmd.exe /c avdmanager`
  ([AvdService.CreateAsync:109-141](AndroidEmulatorPlus/Services/AvdService.cs#L109-L141)).
  Both have legitimate reasons (stdin piping, custom env, stream events). Consider
  adding `ProcessRunner.RunWithStdinAsync` and `ProcessRunner.StreamAsync` overloads
  so all `Process.Start` calls go through one file. This is the second
  CLAUDE.md-cited invariant ("Helpers/ProcessRunner is the only place Process.Start
  lives") that the code already violates twice.

### Refactor candidates

- **MigrationService** has three near-identical pipelines (TransferApk /
  TransferInternalData / TransferExternalData), each with its own
  `try/Directory.Delete/File.Delete` pattern. Extract a `using var scratch = new
  TransferScratch(pkg, _log)` IDisposable to centralize cleanup + size reporting.
- **InstallViewModel** owns its own `DiagnosticsRoot` constant copy — duplicate of
  `LogService.LogDirectory`'s parent. Move to one place (Settings, B-09).
- **`MigrationService.TransferOptions` record** is unused. Delete or wire it up
  (ROADMAP A-30 wants `ForceStop` enabled).

### Test gaps (ROADMAP A-35 expanded)

There is no `AndroidEmulatorPlus.Tests/` project. Target coverage:

- `AvdService.ParseIni` / `WriteIni` round-trip (preserves comments, quoting,
  out-of-order keys).
- `MigrationService.ParseFailReason` — fed canned `adb install` failure strings.
- `DownloadService.LatestMagiskAsync` asset-name filter — fed canned GitHub
  releases JSON with debug/stub/canary mix.
- `DownloadService.LatestCmdlineToolsWindowsUrlAsync` — fed canned HTML from
  developer.android.com (file in `Tests/Fixtures/`).
- `ConfigViewModel.ParseSizeGb` — including the raw-byte case fixed in 3c7b738.
- `RootService.RelativeRamdiskPath` — Windows path separators, drive-letter root.

Use `xunit` + `Microsoft.NET.Test.Sdk`. No mocking framework needed; all targets
are pure or take constructor-injected fakes.

### Documentation gaps

- README has no Privacy / Network section (see microcopy above).
- README's "Typical workflow" jumps from "Migrate" to "Apps" with no mention that
  the emulator's Magisk must be granted shell first (and that the app does that
  for you via Verify+Persist). One sentence in step 4 suffices.
- CHANGELOG `Unreleased` has two `### Fixed` blocks (a merge artifact); collapse to
  one before tagging the next release.
- No CONTRIBUTING.md, but contributions look intentionally not solicited; that's
  fine if intentional.

### Release / build / deployment

- **No CI** — ROADMAP A-36. The build constraint (no SDK on this VM) makes CI
  *required* for a sustainable release cadence, not a nice-to-have.
- **No GitHub Release** — the only versioned artifact is the local
  `bin\Release\…\AndroidEmulatorPlus.exe`. Pair A-36 with a release-attach step.
- **No installer** — power users can run the .exe directly with .NET 9 Runtime
  installed; a self-contained publish (`--self-contained true`) bumps the EXE to
  ~70 MB but removes the .NET dependency. Worth a P2 toggle in the workflow.
- **No code-signing** — SmartScreen will warn on first run. A bare-minimum
  self-signed cert or an Open Source Initiative cert is a future P3.

---

## Prioritized Roadmap

Cross-references to ROADMAP.md preserved by their original IDs. New items use
**B-NN**. Items already `[x]` in ROADMAP are excluded.

### Phase 1 — P0 hardening (do these before any new feature)

- [ ] **P0 B-01** — Fix `IsEnabled` mis-binding on busy buttons
  - Why: Buttons that should disable while busy stay clickable, allowing re-entry into
    a download or rootAVD patch.
  - Evidence: [InstallView.xaml:83](AndroidEmulatorPlus/Views/InstallView.xaml#L83),
    [RootView.xaml:25](AndroidEmulatorPlus/Views/RootView.xaml#L25)
  - Touches: Views/Converters.cs (add `NotBoolConverter`), InstallView.xaml,
    RootView.xaml.
  - Acceptance: Button shows the `IsEnabled=False` opacity (0.45) and cursor=Arrow
    while `IsBusy=True`.
  - Verify: Click Root → button is greyed out for the duration; second click is
    ignored.
- [ ] **P0 B-02** — `.apks` / `.xapk` extractor before install
  - Why: File-dialog filter and drag-drop advertise both formats but `adb install`
    rejects them.
  - Evidence: [AppsViewModel.cs:95](AndroidEmulatorPlus/ViewModels/AppsViewModel.cs#L95),
    [AppsView.xaml.cs:13](AndroidEmulatorPlus/Views/AppsView.xaml.cs#L13)
  - Touches: AppService (new `ExtractBundleAsync`), AppsViewModel.InstallApkFilesAsync.
  - Acceptance: A dropped `.xapk` installs all splits + pushes `*.obb`.
  - Verify: Drop a public split-APK bundle (SAI-export `.apks`); app reports
    "Success" and the package shows in the inventory.
- [ ] **P0 A-04** — SHA-256 verify Magisk APK + cmdline-tools ZIP
  - Why: Supply-chain risk on a tool that rewrites a ramdisk and ships an SDK.
  - Evidence: [DownloadService.cs:19-39](AndroidEmulatorPlus/Services/DownloadService.cs#L19-L39)
    has no hash branch.
  - Touches: DownloadService (new `DownloadAndVerifyAsync`), RootService,
    InstallViewModel.
  - Acceptance: Mismatched hash deletes the partial file + raises an error in the
    UI.
  - Verify: Manually corrupt the downloaded `Magisk.apk` between download and
    install; flow aborts with a clear error.
- [ ] **P0 A-03** — Lock rootAVD pin to a verified SHA
  - Why: A breaking `master` push to newbit's repo silently bricks the root flow.
  - Evidence: [RootService.cs:12](AndroidEmulatorPlus/Services/RootService.cs#L12)
    `RootAvdPinnedRef = "master"` (the *hook* shipped, the *pin* didn't).
  - Touches: RootService.cs constant.
  - Acceptance: `git -C <CacheRoot>/rootAVD rev-parse HEAD` equals the pinned SHA.
  - Verify: Smoke-test root flow against API 35 Google Play AVD, then commit the
    SHA that produced a successful patch.
- [ ] **P0 A-05** — Migration cache size indicator + Clear button
  - Why: `transfer\` orphaned tarballs survive crashes; no visibility.
  - Touches: MigrateView, new `IDiagnosticsService` (size + clear).
  - Acceptance: Card shows total bytes; Clear empties the directory.
  - Verify: Abort a migration mid-flight via task-manager kill on adb; relaunch the
    app, the card shows the orphaned size; click Clear; size returns to 0.
- [ ] **P0 A-07** — Typed confirmation before disk wipe
  - Why: Resize+Wipe silently destroys named snapshots.
  - Touches: ConfigView, new `ConfirmDialog`.
  - Acceptance: User must type `WIPE` to proceed.
  - Verify: Try Resize+Wipe with a saved snapshot; dialog lists it and requires
    typed confirmation.

### Phase 2 — P1 workflow gaps

- [ ] **P1 A-25 UI** — Inline rename popup on AVD card
  - Why: Backend (`RenameAsync` + `RenameTarget` + `RenameCommand`) already wired in
    [AvdViewModel.cs:144](AndroidEmulatorPlus/ViewModels/AvdViewModel.cs#L144); no
    XAML calls it.
  - Touches: AvdView.xaml overflow menu item + Popup with TextBox.
  - Acceptance: Right-click → Rename → typed name → `<old>.ini` renamed to `<new>.ini`
    in `~/.android/avd/`.
  - Verify: After rename, AVD launches under the new name and `getprop
    ro.kernel.qemu.avd_name` returns the new name.
- [ ] **P1 R-01** — System-image picker that can `sdkmanager` new images on demand
  - Why: Today CreateAVD only enumerates already-installed images.
  - Touches: AvdService.ListSystemImagesAsync (online-list flag), new
    `SdkmanagerService`.
  - Acceptance: User can pick `system-images;android-36;google_apis_playstore;x86_64`
    and the app downloads + installs it before creating the AVD.
  - Verify: On a fresh SDK, create an AVD from an image not yet on disk.
- [ ] **P1 A-08** — confirm-before-delete on AVD
  - Why: Adjacent to the recently shipped overflow menu; one more click prevents
    accidental loss.
  - Touches: AvdView overflow menu, ConfirmDialog.
  - Acceptance: Delete requires confirmation showing the AVD folder size.
- [ ] **P1 A-15** — Logcat viewer
  - Why: Migration failure diagnosis often lives in logcat — without it, users
    bounce between this app and Studio.
  - Touches: new `LogcatService` (streams `adb logcat -v threadtime`), new
    LogcatView.
  - Acceptance: Filter by package + level; Clear + Save buttons.
  - Verify: Reproduce an install-failed scenario; offending stack appears in the
    viewer.
- [ ] **P1 A-19** — `allowBackup=false` pre-flight warning
  - Why: Saves users a doomed migration attempt for banking/2FA apps.
  - Touches: MigrationService, MigrateViewModel.
  - Acceptance: Affected rows show ⚠ and skip data tar by default.
- [ ] **P1 A-01 cancel** — Cancel button on rootAVD patch (and others)
  - Why: Timeout exists; user agency doesn't. B-10 generalizes this.
  - Touches: RootViewModel, AvdViewModel, InstallViewModel, MigrateViewModel.
  - Acceptance: Cancel button replaces primary action during `IsBusy`; click kills
    the process tree and rolls back partial state.
- [ ] **P1 A-16** — Auto-accept SDK licenses
  - Why: Wizard / R-01 system-image installer blocks on `sdkmanager --licenses`.
  - Touches: SdkmanagerService.
  - Acceptance: Pipes `y\n` repeatedly; user never sees a prompt.
- [ ] **P1 A-14** — Screen record toggle
  - Why: Parity with scrcpy; useful for bug reports.
  - Touches: AdbService, MainViewModel top-bar.
  - Acceptance: Start → emulator records to `/sdcard`; Stop → pulled to
    `Pictures\AndroidEmulatorPlus\screencast-<ts>.mp4`.
- [ ] **P1 A-18** — `-s` / `-d` toggles + source column on Apps tab
  - Why: Users care about preinstalled OEM apps marked system, not just
    third-party.
  - Touches: AppService.ListAsync, AppsView.
  - Acceptance: Toggle "Include system" + "Include disabled" + per-row badge.
- [ ] **P1 B-10** — Centralize CancellationToken plumbing (umbrella for A-01 cancel)
- [ ] **P1 B-11** — Multi-AVD `Process` tracking (sub-task of A-33).
- [ ] **P1 A-36** — GitHub Actions release build
  - Why: Without CI there is no signed/published artifact, and ROADMAP A-35 tests
    have nowhere to run.
  - Touches: `.github/workflows/build.yml`.
  - Acceptance: Push to `main` → workflow builds `Release | x64`, archives the
    self-contained publish, attaches to a draft Release.
  - Verify: First push; the workflow tab shows green; artifact downloads run on a
    clean Windows 11 VM.

### Phase 3 — P2 polish

- [ ] **P2 A-21** — Screen preset picker (Pixel 7/7 Pro/Tablet/Fold, Custom).
- [ ] **P2 A-22** — GPU mode picker on Configure (`hw.gpu.mode`).
- [ ] **P2 A-23** — Launch flags overflow on AVD card (proxy / DNS / no-window /
  noaudio / camera).
- [ ] **P2 A-24 remediation links** — On accel-check failure, show links to MS
  docs (Windows Hypervisor Platform) and Intel HAXM / AMD-V instructions.
- [ ] **P2 A-26** — Duplicate AVD (copy `.avd` + `.ini`, rewrite `path=`).
- [ ] **P2 A-27** — Keyboard shortcuts (Ctrl+1..6, F5, Ctrl+L).
- [ ] **P2 A-28** — Inline "Launch <name>" CTA on the Root warning.
- [ ] **P2 A-37** — Catppuccin Latte palette + theme switcher.
- [ ] **P2 B-04** — Versioned debloat preset JSON.
- [ ] **P2 B-05** — Per-app data size on Apps tab.
- [ ] **P2 B-06** — `adb pair` Wi-Fi pairing dialog.
- [ ] **P2 B-09** — Settings flyout (paths, theme, network, telemetry-off).
- [ ] **P2 A-35** — Unit test project + first 6 tests.
- [ ] **P2 R-06** — Snapshot manager (list/load/delete).
- [ ] **P2 R-08** — APK signature verification with `apksigner.bat`.
- [ ] **P2 R-09** — Per-package bandwidth-aware progress.
- [ ] **P2 R-04** — Optional OBB pass during migration.
- [ ] **P2 R-05** — Per-app data export to ZIP (cold archive + Restore).
- [ ] **P2 A-29** — Phone-side tar flavor detect + `find -prune` fallback.
- [ ] **P2 A-30** — Force-stop on phone before data tar (gated by checkbox).
- [ ] **P2 A-33** — Multi-AVD `Process` dictionary.
- [ ] **P2 A-34** — Kill emulator children on app close.
- [ ] **P2** — Microcopy / WCAG fixes (detail log brush, empty states, drop-target
  hover border, README Privacy section).

### Phase 4 — P3 / larger bets

- [ ] **P3 R-02** — First-launch wizard (Install → Create → Root → Migrate).
- [ ] **P3 R-03** — Magisk module manager view.
- [ ] **P3 A-38** — Wear OS / Android TV / Auto AVD profiles.
- [ ] **P3 A-39** — scrcpy launcher integration.
- [ ] **P3 A-40** — Sensor / GPS / battery / telephony simulation tab.
- [ ] **P3 A-41** — KernelSU alternative-root path.
- [ ] **P3 B-07** — Bidirectional clipboard sync (off by default).
- [ ] **P3 B-08** — Dry-run root preview via rootAVD `LISTONLY`.
- [ ] **P3 R-07** — Linux/macOS port via Avalonia.

---

## Quick Wins

Items < 30 minutes each, no architectural impact:

- Delete unused `MigrationService.TransferOptions` record
  ([MigrationService.cs:6](AndroidEmulatorPlus/Services/MigrationService.cs#L6)) or
  wire it through to enable A-30.
- Fix the duplicated `### Fixed` blocks in CHANGELOG `Unreleased`.
- Add `[rootAVD pin: <SHA>]` to the LogService first-write session header.
- Bump `Detail` log lines' brush from `OverlayBrush` to `SubtextBrush` for AA
  contrast.
- Add an empty-state TextBlock to AVDs tab when `Avds.Count == 0`.
- Surface "using fallback URL" in InstallView when the cmdline-tools scrape misses.
- Sort `AvailableImages` by parsed API level descending; default `NewImage` to the
  highest API.
- README: add a one-line Privacy / Network section listing the 4 domains the app
  reaches.
- README: add a screenshot section (after taking new screenshots, file under
  `docs/screenshots/`).

---

## Larger Bets

- **First-launch wizard** (R-02) — touches all 6 views; needs a state machine.
- **Snapshot manager** (R-06) — new model, new view, careful destructive flows.
- **Module manager** (R-03) — new tab; depends on a rooted emulator detection from
  RootViewModel and a `magisk --install-module` driver.
- **Settings flyout + persisted settings** (B-09) — touches every service that
  currently reads env or hard-coded paths.
- **Avalonia port** (R-07) — XAML stays mostly compatible, but `Process` PInvoke
  paths and `Microsoft.Win32.OpenFileDialog` need shims. Defer until P0/P1 stabilize.

---

## Explicit Non-Goals

- **App-store distribution** — winget yes, Microsoft Store no. The Store would
  require AppX packaging and lifecycle changes that don't fit the "single EXE +
  shell out to CLIs" identity.
- **Custom kernel / forked emulator** — BlueStacks-style. The product's promise is
  the *real* Android emulator with a real Play Store; custom kernels undo that.
- **Telemetry** — even crash telemetry. The app's audience is exactly the user that
  doesn't want it; the local `crash.log` is sufficient.
- **GUI for `gradle` / `apksigner sign` / app development** — out of scope. This is
  an emulator manager, not an IDE.
- **macro recorder / key-mapping** — BlueStacks niche; off-mission and
  maintenance-heavy.
- **Embedded scrcpy renderer** — keep the integration to launching the external
  scrcpy.exe per A-39. Hosting an SDL surface in WPF is a maintenance trap.

---

## Open Questions

These actually block prioritization or implementation:

1. **Is the SysAdminDoc remote permitted to receive pushes from this VM?** Per
   `memory/sysadmindoc-git-auth.md`, some repos hit 403 from this environment. The
   roadmap items that include "commit and push" need an answer; for now this plan
   assumes commits only, push on the main desktop PC.
2. **What is the verified rootAVD SHA?** Required to close A-03. Needs a manual
   smoke-test on API 35 + API 36 system images that the maintainer trusts before
   committing the constant.
3. **Should `B-09 Settings` persist as JSON in `%LOCALAPPDATA%` or follow
   Windows Settings (Application Data + Roaming)?** Roaming makes sense for paths
   that should follow the user across machines (SDK root), but the cache root
   should not. Default proposal: local-only `settings.json`, no roaming. Confirm
   before implementing.
4. **Distribution channel for releases — winget, GitHub Releases, or both?**
   Affects the A-36 workflow yaml structure. Default proposal: GitHub Releases
   first (lowest friction), winget manifest after a stable v0.2.0.
