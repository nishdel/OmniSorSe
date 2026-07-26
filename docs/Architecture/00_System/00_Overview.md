# OpenSorSe 1.1 System Overview

OpenSorSe is a local-first Avalonia desktop application for understanding selected folders and reviewing organization decisions. It uses .NET 8, C#, MVVM, dependency injection, bounded asynchronous work, and versioned local JSON stores.

## Product boundary

Scanning, exact-duplicate review, metadata extraction, OCR Beta, tag generation, Semantic Search Beta, catalog/history comparison, diagrams, and optional AI suggestions are non-mutating. AI is disabled by default, capability-specific, untrusted, and suggestion-only. Rename/folder requests are metadata-only; bounded extracted text requires a separate opt-in and explicit one-document action.

OpenSorSe 1.1 generalizes the narrow mutation boundary into a persisted **Change Plan** and separate **Operation Journal**. Suggestions remain non-mutating. Only approved actions that pass review and immediate pre-execution validation can reach the dedicated executor after explicit confirmation. Rename, move, and required-directory actions are non-overwriting, verified, journalled, rollback-aware, and conflict-aware for Undo.

## Implemented components

| Project | Responsibility |
| --- | --- |
| `OpenSorSe.Core` | Validated settings, logging, lifecycle, events, state, tasks, and dependency-injection support. |
| `OpenSorSe.Scanner` | Read-only traversal, filesystem metadata, hashing, deterministic classification, and exact duplicate detection. |
| `OpenSorSe.Rules` | Deterministic rule evaluation/planning and conflict resolution; no Desktop execution workflow. |
| `OpenSorSe.Executor` | v1.1 Change Plan factory/validator/stores, durable journal, filesystem gateway, deterministic execution, rollback, Undo, restart recovery, and report export; historical generic components remain unregistered. |
| `OpenSorSe.Application` | Processing orchestration, Results projection, AI gates/contracts, suggestion-to-plan adapters, catalog/search/comparison, content extraction, OCR service, provenance tags, semantic index/search, and restructuring/history/comparison. |
| `OpenSorSe.AI` | Optional Ollama-compatible HTTP transport and bounded AI review-decision persistence. |
| `OpenSorSe.Desktop` | Avalonia shell, global feature controls, MVVM pages, Review Changes, Operation History/details/report/Undo, Help, diagnostics, and explicit confirmation. |

```mermaid
flowchart LR
    UI["Avalonia Desktop / MVVM"] --> App["Application services"]
    App --> Scanner["Read-only Scanner"]
    App --> Rules["Rules planning"]
    App --> Content["Metadata + OCR cache"]
    Content --> Tags["Provenance tags"]
    Content --> Semantic["Local Semantic index"]
    App --> Catalog["Saved catalog"]
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

Settings, logs, AI decisions, saved catalog/searches, extracted content, semantic index, structure history, Change Plans, and the Operation Journal live in separate bounded OpenSorSe application-data files. Missing v1.1 stores are valid empty states. Corrupt optional stores fail closed and cannot activate a file operation.

## Deferred

Plugins, broad localization, installers/release automation, cloud indexing, live monitoring, autonomous AI actions, permanent deletion, learned/external embedding models, and generic rule execution remain post-1.1 work.

## Related documents

- [Component Map](03_Component_Map.md)
- [Data Flow](04_Data_Flow.md)
- [User Flow](06_User_Flow.md)
- [Safety and Privacy](../../SAFETY_AND_PRIVACY.md)
- [v1.1 safety architecture](../07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md)
- [v1.1 specification](../../Implementation_Spec/v1.1/053_Safe_File_Operations_and_Robustness.md)
