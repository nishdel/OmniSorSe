# Architecture documentation

Start with the current [Architecture Overview](../ARCHITECTURE_OVERVIEW.md) and
[OmniSorSe System Map](OpenSorSe_System_Map.md). They describe the current
OmniSorSe v2.12 implementation candidate over the published v2.4 baseline and
take precedence when an older document uses future-oriented or now-stale
language. [Current State](../CURRENT-STATE.md) owns volatile runtime, schema,
protocol, source-line, and confidence facts.

The [Product Vision](../../PRODUCT_VISION.md) defines why these boundaries
exist, and [Engineering Principles](../../ENGINEERING_PRINCIPLES.md) defines
the cross-cutting reasoning for MVVM, providers, testing, validation,
compatibility, and recovery.

## Choose the right architecture level

| Need | Start with | What it owns |
| --- | --- | --- |
| Current narrative | [Architecture Overview](../ARCHITECTURE_OVERVIEW.md) | Components, flows, persistence, failure behavior, and current debt |
| Visual navigation | [System Map](OpenSorSe_System_Map.md) | Five current Mermaid views and important code boundaries |
| Ownership and sources of truth | [Architecture Authority Map](../engineering/ARCHITECTURE_AUTHORITY.md) | Owns/reads/derives/mutates/persists/publishes rules |
| Code location | [Repository Structure](../REPOSITORY_STRUCTURE.md) | Project graph, responsibilities, tests, and change locations |
| Durable decisions | [ADR Index](99_Appendix/ADR.md) | Accepted decisions and reconstructed rationale |
| One subsystem or version | Detailed library below | Scoped current contracts, candidate overlays, or historical foundations as labelled |

## Detailed implementation references

The list is intentionally layered: the overview and system map remain
cumulative authority; versioned records add scoped detail and retain the status
stated in their description.

- `00_System/00_Overview.md`: concise current component summary.
- `00_System/08_v1.5_Platform_Architecture.md`: current Windows/Linux platform,
  identity, filesystem, desktop, tool, and packaging boundaries.
- `00_System/09_v1.6_Reliability_Architecture.md`: current atomic persistence,
  bounded-resource, cancellation, observer-isolation, and lifecycle hardening.
- `00_System/10_v1.7_Deep_Indexing_Architecture.md`: current durable stages,
  embedded provider, recovery, storage policy, progress, and Search coverage.
- `06_Search/09_v1.8_Search_Intelligence_Privacy.md`: current hybrid ranking,
  interpreted-filter, explanation/snippet, privacy, repair, and Search
  diagnostics boundaries.
- `06_Search/10_v1.9_Relationships_Context.md`: inherited relationship and
  virtual-collection foundation at its historical schema-3 boundary. Use the
  v2.12 record for current schema-6 evidence and pair authority.
- `06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md`: released v2.0
  graph/decision isolation, conservative identity, deterministic projection,
  concurrency, bounds, privacy, and mandatory RC contract.
- `06_Search/12_v2.1_Search_AI_Quality.md`: released boundary for
  filename tiers, bounded optional Ollama reranking, model runtime discovery,
  fallback, indexing clarity, result commands, and compatibility.
- `../OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md`: released v2.4
  rename, compatibility-in-place, and Protocol 1.0 foundation. It is a version
  snapshot; use Current State and the System Map for cumulative behavior.
- `../WORKFLOW_AND_INDEXING_QUALITY_v2.5.md` and
  `../OMNIBRILLE_COMPANION_HANDOFF_v2.5.md`: post-operation reconciliation,
  base-first indexing, and the optional explicit OmniBrille launch/handoff.
- `../EXPLAINABLE_SMART_TAGS_v2.6.md`: unreleased schema-6 Smart Tag authority,
  taxonomy, classifier, deferred stage, Search/filter, privacy, UI, and
  user-decision boundaries.
