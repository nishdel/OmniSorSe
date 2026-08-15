# OmniSorSe documentation index

This is the authoritative navigation page for repository documentation. It
explains what each important document contains, when to read it, and whether it
is living guidance or a historical/version snapshot.

## Authority rules

When documents overlap, use this order:

1. current source and tests define implemented behavior;
2. current living product, architecture, engineering, safety, and release-status
   documents explain that behavior;
3. current version-specific guides/specifications explain their subsystem or
   release boundary;
4. older versioned documents and the packaged v1.0 tree are historical
   evidence;
5. planned roadmap concepts and old long-term architecture are not evidence of
   implementation.

[Release Status](RELEASE_STATUS.md) is authoritative for current readiness.
[Release History](../RELEASE_HISTORY.md) is the concise historical index.
[Product Roadmap](../PRODUCT_ROADMAP.md) is authoritative for future planning.

## Understand the project in one hour

| Time | Read | What it answers | Status |
| ---: | --- | --- | --- |
| 5 min | [Repository README](../README.md) | What OmniSorSe is, what exists now, and the current branch/package boundary. | Living |
| 10 min | [Product Vision](../PRODUCT_VISION.md) | Why the project exists; AI, privacy, Search, review, Undo, SQLite, and provider principles. | Living |
| 10 min | [Product Roadmap](../PRODUCT_ROADMAP.md) | What is completed, in progress, planned, research, or backlog. | Living |
| 15 min | [Architecture Overview](ARCHITECTURE_OVERVIEW.md) and [System Map](Architecture/OpenSorSe_System_Map.md) | How components, storage, Search, plugins, and the mutation boundary fit together. | Living |
| 10 min | [Engineering Principles](../ENGINEERING_PRINCIPLES.md) and [Repository Structure](REPOSITORY_STRUCTURE.md) | Why the engineering model exists and where code belongs. | Living |
| 10 min | [Contributing](../CONTRIBUTING.md) and [Developer Guide](DEVELOPER_GUIDE.md) | How to branch, build, test, validate, document, and submit a focused change. | Living |

## Living product and project documents

