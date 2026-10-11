# Documentation consolidation — 2026-10-11

## Outcome and authority

One canonical document per subject replaces version-suffixed living files.
`docs/CHANGELOG.md` owns cumulative user-visible changes; `RELEASE_HISTORY.md`
retains only technical branch/integration chronology. `RELEASE_STATUS.md`
continues to own readiness. No application behavior or recorded test result is
changed by this migration.

## Migration and retained evidence

The [migration register](../documentation-migration.tsv) records all 617 original
Markdown paths, each destination/anchor, disposition and retention reason.
127 paths are consolidated into 55 subject destinations. The register is
retained as migration evidence so every deletion remains independently traceable.
The [policy](../../DOCUMENTATION_POLICY.md) specifies explicit archival exceptions.
Frozen package bytes and accepted specification identities remain intact.

Historical manual checkboxes, v1.6 maintainer attestation without environment
specifics, mixed automated/native evidence, failed OCR installation, native
protocol defects, platform exclusions and v3's 24 Not run rows are preservation
gates. Exact repeated platform checks share definitions with per-version
checkbox status retained. Repeated evidence-policy text shares an anchor.

## External references

Authenticated GitHub inventory covered release bodies, all open/closed issue
and PR bodies, issue comments and PR review comments. Thirty absolute links to
removed paths point to surviving immutable tags/commits and stay unchanged.
Nine relative links in five older release bodies were corrected to canonical
document/section URLs pinned to pushed consolidation commit
`74aab129c366dfcb4f1f43ddc8cfcf0cf5a08dcd`.
Each updated body was reread and compared with the exact expected substitutions;
asset IDs/names/sizes/digests/URLs, tags, target commits, draft/prerelease flags
and publication timestamps were unchanged. No compatibility stubs are needed.
No release was published and no issue result was altered.

## OmniLAB and independent review

The existing verified frozen LocalAgentBridge runtime was invoked over stdio
from outside its development checkout, with the existing configuration and
selected `gpt-oss:20b` model. Source access remained bounded inline because the
configured source root covers the bridge checkout, not OmniSorSe. No personal
configuration or model was changed. Installed metadata reports 0.4.1; capability
use followed the exposed seven-tool interface rather than newer development docs.

- The first inventory packet exceeded the configured 10,000-character limit;
  it was split before inference.
- Request `70208506-a9a1-4a90-9e3b-788416d99de6` reviewed inventory/grouping and
  link counts. Its generic inference that absent checkboxes imply evidence gaps
  was rejected; explicit source statuses and boundaries take precedence.
- Request `5ce122fc-70ac-498d-8ab0-47f7c58434e9` reviewed repetition and link risks.
  Its advice to preserve distinct validation evidence and check inbound links
  was accepted after source inspection. Package-copy repetition is intentional;
  the response did not establish a complete similarity or reference map.
- Codex therefore completed grouping and the exact reference map with exhaustive
  local inventory and source checks; this is a justified fallback for incomplete
  local output, not claimed OmniLAB authorship.
- A separate Codex Documentation/adversarial reviewer independently identified
  historical evidence preservation gates from the original files.

## Validation and repository state

Baseline: clean `OmniSorSe-v3` checkout, actual remote `nishdel/OmniSorSe`,
latest main `783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f` after fetch.
Branch: `docs/consolidate-versioned-documentation`. The dirty parent OpenSorSe
checkout and other worktrees were not modified.

Verified with the existing local SDK 10.0.400:

