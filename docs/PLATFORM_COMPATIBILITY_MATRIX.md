# Platform compatibility matrix

**Document type:** Living platform support and evidence matrix

**Current source:** v1.9

**Last reviewed:** 2026-07-30

OpenSorSe is a Windows-first desktop application with a conservative Linux x64
source-build preview. The solution also builds/tests in macOS CI, but that does
not make macOS a supported product or enable its fail-closed mutation path.

## Status vocabulary

| State | Meaning |
| --- | --- |
| Supported | Implemented and backed by repeatable automated plus relevant available runtime/manual evidence on the named platform. |
| Preview | Implemented behind explicit platform contracts with useful native automated evidence, but important desktop/filesystem/manual limitations remain. |
| Build/test only | Native CI or target compilation exists; product/runtime support is not claimed. |
| Unverified | Implementation exists, but the required named-platform execution evidence is absent. |
| Unavailable | Deliberately disabled because the current platform contract cannot provide the required guarantee. |

Framework portability, target compilation, native CI, desktop startup, and safe
file mutation are separate claims.

## Evidence baseline

- The v1.6 validated commit completed native `windows-latest`,
  `ubuntu-latest`, and `macos-latest` CI. Each runner restored, built and tested
  Debug/Release, rejected skips, checked formatting/analyzers/whitespace, and
  ran repository-policy tests. See the
  [v1.6 Validation Report](V1.6_VALIDATION_REPORT.md).
- The maintainer reported v1.6 interactive smoke validation with no
  release-blocking issue, but did not record detailed per-platform desktop,
  filesystem, assistive-technology, Tesseract, Ollama, or plugin observations.
- v1.7 and v1.8 local Windows validation built `win-x64`, `linux-x64`,
  `osx-x64`, and `osx-arm64` targets. v1.9 repeats this gate for the relationship
  schema and verifies the expected native SQLite
  asset in each output. Those cross-target builds ran on Windows and do not
  prove native Linux/macOS execution. See the
  [v1.7](V1.7_VALIDATION_REPORT.md) and
  [v1.8](V1.8_VALIDATION_REPORT.md) and
  [v1.9](V1.9_VALIDATION_REPORT.md) validation reports.
- v1.9 interactive platform/manual validation remains unchecked.

## Current support matrix

| Capability | Windows | Linux x64 | macOS | Evidence and limits |
| --- | --- | --- | --- | --- |
| Restore/build/automated tests | Supported | Preview | Build/test only | Native three-host CI is proven for earlier releases. Current v1.9 local evidence is Windows execution plus cross-target compilation; exact-tip hosted results are external post-commit evidence. |
| Avalonia Desktop startup/composition | Supported | Preview | Unverified | Windows is the primary runtime. Linux requires a graphical session/native dependencies and remains manually limited. macOS product support is not claimed. |
| Path normalization and confinement | Supported | Preview | Build/test only | Windows and Linux semantics have focused adapters/tests; POSIX execution passed the v1.6 native suite. macOS retains fail-closed product limits. |
| Portable filename policy | Supported | Preview | Build/test only | Recipes can request current-platform, portable, or Windows-compatible output. Import does not silently rewrite policy. |
| Stable file identity | Supported | Preview | Unverified | Windows uses volume/file index; Linux x64 uses device/inode with an explicit fallback. Identity remains bounded evidence, not permanent identity. |
| Read-only scanning/metadata/duplicates | Supported | Preview | Build/test only | Links are skipped and item failures are isolated. Network, removable, FUSE, permissions, and mount behavior can be weaker. |
| Embedded SQLite durable index | Supported | Unverified runtime | Unverified runtime | v1.9 Windows tests cover schema/provider and relationship behavior. Target outputs select the expected native SQLite library for all four runtime identifiers; native Linux/macOS interactive execution is not recorded in the source report. |
| Search over available indexed data | Supported | Unverified runtime | Unverified runtime | Deterministic ranking is portable; full status also depends on current native provider/Desktop execution. Compatible filename/metadata Search can degrade independently. |
| Change Plan rename/move/create | Supported | Preview / manual verification pending | Unavailable | Linux operations require current-platform link, root, identity, permission, and same-filesystem checks. macOS mutation remains disabled. |
| Cross-filesystem move | Unavailable | Unavailable | Unavailable | OpenSorSe does not silently implement move as copy/delete. |
| Operation Journal, rollback, recovery, Undo | Supported | Preview / manual verification pending | Unavailable | Transaction-like compensating operations are not universal filesystem transactions. Unsafe inverse work blocks. |
| Watched Folders | Supported with documented limits | Preview / manual verification pending | Unverified | Notifications are hints; reconciliation is authoritative. Linux depends on inotify resources and filesystem behavior. |
| Workflows and recipes | Supported | Preview | Build/test only | Declarative data is portable only within its recorded filename/platform policy and available dependencies. |
| Managed plugins | Supported | Preview | Unverified | External plugins are in-process. Native plugins require an exact supported runtime identifier. |
| Native plugin dependencies | Supported with limitations | Preview / package-specific | Unavailable | Native content requires explicit compatible RIDs. Binary portability is never inferred. |
| OCR through external Tesseract | Supported with limitations | Unverified runtime | Unverified runtime | Configured path/PATH discovery checks the external tool before bounded invocation. Engine/language packages are user managed. |
| Ollama-compatible AI | Supported with limitations | Unverified runtime | Unverified runtime | Uses a configured HTTP endpoint and is not auto-launched. A custom endpoint may be remote. |
| Open/reveal with file manager | Supported | Preview / manual verification pending | Unavailable | Exact paths use the platform desktop association API; no constructed shell command is used. |
| Application data locations | Supported | Preview | Build/test only | Windows preserves LocalAppData. Linux uses XDG config/data/state/cache categories. Exact current paths are exposed in Platform Diagnostics. |
| Installer/updater/current package | Unavailable | Unavailable | Unavailable | The repository contains only a historical v1.0 Windows portable snapshot. No v1.9 package or installer is claimed. |

## Filesystem limitations

- Volume/file-index and device/inode values identify an object on a particular
  filesystem at a point in time. They may not survive copies, migration,
  snapshots, restore, containers, or identifier reuse.
- Network filesystems, removable media, FUSE, bind mounts, containers, and
  copy-on-write systems may provide weaker identity, locking, timestamp,
  watcher, free-space, transaction, or durability behavior.
- Linux permissions can include ACL, capability, namespace, and mount rules not
  described by Unix mode bits. OpenSorSe never elevates, runs `sudo`, changes
  ownership, or broadly changes permissions.
- Symbolic links, Windows reparse points, junctions, and detected mount escapes
  are not followed through scanning, reconciliation, package traversal, or
  Change Plan validation.
- Watcher delivery may be duplicated, reordered, coalesced, lost, or stopped.
  Startup, resume, reconnect, overflow, periodic, and manual reconciliation
  remain necessary.
- Same-filesystem verification is required for supported moves. Failure to
  establish a safety fact blocks mutation rather than enabling a weaker
  fallback.

## How to update this matrix

A stronger claim requires evidence at the relevant level:

1. portable compilation;
2. target-specific asset selection;
3. native automated tests;
4. native Desktop startup;
5. real filesystem/watcher/external-tool behavior;
6. manual accessibility and interaction;
7. packaging/distribution validation where claimed.

Record each platform independently. Link an immutable validation report or
hosted run; do not infer success from a workflow definition.

See [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md),
[Safety and Privacy](SAFETY_AND_PRIVACY.md), and
[Release Status](RELEASE_STATUS.md).
