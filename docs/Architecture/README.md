# Architecture documentation

Start with the current [Architecture Overview](../ARCHITECTURE_OVERVIEW.md) and
[OpenSorSe System Map](OpenSorSe_System_Map.md). They describe the implemented
v1.5 system and take precedence when an older document uses future-oriented
language.

## Current implementation references

- `00_System/00_Overview.md`: concise current component summary.
- `00_System/08_v1.5_Platform_Architecture.md`: current Windows/Linux platform,
  identity, filesystem, desktop, tool, and packaging boundaries.
- `01_Core/10_Advanced_Diagnostics.md`: implemented unified diagnostics.
- `02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md`: current
  watcher/reconciliation boundary.
- `03_Readers/10_v1_OCR_and_Metadata.md`: implemented extraction/OCR boundary.
- `04_AI/11_Small_Model_Prompt_Contracts.md`: implemented provider contract and
  validation shape.
- `05_Database/09_v1_Local_Content_Stores_and_Migrations.md`: implemented local
  JSON-store boundary; despite the directory name, v1.5 does not use a
  relational database.
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