- `dotnet test tests/OpenSorSe.Core.Tests/OpenSorSe.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RepositoryDocumentationTests`: **15 passed, 0 failed, 0 skipped**. Includes strict UTF-8, relative paths/casing, Mermaid structure, canonical routes, packaging references and the new filename guard.
- The guard first rejected the still-present superseded files, then passed after their reviewed removal. No dependency was added. A pre-delete Mermaid failure exposed CRLF writing; normalization to repository LF resolved it without changing diagrams.
- `dotnet format whitespace tests/OpenSorSe.Core.Tests/OpenSorSe.Core.Tests.csproj --verify-no-changes --no-restore --include tests/OpenSorSe.Core.Tests/RepositoryDocumentationTests.cs`: passed.
- `git diff --check`: passed.
- PowerShell AST parsing of `eng/release/Build-WindowsArtifacts.ps1` and `bash -n eng/release/Build-MacArtifacts.sh`: passed. Both package the cumulative changelog as `RELEASE_NOTES.md` from the exact source being built; no release was built or published here.
- Independent source-block parity covered every migrated document. Two deliberate shared-procedure wording changes replace obsolete sibling-file retention and make inherited checkbox IDs section-relative; neither changes scenarios or results.
- All 26 manual-version checkbox status sequences match the originals; the 24 v3 human rows remain `No / Not run`. The v1.0 model matrix retains 21 Not run rows. v1.6 attestation retains its missing environment detail. v2.6/v2.7 summaries explicitly claim automated evidence only.
- Every deleted file appears in the register. No active internal reference names a deleted file; intentional Git/tag URLs remain unchanged. Active relative Markdown paths and fragments resolve, with no duplicate explicit anchors.
- Local bundled `marked` GFM rendering and headless Chromium checks passed: no malformed source table cardinalities, no rendered row-width mismatch or viewport overflow; collapsible sections balance. Manual, changelog, validation and index previews were inspected. No diagram semantics changed.

The remote Markdown-rendering upload was rejected by automatic approval review
because sending whole document payloads could disclose content. Rendering was
completed locally instead. The UI browser helper also failed to initialize;
the already-installed local headless renderer supplied independent HTML/layout
checks. Neither limitation blocked the implementation.

Product suites were not run for this documentation-only change under the risk
matrix. No new product, native-runtime, package or human observation is claimed.

## Independent findings and disposition

The separate Codex reviewer compared original source snapshots and final files,
not only the implementation summary, and approved after these corrections:

1. Fold duplicate v2.10/v2.11 release notes and duplicated milestone summaries
   into the matching changelog sections. The result has 35 distinct version
   headings, newest first, with original source aliases retained.
2. State that v2.6/v2.7 checked rows are automated evidence only; native and
   interactive checks remain unperformed.
3. Replace obsolete “keep versioned files unchanged” wording with historical
   evidence/Git preservation; document section-relative legacy test IDs.
4. Repair inline-code path prefixes as well as clickable Markdown links.

OmniLAB final manual review request
`8a83ff45-5d0b-4dd6-b542-ae8a89845c73` failed with
`handoff_to_codex=true`, `reason=no_enabled_capable_model`, after a failed
`gpt-oss:20b` response. Final structure review
`c11c1d42-88a4-4635-95cf-b17f104b7f76` mistook bounded excerpt cutoffs for
actual truncated files and treated permitted version headings as forbidden
versioned filenames. Full-file link, render and policy checks refuted those
findings. Its one permitted corrected follow-up retained the logical task and
also returned `no_enabled_capable_model`; no model or budget was substituted.
Codex therefore performed final full-source review and verification. Feedback
recorded the inventory result as rejected, repetition result as corrected, and
structure result as rejected. No successful exhaustive OmniLAB final review is
claimed.

## Frozen-package link exceptions

The 10 links below were already broken in the distributed v1.0 tree. Their
source files are byte-unchanged. They remain archival defects; there are no new
active-document link defects and no external moving-link blockers.

| Frozen source (under `release/OpenSorSe-v1.0.0/`) | Original missing target |
| --- | --- |
| `CHANGELOG.md` | `Implementation_Spec/v0.9/00_v0.9_Release_Proposal.md` |
| `CHANGELOG.md` | `Implementation_Spec/v0.9/AUDIT_CORRECTIONS.md` |
| `README.md` | `docs/images/home.png` |
| `README.md` | `docs/images/files.png` |
| `README.md` | `docs/images/duplicate-detective.png` |
| `README.md` | `docs/images/file-assistant.png` |
| `README.md` | `docs/images/meaning-search.png` |
| `README.md` | `docs/images/settings.png` |
| `README.md` | `global.json` |
| `docs/Architecture/99_Appendix/Technology_Stack.md` | `../../../global.json` |

The authenticated audit found 30 absolute migrated-document URLs pinned to
surviving tags/commits, plus nine relative links in v2.0.0 through v2.4.0 release
bodies. The correction changed only those nine destinations to the canonical documents
at `74aab129c366dfcb4f1f43ddc8cfcf0cf5a08dcd` and verified every resulting body.
All five updates succeeded; no external link remains blocked and no compatibility
stub is retained.

## Canonical destinations

