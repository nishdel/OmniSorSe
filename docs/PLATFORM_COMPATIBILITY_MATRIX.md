# Platform compatibility matrix

**Document type:** Living platform support and evidence matrix

**Current source:** unreleased OmniSorSe v2.12 candidate on the v2.5-v2.11 RC stack;
v2.4.0 remains the latest published release

**Last reviewed:** 2026-08-17

OmniSorSe is a Windows-first desktop application with a conservative Linux x64
source-build preview. v2.0.0 added native Intel/Apple Silicon macOS packages and
package startup/shutdown probes while retaining conservative, fail-closed
mutation limits. Native packaging is not evidence of broad interactive or
filesystem validation.

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

## v2.11 evidence ledger

The v2.11 source targets .NET 10 LTS. Evidence must be read by column; a build
does not imply a native launch, and a package smoke does not imply broad desktop
or filesystem validation.

| Platform | Compiles/publishes | Full automated suite | Native bounded smoke | Package | Installer lifecycle | Mutation | Signing trust |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Windows x64 | Locally and in CI | Locally; CI configured | Locally/package workflow | Self-contained ZIP | Per-user Inno validation path; manual running-app/upgrade gate remains | Existing supported Change Plan boundary | Unsigned unless exact candidate signatures are recorded |
| macOS x64 | Cross-target locally; native CI configured | Native CI configured | Native package workflow configured; no local-Windows execution claim | `.app`/DMG workflow | Copy-to-Applications/manual replacement | Disabled/conservative | Unsigned/unnotarized unless exact candidate evidence is recorded |
| macOS arm64 | Cross-target locally; native CI configured | Native CI configured | Native package workflow configured; no local-Windows execution claim | `.app`/DMG workflow | Copy-to-Applications/manual replacement | Disabled/conservative | Unsigned/unnotarized unless exact candidate evidence is recorded |
| Linux x64 | Cross-target locally; native CI configured | Native CI configured | Source-build smoke configured; no local-Windows execution claim | Publish output only | None | Existing preview boundary; no expansion in v2.11 | Not applicable—no released Linux package |

Hosted workflow configuration becomes evidence only after an exact run result is
reviewed and linked. Native/manual observations belong in the
[v2.11 addendum](MANUAL_TESTING_v2.11.md).

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
- v1.9 and v2.0 interactive platform/manual validation remain unchecked.
- The v2.0 release gate runs the complete suite on native Windows, Ubuntu, and
  macOS, compiles all four runtime identifiers, verifies SQLite assets, and
  builds/inspects Windows and both native macOS packages. Exact evidence belongs
  in the [v2.0 Validation Report](V2.0_VALIDATION_REPORT.md); no stronger
  interactive claim follows from automation alone.

## Current support matrix

