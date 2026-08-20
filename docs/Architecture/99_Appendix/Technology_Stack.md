# OmniSorSe technology stack

**Document type:** Living technology inventory

**Scope:** Current v2.12 candidate source; a technology in a roadmap or historical
architecture document is not a current dependency

## Current stack

| Area | Technology | Current use |
| --- | --- | --- |
| Application target | .NET 10 LTS (`net10.0`) | All production, test, protocol-contract, and helper projects target .NET 10. |
| Repository SDK | .NET SDK selected by `global.json` (currently 10.0.400) | One reproducible SDK/runtime authority; release packages are self-contained and report the exact bundled runtime. |
| Language | C# with nullable reference types and implicit usings | Production and automated test implementation. |
| Desktop UI | Avalonia 12.1 | Cross-platform-capable presentation; Windows is primary and Linux remains a preview. |
| Presentation pattern | MVVM | Views/bindings are separated from ViewModel state/commands and Application/domain services. |
| MVVM support | CommunityToolkit.Mvvm 8.4.2 | Observable properties and commands. |
| Composition | Microsoft.Extensions.DependencyInjection 8.x | Desktop composition root and service registration. |
| Logging | Microsoft.Extensions.Logging 8.x plus OpenSorSe-owned bounded logging | Structured application logging without source content. |
| JSON persistence | `System.Text.Json` plus shared bounded atomic replacement | Settings, catalogs, workflows, watched state, plans, journals, compatible content/Search stores, and other application-owned data. |
| Durable Search/graph persistence | Microsoft.Data.Sqlite 8.0.28 and SQLitePCLRaw bundle 2.1.12 | Schema-6 deep index with shared media/content/Smart Tag evidence; isolated schema-1 graph/decision sidecars remain. |
| Media image parsing/thumbnails | Bounded managed parsers plus existing SkiaSharp 3.119.2 reference | Deterministic JPEG/PNG/WebP/BMP/TIFF headers/EXIF and lazy capped still-image PNG thumbnails; no network or source mutation. |
| Optional media metadata/frames | User-managed `ffprobe` and `ffmpeg` | Capability-detected argument-list processes with output/time/duration/frame bounds; not downloaded or bundled. |
| Media transcription | Provider-neutral Application contract plus optional user-managed whisper.cpp CLI adapter in v2.3.0 | No runtime/model is bundled or downloaded; missing configuration degrades to an unavailable capability. |
| Visual descriptions | Provider-neutral Application contract | No concrete provider, runtime, model, or dependency is selected or bundled. |
| Topics/entities/summaries | Managed deterministic Application provider | No external dependency or network; bounded extraction from already indexed evidence. |
| Native PDF text | PdfPig 0.1.15 | Bounded read-only PDF page text and metadata. |
| PDF page rendering | PDFtoImage 5.2.1 with PDFium native packages | Bounded rendering of PDF pages that need enabled OCR. |
| OCR | External Tesseract 5 CLI | Optional local image/scanned-page recognition; executable and language data are not bundled. |
| Optional AI transport | Ollama-compatible HTTP API | Explicitly configured, capability-gated, bounded review-only suggestions; the endpoint can be local or remote. |
| Compatible local similarity | Deterministic feature hashing | Rebuildable related-concept representation without a model download. |
| Durable indexing and Knowledge Graph | Provider-neutral Application contracts plus `OpenSorSe.Indexing.Sqlite` | Sources, jobs, stages, recovery, Search projections, relationship evidence/collections/corrections, optional stable graph projection/decisions, privacy rules, quotas, and repair. |
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
- `ffprobe` and `ffmpeg`, when used, are installed and managed externally.
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
- an automatic updater or automatic package distribution. Published v2.4
  Windows/macOS packages exist, but their release record identifies them as
  unsigned and the macOS artifacts as not notarized;
- OpenSorSe Server, collaboration, remote/unrestricted graph service, or a
  conversational assistant.

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
