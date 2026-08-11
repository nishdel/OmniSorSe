# OpenSorSe current System Overview

OpenSorSe is a local-first Avalonia desktop application for understanding selected folders and reviewing organization decisions. It uses .NET 8, C#, MVVM, dependency injection, bounded asynchronous work, versioned local JSON stores, an embedded provider-isolated SQLite Search index, and optional isolated Knowledge Graph sidecars.

## Product boundary

Scanning, exact-duplicate review, metadata extraction, OCR Beta, tag generation, Search/background indexing, relationship analysis, virtual Smart Collections, catalog/history comparison, diagrams, and optional AI suggestions are non-mutating. AI is disabled by default, capability-specific, untrusted, and suggestion-only. Rename/folder requests are metadata-only; bounded extracted text requires a separate opt-in and explicit one-document action.

OpenSorSe 1.2 added watched roots, v1.3 typed workflows/recipes, v1.4 the
local plugin host/SDK, and v1.5 explicit platform services. v1.6 hardens atomic
application-data persistence, cancellation, lifecycle, bounded memory,
performance, diagnostics, accessibility, and Windows/Linux/macOS validation.
v1.7 adds provider-neutral durable staged indexing, progressive Search
coverage, bounded storage policy, and interruption recovery without requiring
a database server or adding a mutation path. v1.8 adds bounded deterministic
query interpretation, coherent hybrid ranking, evidence-backed explanations and
snippets, relevance measurement, and provider-neutral index privacy/repair
operations. v1.9 adds deterministic evidence-backed relationships, virtual
collections/context/timelines, persistent user corrections, contextual Search,
and index-only relationship privacy/repair.
v2.0 added an informed-consent, disabled-by-default conservative
Knowledge Graph projection with durable recovery, bounded inspection, and
optional Search context. It leaves the v1.9 schema-3 index authoritative and
never reads or modifies source files.
v2.1 improved Search and optional Ollama behavior without a schema
change. v2.2 adds provider-neutral bounded media
metadata, OCR/transcript/description evidence, lazy image thumbnails, optional
`ffprobe` metadata, optional capped `ffmpeg` frames, Search integration, and
conservative media relationships. It migrates only `deep-index.db` to schema 4.
Watcher events, workflow settings, plugin output, and platform capability
reports remain analysis inputs, not authorization or filesystem truth.

> Workflow profiles automate configuration and analysis, not approval or file modification.

## Implemented components

| Project | Responsibility |
| --- | --- |
| `OpenSorSe.Extensions.Abstractions` | Stable immutable plugin SDK contracts; no reference to Application, Desktop, DI, persistence, credentials, or execution. |
| `OpenSorSe.Core` | Validated settings, logging, lifecycle, events, state, tasks, platform contracts/implementations, application locations, capability reporting, and dependency-injection support. |
| `OpenSorSe.Scanner` | Read-only traversal, filesystem metadata, hashing, deterministic classification, and exact duplicate detection. |
| `OpenSorSe.Rules` | Deterministic rule evaluation/planning and conflict resolution; no Desktop execution workflow. |
| `OpenSorSe.Executor` | v1.1 Change Plan factory/validator/stores, durable journal, filesystem gateway, deterministic execution, rollback, Undo, restart recovery, and report export; historical generic components remain unregistered. |
| `OpenSorSe.Application` | Processing orchestration, Results projection, workflow profile/recipe domain/store/validation/templates/resolution/import/export/plan generation, plugin discovery/loading/lifecycle/packages/registry/invocation/diagnostics, persistent watched-folder management/coordinator/catalogues, debounced event hints, stability and incremental/reconciliation processing, AI gates/contracts, suggestion-to-plan adapters, catalog/search/comparison, deterministic query interpretation and hybrid ranking, relationship evidence/engine/contracts/collections/context, provider-neutral Knowledge Graph projection/query/decision/privacy/repair, content extraction, OCR service, provenance tags, semantic index/search, and restructuring/history/comparison. |
| `OpenSorSe.AI` | Optional Ollama-compatible HTTP transport and bounded AI review-decision persistence. |
| `OpenSorSe.Indexing.Sqlite` | Embedded schema-versioned implementation of provider-neutral durable indexing, Search projection, relationship evidence/collections/corrections, isolated graph/decision sidecars, recovery, coverage, quota, privacy, and targeted-repair contracts. |
| `OpenSorSe.Desktop` | Avalonia shell, Windows/Linux desktop adapters, platform diagnostics, global feature controls, Knowledge Graph/Collections/Search presentation, Workflows/profile/recipe management and preview, manual profile selection, Watched Folders management/status/actions/activity, MVVM pages, Review Changes, Operation History/details/report/Undo, Help, diagnostics, and explicit confirmation. |

