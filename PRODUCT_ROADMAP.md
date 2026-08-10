# OpenSorSe product roadmap

**Document type:** Living roadmap

**Authority:** Release sequence, implementation/integration status, planned
concepts, research, and unassigned ideas

**Last repository review:** 2026-08-11

This roadmap records what the repository proves and separates it from future
intent. A planned version number is a planning label, not a delivery promise.
Only source, history, validation evidence, branches, tags, and packages already
present in the repository are described as complete.

For concise dates, test totals, and links to historical evidence, see
[Release History](RELEASE_HISTORY.md). For exact current readiness, see
[Release Status](docs/RELEASE_STATUS.md).

## Status vocabulary

| State | Meaning |
| --- | --- |
| Completed | The implementation lineage is integrated into `main`. Manual or package caveats remain recorded in the release evidence. |
| In progress | Source implementation exists, but integration or required release gates remain open. |
| Design in progress | An isolated design branch and review package exist, but no runtime implementation is claimed. |
| Planned concept | A named direction with no implementation branch or commitment. Scope and order may change. |
| Research | A question that needs evidence before it can become versioned work. |
| Ideas backlog | A promising unassigned concept with no version or schedule. |

## Completed

`main` currently contains the implementation lineage through v2.0.

| Version | Branch | Release title | Merged status |
| --- | --- | --- | --- |
| v0.1 | `coding/v0.1` | Read-only Processing Foundation | Foundational implementation is in `main`; the remote branch tip has one later unmerged README-only commit. |
| v0.2 | `coding/v0.2` | Read-only Results Exploration | Merged to `main`. |
| v0.3 | `v0.3` | Local Suggestions and Ranked Exploration | Merged to `main`. |
| v0.4 | `v0.9` (batched v0.4-v0.9 line) | Opt-in Local Catalog | Merged to `main`. No dedicated remote v0.4 branch exists. |
| v0.5 | `v0.9` (batched v0.4-v0.9 line) | Catalog Search and Maintenance | Merged to `main`. No dedicated remote v0.5 branch exists. |
| v0.6 | `v0.9` (batched v0.4-v0.9 line) | User-Managed Result Tags | Merged to `main`. No dedicated remote v0.6 branch exists. |
| v0.7 | `v0.9` (batched v0.4-v0.9 line) | Saved Catalog Searches | Merged to `main`. No dedicated remote v0.7 branch exists. |
| v0.8 | `v0.9` (batched v0.4-v0.9 line) | Snapshot Identity and Scope | Merged to `main`. No dedicated remote v0.8 branch exists. |
| v0.9 | `v0.9` | Historical Snapshot Comparison | Merged to `main`. |
| v0.9.1 | `v0.9.1` | Optional AI and Feature Controls | Merged to `main`. |
| v1.0 | `v1.0` | Integrated Local Understanding and Structure History | Merged to `main`; the repository has tag `v1.0.0` and a frozen Windows package snapshot. |
| v1.1 | `v1.1` | Safe File Operations and Robustness | Merged to `main`. |
| v1.2 | `v1.2-watched-folders` | Watched Folders and Incremental Scanning | Merged to `main`. |
| v1.3 | `v1.3-workflow-profiles` | Workflow Profiles and Recipe Library | Merged to `main`. |
| v1.4 | `v1.4-plugin-foundation` | Plugin Foundation and Extension SDK | Merged to `main`. |
| v1.5 | `v1.5-cross-platform-foundation` | Cross-Platform Foundation and Linux Preview | Merged to `main`. |
| v1.6 | `v1.6-reliability-performance` | Reliability, Performance and Production Hardening | Merged to `main`. |
| v1.7 | `v1.7-deep-indexing-foundation` | Deep Indexing Foundation | Merged to `main` through the v2.0.0 integration; interactive manual validation is not claimed. |
| v1.8 | `v1.8-search-intelligence-privacy` | Search Intelligence, Quality and Privacy | Merged to `main` through the v2.0.0 integration; interactive manual validation is not claimed. |
| v1.9 | `v1.9-relationships-context` | Relationships, Context & Smart Collections | Merged to `main` through the v2.0.0 integration; interactive manual validation is not claimed. |
| v2.0 | `v2.0-knowledge-graph` | Knowledge Graph | Merged to `main` by an explicit history-preserving release merge after exact-tip Windows, Ubuntu, and macOS validation. Broad manual/community testing begins with publication. |

### v0.1 — Read-only Processing Foundation

