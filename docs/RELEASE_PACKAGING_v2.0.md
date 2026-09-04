# OmniSorSe native release packaging

This living maintainer guide defines how current native artifacts are produced and
validated. Generated binaries belong in ignored `.artifacts` staging or GitHub
Actions artifacts, never normal Git history.

## Supported distribution artifacts

The native packaging workflow produces exactly:

- `OmniSorSe-v<version>-win-x64.zip`;
- `OmniSorSe-v<version>-win-x64-setup.exe`;
- `OmniSorSe-v<version>-macos-x64.dmg`;
- `OmniSorSe-v<version>-macos-arm64.dmg`;
- `OmniSorSe-v<version>-sbom.cdx.json`;
- `OmniSorSe-v<version>-SHA256SUMS.txt`.

No Linux installer is produced. Linux x64 remains a source-build preview.

`<version>` is the exact semantic product version from `Directory.Build.props`.
It may be a filename-safe prerelease such as `2.12.0-rc`. Prerelease identity
appears in package filenames, installer presentation, managed product metadata,
the build manifest, SBOM, checksums, and the Actions bundle name. Windows file
and assembly versions and standard macOS bundle-version fields use the numeric
core (`2.12.0.0` and `2.12.0` respectively); the full semantic identity remains
separately recorded and validated. Release-note lookup also uses the numeric
core so an RC embeds the reviewed notes for its intended release.

Prerelease packages contain `VALIDATION_BUILD.md`, which identifies the exact
source commit and states that the publisher-unsigned/unnotarized build is not a
stable or GA release and is intended for final real-world/manual validation. A
toolchain-provided macOS ad-hoc signature is permitted because it neither
identifies nor authenticates a publisher. The retained
AppId, install directory, and profile are required for genuine upgrade testing,
so prerelease installers show that notice before installation and warn that
opening the build can migrate retained state. Use a disposable machine/profile
or make a reviewed backup. Producing or downloading the Actions bundle alone
does not create a tag, publish packages, or create a GitHub Release; those remain
explicit reviewed publication steps.

## Reproducible entry points

- `eng/release/Build-WindowsArtifacts.ps1` creates the self-contained Windows
  publish, portable ZIP, and per-user Inno Setup installer. A local
  `-PortableOnly` validation path proves the ZIP/runtime without weakening the
  release workflow's mandatory installer path.
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
- `eng/release/New-ReleaseChecksums.ps1` requires all four distribution files
  plus the SBOM, creates the deterministic checksum list, then recomputes every
  checksum.
- `eng/release/New-ReleaseSbom.ps1` projects the reviewed dependency/license
  inventory into a source-bound CycloneDX 1.6 release SBOM without adding a
  production or build-time package dependency.

v2.11 package manifests also record `net10.0`, the target RID, the exact bundled
.NET runtime version, and `selfContained: true`. Validators require the .NET
host/runtime, native SQLite, and native Skia assets before accepting a package.

`--package-smoke-test` does not open a window. It composes and initializes the
real services, persistence, watchers, indexing, and optional graph runtime,
then performs bounded shutdown. It proves that the packaged executable and
native dependencies start and stop on the runner; it does not claim interactive
UX, accessibility, watcher, OCR, Ollama, battery, or real-filesystem behavior.

## Native CI

`.github/workflows/release-packaging.yml` is manually dispatched only after an
exact main commit is green. Its required `ref` input is the full 40-character
commit, and its version input must exactly match that source's
`OmniSorSeVersion`. Windows packages run on `windows-latest`; Intel
and Apple Silicon packages run on native macOS runner families. The checksum
job downloads only the native job outputs and publishes one complete package
bundle.

The regular and release-validation workflows use least-privilege read
permissions, disable checkout credential persistence, force restore, audit
vulnerabilities, and validate the exact requested ref/commit. Critical official
Actions are pinned to immutable full commit SHAs. The major release remains in
an adjacent comment. To update a pin, resolve the reviewed official tag, inspect
upstream release notes and its source diff, replace the SHA, and rerun ordinary
plus release validation. Never expose release/signing secrets to pull-request
code.

Every release build receives an explicit semantic version and exact 40-character
source commit. Binary product/file metadata, package filenames, app-bundle
metadata, and `OmniSorSe.build.json` must agree before checksums are created.
Shell-facing workflow inputs are validated and passed through environment
variables rather than interpolated into commands. The workflow fails closed if
the maintained Windows runner does not already provide Inno Setup; it does not
install a mutable compiler fallback during trusted packaging.

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
credentials exist in the repository infrastructure reviewed for v2.11.
Artifacts are therefore produced publisher-unsigned and macOS packages are
unnotarized. A toolchain-provided ad-hoc macOS signature can be present; the
validator permits only an absent or ad-hoc signature and rejects Developer ID
or other publisher identity.
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
4. Download and independently audit all six files and verify checksums.
5. Create the reviewed annotated version tag at that exact commit and push it.
6. Publish the GitHub Release with all six artifacts and the reviewed release
   notes.
7. Re-download or query release assets to verify names, sizes, and checksums.
8. Only then execute the reviewed, reachability-proven branch cleanup plan.