```mermaid
flowchart LR
    UI["Avalonia Desktop / MVVM"] --> App["Application services"]
    Watch["OS watcher hints"] --> Queue["Bounded debounce queue"]
    Queue --> App
    App --> Scanner["Read-only Scanner"]
    App --> Rules["Rules planning"]
    App --> Content["Metadata + OCR cache"]
    Content --> Tags["Provenance tags"]
    Content --> Semantic["Local Semantic index"]
    App --> Catalog["Saved catalog"]
    App --> Watched["Watched configuration / catalogue / activity"]
    App --> Plan["Change Plan factory + validation"]
    Structure --> Plan
    Plan -->|review + explicit Apply| Executor["Journalled executor"]
    Executor --> Files["Rename / move / create directory"]
    Executor --> Journal["Operation Journal"]
    UI --> AI["Optional Ollama transport"]
    AI --> App
    Scanner --> Results["Immutable Results"]
    Results --> UI
```

## Local stores

Settings, logs, AI decisions, saved catalog/searches, extracted content,
semantic index, structure history, workflow library, plugin state/packages,
watched configurations/catalogues/grouped activity, Change Plans, and the
Operation Journal live in separate bounded OpenSorSe application-owned files.
v1.6 serializes process-local transactions per normalized path and replaces an
owned document only after a complete bounded sibling is durably flushed.
v2.2 uses schema-4 `deep-index.db` after transactionally migrating schema 3
for shared media evidence. Optional schema-1
`knowledge-graph.db` contains rebuildable projection data, while schema-1
`knowledge-decisions.db` contains graph-native decisions and privacy recovery
state that must not be silently reset.

## Deferred

An online plugin marketplace/download/update service, out-of-process plugin sandbox, publisher signature authority, broad localization, installers/updaters/distribution packages, full macOS support, cloud indexing/synchronization, background-service monitoring while OpenSorSe is closed, automatic moved-root discovery, autonomous AI actions, permanent deletion, arbitrary recipe scripting, learned/external embedding models, automatic speculative entity identity, graph canvas/unrestricted traversal, and generic rule execution remain future work.

## Related documents

- [Component Map](03_Component_Map.md)
- [Data Flow](04_Data_Flow.md)
- [User Flow](06_User_Flow.md)
- [Safety and Privacy](../../SAFETY_AND_PRIVACY.md)
- [v1.1 safety architecture](../07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md)
- [v1.1 specification](../../Implementation_Spec/v1.1/053_Safe_File_Operations_and_Robustness.md)
- [v1.2 watched-folder architecture](../02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md)
- [v1.2 specification](../../Implementation_Spec/v1.2/054_Watched_Folders_and_Incremental_Scanning.md)
- [v1.3 workflow architecture](../07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md)
- [v1.3 specification](../../Implementation_Spec/v1.3/055_Workflow_Profiles_and_Recipe_Library.md)
- [v1.4 plugin architecture](../10_Plugins/06_v1.4_Plugin_Foundation.md)
- [v1.4 specification](../../Implementation_Spec/v1.4/056_Plugin_Foundation_and_Extension_SDK.md)
- [v1.5 platform architecture](08_v1.5_Platform_Architecture.md)
- [v1.5 specification](../../Implementation_Spec/v1.5/057_Cross_Platform_Foundation_and_Linux_Preview.md)
- [v1.6 reliability architecture](09_v1.6_Reliability_Architecture.md)
- [v1.6 specification](../../Implementation_Spec/v1.6/058_Reliability_Performance_and_Production_Hardening.md)
- [v1.7 deep indexing architecture](10_v1.7_Deep_Indexing_Architecture.md)
- [v1.7 specification](../../Implementation_Spec/v1.7/059_Deep_Indexing_Foundation.md)
- [v1.8 Search architecture](../06_Search/09_v1.8_Search_Intelligence_Privacy.md)
- [v1.8 specification](../../Implementation_Spec/v1.8/060_Search_Intelligence_Quality_and_Privacy.md)
- [v1.9 relationship architecture](../06_Search/10_v1.9_Relationships_Context.md)
- [v1.9 specification](../../Implementation_Spec/v1.9/061_Relationships_Context_and_Smart_Collections.md)
- [v2.0 Knowledge Graph guide](../../KNOWLEDGE_GRAPH_v2.0.md)
- [v2.0 Knowledge Graph stability design](../06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md)
- [v2.2 Media Intelligence](../../MEDIA_INTELLIGENCE_v2.2.md)
