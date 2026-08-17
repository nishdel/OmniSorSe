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

## Latest published release: OmniSorSe v2.4.0

v2.4.0 is the Transition & Explorer Foundation release. The active product,
desktop executable, and package identity are now OmniSorSe. Existing OpenSorSe
application-data locations, schema 5, installer identity, and other documented
compatibility identities remain in place so upgrading does not create a new
empty profile or require a branding-only reindex.

This release also introduces Explorer Protocol v1: an on-demand,
authenticated, source-scoped, bounded, read-only local named-pipe interface for
a future separate OmniExplorer. OmniExplorer itself is not included, no graph
renderer or GPU dependency was added, and ordinary OmniSorSe operation starts
no protocol listener.

The release is automatically validated on Windows, Ubuntu, and macOS. Automated
validation is not a claim of broad interactive testing on every host or with
every Ollama/OCR configuration. Read [Release Status](docs/RELEASE_STATUS.md)
for the evidence boundary,
[v2.4.0 Release Notes](docs/RELEASE_NOTES_v2.4.0.md) for user-facing changes,
and [Release History](RELEASE_HISTORY.md) for earlier milestones.

Current development is the unreleased **v2.5 Workflow Completion & Indexing
Quality** branch. It hardens reviewed Change Plan/Undo reconciliation and adds
base-first progressive indexing. It also adds a lazy, scoped **Open in
OmniBrille** handoff for the separately installed companion without changing
Explorer Protocol v1. See the
[v2.5 design](docs/WORKFLOW_AND_INDEXING_QUALITY_v2.5.md); v2.4.0 remains the
latest published release.

The next implementation branch, **v2.6 Explainable Smart Tags**, builds on that
committed v2.5 candidate. It consolidates local Theme, Document Type, and User
Tag authority in schema 6, reuses retained Content/Media Intelligence evidence,
and preserves base-first Search. It is not released. See the
[v2.6 design](docs/EXPLAINABLE_SMART_TAGS_v2.6.md).

The v2.7 release-candidate branch, **v2.7 Scalable Faceted Discovery**, builds
on the committed v2.6 candidate. It restores complete-library candidate
eligibility beyond the former 10,000-document projection, adds database-backed
typed facets/counts and dynamic Saved Views, and keeps the established ranker,
schema 6, progressive indexing, and Explorer Protocol v1. It is not released.
See the [v2.7 design](docs/SCALABLE_FACETED_DISCOVERY_v2.7.md).

The v2.8 candidate, **Guided Workflows & Product
Coherence**, builds on the committed v2.7 candidate. It connects durable Home
readiness, Search, canonical facets/Saved Views, Files details, continuous Smart
Tag review, and reviewed organization through stable file identity. It does not
change schema 6, Search ranking, Explorer Protocol v1, or OmniBrille. It is not
released. See the
[v2.8 design](docs/GUIDED_WORKFLOWS_PRODUCT_COHERENCE_v2.8.md).

The v2.9 candidate, **Reviewed Intelligent Organization**,
builds directly on the committed v2.8 candidate. It connects explicit bounded
Files/Search/Saved View selections to the existing persistent recipe library,
an ephemeral trusted-evidence preview, and the existing Change Plan,
reconciliation, and Undo machinery. It adds no autonomous mutation, schema
change, protocol change, or production dependency and is not released. See the
[v2.9 design](docs/REVIEWED_INTELLIGENT_ORGANIZATION_v2.9.md).

The v2.10 candidate, **Production Hardening & Operational
Resilience**, builds directly on the committed v2.9 candidate. It adds
single-writer profile ownership, fail-closed recovery stores, bounded hostile
PDF handling, logical user-state export/restore, complete Forget coordination,
bounded health checks, and traceable release provenance. It preserves schema 6,
Explorer Protocol v1, OmniBrille separation, and all reviewed feature
boundaries. It is not released. See the
[v2.10 hardening record](docs/PRODUCTION_HARDENING_v2.10.md).