OpenSorSe established the solution, desktop shell, read-only scanning pipeline,
metadata, SHA-256 hashing, deterministic classification, duplicate detection,
rules, lexical planning, conflict resolution, orchestration, diagnostics, and
the first automated coverage.

**Major capabilities:** folder discovery; metadata and hashing; deterministic
rules/proposals; Dashboard, Scan, Settings, Diagnostics, and review
foundations.

**Dependencies:** .NET/C#/Avalonia foundations; no prior OpenSorSe release.

### v0.2 — Read-only Results Exploration

The processing foundation became usable through immutable result snapshots and
a bounded Results Explorer while preserving the no-mutation boundary.

**Major capabilities:** filtering, deterministic sorting, paging, details,
exact-duplicate group review, and group-to-results navigation.

**Dependencies:** v0.1 scan and duplicate results.

### v0.3 — Local Suggestions and Ranked Exploration

OpenSorSe added optional local assistance behind a provider-neutral contract
and improved deterministic retrieval without giving AI execution authority.

**Major capabilities:** optional Ollama-compatible transport; bounded validated
rename/folder proposals; local decision history; in-session tags; deterministic
ranked results with explanations.

**Dependencies:** v0.2 results; an externally managed Ollama-compatible service
only for optional AI flows.

### v0.4 — Opt-in Local Catalog

Completed display-safe snapshots could be retained in an explicit bounded
application-owned catalog without turning saved paths into live filesystem
truth.

**Major capabilities:** versioned atomic JSON catalog; opt-in retention;
historical snapshot reopening; accepted-tag restoration.

**Dependencies:** v0.3 result snapshots and decisions.

### v0.5 — Catalog Search and Maintenance

Users could search across stored catalog metadata and deliberately remove
OpenSorSe-owned catalog data.

**Major capabilities:** deterministic catalog-wide metadata/tag Search; ranked
hits and explanations; selected removal; two-step clear.

**Dependencies:** v0.4 catalog storage.

### v0.6 — User-Managed Result Tags

Tags became explicit user-owned metadata rather than an AI-only output, with
bounded normalization and immediate Search integration.

**Major capabilities:** add/remove tags; protected deterministic extension
tags; Search refresh; persistence for catalog-backed snapshots.

**Dependencies:** v0.5 results and catalog Search.

### v0.7 — Saved Catalog Searches

OpenSorSe retained bounded query definitions separately from catalog data and
reran them against current stored state instead of persisting stale hits.

**Major capabilities:** named query presets; atomic independent storage;
remove/reset; malformed-data recovery.

**Dependencies:** v0.5 catalog Search and v0.6 tags.

### v0.8 — Snapshot Identity and Scope

Catalog snapshots gained stable identity, optional names, and captured source
scope while remaining historical records rather than live verification.

**Major capabilities:** catalog schema 2; schema-1 read compatibility; bounded
names/source roots; explicit legacy/unknown scope.

**Dependencies:** v0.4 catalog and v0.7 saved-query compatibility.

### v0.9 — Historical Snapshot Comparison

Two stored snapshots could be compared deterministically without rescanning
their paths or inferring uncertain renames.

**Major capabilities:** added/removed/modified/unchanged classifications;
metadata/tag comparison; scope warnings; cancellation and bounds; historical
opening.

**Dependencies:** v0.8 snapshot identity and stored metadata.

### v0.9.1 — Optional AI and Feature Controls

The interface and provider boundary were hardened so AI and advanced tools were
separately visible, default-off, bounded, recoverable, and consistently gated.

**Major capabilities:** centralized feature requirements; strict structured
output validation; provider readiness/errors; bounded diagnostics; contextual
Help; responsive Duplicate View; corrective reliability pass.

**Dependencies:** v0.9 workflows; optional external Ollama-compatible provider.

### v1.0 — Integrated Local Understanding and Structure History

The first tagged and packaged milestone combined local text/metadata
understanding, OCR, provenance-aware tags, deterministic semantic retrieval,
structure previews/history, and a coherent desktop experience.

**Major capabilities:** bounded PDF/Open XML/image extraction; optional
Tesseract OCR; local Meaning Search Beta; structured optional AI; Advanced
Diagnostics; structure proposals/history/repeat protection; Windows x64
portable package.

**Dependencies:** v0.9.1; optional external Tesseract and Ollama-compatible
provider; PdfPig/PDFtoImage/PDFium for built-in PDF handling.

### v1.1 — Safe File Operations and Robustness

