# Repository structure

This guide maps the released v2.4 solution as its projects exist in source. It describes
ownership and dependency rules; it is not a proposal for a different layering
model.

## Dependency direction

```mermaid
flowchart TB
    Desktop["OpenSorSe.Desktop"]
    Ai["OpenSorSe.AI"]
    Application["OpenSorSe.Application"]
    Indexing["OpenSorSe.Indexing.Sqlite"]
    Executor["OpenSorSe.Executor"]
    Rules["OpenSorSe.Rules"]
    Scanner["OpenSorSe.Scanner"]
    Core["OpenSorSe.Core"]
    Sdk["OpenSorSe.Extensions.Abstractions"]
    Protocol["OmniSorSe.ExplorerProtocol"]

    Desktop --> Ai
    Desktop --> Application
    Desktop --> Indexing
    Desktop --> Executor
    Desktop --> Rules
    Desktop --> Scanner
    Desktop --> Core
    Desktop --> Sdk
    Ai --> Application
    Ai --> Core
    Application --> Executor
    Application --> Rules
    Application --> Scanner
    Application --> Core
    Application --> Sdk
    Application --> Protocol
    Indexing --> Application
    Indexing --> Core
    Executor --> Rules
    Executor --> Core
    Rules --> Scanner
    Rules --> Core
    Scanner --> Core
```

The arrows are actual `ProjectReference` entries. There are no production
reference cycles.

## Production projects

### `OmniSorSe.ExplorerProtocol`

- **Purpose:** Stable versioned read-only DTO/capability/error contract for the
  future separate OmniExplorer client.
- **Owns:** Protocol 1.0 version, operations, capabilities, nodes, edges,
  requests/results, limits, and stable error envelopes.
- **Must not own:** SQLite, provider implementations, Search/indexing logic,
  filesystem access/mutation, dependency injection, UI, rendering, or transport
  hosting.
- **Dependencies:** None.
- **Reference rule:** Application implements it. No production project may add
  a reverse dependency from the contract into an internal OpenSorSe project.

### `OpenSorSe.Extensions.Abstractions`

- **Purpose:** Stable plugin-author contract for lifecycle and eight bounded
  extension points.
- **Owns:** Immutable request/response models, capability vocabulary,
  contribution identities, `IOpenSorSePlugin`, and collection helpers.
- **Must not own:** Host discovery, persistence, dependency injection, user
  settings, credentials, Change Plans, execution, UI, or transport clients.
- **Principal entry points:** `IOpenSorSePlugin`, `IExtensionContribution`,
  `IMetadataProvider`, `IContentExtractor`, `IFileClassifier`,
  `IRecipeFieldProvider`, `IDuplicateSignalProvider`,
  `IWorkflowCapabilityProvider`, `IImportFormatProvider`, and
  `IExportFormatProvider`.
- **Dependencies:** No OpenSorSe project references.
- **Reference rule:** Application and Desktop may reference the SDK. Internal
  projects must not be introduced as SDK dependencies.

### `OpenSorSe.Core`

- **Purpose:** Reusable infrastructure with no product-feature dependency.
- **Owns:** Validated configuration, logging, diagnostics, lifecycle hosting,
  event delivery, task/state abstractions, errors, platform contracts/adapters,
  application locations, filesystem identity/capabilities, external-tool
  discovery, and Core DI registration.
- **Must not own:** Scanning algorithms, workflows, plugins, Change Plans,
  filesystem mutation, AI transport, or presentation.
- **Principal entry points:** `AddOpenSorSeCore`, `IApplicationHost`,
  `IConfigurationService`, `ILoggingService`, `IDiagnosticsCollector`,
  `IEventBus`, `ITaskManager`, `IPathSemantics`,
  `IApplicationPathProvider`, and `IPlatformCapabilityProvider`.
- **Dependencies:** No other production project.
- **Reference rule:** All internal projects except the standalone SDK may use
  Core; Core must not reference them.

### `OpenSorSe.Scanner`

- **Purpose:** Read-only filesystem discovery and deterministic file analysis.
- **Owns:** Recursive enumeration, basic metadata, hashing, classification,
  exact duplicate grouping, progress, cancellation, and per-item issues.
- **Must not own:** Content/OCR caches, AI, workflows, Change Plans, persistence,
  UI, or filesystem mutation.
- **Principal entry points:** `IFileScanner`, `IFileMetadataReader`,
  `IFileHasher`, `IFileClassifier`, and `IDuplicateDetector`.
- **Representative implementations:** `FileScanner`, `FileMetadataReader`,
  `FileHasher`, `FileClassifier`, and `DuplicateDetector`.
- **Dependencies:** Core.
- **Reference rule:** Rules, Application, and Desktop may use Scanner. Scanner
  must not reference those higher layers.

### `OpenSorSe.Rules`

- **Purpose:** Pure, deterministic evaluation and proposal planning.
- **Owns:** Rule conditions/actions, `RuleEngine`, `ActionPlanner`, lexical
  conflict resolution, and planned-operation models.
- **Must not own:** Approval, journal persistence, user-file mutation, UI, AI,
  or application-data stores.
