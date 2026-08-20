# OmniSorSe current state

**Document type:** Living current-truth reference

**Authority:** Volatile facts about the current source line. Source and tests
remain authoritative for exact behavior; [Release Status](RELEASE_STATUS.md)
owns detailed validation, integration, packaging, tagging, and publication
evidence.

**Scope:** The v2.12 Trusted Relationships & Context implementation candidate.
Update this document when the source line, runtime, schema, protocol, active
authority, or confidence boundary changes. Do not copy its volatile facts into
historical release records.

## Current facts

| Fact | Current truth | Evidence in the repository |
| --- | --- | --- |
| Product identity | The user-facing product is **OmniSorSe**. Existing `OpenSorSe` solution, assembly, namespace, profile, installer, and bundle identifiers are retained where compatibility requires them. | `README.md`; `src/OpenSorSe.Core/Platform/ApplicationPathProvider.cs`; `docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md` |
| Latest published release | **v2.4.0**. Later version lines in this repository are implementation candidates, not published releases. | `docs/RELEASE_STATUS.md`; `RELEASE_HISTORY.md` |
| Current source line | **v2.12.0-rc**, Trusted Relationships & Context, built on the linear v2.5-v2.11 candidate stack. The validated source is integrated into and published from GitHub `main`; exact v2.5-v2.12 branch refs are also available remotely. No v2.5-v2.12 release tag, package, or GitHub Release exists. | `Directory.Build.props`; `docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md`; `docs/RELEASE_STATUS.md` |
| Runtime | All solution projects target **.NET 10**. `global.json` selects SDK `10.0.400` with latest-feature roll-forward. | `Directory.Build.props`; `global.json`; project files |
| Durable Search/index schema | `deep-index.db` is **schema 6**. It contains durable indexing, Search projections, normalized Smart Tag authority, relationships, Smart Collections, privacy rules, and maintenance state behind provider-neutral contracts. | `DeepIndexingVersion.SchemaVersion` in `src/OpenSorSe.Application/Indexing/DeepIndexingModels.cs`; `src/OpenSorSe.Indexing.Sqlite/SqliteDeepIndexStore.cs` |
| Explorer boundary | Explorer Protocol is **1.0**. It is local, authenticated, source-scoped, bounded, read-only, and dormant until explicitly requested. | `ExplorerProtocolVersion` in `src/OmniSorSe.ExplorerProtocol/ExplorerProtocolContracts.cs`; `src/OpenSorSe.Application/Explorer/` |
| OmniBrille boundary | OmniBrille is a separately installed, separately owned optional companion. OmniSorSe can explicitly discover and launch it, pass one scoped session through a current-user handoff, and then serve Protocol 1.0. OmniBrille is not implemented in this repository. | `src/OpenSorSe.Application/Explorer/ExplorerCompanionLaunch.cs`; `docs/OMNIBRILLE_COMPANION_HANDOFF_v2.5.md` |
| Logical state backup | The current `.oms-state` writer uses **format 2** and accepts exact format-1 archives. Restore uses stable identities and a pre-restore recovery point; it does not guess by path or filename. Rebuildable index/graph state, the separate Knowledge Graph decision sidecar, and active mutation history are not included. | `src/OpenSorSe.Application/Resilience/StateBackupService.cs`; `tests/OpenSorSe.Indexing.Sqlite.Tests/StateBackupServiceTests.cs` |
| Profile ownership | One process owns a profile for writing. The Desktop acquires a current-user, profile-specific lock before composing profile services and records abnormal termination through a run marker. | `src/OpenSorSe.Core/Platform/ProfileOwnership.cs`; `src/OpenSorSe.Core/Lifecycle/ApplicationRunState.cs`; `src/OpenSorSe.Desktop/App.axaml.cs` |
| File-mutation authority | Reviewed Change Plan execution is the only supported production source-file mutation path. Journal persistence precedes mutation; rollback, restart recovery, and Undo use recorded facts plus current filesystem truth. Review Changes publishes terminal Apply/Undo results for post-operation reconciliation. | `src/OpenSorSe.Executor/ChangePlanExecutionService.cs`; `src/OpenSorSe.Desktop/ViewModels/ChangePlanReviewViewModel.cs`; `src/OpenSorSe.Application/ChangePlans/ChangePlanReconciliationService.cs` |
| Known projection gap | Operation History Undo calls the shared safe executor but does not publish its result to `MainViewModel` reconciliation; startup interruption recovery also discards the returned journal records. Results and the targeted deep index can therefore remain stale until later scan/index reconciliation on those two entry paths. | `src/OpenSorSe.Desktop/ViewModels/UndoHistoryViewModel.cs`; `src/OpenSorSe.Desktop/App.axaml.cs`; `src/OpenSorSe.Desktop/ViewModels/MainViewModel.cs` |

The platform path authority is `IApplicationPathProvider`. Windows and macOS
retain the `OpenSorSe` compatibility name; Linux uses the retained `opensorse`
name below XDG configuration, data, state, and cache roots. Source files remain
outside application-owned storage.

## Current authority map

