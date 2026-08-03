# OpenSorSe architecture overview

This is the authoritative top-level architecture for the OpenSorSe 1.9 source
tree. The [system map](Architecture/OpenSorSe_System_Map.md) provides the visual
companion, and the [repository structure guide](REPOSITORY_STRUCTURE.md)
describes project ownership and references.

The root [Product Vision](../PRODUCT_VISION.md) explains the product reasons
for local-first, review-before-change, Search, SQLite, and provider neutrality.
[Engineering Principles](../ENGINEERING_PRINCIPLES.md) explains the
cross-cutting implementation and validation policy.

## Architectural shape

OpenSorSe is a local-first Windows desktop application with a Linux preview, built with .NET 8,
Avalonia, MVVM, dependency injection, asynchronous bounded services,
user-local JSON persistence, and an embedded provider-isolated SQLite Search
index. Most of the application analyses data or creates
proposals. A narrow, explicit Change Plan boundary separates those activities
from approved user-file mutation.

The Desktop project is the composition root. Application services orchestrate
Scanner, Rules, Executor, content, workflow, watcher, plugin, catalog, semantic,
and AI-policy services. `OpenSorSe.AI` provides the optional Ollama transport.
The standalone Extension SDK exposes bounded contracts without exposing
internal services.

## Component view

| Area | Ownership and principal entry points |
| --- | --- |
| Desktop shell and navigation | `App` builds the service provider and initializes recovery, plugins, workflows, and watchers. `MainViewModel` owns navigation and top-level operation state. |
| Manual processing | `ApplicationController` → `ProcessingSessionManager` → `ProcessingOrchestrator`. |
| File discovery | `FileScanner` enumerates selected roots read-only with progress, cancellation, and isolated issues. |
| Basic analysis | `FileMetadataReader`, `FileHasher`, `FileClassifier`, and `DuplicateDetector`. |
| Text and OCR | `ContentIndexingService` → `MetadataExtractionPipeline`; `OcrService`, `PdfPageRasterizer`, and `TesseractCliOcrEngine` supply bounded local OCR where enabled and needed. |
| Durable background indexing | `BackgroundIndexingService` coordinates `IIndexFileDiscovery`, `IIndexingStageProcessor`, and provider-neutral `IDeepIndexStore`; `OpenSorSe.Indexing.Sqlite` supplies the embedded provider. |
| Progressive Search | `SemanticSearchService` combines the compatible existing JSON index with `IProgressiveSearchSource`, then delegates constrained local interpretation, coherent hybrid ranking, explanations, and snippets to provider-neutral Application services. |
| Index privacy and repair | `IIndexPrivacyStore` and `IIndexPrivacyService` expose inspection, forgetting, per-file policy, selective clearing, and durable targeted repair without exposing SQLite or source-file mutation to the ViewModel. |
| Relationships and context | `IRelationshipEngine`, `IRelationshipStore`, and `IRelationshipService` own bounded evidence, deterministic confidence, virtual Smart Collections/timelines, user corrections, privacy, and repair; the SQLite provider supplies persistence and `CollectionsViewModel` remains provider-neutral. |
| Rules and planning | `RuleEngine`, `ActionPlanner`, and `ConflictResolver` produce deterministic proposals; they do not execute them. |
| Optional AI | `AiSuggestionService` owns gates, prompts, parsing, validation, and review outcomes. `OllamaSuggestionProvider` owns HTTP transport. |
| Workflows and recipes | `WorkflowLibraryService`, `WorkflowConfigurationResolver`, `WorkflowTemplateEngine`, and `WorkflowRecipePlanService`. |
| Watched Folders | `WatchedFolderManager`, `WatchedFolderCoordinator`, `WatchedFolderProcessor`, and the three watched JSON stores. |
| Plugins | `PluginManager`, `PluginDiscoveryService`, `PluginDependencyResolver`, `PluginRuntime`, `PluginContributionRegistry`, `PluginExtensionHost`, and `PluginPackageService`. |
| Change Plan creation | `SuggestionChangePlanFactory`, `WorkflowRecipePlanService`, and `ChangePlanFactory` capture proposals and source identity. |
| Review and approval | `ChangePlanReviewViewModel` owns action-level decisions, validation commands, and the separate Apply confirmation. |
| Validation and mutation | `ChangePlanValidator` performs non-mutating checks. `ChangePlanExecutionService` is the supported execution/recovery/Undo boundary and delegates mutation to `IFileSystemGateway`. |
| Journalling and history | `JsonOperationJournalStore` persists attempted operations and action transitions. Operation History and Undo project that store; there is no second mutable history database. |
| Diagnostics | Core `IDiagnosticsCollector`, AI request diagnostics, plugin diagnostics, and ordinary bounded logs are intentionally separate. |
| Platform services | Core `IPathSemantics`, `IApplicationPathProvider`, `IFileIdentityProvider`, `IFileSystemCapabilities`, `IExternalToolLocator`, and `IPlatformCapabilityProvider`; Desktop owns `IDesktopIntegration` and platform presentation. |