| Document | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Repository README](../README.md) | Concise product/current-source boundary, capabilities, safety, platform, build, and navigation. | First contact with the project. | Living |
| [Product Vision](../PRODUCT_VISION.md) | Purpose, audience, goals, current-versus-future behavior, AI/privacy/control philosophy, Search/storage reasoning. | Before product or architectural decisions. | Living and authoritative for product philosophy |
| [Product Roadmap](../PRODUCT_ROADMAP.md) | Version branches, dependencies, merge state, completed/in-progress/planned concepts, research, and backlog. | Before describing or proposing future work. | Living and authoritative for roadmap status |
| [Engineering Principles](../ENGINEERING_PRINCIPLES.md) | Reasoning for architecture, MVVM, stores/providers, testing, releases, CI, performance, safety, compatibility, and recovery. | Before a cross-cutting change or review. | Living and authoritative for cross-cutting engineering policy |
| [Release History](../RELEASE_HISTORY.md) | Concise branch/date/test/merge history with links to detailed records. | When tracing how the product evolved. | Living historical index |
| [Release Status](RELEASE_STATUS.md) | Exact current branch, integration, automated/manual validation, package, tag, and publication facts. | Before making a readiness or release claim. | Living and authoritative for current readiness |
| [v2.4 OmniSorSe transition and Explorer Protocol](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md) | Rename compatibility, executable/package decisions, protocol contracts/transport/security/limits, and future companion boundary. | Before changing branding, profile paths, packaging identity, or Explorer integration. | Current released design and implementation record |
| [v2.4 Manual Testing](MANUAL_TESTING_v2.4.md) | Separates genuine Windows profile/installer and external two-process protocol evidence from unchecked accessibility and native-platform scenarios. | During v2.4 review and maintenance. | Current release evidence tracker |
| [v2.4.0 Release Notes](RELEASE_NOTES_v2.4.0.md) | Downloads, transition/protocol changes, compatibility, trust status, limitations, and validation boundary. | Before installing or publishing v2.4.0. | Current release snapshot |
| [v2.5 Workflow & Indexing Quality](WORKFLOW_AND_INDEXING_QUALITY_v2.5.md) | Post-operation reconciliation, scan-depth scheduling, progressive Search coverage, safety, and non-goals. | During v2.5 implementation and review. | Unreleased implementation record |
| [OmniBrille companion handoff](OMNIBRILLE_COMPANION_HANDOFF_v2.5.md) | Optional desktop discovery, one-time current-user handoff pipe, scoped Protocol v1 session, failure lifecycle, and threat model. | During v2.5 integration and security review. | Unreleased additive integration contract |
| [v2.5 Manual Testing](MANUAL_TESTING_v2.5.md) | Separates automated evidence from Windows interactive scrolling and workflow checks. | During v2.5 review. | Unreleased evidence tracker |
| [v2.6 Explainable Smart Tags](EXPLAINABLE_SMART_TAGS_v2.6.md) | Schema-6 authority, taxonomy, evidence fusion, classification, Search/filter, privacy, and progressive-indexing boundaries. | During v2.6 implementation and review. | Unreleased implementation record |
| [v2.6 Manual Testing](MANUAL_TESTING_v2.6.md) | Separates automated evidence from migration, classifier-quality, desktop, accessibility, performance, and native-platform checks. | During v2.6 review. | Unreleased evidence tracker |
| [v2.7 Scalable Faceted Discovery](SCALABLE_FACETED_DISCOVERY_v2.7.md) | Complete-library candidate eligibility, bounded hydration, canonical facets/counts, dynamic Saved Views, extraction, privacy, and non-goals. | During v2.7 implementation and review. | Unreleased implementation record |
| [v2.7 Manual Testing](MANUAL_TESTING_v2.7.md) | Separates automated large-library evidence from desktop, screen-reader, DPI, extraction, and native-platform checks. | During v2.7 review. | Unreleased evidence tracker |
| [v2.8 Guided Workflows](GUIDED_WORKFLOWS_PRODUCT_COHERENCE_v2.8.md) | Durable Home readiness, stable Search-to-Files context, continuous Smart Tag review, organization evidence, privacy, and preserved architecture boundaries. | During v2.8 implementation and review. | Unreleased implementation record |
| [v2.8 Manual Testing](MANUAL_TESTING_v2.8.md) | Separates automated workflow-contract evidence from desktop, optional-tool, DPI, screen-reader, file-operation, and native-platform checks. | During v2.8 review. | Unreleased evidence tracker |
| [v2.9 Reviewed Intelligent Organization](REVIEWED_INTELLIGENT_ORGANIZATION_v2.9.md) | Existing recipe authority, stable-ID selection, trusted tokens, ephemeral preview, action budgeting, privacy/path safety, and Change Plan handoff. | During v2.9 implementation and review. | Unreleased implementation record |
| [v2.9 Manual Testing](MANUAL_TESTING_v2.9.md) | Separates automated recipe/proposal safety evidence from desktop, accessibility, filesystem, partial-failure, and native-platform checks. | During v2.9 review. | Unreleased evidence tracker |
| [v2.3.0 Release Notes](RELEASE_NOTES_v2.3.0.md) | Historical Content Intelligence/local-transcription release snapshot. | When reviewing v2.3.0. | Historical release snapshot |
| [v2.2.0 Release Notes](RELEASE_NOTES_v2.2.0.md) | Historical Media Intelligence/UX changes and validation boundary. | When reviewing the v2.2 milestone. | Historical release snapshot |
| [v2.1.0 Release Notes](RELEASE_NOTES_v2.1.0.md) | Historical Search/AI downloads, changes, and validation boundary. | When reviewing the v2.1 milestone. | Historical release snapshot |
| [v2.0.0 Release Notes](RELEASE_NOTES_v2.0.0.md) | Historical v2.0 downloads, changes, and integration boundary. | When reviewing the v2.0 milestone. | Historical release snapshot |
| [v2.1 Search and AI Quality](SEARCH_AND_AI_QUALITY_v2.1.md) | Ranking, optional Ollama reranking, model states, progress, privacy, recovery workflows, and limits. | When using or changing current Search/AI behavior. | Current feature guide |
| [v2.1 Manual Testing](MANUAL_TESTING_v2.1.md) | Fully unchecked real-host Search, AI, timing, duplicate, notification, Help, packaging, and accessibility scenarios. | During explicit maintainer/community testing. | Current evidence tracker |
| [v2.2 Media Intelligence](MEDIA_INTELLIGENCE_v2.2.md) | Provider architecture, supported media evidence, optional tools, bounds, privacy, Search/Related Files integration, and limitations. | When using or changing v2.2 media behavior. | Current feature guide |
| [v2.2 Manual Testing](MANUAL_TESTING_v2.2.md) | Observed controlled Windows provider/OCR/migration evidence and explicitly unchecked interactive/platform scenarios. | During explicit v2.2 maintainer testing. | Current evidence tracker |
| [v2.3 Content Intelligence](CONTENT_INTELLIGENCE_v2.3.md) | Bounded deterministic concepts/summaries, optional user-managed whisper.cpp, schema 5, Search/Related Files integration, privacy, evaluation decisions, and limitations. | When reviewing or changing v2.3 Content Intelligence. | Current feature guide |
| [v2.3 Manual Testing](MANUAL_TESTING_v2.3.md) | Separate automated, native-provider, interactive, and platform scenarios with no unobserved result claimed. | During v2.3 maintainer/community validation. | Current evidence tracker |
| [v2.0 Native Packaging](RELEASE_PACKAGING_v2.0.md) | Native artifact construction, validation, checksums, signing status, and publication order. | Before building or publishing release artifacts. | Living release procedure |
| [v2.0 Screenshot Checklist](SCREENSHOT_CHECKLIST_v2.0.md) | Privacy-safe real-application capture requirements; intentionally unchecked. | Before adding screenshots to README or documentation. | Pending manual documentation task |
| [Safety and Privacy](SAFETY_AND_PRIVACY.md) | Complete current source-file mutation, AI, watcher, workflow, plugin, storage, diagnostics, Search, recovery, and Undo boundaries. | Before any privacy, persistence, provider, plugin, or file-operation change. | Living and authoritative |
| [v2.0 Security Notes](SECURITY_v2.0.md) | Knowledge Graph trust boundaries, hostile-input/resource defenses, store recovery, and explicit non-claims. | Before graph provider, query, suggestion, diagnostics, or recovery changes. | Current implemented security boundary |
| [Changelog](CHANGELOG.md) | Detailed user-visible changes by historical version. | When release-by-release detail is required. | Cumulative historical record |
| [Documentation Inventory](DOCUMENTATION_INVENTORY.md) | Exhaustive family classification, overlap, retention, consolidation, and known documentation debt. | When adding, moving, superseding, or removing documentation. | Living |

