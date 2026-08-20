# OmniSorSe

<p align="center">
  <img src="docs/images/opensorse-logo.png" width="144" alt="OmniSorSe logo">
</p>

<p align="center">
  <strong>Omni Sort and Search</strong><br>
  Find clarity in your files.
</p>

OmniSorSe (formerly OpenSorSe) is an open-source, local-first desktop application for scanning,
searching, understanding, and safely organizing explicitly selected folders.
It combines deterministic file analysis, exact duplicate review, local text and
OCR extraction, progressive Search, reviewable organization, and optional
Ollama-compatible assistance.

It is not an autonomous file manager. Analysis and suggestions do not authorize
file changes.

## Release and source status

### Latest published release: OmniSorSe v2.4.0

v2.4.0 is the Transition & Explorer Foundation release. The active product,
desktop executable, and package identity are now OmniSorSe. Existing OpenSorSe
application-data locations, schema 5, installer identity, and other documented
compatibility identities remain in place so upgrading does not create a new
empty profile or require a branding-only reindex.

This release also introduces Explorer Protocol v1: an on-demand,
authenticated, source-scoped, bounded, read-only local named-pipe interface for
a separate companion (called OmniExplorer in the v2.4 record and now delivered
as OmniBrille). The companion itself is not included, no graph renderer or GPU
dependency was added, and ordinary OmniSorSe operation starts no protocol
listener.

The release is automatically validated on Windows, Ubuntu, and macOS. Automated
validation is not a claim of broad interactive testing on every host or with
every Ollama/OCR configuration. Read [Release Status](docs/RELEASE_STATUS.md)
for the evidence boundary,
[v2.4.0 Release Notes](docs/RELEASE_NOTES_v2.4.0.md) for user-facing changes,
and [Release History](RELEASE_HISTORY.md) for earlier milestones.

### Current GitHub source: v2.12 candidate

The repository's current source is the unreleased **v2.12 Trusted Relationships
& Context** candidate, built on the linear v2.5–v2.11 candidate stack. It
targets .NET 10 and uses durable index schema 6 and Explorer Protocol 1.0.
GitHub `main` now carries the validated candidate source, so a normal clone of
the default branch contains it. This source publication is not a GitHub
Release: it is not tagged or packaged, and v2.4.0 remains the latest
downloadable release. Start with
[Current State](docs/CURRENT-STATE.md) for the implemented boundary and known
limitations, [Release Status](docs/RELEASE_STATUS.md) for validation/readiness
evidence, and the
[v2.12 implementation record](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) for
release-specific detail. Earlier candidate records remain available through the
[Documentation Index](docs/README.md) without being repeated here.

The [transition and protocol guide](docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md)
documents the compatibility and security boundaries. The
[manual checklist](docs/MANUAL_TESTING_v2.4.md) separates genuine Windows
upgrade/two-process evidence from checks that were not performed.

## Screenshots

Privacy-reviewed screenshots must show the real released application with
synthetic data. Native capture was not reliable in the release-engineering
environment, so no mock or AI-generated screenshot is substituted here. The
remaining capture work is explicit in the unchecked
[v2.0 screenshot checklist](docs/SCREENSHOT_CHECKLIST_v2.0.md).

## Downloads and installation

Use only the official
[v2.4.0 GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.4.0):

- Windows x64 installer: `OmniSorSe-v2.4.0-win-x64-setup.exe`;
- Windows x64 portable: `OmniSorSe-v2.4.0-win-x64.zip`;
- macOS Intel: `OmniSorSe-v2.4.0-macos-x64.dmg`;
- macOS Apple Silicon: `OmniSorSe-v2.4.0-macos-arm64.dmg`;
- SHA-256 checksums: `OmniSorSe-v2.4.0-SHA256SUMS.txt`.

The v2.4.0 Windows and macOS artifacts are unsigned, and the macOS packages are
not notarized, unless the release page explicitly records otherwise. Windows
SmartScreen or macOS Gatekeeper may therefore warn. Checksums detect changed
bytes but do not authenticate an unsigned publisher.

No Linux installer is published. Linux x64 users should follow
[Linux Build and Launch](docs/LINUX_BUILD_AND_LAUNCH.md). Detailed install,
update, uninstall, application-data, and checksum guidance is in
[Installation](docs/INSTALLATION.md).

## What GitHub main implements (v2.12 candidate)

This section describes the current source on `main`, not the downloadable
v2.4.0 package. Use the v2.4.0 Release Notes for the released feature boundary.

