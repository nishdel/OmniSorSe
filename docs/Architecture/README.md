# Architecture documentation

Start with the current [Architecture Overview](../ARCHITECTURE_OVERVIEW.md) and
[OpenSorSe System Map](OpenSorSe_System_Map.md). They describe the current
unmerged v2.0 implementation candidate plus inherited v1.9 system and take
precedence when an older document uses future-oriented
language.

The [Product Vision](../../PRODUCT_VISION.md) defines why these boundaries
exist, and [Engineering Principles](../../ENGINEERING_PRINCIPLES.md) defines
the cross-cutting reasoning for MVVM, providers, testing, validation,
compatibility, and recovery.

## Current implementation references

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
- `06_Search/10_v1.9_Relationships_Context.md`: current relationship evidence,
  virtual collection, contextual Search, schema 3, privacy, repair, and graph
  bound architecture.
- `06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md`: implemented-candidate
  graph/decision isolation, conservative identity, deterministic projection,
  concurrency, bounds, privacy, and mandatory RC contract.
- `01_Core/10_Advanced_Diagnostics.md`: implemented unified diagnostics.
- `02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md`: current
  watcher/reconciliation boundary.
- `03_Readers/10_v1_OCR_and_Metadata.md`: implemented extraction/OCR boundary.
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
- `99_Appendix/ADR-001` through `ADR-003`: accepted architectural decisions.

## v2.0 candidate architecture

- `06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md`: accepted isolated,
  deterministic, bounded Knowledge Graph design implemented by the unmerged
  candidate. Source and tests define exact behavior; validation and RC gates
  remain open.

The practical current guide is
[Knowledge Graph v2.0](../KNOWLEDGE_GRAPH_v2.0.md).

Its supporting failure, migration, recovery, concurrency, integrity, test, and
release plans are indexed in the
[v2.0 specification package](../Implementation_Spec/v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md).

## Long-term and historical design library

The remaining detailed documents in `01_Core` through `10_Plugins`, plus the
older system flows, record the original design vocabulary and future ideas.
Some describe SQLite, generic service registries, broad media readers,
reporting, online plugin services, or security models that are not implemented.
They are retained because they provide design history and candidate boundaries,
but they are not a statement of current product behavior.

When changing the implementation:

1. update the top-level architecture overview and system map;
2. update the relevant versioned subsystem document;
3. preserve historical documents unless their useful information has been
   consolidated and there is clear evidence they are obsolete;
4. label future architecture explicitly rather than presenting it as shipped.

The remaining historical/long-term files are retained deliberately. Their
presence is not a roadmap commitment. See the
[Documentation Inventory](../DOCUMENTATION_INVENTORY.md) for the exhaustive
authority and retention model.