## Manual scan and suggestion flow

1. `FolderSelectionViewModel` raises a `ScanRequest`.
2. `MainViewModel.StartProcessingAsync` resolves the chosen Workflow Profile
   through `IWorkflowConfigurationResolver`.
3. `IApplicationController` opens a processing session and invokes
   `ProcessingOrchestrator`.
4. The orchestrator runs file enumeration, basic metadata, hashing,
   classification, exact duplicate detection, rules, action planning, and
   conflict resolution in order. Optional content indexing is failure-isolated.
5. `ResultsSnapshotProjector` produces the immutable presentation snapshot.
6. The Desktop may persist an opt-in saved scan and refresh local content or
   semantic views.
7. Rules, a Sorting Recipe, or optional AI may produce suggestions.
8. A suggestion adapter creates a non-mutating Change Plan. Plugin-derived
   recipe fields carry plugin/version/contribution provenance.
9. `ChangePlanReviewViewModel` presents action-level review. Editing invalidates
   prior validation.
10. Only approved actions that pass explicit validation and a separate Apply
    confirmation can enter `ChangePlanExecutionService`.

The current plugin foundation exposes all eight bounded invocation surfaces.
Workflow dependency checks and plugin recipe fields are integrated with
Workflow/Profile resolution and recipe planning. Other extension-point adapters
are host-callable through `IPluginExtensionHost`; broad insertion into every
legacy scanner/content pipeline stage is deliberately not implied by v1.9.

## v1.6 reliability boundary

Every versioned application-owned JSON store uses the shared bounded atomic
writer. A normalized host-path coordinator covers complete process-local
transactions across independent store instances. Duplicate analysis and Results
query/projection use bounded allocations and cooperative cancellation.
Processing-session history is bounded, background task progress is terminal
safe, observer failures are isolated, and watcher initialization/disposal is
idempotent. See [Reliability Architecture](Architecture/00_System/09_v1.6_Reliability_Architecture.md).

## v1.7 deep-indexing boundary

Application owns provider-independent stage, progress, storage, Search, and
control contracts. The SQLite project owns relational schema and provider
mechanics; Views, ViewModels, Search ranking, and other application services do
not reference SQLite APIs. Durable transactions separate discovery batches,
claims, and stage outputs. Startup recovers interrupted `running` rows,
completed compatible work is reused, content-derived values can be shared by
hash, and quota maintenance is explicit. PostgreSQL is not a desktop runtime
dependency; a future server adapter can implement the same contracts. See
[Deep Indexing Architecture](Architecture/00_System/10_v1.7_Deep_Indexing_Architecture.md).

## v1.8 Search-intelligence and privacy boundary

`ISearchQueryInterpreter` turns bounded ordinary text into visible removable
filters while preserving uncertain words as topic terms. `IHybridSearchRanker`
owns all score tiers and deterministic tie-breaking; Views and ViewModels never
calculate ranking weights. `ISearchSnippetService` derives bounded snippets only
from retained indexed material, and explanations are assembled from the actual
ranking components. The JSON compatibility index and SQLite progressive source
may fail independently; Search uses whichever remains available and reports
coverage limits.

