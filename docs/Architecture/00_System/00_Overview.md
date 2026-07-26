# OpenSorSe 1.3 System Overview

OpenSorSe is a local-first Avalonia desktop application for understanding selected folders and reviewing organization decisions. It uses .NET 8, C#, MVVM, dependency injection, bounded asynchronous work, and versioned local JSON stores.

## Product boundary

Scanning, exact-duplicate review, metadata extraction, OCR Beta, tag generation, Semantic Search Beta, catalog/history comparison, diagrams, and optional AI suggestions are non-mutating. AI is disabled by default, capability-specific, untrusted, and suggestion-only. Rename/folder requests are metadata-only; bounded extracted text requires a separate opt-in and explicit one-document action.

OpenSorSe 1.2 added persistent watched roots and incremental catalogue reconciliation above the existing read-only scanners. v1.3 adds persistent typed workflow profiles, declarative sorting recipes, safe deterministic templates, and immutable effective-configuration snapshots. Watcher events and workflow settings are analysis inputs, not authorization or filesystem truth. Suggestions remain non-mutating and reuse the v1.1 persisted **Change Plan**, separate **Operation Journal**, review, preflight, explicit confirmation, verified execution, rollback, recovery, and conflict-aware Undo boundary.

> Workflow profiles automate configuration and analysis, not approval or file modification.

## Implemented components

| Project | Responsibility |
| --- | --- |
| `OpenSorSe.Core` | Validated settings, logging, lifecycle, events, state, tasks, and dependency-injection support. |
| `OpenSorSe.Scanner` | Read-only traversal, filesystem metadata, hashing, deterministic classification, and exact duplicate detection. |
| `OpenSorSe.Rules` | Deterministic rule evaluation/planning and conflict resolution; no Desktop execution workflow. |
| `OpenSorSe.Executor` | v1.1 Change Plan factory/validator/stores, durable journal, filesystem gateway, deterministic execution, rollback, Undo, restart recovery, and report export; historical generic components remain unregistered. |
| `OpenSorSe.Application` | Processing orchestration, Results projection, workflow profile/recipe domain/store/validation/templates/resolution/import/export/plan generation, persistent watched-folder management/coordinator/catalogues, debounced event hints, stability and incremental/reconciliation processing, AI gates/contracts, suggestion-to-plan adapters, catalog/search/comparison, content extraction, OCR service, provenance tags, semantic index/search, and restructuring/history/comparison. |
| `OpenSorSe.AI` | Optional Ollama-compatible HTTP transport and bounded AI review-decision persistence. |
| `OpenSorSe.Desktop` | Avalonia shell, global feature controls, Workflows/profile/recipe management and preview, manual profile selection, Watched Folders management/status/actions/activity, MVVM pages, Review Changes, Operation History/details/report/Undo, Help, diagnostics, and explicit confirmation. |

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

Settings, logs, AI decisions, saved catalog/searches, extracted content, semantic index, structure history, workflow library, watched configurations/catalogues/grouped activity, Change Plans, and the Operation Journal live in separate bounded OpenSorSe application-data files. Missing v1.1/v1.2/v1.3 stores are valid empty states. Corrupt stores fail closed and cannot activate a file operation.

## Deferred

Plugins, broad localization, installers/release automation, cloud indexing/synchronization, background-service monitoring while OpenSorSe is closed, automatic moved-root discovery, autonomous AI actions, permanent deletion, arbitrary recipe scripting, learned/external embedding models, and generic rule execution remain post-1.3 work.

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