- **Principal entry points:** `IRuleEngine`, `IActionPlanner`, and
  `IConflictResolver`.
- **Dependencies:** Core and Scanner.
- **Reference rule:** Executor, Application, and Desktop may use Rules. Rules
  must not reference execution or presentation.

### `OpenSorSe.Executor`

- **Purpose:** Safety domain and the only supported production user-file
  mutation implementation.
- **Owns:** Change Plan schema/factory/validation/store, Operation Journal,
  `IFileSystemGateway`, preflight, execution ordering, verification,
  reverse-order rollback, restart recovery, conflict-aware Undo, and reports.
- **Must not own:** UI decisions, AI, watcher coordination, workflow policy, or
  plugin loading.
- **Principal entry points:** `IChangePlanFactory`, `IChangePlanValidator`,
  `IChangePlanExecutionService`, `IChangePlanStore`,
  `IOperationJournalStore`, and `IFileSystemGateway`.
- **Representative implementations:** `ChangePlanFactory`,
  `ChangePlanValidator`, `ChangePlanExecutionService`,
  `JsonChangePlanStore`, `JsonOperationJournalStore`, and
  `PhysicalFileSystemGateway`.
- **Dependencies:** Core and Rules.
- **Reference rule:** Application and Desktop may use Executor. Executor must
  not reference Application, AI, plugins, watchers, or Desktop.
- **Compatibility note:** `ActionExecutor` and `UndoEngine` are older,
  unregistered compatibility components. New product flows must use the Change
  Plan boundary.

### `OpenSorSe.Application`

- **Purpose:** Product use cases and orchestration between domain projects.
- **Owns:** Manual processing sessions, Results projection, local content/OCR,
  catalog/search/comparison, semantic indexing, tags, structure planning and
  history, provider-neutral AI policy, watched-folder coordination, workflows,
  plugin host/lifecycle/packages, relationship/context contracts and engine,
  provider-neutral Knowledge Graph projection/query/decision/privacy/repair
  contracts, provider-neutral bounded media extraction/evidence/thumbnails,
  and suggestion-to-Change-Plan adapters.
- **Must not own:** Avalonia views, shell navigation, Ollama HTTP details, or
  direct approved user-file mutation.
- **Principal entry points:** `IApplicationController`,
  `IProcessingSessionManager`, `IProcessingOrchestrator`,
  `IWorkflowConfigurationResolver`, `IWatchedFolderCoordinator`,
  `IPluginManager`, `IAiSuggestionService`, and
  `ISuggestionChangePlanFactory`.
- **Important folders:**
  - `AI`: gates, prompts, parsing, validation, and diagnostics contracts.
  - `Catalog`, `CatalogSearch`, `CatalogComparison`: saved scan data and query
    services.
  - `ChangePlans`: adapters from reviewed suggestions to executor models.
  - `Content`: bounded metadata/text extraction, PDF rendering, OCR, and cache.
  - `Plugins`: manifest, discovery, integrity, dependencies, packages,
    lifecycle, registry, invocation, provenance, and built-ins.
  - `Relationships`: evidence, confidence, deterministic discovery, Smart
    Collection/context projections, service/store contracts, Search expansion,
    privacy, repair, and diagnostics.
  - `KnowledgeGraph`: conservative identity/projection, completed-manifest
    lifecycle, four-axis state, bounded query/Search, graph-native decisions,
    privacy, repair, suggestions, and diagnostics contracts/services.
  - `Media`: format classification, deterministic image metadata/EXIF,
    optional external-process metadata/frame providers, coordinator, bounded
    evidence projection, transcription/visual contracts, and lazy thumbnails.
  - `ContentIntelligence`: bounded provider-neutral concept/summary contracts,
    deterministic local extraction with provenance, and the optional
    user-managed whisper.cpp process adapter.
  - `Semantic`: deterministic local index and explained search.
  - `Structure`: snapshots, preview plans, history, and comparisons.
  - `Tags`: provenance-aware generated tag candidates.
  - `Watching`: opt-in configurations, hint normalization, debounce,
    stability, reconciliation, incremental catalogues, and activity.
  - `Workflows`: profiles, recipes, validation, resolution, import/export,
    templates, snapshots, and recipe plans.
- **Dependencies:** SDK, Core, Scanner, Rules, and Executor.
- **Reference rule:** AI transport and Desktop may use Application.
  Application must not reference either.

### `OpenSorSe.AI`

- **Purpose:** Optional concrete AI-provider integration.
- **Owns:** Ollama-compatible HTTP transport and its provider-specific wire
  models. It also contains the local bounded decision-history store.
- **Must not own:** feature gating, prompts, application validation, navigation,
  or file operations.
- **Principal entry point:** `OllamaSuggestionProvider`.
- **Dependencies:** Application and Core.
- **Reference rule:** Desktop composes this transport behind
  `IAiSuggestionProvider`; lower projects do not reference it.

### `OpenSorSe.Indexing.Sqlite`

- **Purpose:** Initial embedded implementation of the provider-neutral durable
  background-index store.
