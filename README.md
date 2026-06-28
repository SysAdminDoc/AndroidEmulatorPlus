# AndroidEmulatorPlus

[![Version](https://img.shields.io/badge/version-0.2.6-blue.svg)](https://github.com/SysAdminDoc/AndroidEmulatorPlus/releases)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-9.0-blueviolet.svg)](#)

A single Windows app that handles the full Android-on-PC story end-to-end:
install the SDK, manage AVDs, **root the emulator with Magisk**, **install
Magisk modules**, **migrate apps + app data from a USB or Wi-Fi-paired
phone**, debloat, tune the AVD's hardware, stream logcat, and drive the
emulator console.

No more juggling `adb`, `emulator`, `qemu-img`, `rootAVD`, `apksigner`,
`sdkmanager`, and a pile of bash scripts. Click buttons instead.

---

## What it does

The window is an 8-tab sidebar plus a top-bar of attached-device / quick-action
pills:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ AndroidEmulatorPlus v0.2.6  SDK ✓ … 📱 phone …  💻 emu … 📷 🎥 🖥 ⚙       │
├────────────┬─────────────────────────────────────────────────────────────────┤
│ ① Install  │                                                                 │
│ ② AVDs     │                                                                 │
│ ③ Root     │            ( per-tab content here )                             │
│ ④ Migrate  │                                                                 │
│ ⑤ Apps     │                                                                 │
│ ⑥ Configure│                                                                 │
│ ⑦ Logcat   │                                                                 │
│ ⑧ Console  │                                                                 │
├────────────┴─────────────────────────────────────────────────────────────────┤
│  Log panel — colour-coded ring, mirrored to %LOCALAPPDATA%/…/logs/app-*.log │
└──────────────────────────────────────────────────────────────────────────────┘
```

| Section | What you can do |
|---|---|
| **① Install / SDK** | Detect Android SDK at standard locations. Download the command-line tools (~150 MB) with visible progress, HTTP Range resume, and SHA-256 verification, then lay them out at `cmdline-tools/latest`. Accept all SDK licenses in one click. Run `emulator -accel-check` with one-click remediation links on failure. Surface the rolling `crash.log` tail. |
| **② AVDs** | List existing AVDs with badge / running state. Launch / Cold Boot / Stop / "Launch with options…" (cold/wipe/headless/`-http-proxy`/`-dns-server`/cameras). Per-AVD overflow: Show on disk, Rename, Duplicate, Snapshots, Desktop shortcut, Delete (with confirm). Create from any installed system image. "Browse online…" runs `sdkmanager --list` to install new images on demand. |
| **③ Root** | Patch the AVD's `ramdisk.img` with the latest Magisk via [rootAVD](https://gitlab.com/newbit/rootAVD) (revision-pinned). GitHub `digest` field is cross-checked against the in-tree manifest for supply-chain defense in depth. Magisk shell-policy persistence so `adb shell su` is headless thereafter. Inline "Launch & root" CTA if no emulator is attached. "Dry run (LISTONLY)" preview. **Modules…** sub-dialog installs Shamiko / LSPosed / PlayIntegrityFork / Tricky Store / Zygisk Detach (or arbitrary local `.zip`). Security-testing actions install a Burp/mitmproxy CA into the Magisk-backed system trust store and deploy/stop `frida-server` for the attached rooted emulator. |
| **④ Migrate from Phone** | Detects a USB or Wi-Fi-paired phone (`adb pair` flow built in). Lists user packages with a `⚠ no-backup` pill for apps that declared `android:allowBackup="false"`. Pulls each APK (split-aware), installs on the emulator, then `tar`s `/data/data/<pkg>` over with UID remap + `restorecon`. Optional passes for `/sdcard/Android/data/<pkg>` and `/sdcard/Android/obb/<pkg>`. Force-stop the source app on the phone first to avoid torn SQLite DBs. Phone-side tar flavor is auto-detected (toybox vs. find-prune fallback). |
| **⑤ Apps / Debloat** | Inventory installed apps (include system / disabled toggles, per-row tag pill). Apksigner signature verification before install with a cert-mismatch warning vs. the installed package. Multi-select uninstall or "Disable for user 0" (reversible). Drag-and-drop install for `.apk` / `.apks` / `.xapk` / `.apkm` (bundles auto-extracted, base APK ordered first, OBBs pushed). JSON-driven debloat presets (Google / Samsung / Pixel / Xiaomi / OnePlus / Motorola / Huawei / Nothing); override at `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json`. Per-app data export → ZIP and Import from ZIP for cold archival. Compute per-app data size on demand (`du -sb /data/data/<pkg>`). |
| **⑥ Configure** | Edit `config.ini`: RAM / vCPUs / disk size / fastboot flags. Screen preset picker (Pixel 7 through Pixel 10 Pro Fold / Tablet / Fold / Nexus / TV). GPU mode picker (`hw.gpu.mode`: host / swiftshader_indirect / angle_indirect / guest / off) with inline guidance for VM / RDP scenarios. Resize the qcow2 partition with `qemu-img`, optionally wiping data so the inner ext4 actually grows (typed `WIPE` confirmation listing every snapshot about to be destroyed). |
| **⑦ Logcat** | Dedicated tab streaming `adb logcat -v threadtime` from the attached emulator with priority + package filters. Clear buffer (`logcat -c`) / Clear view / Save to file. Virtualizing 5000-line ring. |
| **⑧ Console** | Emulator-console (`adb emu …`) sandbox: GPS (`geo fix`), battery (capacity + status), telephony (`gsm call`, `sms send`), network condition presets, manual clipboard pull/push, free-form command field. |

**Top bar quick actions:** Screenshot · Record (start/stop) · Mirror (scrcpy)
(launch external `scrcpy.exe`) · ⚙ Settings (theme, SDK root override,
media output dir, HTTP proxy, auto-scrcpy, Velopack updates, show wizard,
telemetry-off statement).

A live device-monitor in the top bar shows whether a phone and an emulator
are currently attached over `adb`, with transport, API level, security patch
freshness, and platform-tools version in the status tooltips.

## Why this exists

Setting up an Android emulator with a real Google Play Store, rooted with
Magisk + Shamiko + LSPosed, populated with your phone's apps and data is a
~30 step process touching half a dozen CLIs. This collapses it into a few
clicks — with supply-chain verification on every downloaded binary.

## Requirements

- Windows 10/11 x64
- [.NET 9 Runtime](https://dotnet.microsoft.com/download) (or build from source)
- A hypervisor (Windows Hypervisor Platform / Hyper-V) for the emulator
- For Root flow: **Git for Windows** (rootAVD is a bash script)
- For Migrate flow: a USB-connected Android phone with USB debugging enabled,
  or any Android 11+ phone with Wireless debugging (pair from the Migrate tab).
  Internal data copy requires the phone to be rooted (Magisk).
- For scrcpy quick-action: `scrcpy.exe` on PATH (`winget install Genymobile.scrcpy`).

## Build from source

```bash
git clone https://github.com/SysAdminDoc/AndroidEmulatorPlus.git
cd AndroidEmulatorPlus
dotnet build AndroidEmulatorPlus/AndroidEmulatorPlus.csproj -c Release
```

The output sits in `AndroidEmulatorPlus/bin/Release/net9.0-windows/AndroidEmulatorPlus.exe`.

For a self-contained single-file publish:

```bash
dotnet publish AndroidEmulatorPlus/AndroidEmulatorPlus.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Maintainer releases are built locally. Use `dotnet publish`, run the local test
suite, create the Velopack packages on the release machine, then upload the
installer, portable package, full package, delta package, and `releases.win.json`
feed to the GitHub Release.

## Tests

```bash
dotnet test AndroidEmulatorPlus.Tests/AndroidEmulatorPlus.Tests.csproj
```

Coverage: ini round-trip, raw-byte disk parsing, install-failed reason
extraction, system-image sort, SHA-256 helper, `OrderBaseFirst`, AVD
duplicate file-tree contract, debloat-preset merge, `allowBackup`
parsing, known-hashes manifest schema, CA certificate hashing, Frida release
asset selection and digest verification, ADB trust diagnostics, HTTP Range download resume, safe ZIP extraction preflight, and
remote migration/import staging cleanup failure paths. The current suite has
116 tests.

## Project planning

Maintainer planning files are kept in the local checkout. Public release history
is represented by Git commits, tags, GitHub Releases, and this README.

## Typical workflow

```
1. ① Install   →  ensures SDK + cmdline-tools + licenses
2. ② AVDs      →  Browse online… → install a Google Play system image
                  → Create "MyEmulator" → Launch → let Play Store sign in
3. ③ Root      →  Root with latest Magisk → Cold Boot → Verify
                  → Modules → Shamiko + LSPosed → CA cert / Frida if needed
4. ④ Migrate   →  pair phone → pick packages → Start (3-10 minutes)
5. ⑤ Apps      →  Verify signatures on, drop APKs to install,
                  preset-debloat what you don't want
6. ⑥ Configure →  bump RAM / disk, pick a screen preset
7. ⑦ Logcat    →  watch for app errors during/after migration
8. ⑧ Console   →  spoof GPS / battery / network for testing
```

## Supply-chain hardening

Every binary downloaded by this app is hash-verified before use:

- **Magisk APK** — GitHub Releases API publishes a per-asset SHA-256
  `digest` field. AndroidEmulatorPlus cross-checks the downloaded file
  against that digest **and** against an in-tree manifest at
  `Resources/known-hashes.json` (curated, updated per release). Mismatch
  on either tier hard-fails and deletes the partial download.
- **Android command-line tools ZIP** — verified against the same in-tree
  manifest. Trust-on-first-use logging for unknown URLs / Magisk tags. Large
  downloads keep a `.download` sibling and resume with HTTP Range when the
  server supports it.
- **rootAVD bash script** — `git clone` pins a verified SHA
  (`RootService.RootAvdPinnedRef`). `master` is no longer trusted blindly.
- **APK signatures** — pre-install verification via `apksigner verify
  --print-certs`. When `aapt2` is available, the signer cert is also
  cross-checked against the installed package's cert; mismatch raises a
  typed ConfirmDialog (catches re-signed sideloads trying to upgrade a
  Play-Store-installed app).

## Privacy & network

The app only reaches out to:

- `https://dl.google.com/android/repository/…` — Android command-line tools ZIP.
- `https://developer.android.com/studio` — to discover the current cmdline-tools URL.
- `https://api.github.com/repos/topjohnwu/Magisk/…` — to discover the latest Magisk release.
- `https://gitlab.com/newbit/rootAVD.git` — `git clone` of the patcher.
- `https://github.com/<module-author>/<repo>/releases/latest` — only if you
  install a Magisk module from the curated catalog.
- `https://github.com/SysAdminDoc/AndroidEmulatorPlus/releases` — installed
  copies check and download Velopack update packages when update checks are
  enabled in Settings.

All traffic is HTTPS. No telemetry is sent, no accounts are required, and no
data ever leaves your machine. Crash details (unhandled exceptions only) are
written locally to `%LOCALAPPDATA%\AndroidEmulatorPlus\crash.log`. The
Settings dialog has a "no telemetry" reminder card.

## Persistence

| Path | Purpose |
|---|---|
| `%LOCALAPPDATA%\AndroidEmulatorPlus\settings.json` | Theme, SDK root override, media dir, HTTP proxy, auto-scrcpy, update checks, wizard-seen |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\logs\app-YYYYMMDD.log` | Rolling daily log mirror (kept 14 days) |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\crash.log` | Unhandled-exception trace |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\cache\` | rootAVD clone + downloaded Magisk APK |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\transfer\` | Per-package tarballs during migration |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\bloat.json` | User-override debloat presets (optional) |
| `%LOCALAPPDATA%\AndroidEmulatorPlus\presets\magisk-modules.json` | User-override Magisk module catalog (optional) |

## Theming

Four Catppuccin palettes ship: **Mocha** (dark, default), **Frappe**,
**Macchiato**, and **Latte** (light). Switch in Settings — the theme swaps live
thanks to the `DynamicResource` brush sweep; no restart needed.

## Acknowledgements

- [rootAVD](https://gitlab.com/newbit/rootAVD) by NewBit — the actual Magisk ramdisk patcher.
- [Magisk](https://github.com/topjohnwu/Magisk) by topjohnwu.
- [Catppuccin](https://github.com/catppuccin/catppuccin) Mocha + Latte palettes.
- Module catalog homepages: [LSPosed](https://github.com/LSPosed/LSPosed),
  [PlayIntegrityFork](https://github.com/osm0sis/PlayIntegrityFork),
  [TrickyStore](https://github.com/5ec1cff/TrickyStore),
  [Zygisk Detach](https://github.com/j-hc/zygisk-detach).

## License

MIT — see [LICENSE](LICENSE).