The v2.11 candidate, **Supported Runtime & Platform Readiness**, builds directly
on the committed v2.10 candidate. It moves every solution project and
self-contained package path to .NET 10 LTS, strengthens runtime/RID/source
provenance and native package smoke evidence, and leaves schema 6, product
behavior, Explorer Protocol v1, and OmniBrille unchanged. It is not released.
See the [v2.11 runtime record](docs/SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md).

The v2.12 candidate, **Trusted Relationships & Context**, builds directly on
the committed v2.11 candidate. It strengthens the existing schema-6
relationship authority with capped evidence families, reversible pair
corrections, graph-independent Related Files, bounded large-library candidate
selection, and `.oms-state` format 2 for authored relationship/Smart Collection
state. Schema 6 and Explorer Protocol 1.0 remain unchanged. It is not released.
See the [v2.12 implementation record](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md).

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

## What the current source implements

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

Start at the [Documentation Index](docs/README.md). The principal living
documents are:

| Document | Purpose |
| --- | --- |
| [Product Vision](PRODUCT_VISION.md) | Why OmniSorSe exists, its current product philosophy, and its long-term direction. |
| [Product Roadmap](PRODUCT_ROADMAP.md) | Completed, in-progress, planned, research, and backlog work with branch/integration truth. |
| [Engineering Principles](ENGINEERING_PRINCIPLES.md) | Reasoning behind architecture, MVVM, persistence, testing, releases, security, privacy, recovery, and review. |
| [Release History](RELEASE_HISTORY.md) | Concise version, branch, date, test-total, and merged-status index. |
| [Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md) | Authoritative current component, data, safety, persistence, and dependency boundaries. |
| [System Map](docs/Architecture/OpenSorSe_System_Map.md) | Visual architecture and mutation-path diagrams. |
| [v2.1 Search and AI quality](docs/SEARCH_AND_AI_QUALITY_v2.1.md) | Current released behavior for ranking, optional Ollama reranking, truthful model/indexing states, fallbacks, privacy, and limits. |
| [v2.2 Media Intelligence](docs/MEDIA_INTELLIGENCE_v2.2.md) | Released media-provider architecture, supported evidence, optional dependencies, bounds, privacy, Search integration, and honest limitations. |
| [v2.2 Manual Testing](docs/MANUAL_TESTING_v2.2.md) | Controlled Windows native-provider, OCR, and migration evidence plus explicitly unchecked interactive, transcription, and native Linux/macOS scenarios. |
| [v2.3 Content Intelligence](docs/CONTENT_INTELLIGENCE_v2.3.md) | Released provider architecture, deterministic topics/entities/summaries, optional user-managed whisper.cpp adapter, schema 5, Search/Related Files integration, privacy, and limits. |
| [v2.3 Manual Testing](docs/MANUAL_TESTING_v2.3.md) | Explicitly separated automated, provider-native, interactive, and native-platform release evidence. |
| [v2.4 Transition and Explorer Protocol](docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md) | Released branding/profile compatibility contract and bounded, authenticated, read-only Explorer Protocol v1 design. |
| [v2.4 Manual Testing](docs/MANUAL_TESTING_v2.4.md) | Genuine Windows profile/installer and external two-process protocol evidence with explicit unchecked boundaries. |
| [v2.11 Runtime & Platform Readiness](docs/SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md) | Unreleased .NET 10 migration, package provenance, platform evidence levels, and preserved architecture boundaries. |
| [v2.11 Manual Addendum](docs/MANUAL_TESTING_v2.11.md) | Runtime/package/platform gates added to the v2.10 master matrix. |
| [v2.12 Trusted Relationships & Context](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) | Unreleased relationship authority, scoring, scale, UX, backup, privacy, and protocol boundaries. |
| [v2.12 Manual Addendum](docs/MANUAL_TESTING_v2.12.md) | Relationship-specific quality, lifecycle, accessibility, and companion gates. |
| [Relationships and Collections](docs/RELATIONSHIPS_AND_COLLECTIONS_v1.9.md) | Evidence, Smart Collections, Search context, privacy, control, and current limits. |
| [v2.4.0 Release Notes](docs/RELEASE_NOTES_v2.4.0.md) | OmniSorSe transition, profile compatibility, Explorer Protocol v1, package trust, limitations, and final validation boundary. |
| [v2.5 Workflow & Indexing Quality](docs/WORKFLOW_AND_INDEXING_QUALITY_v2.5.md) | Unreleased implementation contract for post-operation reconciliation and progressive base-first indexing. |
| [OmniBrille companion handoff](docs/OMNIBRILLE_COMPANION_HANDOFF_v2.5.md) | Unreleased v2.5 local bootstrap, lifecycle, scope, failure, and threat-model contract; Explorer Protocol v1 remains unchanged. |
| [v2.6 Explainable Smart Tags](docs/EXPLAINABLE_SMART_TAGS_v2.6.md) | Unreleased schema-6 taxonomy, authority, classifier, Search/filter, progressive-indexing, privacy, and UI contract. |
| [v2.7 Scalable Faceted Discovery](docs/SCALABLE_FACETED_DISCOVERY_v2.7.md) | Unreleased complete-library candidate selection, database-backed facets/counts, dynamic Saved Views, bounded extraction, privacy, and UI contract. |
| [v2.8 Guided Workflows](docs/GUIDED_WORKFLOWS_PRODUCT_COHERENCE_v2.8.md) | Unreleased durable Home, Search-to-Files context, continuous Smart Tag review, capability readiness, organization evidence, privacy, and architecture contract. |
| [v2.3.0 Release Notes](docs/RELEASE_NOTES_v2.3.0.md) | Historical Content Intelligence and local-transcription release snapshot. |
| [v2.2.0 Release Notes](docs/RELEASE_NOTES_v2.2.0.md) | Historical Media Intelligence and UX release snapshot. |
| [v2.1.0 Release Notes](docs/RELEASE_NOTES_v2.1.0.md) | Historical Search/AI quality release snapshot. |
| [v2.0.0 Release Notes](docs/RELEASE_NOTES_v2.0.0.md) | Historical v2.0.0 release and integration record. |
| [v2.0 Knowledge Graph guide](docs/KNOWLEDGE_GRAPH_v2.0.md) | Implemented scope, sidecar storage, projection/recovery, Search, privacy, bounds, and deferred behavior. |
| [v2.0 Security Notes](docs/SECURITY_v2.0.md) | Trust boundaries, hostile-input/resource defenses, recovery, and explicit non-claims. |
| [Native Release Packaging](docs/RELEASE_PACKAGING_v2.0.md) | Windows/macOS artifact construction, validation, checksums, signing status, and publication order. |
| [v2.0 Knowledge Graph stability design](docs/Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md) | Stability-first design rationale and invariant decisions retained for contributors. |
| [v1.9 User Guide](docs/USER_GUIDE_v1.9.md) | Current relationship, collection, privacy, and Search-context workflows. |
| [Developer Guide](docs/DEVELOPER_GUIDE.md) | Guided build, validation, code tracing, and first-change workflow. |
| [Contributing](CONTRIBUTING.md) | Repository layout, MVVM, testing, documentation, branch, validation, and review expectations. |
| [Release Status](docs/RELEASE_STATUS.md) | Current integration, automated/manual validation, packaging, tag, and publication state. |

Versioned User Guides, Troubleshooting guides, Manual Testing checklists,
Version Notes, implementation specifications, and validation/implementation
reports are preserved as historical or version-specific records. The
[Documentation Inventory](docs/DOCUMENTATION_INVENTORY.md) explains authority
and retention.

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
