# ADR-005: Separate Indexed, User-Authored, and Derived Authority

| Field | Value |
| --- | --- |
| Status | Accepted in the current architecture; reconstructed from source and Git history |
| Effective history | Durable indexing introduced in v1.7; relationship authority in v1.9; Knowledge Graph projection in v2.0; Smart Tag authority in schema 6 |
| Reconstruction date | 2026-08-18 |
| Decision owners | OmniSorSe maintainers |

## Context

OmniSorSe retains filesystem observations, generated text and media evidence, Search representations, Smart Tags, Related Files, collections, Knowledge Graph projections, privacy exclusions, and explicit user decisions. These values have different provenance and recovery requirements. Treating every retained row as equally authoritative would allow derived data to override user choices, make privacy deletion incomplete, or make a rebuild destroy durable decisions.

The current model was reconstructed from the durable-index, relationships, Smart Tags, and Knowledge Graph implementation and their Git history. This ADR names the authority boundaries already enforced by the code; it does not manufacture historical rationale that the surviving evidence does not support.

## Decision

OmniSorSe separates authority by meaning and recoverability:

| State | Authority and rule |
| --- | --- |
| Source files | The filesystem owns current file/path truth. Indexes are observations and projections. |
| Current indexed library | Schema-6 `deep-index.db` owns registered sources, stable indexed file identities, durable work state, Search documents, retained generated evidence, privacy policy, Smart Tag state, relationships, and Smart Collections. |
| User Smart Tag authority | User-created tags and accept/reject decisions are stored separately from regenerated classifier output and survive clearing generated classifications. |
| Relationship/collection authority | Explicit pair decisions, manual links, collection metadata/membership overrides, exclusions, and tombstones remain distinct from replaceable automatic analysis. |
| Knowledge Graph source | `deep-index.db` is adapted through `IGraphProjectionSource`; the graph does not become the owner of source observations or legacy relationship decisions. |
| Derived graph | `knowledge-graph.db` is an isolated rebuildable projection. Its contents may be quarantined and replaced without resetting user decisions or rebuilding the deep index. |
| Graph-native decisions | `knowledge-decisions.db` is the non-rebuildable append-only authority for graph-native choices, consent, resource controls, privacy sequence, and recovery metadata. |

Graph reads and mutations validate current source manifest/revision, privacy sequence, legacy decision manifest, graph decision checkpoint, and applied coverage. Stale or invalid authority fails closed. Corrections to relationship-owned concepts route through `RelationshipGraphAuthorityBridge` to the relationship service and are not duplicated into the graph-native decision ledger.

Generated and optional AI-derived evidence retains provenance. It may support Search, explanations, or review suggestions, but does not silently replace explicit user authority.

Path-keyed `content-index.json`, `semantic-index.json`, and media thumbnails are compatibility/rebuildable caches. They do not own stable indexed identity or privacy authority. Privacy deletion coordinates schema-6 deletion with cache cleanup because those caches cannot prove complete stable-ID ownership.

## Consequences

- Reindexing and graph rebuilding can replace derived output without erasing user-authored decisions.
- Backup/restore exports logical user authority by stable identity and excludes the derived index.
- Graph enablement is explicit; sidecar stores are provisioned only after consent.
- Base Search and indexing remain usable when the optional graph is disabled, stale, starting, or unavailable.
- Relationship changes signal graph reconciliation, while periodic reconciliation covers missed notifications.
- Current Search still consumes both progressive SQLite documents and the legacy semantic JSON cache. Progressive documents replace most same-path fields, but legacy tags are unioned and legacy vectors can be fallback data. This is a recorded compatibility limitation and must not be mistaken for a second user-authority store.

## Alternatives considered

The surviving source and history do not establish a reliable complete alternatives record. No rejected alternatives are reconstructed here.

## Evidence

- `src/OpenSorSe.Application/Indexing/DeepIndexingModels.cs`
- `src/OpenSorSe.Application/Indexing/IndexPrivacyDeletionCoordinator.cs`
- `src/OpenSorSe.Application/SmartTags/SmartTagPersistence.cs`
- `src/OpenSorSe.Application/Relationships/RelationshipContracts.cs`
- `src/OpenSorSe.Application/KnowledgeGraph/GraphContracts.cs`
- `src/OpenSorSe.Application/KnowledgeGraph/GraphApplicationServices.cs`
- `src/OpenSorSe.Application/KnowledgeGraph/RelationshipGraphAuthorityBridge.cs`
- `src/OpenSorSe.Application/Semantic/SemanticSearchService.cs`
- `src/OpenSorSe.Indexing.Sqlite/KnowledgeGraph/SqliteGraphStorageLifecycle.cs`
- `tests/OpenSorSe.Indexing.Sqlite.Tests/StateBackupServiceTests.cs`
- `tests/OpenSorSe.Application.Tests/KnowledgeGraph/GraphReleaseGateMatrixTests.cs`
- [Knowledge Graph design](../06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md)
- [Relationship context](../06_Search/10_v1.9_Relationships_Context.md)
