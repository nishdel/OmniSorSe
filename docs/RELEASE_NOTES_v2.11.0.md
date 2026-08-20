# OmniSorSe v2.11.0 — Supported Runtime & Platform Readiness

**Status:** Unreleased release candidate; no package, tag, or publication is
claimed by this document.

## What changed

- All production, test, protocol, and harness projects now target .NET 10 LTS
  (`net10.0`) through the repository’s central SDK/runtime authority.
- Existing package versions remain unchanged; no production dependency was
  added or upgraded for the migration.
- Windows/macOS package manifests now identify target framework, RID, bundled
  runtime version, self-contained status, semantic version, configuration, and
  exact source commit, with stricter native-asset validation.
- Native Windows, Ubuntu, and macOS CI includes a bounded self-contained publish
  and package-smoke path. Release packaging remains separate and never publishes
  automatically.
- Critical GitHub Actions are pinned to immutable upstream commit SHAs.
- Filesystem Created range filters now match the filesystem-created provenance
  already used by the Created Year facet; EXIF capture time remains separate.
- Current plugins advertise `net10.0`; managed legacy `net8.0` manifests remain
  supported by the net10 host.

## Preserved behavior

Schema remains 6 and Explorer Protocol remains v1. Existing v2.10 profiles and
`.oms-state` backups require no format migration. Search ranking, facets, Saved
Views, Smart Tags, classification, organization recipes, Change Plans,
journalling, reconciliation, Undo, backup/restore, Forget, health, optional AI,
and OmniBrille are not redesigned.

No updater, Linux installer, entity/date intelligence subsystem, classifier
change, new recipe/token, cloud service, protocol expansion, or new production
dependency is included.

## Platform and trust boundary

Windows x64 remains primary. macOS packages remain conservative and do not gain
source-file mutation merely for parity. Linux x64 remains source-build preview.
Unsigned/unnotarized status must be reported honestly when release credentials
are unavailable. Checksums and embedded source provenance are provided but do
not authenticate an unsigned publisher.

See [Supported Runtime & Platform Readiness](SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md),
the [Platform Compatibility Matrix](PLATFORM_COMPATIBILITY_MATRIX.md), and the
[v2.11 manual addendum](MANUAL_TESTING_v2.11.md).
