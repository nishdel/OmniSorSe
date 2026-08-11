# Developer guide

This guide takes a new contributor from clone to a small, safely tested change.
Use it with the [Repository Structure](REPOSITORY_STRUCTURE.md) and
[Architecture Overview](ARCHITECTURE_OVERVIEW.md). Read the root
[Engineering Principles](../ENGINEERING_PRINCIPLES.md) before a cross-cutting
change.

## 1. Clone and inspect

```powershell
git clone https://github.com/nishdel/OpenSorSe.git
Set-Location .\OpenSorSe
git status --short --branch
git branch --all
dotnet --info
```

Windows is the primary Desktop target. macOS Intel and Apple Silicon have
native package paths for read-only and non-mutating functionality; Linux x64
remains a source-build preview. The solution targets .NET 8 and the exact SDK
selection is in `global.json`. Linux contributors should also read
[Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md), while release maintainers
should read [Native Release Packaging](RELEASE_PACKAGING_v2.0.md).

Confirm the intended base before creating a branch. A product version, branch,
or package name is not proof that a commit is integrated; inspect `main`, its
remote-tracking branch, tags, and [Release Status](RELEASE_STATUS.md).

## 2. Restore and build

```powershell
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
```

Warnings are errors. A successful build should report zero warnings and zero
errors.

## 3. Run the application

```powershell
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Windows stores user-local state below `%LOCALAPPDATA%\OpenSorSe`. Linux follows
XDG configuration/data/state/cache locations. Settings → Platform diagnostics
shows exact owned paths. Use disposable scan roots for development. Do not test
Change Plan Apply against important files.

## 4. Run validation

```powershell
dotnet test .\OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet build .\OpenSorSe.sln --configuration Release --no-restore
dotnet test .\OpenSorSe.sln --configuration Release --no-build --no-restore
dotnet format .\OpenSorSe.sln --verify-no-changes --no-restore
git diff --check
```

Repository documentation tests validate relative Markdown links, Mermaid fence
shape, key entry points, SDK XML documentation, and the production project
reference policy.

GitHub Actions repeats restore, Debug/Release builds and complete tests,
formatting, and documentation/dependency checks on `windows-latest`,
`ubuntu-latest`, and `macos-latest`. The separate manually dispatched native
packaging workflow creates temporary release artifacts only for an explicitly
selected ref; it does not publish a GitHub Release by itself.

Run the deterministic Search-quality and bounded performance groups separately
when changing interpretation, ranking, snippets, coverage, relationships, or
provider queries:

```powershell
dotnet test .\tests\OpenSorSe.Application.Tests\OpenSorSe.Application.Tests.csproj `
  --configuration Release --filter Category=SearchRelevance
dotnet test .\tests\OpenSorSe.Application.Tests\OpenSorSe.Application.Tests.csproj `
  --configuration Release --filter Category=PerformanceRegression
