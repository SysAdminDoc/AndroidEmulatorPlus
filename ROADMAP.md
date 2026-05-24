# Roadmap

- [ ] System-image picker that can `sdkmanager` new images on demand (currently only enumerates what's already installed).
- [ ] First-launch wizard: install SDK + create AVD + root + migrate, all guided.
- [ ] Magisk module manager view (install Zygisk, DenyList, LSPosed from inside the tool).
- [ ] OBB transfer pass (currently skipped because game OBBs are huge — opt-in toggle).
- [ ] Per-app data export to ZIP for cold archival, plus a "Restore from ZIP" flow.
- [ ] Snapshot manager — pick from saved emulator states (boot snapshots vs. user-named).
- [ ] Linux + macOS builds (Avalonia or MAUI port).
- [ ] APK signature verification before install (warn on mismatch with already-installed package).
- [ ] Bandwidth-aware `adb` push/pull with progress per-package, not just per-batch.
- [ ] Optional desktop shortcut creation right from the AVDs tab.
