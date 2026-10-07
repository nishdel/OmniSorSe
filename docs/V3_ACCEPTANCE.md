# v3.0.0-rc.1 acceptance and verification

Baseline: remote `main` at `727ce2d09ce9870f6e6ce9e4c3baa467c7e4d5de`.
The 38-item agreed next-major-release specification is the acceptance baseline.
This candidate is in implementation and is **not published**. Paths below identify
the owning implementation; they do not imply that its verification has passed.
The validation report will record executed checks separately from human acceptance.

| Item | Requirement | Implementation / verification owner | State |
| --- | --- | --- | --- |
| 1 | AI offered with folder indexing | FolderSelectionViewModel, ScanRequest, MainViewModel; folder-choice tests | In verification |
| 2 | Fast coverage then background enrichment | BackgroundIndexingService, BaseFirst queue; progressive indexing tests | In verification |
| 3 | Incremental application-owned enrichment | SqliteDeepIndexStore per-file completion and FTS updates; incremental tests | In verification |
| 4 | Enable AI on retained library content | SetSourceAiEnrichmentAsync; retained-content queue tests | In verification |
| 5 | Newly discovered files inherit source AI policy | IndexingSource.AiEnrichmentEnabled and watcher reconciliation; source-policy tests | In verification |
| 6 | Automatic validated enrichment | OllamaIndexingEnrichmentProvider, IndexingEnrichmentValidator, stage processor; provider/FTS tests | In verification |
| 7 | Distinct provenance and user authority | IndexedContentIntelligence.Origin, Smart Tag decisions; authority regression tests | In verification |
| 8 | Structural validation and bounds | IndexingEnrichmentValidator; hostile JSON/path/duplicate/length tests | In verification |
| 9 | AI never executes filesystem actions | ReviewedOrganizationService and existing Change Plan executor; safety tests | In verification |
| 10 | Useful document interpretation | Type/category/tags/topics/entities/summary with provenance; real-Ollama smoke | In verification |
| 11 | Deterministic extraction first | ContentIndexingService, Open XML/PdfPig/PDF rendering/OCR/scanner; existing extraction tests | In verification |
| 12 | Dedicated Organize page | OrganizeView, shell navigation; navigation tests | In verification |
| 13 | Current/recommended trees | ReviewedOrganizationViewModel; tree projection tests and manual UI | In verification |
| 14 | Reasons for each move | ReviewedOrganizationService evidence rows; proposal tests | In verification |
| 15 | Three organization strategies | Preserve/Improve/Fresh proposal strategy; strategy regressions | In verification |
| 16 | Edit/reject files and folder hierarchy | Proposal-only editor; invalid destination and stale-preview tests | In verification |
| 17 | Learn explicit organization preferences | Bounded source-scoped decision history; persistence/reload tests | In verification |
| 18 | Review, validation, approval, history/Undo | Existing executor and preflight; regression and manual round-trip | In verification |
| 19 | AI concepts automatically searchable | Validated retained keywords/content intelligence and per-file FTS/semantic projection; concept-search tests | In verification |
| 20 | AI evidence feeds relationships | Existing RelationshipService and graph projection; AI origin/decision tests | In verification |
| 21 | Simple Related Files and Collections | Existing Related Files UI and relationship services; integration tests | In verification |
| 22 | Raw graph stays advanced | Existing advanced Graph diagnostics destination; navigation tests | In verification |
| 23 | Provider-neutral OCR | Existing IOcrEngine and OcrService; OCR contract tests | Existing, verify |
| 24 | Tesseract remains supported | TesseractCliOcrEngine; capability tests and optional native smoke | Existing, verify |
| 25 | OCR separate from interpretation | Separate extraction and enrichment stages; source/provenance tests | In verification |
| 26 | Richer media understanding | Existing media/OCR adapters feed shared evidence; additional vision/audio/video providers remain future work | Explicitly future-facing |
| 27 | Never write enrichment to source files | Application-owned SQLite/cache writes only; source byte/time non-modification regression | In verification |
| 28 | Configurable data/index/cache location | ApplicationStorageLocation, Settings; verified-copy migration/restart tests | In verification |
| 29 | Storage usage UI | ApplicationStorageService, StorageManagementViewModel; inventory tests | In verification |
| 30 | Configurable bounds | Existing index/graph quotas plus cache quota; bounded write/maintenance tests | In verification |
| 31 | Rebuildable data first, preserve decisions | Cache reclamation and tombstone retention fences; durable-authority regressions | In verification |
| 32 | Clear product terminology | Organize, Search, Related Files; source/UI review | In verification |
| 33 | Explain folder choices | Standard versus AI-enriched indexing panel; folder request tests | In verification |
| 34 | Progress, queue, pause/cancel, per-file state | Existing Search queue controls plus AI state indicators; VM/lifecycle tests | In verification |
| 35 | Beginner entry document | [How OmniSorSe Works](HOW_OMNISORSE_WORKS.md) | In verification |
| 36 | Explain what/code/why per stage | How OmniSorSe Works stage table | In verification |
| 37 | Separate third-party and owned code | How OmniSorSe Works technology table | In verification |
| 38 | Real workflow examples | How OmniSorSe Works examples; link to manual scenarios | In verification |

Alternative OCR providers are extension examples, not implemented provider claims.
All other unfinished current-milestone items remain required release work.

## Required vector-search addition for the first candidate

The original 38-item baseline remains required. The continuation additionally
requires learned-vector hybrid Search before v3.0.0-rc.1 publication.

| Requirement | Implementation / verification owner | State |
| --- | --- | --- |
| Separate local embedding model and provider boundary | `IModelEmbeddingProvider`, `OllamaEmbeddingProvider`; bounded transport/model tests | In verification |
| Derived stable-ID vectors with chunk provenance | `IVectorSearchStore`, schema-8 SQLite tables, `VectorTextChunker` | In verification |
| Existing-library, incremental and recoverable work | `VectorIndexCoordinator`; completion, restart, cancellation and mutation-fence tests | In verification |
| Model digest, deletion, moves, privacy and enrichment invalidation | SQLite triggers, query/commit freshness and digest isolation tests | In verification |
| Actual hybrid Search and vector-assisted Related Files | `SemanticSearchService`, `ReciprocalRankFusion`, `SemanticRelatedFilesService`; [real-model benchmark](../eng/benchmarks/VectorSearch/README.md), independent-vector and provider-bound hydration regressions | In verification |
| Status, controls, storage, relocation and cleanup | Search/Settings views, coordinator, `ApplicationStorageService` | In verification |
| Real v3 screenshots and diagrams | User guide, Hybrid Search guide and screenshot gallery | In verification |

## Development assistance continuation

The local development checkout was not treated as an installed active controller.
The existing Python environment imports an editable LocalAgentBridge checkout, so
this run copied and SHA-256 inventoried its module into an ignored, frozen runtime.
An independent stdio MCP client uses that runtime and the existing personal config
without editing it. The actual tool catalog contains the seven baseline tools.
Inputs use bounded inline excerpts because the configured source root does not
include OmniSorSe. Existing routing selected `gpt-oss:20b` at the Large cap.

Requested substantive work includes parser/queue implementation, editable
organization helpers/tree/preferences, storage retention SQL/migration and tests,
and desktop enrichment commands. Preserve actual request IDs and outcomes in the
validation report. Timeouts, generic answers and explicit router handoffs are
fallbacks, not successful local implementation contributions. Codex owns safety
decisions, application of changes, integration and independent verification.