```

The relevance corpus is synthetic and reports regression-oriented metrics; it
does not claim universal quality. Performance tests use bounded synthetic
candidate sets and are not desktop latency guarantees.

## 5. Locate the major systems

Start at these entry points:

| Question | Entry point |
| --- | --- |
| How is the app composed? | `OpenSorSe.Desktop/App.axaml.cs` |
| How does navigation reach features? | `OpenSorSe.Desktop/ViewModels/MainViewModel.cs` |
| How does a manual scan run? | `ApplicationController`, `ProcessingSessionManager`, `ProcessingOrchestrator` |
| How are files discovered? | `Scanner/FileScanner.cs` |
| How are content and OCR coordinated? | `Application/Content/ContentIndexingService.cs` and `OcrService.cs` |
| How does durable indexing work? | `Application/Indexing/BackgroundIndexingService.cs`, `IDeepIndexStore`, and `OpenSorSe.Indexing.Sqlite/SqliteDeepIndexStore.cs` |
| How does progressive Search work? | `Application/Semantic/SemanticSearchService.cs` and `IProgressiveSearchSource` |
| How are relationships and Smart Collections built? | `Application/Relationships`, `IRelationshipStore`, and `Indexing.Sqlite/SqliteRelationship*.cs` |
| How is the Knowledge Graph projected and queried? | `Application/KnowledgeGraph`, `IGraphProjectionCoordinator`, and `Indexing.Sqlite/KnowledgeGraph` |
| How are workflows resolved? | `Application/Workflows/WorkflowConfigurationResolver.cs` |
| How are watcher hints processed? | `Application/Watching/WatchedFolderCoordinator.cs` and `WatchedFolderProcessor.cs` |
| How are plugins discovered and loaded? | `Application/Plugins/PluginInfrastructure.cs` and `PluginRuntime.cs` |
| How is a Change Plan made safe? | `Executor/ChangePlanFactory.cs`, `ChangePlanValidator.cs`, and `ChangePlanExecutionService.cs` |
| Where is operation history stored? | `Executor/JsonOperationJournalStore.cs` |

## 6. Trace a manual scan

1. `FolderSelectionViewModel` creates a Desktop `ScanRequest`.
2. `MainViewModel.StartProcessingAsync` resolves a Workflow Profile and maps
   presentation input to an Application `ProcessingRequest`.
3. `ApplicationController` delegates to `ProcessingSessionManager`, which owns
   session lifecycle and cancellation state.
4. `ProcessingOrchestrator` invokes Scanner/Rules stages sequentially and
   reports typed progress.
5. Optional `ContentIndexingService` enriches local content without making the
   core scan fail if extraction/OCR is unavailable.
6. `ResultsSnapshotProjector` creates the immutable Results model.
7. Desktop feature ViewModels consume the snapshot; optional catalog/content/
   Search stores persist only when their feature flow requests it. Completed
   scans also queue their roots into durable background indexing when enabled.

Set breakpoints at `MainViewModel.StartProcessingAsync`,
`ProcessingOrchestrator.ProcessAsync`, and `FileScanner.ScanAsync` to observe
the request crossing layers.

## 7. Trace Search and index privacy

1. `SemanticSearchView` binds ordinary query text and visible active filters to
   `SemanticSearchViewModel`.
2. `DeterministicSearchQueryInterpreter` validates bounds and produces topic
   terms plus explicit `SearchFilter` values.
3. `SemanticSearchService` concurrently loads the compatible JSON index and
   progressive provider documents, tolerating an independent recoverable store
   failure.
4. `HybridSearchRanker` evaluates explicit signals, exact/literal tiers,
   bounded typo candidates, optional semantic similarity, completeness,
   recency, and deterministic ties.
5. `SearchSnippetFactory` creates a bounded source-labelled snippet from
   content already present in the candidate. It never extracts a source file at
   query time.
6. The ViewModel projects actual components into **Why this result?** and
   displays coverage limitations.
7. When explicitly enabled, `SemanticSearchService` passes at most 12 already
   ranked candidates to `IAiSearchAssistant`. The assistant may reorder only
   within deterministic tiers; failure returns the original candidates.
8. Inspection/forget/clear/policy/repair commands use `IIndexPrivacyService`;
   `BackgroundIndexingService` coordinates cancellation and durable repair,
   while `SqliteDeepIndexStore` owns transactional provider state.

Views and ViewModels must not create SQL, parse SQLite/FTS syntax, call Ollama,
or calculate ranking scores. New provider implementations must satisfy the same
Application contracts without exposing database details.

## 8. Trace relationships and Smart Collections

1. The durable `RelationshipAnalysisCompleted` indexing stage calls
   `IRelationshipService` after applicable Search data is retained.
2. `DeterministicRelationshipEngine` creates bounded versioned features and
   compares only provider-selected candidates. It returns proposals containing
   the actual evidence used.
3. `SqliteDeepIndexStore` atomically replaces stale automatic edges for the
   analyzed file, preserving manual edges, pair corrections, member overrides,
   and forgotten-collection tombstones.
4. `CollectionsViewModel` reads `IRelationshipService` projections for Smart
   Collections, Related Files, inspectors, timeline, privacy, and repair.
5. `SemanticSearchService` may request bounded direct expansions through
   `IRelationshipSearchSource`; `HybridSearchRanker` keeps literal tiers above
   relationship-only results.

Relationship code must never derive an explanation that was not retained as
evidence. Keep candidate, edge, evidence, member, and expansion bounds intact.
Manual corrections and source ownership are compatibility data. The v2.0
Knowledge Graph is a separate provider-neutral projection and sidecar, not an
unbounded recursive query added to the schema-3 provider.

## Trace the Knowledge Graph candidate

1. The schema-3 projection-source adapter captures only a completed canonical
   manifest with a stable ID, row count, hash, revision, legacy-decision
   manifest, and privacy sequence.
2. `GraphProjectionCoordinator` pages that manifest into durable inbox/jobs,
   validates its completion, and claims bounded work using fencing epoch plus
   opaque claim token.
3. `ConservativeGraphIdentityResolver` and
   `DeterministicGraphProjectionBuilder` create only stable identities, edges,
   and actual evidence. They never open a source file.
4. `SqliteGraphStore` publishes validated generations in
   `knowledge-graph.db`; `SqliteGraphDecisionStore` independently owns
   append-only graph-native decisions and privacy recovery points in
   `knowledge-decisions.db`.
5. `GraphQueryService`, graph Search context, privacy, repair, decision, and
   diagnostics services revalidate provider bounds and source/decision/privacy
   authority before use.
6. `KnowledgeGraphViewModel` projects bounded pages, direct neighbors, evidence,
   progress, coverage, manual controls, privacy, and repair. It does not know
   SQLite.
7. `SemanticSearchService` may expand at most 16 ordinary seeds by one hop; it
   caps graph-only targets at 50 and all contextual targets at 100, preserving
   exact/literal and v1.9 direct-relationship priority.

Keep `deep-index.db` at schema 3. Its v1.9 decisions remain authoritative; the
graph mirror is derived. Do not collapse ingested and applied source, decision,
or privacy watermarks. Do not turn stale or repair-required graph data into a
successful read. The four independent axes are run control, job execution,
freshness, and integrity.

Graph tests should use synthetic manifests, fake clocks, deterministic workers,
and temporary application-data roots. Cover manifest hash/count rejection,
idempotent rebuild, fencing, expired claims, cancellation at publication,
watermark lag, privacy races, backup privacy floors, corruption/newer schemas,
query/traversal bounds, Search fallback, and unchanged source files. Never scan
a developer directory. See [Knowledge Graph v2.0](KNOWLEDGE_GRAPH_v2.0.md).

## 9. Trace a Change Plan

1. A rule, Sorting Recipe, reviewed AI suggestion, or watched suggestion
   produces a proposal.
2. `SuggestionChangePlanFactory`, `WorkflowRecipePlanService`, or another
   adapter calls `ChangePlanFactory`.
3. `ChangePlanFactory` normalizes paths, captures source identity, validates the
   draft, and stores it.
4. `ChangePlanReviewViewModel` owns edits and action-level approval. Any edit
   clears prior validation.
5. The user runs Validate and separately confirms Apply.
6. `ChangePlanExecutionService.ExecuteAsync` performs immediate preflight,
   persists a pending/running journal, and only then uses
   `PhysicalFileSystemGateway`.
7. Results are verified and persisted after each action. A blocking failure
   triggers reverse-order rollback where safe.
8. Startup calls `RecoverInterruptedAsync`. Operation History and Undo use the
   same journal facts.

Never add a shortcut from a suggestion service or ViewModel to raw
`File.Move`, `Directory.Move`, or the compatibility executor.

## 10. Add a small feature

1. Identify the owning project using `REPOSITORY_STRUCTURE.md`.
2. Start with the narrow contract and domain behavior.
3. Add bounds, cancellation, error semantics, and logging/diagnostics only at
   the owning boundary.
4. Compose the implementation in Desktop only if it is a production service.
5. Add the narrow unit tests, then integration/ViewModel tests if the feature
   crosses layers.
6. Update the authoritative user/architecture/safety document affected.
7. Run the complete validation set.

Avoid moving logic between projects merely for aesthetics. A small extraction
is useful when it makes ownership or a safety phase explicit and preserves
behavior.

## 11. Add a test

- Put deterministic domain tests beside the owning production project.
- Use fakes for filesystem/provider/plugin failures unless the test explicitly
  verifies a real bounded integration.
- For real filesystem tests, create a GUID-named directory below the system
  temporary directory and delete only that exact directory.
- Assert cancellation and controlled failures, not timing-sensitive incidental
  details.
- Do not rely on test order or machine-installed Ollama/Tesseract.

## 12. Add a plugin contribution

1. Reference `OpenSorSe.Extensions.Abstractions` only.
2. Implement `IOpenSorSePlugin` and a supported contribution interface.
3. Declare the exact contribution and required capabilities in `plugin.json`.
4. Return immutable bounded results and accurate provenance.
5. Treat cancellation as mandatory; do not start untracked background work.
6. Package one root manifest plus managed assemblies/resources in a local ZIP.
7. Test install, explicit grants, enable, timeout, invalid output, disable,
   restart, dependency loss, upgrade rollback, and removal.

The host does not provide direct mutation, approval, credentials, arbitrary
storage, or dependency injection. External plugin code still runs in-process
with the current user's permissions. See the [Extension SDK](EXTENSION_SDK_v1.4.md)
and [Plugin Author Guide](PLUGIN_AUTHOR_GUIDE_v1.4.md).

## 13. Add a media provider

1. Implement the narrow capability contract in `OpenSorSe.Application.Media`;
   do not add SQL, traversal, ranking, or Desktop concerns to the provider.
2. Return bounded structured evidence and explicit unavailable/failed/skipped
   status. Provider/model/configuration version must participate in the
   processing fingerprint.
3. For processes, use `IExternalMediaProcessRunner` and argument lists; never
   build a shell command from a path. Bound time, output, files, frames, and
   temporary storage, and preserve caller cancellation.
4. Register the provider only in the Desktop composition root. Missing optional
   tools must retain filename/document Search and expose a truthful capability.
5. Extend Search projection, privacy clearing, diagnostics, and tests only for
   evidence the provider actually supplies. Do not make AI descriptions
   authoritative metadata.
6. Evaluate license, redistribution, package size, offline/network behavior,
   and all runtime targets before adding a dependency. See
   [Media Intelligence v2.2](MEDIA_INTELLIGENCE_v2.2.md).

## Common pitfalls

- Treating a watcher notification as authoritative instead of reconciling.
- Reusing a validation result after the plan or filesystem changed.
- Catching `OperationCanceledException` and reporting success.
- Persisting unbounded provider, OCR, diagnostic, or plugin output.
- Adding a store field without a schema/migration decision.
- Calling `Microsoft.Data.Sqlite` outside the provider project or blocking the
  UI thread with synchronous provider work.
- Assuming `AssemblyLoadContext` is a sandbox.
- Updating a historical document instead of the current authoritative guide.
- Adding an Application-to-Desktop or SDK-to-internal project reference.
