# OmniSorSe current state

**Document type:** Living current-truth reference

**Authority:** Volatile facts about the current source line. Source and tests
remain authoritative for exact behavior; [Release Status](RELEASE_STATUS.md)
owns detailed validation, integration, packaging, tagging, and publication
evidence.

**Scope:** The v2.13 Product Clarity & Workflow implementation candidate.
Update this document when the source line, runtime, schema, protocol, active
authority, or confidence boundary changes. Do not copy its volatile facts into
historical release records.

## Current facts

| Fact | Current truth | Evidence in the repository |
| --- | --- | --- |
| Product identity | The user-facing product is **OmniSorSe**. Existing `OpenSorSe` solution, assembly, namespace, profile, installer, and bundle identifiers are retained where compatibility requires them. | `README.md`; `src/OpenSorSe.Core/Platform/ApplicationPathProvider.cs`; `docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md` |
| Latest stable release | **v2.4.0**. The later source line is a release candidate, not a stable/GA release. | `docs/RELEASE_STATUS.md`; `RELEASE_HISTORY.md` |
| Current source line | **v2.13.0-rc**, Product Clarity & Workflow, built on the published v2.12 relationship/context candidate without changing schema, protocol, or file-mutation authority. | `Directory.Build.props`; `docs/RELEASE_NOTES_v2.13.0.md`; `docs/RELEASE_STATUS.md` |
| Current prerelease packages | The current exact-source-bound **v2.13.0-rc** Windows and macOS package set is published as a GitHub prerelease for real-world/manual validation before GA. Windows artifacts are unsigned; macOS artifacts are publisher-unsigned and unnotarized (toolchain-provided ad-hoc signatures do not identify or authenticate a publisher). The release page, tag, embedded build manifest, checksum file, and SBOM own the immutable source/asset identity. | [GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.13.0-rc); `docs/RELEASE_NOTES_v2.13.0.md`; `docs/RELEASE_STATUS.md` |
| Previous prerelease baseline | The prior exact-source **v2.12.0-rc** package set remains available as historical prerelease evidence from tagged commit `4dd27d62fc4ecbe9916b9789c57d5e8d2336c9ac`; it is not the current source/package line and is not stable/GA. | [v2.12 GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.12.0-rc); `docs/RELEASE_NOTES_v2.12.0.md` |
| Remaining release boundary | Interactive workflow, accessibility, resize/DPI, real-library, normal-user installer, signing, notarization, and GA checks remain manual and must not be inferred from automated host/package smoke. | `.github/workflows/ci.yml`; `.github/workflows/release-packaging.yml`; `docs/MANUAL_TESTING_v2.13.md` |
| Runtime | All solution projects target **.NET 10**. `global.json` selects SDK `10.0.400` with latest-feature roll-forward. | `Directory.Build.props`; `global.json`; project files |
| Durable Search/index schema | `deep-index.db` is **schema 6**. It contains durable indexing, Search projections, normalized Smart Tag authority, relationships, Smart Collections, privacy rules, and maintenance state behind provider-neutral contracts. | `DeepIndexingVersion.SchemaVersion` in `src/OpenSorSe.Application/Indexing/DeepIndexingModels.cs`; `src/OpenSorSe.Indexing.Sqlite/SqliteDeepIndexStore.cs` |
| Explorer boundary | Explorer Protocol is **1.0**. It is local, authenticated, source-scoped, bounded, read-only, and dormant until explicitly requested. | `ExplorerProtocolVersion` in `src/OmniSorSe.ExplorerProtocol/ExplorerProtocolContracts.cs`; `src/OpenSorSe.Application/Explorer/` |
| OmniBrille boundary | OmniBrille is a separately installed, separately owned optional companion. OmniSorSe can explicitly discover and launch it, pass one scoped session through a current-user handoff, and then serve Protocol 1.0. OmniBrille is not implemented in this repository. | `src/OpenSorSe.Application/Explorer/ExplorerCompanionLaunch.cs`; `docs/OMNIBRILLE_COMPANION_HANDOFF_v2.5.md` |
| Logical state backup | The current `.oms-state` writer uses **format 2** and accepts exact format-1 archives. Restore uses stable identities and a pre-restore recovery point; it does not guess by path or filename. Rebuildable index/graph state, the separate Knowledge Graph decision sidecar, and active mutation history are not included. | `src/OpenSorSe.Application/Resilience/StateBackupService.cs`; `tests/OpenSorSe.Indexing.Sqlite.Tests/StateBackupServiceTests.cs` |
| Profile ownership | One process owns a profile for writing. The Desktop acquires a current-user, profile-specific lock before composing profile services and records abnormal termination through a run marker. | `src/OpenSorSe.Core/Platform/ProfileOwnership.cs`; `src/OpenSorSe.Core/Lifecycle/ApplicationRunState.cs`; `src/OpenSorSe.Desktop/App.axaml.cs` |
| File-mutation authority | Reviewed Change Plan execution is the only supported production source-file mutation path. Journal persistence precedes mutation; rollback, restart recovery, and Undo use recorded facts plus current filesystem truth. Review Changes, Operation History Undo, and startup interruption recovery now feed the same post-operation reconciliation path. | `src/OpenSorSe.Executor/ChangePlanExecutionService.cs`; `src/OpenSorSe.Desktop/ViewModels/ChangePlanReviewViewModel.cs`; `src/OpenSorSe.Desktop/ViewModels/UndoHistoryViewModel.cs`; `src/OpenSorSe.Application/ChangePlans/ChangePlanReconciliationService.cs` |
| Known mutation-projection limits | Startup preflights authoritative stores, then performs recovery immediately after index initialization and coalesces index submission to at most one root per retained journal operation (500); recovery and reconciliation are adjacent but not crash-atomic, so a crash in that gap still requires a later reconciliation/scan. Duplicate-recovery Undo cannot recreate a Results row removed during Apply because the journal does not own that logical row ID. `FolderRestructuringService.ApplyAsync` remains a direct executor consumer without shell handoff. Separately, if journal persistence fails after Undo has already reversed the filesystem action, no terminal record reaches Operation History reconciliation even though disk may already be restored. | `src/OpenSorSe.Desktop/App.axaml.cs`; `src/OpenSorSe.Desktop/ViewModels/MainViewModel.cs`; `src/OpenSorSe.Application/Structure/FolderRestructuringService.cs`; focused reconciliation/composition tests |

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
| Saved searches | `JsonSavedDiscoveryViewStore` owns bounded dynamic query rules; retained type/storage names preserve compatibility | Search executes them against the current authorized index | A saved search does not persist file membership |
| Knowledge Graph | `knowledge-decisions.db` owns graph-native user decisions/privacy; `knowledge-graph.db` is rebuildable derived projection | Graph services and `KnowledgeGraphViewModel` expose bounded views and optional Search context | The graph does not own source files, schema-6 relationship authority, or Change Plans |
| Change intent and execution facts | `JsonChangePlanStore` owns reviewed intent; `JsonOperationJournalStore` owns execution/recovery facts | Review UI, history, recovery, and Undo consume those facts; Review Changes, Operation History Undo, and startup recovery forward terminal facts to shared reconciliation | Suggestions, watchers, plugins, recipes, and AI cannot execute |
| Explorer contract/session | `OmniSorSe.ExplorerProtocol` owns DTO/version compatibility; Application owns authorization, sessions, transport, and read projections | OmniBrille consumes only an explicitly granted bounded session | Protocol clients cannot access SQLite directly, request arbitrary paths, or mutate |

