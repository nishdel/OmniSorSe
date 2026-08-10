# OpenSorSe v2.0 native release packaging

This living maintainer guide defines how native v2.0 artifacts are produced and
validated. Generated binaries belong in ignored `.artifacts` staging or GitHub
Actions artifacts, never normal Git history.

## Supported distribution artifacts

The native packaging workflow produces exactly:

- `OpenSorSe-v2.0.0-win-x64.zip`;
- `OpenSorSe-v2.0.0-win-x64-setup.exe`;
- `OpenSorSe-v2.0.0-macos-x64.dmg`;
- `OpenSorSe-v2.0.0-macos-arm64.dmg`;
- `OpenSorSe-v2.0.0-SHA256SUMS.txt`.

No Linux installer is produced. Linux x64 remains a source-build preview.

## Reproducible entry points

- `eng/release/Build-WindowsArtifacts.ps1` creates the self-contained Windows
  publish, portable ZIP, and per-user Inno Setup installer.
- `eng/release/Validate-WindowsArtifacts.ps1` inspects the ZIP, installs into a
  controlled directory, runs production composition through
  `--package-smoke-test <isolated-data-root>` with synthetic application data, uninstalls,
  checks installation-file removal, and verifies user-data preservation.
- `eng/release/Build-MacArtifacts.sh` runs only on native macOS, publishes the
  selected architecture, creates a conventional `.app` bundle and icon,
  validates `Info.plist` and native architecture/SQLite, and creates a DMG.
- `eng/release/Validate-MacArtifact.sh` mounts the DMG, inspects bundle metadata,
  validates native dependencies, audits forbidden files, and runs the same
  non-interactive production-composition smoke probe.
- `eng/release/New-ReleaseChecksums.ps1` requires all four distribution files,
  creates the deterministic checksum list, then recomputes every checksum.

`--package-smoke-test` does not open a window. It composes and initializes the
real services, persistence, watchers, indexing, and optional graph runtime,
then performs bounded shutdown. It proves that the packaged executable and
native dependencies start and stop on the runner; it does not claim interactive
UX, accessibility, watcher, OCR, Ollama, battery, or real-filesystem behavior.

## Native CI

`.github/workflows/release-packaging.yml` is manually dispatched only after an
exact main commit is green. Its required `ref` input should be the exact release
commit or final annotated tag. Windows packages run on `windows-latest`; Intel
and Apple Silicon packages run on native macOS runner families. The checksum
job downloads only the native job outputs and publishes one complete release
bundle.

The regular validation workflow uses the maintained Node.js 24 action majors:
`actions/checkout@v7` and `actions/setup-dotnet@v6`. Packaging uses current
official `upload-artifact@v7` and `download-artifact@v7` majors.

## Installer and data policy

The Windows installer defaults to a per-user location below Local AppData,
creates a Start Menu shortcut, registers an uninstaller, and does not require
administrator privileges for the default path. Uninstall removes application
files but preserves `%LOCALAPPDATA%\OpenSorSe`. Users may remove that data
separately after reviewing whether recovery, Undo, settings, index, or history
is still needed.

Portable and app-bundle payloads include product binaries, managed/native
runtime dependencies, the license, notices, dependency inventory, installation
guidance, and release notes. They must not include source, tests, PDB/TRX files,
databases, indexes, logs, diagnostic exports, settings, credentials, secrets,
tokens, or machine-specific paths.

## Signing and notarization

No established Windows code-signing or Apple Developer signing/notarization
credentials exist in the repository infrastructure reviewed for v2.0.0.
Artifacts are therefore produced unsigned and macOS packages are unnotarized.
Do not add secrets to source, print secret values, create an improvised trust
scheme, or describe checksum verification as publisher authentication.

If trusted credentials are added later, they must use GitHub encrypted secrets
or another reviewed secure signing service, least privilege, native verification
(`Get-AuthenticodeSignature`, `codesign`, `spctl`, notarization/stapling), and
updated release documentation.

## Publication order

1. Validate and merge the exact release source into `main` without rewriting
   history.
2. Require exact-main Windows, Ubuntu, and macOS CI success.
3. Dispatch native packaging for that exact main commit.
4. Download and independently audit all five files and verify checksums.
5. Create annotated tag `v2.0.0` at that exact commit and push it.
6. Publish the GitHub Release with all five artifacts and the reviewed release
   notes.
7. Re-download or query release assets to verify names, sizes, and checksums.
8. Only then execute the reviewed, reachability-proven branch cleanup plan.