- **Owns:** SQLite schema/versioning, migrations/backups, transactions,
  integrity checks, durable sources/runs/jobs/stages, shared bounded content,
  coverage/search projections, schema-4 shared media evidence, schema-5
  bounded Content Intelligence, and media
  relationship features, relationship evidence/edges/corrections,
  virtual collections/membership, isolated Knowledge Graph/decision sidecars,
  retention, quota maintenance, and compaction.
- **Must not own:** Views, ViewModels, Search ranking, discovery/processing
  policy, source-file mutation, PostgreSQL clients, or server configuration.
- **Principal entry points:** `SqliteDeepIndexStore`, `SqliteGraphStore`,
  `SqliteGraphDecisionStore`, and the deep-index graph projection adapter.
- **Dependencies:** Application and Core.
- **Reference rule:** Desktop composes this provider behind `IDeepIndexStore`,
  `IIndexPrivacyStore`, `IRelationshipStore`, and provider-neutral graph
  contracts. No Application, ViewModel, or View may use SQLite-specific APIs.

### `OpenSorSe.Desktop`

- **Purpose:** Avalonia composition root and presentation layer.
- **Owns:** Startup/DI, application lifetime, navigation, Views, ViewModels,
  dialogs, commands, progress/cancellation presentation, clipboard, and safe
  shell launching.
- **Must not own:** scanning algorithms, persistence schemas, provider
  validation, or raw user-file mutation.
- **Principal entry points:** `Program`, `App`, `MainWindow`, and
  `MainViewModel`.
- **Important folders:** `Views` contains XAML and minimal code-behind;
  `ViewModels` owns presentation state and commands; `Services` contains
  presentation-only adapters.
- **Dependencies:** All production projects because Desktop is the composition
  root.
- **Reference rule:** No production project may reference Desktop.

## Test projects

| Project | Primary scope |
| --- | --- |
| `OpenSorSe.Core.Tests` | Core infrastructure plus repository documentation and dependency invariants |
| `OpenSorSe.Scanner.Tests` | Traversal, metadata, hashes, classification, duplicates, bounds, and cancellation |
| `OpenSorSe.Rules.Tests` | Rule evaluation, planning, conflicts, and pure models |
| `OpenSorSe.Executor.Tests` | Change Plans, validation, journalling, execution, rollback, recovery, Undo, and compatibility regressions |
| `OpenSorSe.Application.Tests` | Orchestration, stores, OCR/content, AI policy, catalog/search, watchers, workflows, plugins, provenance, and provider-neutral Knowledge Graph behavior |
| `OpenSorSe.Indexing.Sqlite.Tests` | Schema, migration, corruption, incremental identity, durable stages, graph/decision sidecars, concurrency, cancellation, recovery, quota, Search coverage, and bounded performance regressions |
| `OpenSorSe.Desktop.Tests` | Composition, navigation, ViewModels, command state, persistence presentation, Knowledge Graph accessibility, and plugin UI |

Tests may reference the production project under test and explicit
collaborators needed for integration. Production projects never reference test
projects.

## Repository-level directories

- Root living documents: `README.md`, `PRODUCT_VISION.md`,
  `PRODUCT_ROADMAP.md`, `ENGINEERING_PRINCIPLES.md`, `RELEASE_HISTORY.md`, and
  `CONTRIBUTING.md` define the product, direction, policy, history, and
  contribution entry points.
- `docs/`: Current guides, historical release records, architecture, and
  implementation specifications. Start at [docs/README.md](README.md).
- `docs/Architecture/`: Current architectural summaries plus clearly indexed
  historical/long-term design material.
- `docs/Implementation_Spec/`: Numbered, release-specific implementation
  history. Start at its [index](Implementation_Spec/README.md).
- `scripts/`: Maintainer scripts only. It must not contain product source or
  generated output.
- `release/`: Historical packaged release material already tracked by the
  repository. New release output must not be added during ordinary development.
- `.artifacts/`, `bin/`, and `obj/`: Ignored build output, never source.

## Where to make a change

| Change | Preferred location |
| --- | --- |
| Scanner stage or file-level deterministic analysis | `OpenSorSe.Scanner` plus Scanner tests |
| Pure rule/plan behavior | `OpenSorSe.Rules` plus Rules tests |
| Change Plan, validation, journal, execution, recovery, or Undo | `OpenSorSe.Executor` plus Executor tests |
| Product orchestration, persistence, watcher, workflow, content, or plugin host | `OpenSorSe.Application` plus Application tests |
| Embedded durable-index schema/provider mechanics | `OpenSorSe.Indexing.Sqlite` plus SQLite provider tests |
| Knowledge Graph identity/projection/query/privacy policy | `OpenSorSe.Application/KnowledgeGraph` plus Application tests |
| Knowledge Graph SQLite lifecycle/persistence | `OpenSorSe.Indexing.Sqlite/KnowledgeGraph` plus SQLite provider tests |
| Ollama HTTP behavior | `OpenSorSe.AI` plus Application integration tests |
| Navigation, ViewModel, or XAML | `OpenSorSe.Desktop` plus Desktop tests |
| Plugin-author contract | `OpenSorSe.Extensions.Abstractions`, compatibility docs, and adversarial host tests |
