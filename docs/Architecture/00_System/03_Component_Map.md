# OpenSorSe 1.1 Component Map

```mermaid
flowchart TB
    Desktop["Desktop\nAvalonia + MVVM"]
    Application["Application\nworkflows + contracts + stores"]
    Core["Core\nsettings + logging + lifecycle"]
    Scanner["Scanner\nread-only analysis"]
    Rules["Rules\nplanning only"]
    AI["AI\noptional Ollama transport"]
    Content["Content\nmetadata + OCR"]
    Semantic["Semantic\nlocal hybrid index"]
    Structure["Structure\nsnapshot + plan + history + compare"]
    ChangePlan["Change Plan\nreview intent + validation"]
    Executor["Executor\njournal + apply + rollback + Undo"]

    Desktop --> Application
    Desktop --> Core
    Application --> Scanner
    Application --> Rules
    Application --> Content
    Application --> Semantic
    Application --> Structure
    Application --> ChangePlan
    ChangePlan --> Executor
    Desktop --> ChangePlan
    AI --> Application
    AI --> Core
    Structure --> Executor
```

| Boundary | Enforcement |
| --- | --- |
| Feature visibility | `FeatureRequirement` and `FeatureAccess` combine AI, Advanced, and Semantic settings for navigation and commands. |
| AI | `IAiSuggestionService` checks global/capability/provider/context state before `IAiSuggestionProvider`; structured parsers/validators reject unsafe output. |
| Content | Extractors and OCR use read-only, bounded, cancellable requests; `IContentStore` is independent of source files. |
| Semantic | `ISemanticIndexer` and `ISemanticSearchService` use a local deterministic provider and versioned index store; disabled calls do no store/provider work. |
| Structure | `IFolderRestructuringService` separates preview from exact confirmation and uses `IFolderStructureSnapshotService` plus `IStructureHistoryStore`. |
| Change Plan | `IChangePlanFactory` captures source identity and `IChangePlanValidator` enforces complete read-only validation at creation, review, and pre-execution. |
| Execution | `IChangePlanExecutionService` freezes approved actions, journals before mutation, verifies, rolls back, undoes, and recovers through `IFileSystemGateway`. |
| Persistence | `IChangePlanStore` and `IOperationJournalStore` are independent bounded versioned atomic stores. |
| UI | ViewModels own asynchronous state/commands; views contain layout and bindings, not filesystem business logic. |
| Legacy generic execution | `IActionExecutor`/`IUndoEngine` remain unregistered compatibility components; production organization uses `IChangePlanExecutionService`. |

The production service provider validates every registration in automated tests.