The former [project philosophy](project_philosophy.md) and
[roadmap](roadmap.md) paths are retained as compatibility navigation pages.
Their authoritative content now lives in the root Product Vision and Product
Roadmap.

## Current user documentation

| Document | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Installation](INSTALLATION.md) | Current source-build instructions, historical package boundary, optional dependencies, rename compatibility, update/uninstall, and application data. | Before installing, building, updating, or removing OmniSorSe. | Living |
| [OpenSorSe 1.9 User Guide](USER_GUIDE_v1.9.md) | Inherited v1.9 relationships, Smart Collections, Search context, privacy, and repair. | When using inherited v1.9 workflows. | Current inherited guidance |
| [Relationships and Collections](RELATIONSHIPS_AND_COLLECTIONS_v1.9.md) | Evidence/confidence, virtual collections, user control, Search context, privacy, and limits. | Before relying on or changing relationship behavior. | Current feature guide |
| [OpenSorSe 2.0 Knowledge Graph](KNOWLEDGE_GRAPH_v2.0.md) | Graph scope, consent, storage, lifecycle, browsing/Search, privacy, repair, limits, and deferred work. | When using, testing, or changing Knowledge Graph. | Current feature guide |
| [OpenSorSe 2.0 Version Notes](VERSION_NOTES_v2.0.md) | Concise changes, compatibility, defaults, and limits. | For the v2.0 overview. | Current version snapshot |
| [OpenSorSe 1.8 Troubleshooting](TROUBLESHOOTING_v1.8.md) | Inherited Search/index failure, coverage, privacy, repair, and diagnostic guidance. | When Search or indexing is unclear or fails. | Current inherited guidance |
| [OpenSorSe 1.9 Manual Testing](MANUAL_TESTING_v1.9.md) | Interactive relationship/collection scenarios, intentionally unchecked until observed. | During maintainer manual validation. | Current version-specific evidence template |
| [OpenSorSe 1.9 Version Notes](VERSION_NOTES_v1.9.md) | Concise v1.9 user-visible changes and limits. | For the v1.9 milestone overview. | Current version snapshot |
| [v1.9 Implementation Report](V1.9_IMPLEMENTATION_REPORT.md) | What v1.9 changed and reused. | For implementation evidence, not general onboarding. | Current version snapshot |
| [v1.9 Validation Report](V1.9_VALIDATION_REPORT.md) | Exact automated evidence and explicit manual exclusions. | Before citing v1.9 validation totals. | Current version snapshot |
| [v2.0 Implementation Report](V2.0_IMPLEMENTATION_REPORT.md) | Architecture and compatibility implemented by the candidate. | For v2.0 implementation evidence. | Current candidate snapshot |
| [v2.0 Validation Report](V2.0_VALIDATION_REPORT.md) | Exact automated, native-target, packaging, and explicit manual-exclusion evidence. | Before citing v2.0 validation. | Current release evidence |
| [Platform Compatibility Matrix](PLATFORM_COMPATIBILITY_MATRIX.md) | Current support vocabulary and Windows/Linux/macOS capability evidence. | Before making a platform claim. | Living |
| [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md) | Linux source validation, run, and framework-dependent publish steps. | For Linux source work. | Living, conservative preview guidance |

