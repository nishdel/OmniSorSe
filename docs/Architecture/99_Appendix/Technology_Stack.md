# OpenSorSe technology stack

**Document type:** Living technology inventory

**Scope:** Current v1.8 source; a technology in a roadmap or historical
architecture document is not a current dependency

## Current stack

| Area | Technology | Current use |
| --- | --- | --- |
| Application target | .NET 8 (`net8.0`) | All production and test projects target .NET 8. |
| Repository SDK | .NET SDK selected by `global.json` (currently 9.0.315) | Reproducible build/tool selection; the newer SDK does not change the .NET 8 runtime target. |
| Language | C# with nullable reference types and implicit usings | Production and automated test implementation. |
| Desktop UI | Avalonia 12.1 | Cross-platform-capable presentation; Windows is primary and Linux remains a preview. |
| Presentation pattern | MVVM | Views/bindings are separated from ViewModel state/commands and Application/domain services. |
| MVVM support | CommunityToolkit.Mvvm 8.4.2 | Observable properties and commands. |
| Composition | Microsoft.Extensions.DependencyInjection 8.x | Desktop composition root and service registration. |
| Logging | Microsoft.Extensions.Logging 8.x plus OpenSorSe-owned bounded logging | Structured application logging without source content. |
| JSON persistence | `System.Text.Json` plus shared bounded atomic replacement | Settings, catalogs, workflows, watched state, plans, journals, compatible content/Search stores, and other application-owned data. |
| Durable Search persistence | Microsoft.Data.Sqlite 8.0.28 and SQLitePCLRaw bundle 2.1.12 | Embedded schema-versioned provider behind Application contracts; no database server is required. |
| Native PDF text | PdfPig 0.1.15 | Bounded read-only PDF page text and metadata. |
| PDF page rendering | PDFtoImage 5.2.1 with PDFium native packages | Bounded rendering of PDF pages that need enabled OCR. |
| OCR | External Tesseract 5 CLI | Optional local image/scanned-page recognition; executable and language data are not bundled. |
| Optional AI transport | Ollama-compatible HTTP API | Explicitly configured, capability-gated, bounded review-only suggestions; the endpoint can be local or remote. |
| Compatible local similarity | Deterministic feature hashing | Rebuildable related-concept representation without a model download. |
| Durable indexing | Provider-neutral Application contracts plus `OpenSorSe.Indexing.Sqlite` | Sources, jobs, stages, recovery, Search projections, privacy rules, quotas, and repair. |
| Plugin model | `OpenSorSe.Extensions.Abstractions` plus in-process collectible load contexts | Eight bounded extension points, local packages, explicit grants, validation, and lifecycle containment. Load contexts are not sandboxing. |
| Testing | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet collector | Unit, integration, ViewModel, provider, repository-policy, relevance, and bounded performance regression tests. |
| Documentation | Markdown and Mermaid | Living guides, historical evidence, architecture, and diagrams. |
| CI | GitHub Actions | Native Windows/Ubuntu/macOS restore, build, test, format, and repository-policy validation on configured branches; no automatic publishing. |
| Version control | Git | Source, documentation, release history, and collaboration. |

Exact direct/transitive package purpose, license, bundling, and notices are in
the [machine-readable inventory](../../dependency-licenses.json),
[Third-Party Notices](../../../THIRD_PARTY_NOTICES.md), and
[FOSS Dependency Policy](../../FOSS_DEPENDENCY_POLICY.md).

## Why embedded SQLite

The v1.7+ durable Search index needs transactions, migration, recovery,
integrity checks, query indexes, and durable stage state without requiring a
separate service. SQLite provides that embedded implementation.

SQLite is isolated in `OpenSorSe.Indexing.Sqlite`. Views, ViewModels, Search
ranking, and Application orchestration use provider-neutral contracts and do
not use SQL or SQLite types. A future reviewed server provider can implement
those contracts; PostgreSQL is not a current desktop dependency.

See [Product Vision](../../../PRODUCT_VISION.md) and
[Deep Indexing Architecture](../00_System/10_v1.7_Deep_Indexing_Architecture.md).

## Optional components

- Tesseract and language data are installed and managed externally.
- Ollama-compatible providers are installed and managed externally.
- External plugins are user-selected local packages and run in-process with the
  current user’s permissions.
- Missing optional components must leave unrelated deterministic features
  usable and report an explicit unavailable/waiting state.

## Current non-adoptions

Current source does not use or claim:

- PostgreSQL or another required database server;
- cloud Search, synchronization, telemetry-based ranking, or a remote query
  service;
- learned ranking or a bundled embedding model/GPU requirement;
- bundled Tesseract executables or language/model data;
- a plugin marketplace, automatic plugin download/update, publisher signature
  authority, or OS sandbox;
- Python/PySide as an application runtime;
- a signed installer, automatic updater, or v1.8 distribution package;
- OpenSorSe Server, collaboration, a knowledge graph, or a conversational
  assistant.

Those topics belong to the
[Product Roadmap](../../../PRODUCT_ROADMAP.md) as planned concepts, research,
or backlog work. Naming a technology in future design material does not adopt
it.

## Selection principles

Technology choices should preserve:

- local-first usefulness and explicit remote boundaries;
- current safety and recovery invariants;
- clear ownership and replaceable provider seams;
- FOSS licensing and redistributable packaging;
- bounded resources, cancellation, and failure containment;
- supported-platform evidence rather than framework-level assumptions;
- backward-compatible data and plugin contracts or an explicit migration.

See [Engineering Principles](../../../ENGINEERING_PRINCIPLES.md) for the
complete dependency and architecture rationale.