| Concept | Owns or mutates | Reads, derives, or presents | Does not own |
| --- | --- | --- | --- |
| Source filesystem state | The filesystem and user; supported mutation only through `ChangePlanExecutionService` and `IFileSystemGateway` | Scanner, indexing, watchers, Search, relationships, and reconciliation | AI, plugins, rules, recipes, Search, Knowledge Graph, and OmniBrille do not gain mutation authority |
| Indexed file state | `IDeepIndexStore` contracts with the `SqliteDeepIndexStore` provider | Background indexing, Search, Smart Tags, relationships, health, privacy/Forget, Explorer | Views/ViewModels and the protocol contract do not own SQL or migrations |
| Smart Tags | Schema-6 taxonomy, assignment, decision, and status records; explicit User Tags and accept/reject decisions are durable authority | Deterministic classifier proposes from retained evidence; Search and facets present canonical values | Classifier and optional AI do not override user decisions or write source metadata |
| Relationships | Schema-6 retained evidence, typed edges, explicit pair authority, Smart Collection authority, and privacy state | `RelationshipService`, Search, Related Files, Explorer, and the optional graph consume bounded projections | Knowledge Graph is not relationship or grouping authority |
| Saved Views | `JsonSavedDiscoveryViewStore` owns bounded dynamic query rules | Search executes them against the current authorized index | A Saved View does not persist file membership |
| Knowledge Graph | `knowledge-decisions.db` owns graph-native user decisions/privacy; `knowledge-graph.db` is rebuildable derived projection | Graph services and `KnowledgeGraphViewModel` expose bounded views and optional Search context | The graph does not own source files, schema-6 relationship authority, or Change Plans |
| Change intent and execution facts | `JsonChangePlanStore` owns reviewed intent; `JsonOperationJournalStore` owns execution/recovery facts | Review UI, history, recovery, and Undo consume those facts; reconciliation currently receives terminal facts only from Review Changes | Suggestions, watchers, plugins, recipes, and AI cannot execute |
| Explorer contract/session | `OmniSorSe.ExplorerProtocol` owns DTO/version compatibility; Application owns authorization, sessions, transport, and read projections | OmniBrille consumes only an explicitly granted bounded session | Protocol clients cannot access SQLite directly, request arbitrary paths, or mutate |

The [System Map](Architecture/OpenSorSe_System_Map.md) visualizes these
authorities and links them to source and tests. The
[Architecture Overview](ARCHITECTURE_OVERVIEW.md) contains deeper invariants,
failure behavior, cancellation, performance bounds, and technical debt.

## Implemented product boundary

The current source implements local scanning and extraction, exact duplicate
review, Watched Folders, progressive durable Search, media/content
intelligence, explainable Smart Tags, complete-index faceted discovery and
dynamic Saved Views, evidence-backed relationships and Smart Collections,
optional bounded Knowledge Graph projection, reviewed recipe-based
organization, Change Plans with journal-aware recovery/Undo and Review Changes
reconciliation, logical state backup/restore, optional Ollama-compatible
assistance, plugins, and the
read-only Explorer/OmniBrille integration.

It does **not** implement cloud synchronization, collaboration, OmniSorSe
Server, a conversational assistant, autonomous organization, permanent
deletion, a plugin security sandbox, unrestricted media/AI processing, or an
OmniBrille renderer/client in this repository.

Deterministic operation remains useful without AI. AI output is optional,
bounded, validated as untrusted, provenance-bearing, and cannot directly
change a source file or override durable user-authored relationship/tag state.

## Validation and confidence

### Verified in recorded automated evidence

Release Status records forced no-cache restore, zero-warning Debug and Release
builds, and 1,861 passing tests in each configuration for correction commit
`d81f154`. Focused affected and neighboring Change Plan/platform regressions,
documentation/configuration, formatting, analyzers, dependency policy,
vulnerability, Skill, diff, and repository-integrity gates also passed. Pull
request #35 and exact-main merge commit `542e14a` then passed the complete
Windows, Ubuntu, macOS ARM, and macOS Intel hosted matrix, including Debug and
Release builds/tests and native package smoke. Treat those results as evidence
for the recorded commits/environments, not as a guarantee for an unvalidated
later working tree. Release Status separately records original publication
fresh-clone evidence and the exact correction and merge runs.

### Not verified or still manual

The v2.12 manual addendum remains unchecked. Interactive relationship quality,
desktop accessibility, actual OmniBrille integration, removable-source
identity, installer lifecycle, and broader native Linux/macOS behavior remain
outside the recorded confidence boundary. The initial published-main run
exposed three macOS test-portability/fixture failures; the correction retained
the handoff's 128-bit random identity and did not enable production mutation on
macOS. Follow-up pull-request and exact-main hosted runs passed all four hosts.
Automated host and package-smoke evidence is not interactive UX, accessibility,
installer-lifecycle, signing, notarization, or release evidence.

Read [Release Status](RELEASE_STATUS.md), the
[v2.12 implementation record](TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md), and the
[v2.12 manual addendum](MANUAL_TESTING_v2.12.md) before making a readiness or
release claim.

## Where current truth belongs

- Update this document for current version/runtime/schema/protocol/authority
  facts.
- Update the relevant subsystem architecture and diagram for ownership or flow
  changes.
- Record durable reasoning in an ADR rather than repeating it here.
- Record exact validation in a validation or release-status record.
- Preserve versioned implementation, manual, release, and historical documents
  as evidence of their own time; do not rewrite them to look current.