The v1.9 guide builds on stable earlier workflows. Use the versioned v1.1-v1.8
guides only when the current guide links to an inherited subsystem or when
researching that release.

## Current architecture

| Document | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Architecture Overview](ARCHITECTURE_OVERVIEW.md) | Current component ownership, flows, persistence, safety invariants, concurrency, and debt. | Start of any architecture investigation. | Living and authoritative |
| [System Map](Architecture/OpenSorSe_System_Map.md) | Five Mermaid views of adapters, communication, processing, safe execution, and plugins. | When relationships are easier to understand visually. | Living and authoritative |
| [Repository Structure](REPOSITORY_STRUCTURE.md) | Actual project reference graph, project responsibilities, tests, and change locations. | Before selecting a project to edit. | Living and authoritative |
| [Architecture Library Index](Architecture/README.md) | Which detailed subsystem documents are current versus historical/long-term design. | Before reading any detailed architecture file. | Living |
| [v1.7 Deep Indexing Architecture](Architecture/00_System/10_v1.7_Deep_Indexing_Architecture.md) | Provider-neutral durable stages, SQLite boundary, recovery, identity, quota, and coverage. | For indexing/provider/storage work. | Current subsystem contract inherited by v1.9 |
| [v1.8 Search Intelligence and Privacy Architecture](Architecture/06_Search/09_v1.8_Search_Intelligence_Privacy.md) | Ranking, filters, snippets, concurrency, schema 2, privacy, and repair. | For Search and index-privacy work. | Current subsystem contract inherited by v1.9 |
| [v2.1 Search and AI Quality](SEARCH_AND_AI_QUALITY_v2.1.md) | Filename ranking, optional bounded Ollama ordering, model states, indexing clarity, result commands, privacy, fallback, and limitations. | For current v2.1 behavior and user guidance. | Current released subsystem |
| [v2.1 Search and AI Quality Architecture](Architecture/06_Search/12_v2.1_Search_AI_Quality.md) | Deterministic authority, provider boundaries, bounded reranking protocol, cancellation, diagnostics, and compatibility. | For current Search or Ollama implementation work. | Current released subsystem architecture |
| [v2.2 Media Intelligence](MEDIA_INTELLIGENCE_v2.2.md) | Provider-neutral extraction, schema-4 media evidence, bounded optional tools, Search, relationships, privacy, and diagnostics. | For media/index provider work. | Current released subsystem |
| [v2.3 Content Intelligence](CONTENT_INTELLIGENCE_v2.3.md) | Provider-neutral bounded concepts, extractive summaries, optional local whisper.cpp process boundary, schema 5, ranking, relationships, privacy, and failure isolation. | For current v2.3 content/provider/index work. | Current released subsystem |
| [v1.9 Relationships and Context Architecture](Architecture/06_Search/10_v1.9_Relationships_Context.md) | Evidence, confidence, incremental discovery, schema 3, virtual collections, Search context, privacy, and graph bounds. | For current relationship/collection work. | Current subsystem contract |
| [Advanced Diagnostics](Architecture/01_Core/10_Advanced_Diagnostics.md) | Current detailed diagnostics model and privacy. | For instrumentation/export changes. | Current subsystem contract |
| [OCR and Metadata](Architecture/03_Readers/10_v1_OCR_and_Metadata.md) | Implemented extraction/OCR capability and bounds. | For extraction or OCR work. | Current subsystem contract |
| [Small-model Prompt Contracts](Architecture/04_AI/11_Small_Model_Prompt_Contracts.md) | Implemented prompt/structured-output rules. | For optional AI changes. | Current subsystem contract |
| [Change Plans and Operation Journal](Architecture/07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md) | Current supported mutation/recovery boundary. | Before any organization or filesystem action change. | Current subsystem contract |
| [Watched Folders](Architecture/02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md) | Current hint/reconciliation/incremental-processing boundary. | For watcher work. | Current subsystem contract |
| [Workflow Profiles and Recipes](Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md) | Current workflow/recipe policy and Change Plan integration. | For workflow changes. | Current subsystem contract |
| [Plugin Foundation](Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md) | Current plugin host/SDK/package/trust boundary. | For host or extension changes. | Current subsystem contract |

