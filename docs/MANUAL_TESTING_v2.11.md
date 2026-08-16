# OmniSorSe v2.11 Manual Validation Addendum

**Status:** Unreleased; every unchecked item is genuinely unperformed.

Use the [v2.10 master matrix](MANUAL_TESTING_v2.10.md) for inherited product,
recovery, scale, accessibility, and failure gates. This addendum contains only
runtime/platform/package work introduced by v2.11.

## Windows x64

- [ ] Confirm About, diagnostics, package manifest, file/product version, and
  installer all show the same v2.11 version and exact commit.
- [ ] Launch the self-contained portable ZIP on a clean machine without a
  separately installed .NET runtime.
- [ ] Install per-user, first-launch, close, upgrade from the published v2.4
  installer/profile, confirm schema-5-to-6 migration and user-state retention,
  then uninstall/reinstall and confirm application data remains.
- [ ] Run the installer while OmniSorSe is open; confirm Restart Manager requests
  closure/refuses unsafe replacement and no journal/profile damage occurs.
- [ ] Record Windows signature and SmartScreen status; do not mark signed unless
  the exact executable and installer signatures verify.
- [ ] Repeat keyboard, screen-reader, 100/125/150% DPI, and issues #29/#31 smoke
  from the v2.10 matrix on the packaged .NET 10 build.
- [ ] Confirm configured/absent Tesseract, ffmpeg/ffprobe, whisper.cpp, Ollama,
  and OmniBrille readiness on the package without downloading them automatically.

## macOS x64 and arm64

- [ ] On native Intel macOS, mount the x64 DMG, inspect architecture/version/SHA,
  launch, create a disposable profile, scan/Search, health-check, backup/restore,
  and exit cleanly.
- [ ] Repeat on native Apple Silicon with the arm64 DMG.
- [ ] Record signature, notarization, staple, and Gatekeeper status separately;
  an unsigned override is not notarization evidence.
- [ ] Exercise NFC/NFD-equivalent names, case behavior, permissions, and symlinks
  on disposable roots. Confirm unsupported mutation remains blocked.
- [ ] Perform keyboard, VoiceOver, and window-scaling smoke if normal macOS
  support is to be claimed.
- [ ] Check optional-tool discovery/version/error behavior for tools actually
  installed on each architecture.

## Linux x64 preview

- [ ] On a representative supported distribution, install SDK 10.0.400 (or the
  documented feature-band-compatible SDK), restore/build, launch, scan/Search,
  health-check, backup/restore, and exit cleanly.
- [ ] Exercise case-sensitive names, owner permissions, symlinks, watcher limits,
  and a missing/remounted source on disposable data.
- [ ] Perform keyboard/basic desktop smoke and record the desktop environment.
- [ ] Confirm documentation says source-build preview and offers no installer.

## Package replacement and trust

- [ ] Verify SHA-256 for every candidate artifact and compare package filenames,
  manifests, binary metadata, RID, bundled .NET 10 runtime, and exact commit.
- [ ] Inspect every artifact for profiles, `.oms-state` backups, logs, databases,
  test results, credentials, developer paths, models, and unintended executables.
- [ ] Follow the supported update path: download from the trusted release, verify
  provenance/checksum/signature status, close OmniSorSe, install/replace, launch,
  and inspect health/profile migration. No in-app updater exists.