Index inspection, forgetting, policy, and selective repair use Application
contracts. The SQLite provider owns transactions and schema 2 privacy rules;
the background coordinator owns cancellation, durable queued repair, and
coverage refresh. Every such action changes application-owned index state only.
See [Search Intelligence and Privacy
Architecture](Architecture/06_Search/09_v1.8_Search_Intelligence_Privacy.md).

## v1.9 relationships and context boundary

`IRelationshipEngine` compares only bounded retained index projections and
publishes automatic edges only with actual evidence. `IRelationshipStore`
isolates schema 3 persistence, while `IRelationshipService` coordinates durable
analysis, manual decisions, virtual collection control, privacy, Search
expansion, diagnostics, and repair. Views and ViewModels do not use SQL or
calculate confidence.

The existing durable relationship stage performs incremental work and reuses
unchanged completed processing. Pair corrections, collection splits, and
forgotten collection tombstones prevent unwanted regeneration. Queries are
direct and bounded; no recursive graph traversal or O(n²) all-file comparison
is performed. Relationship-only Search results remain below exact/literal
matches and can be disabled per query. See [Relationships, Context and Smart
Collections](Architecture/06_Search/10_v1.9_Relationships_Context.md).

## Platform boundary

Platform detection is contained in `OpenSorSe.Core.Platform`. Windows preserves
the existing local-application-data layout, case-insensitive semantics, and
volume/file-index identity. Linux uses XDG storage categories, case-sensitive
semantics, device/inode identity, advisory Unix permission checks, and explicit
watcher/desktop limitations. Business logic consumes contracts rather than
detecting an OS. Failure to verify identity, links, permissions, or a
same-filesystem move blocks mutation. The complete design and support claim are
in [Platform Architecture](Architecture/00_System/08_v1.5_Platform_Architecture.md)
and the [Capability Matrix](PLATFORM_COMPATIBILITY_MATRIX.md).

## Watched-folder flow

1. `FileSystemWatcherEventSource` translates operating-system notifications
   into hints; notifications are never treated as filesystem truth.
2. `WatchedFolderCoordinator` root-checks and debounces hints into a bounded
   channel. Overflow or backpressure requests reconciliation.
3. Startup, resume, reconnect, overflow, periodic maintenance, configuration
   change, and manual commands can request a full reconciliation.
4. `WatchedFolderProcessor` discovers actual state, applies ignore policy,
   checks file stability, and compares it with the dedicated watched catalogue.
5. Changed files pass through bounded metadata/content/hash/classification/
   duplicate/rule analysis according to the resolved Workflow Profile.
6. The catalogue is updated independently of optional AI success. AI retry
   state prevents successful items from being repeated.
7. `WatchedSuggestionService` can create a Change Plan for review.
8. The coordinator records grouped activity and raises a review request. It
   never calls `IChangePlanExecutionService`.

`OperationJournalWatchedExecutionCorrelation` recognizes verified events caused
by prior OpenSorSe execution so watcher hints do not recursively create the same
proposal. It does not suppress unrelated external changes.

## Plugin lifecycle

1. A user selects a local ZIP; there is no marketplace or downloader.
2. `PluginPackageService` checks archive bounds, normalized paths, duplicate
   entries, links/reparse points, native declarations, managed entry assembly,
   strict manifest shape, and optional integrity.
3. Extraction occurs in a controlled staging directory. The staged tree and
   manifest are revalidated before an atomic directory move into
   the platform-owned plugin root under `<id>/<version>`.
4. `PluginDiscoveryService` performs bounded manifest-first discovery without
   executing code.
5. `PluginDependencyResolver` resolves exact versions, required transitive
   dependencies, cycles, compatibility, integrity, and conflicts
   deterministically.
6. External plugins remain disabled until explicit enable and capability grant.
7. `PluginRuntime` creates one collectible `AssemblyLoadContext` per external
   plugin, initializes it with a bounded context and timeout, and registers only
   manifest-declared, capability-authorized contributions.
8. `PluginExtensionHost` applies call timeouts, cancellation, exception
   containment, and output validation.
9. Failures are recorded; repeated activation failure can quarantine a plugin.
   Disable unregisters contributions. Actual unload may require restart.