OpenSorSe introduced the durable review and recovery boundary that all current
supported source-file changes must use.

**Major capabilities:** persisted Change Plans; action-level review/editing;
immediate preflight; non-overwriting rename/move/create-directory; Operation
Journal; verification; rollback; restart recovery; conflict-aware Undo.

**Dependencies:** v1.0 proposals and structure workflow.

### v1.2 — Watched Folders and Incremental Scanning

Selected roots could be monitored while retaining reconciliation as the source
of truth and keeping all organization work behind v1.1 review.

**Major capabilities:** persistent watched roots; bounded debounced hint queue;
stability checks; incremental catalogues; startup/offline/overflow
reconciliation; ignore policy; optional per-item AI retry; grouped activity.

**Dependencies:** v1.1 Change Plans/journal and existing scan/content services.

### v1.3 — Workflow Profiles and Recipe Library

OpenSorSe made reusable processing policy and deterministic organization
templates explicit, versioned, inspectable, and portable.

**Major capabilities:** built-in/user profiles and recipes; constrained
templates; previews; immutable resolution snapshots; watched/manual
assignments; import/export/recovery; Change Plan provenance.

**Dependencies:** v1.2 watched configuration and v1.1 execution boundary.

### v1.4 — Plugin Foundation and Extension SDK

A bounded in-process extension model was added without exposing credentials,
approval, or direct user-file mutation to the supported SDK.

**Major capabilities:** standalone SDK; eight extension points; strict
manifests; dependencies/integrity; explicit grants; local package lifecycle;
timeouts/cancellation/quarantine; workflow and Change Plan provenance.

**Dependencies:** v1.3 workflows and v1.1 safety boundary; trusted local plugin
packages for optional external extensions.

### v1.5 — Cross-Platform Foundation and Linux Preview

Platform assumptions moved behind focused contracts, preserving Windows
behavior and adding a conservative Linux source-build/preview boundary.

**Major capabilities:** path, identity, application-location, filesystem,
external-tool, capability, and desktop adapters; XDG paths; platform-aware
mutation checks; plugin runtime identifiers; Windows/Ubuntu CI foundation.

**Dependencies:** v1.4 plugin/workflow behavior and v1.1 mutation invariants.

### v1.6 — Reliability, Performance and Production Hardening

The existing product was hardened rather than expanded: durable stores,
resource use, cancellation, lifecycle behavior, accessibility, and
cross-platform validation were strengthened.

**Major capabilities:** shared atomic persistence; cross-instance transaction
coordination; lower-allocation duplicate/results work; bounded sessions;
idempotent watchers; isolated observers; host-correct paths; accessibility
metadata; Windows/Ubuntu/macOS CI.

**Dependencies:** the full v1.5 feature set; no new product service dependency.

## Integrated v1.7-v2.0 milestones

These versions are now ancestors of `main` through the v2.0.0 release merge.
Their exact validation state is stated per row; integration does not imply that
the still-unchecked interactive or community scenarios were completed.

| Version | Branch | Release title | Merged status |
| --- | --- | --- | --- |
| v1.7 | `v1.7-deep-indexing-foundation` | Deep Indexing Foundation | Integrated into `main` through v2.0.0; source and automated validation complete, interactive manual validation not claimed. |
| v1.8 | `v1.8-search-intelligence-privacy` | Search Intelligence, Quality and Privacy | Integrated into `main` through v2.0.0; source and automated validation complete, interactive manual validation not claimed. |
| v1.9 | `v1.9-relationships-context` | Relationships, Context & Smart Collections | Integrated into `main` through v2.0.0; source and automated validation complete, interactive manual validation not claimed. |
| v2.0 | `v2.0-knowledge-graph` | Knowledge Graph | Integrated into `main` after complete local validation and exact-tip Windows, Ubuntu, and macOS CI. Interactive/community testing begins with publication. |

### v1.7 — Deep Indexing Foundation

v1.7 adds a provider-neutral durable background-indexing pipeline and an
embedded SQLite provider while preserving existing JSON Search compatibility
and every earlier mutation boundary.

**Major capabilities:** Basic/Standard/Deep policies; durable
source/run/job/stage state; restart recovery; incremental invalidation;
duplicate-content sharing; progressive Search coverage; storage quotas and
maintenance; indexing controls/diagnostics.

**Dependencies:** v1.6 reliability/platform baseline; embedded SQLite provider
and native runtime library. No database server is required.

**Open evidence:** the unchecked v1.7 interactive checklist remains a manual
evidence tracker; it is not silently completed by v2.0 integration.

