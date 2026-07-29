# Documentation inventory

This inventory records the v1.4 pre-commit documentation audit and subsequent
cross-platform, reliability, and deep-indexing addenda. The numerical inventory
below is historical v1.4 evidence; current link/structure tests enumerate the live tree instead of
pretending those counts stayed fixed. Source
documentation and the checked-in v1.0 distribution snapshot are counted
separately so packaged copies do not masquerade as independent current
documents. The audit read metadata and content from 237 source
documentation/diagram/image candidates:

- 234 Markdown files;
- three PNG files;
- 231 Markdown files under `docs`;
- 117 architecture Markdown files;
- 80 implementation-specification Markdown files;
- one root README, one third-party notice, and one misplaced script document;
- one documentation logo plus two Desktop product assets.

The repository also contains 211 documentation-like files inside the immutable
`release/OpenSorSe-v1.0.0` distribution snapshot: 210 Markdown files and one
logo copy. They were inventoried as packaged release contents, remain
historical v1.0 evidence, and were not edited or counted as source documents.

Classification rules below are exhaustive by path family. Exceptions are
listed explicitly, so every audited candidate has an authority classification
without pretending that historical plans are current behavior.

## Audit outcome

- 236 of the 237 initial source candidates were retained.
- One obsolete source document was removed with its rationale recorded below.
- Nine current source documents were added.
- The final source inventory contains 245 candidates: 242 Markdown files and
  three PNG files.
- All 211 packaged v1.0 documentation-like files remain unchanged.
- There are no exact-content duplicate groups within the source inventory.
  Copies inside the versioned v1.0 package are intentional distribution
  contents, not consolidation candidates.

## Authoritative current documentation

The following documents describe the current repository or current cross-
version policy:

- `README.md`
- `CONTRIBUTING.md`
- `THIRD_PARTY_NOTICES.md`
- `docs/README.md`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/REPOSITORY_STRUCTURE.md`
- `docs/DEVELOPER_GUIDE.md`
- `docs/MAINTAINER_GUIDE.md`
- `docs/DOCUMENTATION_INVENTORY.md`
- `docs/INSTALLATION.md`
- `docs/SAFETY_AND_PRIVACY.md`
- `docs/FOSS_DEPENDENCY_POLICY.md`
- `docs/project_philosophy.md`
- `docs/roadmap.md`
- `docs/RELEASE_STATUS.md`
- `docs/CHANGELOG.md`
- `docs/Architecture/README.md`
- `docs/Architecture/OpenSorSe_System_Map.md`
- `docs/Architecture/00_System/00_Overview.md`
- `docs/Architecture/99_Appendix/Coding_Standards.md`
- `docs/Architecture/99_Appendix/Naming_Conventions.md`
- `docs/Architecture/99_Appendix/Glossary.md`
- `docs/Architecture/99_Appendix/Technology_Stack.md`
- `docs/Architecture/99_Appendix/ADR.md`
- `docs/Architecture/99_Appendix/ADR-001_Optional_Ollama_Suggestions.md`
- `docs/Architecture/99_Appendix/ADR-002_Bounded_Saved_Query_Persistence.md`
- `docs/Architecture/99_Appendix/ADR-003_Historical_Metadata_Comparison.md`
- `docs/Implementation_Spec/README.md`
- `docs/images/README.md`
- `docs/images/opensorse-logo.png`

`docs/Architecture/Template.md` and
`docs/Implementation_Spec/Template.md` are current contributor templates, not
product architecture.

## Current version-specific documentation

These files are authoritative for v1.8 within their scope:

- `docs/USER_GUIDE_v1.8.md`
- `docs/TROUBLESHOOTING_v1.8.md`
- `docs/MANUAL_TESTING_v1.8.md`
- `docs/VERSION_NOTES_v1.8.md`
- `docs/V1.8_IMPLEMENTATION_REPORT.md`
- `docs/V1.8_VALIDATION_REPORT.md`
- `docs/Architecture/06_Search/09_v1.8_Search_Intelligence_Privacy.md`
- `docs/Implementation_Spec/v1.8/060_Search_Intelligence_Quality_and_Privacy.md`

The v1.7 deep-indexing documents remain authoritative for the durable pipeline
foundation reused by v1.8:

- `docs/Architecture/00_System/10_v1.7_Deep_Indexing_Architecture.md`
- `docs/Implementation_Spec/v1.7/059_Deep_Indexing_Foundation.md`

The v1.6 reliability and v1.5 platform documents remain authoritative for
their stable subsystem boundaries:

- `docs/USER_GUIDE_v1.5.md`
- `docs/TROUBLESHOOTING_v1.5.md`
- `docs/MANUAL_TESTING_v1.5.md`
- `docs/VERSION_NOTES_v1.5.md`
- `docs/LINUX_BUILD_AND_LAUNCH.md`
- `docs/PLATFORM_COMPATIBILITY_MATRIX.md`
- `docs/PLUGIN_PLATFORM_COMPATIBILITY_v1.5.md`
- `docs/WORKFLOW_PORTABILITY_v1.5.md`
- `docs/WATCHED_FOLDERS_LINUX_v1.5.md`
- `docs/Architecture/00_System/08_v1.5_Platform_Architecture.md`
- `docs/Implementation_Spec/v1.5/057_Cross_Platform_Foundation_and_Linux_Preview.md`

The v1.4 SDK and plugin documents remain authoritative for their stable
contract and are extended, not replaced, by the v1.5 platform constraints:

- `docs/USER_GUIDE_v1.4.md`
- `docs/TROUBLESHOOTING_v1.4.md`
- `docs/MANUAL_TESTING_v1.4.md`
- `docs/VERSION_NOTES_v1.4.md`
- `docs/EXTENSION_SDK_v1.4.md`
- `docs/PLUGIN_AUTHOR_GUIDE_v1.4.md`
- `docs/PLUGIN_MANIFEST_REFERENCE_v1.4.md`
- `docs/LOCAL_PLUGIN_PACKAGES_v1.4.md`
- `docs/Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md`
- `docs/Implementation_Spec/v1.4/056_Plugin_Foundation_and_Extension_SDK.md`

Current subsystem references introduced by earlier releases remain
authoritative for the feature they describe:

- `Architecture/01_Core/10_Advanced_Diagnostics.md`
- `Architecture/02_Scanner/09_v1.2_Watched_Folders_and_Incremental_Scanning.md`
- `Architecture/03_Readers/10_v1_OCR_and_Metadata.md`
- `Architecture/04_AI/11_Small_Model_Prompt_Contracts.md`
- `Architecture/05_Database/09_v1_Local_Content_Stores_and_Migrations.md`
- `Architecture/06_Search/07_v1_Semantic_Index.md`
- `Architecture/06_Search/08_v1_Tag_Provenance.md`
- `Architecture/07-Rules/06_Restructuring_History.md`
- `Architecture/07-Rules/07_v1.1_Change_Plans_and_Operation_Journal.md`
- `Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md`
- `Architecture/08_Gui/04_Results_Page.md`
- `Architecture/08_Gui/11_Catalog_Page.md`
- `Architecture/08_Gui/12_Catalog_Comparison_Page.md`
- `Architecture/08_Gui/13_Structure_History.md`
- `Architecture/08_Gui/14_Review_Changes.md`

## Historical but intentionally retained

Every file matching these families is retained release history:

- `docs/USER_GUIDE_v1.1.md` through `USER_GUIDE_v1.3.md`
- `docs/TROUBLESHOOTING_v1.1.md` through `TROUBLESHOOTING_v1.3.md`
- `docs/MANUAL_TESTING_v0.9.1.md` through `MANUAL_TESTING_v1.3.md`
- `docs/VERSION_NOTES_v1.0.md` through `VERSION_NOTES_v1.3.md`
- `docs/DATA_MODEL_v1.0.md`
- `docs/MIGRATION_v1.0.md`
- `docs/RELEASE_CHECKLIST_v1.0.md`
- all numbered `docs/Implementation_Spec/001_*.md` through `028_*.md`
- `docs/Implementation_Spec/AUTONOMOUS_DECISIONS.md`
- `docs/Implementation_Spec/IMPLEMENTATION_PROGRESS_REMOTE.md`
- every file under `docs/Implementation_Spec/v0.2` through `v1.3`

These files preserve proposals, decisions, migration assumptions, validation
evidence, and compatibility rationale. Old branch names, test totals, blocked
environment notes, and release claims are valid only in their dated historical
context.

## Long-term design, superseded detail, and consolidation candidates

Unless a file is listed as current above, documents under these directories are
classified as historical/long-term design:

- `docs/Architecture/00_System`
- `docs/Architecture/01_Core`
- `docs/Architecture/02_Scanner`
- `docs/Architecture/03_Readers`
- `docs/Architecture/04_AI`
- `docs/Architecture/05_Database`
- `docs/Architecture/06_Search`
- `docs/Architecture/07-Rules`
- `docs/Architecture/08_Gui`
- `docs/Architecture/09_Reports`
- `docs/Architecture/10_Plugins`

They remain useful as design vocabulary and future candidates, but many
describe relational storage, broad readers, reporting, online plugin services,
generic service registries, or UI surfaces not implemented in v1.4. The new
Architecture Overview consolidates current behavior; the architecture index
prevents these older documents from being mistaken for shipped functionality.
No mass deletion is justified.

`docs/Architecture/00_System/03_Component_Map.md`,
`04_Data_Flow.md`, and `06_User_Flow.md` are superseded high-level views with
historical value. The current System Map replaces them as the recommended
visual entry point.

## Product assets, not documentation

- `src/OpenSorSe.Desktop/Assets/opensorse-app-icon.png`
- `src/OpenSorSe.Desktop/Assets/opensorse-logo.png`

They were included by the extension-based inventory but are runtime UI assets,
not generated documentation.

## Removed

- `scripts/001_FileScanner.md` — removed during this pass. Despite its Markdown
  extension, it contained an abandoned, uncompilable C# sketch with incorrect
  cancellation, duration, error, recursion, and model behavior. It was
  unreferenced, not a script, and its name conflicted with the real historical
  specification at `docs/Implementation_Spec/001_FileScanner.md`. Git history
  preserves it; no useful current documentation was lost.

No other document was removed merely because it was old.

## Misleading or incomplete documents corrected

- `docs/INSTALLATION.md` described only the v1.0 portable package while the
  root README linked it as current. It now distinguishes the published v1.0
  package from building/running v1.4 source and makes no v1.4 release claim.
- `docs/SAFETY_AND_PRIVACY.md` had a v1.3 title and opening even though its body
  contained v1.4 plugin material. Its current scope and workflow store are now
  explicit.
- The root README lacked a central user/contributor/maintainer documentation
  route and direct current architecture map; those links are now present.
- Missing screenshot paths in HTML comments remain intentional placeholders,
  not active broken links. The documentation validator ignores HTML comments.
- Thirty-one legacy architecture links relied on Windows case-insensitive path
  resolution. Their target casing now exactly matches repository filenames so
  the links work on GitHub and case-sensitive checkouts.
- Four personal-looking example paths in historical UI specifications were
  normalized to environment/example notation.

## Generated and temporary documentation

No generated, temporary, machine-specific, or accidentally exported
documentation was found. Build outputs under ignored `bin`, `obj`, and
`.artifacts` directories are not documentation sources.

The checked-in `release/OpenSorSe-v1.0.0` tree is a versioned distribution
artifact rather than generated working output. Its documentation is frozen
with that package and must not be updated to describe v1.4.

## Open documentation debt

- Several long-term architecture documents are verbose and partly superseded.
  Consolidating them should be a deliberate future documentation project with
  historical review, not a bulk deletion.
- Real current screenshots are not present. The root README intentionally keeps
  non-rendered placeholders until verified captures exist.
- The v1.0 packaging checklist is the latest packaged-release checklist. A
  dedicated v1.4 packaging checklist should be created only when packaging and
  manual verification are authorized.
- Mermaid validation is structural in repository tests. Pixel/render review
  still depends on GitHub or a Mermaid renderer.