The [System Map](Architecture/OpenSorSe_System_Map.md) visualizes these
authorities and links them to source and tests. The
[Architecture Overview](ARCHITECTURE_OVERVIEW.md) contains deeper invariants,
failure behavior, cancellation, performance bounds, and technical debt.

## Implemented product boundary

The current source implements local scanning and extraction, exact duplicate
review, Watched Folders, progressive durable Search, media/content
intelligence, explainable Smart Tags, complete-index faceted discovery and
dynamic saved searches, evidence-backed relationships and Smart Collections,
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

For the published v2.12 lineage, Release Status records forced no-cache restore, zero-warning Debug and Release
builds, and 1,870 passing tests in each configuration for implementation commit
`1cf1910`. Focused Change Plan reconciliation, Desktop recovery/Undo,
documentation/configuration, formatting, analyzers, dependency policy,
vulnerability, Skill, diff, repository-integrity, and native package-smoke
gates also passed. Pull request #36 and exact-main merge commit `3bb3919` then
passed the complete Windows, Ubuntu, macOS ARM, and macOS Intel hosted matrix.
The recorded report-only baseline `ffc29ed` passed that same matrix in
[run 32410287837](https://github.com/nishdel/OmniSorSe/actions/runs/32410287837).
Later exact-main baseline `d682997` passed the four-host matrix in
[run 32490622011](https://github.com/nishdel/OmniSorSe/actions/runs/32490622011),
and its exact-source native package set passed
[run 32492043785](https://github.com/nishdel/OmniSorSe/actions/runs/32492043785).
That package workflow verified Windows install/start/stop/uninstall and data
preservation, native macOS mount/composition smoke, provenance, runtime and
version metadata, contents, checksums, and SBOM generation.
The PR needed two controlled Windows reruns and the report-only baseline needed one
after different unchanged timing-sensitive tests failed; final attempts passed
without code, threshold, or workflow changes. This remains validation-
infrastructure uncertainty rather than first-attempt stability evidence.
Treat those results as evidence for the recorded commits and environments, not
as a guarantee for the current v2.13 candidate. On the v2.13 implementation
worktree, SDK 10.0.400 produced zero-warning Debug and Release builds and
1,878/1,878 passing tests in each configuration with no skips. The focused
Search-relevance test, 16 performance-regression tests, 14 repository-policy
tests, formatting/style/analyzers, AXAML parsing, 18-project vulnerability
audit, and native `win-x64` self-contained package smoke also passed. Pull
request #47 passed the four-host matrix in
[run 33890703737](https://github.com/nishdel/OmniSorSe/actions/runs/33890703737),
and its first exact-main merge commit passed the same complete matrix in
[run 33893063679](https://github.com/nishdel/OmniSorSe/actions/runs/33893063679).
A first exact-source package run
[33895004016](https://github.com/nishdel/OmniSorSe/actions/runs/33895004016)
passed all automated artifact/lifecycle gates but was deliberately not
published after independent inspection found that its embedded installation
guide still named v2.12 downloads. Final v2.13 public assets are rebuilt only
from the post-correction exact `main` commit; the GitHub prerelease owns that
final immutable run, tag, and checksum evidence.
Release Status separately
preserves the original publication and macOS-correction evidence.

### Not verified or still manual

The v2.13 manual addendum and inherited v2.10-v2.12 checklists remain
unchecked. Interactive Scan → Review → Organize, Search resize/scroll,
Duplicates, optional AI, Smart Tags, relationship quality and confirmations,
desktop accessibility, actual OmniBrille integration, removable-source
identity, installer UI/SmartScreen/Restart Manager, real profile upgrade,
reinstall behavior, and broader native Linux/macOS behavior remain outside the
recorded confidence boundary. The initial v2.12 published-main run
exposed three macOS test-portability/fixture failures; the correction retained
the handoff's 128-bit random identity and did not enable production mutation on
macOS. Follow-up pull-request and exact-main hosted runs passed all four hosts.
Automated host and package-smoke evidence is not interactive UX, accessibility,
real-world upgrade, signing, notarization, or stable-release evidence.

Read [Release Status](RELEASE_STATUS.md), the
[v2.13 release notes](RELEASE_NOTES_v2.13.0.md), and the
[v2.13 manual addendum](MANUAL_TESTING_v2.13.md) before making a current
readiness or release claim. The [v2.12 implementation
record](TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) remains authority for the
inherited relationship/context design.

## Where current truth belongs

- Update this document for current version/runtime/schema/protocol/authority
  facts.
- Update the relevant subsystem architecture and diagram for ownership or flow
  changes.
- Record durable reasoning in an ADR rather than repeating it here.
- Record exact validation in a validation or release-status record.
- Preserve versioned implementation, manual, release, and historical documents
  as evidence of their own time; do not rewrite them to look current.
