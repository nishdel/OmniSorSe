# Documentation inventory

**Archive boundary:** This record preserves historical design/audit context.
Its old inventory and retention recommendations are not current policy.
Use the [canonical-document policy](DOCUMENTATION_POLICY.md) for new work.

**Document type:** Historical documentation audit snapshot

**Status:** Frozen to its v2.4 repository basis. Use
[`docs/README.md`](README.md) for the current documentation hierarchy and
[`docs/engineering/README.md`](engineering/README.md) for the current
engineering-knowledge system. The counts and classifications below remain
useful archaeology but are not a current inventory.

**Audit date:** 2026-08-03

**Repository basis:** released OmniSorSe v2.4.0 from
`v2.4-omnisorse-transition`, based on exact OpenSorSe v2.3.0 tip
`abe43e171bdcefa48cc55a6af6e560e2c8c8ce94`

## Audit method and initial inventory

The audit inspected every committed documentation candidate in the clean
repository tree. It read each text file, collected headings/links/status/version
language, hashed every file, inspected non-text documentation assets, reviewed
all README-linked documents, and compared documentation claims with Git
history, branches/tags, source projects, tests, CI policy, and version metadata.

The initial inventory contained 522 documentation candidates:

| Area | Files | Classification |
| --- | ---: | --- |
| Repository root | 4 | README, Contributing, license, and third-party notices |
| `docs/` top level and images | 69 | Living guides plus versioned/historical records |
| `docs/Architecture/` | 123 | Current architecture mixed with historical/long-term design |
| `docs/Implementation_Spec/` | 94 | Historical specifications, decisions, proposals, and indexes |
| Frozen v1.0 package documentation | 214 | Immutable package snapshot |
| Frozen v1.0 package licenses | 12 | Immutable redistribution evidence |
| Frozen v1.0 package top-level documents | 6 | Immutable package README/release/install/legal records |
| **Total** | **522** | |

Of these, 520 were readable text files and two were PNG documentation assets.
The text inventory contained 66,317 lines. Hash analysis found 365 unique
contents and 154 exact-duplicate groups (157 copies beyond the first). One
hundred fifty duplicate groups crossed from current/source paths into the
frozen v1.0 package. Those copies are intentional package contents, not current
documentation consolidation candidates.

Two zero-byte files exist:

- `docs/Implementation_Spec/v0.1_AI_Coding_Prompts/03_Specification_Fix`
- its exact frozen v1.0 package copy

They are empty historical prompt slots, not current guidance. No exact-content
duplicate group existed solely within the source documentation tree.

## Final inventory for this review

This review adds four root living documents:

- `PRODUCT_VISION.md`
- `PRODUCT_ROADMAP.md`
- `ENGINEERING_PRINCIPLES.md`
- `RELEASE_HISTORY.md`

No documentation file is removed. The v2.0 design package adds twelve source
documents; the implementation candidate adds a user/architecture guide,
security notes, Version Notes, implementation/validation records, and an RC
plan. Historical release, implementation, validation, version,
troubleshooting, user, migration, and manual-testing records remain present.

The v2.2 release additionally adds
`docs/MEDIA_INTELLIGENCE.md` as its authoritative implementation/design
boundary and `docs/MANUAL_TESTING.md#manual-v2-2` as a mixed evidence tracker. It marks
only completed controlled Windows provider/OCR/migration checks and leaves
interactive, transcription, and native Linux/macOS scenarios unchecked. It
also adds `docs/CHANGELOG.md#release-v2-2-0`. These records extend this inventory
without rewriting historical release records.

The v2.3 release adds
`docs/CONTENT_INTELLIGENCE.md` as its authoritative design/implementation
boundary, `docs/MANUAL_TESTING.md#manual-v2-3` as its evidence-separated validation
tracker, and `docs/CHANGELOG.md#release-v2-3-0` as the published release summary.

The v2.4 release adds
`docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md` as the authoritative
rename, compatibility, threat-model, and Explorer Protocol boundary, plus
`docs/MANUAL_TESTING.md#manual-v2-4` as its evidence-separated checklist, and
`docs/CHANGELOG.md#release-v2-4-0` as its published summary. These documents do not
claim an OmniExplorer implementation.

## Authority model

### Living and authoritative

These files describe current product/project policy or the current source tree:

- `README.md`
- `CONTRIBUTING.md`
- `PRODUCT_VISION.md`
- `PRODUCT_ROADMAP.md`
- `ENGINEERING_PRINCIPLES.md`
- `RELEASE_HISTORY.md`
- `THIRD_PARTY_NOTICES.md`
- `docs/README.md`
- `docs/DOCUMENTATION_INVENTORY.md`
- `docs/RELEASE_STATUS.md`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/REPOSITORY_STRUCTURE.md`
- `docs/DEVELOPER_GUIDE.md`
- `docs/MAINTAINER_GUIDE.md`
- `docs/INSTALLATION.md`
- `docs/SAFETY_AND_PRIVACY.md`
- `docs/SECURITY.md`
- `docs/PLATFORM_COMPATIBILITY_MATRIX.md`
- `docs/LINUX_BUILD_AND_LAUNCH.md`
- `docs/FOSS_DEPENDENCY_POLICY.md`
- `docs/CONTENT_INTELLIGENCE.md`
- `docs/MANUAL_TESTING.md#manual-v2-3`
- `docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md`
- `docs/MANUAL_TESTING.md#manual-v2-4`
- `docs/WORKFLOW_AND_INDEXING_QUALITY.md`
- `docs/OMNIBRILLE_COMPANION_HANDOFF.md`
- `docs/MANUAL_TESTING.md#manual-v2-5`
- `docs/EXPLAINABLE_SMART_TAGS.md`
- `docs/MANUAL_TESTING.md#manual-v2-6`
- `docs/SCALABLE_FACETED_DISCOVERY.md`
- `docs/MANUAL_TESTING.md#manual-v2-7`
- `docs/GUIDED_WORKFLOWS_PRODUCT_COHERENCE.md`
- `docs/MANUAL_TESTING.md#manual-v2-8`
- `docs/REVIEWED_INTELLIGENT_ORGANIZATION.md`
- `docs/MANUAL_TESTING.md#manual-v2-9`
- `docs/PRODUCTION_HARDENING.md`
- `docs/OPERATIONAL_RUNBOOKS.md`
- `docs/MANUAL_TESTING.md#manual-v2-10`
- `docs/CHANGELOG.md#release-v2-10-0`
- `docs/SUPPORTED_RUNTIME_PLATFORM_READINESS.md`
- `docs/MANUAL_TESTING.md#manual-v2-11`
- `docs/CHANGELOG.md#release-v2-11-0`
- `docs/TRUSTED_RELATIONSHIPS_CONTEXT.md`
- `docs/MANUAL_TESTING.md#manual-v2-12`
- `docs/CHANGELOG.md#release-v2-12-0`
- `docs/CHANGELOG.md` as the cumulative historical change record
- `docs/Architecture/README.md`
- `docs/Architecture/OpenSorSe_System_Map.md`
- `docs/Architecture/00_System/00_Overview.md`
- `docs/Architecture/99_Appendix/Coding_Standards.md`
- `docs/Architecture/99_Appendix/Naming_Conventions.md`
- `docs/Architecture/99_Appendix/Glossary.md`
- `docs/Architecture/99_Appendix/Technology_Stack.md`
- `docs/Architecture/99_Appendix/ADR.md` as the ADR index/policy
- `docs/Implementation_Spec/README.md` as the historical specification index
- `docs/images/README.md`
- `docs/images/opensorse-logo.png`

`docs/roadmap.md` and `docs/project_philosophy.md` are retained compatibility
navigation pages. They no longer compete as independent living authorities.

### Current subsystem contracts

These version-named documents remain current because later releases build on,
rather than supersede, their stable subsystem boundary:

- `docs/Architecture/01_Core/10_Advanced_Diagnostics.md`
- `docs/Architecture/02_Scanner/09_Watched_Folders_and_Incremental_Scanning.md`
- `docs/Architecture/03_Readers/10_OCR_and_Metadata.md`
- `docs/Architecture/04_AI/11_Small_Model_Prompt_Contracts.md`
- `docs/Architecture/05_Database/09_Local_Content_Stores_and_Migrations.md`
- `docs/Architecture/06_Search/07_Semantic_Index.md`
- `docs/Architecture/06_Search/08_Tag_Provenance.md`
- `docs/Architecture/07-Rules/06_Restructuring_History.md`
- `docs/Architecture/07-Rules/07_Change_Plans_and_Operation_Journal.md`
- `docs/Architecture/07-Rules/08_Workflow_Profiles_and_Recipes.md`
- `docs/Architecture/08_Gui/04_Results_Page.md`
- `docs/Architecture/08_Gui/11_Catalog_Page.md`
- `docs/Architecture/08_Gui/12_Catalog_Comparison_Page.md`
- `docs/Architecture/08_Gui/13_Structure_History.md`
- `docs/Architecture/08_Gui/14_Review_Changes.md`
- `docs/Architecture/10_Plugins/06_Plugin_Foundation.md`
- `docs/Architecture/00_System/08_Platform_Architecture.md`
- `docs/Architecture/00_System/09_Reliability_Architecture.md`
- `docs/Architecture/00_System/10_Deep_Indexing_Architecture.md`
- `docs/Architecture/06_Search/09_Search_Intelligence_Privacy.md`
- `docs/Architecture/06_Search/10_Relationships_Context.md`
- `docs/Architecture/06_Search/11_Knowledge_Graph_Stability_Design.md`