10. Upgrade keeps the previous version for rollback. Removal is confirmed and
    blocked while known configurations depend on the exact version.

An assembly-load context isolates dependency resolution, not operating-system
permissions. External code runs in-process as the current user. SHA-256 detects
change but does not authenticate a publisher. The supported SDK provides no
executor, mutation gateway, Change Plan approval, settings store, credentials,
or service provider.

## The only supported user-file mutation path

```text
Suggestion
  → persisted Change Plan
  → action-level user selection
  → explicit validation
  → separate Apply confirmation
  → immediate pre-execution revalidation
  → durable pending/running Operation Journal
  → ChangePlanExecutionService
  → PhysicalFileSystemGateway
  → result verification and journal update
  → Operation History
  → conflict-aware Undo or restart recovery
```

Responsibilities along this path:

- `ChangePlanFactory` captures normalized proposals and source identity.
- `JsonChangePlanStore` owns draft persistence.
- `ChangePlanReviewViewModel` owns review state and explicit commands.
- `ChangePlanValidator` rejects stale, out-of-root, linked, occupied,
  conflicting, invalid, unsupported, or changed actions without mutation.
- `JsonOperationJournalStore` must persist the operation before mutation and
  after each durable transition.
- `ChangePlanExecutionService` freezes approved actions, orders and executes
  them, verifies results, and rolls back completed reversible work on a
  blocking failure.
- `PhysicalFileSystemGateway` contains the low-level rename/move/create/remove-
  empty-directory operations used by this boundary.
- `ChangePlanExecutionService.UndoAsync` revalidates recorded result identity
  and refuses overwrite or unsafe reversal.
- `RecoverInterruptedAsync` inspects journalled state on startup and reports
  what can be safely recovered or undone.

The older `ActionExecutor` and `UndoEngine` remain as unregistered compatibility
code. They are not the production route for current Desktop suggestions.

## Persistence view

Production stores are rooted under `%LOCALAPPDATA%\OpenSorSe` unless the user
chooses an allowed log directory. Source files remain where the user selected
them.

| Data | Owner | Default location | Version/corruption behavior |
| --- | --- | --- | --- |
| Application settings | `JsonConfigurationService` | `settings.json` | Validated bounded JSON; missing is default; save uses a temporary sibling and replace. |
| Ordinary logs | `LocalFileLoggerProvider` | `Logs/` | Daily bounded owned files with retention; no source content or secrets. |
| Saved scans/catalogues | `JsonResultsCatalogStore` | `catalog.json` | Schema v2, reads supported older schema, bounded atomic replacement. |
| Saved searches | `JsonSavedCatalogSearchStore` | `saved-catalog-searches.json` | Schema v1; invalid input fails closed; hits are not persisted. |
| Extracted content | `JsonContentStore` | `content-index.json` | Schema v1; bounded/rebuildable; contains potentially sensitive local text. |
| Semantic index | `JsonSemanticIndexStore` | `semantic-index.json` | Schema v1; bounded/rebuildable deterministic vectors and terms. |
| Durable Search index | `SqliteDeepIndexStore` | `index/deep-index.db` | Schema v3; v1/v2 migrate transactionally with a recovery copy; transactional stages, privacy rules, relationship evidence/corrections/virtual collections, targeted repair, integrity checks, backups, interruption recovery, shared bounded content, retention, quota maintenance, and rebuildable derived data. |
| AI decisions | `JsonDecisionHistoryStore` | `decision-history.json` | Bounded metadata-only review history. |
| Structure history | `JsonStructureHistoryStore` | `structure-history.json` | Schema v1; bounded snapshots and relative paths. |
| Workflow Profiles and Sorting Recipes | `JsonWorkflowLibraryStore` | `workflow-library.json` | Library schema v2; migration occurs on load/save; a corrupt source is preserved where possible before built-in recovery. |
| Watched Folder configuration | `JsonWatchedFolderConfigurationStore` | `watched-folders.json` | Schema v3 with bounded migration/validation. |
| Watched catalogues | `JsonWatchedFolderCatalogueStore` | `watched-catalogues.json` | Schema v2; bounded atomic updates independent of saved scans. |
| Watched activity | `JsonWatchedActivityStore` | `watched-activity.json` | Schema v1 grouped activity; raw events are not persisted. |
| Change Plans | `JsonChangePlanStore` | `change-plans.json` | Schema v1; bounded drafts; corruption blocks use rather than executing. |
| Operation Journal / History | `JsonOperationJournalStore` | `operation-journal.json` | Schema v1; durable action facts, rollback, recovery, and Undo state. |
| Plugin state/integrity | `JsonPluginStateStore` | `plugins-state.json` | Schema v1; atomic enabled/grants/hash/failure/quarantine state. |
| Installed plugins | `PluginPackageService` | `plugins/<id>/<version>/` | Controlled exact-version directories; not user documents. |
| Advanced Diagnostics | `InMemoryDiagnosticsCollector` | Memory unless explicitly exported | Cleared on disable/exit; bounded and redacted by default. |