[Implementation specification 059](Implementation_Spec/v1.7/059_Deep_Indexing_Foundation.md)
and [implementation specification 060](Implementation_Spec/v1.8/060_Search_Intelligence_Quality_and_Privacy.md),
plus [implementation specification 061](Implementation_Spec/v1.9/061_Relationships_Context_and_Smart_Collections.md),
are release-specific implementation records. Use the architecture documents
above for the living subsystem model.

## v2.0 release and design authority

The design package remains the rationale and acceptance authority for the
v2.0 implementation. Source and tests define what is actually implemented;
unchecked manual or historical RC gates remain incomplete.

| Document | What it contains | Status |
| --- | --- | --- |
| [v2.0 Knowledge Graph guide](KNOWLEDGE_GRAPH_v2.0.md) | Implemented stable scope, storage, projection/recovery, Search, privacy, repair, bounds, and deferred work. | Current feature guide |
| [v2.0 Knowledge Graph stability architecture](Architecture/06_Search/11_v2.0_Knowledge_Graph_Stability_Design.md) | Isolated graph/decision stores, conservative scope, identity, projection, states, bounds, privacy, and RC policy. | Design authority implemented by v2.0 |
| [v2.0 specification package](Implementation_Spec/v2.0/00_v2.0_Knowledge_Graph_Stability_Proposal.md) | Failure, migration, recovery, concurrency, integrity, and automated-test acceptance plans. | Accepted implementation authority |
| [v2.0 compatibility matrix](V2.0_COMPATIBILITY_MATRIX.md) | v1.7/v1.8/v1.9 upgrade and rollback requirements. | Current compatibility boundary |
| [v2.0 release-readiness checklist](RELEASE_READINESS_v2.0.md) | Historical implementation, validation, RC, and release gates. | Unchecked evidence template; no unobserved completion claimed |
| [v2.0 manual checklist](MANUAL_TESTING_v2.0.md) | Interactive migration, recovery, graph, Search, privacy, resource, accessibility, and regression scenarios. | Fully unchecked |
| [v2.0 RC stabilization plan](V2.0_RC_STABILIZATION_PLAN.md) | Structured soak, fault, upgrade/rollback, privacy, platform, accessibility, and exit gates. | Follow-up/community validation guide; no completion claimed |

These records do not prove validation that has not been run and do not
supersede v1.9 authority for existing relationships, Collections, privacy, or
schema-3 persistence.

## Contributors and maintainers

| Document | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Contributing](../CONTRIBUTING.md) | Prerequisites, repository layout, MVVM, safety, tests, documentation, branches, manual validation, release and review expectations. | Before making a contribution. | Living |
| [Developer Guide](DEVELOPER_GUIDE.md) | Guided clone/build/test flow and traces through Scan, Search, Change Plans, and plugins. | For a first code change or subsystem trace. | Living |
| [Maintainer Guide](MAINTAINER_GUIDE.md) | Release gates, version metadata, migrations, safety, journal/plugin compatibility, and documentation maintenance. | Before integration or release work. | Living |
| [Coding Standards](Architecture/99_Appendix/Coding_Standards.md) | Code readability and implementation-level conventions. | During implementation/review. | Living companion to Engineering Principles |
| [Naming Conventions](Architecture/99_Appendix/Naming_Conventions.md) | General naming guidance. | When introducing public/domain vocabulary. | Living guidance; actual source/domain terms take precedence |
| [Technology Stack](Architecture/99_Appendix/Technology_Stack.md) | Current runtime, UI, persistence, Search, OCR, AI, plugin, test, and documentation technology. | Before adding or describing a dependency. | Living |
| [Glossary](Architecture/99_Appendix/Glossary.md) | Current shared product and architecture terminology. | When terminology is ambiguous. | Living and authoritative for vocabulary |
| [ADR Index](Architecture/99_Appendix/ADR.md) | Accepted ADRs and decision-record policy. | Before revisiting an accepted decision or proposing a new one. | Living index; accepted ADRs are immutable records |
| [FOSS Dependency Policy](FOSS_DEPENDENCY_POLICY.md) | License/inventory rules and optional component policy. | Before adding or distributing a dependency. | Living |