The v1.4 Extension SDK, Plugin Author Guide, Manifest Reference, Local Package
Guide, and v1.5 platform/portability addenda also remain current stable
contracts.

### Current version-specific documentation

These files accurately describe the v1.9 branch within their scope:

- `docs/USER_GUIDE.md#guide-v1-9`
- `docs/RELATIONSHIPS_AND_COLLECTIONS.md`
- `docs/TROUBLESHOOTING.md#support-v1-8`
- `docs/MANUAL_TESTING.md#manual-v1-9`
- `docs/CHANGELOG.md#notes-v1-9`
- `docs/IMPLEMENTATION_HISTORY.md#implementation-v1-9`
- `docs/VALIDATION.md#validation-v1-9`
- `docs/Implementation_Spec/v1.9/061_Relationships_Context_and_Smart_Collections.md`

The v1.9 User/Relationship guides and inherited v1.8 Troubleshooting file are
current guidance. Version Notes, implementation/validation reports, manual
checklist, and specification are immutable release evidence once that work is
complete.

The v1.7 deep-indexing and v1.8 Search architecture/specifications remain
current foundations. Their reports, Version Notes, User Guides,
Troubleshooting, and Manual Testing documents remain version snapshots.

### v2.0 implementation candidate and design authority

The v2.0 Knowledge Graph package is the accepted design/acceptance authority
for the unmerged implementation candidate:

- `docs/Architecture/06_Search/11_Knowledge_Graph_Stability_Design.md`
- `docs/Implementation_Spec/v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md`
- `docs/Implementation_Spec/v2.0/062_Knowledge_Graph_Stability_Design.md`
- `docs/Implementation_Spec/v2.0/FAILURE_MODE_ANALYSIS.md`
- `docs/Implementation_Spec/v2.0/MIGRATION_SAFETY_PLAN.md`
- `docs/Implementation_Spec/v2.0/RECOVERY_AND_REPAIR_PLAN.md`
- `docs/Implementation_Spec/v2.0/CONCURRENCY_CANCELLATION_AND_RESOURCE_MODEL.md`
- `docs/Implementation_Spec/v2.0/GRAPH_INTEGRITY_MODEL.md`
- `docs/Implementation_Spec/v2.0/AUTOMATED_TEST_MATRIX.md`
- `docs/KNOWLEDGE_GRAPH_COMPATIBILITY.md`
- `docs/RELEASE_STATUS.md#historical-v2-0-readiness`
- `docs/MANUAL_TESTING.md#manual-v2-0`
- `docs/KNOWLEDGE_GRAPH.md`
- `docs/SECURITY.md`
- `docs/CHANGELOG.md#notes-v2-0`
- `docs/IMPLEMENTATION_HISTORY.md#implementation-v2-0`
- `docs/VALIDATION.md#validation-v2-0`
- `docs/MAINTAINER_GUIDE.md#historical-v2-0-stabilization`

The architecture/specification describe implemented-candidate boundaries;
source and tests remain authoritative for exact behavior. Compatibility,
validation, RC, and checklist evidence is incomplete, and every
manual/release-readiness/RC checkbox remains unchecked.

### Historical records to preserve

The following are historical by family and must not be rewritten to describe a
newer release:

- all older `USER_GUIDE_v*.md` files;
- all older `TROUBLESHOOTING_v*.md` files;
- all `MANUAL_TESTING_v*.md` files once their release closes;
- all `VERSION_NOTES_v*.md` files;
- `DATA_MODEL.md`, `MIGRATION.md`, and
  `MAINTAINER_GUIDE.md#historical-v1-0-release-checklist`;
- all `V1.*_IMPLEMENTATION_REPORT.md` and `V1.*_VALIDATION_REPORT.md` files;
- numbered specifications `001` through `061`;
- every release proposal, implementation-decision file, audit correction, and
  archived v0.1 coding prompt under `docs/Implementation_Spec/`;
- `AUTONOMOUS_DECISIONS.md` and `IMPLEMENTATION_PROGRESS_REMOTE.md`;
- every file under `release/OpenSorSe-v1.0.0/`.

Old branch names, environment blockers, test totals, planned wording, and
manual completion states are valid evidence in these snapshots. They do not
override current living documents.

### Historical and long-term architecture

Unless explicitly listed as current above, detailed documents in these
directories are historical/long-term design:

- `docs/Architecture/00_System/`
- `docs/Architecture/01_Core/`
- `docs/Architecture/02_Scanner/`
- `docs/Architecture/03_Readers/`
- `docs/Architecture/04_AI/`
- `docs/Architecture/05_Database/`
- `docs/Architecture/06_Search/`
- `docs/Architecture/07-Rules/`
- `docs/Architecture/08_Gui/`
- `docs/Architecture/09_Reports/`
- `docs/Architecture/10_Plugins/`

Many predate the implementation and describe broad readers, reports, generic
services, database schemas, rule execution, online plugins, or security models
that are not current product behavior. They remain useful design history. The
Architecture Overview and System Map replace the older Component Map, Data
Flow, Event Flow, User Flow, and deployment diagrams as current entry points.
No mass deletion is justified.

## Overlap and consolidation decisions

| Overlap | Decision |
| --- | --- |
| `docs/project_philosophy.md` and product direction | Consolidated into root `PRODUCT_VISION.md`; old path retained as a navigation page. |
| `docs/roadmap.md`, release status, and future ideas | Consolidated future/version planning into root `PRODUCT_ROADMAP.md`; old path retained as a navigation page. |
| Changelog, Release Status, Release History, Version Notes, reports | Assigned distinct roles: detailed changes, current readiness, concise index, version summary, and immutable evidence. |
| Architecture Overview, System Map, and detailed architecture library | Overview is current narrative, System Map is the visual model, and the architecture index labels detailed current/historical records. |
| Contributing, Developer Guide, Maintainer Guide, Engineering Principles | Contributing is the contribution contract, Developer Guide is the walkthrough, Maintainer Guide is release/compatibility operations, and Engineering Principles explains cross-cutting reasoning. |
| Current docs and exact copies in v1.0 package | Package copies remain frozen distribution contents. They are not removed or updated. |

No historical report, validation evidence, manual checklist, release note,
implementation specification, package document, or license file was deleted.

## Outdated or incomplete documents found

The review found these living-document problems:

- the former philosophy claimed a v1.5 current boundary;
- the former roadmap had completed milestones but did not contain the requested
  planned version concepts, branch/merge truth, research, or backlog structure;
- Installation described building v1.5 rather than current v1.8 source and
  linked v1.4 Troubleshooting as current;
- Technology Stack described v1.0 and explicitly said SQLite/plugins were not
  implemented;
- the Glossary described rules/actions and processing as if broad autonomous
  execution and a generic database pipeline were current;
- the unversioned Platform Compatibility Matrix was still framed as a v1.5
  pre-implementation audit despite later native CI evidence;
- branch guidance invented `v1.1-safe-file-operations`, while the repository’s
  actual branch is `v1.1`;
- the documentation inventory’s numerical result stopped at v1.4;
- README navigation mixed living, current version, and historical evidence
  without saying which was authoritative;
- no single living document explained the reasoning for MVVM, provider-neutral
  SQLite storage, validation, release evidence, recovery, compatibility, and
  documentation policy.

This documentation branch corrects those living documents. Historical snapshots
retain their original wording.

## Templates and assets

- `docs/Architecture/Template.md` and
  `docs/Implementation_Spec/Template.md` are contributor templates, not current
  product behavior.
- `docs/images/README.md` defines screenshot asset policy.
- `docs/images/opensorse-logo.png` is the current documentation logo.
- product icons under `src/OpenSorSe.Desktop/Assets/` are runtime assets, not
  documentation candidates.

## Remaining documentation debt

- Many original architecture-library documents are verbose and aspirational.
  They are indexed safely, but per-file historical banners would further reduce
  accidental misuse.
- Significant later decisions—Change Plans, embedded SQLite/provider
  neutrality, the plugin trust model, and hybrid ranking—are documented in
  specifications/architecture but not formal standalone ADRs. Accepted ADRs
  must not be rewritten; new ADRs can record future decisions.
- Real current screenshots are not checked in. README screenshot placeholders
  were removed rather than presenting stale or generated captures. The
  privacy-safe v2.0 capture checklist remains intentionally unchecked.
- Native v2.0 packaging is documented in `RELEASE_PACKAGING.md`; the v1.0
  package tree remains frozen historical evidence.
- v1.7, v1.8, and v1.9 interactive manual validation remains open where each
  release checklist records it.
- v1.7/v1.8/v1.9 are not integrated into `main`; documentation must keep source
  implementation separate from integrated/published release language.
- Mermaid validation is structural. Visual rendering still requires GitHub or
  a compatible renderer.

## Removal rule

A documentation file may be removed only when:

1. its authority and historical value have been classified;
2. useful unique content has a verified replacement;
3. all inbound links are updated;
4. release/package evidence is unaffected;
5. Git history is an adequate remaining archive;
6. the change records why deletion is safer than a compatibility page.

Age, a versioned filename, or overlap alone is not sufficient reason.