| Capability | Windows | Linux x64 | macOS | Evidence and limits |
| --- | --- | --- | --- | --- |
| Restore/build/automated tests | Supported | Preview | Preview | v2.0 release CI runs the complete Debug/Release suite and policies on native Windows, Ubuntu, and macOS. |
| Avalonia Desktop startup/composition | Supported | Preview | Preview | Windows is primary. Native packages run a non-interactive production-composition startup/shutdown probe; this is not broad UI validation. Linux requires a graphical session/native dependencies. |
| Path normalization and confinement | Supported | Preview | Build/test only | Windows and Linux semantics have focused adapters/tests; POSIX execution passed the v1.6 native suite. macOS retains fail-closed product limits. |
| Portable filename policy | Supported | Preview | Build/test only | Recipes can request current-platform, portable, or Windows-compatible output. Import does not silently rewrite policy. |
| Stable file identity | Supported | Preview | Unverified | Windows uses volume/file index; Linux x64 uses device/inode with an explicit fallback. Identity remains bounded evidence, not permanent identity. |
| Read-only scanning/metadata/duplicates | Supported | Preview | Build/test only | Links are skipped and item failures are isolated. Network, removable, FUSE, permissions, and mount behavior can be weaker. |
| Embedded SQLite durable index | Supported | Preview | Preview | Native suites and package smoke probes initialize provider-isolated SQLite; target packages verify the expected native library. Real-filesystem breadth remains manually limited. |
| Knowledge Graph/decision sidecars | Supported, default off | Preview, default off | Preview, default off | Provider-neutral schema-1 SQLite sidecars, recovery, and regressions run natively; interactive quality/accessibility evidence remains pending. |
| Search over available indexed data | Supported | Preview | Preview | Deterministic ranking and provider regressions run natively. Compatible filename/metadata Search degrades independently from optional deeper data. |
| Change Plan rename/move/create | Supported | Preview / manual verification pending | Unavailable | Linux operations require current-platform link, root, identity, permission, and same-filesystem checks. macOS mutation remains disabled. |
| Cross-filesystem move | Unavailable | Unavailable | Unavailable | OmniSorSe does not silently implement move as copy/delete. |
| Operation Journal, rollback, recovery, Undo | Supported | Preview / manual verification pending | Unavailable | Transaction-like compensating operations are not universal filesystem transactions. Unsafe inverse work blocks. |
| Watched Folders | Supported with documented limits | Preview / manual verification pending | Unverified | Notifications are hints; reconciliation is authoritative. Linux depends on inotify resources and filesystem behavior. |
| Workflows and recipes | Supported | Preview | Build/test only | Declarative data is portable only within its recorded filename/platform policy and available dependencies. |
| Managed plugins | Supported | Preview | Unverified | External plugins are in-process. Native plugins require an exact supported runtime identifier. |
| Native plugin dependencies | Supported with limitations | Preview / package-specific | Unavailable | Native content requires explicit compatible RIDs. Binary portability is never inferred. |
| OCR through external Tesseract | Supported with limitations | Unverified runtime | Unverified runtime | Configured path/PATH discovery checks the external tool before bounded invocation. Engine/language packages are user managed. |
| v2.2 deterministic image metadata/thumbnails | Supported with bounded automated and controlled Windows evidence | Build/test only | Build/test and native package smoke only | In-process header/EXIF parsing and Skia encoding are bounded. Windows validation exercised generated JPEG/PNG, orientation-aware cache reuse, and real Tesseract OCR/Search; broad interactive testing remains incomplete. |
| v2.2 audio/video metadata and frames | Supported with controlled user-managed FFmpeg/ffprobe 9.0 evidence against generated MP3, WAV, FLAC, M4A, and MP4 plus bounded frame extraction/cancellation | Unverified native runtime | Unverified native runtime | User-managed `ffprobe`/`ffmpeg` are capability-detected and are not bundled. Cross-target compilation and macOS package smoke do not prove codecs or executable availability. No transcription or visual-description implementation is bundled. |
| Ollama-compatible AI | Supported with limitations | Unverified runtime | Unverified runtime | Uses a configured HTTP endpoint and is not auto-launched. A custom endpoint may be remote. |
| Open/reveal with file manager | Supported | Preview / manual verification pending | Unavailable | Exact paths use the platform desktop association API; no constructed shell command is used. |
| Application data locations | Supported | Preview | Preview | Windows preserves LocalAppData; Linux uses XDG; macOS uses Application Support/Caches/Logs. Exact current paths are exposed in Platform Diagnostics. |
| Explorer Protocol v1 | Native Windows named-pipe round trip validated | Cross-target compilation only | Cross-target compilation only | On-demand current-user local transport with no TCP listener. Native Linux/macOS protocol execution is not claimed. |
| Installer/current package | Supported, unsigned | Unavailable | Preview, unsigned/unnotarized | OmniSorSe v2.4.0 provides Windows x64 installer/portable ZIP and native Intel/Apple Silicon DMGs while retaining installer/bundle/profile compatibility. Linux remains source build only. |

## Filesystem limitations

- Volume/file-index and device/inode values identify an object on a particular
  filesystem at a point in time. They may not survive copies, migration,
  snapshots, restore, containers, or identifier reuse.
- Network filesystems, removable media, FUSE, bind mounts, containers, and
  copy-on-write systems may provide weaker identity, locking, timestamp,
  watcher, free-space, transaction, or durability behavior.
- Linux permissions can include ACL, capability, namespace, and mount rules not
  described by Unix mode bits. OmniSorSe never elevates, runs `sudo`, changes
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