The register is the complete old-path → canonical document → anchor → disposition
map and the complete deletion/archive list. It classifies 127 merged/deleted
files, 56 distinct living documents/routers, and 434 existing archival records
in place. “Archived” does not mean another archive copy was created. Each
archive row carries its own preservation reason; current guidance does not
route readers through historical files by default.


| Canonical destination | Migrated source paths |
| --- | ---: |
| [docs/ACCEPTANCE_CRITERIA.md](../../ACCEPTANCE_CRITERIA.md) | 1 |
| [docs/Architecture/00_System/08_Platform_Architecture.md](../../Architecture/00_System/08_Platform_Architecture.md) | 1 |
| [docs/Architecture/00_System/09_Reliability_Architecture.md](../../Architecture/00_System/09_Reliability_Architecture.md) | 1 |
| [docs/Architecture/00_System/10_Deep_Indexing_Architecture.md](../../Architecture/00_System/10_Deep_Indexing_Architecture.md) | 1 |
| [docs/Architecture/02_Scanner/09_Watched_Folders_and_Incremental_Scanning.md](../../Architecture/02_Scanner/09_Watched_Folders_and_Incremental_Scanning.md) | 1 |
| [docs/Architecture/03_Readers/10_OCR_and_Metadata.md](../../Architecture/03_Readers/10_OCR_and_Metadata.md) | 1 |
| [docs/Architecture/05_Database/09_Local_Content_Stores_and_Migrations.md](../../Architecture/05_Database/09_Local_Content_Stores_and_Migrations.md) | 1 |
| [docs/Architecture/06_Search/07_Semantic_Index.md](../../Architecture/06_Search/07_Semantic_Index.md) | 1 |
| [docs/Architecture/06_Search/08_Tag_Provenance.md](../../Architecture/06_Search/08_Tag_Provenance.md) | 1 |
| [docs/Architecture/06_Search/09_Search_Intelligence_Privacy.md](../../Architecture/06_Search/09_Search_Intelligence_Privacy.md) | 1 |
| [docs/Architecture/06_Search/10_Relationships_Context.md](../../Architecture/06_Search/10_Relationships_Context.md) | 1 |
| [docs/Architecture/06_Search/11_Knowledge_Graph_Stability_Design.md](../../Architecture/06_Search/11_Knowledge_Graph_Stability_Design.md) | 1 |
| [docs/Architecture/06_Search/12_Search_AI_Quality.md](../../Architecture/06_Search/12_Search_AI_Quality.md) | 1 |
| [docs/Architecture/07-Rules/07_Change_Plans_and_Operation_Journal.md](../../Architecture/07-Rules/07_Change_Plans_and_Operation_Journal.md) | 1 |
| [docs/Architecture/07-Rules/08_Workflow_Profiles_and_Recipes.md](../../Architecture/07-Rules/08_Workflow_Profiles_and_Recipes.md) | 1 |
| [docs/Architecture/10_Plugins/06_Plugin_Foundation.md](../../Architecture/10_Plugins/06_Plugin_Foundation.md) | 1 |
| [docs/CHANGELOG.md](../../CHANGELOG.md) | 21 |
| [docs/CONTENT_INTELLIGENCE.md](../../CONTENT_INTELLIGENCE.md) | 1 |
| [docs/DATA_MODEL.md](../../DATA_MODEL.md) | 1 |
| [docs/EXPLAINABLE_SMART_TAGS.md](../../EXPLAINABLE_SMART_TAGS.md) | 1 |
| [docs/EXTENSION_SDK.md](../../EXTENSION_SDK.md) | 1 |
| [docs/GUIDED_WORKFLOWS_PRODUCT_COHERENCE.md](../../GUIDED_WORKFLOWS_PRODUCT_COHERENCE.md) | 1 |
| [docs/HYBRID_SEARCH.md](../../HYBRID_SEARCH.md) | 1 |
| [docs/IMPLEMENTATION_HISTORY.md](../../IMPLEMENTATION_HISTORY.md) | 6 |
| [docs/KNOWLEDGE_GRAPH.md](../../KNOWLEDGE_GRAPH.md) | 1 |
| [docs/KNOWLEDGE_GRAPH_COMPATIBILITY.md](../../KNOWLEDGE_GRAPH_COMPATIBILITY.md) | 1 |
| [docs/LOCAL_PLUGIN_PACKAGES.md](../../LOCAL_PLUGIN_PACKAGES.md) | 1 |
| [docs/MAINTAINER_GUIDE.md](../../MAINTAINER_GUIDE.md) | 2 |
| [docs/MANUAL_TESTING.md](../../MANUAL_TESTING.md) | 27 |
| [docs/MEDIA_INTELLIGENCE.md](../../MEDIA_INTELLIGENCE.md) | 1 |
| [docs/MIGRATION.md](../../MIGRATION.md) | 1 |
| [docs/OMNIBRILLE_COMPANION_HANDOFF.md](../../OMNIBRILLE_COMPANION_HANDOFF.md) | 1 |
| [docs/OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md](../../OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md) | 1 |
| [docs/OPERATIONAL_RUNBOOKS.md](../../OPERATIONAL_RUNBOOKS.md) | 1 |
| [docs/PLUGIN_AUTHOR_GUIDE.md](../../PLUGIN_AUTHOR_GUIDE.md) | 1 |
| [docs/PLUGIN_MANIFEST_REFERENCE.md](../../PLUGIN_MANIFEST_REFERENCE.md) | 1 |
| [docs/PLUGIN_PLATFORM_COMPATIBILITY.md](../../PLUGIN_PLATFORM_COMPATIBILITY.md) | 1 |
| [docs/PRODUCTION_HARDENING.md](../../PRODUCTION_HARDENING.md) | 1 |
| [docs/RELATIONSHIPS_AND_COLLECTIONS.md](../../RELATIONSHIPS_AND_COLLECTIONS.md) | 1 |
| [docs/RELEASE_PACKAGING.md](../../RELEASE_PACKAGING.md) | 1 |
| [docs/RELEASE_STATUS.md](../../RELEASE_STATUS.md) | 1 |
| [docs/REVIEWED_INTELLIGENT_ORGANIZATION.md](../../REVIEWED_INTELLIGENT_ORGANIZATION.md) | 1 |
| [docs/SCALABLE_FACETED_DISCOVERY.md](../../SCALABLE_FACETED_DISCOVERY.md) | 1 |
| [docs/SEARCH_AND_AI_QUALITY.md](../../SEARCH_AND_AI_QUALITY.md) | 1 |
| [docs/SECURITY.md](../../SECURITY.md) | 1 |
| [docs/STORAGE_MANAGEMENT.md](../../STORAGE_MANAGEMENT.md) | 1 |
| [docs/SUPPORTED_RUNTIME_PLATFORM_READINESS.md](../../SUPPORTED_RUNTIME_PLATFORM_READINESS.md) | 1 |
| [docs/TROUBLESHOOTING.md](../../TROUBLESHOOTING.md) | 8 |
| [docs/TRUSTED_RELATIONSHIPS_CONTEXT.md](../../TRUSTED_RELATIONSHIPS_CONTEXT.md) | 1 |
| [docs/USER_GUIDE.md](../../USER_GUIDE.md) | 9 |
| [docs/VALIDATION.md](../../VALIDATION.md) | 6 |
| [docs/WATCHED_FOLDERS_LINUX.md](../../WATCHED_FOLDERS_LINUX.md) | 1 |
| [docs/WORKFLOW_AND_INDEXING_QUALITY.md](../../WORKFLOW_AND_INDEXING_QUALITY.md) | 1 |
| [docs/WORKFLOW_PORTABILITY.md](../../WORKFLOW_PORTABILITY.md) | 1 |
| [docs/images/README.md](../../images/README.md) | 1 |

## Published reference corrections

| Existing release | Corrected documentation links | Result |
| --- | ---: | --- |
| [v2.4.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0) | 2 | Body verified; assets, tag and publication unchanged |
| [v2.3.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.3.0) | 2 | Body verified; assets, tag and publication unchanged |
| [v2.2.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.2.0) | 2 | Body verified; assets, tag and publication unchanged |
| [v2.1.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.1.0) | 1 | Body verified; assets, tag and publication unchanged |
| [v2.0.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.0.0) | 2 | Body verified; assets, tag and publication unchanged |

The complete branch diff, including added files, passes `git diff --check`
after removing four Markdown hard-break spaces found by the staged check.
The branch is pushed for PR review; no merge or release publication is authorized
or performed by this delivery.