## Plugin authors

| Document | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Extension SDK](EXTENSION_SDK_v1.4.md) | Public contracts, extension points, capabilities, bounds, lifetime, and safety. | First plugin-author document. | Current stable SDK contract introduced in v1.4 |
| [Plugin Author Guide](PLUGIN_AUTHOR_GUIDE_v1.4.md) | Minimal plugin example, reliability, security, privacy, and versioning. | When implementing a plugin. | Current stable guide |
| [Manifest Reference](PLUGIN_MANIFEST_REFERENCE_v1.4.md) | Strict schema, fields, extension points, capabilities, paths, and dependencies. | When creating `plugin.json`. | Current stable schema plus v1.5 addendum |
| [Local Plugin Packages](LOCAL_PLUGIN_PACKAGES_v1.4.md) | ZIP layout, install/upgrade/rollback/removal, and trust. | When packaging or installing a plugin. | Current stable package contract |
| [Plugin Platform Compatibility](PLUGIN_PLATFORM_COMPATIBILITY_v1.5.md) | Runtime identifier/native-dependency additions. | For cross-platform plugins. | Current addendum |
| [Workflow Portability](WORKFLOW_PORTABILITY_v1.5.md) | Filename/platform policy in portable workflows. | For workflow/plugin portability. | Current addendum |
| [Watched Folders on Linux](WATCHED_FOLDERS_LINUX_v1.5.md) | Linux watcher limits and reconciliation. | For Linux watcher integrations. | Current addendum |

External plugins run in-process with the current user’s permissions.
Load-context isolation and hashes are not a security sandbox or publisher
authentication.

## Release and implementation records

| Document/family | What it contains | When to read it | Status |
| --- | --- | --- | --- |
| [Release History](../RELEASE_HISTORY.md) | Concise complete milestone index. | Start of historical research. | Living historical index |
| [Changelog](CHANGELOG.md) | Detailed cumulative changes. | User-visible version detail. | Historical record |
| [Implementation Specification Index](Implementation_Spec/README.md) | Numbered specifications, proposals, decisions, and acceptance boundaries. | Implementation archaeology and release-specific rationale. | Living index over historical records |
| `VERSION_NOTES_v*.md` | User-facing change summary for one version. | When researching that version. | Historical/version snapshots; v2.0 is a candidate snapshot |
| `MANUAL_TESTING_v*.md` | Interactive checklist and observed/unobserved state for one version. | Manual validation or evidence review. | Historical/version snapshots |
| `USER_GUIDE_v*.md` and `TROUBLESHOOTING_v*.md` | User behavior and support at that version. | Compatibility and historical UX research. | Historical except current v1.9/inherited guidance |
| `V*.*_IMPLEMENTATION_REPORT.md` | What a major branch implemented. | Detailed implementation evidence. | Historical/candidate snapshots; preserve |
| `V*.*_VALIDATION_REPORT.md` | Exact local/hosted/manual evidence and exclusions. | Before quoting validation. | Historical/pending candidate records; preserve |
| `DATA_MODEL_v1.0.md`, `MIGRATION_v1.0.md`, `RELEASE_CHECKLIST_v1.0.md` | v1.0 data, migration, and packaging assumptions. | v1.0 compatibility or package research. | Historical snapshots |

## Historical architecture and packaged release

Most unversioned detailed files below `Architecture/01_Core` through
`Architecture/10_Plugins` were original design-library documents. Some describe
broad readers, reports, databases, generic services, or plugin models that are
not current implementation. The [Architecture Library Index](Architecture/README.md)
classifies the current exceptions. Treat all others as historical/long-term
design.

The entire `release/OpenSorSe-v1.0.0/` tree is a frozen distribution snapshot:
package README, release notes, changelog, documentation copies, license files,
and binaries describe that package only. Do not update it to match current
source. Intentional exact copies inside that tree are package contents, not
living-document duplicates.

## Documentation maintenance

- Update living documents when current behavior or policy changes.
- Add or update a version snapshot only as part of that version’s work.
- Do not rewrite old implementation, validation, release, or manual evidence to
  look current.
- Link to authoritative detail instead of copying it.
- Label planned and research material explicitly.
- Run documentation link/Mermaid tests and `git diff --check` after changes.
- Consult the [Documentation Inventory](DOCUMENTATION_INVENTORY.md) before
  removing or consolidating a path.
