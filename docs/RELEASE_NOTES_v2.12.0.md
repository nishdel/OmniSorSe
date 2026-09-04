# OmniSorSe v2.12.0-rc — Trusted Relationships & Context

**Status:** GitHub prerelease candidate for final real-world/manual validation
before v2.12.0 GA. This is not a stable release. Windows artifacts are unsigned;
macOS artifacts are unsigned and unnotarized.

## Downloads

Use only the assets attached to the canonical
[v2.12.0-rc GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.12.0-rc):

- `OmniSorSe-v2.12.0-rc-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.12.0-rc-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.12.0-rc-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.12.0-rc-macos-arm64.dmg` — Apple Silicon macOS package;
- `OmniSorSe-v2.12.0-rc-sbom.cdx.json` — CycloneDX 1.6 SBOM;
- `OmniSorSe-v2.12.0-rc-SHA256SUMS.txt` — SHA-256 hashes for the other five
  release files.

Every package is bound to the exact tagged `main` commit through binary version
metadata, `OmniSorSe.build.json`, the SBOM, and the checksum manifest. Verify the
named SHA-256 value before opening a package. A checksum detects changed bytes;
it does not authenticate an unsigned publisher.

## Important upgrade and trust boundary

The visible product is OmniSorSe, but established OpenSorSe application-data,
installer, bundle, schema, assembly, and namespace identifiers remain where
compatibility requires them. The RC installer can replace an existing OmniSorSe
installation, and first launch can migrate the retained OpenSorSe profile and
schema. Close the application first. Use a disposable account/machine or make a
reviewed backup before testing an upgrade.

Windows SmartScreen may report an unrecognized publisher. macOS Gatekeeper may
require a reviewed override. No signature, notarization, or publisher-authenticity
claim is made.

## What changed

v2.12 makes the existing relationship system easier to trust and correct. Direct
Related Files no longer depends on the optional Knowledge Graph. Evidence is
grouped into capped independent families, noisy correlated signals cannot stack
without bounds, and semantic or AI-derived evidence cannot establish a
relationship alone.

Users can mark a pair **Related**, **Not Related**, or return it to **Use automatic
result**. Negative corrections remain discoverable for reversal. Multiple typed
edges are shown as one related target with a bounded explanation. Search and
Files provide direct entry points while exact filename, stem, and prefix intent
remain dominant.

Large-library work remains bounded through indexed candidate buckets, compact
batch hydration, a 512 defensive ceiling, and resumable relationship-only
version refresh. Explorer Protocol stays at 1.0 and returns aggregated,
authorized, opaque related context without adding writes.

Logical `.oms-state` format 2 adds pair and user-authored Smart Collection
authority. Format-1 import remains supported. Generated edges, evidence,
automatic membership, and graph projections remain rebuildable and are not
backed up. Restore never guesses unresolved identity, and a format-1 import does
not clear newer Smart Collection authority that its older payload cannot carry.

Architecture remains intentionally stable: .NET 10, schema 6, no new production
dependency, optional unchanged AI, optional derived Knowledge Graph, Smart
Collections as grouping authority, and reviewed Change Plans as the only
file-mutation path.

## Automated validation completed

The release process requires the exact tagged `main` commit to pass:

- no-cache restore, zero-warning Debug and Release builds, and the complete test
  suite on Windows, Ubuntu, macOS Intel, and macOS Apple Silicon;
- formatting, analyzers, documentation/repository policy, dependency and
  vulnerability checks, and native package smoke;
- exact source/version/RID/runtime provenance, portable/app-bundle content and
  forbidden-file checks, CycloneDX SBOM generation, and SHA-256 verification;
- controlled Windows per-user install, installed production-composition smoke,
  stop/uninstall, shortcut and uninstall-entry cleanup, and user-data
  preservation;
- native macOS DMG mount, architecture/runtime inspection, composition smoke,
  and explicit unsigned/unnotarized checks.

These are automated and controlled checks. They do not substitute for the manual
validation below.

## Manual validation still required

The inherited v2.10 matrix and v2.11/v2.12 addenda remain unchecked. Important
remaining work includes:

- real-library relationship quality, pair corrections, scale, cancellation, and
  removable/offline source identity;
- normal interactive Windows install, SmartScreen, installer wizard, Restart
  Manager, v2.4 profile upgrade/migration, uninstall/reinstall, and rollback;
- keyboard, screen-reader/VoiceOver, DPI/scaling, and broader desktop UX checks;
- actual optional Tesseract, ffmpeg/ffprobe, whisper.cpp, Ollama, plugins, and
  OmniBrille configurations;
- broader native Windows filesystem behavior and native Linux/macOS interactive
  and real-filesystem scenarios.

See [Release Status](RELEASE_STATUS.md), the
[v2.12 manual addendum](MANUAL_TESTING_v2.12.md), the inherited
[v2.11 addendum](MANUAL_TESTING_v2.11.md), and the
[v2.10 master matrix](MANUAL_TESTING_v2.10.md) for the exact evidence boundary.
