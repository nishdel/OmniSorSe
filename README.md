# OpenSorSe

<p align="center">
  <img src="docs/images/opensorse-logo.png" width="144" alt="OpenSorSe logo">
</p>

<p align="center">
  <strong>Open Sort and Search</strong><br>
  Find clarity in your files.
</p>

OpenSorSe is an open-source, local-first desktop application for scanning,
searching, understanding, and safely organizing explicitly selected folders.
It combines deterministic file analysis, exact duplicate review, local text and
OCR extraction, progressive Search, reviewable organization, and optional
Ollama-compatible assistance.

It is not an autonomous file manager. Analysis and suggestions do not authorize
file changes.

## Repository status

- `main` contains the integrated implementation through v1.6.
- v1.7 **Deep Indexing Foundation** is implemented on
  `v1.7-deep-indexing-foundation` and is not merged to `main`.
- v1.8 **Search Intelligence, Quality and Privacy** is implemented on
  `v1.8-search-intelligence-privacy` and is not merged to `main`.
- v1.9 **Relationships, Context & Smart Collections** is implemented on
  `v1.9-relationships-context`, directly above v1.8, and is not merged to
  `main`. Its interactive manual checklist remains unchecked.
- v2.0 **Knowledge Graph** has a stability-first design package on
  `v2.0-knowledge-graph-design`, based directly on the validated v1.9 tip. It
  contains no v2.0 runtime implementation or release claim.
- The only tagged and packaged repository release is the historical v1.0
  Windows x64 snapshot. There is no v1.9 package, installer, tag, or published
  release in this repository.

Read [Release Status](docs/RELEASE_STATUS.md) for the exact current boundary,
[Release History](RELEASE_HISTORY.md) for completed milestones, and the
[Product Roadmap](PRODUCT_ROADMAP.md) for future concepts.

## What the current source implements

| Capability | Current behavior |
| --- | --- |
| Scanning and analysis | Recursively discovers selected files with progress, cancellation, metadata, classification, SHA-256 hashing, exact duplicate detection, and isolated errors. |
| Watched Folders | Treats operating-system events as hints, verifies actual state, performs bounded incremental analysis, and reconciles missed/offline changes. Watching never applies file changes. |
| Search | Uses local filename, folder, path, type, metadata, tag, retained text/OCR, summary, keyword, selected-text, optional related-concept evidence, and optional direct relationship context with visible filters and progressive coverage. |
| Search explanations | Preserves exact/literal evidence above related-concept-only similarity and exposes actual ranking reasons plus bounded source-labelled snippets. |
| Relationships and Collections | Discovers bounded deterministic relationships from retained evidence, provides virtual Smart Collections and timelines, preserves user corrections, and never moves original files. |
| Content and OCR | Extracts bounded metadata/native text for supported formats and can call an externally installed local Tesseract 5 engine for enabled image/scanned-page OCR. |
| Local AI | Uses an explicitly configured Ollama-compatible endpoint for separately enabled, bounded, validated, review-only suggestions. Ordinary Search and OCR do not require AI. |
| Workflows and plugins | Provides typed Workflow Profiles, constrained Sorting Recipes, and a bounded local in-process plugin SDK with explicit capability grants. |
| Review and file operations | Converts supported proposals into persisted Change Plans. Rename, same-filesystem move, and create-directory actions require review, validation, separate Apply confirmation, immediate preflight, journalling, and verification. |
| Recovery and Undo | Records action-level Operation Journal facts, attempts safe rollback, inspects interrupted operations, and blocks Undo when external changes make reversal unsafe. |
| Persistence | Uses bounded versioned local JSON stores plus an embedded SQLite durable Search index behind provider-neutral Application contracts. No database server is required. |

The current source does not implement cloud synchronization, collaboration,
OpenSorSe Server, a knowledge graph, a conversational assistant, unrestricted
media intelligence, autonomous organization, permanent deletion, generic
script execution, or a plugin security sandbox.

## Safety and privacy

OpenSorSe is non-destructive by default:

- scanning, watchers, duplicate review, extraction, OCR, Search/indexing,
  relationships, virtual collections, comparison, diagrams, workflows,
  plugins, and AI do not modify source files;
- suggestions become non-mutating Change Plans;
- only reviewed and approved actions can reach the dedicated execution service;
- destinations are not silently overwritten;
- the Operation Journal is durable before mutation;
- rollback, recovery, and Undo verify current state and report unsafe
  ambiguity instead of guessing.

OpenSorSe is local-first:

- selected files are not uploaded by scanning, OCR, Search, indexing, saved
  scans, or duplicate review;
- no cloud account is required;
- the embedded Search provider runs locally and needs no server;
- AI is optional and disabled by default;
- ordinary logs exclude source content, complete queries, vectors,
  credentials, and raw model payloads;
- relationship diagnostics retain bounded aggregate counts and timings rather
  than document content or unnecessary paths;
- detailed diagnostics are separately gated, bounded, redacted by default, and
  memory-only unless explicitly exported.

A custom Ollama-compatible endpoint can be remote. When configured that way,
an explicitly requested AI flow can send its bounded input to that endpoint.
The setting is therefore a real privacy boundary, not a claim that every
configuration remains on-device.

Read [Safety and Privacy](docs/SAFETY_AND_PRIVACY.md) for the complete current
contract.

## Platform and dependencies

- **Windows:** primary verified desktop target.
- **Linux x64:** source-build preview with documented filesystem, watcher,
  desktop, and packaging limitations.
- **macOS:** native CI/build evidence exists for the inherited v1.6 baseline,
  but the product does not claim a supported macOS desktop or mutation path.
- **Ollama-compatible service:** optional and externally managed.
- **Tesseract 5:** optional and externally installed for OCR recognition.
- **Plugins:** optional local ZIP packages; external code runs in-process as the
  current user. Load-context isolation and SHA-256 integrity are not sandboxing
  or publisher authentication.

See the [Platform Compatibility Matrix](docs/PLATFORM_COMPATIBILITY_MATRIX.md),
[Installation](docs/INSTALLATION.md), and
[Linux Build and Launch](docs/LINUX_BUILD_AND_LAUNCH.md).

## Build and test

The SDK selected by [`global.json`](global.json) targets the .NET 8
application.

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
| [Product Vision](PRODUCT_VISION.md) | Why OpenSorSe exists, its current product philosophy, and its long-term direction. |
| [Product Roadmap](PRODUCT_ROADMAP.md) | Completed, in-progress, planned, research, and backlog work with branch/integration truth. |
| [Engineering Principles](ENGINEERING_PRINCIPLES.md) | Reasoning behind architecture, MVVM, persistence, testing, releases, security, privacy, recovery, and review. |
| [Release History](RELEASE_HISTORY.md) | Concise version, branch, date, test-total, and merged-status index. |
| [Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md) | Authoritative v1.9 component, data, safety, persistence, and dependency boundaries. |
| [System Map](docs/Architecture/OpenSorSe_System_Map.md) | Visual architecture and mutation-path diagrams. |
| [Relationships and Collections](docs/RELATIONSHIPS_AND_COLLECTIONS_v1.9.md) | Evidence, Smart Collections, Search context, privacy, control, and current limits. |
| [v2.0 Knowledge Graph stability design](docs/Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md) | Proposed isolated projection, conservative scope, failure states, recovery, bounds, and release blockers; not implemented behavior. |
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

OpenSorSe is available under the [MIT License](LICENSE). Dependencies retain
their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the
[FOSS Dependency Policy](docs/FOSS_DEPENDENCY_POLICY.md), and the
[machine-readable dependency inventory](docs/dependency-licenses.json).