### v1.8 — Search Intelligence, Quality and Privacy

v1.8 builds on v1.7 with one deterministic hybrid ranker, visible local query
interpretation, evidence-backed result presentation, and index-only privacy and
repair.

**Major capabilities:** exact/literal-first hybrid ranking; visible removable
filters; bounded snippets and **Why this result?** explanations; detailed
coverage; indexed-data inspection/forgetting; durable privacy rules; selective
repair; synthetic relevance and performance regression gates.

**Dependencies:** exact v1.7 tip and its provider-neutral/SQLite indexing
foundation. Ollama remains optional and is not required for ordinary Search.

**Open evidence:** the unchecked v1.8 manual checklist remains a manual
evidence tracker. No standalone v1.8 package or tag is claimed.

### v1.9 — Relationships, Context & Smart Collections

v1.9 builds directly on v1.8 with provider-neutral, evidence-backed
relationships, virtual Smart Collections, bounded context/timelines, persistent
user corrections, privacy/repair operations, and optional relationship-aware
Search expansion.

**Major capabilities:** deterministic versioned relationship evidence and
confidence levels; incremental SQLite schema-3 projection; Related Files and
collection inspectors; manual link/unlink and collection control; index-only
forget/rebuild; graph bounds and corruption repair; accessible Collections UI;
exact-first contextual Search.

**Dependencies:** exact validated v1.8 branch tip and its durable index/Search
contracts. No database server, new AI provider, or online service is required.

**Open evidence:** the fully unchecked v1.9 interactive manual checklist
remains a manual evidence tracker. No standalone v1.9 package or tag is
claimed.

### v2.0 — Knowledge Graph

v2.0 implements an optional provider-neutral projection over v1.9 indexed
data. It preserves `deep-index.db` schema 3 and adds isolated schema-1
`knowledge-graph.db` and `knowledge-decisions.db` sidecars. It is disabled by
default and never reads or mutates source files.

**Branch:** `v2.0-knowledge-graph`, created directly from exact validated design
tip `a2a9a071600de74759937f05a7be61f85e9d5d93`.

**Merged status:** Integrated into `main` by an explicit history-preserving
v2.0.0 release merge after exact-tip Windows, Ubuntu, and macOS CI. Completed
interactive/community validation is not claimed.

The [stability-first design](docs/Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md)
defines the bounded graph projection implemented over existing v1.9 files, relationships,
collections, and explicit decisions. It prioritizes failure isolation,
determinism, conservative identity, correction preservation, selective repair,
Search fallback, privacy, and bounded resources over feature breadth.

**Stable implementation-candidate scope:** separate derived graph and
authoritative graph-native decision stores;
File/Source/Folder/Collection/Document Set/manual-entity
nodes; typed evidence-backed edges; one-hop list/detail navigation; manual
merge/split/control; incremental reconciliation; graph inspection/forget/repair;
bounded opt-out Search context.

**Experimental/deferred:** entity-suggestion contracts are prepared but no
live producer is wired; two-hop browsing remains opt-in; automatic
Person/Place/Event/Topic identity, graph canvas,
unrestricted traversal, conversation, autonomous actions, and remote/cross-device
graphs are deferred.

**Release boundary:** native Windows/macOS artifacts, checksums, tagging, and
publication are performed by the release workflow from the exact integrated
source. The fully unchecked manual and RC checklists remain
follow-up/community evidence trackers rather than claims of completed testing.
See the [Knowledge Graph guide](docs/KNOWLEDGE_GRAPH_v2.0.md),
[validation report](docs/V2.0_VALIDATION_REPORT.md), and
[RC plan](docs/V2.0_RC_STABILIZATION_PLAN.md).

## Planned

The versions below are planning concepts supplied by current project direction.
The repository contains no corresponding branches, implementation
specifications, source, commits, tags, or release promises. Titles, order, and
scope may change after research and review.

### v2.1 — Adaptive Intelligence

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is opt-in local adaptation from explicit user decisions, with
inspectable influence and reset controls rather than hidden behavioral
profiling.

**Potential major capabilities:** local preference signals; suggestion/ranking
adaptation; influence explanations; correction, reset, export, and retention
controls.

**Conceptual dependencies:** stable graph/context provenance and a privacy
design that prevents implicit authorization or opaque model training.

### v2.2 — Media Intelligence

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is richer bounded understanding of images, audio, and video while
retaining local-first processing, optional dependencies, and honest capability
states.

