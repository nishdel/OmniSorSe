# Changelog

**Document type:** Cumulative historical change record

Entries below preserve release-specific terminology and claims. Use
[Release History](../RELEASE_HISTORY.md) for the concise branch/date/merge
index, [Release Status](RELEASE_STATUS.md) for current readiness, and
[Product Roadmap](../PRODUCT_ROADMAP.md) for future planning.

## v3.0.0-rc.1 candidate — Progressive understanding and reviewed organization

<details>
<summary>Original milestone summary</summary>

Progressive understanding and reviewed organization. Per-source background AI, retained-content enrichment, inferred Search/relationships, editable Organize, verified storage relocation and protected cleanup.

</details>

At the original implementation checkpoint this candidate was not yet published.
The [official prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
owns subsequent publication state. Historical implementation changes: Adds optional per-folder automatic
local AI enrichment with inference provenance, retained-content requeue and
incremental Search/relationship updates; an editable Organize page with strategies,
explained moves and remembered preferences; and verified-copy application storage
relocation with usage, limits and safe pruning. Preserves reviewed executor/Undo
authority and protects user-authored state during maintenance. See the
[candidate release notes](CHANGELOG.md#release-v3-0-0) for compatibility and limitations.


<a id="release-v3-0-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v3.0.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Release-candidate source; publication is tracked in [Release Status](RELEASE_STATUS.md).** Begin user testing only after the merged v3 code and matching non-draft installer exist. The matching
[GitHub release](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
owns exact package, source commit and checksum evidence.

The milestone adds optional background local-AI enrichment to folder indexing,
per-folder policy and retained-content requeue, automatically searchable inferred
document information with provenance, editable Organize previews with three
strategies and remembered preferences, and application storage location/usage
management. Original document contents and embedded metadata remain untouched.

This candidate also adds [vector-powered hybrid Search](HYBRID_SEARCH.md),
using a separate local Ollama embedding model, traceable chunks, incremental
background processing, model-digest invalidation and reciprocal rank fusion.
Keyword Search remains available without vectors. Related Files labels semantic
similarity separately from factual relationships. Search exposes model/progress,
pause/resume and rebuild; storage accounts for and reclaims disposable vectors.
The opt-in [benchmark](../eng/benchmarks/VectorSearch/README.md) supplies repeatable
synthetic relevance and source-integrity checks with an installed real model.

See [How OmniSorSe Works](HOW_OMNISORSE_WORKS.md), the complete
[38-item acceptance map](ACCEPTANCE_CRITERIA.md), and the
[manual acceptance checklist](MANUAL_TESTING.md#manual-v3-0).

<a id="release-v3-0-0-compatibility-and-recovery"></a>
#### Compatibility and recovery

- Stable OpenSorSe assembly/profile/package identifiers are retained. The
  Explorer read-only protocol remains 1.0.
- Durable index schema 8 retains per-source AI policy and adds disposable vector
  tables with transactional catalog/privacy invalidation. Existing source libraries
  default to standard indexing until explicitly enabled. Migration uses the
  existing provider's backup and transaction path; user authority stays in the
  same durable store.
- AI inference is distinct from extracted facts and user decisions. AI does
  not gain file-operation authority and does not override accepted/rejected tags.
- Changing storage location applies on restart under the original profile lock.
  The app verifies copied registered data/cache files, then atomically switches
  its location receipt. Earlier generations remain recovery copies. Do not
  delete them until independently backed up and verified. Keep the active drive
  available; startup must fail rather than silently open an empty library.
- Configuration, operation history and profile lock retain their original
  location. The logical `.oms-state` archive has existing exclusions and is not a
  complete substitute for a closed-profile filesystem backup. In particular,
  scoped Organize preferences in `decision-history.json` are not included in
  the logical archive; preserve the complete profile and active data location.
- Older applications do not understand schema 8 or the new active-location
  receipt. Downgrade in place is unsupported. To recover an old version, close
  all instances and restore an independently preserved, complete pre-upgrade
  profile/location; do not merge old/new SQLite files or journals.

<a id="release-v3-0-0-explicit-limitations"></a>
#### Explicit limitations

Local AI and document interpretation must be enabled and an installed model
selected. Enrichment validates structure, not factual correctness. A metadata-only
record needs deterministic extraction before useful AI enrichment. OCR depends on
its configured local engine and applicable PDF renderer. Alternative OCR engines
and richer media understanding remain extension/future work.

Embeddings are a separate opt-in from chat/enrichment and require an installed
dedicated model. Sixteen chunks of up to 1,600 characters per file bound semantic
coverage; very long documents may have relevant text outside that coverage.
Cosine retrieval is an exact scan under the vector storage budget, with no
unbounded-library performance promise. Literal facet counts and the older
deterministic “semantic available” filter retain their existing meanings. Weak
similarity suggestions can appear even for an out-of-corpus query; they do not
establish factual relationships. Larger representative libraries and subjective
relevance remain human validation work.

Human acceptance, keyboard/accessibility, real-world profile upgrade and
subjective relevance checks remain Not run until a tester records observations.
Windows packages are expected to be unsigned; macOS packages are expected to be
publisher-unsigned and unnotarized. Native package smoke and CI evidence must be
recorded before publication and never substituted for human acceptance.

</details>

## v2.13.0-rc prerelease — OmniSorSe Product Clarity & Workflow

<details>
<summary>Original milestone summary</summary>

Product Clarity & Workflow prerelease. Clear Scan → Review → Organize hierarchy, explicit Change Plan/duplicate intent, discoverable optional AI and Smart Tags, simplified Search/relationship/graph surfaces, accessible status hierarchy, guarded relationship authority, and recent indexing throughput/ETA.

</details>

Release source: GitHub `main` after the Product Clarity & Workflow pull request.
The annotated tag and GitHub prerelease own the exact packaged source identity;
this remains an unsigned release candidate rather than stable/GA.

### Added and changed

- Restore Scan → Review → Organize as the primary Home/navigation hierarchy.
- Clarify Change Plan provenance and bulk eligibility; distinguish duplicate
  recovery selection from the five-item shell-open limit.
- Keep optional AI organization visible while disabled and link directly to AI
  settings without enabling a provider.
- Give Search results useful vertical space and progressively disclose facets,
  saved searches, and maintenance.
- Explain and refresh Smart Tag states; distinguish Related Files from advanced
  graph diagnostics.
- Require a target-specific confirmation before relationship/collection
  authority is removed.
- Calculate recent indexing throughput and ETA from bounded recent completions.
- Preserve schema 6, Explorer Protocol 1.0, compatibility identifiers, and the
  reviewed Change Plan file-mutation boundary.

### Evidence boundary

Local SDK 10.0.400 qualification passes zero-warning Debug/Release builds,
1,878 tests in each configuration with no skips, focused relevance/performance/
policy checks, formatting/analyzers, dependency audit, and native Windows
package smoke. Exact-main hosted cross-platform, complete native packaging,
public-asset, and manual validation remain release gates for publication; the
GitHub prerelease records their final exact-commit evidence. See
[v2.13 Release Notes](CHANGELOG.md#release-v2-13-0) and
[v2.13 Manual Testing](MANUAL_TESTING.md#manual-v2-13).


<a id="release-v2-13-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.13.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** Release candidate for final real-world/manual validation before any
v2.13.0 GA claim. This is not a stable release. Windows artifacts are unsigned;
macOS artifacts are not Apple Developer ID-signed and are unnotarized. A
toolchain-provided ad-hoc signature does not identify or authenticate a
publisher.

<a id="release-v2-13-0-downloads"></a>
#### Downloads

Use only the assets attached to the canonical
[v2.13.0-rc GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.13.0-rc):

- `OmniSorSe-v2.13.0-rc-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.13.0-rc-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.13.0-rc-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.13.0-rc-macos-arm64.dmg` — Apple Silicon macOS package;
- `OmniSorSe-v2.13.0-rc-sbom.cdx.json` — CycloneDX 1.6 SBOM;
- `OmniSorSe-v2.13.0-rc-SHA256SUMS.txt` — SHA-256 hashes for the other five
  release files.

Do not treat the files as published until the GitHub release exists and its
exact tagged commit, workflow provenance, metadata, and checksums have been
verified. Checksums detect changed bytes; they do not authenticate an unsigned
publisher.

<a id="release-v2-13-0-what-changed"></a>
#### What changed

v2.13 restores the product's main path: **Scan → Review → Organize**. Home and
navigation now lead with those tasks. Search, Duplicates, Related Files, library
automation, and graph diagnostics remain available with clearer roles, while
technical controls use progressive disclosure or Settings.

Review Changes identifies the plan's origin, purpose, warnings, and eligible
action count. **Approve all eligible** now states its exact boundary: actions in
Valid or Warning state, with no blocking conflict. It never applies a plan.
Duplicate review keeps one copy per group, supports selecting more than the
five-file shell-open limit, offers a keep-first helper, and identifies its
recovery-based Change Plan explicitly.

Optional AI organization remains visible when AI is disabled or unconfigured
and links directly to AI settings. It does not enable a provider, contact one,
or bypass Change Plan review. Folder proposals have explicit Keep proposal and
Dismiss actions.

Search results receive practical vertical space. Facets, saved searches, and
index maintenance are collapsed until requested. Smart Tags explain whether a
file is unselected, still indexing, genuinely has no retained tags, or has
reviewable evidence, and they can be refreshed explicitly.

Related Files is the everyday evidence-and-correction surface. **Graph
diagnostics** is the advanced derived projection. Unlinking, forgetting,
merging, splitting, or returning a corrected pair to automatic evidence now
requires a target-specific confirmation. These actions change retained
OmniSorSe relationship state, not original files.

Index progress now reports recent throughput from terminal work completed in a
bounded trailing window. ETA appears only after enough recent samples exist, so
a long-running job no longer dilutes the displayed rate merely because of its
age. Default concurrency and resource policies are unchanged; no throughput
increase is claimed without real-library measurement.

<a id="release-v2-13-0-preserved-boundaries"></a>
#### Preserved boundaries

- .NET 10, deep-index schema 6, Explorer Protocol 1.0, `.oms-state` format 2,
  compatibility identifiers, and the local-first architecture are unchanged.
- Reviewed Change Plans remain the only supported production source-file
  mutation path. AI, Search, Smart Tags, relationships, graph diagnostics,
  watchers, recipes, and plugins do not gain direct file-mutation authority.
- Existing saved-search storage/type names, OpenSorSe profile/assembly names,
  installer identity, and bundle identifiers remain where compatibility needs
  them.
- Optional AI stays disabled by default. A configured non-loopback
  Ollama-compatible endpoint is a remote privacy boundary.

<a id="release-v2-13-0-automated-versus-manual-evidence"></a>
#### Automated versus manual evidence

Repository tests cover navigation composition, workflow labels, plan context
and bulk eligibility, duplicate selection bounds, disabled-AI discoverability,
Smart Tag states, relationship confirmation, and recent indexing throughput.
Local SDK 10.0.400 qualification passed zero-warning Debug/Release builds,
1,878 tests in each configuration with no skips, focused relevance/performance/
policy checks, formatting/analyzers, an 18-project vulnerability audit, and a
native Windows self-contained package smoke. Publication additionally requires
hosted four-platform and complete native packaging workflows against the exact
tagged commit; the GitHub prerelease records those immutable run and asset
details.

Automation does not validate human-scale visual hierarchy, small-window and
high-DPI layout, wheel/keyboard/focus behavior, screen readers, real-library
relationship/tag quality, optional provider integration, or normal-user
installer prompts. Those checks remain explicit in the
[v2.13 manual checklist](MANUAL_TESTING.md#manual-v2-13).

</details>

## v2.12.0-rc prerelease — OmniSorSe Trusted Relationships & Context

<details>
<summary>Original milestone summary</summary>

Trusted Relationships & Context prerelease. Stronger bounded relationship evidence, reversible pair authority, direct Related Files, relationship-only reanalysis, and format-2 authored-state backup.

</details>

Release source: GitHub `main`, including the history from
`v2.12-trusted-relationships-context`. This is an unsigned prerelease candidate
for final real-world/manual validation, not the v2.12.0 stable/GA release.

### Added and changed

- Cap independent identity, content, named-context, lexical, tag,
  structural/temporal, and semantic evidence families; semantic/AI evidence
  cannot qualify alone.
- Add reversible Related, Not Related, and Use automatic pair authority plus a
  bounded correction view.
- Aggregate multiple typed edges into one direct Related Files/Explorer target
  without discarding persisted detail.
- Use bounded indexed candidate buckets, compact batch hydration, and
  resumable relationship-only version refresh for large libraries.
- Make direct Related Files independent of the optional Knowledge Graph and add
  Search/Files entry points.
- Write `.oms-state` format 2 with authored Smart Collection authority while
  retaining exact format-1 import.

### Boundaries

- Schema remains 6 and Explorer Protocol remains 1.0.
- Knowledge Graph remains optional/derived; Smart Collections remain grouping
  authority.
- No new AI relationship inference, production dependency, clustering engine,
  Search-ranker redesign, or file-mutation path is included.


<a id="release-v2-12-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.12.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** GitHub prerelease candidate for final real-world/manual validation
before v2.12.0 GA. This is not a stable release. Windows artifacts are unsigned;
macOS artifacts are not Apple Developer ID-signed and are unnotarized. A
toolchain-provided ad-hoc signature does not identify or authenticate a
publisher.

<a id="release-v2-12-0-downloads"></a>
#### Downloads

Use only the assets attached to the canonical
[v2.12.0-rc GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.12.0-rc):

- `OmniSorSe-v2.12.0-rc-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.12.0-rc-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.12.0-rc-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.12.0-rc-macos-arm64.dmg` — Apple Silicon macOS package;
- `OmniSorSe-v2.12.0-rc-sbom.cdx.json` — CycloneDX 1.6 SBOM;
- `OmniSorSe-v2.12.0-rc-SHA256SUMS.txt` — SHA-256 hashes for the other five
  release files.

Every package is bound to the exact tagged `main` commit through binary version
metadata, `OmniSorSe.build.json`, the SBOM, and the checksum manifest. Verify the
named SHA-256 value before opening a package. A checksum detects changed bytes;
it does not authenticate an unsigned publisher.

<a id="release-v2-12-0-important-upgrade-and-trust-boundary"></a>
#### Important upgrade and trust boundary

The visible product is OmniSorSe, but established OpenSorSe application-data,
installer, bundle, schema, assembly, and namespace identifiers remain where
compatibility requires them. The RC installer can replace an existing OmniSorSe
installation, and first launch can migrate the retained OpenSorSe profile and
schema. Close the application first. Use a disposable account/machine or make a
reviewed backup before testing an upgrade.

Windows SmartScreen may report an unrecognized publisher. macOS Gatekeeper may
require a reviewed override. No signature, notarization, or publisher-authenticity
claim is made.

<a id="release-v2-12-0-what-changed"></a>
#### What changed

v2.12 makes the existing relationship system easier to trust and correct. Direct
Related Files no longer depends on the optional Knowledge Graph. Evidence is
grouped into capped independent families, noisy correlated signals cannot stack
without bounds, and semantic or AI-derived evidence cannot establish a
relationship alone.

Users can mark a pair **Related**, **Not Related**, or return it to **Use automatic
result**. Negative corrections remain discoverable for reversal. Multiple typed
edges are shown as one related target with a bounded explanation. Search and
Files provide direct entry points while exact filename, stem, and prefix intent
remain dominant.

Large-library work remains bounded through indexed candidate buckets, compact
batch hydration, a 512 defensive ceiling, and resumable relationship-only
version refresh. Explorer Protocol stays at 1.0 and returns aggregated,
authorized, opaque related context without adding writes.

Logical `.oms-state` format 2 adds pair and user-authored Smart Collection
authority. Format-1 import remains supported. Generated edges, evidence,
automatic membership, and graph projections remain rebuildable and are not
backed up. Restore never guesses unresolved identity, and a format-1 import does
not clear newer Smart Collection authority that its older payload cannot carry.

Architecture remains intentionally stable: .NET 10, schema 6, no new production
dependency, optional unchanged AI, optional derived Knowledge Graph, Smart
Collections as grouping authority, and reviewed Change Plans as the only
file-mutation path.

<a id="release-v2-12-0-automated-validation-completed"></a>
#### Automated validation completed

The release process requires the exact tagged `main` commit to pass:

- no-cache restore, zero-warning Debug and Release builds, and the complete test
  suite on Windows, Ubuntu, macOS Intel, and macOS Apple Silicon;
- formatting, analyzers, documentation/repository policy, dependency and
  vulnerability checks, and native package smoke;
- exact source/version/RID/runtime provenance, portable/app-bundle content and
  forbidden-file checks, CycloneDX SBOM generation, and SHA-256 verification;
- controlled Windows per-user install, installed production-composition smoke,
  stop/uninstall, shortcut and uninstall-entry cleanup, and user-data
  preservation;
- native macOS DMG mount, architecture/runtime inspection, composition smoke,
  and explicit publisher-signature/notarization checks that permit only an
  absent or ad-hoc signature and reject Developer ID signing.

These are automated and controlled checks. They do not substitute for the manual
validation below.

<a id="release-v2-12-0-manual-validation-still-required"></a>
#### Manual validation still required

The inherited v2.10 matrix and v2.11/v2.12 addenda remain unchecked. Important
remaining work includes:

- real-library relationship quality, pair corrections, scale, cancellation, and
  removable/offline source identity;
- normal interactive Windows install, SmartScreen, installer wizard, Restart
  Manager, v2.4 profile upgrade/migration, uninstall/reinstall, and rollback;
- keyboard, screen-reader/VoiceOver, DPI/scaling, and broader desktop UX checks;
- actual optional Tesseract, ffmpeg/ffprobe, whisper.cpp, Ollama, plugins, and
  OmniBrille configurations;
- broader native Windows filesystem behavior and native Linux/macOS interactive
  and real-filesystem scenarios.

See [Release Status](RELEASE_STATUS.md), the
[v2.12 manual addendum](MANUAL_TESTING.md#manual-v2-12), the inherited
[v2.11 addendum](MANUAL_TESTING.md#manual-v2-11), and the
[v2.10 master matrix](MANUAL_TESTING.md#manual-v2-10) for the exact evidence boundary.

</details>

<a id="unreleased--omnisorse-v211-supported-runtime--platform-readiness"></a>
## v2.11 Supported Runtime & Platform Readiness — historical candidate

Implementation branch: `v2.11-supported-runtime-platform-readiness`. This work
is not a published release and builds directly on the committed v2.10 candidate.

### Added and changed

- Move every solution project and package path to .NET 10 LTS (`net10.0`) using
  one SDK/runtime authority.
- Preserve the existing package set; remove only net8-specific transitive
  compatibility entries that no longer resolve.
- Record and validate target framework, RID, bundled runtime, self-contained
  status, semantic version, configuration, and exact source commit in packages.
- Add bounded native package-smoke validation to Windows, Ubuntu, and macOS CI
  and pin critical Actions to immutable commits.
- Make Created range filters use filesystem-created time consistently with the
  Created Year facet while keeping media capture evidence distinct.
- Preserve managed legacy `net8.0` plugin manifests while current manifests
  advertise `net10.0`.

### Boundaries

- Schema remains 6 and Explorer Protocol remains v1.
- No Search-ranker, classifier, Smart Tag, recipe, Change Plan, AI, graph,
  OmniBrille, updater, Linux-package, or production-dependency expansion is
  included.
- macOS/Linux mutation support remains conservative; cross-target compilation
  is not represented as native runtime or interactive evidence.



<a id="release-v2-11-0"></a>

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.11.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** Unreleased release candidate; no package, tag, or publication is
claimed by this document.

<a id="release-v2-11-0-what-changed"></a>
#### What changed

- All production, test, protocol, and harness projects now target .NET 10 LTS
  (`net10.0`) through the repository’s central SDK/runtime authority.
- Existing package versions remain unchanged; no production dependency was
  added or upgraded for the migration.
- Windows/macOS package manifests now identify target framework, RID, bundled
  runtime version, self-contained status, semantic version, configuration, and
  exact source commit, with stricter native-asset validation.
- Native Windows, Ubuntu, and macOS CI includes a bounded self-contained publish
  and package-smoke path. Release packaging remains separate and never publishes
  automatically.
- Critical GitHub Actions are pinned to immutable upstream commit SHAs.
- Filesystem Created range filters now match the filesystem-created provenance
  already used by the Created Year facet; EXIF capture time remains separate.
- Current plugins advertise `net10.0`; managed legacy `net8.0` manifests remain
  supported by the net10 host.

<a id="release-v2-11-0-preserved-behavior"></a>
#### Preserved behavior

Schema remains 6 and Explorer Protocol remains v1. Existing v2.10 profiles and
`.oms-state` backups require no format migration. Search ranking, facets, Saved
Views, Smart Tags, classification, organization recipes, Change Plans,
journalling, reconciliation, Undo, backup/restore, Forget, health, optional AI,
and OmniBrille are not redesigned.

No updater, Linux installer, entity/date intelligence subsystem, classifier
change, new recipe/token, cloud service, protocol expansion, or new production
dependency is included.

<a id="release-v2-11-0-platform-and-trust-boundary"></a>
#### Platform and trust boundary

Windows x64 remains primary. macOS packages remain conservative and do not gain
source-file mutation merely for parity. Linux x64 remains source-build preview.
Unsigned/unnotarized status must be reported honestly when release credentials
are unavailable. Checksums and embedded source provenance are provided but do
not authenticate an unsigned publisher.

See [Supported Runtime & Platform Readiness](SUPPORTED_RUNTIME_PLATFORM_READINESS.md),
the [Platform Compatibility Matrix](PLATFORM_COMPATIBILITY_MATRIX.md), and the
[v2.11 manual addendum](MANUAL_TESTING.md#manual-v2-11).

</details>

<details>
<summary>Original milestone summary</summary>

Supported Runtime & Platform Readiness. .NET 10 migration, stronger source/RID/runtime provenance, and clearer compile/native/package evidence boundaries.

</details>

<a id="unreleased--omnisorse-v210-production-hardening--operational-resilience"></a>
## v2.10 Production Hardening & Operational Resilience — historical candidate

Implementation branch: `v2.10-production-hardening-operational-resilience`.
This work is not a published release and builds directly on the committed v2.9
release candidate.

### Added and changed

- Enforce one current-user writer per profile and record abnormal shutdowns.
- Preserve and fail closed on corrupt Change Plan or Operation Journal state.
- Add bounded schema/store/profile/source/job/storage health inspection.
- Bound managed PDF extraction and PDFium rasterization inputs and work.
- Add reviewed logical export/restore for difficult-to-reconstruct user state,
  including exact-pair manual relationship decisions.
- Coordinate Forget across schema-6 SQLite and rebuildable compatibility caches.
- Centralize product version/source provenance and validate packaged metadata.
- Preserve all unrelated configuration during logging/diagnostic normalization.
- Harden Ollama prompts against instructions embedded in untrusted file data.
- Add high-blast-radius corruption, rollback, deletion, parser, profile-lock,
  configuration, provenance, and backup/restore regression coverage.

### Boundaries

- Schema remains 6 and Explorer Protocol remains v1.
- No Search, classifier, organization, graph, OmniBrille, cloud, autonomous,
  mutation-engine, protocol, or production-dependency expansion is included.
- PdfPig extraction and PDFium rasterization remain in process behind strict
  bounds; parser-internal allocation and native crash/hang risk are not claimed
  eliminated and remain manual/native validation gates.



<a id="release-v2-10-0"></a>

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.10.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Candidate only:** no v2.10.0 package, tag, merge, or release exists yet.

v2.10 hardens long-lived local state and reviewed file operations. It adds
single-writer profile ownership, fail-closed corrupted Change Plan/Operation
Journal handling, bounded PDF parsing, logical state export/restore, complete
Forget coordination, traceable build provenance, bounded health checks,
abnormal-shutdown detection, and adversarial/failure regression coverage.
Logical state includes exact-ID Smart Tag authority and exact-pair manual
relationship decisions without path guessing.

It preserves schema 6, Explorer Protocol v1, Search ranking, Smart Tag
authority, organization recipes, Change Plans, reconciliation, and Undo. It
adds no new production dependency and no autonomous or network-listening
capability. PdfPig extraction and native PDFium rasterization remain in
process behind strict bounds; manual hostile-fixture and native-platform
validation remains required.

See [production hardening](PRODUCTION_HARDENING.md), the
[master manual matrix](MANUAL_TESTING.md#manual-v2-10), and
[operational runbooks](OPERATIONAL_RUNBOOKS.md).

</details>

<details>
<summary>Original milestone summary</summary>

Production Hardening & Operational Resilience. Single-writer profile ownership, fail-closed recovery stores, health/lifecycle checks, logical state backup/restore, and coordinated Forget.

</details>

<a id="unreleased--omnisorse-v29-reviewed-intelligent-organization"></a>
## v2.9 Reviewed Intelligent Organization — historical candidate

Implementation branch: `v2.9-reviewed-intelligent-organization`. This work is
not a published release and builds directly on the committed v2.8 release
candidate.

### Added and changed

- Connect explicit bounded Files, Search, and current Saved View selections to
  the existing persistent recipe authority using stable indexed file IDs.
- Add a cancellable ephemeral Organization recipe preview with trusted evidence
  mappings, literal coverage, Reliable/Needs review/Cannot propose states,
  fallbacks, privacy warnings, and conflict-first bounded rows.
- Promote a small modern token picker for original name, accepted/uniquely
  Strong deterministic Theme and Document Type, explicit filesystem-created and
  filesystem-modified dates, and coarse file category.
- Preserve naming and destination as independent patterns and always retain the
  exact original extension in the reviewed Organization workflow.
- Budget file and deduplicated directory-creation actions together before Change
  Plan creation, with a hard 1,000-file and 1,000-total-action limit.
- Re-resolve stable IDs, evidence, targets, collisions, and bounds before
  handing the explicit request to the existing Change Plan.
- Use **Organization recipe** as the user-facing name for the compatible
  `SortingRecipe` library and add safe built-in examples with honest date
  semantics.

### Boundaries

- Schema 6, the atomic workflow-library JSON store, Search/facets/Saved Views,
  Smart Tag authority, Change Plan execution/journal/reconciliation/Undo,
  Explorer Protocol v1, and OmniBrille remain unchanged.
- No autonomous/watched-folder recipe execution, cross-root moves, metadata
  writeback, new AI behavior, cloud service, schema migration, protocol field,
  mutation engine, or production dependency is included.



<details>
<summary>Original milestone summary</summary>

Reviewed Intelligent Organization. Bounded stable-ID selection, recipe preview, trusted evidence/fallbacks, collision checks, and existing Change Plan handoff.

</details>

<a id="unreleased--omnisorse-v28-guided-workflows--product-coherence"></a>
## v2.8 Guided Workflows & Product Coherence — historical candidate

Implementation branch: `v2.8-guided-workflows-product-coherence`. This work is
not a published release and builds directly on the committed v2.7 release
candidate.

### Added and changed

- Add a bounded discovery context that opens a Search result in Files by stable
  file ID and restores query, canonical facets, Saved View, and review mode.
- Add continuous unresolved Moderate Smart Tag review with bounded evidence,
  previous/next navigation, and automatic current-membership refresh after an
  explicit Keep or Dismiss decision.
- Replace session-only Home status with bounded durable library, readiness,
  pending-review, Saved View, and optional-capability projections.
- Add Find, Understand, Review, and Organize entry points into existing product
  surfaces without adding destinations or bypassing prerequisites.
- Retire redundant legacy Smart Tag Search selectors; v2.7 facets remain the
  sole canonical Theme, Document Type, and User Tag filter model.
- Allow accepted, explicit User Tag, and Strong deterministic classification
  evidence to inform bounded editable rename suggestions with visible authority.
- Move ordinary Search maintenance and repair actions behind collapsed advanced
  disclosure while retaining critical readiness and safety information.

### Boundaries

- Schema 6, deterministic Search ranking/candidate selection, facet SQL, Saved
  View rules, progressive indexing, Smart Tag taxonomy/authority, Change
  Plans/Undo, Explorer Protocol v1, and OmniBrille remain unchanged.
- No cloud service, new production dependency, automatic organization,
  protocol field, schema migration, classifier redesign, or metadata writeback
  is included.



<details>
<summary>Original milestone summary</summary>

Guided Workflows & Product Coherence. Stable-identity Search/Files navigation, durable Home readiness, continuous Smart Tag review, and coherent organization entry points.

</details>

<a id="unreleased--omnisorse-v27-scalable-faceted-discovery"></a>
## v2.7 Scalable Faceted Discovery — historical candidate

Implementation branch: `v2.7-scalable-faceted-discovery`. This work is not a
published release and builds directly on the committed v2.6 release candidate.

### Added and changed

- Select complete-library Search candidate eligibility in SQLite before
  bounded hydration into the existing filename-first deterministic ranker.
- Report candidate eligibility, plausible matches, hydration count, and any
  deliberate bound separately from indexing coverage and displayed results.
- Add database-backed Theme, Document Type, User Tag, file-type,
  filesystem-created-year, and filesystem-modified-year facets with contextual
  counts, OR-within-type, and AND-across-type semantics.
- Add dynamic local Saved Views that persist canonical query/filter rules and
  reevaluate current index contents without copying result membership.
- Add unresolved Moderate Smart Tag discovery using the existing user-authority
  review actions.
- Add bounded CSV/TSV native evidence and conservative XLSX/PPTX text evidence
  without executing formulas, macros, embedded objects, or external resources.

### Boundaries

- Schema 6, Explorer Protocol v1, Smart Tag authority, filename ranking,
  progressive indexing, Change Plans, and OmniBrille separation remain intact.
- No entity facet, bulk dashboard, Ollama classifier, embeddings, metadata
  writeback, automatic organization, cloud service, or new production
  dependency is included.



<details>
<summary>Original milestone summary</summary>

Scalable Faceted Discovery. Complete-index candidate selection, contextual facets/counts, dynamic Saved Views, and bounded document extraction.

</details>

<a id="unreleased--omnisorse-v26-explainable-smart-tags"></a>
## v2.6 Explainable Smart Tags — historical candidate

Implementation branch: `v2.6-explainable-smart-tags`. This work is not a
published release and builds on the committed v2.5 release candidate.

### Added and changed

- Add schema-6 normalized Smart Tag definitions, file assignments, user
  decisions, classification status, indexed canonical filters, transactional
  schema-5 migration, and conservative legacy authority import.
- Add small versioned English-first Theme and Document Type taxonomies plus
  bounded freeform local User Tags.
- Add deterministic grouped-evidence classification with Strong, Moderate, and
  Limited bands; no-evidence/conflict states; bounded reasons; and protection
  against filename dominance and correlated-evidence double counting.
- Add a durable deferred Smart Tag stage that reuses retained document, OCR,
  transcript, metadata, and Content Intelligence evidence without delaying
  v2.5 Fast/searchable-first coverage.
- Add exact Smart Tag Search evidence and typed canonical filters with OR within
  one type and AND across types while preserving filename-first ranking.
- Add Files details actions for accepting/rejecting suggestions, User Tags,
  decision reset, generated-tag clearing, and View files with this tag.
- Add bounded native `.txt`, `.md`, `.markdown`, and `.text` extraction.

### Boundaries

- No source metadata writeback, embeddings/vector database, cloud classifier,
  automatic file mutation, Smart Tags dashboard, OmniBrille modification, or
  Explorer Protocol v1 change is included.



<details>
<summary>Original milestone summary</summary>

Explainable Smart Tags. Schema-6 Theme/Document Type and User Tag authority, bounded deterministic classification, reviewable decisions, and canonical Search filters.

</details>

<a id="unreleased--omnisorse-v25-workflow-completion--indexing-quality"></a>
## v2.5 Workflow Completion & Indexing Quality — historical candidate

Implementation branch: `v2.5-workflow-indexing-quality`. This work is not a
published release.

### Added and changed

- Reconcile Files, Search/index refresh inputs, duplicate projections, details,
  and logical selection from terminal Apply/Undo journal outcomes rather than
  assuming the reviewed Change Plan fully succeeded.
- Preserve stable file identity for successful rename/move projection updates;
  mixed rollback outcomes remain explicit and refresh only affected configured
  sources.
- Add persisted **Fast — searchable first** and **Deep initial analysis**
  scheduling choices while retaining existing capability switches and durable
  jobs.
- Distinguish discovery, usable base Search coverage, and continuing deeper
  analysis in progress presentation.
- Clarify the Suggest → Review Changes → execute journey and the bounded
  current-page scope of folder suggestions.
- Add an optional **Open in OmniBrille** action that discovers a separately
  installed companion only on demand, authorizes enabled indexed sources, and
  transfers one independent short-lived Protocol v1 session through the
  companion's established one-time current-user handoff pipe. Bearer material
  is not placed on the command line or disk.

### Boundaries

- Explorer Protocol v1 wire contracts/version and schema 5 are unchanged; the
  companion bootstrap is an additive desktop integration boundary.
- No server/cloud architecture, autonomous organization, embeddings/vector
  store, graph renderer, voice, or OmniBrille rendering/Context functionality
  is included.



<details>
<summary>Original milestone summary</summary>

Workflow Completion & Indexing Quality. Post-mutation reconciliation, base-search-first indexing, truthful indexing phases, organization clarity, and optional scoped OmniBrille handoff.

</details>

## v2.4.0 — OmniSorSe Transition & Explorer Foundation

<details>
<summary>Original milestone summary</summary>

OmniSorSe Transition & Explorer Foundation. Active OmniSorSe product/package identity with in-place OpenSorSe profile compatibility, plus a dormant authenticated, authorized-root-scoped, bounded, read-only local Explorer Protocol v1.

</details>

Release branch: `v2.4-omnisorse-transition`. Published after genuine Windows
profile/installer transition, external two-process protocol, full automated,
cross-target, exact-main, and native packaging validation.

### Added and changed

- Active product, desktop assembly/apphost, visible package, installer, macOS
  application, and future artifact identity change to OmniSorSe.
- Established OpenSorSe profile paths, schema 5, installer AppId/install
  directory, macOS bundle identifier, repository URL, and internal namespaces
  remain compatibility identities so the rename does not fork user state.
- Dependency-free Explorer Protocol 1.0 contracts plus an on-demand,
  current-user local named-pipe host expose authenticated, source-scoped,
  bounded, read-only Structure, unified Search, Related/context, and safe detail
  projections for a future separately distributed OmniExplorer.
- Protocol limits, stable errors, cancellation, backpressure, session expiry,
  opaque node identity, strict serialization, diagnostic redaction, security
  tests, architecture guidance, and an evidence-separated manual checklist.

### Safety and limitations

- OmniExplorer is not implemented or released. No graph renderer, layout/GPU
  dependency, voice surface, standalone scanner, external listener, cloud
  relay, or protocol mutation operation is included.
- The host is dormant until an explicit authorized session is requested;
  normal OmniSorSe startup and Search do not depend on a companion.


<a id="release-v2-4-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.4.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OmniSorSe v2.4.0 changes the active product identity from OpenSorSe to
OmniSorSe without moving or rebuilding existing user data. It also introduces
Explorer Protocol v1, a small authenticated read-only local IPC boundary for a
future separately distributed OmniExplorer companion.

<a id="release-v2-4-0-downloads"></a>
#### Downloads

The official [v2.4.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0)
contains:

- `OmniSorSe-v2.4.0-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.4.0-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.4.0-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.4.0-macos-arm64.dmg` — Apple Silicon package;
- `OmniSorSe-v2.4.0-SHA256SUMS.txt` — SHA-256 checksums for all packages.

Linux remains source-build supported. The Windows and macOS packages are
unsigned, and the macOS packages are not notarized. Verify the checksum and
review the source/release origin before overriding operating-system warnings.

<a id="release-v2-4-0-omnisorse-transition"></a>
#### OmniSorSe transition

- The active desktop, About, Help, diagnostics, package, executable, shortcut,
  and visible macOS application identity is OmniSorSe.
- Existing profiles stay in their established OpenSorSe locations. Settings,
  indexed sources, schema-5 Search data, watched folders, Media and Content
  Intelligence, external-tool paths, plans, journals, and recovery state remain
  compatible in place.
- Windows retains the existing installer AppId and default installation
  directory, allowing an installed OpenSorSe v2.3 release to upgrade to
  OmniSorSe v2.4 without creating a second product entry.
- The macOS bundle identifier remains unchanged for continuity.
- Internal `OpenSorSe.*` namespaces/projects and branding-neutral processing
  fingerprints remain unchanged. Branding alone does not require reindexing.
- Historical releases remain named OpenSorSe. Repository URLs also remain at
  the current OpenSorSe repository until a separately reviewed repository
  rename.

<a id="release-v2-4-0-explorer-protocol-v1"></a>
#### Explorer Protocol v1

- Protocol v1 is read-only, bounded, authenticated, and dormant until an
  explicitly authorized local session is created.
- It uses a current-user-only local named pipe on Windows (Unix-domain-backed
  by .NET on Unix hosts). It creates no TCP, HTTP, LAN, cloud, or discovery
  listener.
- Sessions use random 128-bit identifiers, 256-bit bearer tokens retained only
  as hashes, absolute expiry, immediate revocation, and session-bound HMAC
  opaque node identifiers.
- An authorized client can negotiate capabilities and limits, list only
  approved indexed roots, inspect bounded structural children/neighborhoods,
  run unified deterministic Search, read existing Related Files evidence, and
  request bounded node details.
- Raw paths are omitted unless separately granted. Full documents, complete
  OCR/transcripts, precise GPS, binary media, diagnostics, credentials, and
  arbitrary filesystem paths are not exposed.
- Strict JSON, frame/query/node/edge/depth/result limits, four concurrent
  requests, a bounded queue, hard timeouts, disconnect cancellation, and safe
  saturation recovery limit resource use.
- Search membership remains grounded in indexed file IDs and authorized scope;
  protocol requests never enable AI assistance.

<a id="release-v2-4-0-omniexplorer-status"></a>
#### OmniExplorer status

OmniExplorer is a future optional companion and is not included in v2.4.0.
There is no graph renderer, Explorer UI, GPU dependency, voice dependency,
companion installer, or direct SQLite access in this release. OmniSorSe remains
fully functional by itself.

<a id="release-v2-4-0-privacy-and-security"></a>
#### Privacy and security

- No telemetry, cloud relay, external listener, silent upload, or new cloud
  dependency was introduced.
- Explorer Protocol v1 is local, current-user-only, explicitly scoped, and
  read-only. It cannot rename, move, delete, organize, or otherwise mutate
  source files.
- Diagnostics retain bounded operation/state/count/timing facts without raw
  tokens, queries, paths, snippets, OCR, transcripts, or request payloads.
- Existing local-first Search, Media Intelligence, Content Intelligence, AI
  endpoint warnings, and privacy clearing behavior remain unchanged.

<a id="release-v2-4-0-compatibility-and-optional-dependencies"></a>
#### Compatibility and optional dependencies

Schema remains version 5. No data migration or reindex is required for the
rename. OmniSorSe continues to treat Ollama-compatible services, ffmpeg,
ffprobe, Tesseract, whisper.cpp, and Whisper models as optional user-managed
capabilities; none is bundled in official packages.

<a id="release-v2-4-0-quality-boundary"></a>
#### Quality boundary

- Fresh Debug and Release suites each pass 1,671 tests with zero failures and
  zero skipped tests, including two native named-pipe regressions added during
  final external-process validation.
- Release builds pass for Windows x64, Linux x64, macOS x64, and macOS arm64.
- A genuine published-v2.3 profile was opened repeatedly by the v2.4 candidate
  with schema 5, Search, watched folders, representative settings, and source
  bytes preserved.
- Windows-native two-process protocol validation covered negotiation,
  authorized roots, Structure, Search, Related Files, bounded details, invalid
  authentication, strict JSON, oversized frames, expiry, revocation, forced
  disconnect, application shutdown, concurrency, backpressure, Unicode, and no
  TCP listener.
- Native Linux/macOS protocol execution and comprehensive screen-reader, DPI,
  UNC, and broad interactive desktop testing are not claimed. Native macOS
  package validation is performed by the established release workflow.

See [Installation](INSTALLATION.md), the
[transition and protocol design](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL.md),
and [manual validation evidence](MANUAL_TESTING.md#manual-v2-4) for the exact
compatibility, security, packaging, and validation boundaries.

</details>

## v2.3.0 — Content Intelligence & Local Understanding

<details>
<summary>Original milestone summary</summary>

Content Intelligence & Local Understanding. Bounded topics/keywords, textual entities, extractive summaries, provenance, optional user-managed whisper.cpp transcription, grounded Search evidence, explainable cross-media relationships, generic-topic suppression, and schema 5.

</details>

Release branch: `v2.3-content-intelligence`. Published after local,
exact-main hosted, and native packaging gates.

### Added

- Provider-neutral bounded topics, textual entities, keywords, extractive
  summaries, provenance, and processing fingerprints over already indexed
  document/media evidence.
- Optional process-isolated adapter for a user-managed whisper.cpp CLI and
  local GGML model, including bounded timestamp segments, cancellation,
  timeout, safe temporary audio, and cache invalidation.
- Schema-5 nullable Content Intelligence persistence with transactional
  migration from v2.2 schema 4, malformed-record fallback, privacy inspection,
  clearing, and byte reporting.
- Explicit topic, textual-entity, and source-grounded-summary Search signals,
  explanations, and snippets plus corroborated cross-media Related Files
  evidence.
- Content Intelligence settings, indexed-data controls, built-in Help, design
  guide, and evidence-separated manual checklist.

### Safety and limitations

- Exact filename and literal Search tiers remain authoritative. Optional AI and
  derived evidence cannot invent file membership.
- No whisper.cpp runtime/model, ffmpeg, Tesseract, visual model, learned
  embedding model, or vector database is bundled or downloaded.
- No concrete visual-description provider, cloud transcription, telemetry,
  facial recognition, person identification, or autonomous organization is
  added.


<a id="release-v2-3-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.3.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe v2.3.0 adds bounded local understanding across documents and media
without replacing deterministic Search. Derived topics, keywords, textual
entities, extractive summaries, and optional local transcripts remain
explainable evidence attached to files that actually exist.

<a id="release-v2-3-0-downloads"></a>
#### Downloads

The official [v2.3.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.3.0)
contains:

- `OpenSorSe-v2.3.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.3.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.3.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.3.0-macos-arm64.dmg` — Apple Silicon package;
- `OpenSorSe-v2.3.0-SHA256SUMS.txt` — SHA-256 checksums.

Linux x64 remains supported through the documented source-build path; no Linux
installer is published.

<a id="release-v2-3-0-content-intelligence"></a>
#### Content Intelligence

- Bounded deterministic topic and keyword extraction uses already retained
  indexed evidence and suppresses generic/noisy concepts.
- Conservative textual entity extraction identifies source-grounded names,
  places, organizations, products, projects, dates, and document identifiers;
  it performs no biometric or internet identity resolution.
- One-sentence extractive summaries remain bounded and cannot invent facts that
  were absent from the indexed evidence.
- Provider, provider version, origin, confidence, processing fingerprint, and
  source evidence are retained for explainability and cache invalidation.
- Search exposes distinct Topic, Entity, Summary, Transcript, OCR, and metadata
  explanations. Exact filename and literal tiers remain authoritative.
- Related Files uses bounded shared topics/entities across file types, requires
  corroborating evidence, and suppresses generic-topic clusters.

<a id="release-v2-3-0-optional-local-transcription"></a>
#### Optional local transcription

- v2.3.0 includes an optional process-isolated adapter for a user-managed
  whisper.cpp CLI and GGML model.
- OpenSorSe does not bundle or silently download a runtime or model.
- Transcription is local, cancellable, timeout-bounded, cacheable, and can
  retain up to 512 timestamped segments within configured transcript limits.
- Video audio preparation reuses a separately configured local `ffmpeg` and an
  application-owned temporary workspace.
- Transcripts enter the existing media index and unified Search; they do not
  create a separate transcript database.
- No cloud transcription is provided and media is not sent to Ollama for
  transcription.

<a id="release-v2-3-0-persistence-and-privacy"></a>
#### Persistence and privacy

- Embedded Search schema 5 transactionally upgrades schema 4, preserving v2.2
  document/media evidence and creating a recovery backup before migration.
- A bounded indexed relationship-term projection avoids all-pairs scans.
- Clear/forget operations remove derived Content Intelligence and dependent
  automatic relationships without changing source files.
- No telemetry, silent upload, hidden internet lookup, facial recognition,
  person identification, learned embedding model, or vector database is added.
- No concrete visual-description provider ships; images and video frames are
  not silently sent to Ollama.

<a id="release-v2-3-0-optional-dependencies"></a>
#### Optional dependencies

The following tools remain external, user-managed, optional, and absent from
official OpenSorSe packages:

- whisper.cpp CLI and model for speech transcription;
- `ffmpeg` for video/container audio preparation and representative frames;
- `ffprobe` for audio/video metadata;
- Tesseract 5 for OCR;
- an Ollama-compatible service for separately enabled existing AI assistance.

Missing or invalid optional-tool configuration degrades only that capability;
ordinary local indexing and Search remain available.

<a id="release-v2-3-0-quality-and-validation-boundary"></a>
#### Quality and validation boundary

- Debug: 1,637 passed, 0 failed, 0 skipped.
- Release: 1,637 passed, 0 failed, 0 skipped.
- Non-incremental Debug and Release builds completed with zero warnings and zero
  errors.
- Release compilation and runtime-asset checks passed for Windows x64, Linux
  x64, macOS x64, and macOS arm64.
- Controlled Windows-native validation exercised official whisper.cpp 1.9.2
  with a local tiny English model, timestamped audio and video transcription,
  Transcript-to-Search retrieval, cancellation and workspace cleanup, plus real
  ffprobe/ffmpeg metadata/frame paths.
- A genuine v2.2 schema-4 index upgraded to schema 5, retained its indexed
  document/media evidence, accepted new Content Intelligence, reopened cleanly,
  and left source data unchanged.
- Native Tesseract OCR was not repeated in the final v2.3 environment because
  the reviewed installer was cancelled by the host; deterministic OCR provider
  tests and earlier v2.2 native evidence remain, but this is not claimed as a
  fresh v2.3 native OCR pass.
- Cross-target compilation is not a claim of native Linux/macOS transcription
  or interactive behavior. Broad community testing may still uncover defects
  for later maintenance releases.

<a id="release-v2-3-0-trust-and-known-limitations"></a>
#### Trust and known limitations

The Windows artifacts are unsigned. The macOS artifacts are unsigned and
unnotarized. Checksums detect changed bytes but do not authenticate an unsigned
publisher.

Transcription quality, memory use, and speed depend on the user-selected model
and hardware. There is no transcript seek UI, media playback, reverse
geocoding/map, visual-description provider, learned vector search, facial
recognition, cloud transcription, or autonomous organization in v2.3.0.

See [Content Intelligence](CONTENT_INTELLIGENCE.md),
[Manual Testing](MANUAL_TESTING.md#manual-v2-3), [Installation](INSTALLATION.md), and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).

</details>

## v2.2.0 — Media Intelligence

<details>
<summary>Original milestone summary</summary>

Media Intelligence. Bounded image/EXIF/GPS metadata, optional local OCR, lazy thumbnails, optional ffprobe/ffmpeg metadata and representative frames, unified media Search/relationships, scan ETA, multi-group duplicate recovery, and navigation/privacy clarity.

</details>

Release branch: `v2.2-media-intelligence`. Published as the Media Intelligence
release after local, exact-main hosted, and native packaging gates.

### Added

- Provider-neutral bounded media metadata, transcription,
  representative-frame, visual-description, and thumbnail contracts integrated
  with the durable indexing pipeline.
- Deterministic JPEG/PNG/WebP/BMP/TIFF header metadata, bounded EXIF
  make/model/orientation/capture/GPS parsing, and lazy cached still-image
  thumbnails.
- Optional safely invoked `ffprobe` audio/video metadata and optional capped
  `ffmpeg` representative frames, with explicit capability/unavailable states.
- Schema-4 shared media evidence, typed Search signals/snippets/explanations,
  media privacy inspection/clearing, aggregate diagnostics, storage accounting,
  and conservative transcript/OCR/device/capture relationship signals.
- Media Intelligence Settings and Help covering independent capabilities,
  conservative size/duration/frame/text limits, optional dependencies, and
  privacy.
- Truthful smoothed scan ETA after sufficient comparable work; combined
  duplicate-recovery plans across multiple groups with an independent keeper
  invariant; and a clearer primary/secondary navigation hierarchy with Search
  and Related Files as unique primary destinations.
- Scroll-bounded Virtual Collections and Related Files layouts, clearer
  local-indexing versus optional remote-endpoint wording, and EXIF-oriented
  still-image previews.
- External media-tool deadlines are classified as isolated provider timeouts,
  while caller cancellation remains cooperative cancellation; both paths
  terminate the process tree and remove owned temporary work.

### Safety and limitations

- No transcription or visual-description implementation, model, codec pack, or
  external executable is bundled. The contracts report unavailable cleanly.
- Exact filename/literal Search remains authoritative; weak optional
  descriptions cannot identify new files or create relationships alone.
- No source file is changed, no media is silently uploaded, and no telemetry,
  facial recognition, person identification, geocoding, or cloud service is
  introduced.
- Controlled Windows ffprobe/ffmpeg, image/preview, real Tesseract OCR/Search,
  cancellation/timeout, and published-v2.1 schema-3 migration checks are
  recorded separately from still-unperformed interactive and native
  Linux/macOS checks.


<a id="release-v2-2-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.2.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe v2.2.0 extends the local-first Search experience to images, audio,
and video. Deterministic local Search remains authoritative; media evidence is
bounded, optional capabilities fail independently, and source files remain
unchanged unless a user separately approves a supported Change Plan.

<a id="release-v2-2-0-downloads"></a>
#### Downloads

Use the assets attached to the official
[v2.2.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.2.0):

- `OpenSorSe-v2.2.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.2.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.2.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.2.0-macos-arm64.dmg` — Apple Silicon package;
- `OpenSorSe-v2.2.0-SHA256SUMS.txt` — verified SHA-256 checksums.

No Linux installer is published. Linux x64 remains source-build supported.

<a id="release-v2-2-0-media-intelligence"></a>
#### Media Intelligence

- Supported images become first-class indexed content with bounded dimensions,
  orientation, capture time, camera/device, textual EXIF, and local GPS
  metadata where present.
- Existing optional local Tesseract OCR now supplies searchable image and
  representative-video-frame evidence.
- Image previews are generated lazily, orientation-corrected, bounded, cached
  only below OpenSorSe-owned storage, and never written beside the source file.
- Optional user-managed `ffprobe` supplies bounded audio/video container,
  codec, duration, resolution, frame-rate, bitrate, channel, sample-rate, and
  embedded textual metadata where available.
- Optional user-managed `ffmpeg` produces a strictly capped set of evenly
  spaced interior video frames for bounded preview/OCR evidence. Temporary
  workspaces are application-owned and removed after success, cancellation,
  timeout, or failure.
- Media metadata, OCR, future transcript evidence, and optional future visual
  descriptions use the same provider-neutral indexing and unified Search
  contracts. Search explanations identify the actual evidence source.
- Related Files can use exact OCR/transcript fingerprints and conservative
  capture-time/device corroboration without allowing weak camera identity to
  create broad clusters.
- Expensive results use file/configuration/provider fingerprints, bounded
  quotas, one-at-a-time default indexing, finite provider timeouts, and
  cooperative cancellation.
- The durable Search index migrates transactionally from schema 3 to schema 4,
  retaining a recovery backup and existing searchable content.

<a id="release-v2-2-0-search-and-workflow-quality"></a>
#### Search and workflow quality

- Search is now an obvious primary navigation destination and remains one
  unified experience for files, documents, images, audio, and video.
- Scan progress can show a smoothed ETA only after sufficient comparable work;
  indeterminate and terminal states remain truthful.
- Duplicate selections can span multiple groups and enter one reviewed Change
  Plan while enforcing at least one keeper per affected group.
- Virtual Collections and Related Files now have corrected scroll ownership and
  bounded layouts.
- Related Files appears once as a visible top-level destination; the previous
  route remains compatibility-only.
- Navigation now gives common tasks greater visual priority without removing
  advanced tools.
- Settings clearly separate local file analysis from optional AI endpoints.
  Loopback endpoints are identified as local; non-loopback endpoints display a
  privacy warning.

<a id="release-v2-2-0-privacy-and-optional-dependencies"></a>
#### Privacy and optional dependencies

- Scanning, indexing, EXIF parsing, thumbnails, configured Tesseract OCR, and
  configured ffprobe/ffmpeg processing happen locally.
- OpenSorSe introduces no telemetry, mandatory cloud service, or silent media
  upload.
- `ffprobe`, `ffmpeg`, and Tesseract are optional user-managed tools and are not
  bundled in release packages. Missing or invalid tools disable only their own
  capabilities.
- The transcription architecture is present, but no concrete transcription
  runtime or model ships in v2.2.0. No model is silently downloaded and media
  is not sent to cloud transcription.
- The visual-description provider boundary is present, but no concrete model
  ships. Images and frames are not silently sent to Ollama.
- GPS remains structured local metadata. v2.2.0 performs no reverse geocoding
  and transmits no GPS information to a map service.
- Facial recognition and automatic person identification are not implemented.

<a id="release-v2-2-0-validation-boundary"></a>
#### Validation boundary

The final local suite passed **1,603 tests** in Debug and Release with zero
failures and zero skips, alongside zero-warning builds, Search/media/indexing,
migration/recovery, performance, privacy, accessibility, provider failure,
formatting, analyzer, policy, vulnerability, and four-runtime cross-target
gates. Native Windows checks exercised real image decoding, Tesseract OCR,
ffprobe/ffmpeg providers, bounded frame extraction, and a genuine v2.1
schema-3 to schema-4 migration.

Cross-target builds passed for Windows x64, Linux x64, macOS x64, and macOS
ARM64. That is not a claim of native Linux/macOS media execution. Native
macOS package construction and inspection are release-workflow evidence, not
broad interactive testing.

<a id="release-v2-2-0-signing-and-known-limitations"></a>
#### Signing and known limitations

Unless the GitHub release explicitly records otherwise, Windows artifacts are
unsigned and macOS artifacts are unsigned/unnotarized. SmartScreen or
Gatekeeper may warn. Verify the complete SHA-256 checksum before use.

No concrete transcription or visual-description provider, video playback,
transcript seek UI, reverse geocoding/map, facial recognition, object tracking,
full-video understanding, cloud indexing, or autonomous media organization is
included.

See [Installation](INSTALLATION.md),
[Media Intelligence](MEDIA_INTELLIGENCE.md),
[Safety and Privacy](SAFETY_AND_PRIVACY.md), and the honest
[v2.2 manual checklist](MANUAL_TESTING.md#manual-v2-2).

</details>

## v2.1.0


<details>
<summary>Original milestone summary</summary>

Search & AI Quality. Deterministic filename relevance and typo quality, optional bounded Ollama reranking, truthful scan timing, safe duplicate recovery, dismissible notifications, privacy clarity, Related Files guidance, and contextual Help.

</details>
Release branch: `v2.1-search-ai-quality`. Published as the Search & AI Quality
release after local and hosted release gates.

### Added

- Explicit exact filename-stem, filename-prefix, and filename-substring ranking
  signals plus bounded adjacent-letter transposition matching.
- Default-off, per-query local-AI ordering for at most 12 already-ranked known
  results, with strict schema/identity validation and deterministic tier
  fencing.
- Provider-confirmed Ollama running-model discovery with graceful installed-
  model fallback when runtime status is unavailable.
- Cross-platform **Copy full path** Search result action and truthful
  indeterminate indexing progress while discovery has no known total.
- Monotonic live scan elapsed time that freezes at terminal operation state.
- Reviewable safe duplicate-removal plans that preserve a known keeper and move
  selected copies into an excluded recovery area through the existing executor.
- Compact dismissible categorized notifications without deleting durable
  diagnostic evidence.
- Contextual Help topics for the current product, including Watched Folders,
  Workflows, Related Files, Privacy, and Troubleshooting.

### Changed

- Filename matches are stronger and more explicit without allowing one weak
  filename word to overwhelm a multi-field or exact document-text match.
- Missing persisted Ollama models fall back to a deterministic installed model
  in the editable Settings draft with a clear save requirement.
- Ollama endpoint, timeout, missing-model, malformed-response, and cancellation
  outcomes preserve ordinary deterministic Search.
- Local file-analysis/indexing settings are separate from optional AI settings;
  only verified loopback endpoints are labelled local.
- The ordinary graph-navigation label is **Related Files** and Search shows
  **Hybrid** or **Hybrid + AI assistance** without changing internal contracts.

### Safety and compatibility

- AI cannot discover or invent files, cross deterministic relevance tiers,
  modify scores, or mutate original files.
- Search assistance sends no absolute paths, vectors, complete index, or whole
  documents. A configured non-local endpoint receives the explicitly enabled
  bounded query/candidate text.
- No database/index schema or source-file mutation boundary changed.
- Duplicate recovery moves remain reviewable, journalled, and undoable; they do
  not permanently delete data or immediately reclaim disk space.


<a id="release-v2-1-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.1.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe v2.1.0 is a focused quality release over the v2.0 local-first
architecture. Deterministic indexed Search remains authoritative and fully
usable without Ollama. No Search/index database schema or migration changes are
introduced.

<a id="release-v2-1-0-downloads"></a>
#### Downloads

Use the assets on the official
[v2.1.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.1.0):

- `OpenSorSe-v2.1.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.1.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.1.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.1.0-macos-arm64.dmg` — Apple Silicon macOS package;
- `OpenSorSe-v2.1.0-SHA256SUMS.txt` — verified SHA-256 checksums.

No Linux installer is published. Linux x64 remains validated as a source-build
target; see [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md).

<a id="release-v2-1-0-search--ai-quality"></a>
#### Search & AI Quality

- Exact complete filenames and exact filename stems rank above prefix,
  substring, ordinary field, fuzzy, relationship, and semantic-only evidence.
- Case, punctuation, diacritics, spaces, underscores, hyphens, dots, path
  separators, partial filenames, and one adjacent-letter transposition use
  bounded deterministic normalization and matching.
- **Why this result?**, source-labelled bounded snippets, and **Copy full path**
  make matches and common next actions clearer.
- Optional AI assistance reranks at most 12 files already returned by Search,
  cannot add file identities, cannot cross deterministic relevance tiers, and
  falls back to the original order on any invalid output or provider failure.
- Ollama installed-model discovery is deterministic. Provider-confirmed running
  models are distinguished where `/api/ps` is available; OpenSorSe does not
  invent a loading state.
- Missing models, refused connections, malformed responses, model-load errors,
  timeout, and cancellation are actionable states. Search remains available.
- Prompts are compact and suitable for small local models. Absolute paths,
  complete documents, the whole index, and raw vectors are not sent by Search
  assistance.

<a id="release-v2-1-0-manual-validation-corrections"></a>
#### Manual-validation corrections

- Scan elapsed time now measures the actual operation with a monotonic clock,
  updates while scanning, and freezes truthfully at completion, failure, or
  cancellation.
- Duplicate review can create a safe-removal Change Plan for selected unwanted
  copies while requiring a known keeper. Confirmed moves go to the excluded
  `.opensorse/duplicate-recovery` area, are journalled, and can be undone while
  conflict checks pass. They are not permanent deletion and do not immediately
  reclaim disk space.
- Errors, warnings, and information use a compact dismissible badge/drawer.
  Dismissal never deletes Advanced Diagnostics evidence.
- Settings distinguish local file analysis/indexing from optional AI. Verified
  loopback endpoints are labelled local; other valid endpoints show an explicit
  remote privacy warning.
- The ordinary UI calls the evidence-backed Knowledge Graph experience
  **Related Files** and explains it without requiring graph terminology.
- Search explicitly reports **Hybrid** or **Hybrid + AI assistance** and states
  that Ollama does not perform the underlying file search.
- Help now covers Getting Started, Scan, Results, Duplicates, Search, Related
  Files, Change Plans, Watched Folders, Workflows, AI/Ollama, Settings,
  Diagnostics, Privacy, and Troubleshooting, with contextual `?` routing.
- The selected-file AI rename and folder-restructure workflows remain behind
  review, confirmation, validation, execution, history, and recovery tests.

<a id="release-v2-1-0-privacy-and-safety"></a>
#### Privacy and safety

Scanning, indexing, OCR, Search, relationships, Related Files, Smart
Collections, and AI suggestions do not modify original files. Only a reviewed,
approved, validated, separately confirmed Change Plan reaches the executor.

Ollama is optional. `localhost`, IPv4 loopback, and IPv6 loopback endpoints are
identified as local. Any other endpoint is treated as remote: bounded data
supplied to an explicit AI request may leave the computer. OpenSorSe adds no
cloud service, account, telemetry, or remote database.

<a id="release-v2-1-0-quality-evidence"></a>
#### Quality evidence

The complete Debug and Release automated suites each pass **1,531 tests with
zero failures and zero skips**. Analyzers, formatting,
documentation/dependency/architecture policies, vulnerability audit, Search
relevance and performance regressions, and win-x64/linux-x64/osx-x64/osx-arm64
builds are release gates. Hosted validation runs on Windows, Ubuntu, and macOS.
Native package workflows inspect and smoke-test their platform artifacts.

Automated and packaged smoke validation is not a claim of broad interactive
testing across every computer, filesystem, Ollama model, OCR installation, or
accessibility technology. Real-world findings are handled as normal maintenance
work.

<a id="release-v2-1-0-trust-and-known-limitations"></a>
#### Trust and known limitations

- Windows and macOS packages are unsigned, and macOS packages are unnotarized,
  unless the GitHub Release explicitly records a verified signature change.
  SmartScreen or Gatekeeper may warn.
- Checksums detect changed bytes; they do not authenticate an unsigned publisher.
- AI reranking does not answer questions, discover files, or create results.
- Ollama does not expose a reliable general loading state through the endpoints
  used, so only installed and provider-confirmed running states are shown.
- Typo matching is conservative and is not language-specific spell checking.
- Safe duplicate removal uses recovery staging rather than OS trash or permanent
  deletion; disk space is not reclaimed immediately.

See [Installation](INSTALLATION.md),
[Search and AI Quality](SEARCH_AND_AI_QUALITY.md), and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).

</details>

## v2.0.0


<details>
<summary>Original milestone summary</summary>

Knowledge Graph and integrated v1.7-v1.9 release. Optional conservative graph projection, isolated schema-1 graph/decision sidecars, bounded browsing/Search context, privacy and recovery.

</details>
Release history: v1.7 Deep Indexing, v1.8 Search, v1.9 Relationships/Smart
Collections, the v2.0 stability design, and the v2.0 Knowledge Graph candidate
are integrated without squashing their version ancestry.

### Added

- Optional, default-off, provider-neutral Knowledge Graph projection over
  stable files, sources, folders, virtual Collections, exact-content document
  sets, and explicit manual entities.
- Isolated schema-1 SQLite derived and decision sidecars with completed
  manifests, active-generation publication, leases/fencing, watermarks,
  privacy floors, recovery points, repair, and corruption/newer-schema
  handling.
- Bounded accessible graph list/detail/evidence/privacy/repair UI and optional
  explainable Search context that remains subordinate to exact/literal ranking.
- Self-contained Windows x64 portable ZIP and per-user installer plus native
  Intel and Apple Silicon macOS app/DMG packages, package startup/shutdown
  probes, and a verified SHA-256 checksum bundle.
- Process-boundary startup/shutdown exception containment, observed indexing
  task faults, strict UTF-8 documentation validation, and defensive validation
  of internal SQLite identifiers.

### Changed

- `main` now includes the preserved v1.7, v1.8, v1.9, v2.0 design, and v2.0
  implementation history developed above v1.6.
- GitHub Actions use maintained Node.js 24 action majors and validate Windows,
  Ubuntu, and macOS. Native release packaging runs separately from ordinary CI.
- Assembly, file, informational, product, installer, and bundle versions are
  consistently `2.0.0` (`2.0.0.0` where four-part metadata is required).
- User, contributor, platform, privacy/security, installation, packaging, and
  release documentation is reconciled to the current implementation.

### Safety, privacy, and limitations

- Search, indexing, OCR/AI analysis, Relationships, Smart Collections, and
  Knowledge Graph operations never modify original files. Explicit reviewed
  Change Plans remain the production mutation boundary.
- Graph facts require retained evidence; ambiguous identities remain separate;
  decisions and forgetting fences survive derived rebuilds.
- Windows artifacts are unsigned and macOS artifacts are unsigned/unnotarized
  unless the official release page explicitly records otherwise. Linux remains
  source-build only for v2.0.0.
- Broad interactive/community validation begins after publication and is not
  claimed by automated package or CI evidence.


<a id="release-v2-0-0"></a>
### Retained release notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_NOTES_v2.0.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe is a local-first desktop application for scanning, searching,
understanding, and safely organizing folders that you explicitly select. It is
not an autonomous file manager: analysis, Search, OCR, AI, relationships,
collections, and the Knowledge Graph do not authorize file changes.

<a id="release-v2-0-0-highlights-since-the-previous-integrated-main"></a>
#### Highlights since the previous integrated `main`

- **Deep Indexing** processes large folders through durable, resumable stages
  with progress, pause, cancellation, retry, storage quotas, cleanup, and
  partial Search coverage.
- **Search** combines exact filenames, paths, metadata, retained document/OCR
  text, tags, summaries, keywords, and optional related-concept evidence. Exact
  and literal matches remain strongest, filters are visible, snippets are
  bounded, and “Why this result?” uses the ranking evidence that actually ran.
- **Indexed-data privacy controls** show what OpenSorSe retained and can forget
  or selectively rebuild generated data without deleting original files.
- **Relationships and Smart Collections** group related files virtually from
  deterministic evidence. User links, rejections, exclusions, and corrections
  persist; collections never move files.
- **Knowledge Graph** is an optional, default-off projection over stable indexed
  evidence. It uses conservative identity rules, completed manifests,
  generation fencing, bounded one-hop browsing, privacy/repair controls, and
  optional explainable Search context.
- **File safety** remains centered on persisted Change Plans, an explicit Apply
  confirmation, immediate preflight, the Operation Journal, verification,
  rollback, recovery, and conflict-aware Undo.
- **Reliability and portability** include atomic stores, cancellation and
  recovery hardening, provider-isolated SQLite, platform capability gating,
  three-host CI, and native runtime builds.

<a id="release-v2-0-0-install"></a>
#### Install

Download artifacts only from the official
[v2.0.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.0.0).

<a id="release-v2-0-0-windows-x64"></a>
##### Windows x64

- `OpenSorSe-v2.0.0-win-x64-setup.exe` installs per-user, creates a Start Menu
  shortcut, registers an uninstaller, and preserves application data when the
  program is uninstalled.
- `OpenSorSe-v2.0.0-win-x64.zip` is the self-contained portable distribution.
  Extract the complete ZIP and run `OpenSorSe.exe`; keep all extracted files
  together.

The v2.0.0 Windows executable and installer are unsigned unless the GitHub
Release explicitly says otherwise. Windows SmartScreen may warn that the
publisher is unrecognized. A SHA-256 checksum verifies bytes but does not
authenticate an unsigned publisher.

<a id="release-v2-0-0-macos"></a>
##### macOS

- Intel: `OpenSorSe-v2.0.0-macos-x64.dmg`
- Apple Silicon: `OpenSorSe-v2.0.0-macos-arm64.dmg`

Open the matching disk image and copy `OpenSorSe.app` to Applications. The
v2.0.0 macOS packages are unsigned and unnotarized unless the GitHub Release
explicitly says otherwise, so Gatekeeper may require an explicit reviewed
override. macOS package startup and non-mutating functionality are validated;
source-file mutation remains disabled where platform capability policy cannot
prove equivalent safety.

<a id="release-v2-0-0-linux"></a>
##### Linux

No `.deb`, `.rpm`, AppImage, Flatpak, or Snap is published for v2.0.0. Linux
x64 remains available through the documented source-build preview. Follow
[Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md).

<a id="release-v2-0-0-verify-downloads"></a>
#### Verify downloads

Download `OpenSorSe-v2.0.0-SHA256SUMS.txt` from the same GitHub Release.

Windows PowerShell:

```powershell
(Get-FileHash .\OpenSorSe-v2.0.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

macOS or Linux:

```bash
shasum -a 256 OpenSorSe-v2.0.0-macos-arm64.dmg
```

Compare the complete value with the corresponding checksum-file line.

<a id="release-v2-0-0-optional-local-dependencies"></a>
#### Optional local dependencies

- Ordinary Search, filtering, explanations, snippets, indexing, relationships,
  and Knowledge Graph browsing do not require Ollama.
- Optional AI is disabled by default. OpenSorSe does not install or start an
  Ollama-compatible service. A custom endpoint can be remote and is therefore a
  real privacy boundary.
- Tesseract 5 and language data are installed separately and are required only
  for enabled OCR recognition. Native text extraction remains available
  independently.

<a id="release-v2-0-0-privacy-and-local-data"></a>
#### Privacy and local data

The index can contain paths, metadata, retained document/OCR text, tags,
summaries, keywords, semantic representations, relationship evidence, graph
facts, user decisions, operational history, and diagnostics. Treat application
data as sensitive. It is not claimed to be encrypted by OpenSorSe.

Ordinary diagnostics avoid complete queries, document paragraphs, OCR text,
summaries, vectors, prompts, secrets, and unnecessary absolute paths. Review
any explicitly exported diagnostic before sharing it. See
[Safety and Privacy](SAFETY_AND_PRIVACY.md) and [v2.0 Security Notes](SECURITY.md).

<a id="release-v2-0-0-compatibility-and-updates"></a>
#### Compatibility and updates

v2.0.0 preserves existing saved scans, catalogs, watched folders, workflows,
plugins, Change Plans, journal/recovery records, Undo behavior, the schema-3
deep index, and v1.9 relationship data. Knowledge Graph data uses separate
schema-1 derived and decision sidecars so rollback can ignore it. Back up
important application-owned data before upgrading and do not overwrite a
running installation.

<a id="release-v2-0-0-known-limitations"></a>
#### Known limitations

- Knowledge Graph is optional, disabled by default, conservative, bounded, and
  not a conversational assistant or general-purpose knowledge graph.
- Relationship and graph context can be incomplete while indexing, projection,
  exclusions, dependencies, repair, or quotas limit coverage.
- External plugins run in-process with current-user permissions. Integrity and
  load-context isolation are not a security sandbox or publisher identity.
- Optional AI quality depends on the separately managed provider and model.
- Windows signing and Apple signing/notarization are not available through the
  current project infrastructure unless the release page explicitly records a
  verified signature.
- Linux has no binary installer for this release, and macOS source-file mutation
  remains capability-gated.

<a id="release-v2-0-0-validation-and-community-testing"></a>
#### Validation and community testing

The release source passed the repository’s automated restore, zero-warning
Debug/Release build, complete tests with zero failures/skips, analyzers,
policies, vulnerability audit, Search/relationship/Knowledge Graph/performance
regressions, four runtime-target builds, native package inspection, and Windows,
Ubuntu, and macOS CI before publication. Exact totals and commits are recorded
in the [v2.0 Validation Report](VALIDATION.md#validation-v2-0) and
[Release Status](RELEASE_STATUS.md).

Broad interactive and community validation begins with this publication; it is
not claimed to have happened already. Real-world defects reported by testers
will be triaged normally and may be corrected in v2.0.x patches or later
releases.

<a id="release-v2-0-0-report-a-problem"></a>
#### Report a problem

Use the [GitHub issue tracker](https://github.com/nishdel/OmniSorSe/issues).
Include the exact version, operating system, operation, expected and observed
result, and reviewed redacted diagnostics where useful. Never attach private
documents, full index databases, raw OCR/document text, secrets, tokens, or an
unreviewed diagnostics bundle.

</details>


<a id="notes-v2-0"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v2.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** v2.0.0 release source; automated/native-package evidence and
interactive/community evidence remain separately reported

v2.0 adds an optional, local, evidence-backed Knowledge Graph over the durable
index and relationship foundation from v1.7–v1.9. It is designed for bounded
inspection and contextual Search, not conversation or autonomous file work.

<a id="notes-v2-0-what-is-included"></a>
#### What is included

- Provider-neutral graph, decision, privacy, query, Search, repair, suggestion,
  diagnostics, projection, and lifecycle contracts.
- A conservative stable graph of files, sources, folders, existing Smart
  Collections, exact-content document sets, and manual entities.
- Typed evidence, deterministic confidence levels, algorithm versions,
  freshness, integrity, origin, and user-decision provenance.
- Isolated SQLite schema-1 sidecars for rebuildable derived graph data and
  non-rebuildable graph-native decisions. The existing `deep-index.db` remains
  schema 3 and is not migrated.
- Completed-manifest ingestion, durable incremental jobs, generation-based
  publication, pause/resume/cancel/retry, expired-claim recovery, fencing, and
  selective repair.
- A bounded, accessible Knowledge Graph page with progress, coverage,
  list/detail inspection, direct neighbors/evidence, manual decisions, privacy,
  and maintenance controls.
- Optional one-hop Search expansion that preserves exact/literal and v1.9
  direct-relationship priority and falls back cleanly when graph data is
  unavailable.
- Privacy inspection, exclusions, index-only forgetting, verified decision
  recovery points, privacy-safe diagnostics, and source-file safety wording.

<a id="notes-v2-0-defaults-and-compatibility"></a>
#### Defaults and compatibility

Knowledge Graph processing is disabled by default and requires informed user
consent. Search context can be disabled separately. OpenSorSe continues to work
without Ollama, OCR, or the graph. No database server is required.

The graph projects existing indexed data and never opens or modifies source
files. v1.9 relationships, Smart Collections, corrections, privacy rules, and
Search behavior retain authority. Existing saved scans, watched folders,
duplicate detection, workflows, plugins, Change Plans, the Operation Journal,
recovery, and Undo keep their prior contracts.

<a id="notes-v2-0-deliberate-limits"></a>
#### Deliberate limits

The release does not implement tag nodes, automatic real-world entity
identity, unrestricted traversal, a graph canvas, a conversational assistant,
autonomous organization, cloud synchronization, or a remote graph provider.
Provider-neutral entity-suggestion contracts and strict bounded validation are
prepared, but no live suggestion producer is wired. Validation is disabled by
default and cannot establish identity.

See [Knowledge Graph](KNOWLEDGE_GRAPH.md) for operational details,
[Compatibility Matrix](KNOWLEDGE_GRAPH_COMPATIBILITY.md) for upgrade/rollback
expectations, and the fully unchecked [Manual Testing](MANUAL_TESTING.md#manual-v2-0)
and historical [Release Readiness](RELEASE_STATUS.md#historical-v2-0-readiness) records. Broad
interactive/community testing begins with publication and is not claimed as
completed here.

</details>

## v1.9.0

Release branch: `v1.9-relationships-context`.

### Added

- Provider-neutral relationship engine, storage, service, Search-expansion,
  evidence, collection, context, timeline, privacy, repair, and diagnostic
  contracts.
- Deterministic bounded relationship discovery from concrete retained signals,
  with versioned algorithms and Low/Medium/High confidence instead of invented
  percentages.
- Embedded SQLite schema 3 for candidate features, relationships/evidence,
  pair corrections, Smart Collections/membership, forgotten projections, and
  aggregate diagnostics, including transactional migration from schema 2.
- Accessible Collections and Related Files surfaces with inspectors, sorting,
  filtering, timeline, manual link/unlink, confirm/reject, always/never,
  rename/pin/merge/split, privacy, rebuild, and repair controls.
- Synthetic deterministic relationship, migration, corruption, Search
  integration, accessibility, and bounded performance regression coverage.

### Changed

- The existing durable `RelationshipAnalysisCompleted` stage now produces
  incremental evidence-backed output and tracks relevant configuration in its
  processor fingerprint.
- Search may add bounded direct related-file context after ordinary v1.8
  ranking. Exact and literal matches remain above context-only results, and a
  per-query control can disable expansion.
- Index privacy inspection, storage breakdown, forgetting, cleanup, and repair
  include relationship-derived data without modifying source files.
- Product, assembly, file, manifest, and About versions are `1.9.0` /
  `1.9.0.0`.

### Preserved boundaries

- Every automatic relationship retains actual evidence and algorithm
  provenance. Semantic similarity alone cannot create a relationship.
- Smart Collections and timelines are virtual index projections; they never
  move or edit source files or invent unrecorded events.
- Ordinary Search remains useful when relationship analysis is disabled or
  unavailable. Ollama remains optional.
- v1.7/v1.8 indexing, Search, catalogs, watched folders, duplicate detection,
  workflows, plugins, Change Plans, Operation Journal, recovery, and Undo keep
  their existing contracts.
- No Knowledge Graph, conversational assistant, database server, tag, package,
  merge, or published release is added.


<a id="notes-v1-9"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.9.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

v1.9 builds directly on the unmerged, validated v1.8 branch. It adds a local,
provider-neutral relationship projection over indexed files while keeping every
existing file-operation safety boundary.

<a id="notes-v1-9-highlights"></a>
#### Highlights

- deterministic, versioned file relationships with retained evidence and
  understandable Low/Medium/High/Confirmed confidence;
- virtual Smart Collections, member inspection, and bounded timestamp timeline;
- Related Files inspection, sorting, filtering, explanations, and provenance;
- persistent manual link/unlink, confirm/reject, always/never, rename, pin,
  merge, split, forget, rebuild, and repair controls;
- optional explainable relationship expansion in Search, with exact and literal
  results still ranked first;
- SQLite schema 3 migration from the v1.8 schema with recovery-copy and
  transactional migration behavior;
- privacy exclusions, file/source/collection forgetting, aggregate diagnostics,
  graph bounds, corruption repair, and synthetic regression coverage.

<a id="notes-v1-9-preserved-behavior"></a>
#### Preserved behavior

The v1.7 durable indexing pipeline and v1.8 Search/ranking/privacy design are
extended, not reimplemented. Existing catalogs, saved scans, watched folders,
duplicate detection, workflows, plugins, Change Plans, Operation Journal,
recovery, and Undo retain their contracts. Ollama is not required.

<a id="notes-v1-9-important-limits"></a>
#### Important limits

Smart Collections never move files. Automatic relationships are conservative
and may miss useful context. Semantic similarity alone cannot create an edge.
This branch has no release tag or package and is not merged to `main`.
Interactive manual testing is not claimed; every item in
`MANUAL_TESTING.md#manual-v1-9` remains unchecked until a maintainer observes it.

</details>



<details>
<summary>Original milestone summary</summary>

Relationships, Context & Smart Collections. Evidence-backed relationships, virtual collections/timeline, user corrections, contextual Search, privacy/repair, schema 3.

</details>

## v1.8.0

Release branch: `v1.8-search-intelligence-privacy`.

### Added

- Provider-neutral Search query, interpreted-filter, candidate, ranking-signal,
  explanation, snippet, coverage, privacy-inspection, forget, and repair
  contracts.
- Deterministic bounded query interpretation for common file type, extension,
  date, size, source, folder, tag, indexing, OCR, semantic, and failure filters
  with an injectable clock and visible removable filters.
- One coherent hybrid ranker with exact/literal tiers, bounded typo tolerance,
  optional semantic supplementation, explicit components, deterministic
  tie-breaking, bounded snippets, and source indicators.
- Search-quality evaluation over a synthetic corpus, relevance metrics, and
  separate bounded performance regression tests.
- User-facing indexed-data inspection, file/source forgetting, per-file
  processing policy, selective clearing, and targeted durable repair controls.
- SQLite schema 2 privacy rules with transactional migration, recovery copy,
  parameterized operations, and corrupt-ranking-data fallback.

### Changed

- Search distinguishes excluded, OCR-waiting, AI-waiting, failed-stage,
  unavailable-index, and incomplete coverage without treating partial results
  as exhaustive.
- Search diagnostics record duration, counts, filters, coverage, and ranking
  stages without complete query text, snippets, extracted content, or absolute
  paths.
- Summary and semantic processing can be disabled independently while filename,
  folder, metadata, text, OCR, filtering, ranking, snippets, and explanations
  remain local and usable without Ollama.
- Product, assembly, file, manifest, and About versions are `1.8.0` /
  `1.8.0.0`.

### Preserved boundaries

- v1.7 indexes migrate without silent loss; existing catalogs, saved scans,
  watched folders, duplicate detection, workflows, plugins, Change Plans,
  Operation Journal, recovery, and Undo remain compatible.
- Forget and repair actions alter only application-owned indexed data. Original
  user files are never deleted or modified.
- No database server, remote query service, improvised encryption,
  conversational assistant, autonomous organization, package, tag, installer,
  merge, or release publishing is added.


<a id="notes-v1-8"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.8.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.8 — Search Intelligence, Quality and Privacy builds on the v1.7
Deep Indexing Foundation.

- One deterministic hybrid pipeline ranks filename, folder/path, type,
  extension, tag, metadata, document/OCR text, summary, keyword, chunk, and
  optional related-concept signals.
- Exact filenames and literal evidence remain stronger than semantic-only
  similarity.
- Conservative local natural-language filters are visible, removable, and
  clearable; ordinary filtering does not require Ollama.
- Results provide a keyboard/click/touch accessible **Why this result?** view
  backed by actual ranking components and bounded retained-data snippets.
- Coverage now distinguishes exclusions, OCR/local-AI waits, failed stages, and
  temporary deep-index unavailability.
- Indexed-data inspection reports retained categories without showing raw
  vectors or complete document contents.
- Confirmed index-only forget, metadata-only, exclusion, clear, retry, file
  rebuild, and source rebuild actions preserve original files and source
  ownership.
- A confirmed clear-all action removes generated compatible/deep Search data
  while preserving source registration and original files.
- SQLite schema 2 adds transactional durable privacy/repair rules with a safe
  migration and pre-migration backup from v1.7 schema 1.
- Query, token, filter, fuzzy, candidate, snippet, malformed Unicode, generated
  field, concurrent request, and diagnostic privacy limits are explicit.
- A deterministic synthetic relevance framework reports top-result, top-k
  recall, reciprocal rank, exact-match preservation, and ordering stability.
- Search remains fully useful without Ollama and degrades to compatible
  filename/metadata coverage when deeper storage is recoverably unavailable.

This release is not a conversational file assistant, learned ranking engine,
cloud Search service, server database, recursive archive Search, or
source-file automation feature.

</details>



<details>
<summary>Original milestone summary</summary>

Search Intelligence, Quality and Privacy. Hybrid ranker, visible filters, explanations/snippets, coverage, index privacy/repair, relevance gates.

</details>

## v1.7.0

Release branch: `v1.7-deep-indexing-foundation`.

### Added

- Provider-neutral durable background-index contracts and a separate embedded
  SQLite provider with schema 1, migrations/backups, integrity checks,
  transactions, WAL/full synchronization, recovery, and disposal.
- Basic, Standard, and Deep indexing policies; stable file identity/content
  fingerprints; incremental invalidation; duplicate content sharing; deletion
  retention; exclusions; storage quota/maintenance; and bounded chunks/text.
- Persistent staged jobs with pause, resume, safe cancellation, retry,
  dependency waiting, prioritization, source removal, rebuild, and
  interruption recovery. Interrupted discovery resumes without resetting
  completed work; paused/cancelled state remains explicit across restart.
- Progressive Search documents/coverage and accurate indexing progress,
  counts, speed, sample-gated ETA, storage usage/breakdown, failures, and
  privacy-safe diagnostics.
- Watched-folder source ownership, automatic eligible-wait recovery, explicit
  corruption/newer-schema recovery copies, and bounded backup sidecar cleanup.
- Expanded unit, integration, persistence, migration, corruption, concurrency,
  cancellation, recovery, ViewModel/accessibility, and bounded synthetic
  performance-regression coverage.

### Changed

- User-facing **Meaning Search** is now **Search**. Stable internal types,
  schemas, APIs, and migration identifiers retain compatible names.
- Search includes an accessible pointer/keyboard/touch/screen-reader help
  affordance and remains usable with an explicit partial-coverage warning.
- Product, assembly, file, manifest, and About versions are `1.7.0` /
  `1.7.0.0`.
- Search/index diagnostics are now an instrumented category.
- Search exposes a bounded failure inspector and direct current-run diagnostics
  navigation; compatible existing Search stays available if the derived store
  requires recovery.

### Preserved boundaries

- Existing JSON settings/catalog/content/Search/history/watch/workflow/plugin/
  Change Plan/Operation Journal data remains compatible.
- No PostgreSQL or database server, conversational Search, final hybrid ranker,
  cloud indexing, autonomous file organization, new mutation path, package,
  tag, installer, or release publishing is added.


<a id="notes-v1-7"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.7.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.7 introduces a durable background-indexing foundation.

- Meaning Search is now named **Search** in the user interface.
- Accessible concise Search help explains names, metadata, text, OCR, tags, and
  related concepts without implementation jargon.
- Basic, Standard, and Deep indexing levels provide conservative progressive
  coverage.
- A provider-independent application boundary uses embedded SQLite locally;
  no database server or PostgreSQL installation is required.
- Durable stage state supports pause, resume, safe cancellation, dependency
  waiting, bounded retry, process-interruption recovery, and completed-work
  reuse.
- Interrupted discovery resumes in the same run; explicit paused/cancelled
  state survives restart; dependency and resource waits resume when eligible.
- Stable identity, content fingerprints, shared content, retention, quota
  maintenance, and compaction bound long-term storage.
- Search shows exact coverage and remains available while deeper processing is
  incomplete.
- Progress exposes active stage/file, counts, speed, gated ETA, and storage.
- Search exposes an inspectable privacy-minimized failure list and direct
  current-run diagnostics.
- Corrupt or newer derived storage does not disable compatible existing Search;
  explicit rebuild preserves a bounded recovery copy before starting fresh.
- Existing JSON stores, APIs, workflows, plugins, watched folders, duplicate
  detection, Change Plans, Operation History, and Undo are preserved.

This release does not deliver conversational Search, a final intelligent
ranking engine, a server database, cloud indexing, or autonomous file changes.

</details>



<details>
<summary>Original milestone summary</summary>

Deep Indexing Foundation. Provider-neutral durable indexing, embedded SQLite, progressive Search, quotas, recovery and controls.

</details>

## v1.6.0

Release name: **Reliability, Performance, and Production Hardening**<br>
Release branch: `v1.6-reliability-performance`.

- Consolidated all application-owned JSON stores onto one bounded, flushed,
  cancellation-safe atomic replacement primitive.
- Added normalized process-local transaction coordination across independent
  store instances without changing persisted schemas.
- Reduced duplicate-detection and Results-query transient allocations and made
  large projection/query paths cancellation-responsive.
- Bounded processing-session history and hardened background-task progress,
  cancellation classification, and observer isolation.
- Made watched-folder initialization/disposal concurrent-safe, awaited owned
  loops, observed background failures, and applied host path semantics.
- Added critical-workflow automation names and polite live status regions.
- Expanded concurrency, recovery, stress, cancellation, persistence,
  lifecycle, accessibility, and repository-policy tests.
- Expanded source CI to Windows, Ubuntu, and macOS with Debug/Release,
  zero-skip, analyzer, style, whitespace, documentation, and patch gates.
- Product/informational version is `1.6.0`; assembly/file/manifest version is
  `1.6.0.0`; About displays `1.6`.
- No tag, installer, package, updater, or published release is created by the
  source integration.


<a id="notes-v1-6"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.6.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.6.0 is a reliability and performance release. It preserves every
v1.5 feature and persisted format while strengthening production behavior.

<a id="notes-v1-6-highlights"></a>
#### Highlights

- One durable, bounded atomic JSON writer across all application-owned stores.
- Process-local cross-instance coordination for complete persistence
  transactions, including safety-critical Change Plans and Operation Journals.
- Faster, lower-allocation exact-duplicate analysis and Results search.
- Responsive cancellation during large result projection and query work.
- Bounded processing-session memory and terminal-safe task progress.
- Idempotent watched-folder initialization/disposal and isolated observer
  failures.
- Host-correct case semantics for watched hints and action planning.
- Accessibility names and live status announcements on critical workflows.
- Windows, Ubuntu, and macOS CI with Debug/Release, zero-skip, analyzer, style,
  formatting, and documentation gates.
- A single runtime product-version source to prevent provenance drift.

<a id="notes-v1-6-compatibility-and-safety"></a>
#### Compatibility and safety

No JSON schema is bumped. Existing settings, scans, tags, searches, AI
decisions, indexes, workflows, watched folders, plugin state, plans, journals,
and history remain compatible. AI remains optional and suggestion-only. File
changes still require a reviewed, approved, validated, explicitly confirmed
Change Plan and remain journalled, recoverable, and conflict-aware for Undo.

<a id="notes-v1-6-release-boundary"></a>
#### Release boundary

This source implementation does not itself claim a package, installer,
signature, tag, or published release. Automated validation and the required
interactive manual smoke testing are complete with no release-blocking issues.
See the [validation report](VALIDATION.md#validation-v1-6) and reusable
[manual checklist](MANUAL_TESTING.md#manual-v1-6).

</details>



<details>
<summary>Original milestone summary</summary>

Reliability, Performance and Production Hardening. Shared atomic persistence, bounded resources, lifecycle/cancellation hardening, accessibility, native CI.

</details>

## v1.5.0

Release name: **Cross-Platform Foundation and Linux Preview**<br>
Release branch: `v1.5-cross-platform-foundation`.

- Added focused platform contracts and capability reporting for path semantics,
  application locations, file identity, filesystem checks, external tools, and
  desktop integration.
- Preserved the Windows local-data layout; added XDG configuration/data/state/
  cache separation on Linux.
- Added Windows volume/file-index and Linux device/inode identity with explicit
  metadata fallback limitations.
- Made Change Plan validation/execution, case handling, confinement, permission,
  same-filesystem moves, rollback, recovery, and Undo platform-aware without
  permitting overwrite or unreviewed mutation.
- Added portable, Windows-compatible, and current-platform recipe filename
  policies; existing recipes retain conservative portable behavior.
- Added exact plugin runtime-identifier constraints for native dependencies.
- Added configured-path and safe `PATH` discovery for Tesseract and explicit
  Windows/Linux desktop-opening adapters.
- Added Settings platform diagnostics, human-readable report export, platform
  matrix, Linux build/manual/troubleshooting guidance, architecture maps, and a
  Windows/Ubuntu CI matrix that publishes no artifacts.
- Product/informational version is `1.5.0`; assembly/file/manifest version is
  `1.5.0.0`; About displays `1.5`.
- No v1.5 tag, installer, package, updater, or published release is created by
  this implementation task.


<a id="notes-v1-5"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.5.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Version: `v1.5`<br>
Release name: **Cross-Platform Foundation and Linux Preview**<br>
Branch: `v1.5-cross-platform-foundation`

OpenSorSe 1.5 makes operating-system behavior explicit and replaceable while
preserving the analysis → review → approval → mutation boundary. Product and
informational version are `1.5.0`; assembly, file, and Windows manifest version
are `1.5.0.0`; About displays `1.5`.

<a id="notes-v1-5-included"></a>
#### Included

- Small platform contracts for path semantics, application locations, file
  identity, filesystem inspection, tool discovery, capability reporting, and
  desktop integration.
- Existing Windows local-data layout is preserved. Linux separates
  configuration, data, state, cache, diagnostics, and plugins using XDG
  locations.
- Windows volume/file-index identity and Linux x64 device/inode identity, with
  an explicit metadata fallback for other architectures and documented
  lifetime limits.
- Platform-aware, root-confined Change Plan validation; non-overwriting,
  same-filesystem execution; journalled verification; compensating Undo.
- Portable, Windows-compatible, or current-platform recipe filename policy.
  Existing recipes default to the conservative portable policy.
- Plugin runtime-identifier constraints. Native payloads without a declared
  supported runtime identifier fail manifest validation.
- Explicit configured-path and safe `PATH` discovery for external Tesseract.
- Windows and Linux desktop-opening adapters with non-fatal unavailable states.
- Settings platform diagnostics and a copyable, secret-free support report.
- Windows/Ubuntu CI source validation. CI builds and tests source; it does not
  publish packages.

<a id="notes-v1-5-support-statement"></a>
#### Support statement

Windows is the locally verified development platform. Linux has implemented
source, XDG, filesystem, watcher, plugin, OCR-discovery, and desktop foundations
and is exercised by the Ubuntu CI definition; it remains a preview until that
workflow and the manual Linux checklist have run successfully in the target
environment. macOS paths are implemented conservatively but the platform is
unverified and is not a supported v1.5 target.

There is no v1.5 installer, updater, tag, package, or published release in this
source task. See the [capability matrix](PLATFORM_COMPATIBILITY_MATRIX.md),
[Linux build guide](LINUX_BUILD_AND_LAUNCH.md), [user guide](USER_GUIDE.md#guide-v1-5),
and [manual checklist](MANUAL_TESTING.md#manual-v1-5).

</details>



<details>
<summary>Original milestone summary</summary>

Cross-Platform Foundation and Linux Preview. Platform adapters, XDG paths, Linux semantics, plugin RIDs, source CI foundation.

</details>

## v1.4.0

Plugin Foundation and Extension SDK.

Release branch: `v1.4-plugin-foundation`.

### Added

- Standalone immutable, asynchronous, cancellation-aware Extension SDK with
  eight bounded analysis/proposal/import/export extension points.
- Strict manifest parsing, controlled manifest-first discovery, runtime and
  host compatibility, deterministic dependency graphs, integrity-change
  lockout, diagnostics, quarantine, and conflict-safe contribution registry.
- Dedicated collectible assembly-load contexts for external plugins with
  bounded initialization/stop, exception containment, and restart reporting.
- Explicit external enable/capability grants and four built-in reference
  plugins for metadata, classification, recipe fields, and JSON export.
- Transactional local ZIP install, fully validated upgrade with previous
  version preservation, dependency-aware confirmed removal, and archive
  traversal/link/native/bounds defenses.
- Exact plugin/version/contribution references in profiles and recipes,
  immutable resolution snapshots, recipe value/action provenance, watched
  fail-closed behavior, and import/export host boundaries.
- Settings > Plugins management, redacted diagnostics export, SDK/author/
  manifest/package documentation, and adversarial/runtime/workflow/UI tests.
- Audience-oriented documentation index, repository/developer/maintainer guides,
  authoritative architecture overview, and four-part Mermaid system map.
- Deterministic repository tests for case-correct relative documentation links,
  Mermaid structure, documentation entry points, production dependency policy,
  and public Extension SDK XML documentation.

### Changed

- Product/informational version is `1.4.0`; assembly/file/manifest version is
  `1.4.0.0`; About displays `1.4`.
- Workflow import/export schema identity advances to 1.4 and preserves exact
  plugin contribution references.
- Current installation/safety guidance, SDK call contracts, subsystem
  lifecycle/invariant comments, and legacy architecture link casing were
  corrected without changing product behavior.

### Safety

- Plugins can analyze, suggest, parse import proposals, or return export bytes;
  they cannot directly mutate user files, approve/apply Change Plans, write the
  journal, or receive the host service container.
- External plugins are disabled until explicit enable and capability grant.
- Missing, incompatible, changed, conflicting, failed, or quarantined
  capabilities fail closed with no silent workflow fallback.
- External code remains in-process with the current user's OS permissions.
  Assembly-load-context isolation is not a sandbox and SHA-256 integrity does
  not authenticate publishers.


<a id="notes-v1-4"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.4.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="notes-v1-4-plugin-foundation-and-extension-sdk"></a>
#### Plugin Foundation and Extension SDK

OpenSorSe 1.4 adds a local-first plugin foundation on branch
`v1.4-plugin-foundation`. It preserves the existing preview, approval, Change
Plan, Operation Journal, recovery, and Undo boundaries.

<a id="notes-v1-4-highlights"></a>
##### Highlights

- A stable `OpenSorSe.Extensions.Abstractions` SDK with immutable, asynchronous,
  cancellation-aware contracts.
- Extension points for metadata, content extraction, classification, recipe
  fields, duplicate evidence, workflow capabilities, configuration import
  proposals, and report export.
- Strict `plugin.json` parsing, controlled discovery, deterministic dependency
  resolution, compatibility checks, integrity-change lockout, and
  conflict-safe contribution registration.
- One collectible assembly load context per external plugin, bounded lifecycle
  calls, exception containment, diagnostics, repeated-failure quarantine, and
  explicit enable/capability grant.
- Local ZIP install, upgrade with rollback preservation, and dependency-aware
  removal. v1.4 has no online marketplace, remote download, or automatic
  update channel.
- Plugin references and exact resolved versions in workflow/recipe snapshots,
  recipe-field provenance in Change Plans, and fail-closed watched/manual
  workflow resolution.
- Four built-in reference plugins: filesystem metadata, extension
  classification, a recipe field, and JSON report export.
- A Plugins panel under Settings for inspection, enable/disable, package
  operations, quarantine state, and redacted diagnostics export.

<a id="notes-v1-4-safety-boundary"></a>
##### Safety boundary

Plugins analyze data or return suggestions and bounded outputs. They receive no
Change Plan execution service, dependency-injection container, settings store,
credential store, or unrestricted mutation API. A plugin cannot approve or
apply file operations. All supported mutations remain host-created proposals
that pass normal validation, review, explicit confirmation, execution,
journaling, recovery, and Undo.

External plugins execute in the OpenSorSe process with the effective operating
system permissions of that process. Assembly-load-context isolation is not a
security sandbox. SHA-256 integrity detects changed installed content but does
not authenticate a publisher or establish trust. Install only plugins whose
publisher and code you trust.

<a id="notes-v1-4-compatibility-and-limitations"></a>
##### Compatibility and limitations

- Host version: `1.4.0`; assembly/file/manifest version: `1.4.0.0`.
- Plugins must target the v1.4 abstractions and declare compatible host/runtime
  versions.
- External plugins start disabled and require an explicit capability grant.
- Disabling or upgrading a plugin can require an application restart because
  .NET cannot guarantee immediate in-process unload.
- There is no out-of-process worker, marketplace, signing authority, plugin
  script engine, UI injection API, background service, or supported direct file
  mutation extension point in v1.4.

See the [user guide](USER_GUIDE.md#guide-v1-4), [plugin architecture](Architecture/10_Plugins/06_Plugin_Foundation.md),
[SDK guide](EXTENSION_SDK.md), and [manual checklist](MANUAL_TESTING.md#manual-v1-4).

</details>



<details>
<summary>Original milestone summary</summary>

Plugin Foundation and Extension SDK. Standalone SDK, eight bounded extension points, local packages, grants, integrity/lifecycle isolation.

</details>

## v1.3.0

Workflow Profiles and Recipe Library.

Release branch: `v1.3-workflow-profiles`.

### Added

- Typed, versioned workflow profiles and declarative sorting recipes with stable IDs, revisions, origins, capability/file/extraction/analysis/AI/plan/notification/scan policies, applicability, templates, fallbacks, normalization, collision/uncertainty policy, and preview examples.
- Five immutable duplicable profile defaults and four immutable duplicable recipe defaults.
- Bounded atomic `workflow-library.json` persistence with schema migration, corruption preservation/diagnostic copy, safe built-in recovery, lifecycle operations, dependency protection, usage inspection, and diagnostic export.
- Constrained field/date template parser and deterministic evaluator with Unicode/case/whitespace policy, portable invalid-character sanitization, reserved-device/length/root/traversal/collision checks, and full preview explanations.
- Immutable effective configuration resolution and historical workflow snapshots for manual scans, saved scans, watched cycles, and watched catalogues.
- Persistent watched-folder profile resolution, multiple permitted recipe selection, constrained overrides, configuration-change reconciliation, and explicit profile-unavailable state.
- Manual scan profile selection, capability/intensity summary, one-time narrowing, and save-adjusted-as-new-profile flow.
- Workflow/recipe provenance on Change Plan actions, including inferred directories, with profile/recipe revisions, values, evidence, deterministic/AI state, warnings, and unresolved fields.
- Versioned human-inspectable profile/recipe import/export with explicit conflict policy and size/depth/schema/dependency/template/capability validation.
- Dedicated Workflows profile/recipe/editor/preview/import/export UI with search, file/origin/capability/archive filters, usage, lifecycle actions, scan/watch routing, and diagnostics export.
- Comprehensive workflow persistence, migration, recovery, template safety, precedence, historical snapshot, Change Plan, AI-gate, transfer, and ViewModel tests.

### Changed

- Watched configuration/catalogue schemas advance to 3/2 for recipe lists, overrides, and workflow revision snapshots.
- v1.2 `default` maps explicitly to General Documents; session-only `current` recipes require deliberate replacement.
- Processing stages honor effective extraction, classification, duplicate, rule, AI, file-selection, and Change Plan settings.
- Product/informational version is `1.3.0`; assembly/file/manifest version is `1.3.0.0`; About displays `1.3`.

### Safety

- Workflow profiles automate configuration and analysis, not approval or file modification.
- Imported recipes cannot execute code, contain destructive recipe rules, escape an approved root, or overwrite a destination.
- Profiles cannot bypass global OCR/AI gates; item-level AI policy is checked before provider requests.
- All mutations continue through v1.1 review, approval, preflight, explicit Apply, journaling, verification, recovery, rollback, history, and Undo.


<a id="notes-v1-3"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.3.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Release name: **Workflow Profiles and Recipe Library**

Development branch: `v1.3-workflow-profiles`

Version metadata: product `1.3.0`, assembly/file `1.3.0.0`, About `1.3`

> Workflow profiles automate configuration and analysis, not approval or file modification.

<a id="notes-v1-3-highlights"></a>
#### Highlights

- A durable, versioned workflow library replaces v1.2's runtime fallback with inspectable profiles and recipes.
- Five canonical profiles ship in source: General Documents, Invoices and Receipts, Photos, Downloads Cleanup, and Minimal Local Processing.
- Four canonical recipes demonstrate conservative document, invoice, photo, and download organization. Built-ins are visible and duplicable but never mutated in place.
- The **Workflows** destination supports search, capability/file-type/origin/archive filters, structured profile editing, recipe design and deterministic previews, lifecycle actions, usage information, import/export, and diagnostic export.
- Manual scans select a profile and may apply session-only constraints or save an adjusted copy.
- Watched folders persist one profile and zero or more permitted recipes. Missing, archived, disabled, or incompatible dependencies enter **Profile unavailable — review configuration** instead of silently using another profile.
- Resolved profile/recipe revisions and effective settings are stored with scan and watched-catalogue snapshots so later edits do not rewrite history.

<a id="notes-v1-3-safety"></a>
#### Safety

Templates are a field whitelist, not an expression language. Evaluation normalizes and sanitizes values, blocks rooted/traversing/out-of-root paths, reserved Windows names, overlong output, unresolved required fields, and occupied destinations. Imported recipes cannot contain executable code, destructive rule actions, or absolute move-rule destinations; organization moves come from the root-confined destination template.

Recipe output enters the existing v1.1 Change Plan factory. Profile/recipe revision, values, evidence, AI-assisted state, warnings, and unresolved fields are retained per proposal. Required directory proposals retain the same provenance. No workflow service calls the executor.

AI remains optional and must pass the global switch, configured model/capability, profile policy, watched/manual constraint, and item policy. AI-derived values are labeled and are never reparsed as template syntax.

<a id="notes-v1-3-compatibility"></a>
#### Compatibility

- The v1.2 profile ID `default` is explicitly mapped to General Documents with a migration warning.
- The session-only recipe ID `current` is not silently persisted. A watched folder using it becomes unavailable until a persistent recipe is chosen.
- Existing v1.1 Change Plans and journals retain schema 1; v1.3 provenance is an additive optional field.
- Existing saved scans without workflow snapshots remain readable.

<a id="notes-v1-3-known-limitations"></a>
#### Known limitations

- Profile/recipe transfer is human-inspectable JSON through the Workflows text area; no online library or synchronization exists.
- Recipe fields are limited to the documented whitelist. Existing deterministic processing supplies names, extensions, filesystem timestamps, and category/document type; recipes do not run arbitrary expressions or scripts. The photo recipe uses filesystem creation time rather than EXIF capture time, and the invoice recipe visibly falls back to `UnknownVendor` when no reviewed vendor field is available.
- Built-in General Documents intentionally has no attached move/rename recipe, so explicit migration from v1.2 `default` cannot unexpectedly create organization proposals.
- Live OCR/provider availability still depends on separately installed/configured local components.
- Interactive GUI, live watcher, OCR, provider, packaging, and Windows permission matrices remain release-checklist work and are not replaced by automated tests.

</details>



<details>
<summary>Original milestone summary</summary>

Workflow Profiles and Recipe Library. Typed profiles/recipes, safe templates, snapshots, assignments, import/export, provenance.

</details>

## v1.2.0

Watched Folders and Incremental Scanning.

Release branch: `v1.2-watched-folders`.

### Added

- Persistent watched-folder configurations with stable IDs, availability/status, subfolder scope, exact/pattern ignores, scan profile, sorting recipe, deterministic/AI switches, notification preferences, quiet period, size/hidden policy, timestamps, queue state, summaries, pending plans, and associated catalogue identity.
- Versioned atomic `watched-folders.json`, `watched-catalogues.json`, and grouped `watched-activity.json` stores with bounds, corruption preservation, schema migration, and missing-store compatibility.
- Replaceable `FileSystemWatcher` event source, canonical event/root validation, per-folder quiet-period debounce, duplicate burst grouping, directory/overflow escalation, and a bounded 256-batch single-reader queue with backpressure.
- Stable Windows file identity and portable best-effort identity, real-filesystem probes, file-stability observations, deferral/retry, and root-confined reparse-safe enumeration.
- Targeted incremental processing that preserves unchanged analysis and selectively reuses metadata, content/OCR cache, SHA-256, classification, duplicate, and rule infrastructure.
- Startup, pause/resume, reconnect, overflow, daily, user-triggered incremental, and full reconciliation workflows.
- Canonical ignore policy for exact paths, directories, filename/extension patterns, hidden/linked/internal/oversized items, and visible built-in temporary/incomplete-download patterns.
- Optional per-folder AI with global/capability/model gates, 12-item requests, a 120-item per-cycle backlog bound, cancellation, unchanged-content avoidance, persisted pending/completed/failed item state, provenance, independent failure, and pending/failed-only retry.
- Operation Journal path/identity correlation and verified post-operation reconciliation to suppress recursive suggestions without disabling watching for a fixed duration.
- **Watched Folders** desktop management, status, actions, grouped activity, precise notifications, explicit configuration-removal confirmation, and review routing.
- Automated configuration, store, ignore, event/debounce, processor, reconciliation, AI, correlation, stability, and ViewModel tests.

### Changed

- Product, assembly, informational, file, manifest, and About versions report `1.2.0` / `1.2.0.0`.
- Dedicated watched catalogues update in place without consuming or evicting entries from the separate opt-in Saved scans catalogue.
- Existing v1.1 deterministic and optional AI suggestions are reused to create reviewable Change Plans.
- Release branches follow `v<version>-<primary-feature>`.
- Late v1.1 Review Changes progress callbacks no longer overwrite the verified terminal execution status.

### Safety

- Watched folders automate detection and analysis, not file modification.
- Watcher events are hints and are reconciled with actual canonical in-root state.
- Overlapping roots are rejected to prevent duplicate ownership and processing.
- Missing/disconnected folders retain configuration, catalogue, and history.
- Ignored files never enter optional AI.
- Watched-folder processing never invokes `IChangePlanExecutionService`; every mutation remains behind existing v1.1 manual review, approval, validation, and explicit Apply.
- Journal-correlated OpenSorSe changes update catalogue state without repeated plans or AI analysis.


<a id="notes-v1-2"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.2.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

Release name: **Watched Folders and Incremental Scanning**

Release branch: `v1.2-watched-folders`

OpenSorSe 1.2 adds persistent, opt-in watched folders while preserving the v1.1 review and execution boundary.

> Watched folders automate detection and analysis, not file modification.

<a id="notes-v1-2-highlights"></a>
#### Highlights

- Persistent **Watched Folders** management with pause, resume, settings, immediate incremental scan, full reconciliation, open-folder, recent activity, review, and safe configuration removal.
- Operating-system watcher events are bounded, debounced hints. OpenSorSe verifies the real filesystem before changing its catalogue.
- Stable Windows file identities where available, with a portable best-effort fallback, detect external rename and move operations without relying only on paths.
- Incremental processing preserves unchanged metadata, hashes, content/OCR cache results, classifications, and duplicate state. Content-changing files alone are re-extracted, re-hashed, and reclassified.
- Full metadata reconciliation finds changes missed during shutdown, pause, disconnection, watcher overflow, or operating-system event loss without automatically reanalysing unchanged content.
- Built-in and configurable canonical ignore rules exclude temporary, incomplete-download, hidden, oversized, linked, internal, exact-path, extension-pattern, and filename-pattern items. Ignored items never enter AI analysis.
- Optional per-folder AI is off by default, additionally requires the global AI/capability gates and an available selected Ollama model, uses batches of at most 12 files and a 120-item per-cycle backlog bound, records per-file pending/completed/failed state, retries only pending/failed work, and fails independently from catalogue updates.
- Deterministic and optional AI suggestions create existing v1.1 Change Plans. They never call the execution service automatically.
- Operation Journal correlation recognizes verified OpenSorSe-generated rename/move/directory events, reconciles affected paths, and suppresses recursive suggestions.
- Versioned atomic `watched-folders.json`, `watched-catalogues.json`, and grouped `watched-activity.json` stores.
- A bounded 256-batch processing queue, per-folder quiet period, cancellation, stability retries, daily reconciliation, availability checks, and truthful busy/overflow/unresolved states.

<a id="notes-v1-2-compatibility"></a>
#### Compatibility

Product/informational version is `1.2.0`; assembly/file/manifest version is `1.2.0.0`. Existing v1.0 and v1.1 settings, saved catalogues, searches, decisions, content cache, semantic index, structure history, Change Plans, Operation Journal, rollback, recovery, and Undo data remain readable.

Overlapping watched roots are rejected. For example, `Documents` and `Documents/Invoices` cannot both be registered. This explicit policy prevents ambiguous ownership and duplicate analysis.

<a id="notes-v1-2-limitations"></a>
#### Limitations

`FileSystemWatcher` cannot guarantee delivery or perfect ordering. OpenSorSe therefore reconciles on startup, resume, reconnect, overflow, at least daily while running, and on demand. A root folder renamed or moved externally is shown as unavailable; v1.2 does not search arbitrary drives to guess its new location.

Stable file identity is strongest on Windows local filesystems. The portable fallback uses creation time and length and can become ambiguous; collisions fall back to path-qualified identities and may be reported as remove/add rather than rename.

Files that keep changing or remain locked are deferred and make the batch incomplete. They are not immediately recorded as permanently failed. Network shares, removable media, permissions, external applications, and power loss can still require a later reconciliation.

The sorting recipe ID `current` resolves only to rules saved in the current application session's Rule Editor. v1.2 does not persist a named recipe library, so those current-session rules must be saved again after restart.

The scan-profile identifier is persisted for configuration compatibility, but v1.2 ships only the existing `default` scan behavior; it does not add a profile-library editor.

Watching operates only while OpenSorSe runs. Changes made while it is closed are detected by the next startup reconciliation, not by a background service.

No v1.2 packaged binary, signature, installer, or interactive platform validation is claimed until the manual checklist is completed.

</details>



<details>
<summary>Original milestone summary</summary>

Watched Folders and Incremental Scanning. Reconciled watcher hints, incremental catalogues, ignores, stability/retry, reviewed suggestions.

</details>

## v1.1.0

Safe File Operations and Robustness stable release.

### Added

- Persisted Change Plans with stable plan/action identities, source file identity snapshots, suggestion provenance, approval/validation state, warnings, conflicts, edit state, scan freshness, and forward-compatible action types.
- Review Changes UI with approve-all-safe, deselect-all, per-action approve/reject, editable filename/destination, action/issue filters, counts, validation, final confirmation summary, explicit Apply, progress, result summary, and Undo.
- Dedicated non-overwriting filesystem gateway and execution service for rename, move, and create-directory actions.
- Durable versioned Operation Journal with pending/running/action/terminal writes, pre/post identities, safe error categories, rollback and Undo facts, AI correlation metadata, and bounded human-readable report export.
- Immediate pre-execution revalidation, deterministic ordering, safe-boundary cancellation, result verification, reverse-order rollback, case-only rename handling, and startup Interrupted Operation inspection.
- Conflict-aware whole-operation and individual-operation Undo, including external modification, occupied original, later-operation dependency, and non-empty created-directory protection.
- `change-plans.json` and `operation-journal.json` atomic local application-data stores, legacy journal-array compatibility, and graceful corrupt-entry recovery.
- Automated safety tests using isolated temporary directories for planning, stale state, collisions, execution, rollback failure, verification failure, cancellation, Unicode/spaces, case-only rename, persistence, migration, restart recovery, partial Undo, history, and ViewModel apply gating.
- v1.1 user, safety, architecture, troubleshooting, manual-testing, and implementation documentation.

### Changed

- Accepted AI rename and folder-structure suggestions now create a Change Plan instead of ending at a decision record.
- Deterministic folder restructuring now routes applied moves through the same Change Plan validator, journal, execution, rollback, and Undo boundary.
- The advanced history destination is named **Operation History** and loads persistent journal records across restarts.
- Product, assembly, informational, file, manifest, and About versions report `1.1.0` / `1.1.0.0`.

### Safety

- No AI generation, parsing, retry, acceptance, or diagnostic path performs a filesystem mutation.
- Destinations are never overwritten and no automatic numeric suffix or implicit conflict resolution is used.
- Approved actions are revalidated immediately before mutation and the executed action list is immutable for that operation.
- Every attempted supported apply is journalled before mutation; successful actions carry verified inverse information.
- Rollback and Undo are reported successful only after verification. Unsafe inverse actions are blocked and journalled instead of overwriting newer data.
- Permanent deletion remains outside v1.1.


<a id="notes-v1-1"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.1.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.1 introduces a complete preview-first safety boundary for supported organization changes.

<a id="notes-v1-1-highlights"></a>
#### Highlights

- Reviewable, editable, persisted Change Plans.
- Rename, move, and create-directory actions with no default overwrite.
- Validation at creation/review and immediately before Apply.
- Durable action-level Operation Journal across restarts.
- Verification, reverse-order rollback, safe-boundary cancellation, and Interrupted Operation inspection.
- Conflict-aware whole-operation and selected-operation Undo.
- Persistent Operation History and human-readable debugging report.
- Accepted AI rename/folder suggestions become plans; AI remains read-only and cannot execute.
- Existing deterministic folder apply is routed through the shared execution/journal service.

<a id="notes-v1-1-compatibility"></a>
#### Compatibility

Product version is `1.1.0`; file/assembly version is `1.1.0.0`. Existing v1.0 settings, saved catalogs, saved searches, tags, decision history, content cache, semantic index, and structure history remain readable. The new stores are optional when absent.

<a id="notes-v1-1-limitations"></a>
#### Limitations

Permanent deletion, unattended organization, live monitoring, cloud AI/synchronization, learning from decisions, and collaborative catalogs remain outside this release. Filesystems are not perfectly transactional. Undo can be blocked by later external or OpenSorSe changes, and partial rollback/manual recovery remains possible.

No v1.1 packaged binary, signature, installer, or interactive platform validation is claimed by these source notes until the manual release checklist is completed.

</details>



<details>
<summary>Original milestone summary</summary>

Safe File Operations and Robustness. Change Plans, Review Changes, journal-before-mutation, verification, rollback, recovery, Undo.

</details>

## v1.0.0

Integrated local-understanding and structure-history release candidate.

### Added

- A self-contained Windows x64 portable release layout with native `OpenSorSe.exe`, official embedded icon, version/product metadata, legal notices, installation guidance, ZIP archive, and SHA-256 checksum.
- A public-facing GitHub README with official branding and commented real-screenshot slots under `docs/images/`; no generated screenshot placeholders are shipped.
- Local, bounded metadata extraction for filesystem, PDF, Open XML, and image metadata with source provenance and per-file failure isolation.
- Optional OCR Beta through capability-detected local Tesseract CLI execution for images and rendered PDF pages, with PdfPig native page text, built-in PDFtoImage/PDFium rasterization, mixed-document page decisions, English/German language checks, and deterministic bounds.
- Page-level OCR provenance, engine/rasterizer-aware cache fingerprints, owned temporary-workspace cleanup, and stale-compatible cache migration.
- A separate default-off AI document-text interpretation capability with bounded page context, strict JSON validation, non-local endpoint warning, and an unverified review-only preview.
- A unified, default-off Advanced Diagnostics framework with one bounded process-memory store, category/status filtering, seven shared viewer tabs, correlation, redaction, copy/export/clear actions, and fully instrumented AI, OCR/text-extraction, and scanning sessions.
- Versioned small-model prompt contracts for rename, folder structure, and document interpretation, with exact Ollama JSON Schemas, deterministic DTO/property ordering, fail-closed grounding and identity validation, and snapshot tests.
- Machine-readable resolved dependency/license inventory, third-party notices, and automated unknown/forbidden-license protection.
- Provenance-aware confirmed, suggested, accepted, and rejected tags sourced from users, deterministic rules, file type/date/folder context, embedded metadata, local OCR, preferences, semantic inference, and optional AI review.
- Default-off local Semantic Search Beta with deterministic feature-hashing vectors, hybrid exact/tag/metadata/native-text/OCR ranking, match explanations, incremental refresh, cancellation, stale-file removal, and clear/rebuild controls.
- Versioned atomic `content-index.json`, `semantic-index.json`, and `structure-history.json` stores with explicit bounds and controlled corrupt optional-index recovery.
- Advanced Structure history page with root/status filters, source/proposed/applied/current snapshots, bounded tree projection, accessible text, and Added/Removed/Moved/Renamed/Unchanged comparison labels.
- Deterministic preview-first root-level folder proposals, separately confirmed bounded apply, current-root revalidation, traversal/reparse/conflict/overwrite protection, rollback attempts, and per-item outcomes.
- Successful-apply repeat protection, incremental proposals for new files, material-change detection, and an explicit **Propose restructuring again** override.
- Contextual Help for Semantic Search Beta and Structure history.

### Changed

- The Desktop output assembly is named `OpenSorSe`, so public builds expose `OpenSorSe.exe` rather than an implementation-oriented executable name.
- Replaced the page-heavy shell with six everyday destinations: Home, Scan, Files, Duplicates, Saved scans, and Settings; advanced tools are grouped separately and Help/About are in the footer.
- Consolidated the saved scan library, saved-scan search, and advanced scan comparison under one Saved scans workspace.
- Exposed local Semantic Search as **Meaning Search (Beta)** from the Files search area rather than as an unrelated top-level destination.
- Redesigned Files around one primary search, an on-demand filter drawer, a bounded file list, and a selection-only details/File Assistant panel.
- Added a persistent bottom status bar with active-operation details and shared cancellation for scans, Meaning Search, and AI requests.
- Added a warmer theme-resource system, semantic feature colors, layered cards, selected navigation state, compact brand mark, friendly empty states, and a metric-tile Home layout.
- Replaced the placeholder shell/window icon with the official compact OpenSorSe mark and added the expanded product name and tagline to the roomier sidebar brand block.
- Made the Files table/details boundary draggable and keyboard adjustable, with 450/320 device-independent-pixel minimums and a validated, persisted 20–50% details-width preference.
- Added subtle alternating Files rows, clearer hover/selection feedback, improved row spacing, and keyboard-resizable table columns.
- Replaced technical user-facing terms such as Results, Saved catalog, Compare snapshots, Semantic Search, Diagnostics, and Operation history with plain-language labels while retaining stable internal type names.
- Results search/filter/status controls remain fixed while the virtualized result list scrolls independently.
- Duplicate View keeps its group list visible and opens selected details in a responsive right-side drawer with Escape/close support.
- Global **Enable AI** and **Advanced features** controls remain visible in the navigation shell and synchronize with Settings.
- Assembly, package, informational, file, and About versions report `1.0.0`.
- Advanced navigation now includes Structure history; Semantic Search Beta remains independently enabled and does not require AI or Advanced mode.
- Existing v0.9.1 settings, catalog schemas, accepted tags, saved searches, and AI decisions remain readable with safe defaults for new settings.
- English/German search normalization now folds diacritics, splits punctuation/extensions, retains ISO dates, and adds conservative suffix variants without a model.
- Folder-structure suggestions now reject selections above the 12-file contract bound before contacting Ollama and state the exact count; no file is silently omitted and no partial plan is shown.
- Updated test-only xUnit packages to remove the obsolete .NET Standard dependency chain flagged by the NuGet vulnerability audit; production dependencies and packaged runtime files are unchanged.

### Safety

- Scanning, OCR, extraction, indexing, duplicate review, diagrams, and AI suggestions never modify source files.
- AI remains default-off, capability-specific, untrusted, and suggestion-only; bounded extracted text can leave the process only through its separate opt-in and explicit one-file request, and no AI result enters a filesystem operation.
- The only new source-file mutation is a deterministic restructuring plan applied after a separate exact-preview confirmation. It moves only listed files under one explicit root and never overwrites or deletes.
- Raw OCR/document text and semantic vectors are excluded from ordinary logs.
- Advanced diagnostic content is retained only in bounded process memory unless explicitly exported, is redacted by default, is cleared on disable/exit, and removes credential-like values even in unredacted mode.
- The portable package now includes copied runtime dependency licence/notice files alongside the reviewed machine-readable dependency inventory.

### Fixed

- Selecting a visible Files row now immediately updates File Assistant context, so rename suggestions no longer remain incorrectly disabled until a later query refresh.
- File Assistant now explains every common disabled state and distinguishes not configured, unchecked, unavailable server, available server, missing model, ready, running, failed, and cancelled readiness.
- Cancelled, failed, unavailable, timed-out, and invalid AI results return to idle and remain retryable with a fresh cancellation source.
- Added explicit connection retry, exact selected-model validation, and display of the actual model used by the latest validated suggestion.
- Switching models after a failed request now causes the next request to use the newly configured exact model rather than retaining stale presentation state.
- Generated content tags no longer trigger a re-entrant Results refresh, and loading them no longer replaces the deterministic extension tag.
- Hiding or clearing the selected-file details panel now returns all available width to the Files table instead of leaving an empty reserved column.
- Navigation falls back safely when Advanced mode hides Structure history or any other selected advanced page.
- Changed roots are rejected between restructuring preview and apply, preventing stale proposals from moving files.
- Failed or preview-only restructuring records cannot activate repeat protection.
- Mixed PDFs no longer skip scanned pages merely because another page contains enough native text.
- Content reprocessing preserves accepted/user tags and same-source rejection decisions instead of replacing them with regenerated candidates.
- Successful OCR capability detection is refreshable and validates every configured Tesseract language before recognition.
- `AvaloniaUI.DiagnosticsSupport` was removed because its resolved package metadata did not declare a license; built-in OpenSorSe diagnostics remain available.


<a id="notes-v1-0"></a>
### Retained version notes

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VERSION_NOTES_v1.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

OpenSorSe 1.0.0 is the first integrated local-understanding release candidate.

- The everyday navigation is now Home, Scan, Files, Duplicates, Saved scans, and Settings. Advanced tools are disclosed separately.
- The official OpenSorSe mark now identifies the window and roomier sidebar alongside “Open Sort and Search” and “Find clarity in your files.”
- The Files table/details divider supports pointer and keyboard resizing, protects both panes, and remembers its validated local ratio; file columns also resize from accessible header handles.
- Saved scan library, history search, and comparison share one Saved scans area.
- Meaning Search (Beta) opens from Files and clearly identifies its local index controls and match explanations.
- Files uses one search field, a filter drawer, and a right panel that appears only after a file is selected.
- A persistent status bar reports scan, Meaning Search, and File Assistant activity and offers cancellation when supported.
- The File Assistant has explicit Ollama/model readiness, connection retry, exact model switching, actual-model display, and recovery after failure or cancellation.
- File Assistant generation now uses compact single-task prompts designed for small local instruction models, exact Ollama JSON Schemas, temperature `0.0`, application-owned extension preservation, opaque folder identities, fail-closed evidence/relationship validation, and at most one related shape-repair attempt. Real-model compatibility is not claimed until the manual 2B/4B/7B-8B matrix is completed.
- Folder-structure AI requests accept at most 12 selected files; a larger selection is rejected in full before Ollama access with its exact count shown, so no source is silently omitted and no partial plan is presented.
- Advanced testers can opt into one live, non-modal diagnostics viewer for AI, OCR/text extraction, and scanning. It provides correlated bounded sessions, stage timing, seven shared tabs, filters, copy/export/clear actions, redacted defaults, and a separate unredacted-content warning switch. Duplicate, search/indexing, rules/organisation, file-operation, and performance categories are registered but honestly marked not yet instrumented.
- Home uses friendly, understandable metric tiles and the UI uses reusable light/dark semantic color resources.
- Results filtering remains visible while rows scroll.
- Duplicate details open in a responsive right drawer.
- AI and Advanced switches are persisted in Settings and continue to gate visible and executable features centrally.
- Bounded metadata extraction is local and read-only.
- OCR is Beta: PdfPig reads native PDF text by page, PDFtoImage/PDFium renders only insufficient pages, and an optional detected Tesseract CLI recognizes images or rendered pages.
- Tesseract language capability is checked explicitly for configured English (`eng`) and/or German (`deu`) data; Tesseract remains externally installed.
- Optional bounded AI interpretation of extracted document text has its own default-off gate and creates only an unverified review proposal.
- Semantic Search is Beta, local, deterministic, explainable, and independent of AI.
- Provenance-aware tags connect metadata, OCR, search, and organization.
- Structure history, repeat protection, and read-only diagrams retain organization context.
- A separately confirmed deterministic restructuring plan can move only reviewed root-confined files; it never uses AI output, overwrites, or deletes.

Index quality may evolve and indexes may need rebuilding after future upgrades. OCR, AI interpretation, and indexing never modify source files. The cache fingerprint includes OCR settings and engine/rasterizer versions so legacy records are safely reprocessed. GPU acceleration, a bundled Tesseract distribution, live Tesseract recognition, and cross-platform packaging are not claimed as verified by this Windows development environment.

</details>



<details>
<summary>Original milestone summary</summary>

Integrated local understanding and structure history. Extraction/OCR, local semantic retrieval, tags, Advanced Diagnostics, structure planning/history, Windows package.

</details>

## v0.9.1


<details>
<summary>Original milestone summary</summary>

Optional AI and feature controls. Default-off gates, strict structured output, provider hardening, diagnostics, Help, Duplicate View.

</details>
Focused optional-AI and interface-complexity refinement; this is not the v1.0 milestone.

### Added

- Default-off global **Enable AI features** and **Show advanced features** settings.
- Independent default-off file-rename and folder-structure suggestion capabilities.
- Central feature requirements shared by navigation, views, commands, Settings, and application services.
- Capability-specific deterministic metadata-only prompt builders with explicit size bounds.
- Strict JSON response contracts, parsing, identity/graph/count/confidence checks, and portable filename/path validation.
- Review, edit, accept, and reject proposal workflow that records local decisions without executing them.
- Typed Ollama missing-model, timeout, cancellation, unsupported-response, malformed/empty/oversized-response, and connection failure handling.
- A bounded, newest-first 500-event process-session diagnostic viewer with severity/category filters, safe details, and copy support.
- Optional live AI request diagnostics in a separate non-modal window, bounded to 20 memory-only records and available only when AI, advanced mode, and the explicit diagnostic switch are all enabled.
- Separate default-off unredacted diagnostic-content opt-in; redacted display retention remains the default and disabling diagnostics clears history.
- Ollama generation now sends a capability-specific JSON Schema aligned with prompt and C# validation contracts, while retaining raw HTTP envelopes separately from extracted assistant content.
- Precise structured-response diagnostics now report actual JSON types, including the former generic invalid-`reason` failure.
- Contextual Help from every major page, with topic-specific workflow, safety, error, and related-topic guidance.
- A responsive **Duplicate View** with per-file details and explicitly requested, capped opening of known files or containing folders through a testable launcher abstraction.
- Reusable severity-labelled status presentation for Settings, AI, Diagnostics, Catalog Search, and Duplicate View.

### Changed

- Raw provider/request diagnostics, detailed logging, historical comparison, detailed diagnostics, and operation-history internals are classified as advanced.
- Essential Ollama endpoint, connection check, model discovery/selection, timeout, and capability controls are visible whenever AI is enabled; only raw request inspection and other technical detail require advanced mode.
- Ollama endpoint normalization accepts safe HTTP(S) base paths and strips known `/api`, `/api/tags`, and `/api/generate` suffixes before building request URIs.
- Provider operations use one request-scoped timeout from 5 through 300 seconds instead of competing `HttpClient` and request timeouts.
- Model discovery preserves the configured exact model and reports it unavailable instead of silently selecting another model.
- AI requests report typed progress stages and preflight the selected exact model before generation.
- Folder prompts use deterministic request-local `item-NNN` identities, report included/omitted counts, and require every included item exactly once.
- Catalog Search now prioritizes search, has one result/status surface, supports clear and rename workflows, and separates saved-search maintenance.
- Settings preserves the current scroll offset across visibility-driven layout changes.
- The earlier mixed AI organization proposal is narrowed to rename only; AI no longer proposes tags, deterministic categories, or file destinations in v0.9.1.
- About and assembly versions report `0.9.1`.
- Generated validation directories are ignored and removed from source control.

### Safety

- AI is disabled by default, and disabled or invalid requests are rejected before provider invocation.
- Ollama remains optional, local-first, and externally managed; a custom endpoint may be remote.
- Requests exclude file content and absolute paths, and model output is always treated as untrusted.
- No AI result renames, moves, creates, deletes, overwrites, or edits a file or folder.

### Fixed

- Hidden-page navigation now rejects stale/direct access and falls back safely when the selected page becomes unavailable.
- Changing Results context now cancels in-flight AI work and clears stale proposals before they can be reviewed against another file.
- A rename edited back to the current filename is treated as no change and is not saved as an accepted decision.
- Provider-diagnostic transport failures are normalized instead of escaping the application boundary.
- Folder validation rejects reserved system-directory names and duplicate logical paths rather than silently normalizing them.
- Undefined internal suggestion kinds and invalid provider timeouts are blocked before network transport.
- Quoted JSON authorization values are redacted from opt-in AI diagnostics.
- Raw AI diagnostic capture can no longer bypass the advanced-mode requirement through an application-service call.
- Diagnostic event capture and clipboard failures remain isolated from scanning and other primary workflows.

## v0.9


<details>
<summary>Original milestone summary</summary>

Historical snapshot comparison. Bounded metadata/tag comparison, scope warnings, filters and cancellation.

</details>
See the preserved [v0.9 release proposal](Implementation_Spec/v0.9/00_v0.9_Release_Proposal.md) and [audit corrections](Implementation_Spec/v0.9/AUDIT_CORRECTIONS.md) for the historical snapshot-comparison release.

## v0.8 milestone

<details>
<summary>Original milestone summary</summary>

Snapshot identity and scope. Catalog schema 2, names, source roots, legacy read compatibility.

</details>

## v0.7 milestone

<details>
<summary>Original milestone summary</summary>

Saved catalog searches. Separate bounded query presets, rerun/remove/reset.

</details>

## v0.6 milestone

<details>
<summary>Original milestone summary</summary>

User-managed result tags. Bounded tag editing, Search refresh, catalog-backed persistence.

</details>

## v0.5 milestone

<details>
<summary>Original milestone summary</summary>

Catalog Search and maintenance. Cross-snapshot metadata/tag Search, removal, two-step clear.

</details>

## v0.4 milestone

<details>
<summary>Original milestone summary</summary>

Opt-in local catalog. Bounded atomic snapshot persistence and historical reopening.

</details>

## v0.3 milestone

<details>
<summary>Original milestone summary</summary>

Optional local suggestions and ranked exploration. Ollama-compatible provider, validated proposals, decisions/tags, deterministic ranking.

</details>

## v0.2 milestone

<details>
<summary>Original milestone summary</summary>

Read-only result exploration. Immutable snapshots, filters, sorting, paging, details, exact-duplicate review.

</details>

## v0.1 milestone

<details>
<summary>Original milestone summary</summary>

Read-only processing foundation. Scan pipeline, metadata, hashing, classification, duplicates, rules/planning, initial desktop/orchestration.

</details>