| Capability | Current behavior |
| --- | --- |
| Scanning and analysis | Recursively discovers selected files with progress, cancellation, metadata, classification, SHA-256 hashing, exact duplicate detection, and isolated errors. |
| Watched Folders | Treats operating-system events as hints, verifies actual state, performs bounded incremental analysis, and reconciles missed/offline changes. Watching never applies file changes. |
| Search | A primary navigation destination using deterministic local filename, folder, path, type, metadata, tag, retained text/OCR, summary, keyword, selected-text, optional related-concept evidence, and optional direct relationship context. v2.7 selects plausible IDs across the complete authorized index before bounded hydration, preserving exact/stem/prefix order beyond the old path-ordered projection ceiling. |
| Search explanations | Preserves exact/literal evidence above related-concept-only similarity and exposes actual ranking reasons plus bounded source-labelled snippets. |
| Relationships and Collections | Discovers bounded deterministic relationships through capped independent evidence families, provides virtual Smart Collections and timelines, preserves reversible pair/collection authority, and never moves original files. |
| Related Files | Directly shows one aggregated target per related pair with a confidence band and bounded reason, independently of the optional Knowledge Graph. Search and Files can open the selected stable identity here; Related, Not Related, and Use automatic result remain reversible. |
| Content and OCR | Extracts bounded metadata/native text for supported formats and can call an externally installed local Tesseract 5 engine for enabled image/scanned-page OCR. |
| Media Intelligence | Adds bounded structured image metadata and EXIF-oriented lazy thumbnails; optional image/video-frame OCR; optional `ffprobe` audio/video metadata and `ffmpeg` representative frames; and provider-neutral transcription/visual-description boundaries. v2.3 can use an explicitly configured user-managed whisper.cpp CLI/model for local speech transcription; no visual-description provider is included. Missing optional tools never disable ordinary Search. |
| Content Intelligence | Derives bounded normalized topics, textual entities, keywords, and a one-sentence extractive summary from already indexed local evidence, retaining provider/version and source provenance. These remain optional clues rather than facts. |
| Explainable Smart Tags (unreleased v2.6) | Classifies retained local evidence into bounded Theme and Document Type suggestions, preserves explicit User Tags and accept/reject authority, and adds canonical Search filters without requiring AI or writing file metadata. |
| Scalable faceted discovery (unreleased v2.7) | Combines free text with database-backed Theme, Document Type, User Tag, file-type, filesystem-created-year, and filesystem-modified-year facets; shows contextual counts and truthful candidate coverage; and saves dynamic current-index query rules without copying membership. |
| Guided workflows (unreleased v2.8) | Projects bounded durable library readiness on Home; connects Search results to Files by stable identity and back without losing canonical discovery state; provides continuous unresolved Smart Tag review; and lets accepted/Strong evidence inform editable, reviewed organization proposals. |
| Reviewed intelligent organization (unreleased v2.9) | Applies existing deterministic Organization recipes to an explicit stable-ID selection, previews trusted token provenance, missing evidence, fallbacks, privacy, collisions, and combined action bounds, then revalidates into the existing reviewed Change Plan. |
| Explorer Protocol v1 | Provides an on-demand current-user local named-pipe boundary for an optional separate explorer companion: authorized indexed roots, bounded Structure, grounded Search, Related/context, and safe details. It is read-only, session-scoped, dormant by default, and independent of SQLite schema. Unreleased v2.5 can launch separately installed OmniBrille through its established one-time current-user handoff pipe. |
| Optional AI | Uses an explicitly configured Ollama-compatible endpoint for separately enabled, bounded, validated suggestions and same-tier reranking of files already found by Search. Ordinary Search and OCR do not require AI. Remote endpoints are labelled as a privacy boundary. |
| Workflows and plugins | Provides typed Workflow Profiles, constrained persistent Organization recipes (internally compatible with Sorting Recipes), and a bounded local in-process plugin SDK with explicit capability grants. |
| Review and file operations | Converts supported proposals into persisted Change Plans. Rename, same-filesystem move, create-directory, and safe duplicate-recovery moves require review, validation, separate Apply confirmation, immediate preflight, journalling, and verification. |
| Recovery and Undo | Records action-level Operation Journal facts, attempts safe rollback, inspects interrupted operations, and blocks Undo when external changes make reversal unsafe. |
| Persistence | The current unreleased source uses bounded local JSON, schema-6 embedded Search with shared media/content evidence, normalized Smart Tag authority, and a bounded relationship-term projection, plus isolated schema-1 Knowledge Graph/decision sidecars. v2.7 keeps schema 6 and stores Saved View rules in bounded atomic application-owned JSON. The established OpenSorSe profile locations remain authoritative. No database server is required. |
| Production resilience (unreleased v2.10) | Enforces one writer per profile; fails closed on corrupt mutation/recovery stores; provides bounded health, abnormal-shutdown evidence, logical user-state backup/restore, and coordinated Forget. |
| Runtime/platform readiness (unreleased v2.11) | Targets .NET 10 LTS throughout and strengthens self-contained runtime/RID/source provenance plus native smoke evidence without changing schema or product behavior. |
| Trusted relationships (unreleased v2.12) | Keeps schema 6 and Protocol 1.0 while improving relationship evidence quality, explicit pair authority, direct access, bounded reanalysis, and format-2 logical backup of authored collection state. |

The current source does not implement cloud synchronization, collaboration,
OmniSorSe Server, a conversational assistant, unrestricted
media intelligence, autonomous organization, permanent deletion, generic
script execution, or a plugin security sandbox.

## Safety and privacy

OmniSorSe is non-destructive by default:

- scanning, watchers, duplicate review, extraction, OCR, Search/indexing,
  relationships, virtual collections, Knowledge Graph projection, comparison,
  diagrams, workflows,
  plugins, and AI do not modify source files;