**Potential major capabilities:** media metadata/timelines; optional local
transcription or visual analysis; searchable segments; provenance and resource
controls.

**Conceptual dependencies:** reader/security research, platform/native
dependency policy, storage quotas, and explicit performance/privacy limits.

### v2.3 — Conversational Assistant

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is an evidence-grounded conversation over user-selected indexed
material. It would answer and navigate; it would not become an autonomous
filesystem agent.

**Potential major capabilities:** cited responses; visible retrieval context;
session/privacy controls; deterministic fallback and bounded local-provider
integration; proposal-only actions.

**Conceptual dependencies:** mature Search/graph evidence, prompt-injection and
data-disclosure design, provider policy, and strict Change Plan separation.

### v2.4 — Cloud and NAS

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is optional, explicit indexing or synchronization with
user-controlled network, NAS, or cloud locations while preserving ownership,
offline behavior, and visible data movement.

**Potential major capabilities:** provider-scoped sources; credentials outside
ordinary stores/logs; synchronization status; offline/retry/conflict behavior;
selective local retention.

**Conceptual dependencies:** provider protocol and threat-model research,
identity/conflict semantics, encryption/authentication, and migration policy.

### v2.5 — OpenSorSe Server

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is a self-hostable server provider for durable indexing and query
services. It would not make PostgreSQL or another server a desktop requirement.

**Potential major capabilities:** authenticated provider API; server-side
indexing/storage; provider-neutral desktop client; deployment, backup,
migration, quotas, and observability.

**Conceptual dependencies:** v2.4 provider boundaries, a formal security model,
operational ownership, protocol versioning, and compatibility tests.

### v2.6 — Collaboration

**Branch:** None; no branch exists.

**Merged status:** Not applicable; planned concept only.

The concept is controlled sharing of collections, annotations, relationships,
and proposals between identified users without silently sharing source content
or approval authority.

**Potential major capabilities:** workspaces and roles; shared metadata;
review/comments; conflict/audit history; explicit content-sharing policy;
independent Change Plan approval.

**Conceptual dependencies:** a deployed/authenticated server model, access
control, audit, privacy, synchronization, and recovery designs.

## Long-term Vision

OpenSorSe may grow from a single-user desktop application into an optional
self-hosted knowledge platform. The stable ideas are more important than the
version labels:

- users retain control of files, derived data, providers, and consequential
  actions;
- local deterministic behavior remains useful;
- server and cloud capabilities are optional, explicit, and replaceable;
- facts, user assertions, and machine inferences remain distinguishable;
- Search and assistance expose evidence and coverage;
- collaboration never transfers mutation approval implicitly;
- migration, recovery, privacy, and accessibility are designed with the
  feature, not after it.

## Research

These questions require evidence before a version can commit to them:

- Which relationship/graph model is expressive without becoming an
  unbounded ontology or migration trap?
- What local embedding or learned-ranking options improve retrieval enough to
  justify model, GPU, licensing, privacy, and packaging costs?
- What protocol can preserve provider-neutral Search, privacy, repair, and
  cancellation semantics over a server boundary?
- What trust, authentication, authorization, encryption, audit, and key
  recovery model is appropriate for Cloud/NAS/Server/Collaboration?
- Which media extractors can be bounded, cross-platform, FOSS-compatible, and
  safely isolated from hostile input?
- What out-of-process plugin model provides meaningful isolation without
  breaking the current SDK’s cancellation, provenance, and compatibility
  guarantees?
- Which at-rest protection options add real value beyond operating-system disk
  and account controls without making unrecoverable custom cryptography?

Research produces decision records, prototypes, threat models, measurements,
and rejected alternatives—not product claims.

## Ideas Backlog

Promising concepts not assigned to a version include:

- signed and reproducible installers, updates, and distribution packages;
- broader localization and locale-specific Search evaluation;
- out-of-process plugin isolation, publisher signatures, and a reviewed
  extension catalog;
- richer full-fidelity office/archive readers with hostile-input containment;
- exportable reports and carefully evidenced rename/move inference;
- user-visible backup/restore tools for OpenSorSe-owned data;
- portable encrypted export for selected application-owned metadata;
- improved startup coordination and progress;
- a typed application-data path service;
- additional AI providers behind the existing application contract;
- mobile or web companion exploration, contingent on a server/privacy model.

An idea enters a version only after scope, dependencies, non-goals, migration,
security, privacy, validation, accessibility, and release evidence are defined.
