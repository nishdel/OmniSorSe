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

## OpenSorSe v2.1.0

v2.1.0 is the Search & AI Quality release. It improves deterministic filename
ranking, conservative typo handling, result explanations and workflows,
optional bounded Ollama reranking, model discovery/failure states, truthful
scan/index progress, duplicate review, notifications, privacy wording, Related
Files guidance, and contextual Help without replacing the v2.0 architecture.

The release is automatically validated on Windows, Ubuntu, and macOS. Automated
validation is not a claim of broad interactive testing on every host or with
every Ollama/OCR configuration. Read [Release Status](docs/RELEASE_STATUS.md)
for the evidence boundary,
[v2.1.0 Release Notes](docs/RELEASE_NOTES_v2.1.0.md) for user-facing changes,
and [Release History](RELEASE_HISTORY.md) for earlier milestones.

## Screenshots

Privacy-reviewed screenshots must show the real released application with
synthetic data. Native capture was not reliable in the release-engineering
environment, so no mock or AI-generated screenshot is substituted here. The
remaining capture work is explicit in the unchecked
[v2.0 screenshot checklist](docs/SCREENSHOT_CHECKLIST_v2.0.md).

## Downloads and installation

Use only the official
[v2.1.0 GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.1.0):

- Windows x64 installer: `OpenSorSe-v2.1.0-win-x64-setup.exe`;
- Windows x64 portable: `OpenSorSe-v2.1.0-win-x64.zip`;
- macOS Intel: `OpenSorSe-v2.1.0-macos-x64.dmg`;
- macOS Apple Silicon: `OpenSorSe-v2.1.0-macos-arm64.dmg`;
- SHA-256 checksums: `OpenSorSe-v2.1.0-SHA256SUMS.txt`.

The v2.1.0 Windows and macOS artifacts are unsigned, and the macOS packages are
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
| Search | Uses deterministic local filename, folder, path, type, metadata, tag, retained text/OCR, summary, keyword, selected-text, optional related-concept evidence, and optional direct relationship context. v2.1 strengthens exact/stem/prefix filename order, bounded typo handling, match explanations, and progressive coverage. |
| Search explanations | Preserves exact/literal evidence above related-concept-only similarity and exposes actual ranking reasons plus bounded source-labelled snippets. |
| Relationships and Collections | Discovers bounded deterministic relationships from retained evidence, provides virtual Smart Collections and timelines, preserves user corrections, and never moves original files. |
| Related Files | Optionally projects stable files, sources, folders, Collections, exact-content sets, and manual entities into an evidence-backed local Knowledge Graph with bounded browsing, privacy/repair controls, and opt-out Search context. It is disabled by default. |
| Content and OCR | Extracts bounded metadata/native text for supported formats and can call an externally installed local Tesseract 5 engine for enabled image/scanned-page OCR. |
| Optional AI | Uses an explicitly configured Ollama-compatible endpoint for separately enabled, bounded, validated suggestions and same-tier reranking of files already found by Search. Ordinary Search and OCR do not require AI. Remote endpoints are labelled as a privacy boundary. |
| Workflows and plugins | Provides typed Workflow Profiles, constrained Sorting Recipes, and a bounded local in-process plugin SDK with explicit capability grants. |
| Review and file operations | Converts supported proposals into persisted Change Plans. Rename, same-filesystem move, create-directory, and safe duplicate-recovery moves require review, validation, separate Apply confirmation, immediate preflight, journalling, and verification. |
| Recovery and Undo | Records action-level Operation Journal facts, attempts safe rollback, inspects interrupted operations, and blocks Undo when external changes make reversal unsafe. |
| Persistence | Uses bounded versioned local JSON stores, the schema-3 embedded Search index, and isolated schema-1 Knowledge Graph/decision sidecars behind provider-neutral Application contracts. No database server is required. |

The current source does not implement cloud synchronization, collaboration,
OpenSorSe Server, a conversational assistant, unrestricted
media intelligence, autonomous organization, permanent deletion, generic
script execution, or a plugin security sandbox.

## Safety and privacy

OpenSorSe is non-destructive by default:

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

OpenSorSe is local-first:

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
  safety. Packages are unsigned/unnotarized for v2.1.0.
- **Linux x64:** source-build preview with documented filesystem, watcher,
  desktop, and packaging limitations; no binary installer is advertised.
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
| [Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md) | Authoritative current component, data, safety, persistence, and dependency boundaries. |
| [System Map](docs/Architecture/OpenSorSe_System_Map.md) | Visual architecture and mutation-path diagrams. |
| [v2.1 Search and AI quality](docs/SEARCH_AND_AI_QUALITY_v2.1.md) | Current development behavior for ranking, optional Ollama reranking, truthful model/indexing states, fallbacks, privacy, and limits. |
| [Relationships and Collections](docs/RELATIONSHIPS_AND_COLLECTIONS_v1.9.md) | Evidence, Smart Collections, Search context, privacy, control, and current limits. |
| [v2.1.0 Release Notes](docs/RELEASE_NOTES_v2.1.0.md) | Downloads, Search/AI quality changes, checksums, trust status, limitations, and validation boundary. |
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

OpenSorSe is available under the [MIT License](LICENSE). Dependencies retain
their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the
[FOSS Dependency Policy](docs/FOSS_DEPENDENCY_POLICY.md), and the
[machine-readable dependency inventory](docs/dependency-licenses.json).