- suggestions become non-mutating Change Plans;
- only reviewed and approved actions can reach the dedicated execution service;
- destinations are not silently overwritten;
- the Operation Journal is durable before mutation;
- rollback, recovery, and Undo verify current state and report unsafe
  ambiguity instead of guessing.

OmniSorSe is local-first:

- selected files are not uploaded by scanning, OCR, Search, indexing, saved
  scans, or duplicate review;
- no cloud account is required;
- the embedded Search provider runs locally and needs no server;
- AI is optional and disabled by default;
- ordinary logs exclude source content, complete queries, vectors,
  credentials, and raw model payloads;
- relationship and graph diagnostics retain bounded aggregate counts, states,
  watermarks, and timings rather than document content or unnecessary paths;
- detailed diagnostics are separately gated, bounded, redacted by default, and
  memory-only unless explicitly exported.

A custom Ollama-compatible endpoint can be remote. When configured that way,
an explicitly requested AI flow can send its bounded input to that endpoint.
The setting is therefore a real privacy boundary, not a claim that every
configuration remains on-device.

Read [Safety and Privacy](docs/SAFETY_AND_PRIVACY.md) for the complete current
contract.

## Platform and dependencies

- **Windows x64:** primary desktop target with a self-contained portable ZIP
  and per-user installer.
- **macOS Intel and Apple Silicon:** native `.app`/DMG packages. Read-only and
  non-mutating capabilities are packaged and smoke-tested; source-file mutation
  remains disabled where platform capability policy cannot prove equivalent
  safety. Packages are unsigned/unnotarized for v2.4.0.
- **Linux x64:** source-build preview with documented filesystem, watcher,
  desktop, and packaging limitations; no binary installer is advertised.
- **Ollama-compatible service:** optional and externally managed.
- **Tesseract 5:** optional and externally installed for OCR recognition.
- **whisper.cpp CLI and model:** optional, user-managed, and never downloaded or
  bundled by OmniSorSe.
- **Plugins:** optional local ZIP packages; external code runs in-process as the
  current user. Load-context isolation and SHA-256 integrity are not sandboxing
  or publisher authentication.

See the [Platform Compatibility Matrix](docs/PLATFORM_COMPATIBILITY_MATRIX.md),
[Installation](docs/INSTALLATION.md), and
[Linux Build and Launch](docs/LINUX_BUILD_AND_LAUNCH.md).

## Build and test

The SDK selected by [`global.json`](global.json) targets the .NET 10 LTS
application (`net10.0`). Release packages are self-contained.

```powershell
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet test .\OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet build .\OpenSorSe.sln --configuration Release --no-restore
dotnet test .\OpenSorSe.sln --configuration Release --no-build --no-restore
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Use disposable folders when testing Change Plan Apply, rollback, recovery, or
Undo. Build output is not a published release.

## Documentation

Start at the [Documentation Index](docs/README.md) for the full intent-based
router. First-time readers can choose a short path here:

| I want to… | Read first | Then continue with… |
| --- | --- | --- |
| Understand what is true now | [Current State](docs/CURRENT-STATE.md) | [Product Vision](PRODUCT_VISION.md) |
| Install or use the latest release | [Installation](docs/INSTALLATION.md) | [v2.4.0 Release Notes](docs/RELEASE_NOTES_v2.4.0.md) |
| Build or contribute | [Developer Guide](docs/DEVELOPER_GUIDE.md) | [Contributing](CONTRIBUTING.md) and [Engineering Principles](ENGINEERING_PRINCIPLES.md) |
| Understand the architecture | [Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md) | [System Map](docs/Architecture/OpenSorSe_System_Map.md) and [Architecture Library](docs/Architecture/README.md) |
| Review the current candidate | [v2.12 Trusted Relationships & Context](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) | Candidate lineage in the [Documentation Index](docs/README.md#current-unreleased-candidate-lineage) |
| Check validation or readiness | [Release Status](docs/RELEASE_STATUS.md) | [Platform Compatibility](docs/PLATFORM_COMPATIBILITY_MATRIX.md) and versioned manual gates |
| Research released or historical work | [Release History](RELEASE_HISTORY.md) | [Changelog](docs/CHANGELOG.md) and historical records in the [Documentation Index](docs/README.md#release-and-implementation-records) |

The index preserves versioned guides, checklists, specifications, and reports
without presenting them as equal current authorities.

## Contributing

Contributions should be focused, reviewable, tested at the owning layer, and
explicit about privacy, persistence, platform, and source-file safety effects.
Do not update a historical release snapshot to describe current behavior.

Read [CONTRIBUTING.md](CONTRIBUTING.md),
[Engineering Principles](ENGINEERING_PRINCIPLES.md), and the
[Developer Guide](docs/DEVELOPER_GUIDE.md) before making a cross-cutting
change.

## License

OmniSorSe is available under the [MIT License](LICENSE). Dependencies retain
their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the
[FOSS Dependency Policy](docs/FOSS_DEPENDENCY_POLICY.md), and the
[machine-readable dependency inventory](docs/dependency-licenses.json).