Every store owns its own schema and migration. A maintainer changing a record
must update that owner, its bounds/validation, migration or explicit rejection,
tests, safety documentation, and release notes together. Application-data files,
plugin binaries, logs, exports, OCR temporary images, and test workspaces must
never be committed.

## Safety invariants

- AI proposes but does not execute.
- Plugins contribute bounded data or proposals but have no supported direct
  mutation or approval API.
- Watched Folders detect, reconcile, and analyse but never execute.
- Relationships and Smart Collections are derived or user-authored index data;
  their controls never modify original files.
- Workflow Profiles configure processing; they do not approve actions.
- Sorting Recipes generate sanitized names and destinations; they do not apply
  them.
- Only the executor performs approved user-file mutation.
- The Operation Journal is durable before the first mutation.
- Validation at review time is not sufficient; Apply revalidates immediately.
- Paths remain within the approved root and linked escapes are rejected.
- Silent overwrite and permanent deletion are unsupported.
- Cancellation is observed at defined safe boundaries; it is not represented as
  success.
- Undo relies on recorded execution state and current identity, and blocks
  rather than overwriting changed data.

## Concurrency and cancellation

- Long-running APIs accept `CancellationToken`; cancellation is propagated
  through scan, extraction, OCR, AI, watcher, plugin, persistence, and executor
  boundaries.
- `MainViewModel` and feature ViewModels own presentation cancellation sources
  and guard overlapping operations.
- Watched Folder coordination uses a bounded channel, a lifecycle semaphore,
  per-root debounce state, and one lifetime cancellation source.
- JSON stores serialize writes with semaphores and replace only their own target
  after complete serialization and size validation.
- The durable Search provider serializes provider operations, moves synchronous
  SQLite work off the UI thread, uses short transactions, supports concurrent
  external readers through WAL, and cooperatively cancels discovery/workers.
- Search admits at most four overlapping queries per service instance; query
  size, token/filter count, fuzzy candidates, result count, snippets, and
  provider projections are bounded and cancellation-aware.
- Plugin lifecycle and package operations are serialized by `PluginManager`;
  extension calls receive linked timeout/caller cancellation.
- Executor cancellation is checked at safe action boundaries so a partially
  applied operation remains journalled and recoverable.

## Architectural risks and technical debt

- `MainViewModel`, several feature ViewModels, watcher services, AI parsing, and
  plugin infrastructure are large. Future releases should extract narrowly
  scoped coordinators only with regression coverage; broad rewrites would risk
  lifecycle and safety behavior.
- External plugins are in-process. Strong isolation requires a future
  out-of-process protocol, OS-level policy, authentication, and a compatible
  failure model.
- Several legacy architecture documents describe aspirational database,
  reporting, reader, and plugin systems. The architecture index marks them as
  design history so they are not confused with this document.
- Application store paths are composed centrally in `App`. A typed
  application-data path object could reduce repetition in a future release.
- The eight plugin points have a complete host invocation surface, but not every
  legacy processing stage consumes external contributions yet. Add adapters
  deliberately, with capability, timeout, provenance, and fail-closed tests.
- Desktop startup performs synchronous initialization before the first window.
  A future startup coordinator could improve progress reporting while
  preserving recovery → plugin → workflow → watcher ordering.
