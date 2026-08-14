# OmniSorSe architecture overview

This is the authoritative top-level architecture for the released OmniSorSe
v2.4 implementation candidate. It extends the released OpenSorSe v2.3.0
baseline without changing schema 5 or established profile locations.
The
[system map](Architecture/OpenSorSe_System_Map.md) provides the visual
companion, and the [repository structure guide](REPOSITORY_STRUCTURE.md)
describes project ownership and references.

The root [Product Vision](../PRODUCT_VISION.md) explains the product reasons
for local-first, review-before-change, Search, SQLite, and provider neutrality.
[Engineering Principles](../ENGINEERING_PRINCIPLES.md) explains the
cross-cutting implementation and validation policy.

## Architectural shape

OmniSorSe is a local-first cross-platform desktop application with a primary
Windows target, native macOS distribution, and Linux source-build preview. It is built with .NET 8,
Avalonia, MVVM, dependency injection, asynchronous bounded services,
user-local JSON persistence, an embedded provider-isolated SQLite Search index,
and optional isolated Knowledge Graph sidecars. Most of the application analyses data or creates
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
| Media Intelligence candidate | `IMediaIntelligenceService` coordinates capability-based metadata, transcription, representative-frame, OCR, visual-description, and thumbnail providers. Structured evidence enters the existing durable stage/store/Search contracts; providers never own traversal, ranking, or source-file mutation. |
| Content Intelligence candidate | `IContentIntelligenceProvider` receives only bounded retained evidence and returns normalized topics, textual entities, keywords, an extractive summary, provenance, and a processing fingerprint. `WhisperCppTranscriptionProvider` is an optional user-managed local process adapter; no runtime/model is bundled or downloaded. |
| Explainable Smart Tags candidate | `ISmartTagClassifier` consumes already retained bounded evidence; `ISmartTagService` is the application authority; and the SQLite provider owns schema-6 taxonomy definitions, assignments, decisions, status, and canonical filter joins. Classification is deferred behind base Search and does not repeat extraction or modify source metadata. |
| Explorer Protocol v1 | The dependency-free `OmniSorSe.ExplorerProtocol` project owns only DTOs/enums/version/capabilities. Application `IExplorerDataSource`, `ExplorerReadService`, and the on-demand current-user local named-pipe host project authorized indexed Structure/Search/Context without exposing SQLite or write operations. The host remains dormant unless an explicit session is requested. Unreleased v2.5 adds a desktop-only `ExplorerCompanionLaunchService` that discovers a separate OmniBrille executable on demand and transfers one scoped session through OmniBrille's established one-time current-user handoff pipe; Protocol v1 itself is unchanged. |
| Progressive Search | `SemanticSearchService` combines the compatible existing JSON index with `IProgressiveSearchSource`, then delegates constrained local interpretation, coherent hybrid ranking, explanations, and snippets to provider-neutral Application services. |
| Index privacy and repair | `IIndexPrivacyStore` and `IIndexPrivacyService` expose inspection, forgetting, per-file policy, selective clearing, and durable targeted repair without exposing SQLite or source-file mutation to the ViewModel. |
| Relationships and context | `IRelationshipEngine`, `IRelationshipStore`, and `IRelationshipService` own bounded evidence, deterministic confidence, virtual Smart Collections/timelines, user corrections, privacy, and repair; the SQLite provider supplies persistence and `CollectionsViewModel` remains provider-neutral. |
| Knowledge Graph | Provider-neutral graph projection/query/decision/privacy/repair contracts and a durable coordinator own conservative graph behavior; the SQLite provider owns isolated schema-1 sidecars, and `KnowledgeGraphViewModel` owns bounded accessible presentation. |
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
filters while preserving uncertain words as topic terms. `ISearchRanker`
owns all score tiers and deterministic tie-breaking; Views and ViewModels never
calculate ranking weights. `ISearchSnippetFactory` derives bounded snippets only
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

## v2.1 Search and optional-AI quality boundary

The deterministic ranker adds explicit complete-filename, filename-stem,
prefix, substring, and bounded transposition evidence while retaining strong
literal document phrases and the existing stable tie-breakers. An explicit
`SearchRequest.UseAiAssistance` may invoke `IAiSearchAssistant` only after local
ranking. The assistant supplies at most 12 known candidates with opaque IDs to
the existing `IAiSuggestionProvider`, rejects ungrounded output, preserves
scores/membership, and can reorder only within deterministic relevance tiers.

Ollama remains optional. Installed and provider-confirmed running state are
discovered asynchronously; failure of the secondary runtime check does not
discard installed models. Search, result explanations/actions, and indexing
coverage remain usable without AI. v2.1 changes no SQLite or JSON schema. See
[v2.1 Search and AI Quality](Architecture/06_Search/12_v2.1_Search_AI_Quality.md).

## v2.2 Media Intelligence boundary

