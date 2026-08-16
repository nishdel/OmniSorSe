# OmniSorSe v2.11 Supported Runtime & Platform Readiness

**Status:** Unreleased implementation candidate

**Branch:** `v2.11-supported-runtime-platform-readiness`

## Purpose

v2.11 moves every production, test, protocol-contract, and test-harness project
to `net10.0`, selected centrally by `global.json` with .NET SDK 10.0.400. It is
a supportability release: schema 6, Explorer Protocol v1, Search ranking, Smart
Tag authority, organization recipes, Change Plans, recovery, backup/restore,
Forget, and OmniBrille separation remain unchanged.

The compatibility spike restored all existing package versions unchanged and
produced a zero-warning Release build. Two applicable .NET 10 changes required
narrow source corrections: C# 14 reserves `field` in property accessors, and
CA2024 rejects synchronous `StreamReader.EndOfStream` checks inside async
methods. Solution-wide load also exposed that the bounded, non-backtracking
Search query parser's former 100 ms wall-clock regex timeout could treat
scheduler starvation as an invalid query; it is now a still-bounded one second
without changing grammar or ranking. No production dependency was added or upgraded. Three net8-only
transitive compatibility packages disappeared from the resolved graph and were
removed from the dependency inventory.

## Runtime authority and deployment

- All solution projects target `net10.0`; accidental mixed target frameworks
  are rejected by repository policy tests.
- `global.json` selects SDK 10.0.400 with `latestFeature` roll-forward inside
  that feature band. Release CI installs the selected SDK.
- Windows and macOS release packages remain self-contained, non-single-file,
  and untrimmed. They do not require a machine-wide .NET runtime.
- Runtime manifests record product version, source commit, configuration,
  target framework, RID, bundled runtime version, and self-contained status.
- Plugin manifests produced by current code advertise `net10.0`. Managed
  `net8.0` manifests remain accepted as an explicit host-compatibility bridge;
  native plugins must still match the active RID.
- No NativeAOT, aggressive trimming, self-updater, Linux installer, package
  manager, schema migration, or backup-format migration is introduced.

A like-for-like self-contained `win-x64` publish measured 232,682,219 bytes
(221.90 MiB) at the exact v2.10 commit and 239,121,206 bytes (228.04 MiB) in
the precommit v2.11 validation tree: +6,438,987 bytes (+6.14 MiB, 2.77%). The
small runtime growth is accepted; trimming/AOT was not enabled to disguise it.

As reviewed on 2026-08-17, the [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
lists .NET 10 as active LTS through 2028-11-14. Release maintainers must recheck the official support policy and
ship a current servicing patch before each release rather than treating that
date or the SDK feature band as a permanent guarantee.

## Platform evidence model

“Compiles,” “automated tests pass natively,” “package smoke passes,” “installer
tested,” “signed,” and “notarized” are separate claims.

- **Windows x64** is primary. Local Windows validation covers the full suite,
  cross-RID publish, package metadata/native assets, and bounded package smoke.
  Installer lifecycle and interactive UI/accessibility remain explicit gates
  unless the final validation record says they ran.
- **macOS x64/arm64** have native CI build/package/smoke paths. A Windows
  cross-publish is compile/package-asset evidence only. Source-file mutation
  remains disabled by the existing platform capability boundary.
- **Linux x64** remains source-build preview. Native CI builds, tests, publishes,
  and runs a bounded package smoke. No AppImage, Flatpak, Snap, deb, or rpm is
  promised. Existing conservative mutation boundaries remain unchanged.

Unsigned artifacts remain valid engineering outputs but are not authenticated
publisher identity. The release workflow accepts future signing/notarization
credentials without storing credentials in the repository. SHA-256 checksums
detect changed bytes; source/runtime manifests provide traceability; neither is
a substitute for a trusted signature.

## Package and CI trust

The Windows ZIP/installer and macOS DMGs are built only for an exact requested
ref and source SHA. Validators compare filenames, binary version metadata,
manifest version/SHA/RID/runtime, target framework, required SQLite/Skia/.NET
native assets, and forbidden development/user-state files. Windows validation
uses a controlled per-user installation, package smoke, uninstall, and profile
preservation marker. macOS validation mounts each DMG on the matching native
architecture and runs the bounded smoke host.

Critical GitHub Actions are pinned to immutable commit SHAs with their major
version documented in comments. Update them by resolving the reviewed official
tag to its commit, reviewing upstream release notes/diffs, replacing the SHA,
and rerunning both ordinary and release validation. Workflow permissions remain
read-only and checkout credentials are not persisted.

NuGet policy keeps explicit non-floating versions in project files, validates
the complete direct/transitive resolved graph against
`docs/dependency-licenses.json`, performs forced/no-cache release restores, and
audits vulnerabilities. Per-project lock files were not introduced: RID-specific
self-contained restore graphs would create broad lock churn across 18 projects
without changing the runtime dependency boundary. Exact package inventory plus
artifact manifests is the chosen functional reproducibility contract; bitwise
identical archives are not promised.

Release validation also generates a source-bound CycloneDX 1.6 JSON SBOM from
the reviewed direct/transitive dependency inventory. It is checksummed with the
native artifacts and introduces no production or build-time package dependency.

## Compatibility and known limits

- Existing schema-6 profiles and v2.10 `.oms-state` backups require no rewrite.
- Released schema-5 profiles still use the established tested migration path to
  schema 6; the framework migration adds no database step.
- “Created on/after/before” now consistently means filesystem-created time,
  matching the Created Year facet. Media capture time remains distinct evidence.
  Saved View serialization is unchanged.
- Optional Tesseract, ffmpeg/ffprobe, whisper.cpp, and Ollama integrations remain
  user-managed and non-fatal when absent. CI does not download large models or
  optional executables.
- Cross-target compilation from Windows is not native macOS/Linux execution.
- Signing, notarization, SmartScreen, Gatekeeper, real removable/network drives,
  native optional tools, DPI, and assistive-technology behavior require the
  manual/release evidence listed in the v2.11 checklist.

The applicable .NET 10 compatibility review covered filesystem, JSON,
configuration, networking, process, native loading, globalization, threading,
and SDK changes. The repository does not use affected tar, LDAP, ASP.NET,
WinForms/WPF, custom OpenSSL/ICU override, trimming, or single-file paths.
Existing serialization/configuration, process, locale, filesystem, and native
loading tests exercise the relevant used paths. No runtime behavior workaround
was required beyond the narrow source/analyzer corrections recorded above.

See the [Platform Compatibility Matrix](PLATFORM_COMPATIBILITY_MATRIX.md),
[Installation](INSTALLATION.md), and [v2.11 Manual Testing](MANUAL_TESTING_v2.11.md).