- `../SCALABLE_FACETED_DISCOVERY_v2.7.md`: unreleased complete-library candidate
  selection, faceted discovery, Saved Views, and bounded extraction boundaries.
- `../GUIDED_WORKFLOWS_PRODUCT_COHERENCE_v2.8.md`: durable readiness and stable
  Search/Files/Smart Tag/review workflow context without a new authority.
- `../REVIEWED_INTELLIGENT_ORGANIZATION_v2.9.md`: bounded stable-ID recipe
  preview and handoff into the existing Change Plan boundary.
- `../PRODUCTION_HARDENING_v2.10.md`: profile ownership, fail-closed recovery
  stores, logical state transfer, Forget, health, and provenance.
- `../SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md`: .NET 10 and current
  runtime/package evidence boundaries.
- `../TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md`: current schema-6 relationship
  evidence/pair authority, direct Related Files, bounded reanalysis, and
  format-2 authored state.
- `01_Core/10_Advanced_Diagnostics.md`: implemented unified diagnostics.
- `02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md`: current
  watcher/reconciliation boundary.
- `03_Readers/10_v1_OCR_and_Metadata.md`: implemented extraction/OCR boundary.
- `../MEDIA_INTELLIGENCE_v2.2.md`: authoritative released v2.2 provider,
  evidence, schema-4, bounds, optional-tool, Search, relationship, privacy, and
  diagnostics boundary.
- `04_AI/11_Small_Model_Prompt_Contracts.md`: implemented provider contract and
  validation shape.
- `05_Database/09_v1_Local_Content_Stores_and_Migrations.md`: implemented local
  JSON-store boundary retained by v1.9; the separate durable Search provider
  uses embedded SQLite behind Application contracts.
- `06_Search/07_v1_Semantic_Index.md` and `08_v1_Tag_Provenance.md`: implemented
  local semantic/tag behavior.
- `07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md`: current mutation
  boundary.
- `07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md`: current workflow/recipe
  boundary.
- `08_Gui/04_Results_Page.md`, `11_Catalog_Page.md`,
  `12_Catalog_Comparison_Page.md`, `13_Structure_History.md`, and
  `14_Review_Changes.md`: current implemented feature summaries.
- `10_Plugins/06_v1.4_Plugin_Foundation.md`: current plugin host and SDK.
- [ADR Index](99_Appendix/ADR.md): accepted decisions; use the index rather
  than assuming a fixed numerical range.

## Released foundation: v2.0 implemented architecture

- `06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md`: accepted isolated,
  deterministic, bounded Knowledge Graph design released in v2.0. Source and
  tests define exact behavior; its historical validation records remain
  available.

The practical current guide is
[Knowledge Graph v2.0](../KNOWLEDGE_GRAPH_v2.0.md).

Its supporting failure, migration, recovery, concurrency, integrity, test, and
release plans are indexed in the
[v2.0 specification package](../Implementation_Spec/v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md).

## Long-term and historical design library

The remaining detailed documents in `01_Core` through `10_Plugins`, plus the
older system flows, record the original design vocabulary and future ideas.
Some describe SQLite, generic service registries, media capabilities broader
than the bounded v2.2 release,
reporting, online plugin services, or security models that are not implemented.
They are retained because they provide design history and candidate boundaries,
but they are not a statement of current product behavior.

When changing the implementation:

1. identify Current State, architecture, diagram, glossary, ADR, safety, and
   validation impact during planning;
2. update Current State only when its volatile facts changed, and update the
   top-level architecture/system map only when their boundaries changed;
3. update the current subsystem document; create or update a version record
   only when the work belongs to that release boundary;
4. preserve historical documents unless their useful information has been
   consolidated and there is clear evidence they are obsolete;
5. label future architecture explicitly rather than presenting it as shipped.

The remaining historical/long-term files are retained deliberately. Their
presence is not a roadmap commitment. See the
[Documentation Inventory](../DOCUMENTATION_INVENTORY.md) for the exhaustive
authority and retention model.