The v2.2 release classifies a conservative media extension set and
produces bounded `IndexedMediaEvidence`. Deterministic image-header/EXIF
parsing and lazy still-image thumbnails are in process. Existing OCR can
recognize enabled image or sampled-frame content. Optional `ffprobe` supplies
audio/video stream metadata, and optional `ffmpeg` produces at most the
configured number of evenly distributed interior frames. Both are invoked
without a shell and with time, output, size, duration, and cancellation bounds.

Transcription and visual descriptions are provider-neutral optional contracts.
Released v2.2 bundled no implementation. Released v2.3 adds an
optional process-isolated adapter for a user-managed whisper.cpp CLI/model;
visual descriptions remain unavailable. Media evidence is versioned and
content-hash shared in schema 4, while the
existing file, job, stage, privacy, quota, recovery, and Search architecture is
retained. See [Media Intelligence v2.2](MEDIA_INTELLIGENCE_v2.2.md).

## v2.3 Content Intelligence boundary

`IContentIntelligenceProvider` consumes only already bounded index evidence.
The deterministic provider produces capped normalized topics, textual entities,
keywords, and a single-sentence extractive summary with source references,
origin, provider/version, and processing fingerprint. It performs no I/O or
network request. Search projects three explicit lower-tier signals while
preserving exact/literal tiers and unknown-ID rejection. Related Files requires
corroborated specific concepts and suppresses generic one-topic clusters.

Schema 5 adds one nullable validated JSON field to content-hash-owned index
content plus an indexed, bounded relationship-term projection through the
established transactional recovery migration. The projection avoids an
unbounded all-pairs relationship scan and is replaced atomically with its
owning feature record. Corrupt derived evidence is omitted and exposed as an indexing failure. Privacy
inspection, byte reporting, per-file clear, source forget, and full index clear
remain provider-neutral and never affect source files. See
[Content Intelligence v2.3](CONTENT_INTELLIGENCE_v2.3.md).

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

## v2.0 Knowledge Graph boundary

The v2.0 graph projects only retained, authoritative v1.9 observations; it does
not open source files or join the v1.7 `FileFullyIndexed` critical path. Stable
nodes are File, Source, Folder, Collection, Document Set, and Manual Entity.
Stable edges are Related File, Owned by Source, Located in Folder, Member Of,
Same Document Set, and Manual. Identity uses stable provider keys, source-scoped
relative folders, validated exact hashes, authoritative v1.9 facts, and explicit
user decisions. Merely similar text or semantic data cannot merge identities.

`knowledge-graph.db` schema 1 is rebuildable derived state;
`knowledge-decisions.db` schema 1 is non-rebuildable graph-native decision and
privacy authority. Released v2.2 used `deep-index.db` schema 4 after a
transactional migration from schema 3. Released v2.3 adds only
bounded Content Intelligence as schema 5 while retaining its authority for v1.9
relationships, Collections, decisions, and privacy. Completed source manifests
carry a canonical count and hash, and projection keeps source, decision, and
privacy ingestion watermarks separate from applied watermarks.

Run control, job execution, freshness, and integrity are independent durable
axes. Projection publication is generation-based. Coordinator epochs and
per-claim tokens fence stale workers; heartbeat, claim TTL, and shutdown grace
are 5, 30, and 5 seconds respectively. Graph reads and Search context fail
closed until source/decision/privacy authority is current and applied.

Ordinary graph pages default to 50 and cap at 100. Stable traversal is one hop
and 100 nodes. Search uses at most 16 existing ranked seeds and 50 graph-only
expansions within the combined contextual cap of 100. Exact/literal ranking and
v1.9 direct relationships keep authority. See
[Knowledge Graph](KNOWLEDGE_GRAPH_v2.0.md) and the
[stability design](Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md).

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
by prior OmniSorSe execution so watcher hints do not recursively create the same
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

Production stores remain rooted under `%LOCALAPPDATA%\OpenSorSe` unless the user
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
| Durable Search index | `SqliteDeepIndexStore` | `index/deep-index.db` | v2.2 schema v4 uses a transactional recovery-copy migration from v2.1 schema v3 and content-hash-shared bounded media evidence. Existing stages, privacy rules, relationships/collections, repair, integrity, recovery, retention, quotas, and rebuildability remain. |
| Knowledge Graph projection | `SqliteGraphStore` | `index/knowledge-graph.db` | Schema v1; rebuildable completed-manifest projection, jobs, generations, nodes/edges/evidence, applied/ingested watermarks, repair, and bounded diagnostics. |
| Knowledge Graph decisions | `SqliteGraphDecisionStore` | `index/knowledge-decisions.db` | Schema v1; append-only graph-native decisions, checkpoints, exclusions, privacy floor, and verified recovery points; never silently reset. |
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
- Knowledge Graph data is optional application-owned projection/decision data;
  graph privacy, repair, and browsing never open or modify original files.
- Media metadata, OCR, transcripts, descriptions, thumbnails, and relationships
  are application-owned derived data and never authorize or perform source-file
  changes.
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
- Knowledge Graph projection uses completed immutable manifests, durable claims,
  generation publication, epoch/token fencing, 5-second heartbeats, 30-second
  leases, and a 5-second cooperative shutdown grace. Reads require current
  integrity plus applied source, decision, and privacy authority.
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
