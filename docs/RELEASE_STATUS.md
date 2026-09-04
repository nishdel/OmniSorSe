# Release Status

**Document type:** Living release-readiness record

This document answers whether implementation, integration, automated
validation, manual validation, packaging, tagging, and publication are
complete. It is not the product roadmap or the concise version history.

- Use [Release History](../RELEASE_HISTORY.md) for dates, branches, test totals,
  and links to historical evidence.
- Use [Product Roadmap](../PRODUCT_ROADMAP.md) for completed, in-progress,
  planned, research, and backlog work.
- Use [Changelog](CHANGELOG.md) for detailed user-visible changes.

Repository history places the released lineage through v2.4.0 in `main`.
GitHub `main` now also contains the linear v2.5-v2.12 candidate history and the
validated engineering system through an explicit history-preserving source
integration. v2.4.0 remains the latest stable release. The current source is
2.12.0-rc; its exact-source Windows and macOS packages have completed automated
validation and are ready for GitHub prerelease publication. Manual/real-world,
signing, notarization, and v2.12.0 GA gates remain open.

## Current release and source

- **Latest stable release:** v2.4.0 (tagged and packaged).
- **Current GitHub source:** 2.12.0-rc integrated on `main`.
- **Current prerelease:** v2.12.0-rc exact-source packages are validated and
  ready for publication as an unsigned GitHub prerelease for final manual
  validation; macOS packages are also unnotarized.
- **Implemented behavior authority:** [Current State](CURRENT-STATE.md).
- **Readiness evidence authority:** this document.

## Default-source publication state

On 2026-08-20 local `main` advanced from the released v2.4.0 integration commit
`40552b9b2b18637313354713d66593d04cf0d92f` to merge commit
`be536a0354e5ea2c28c826ea24547ebbcdb0432f` without rewriting history. The merge
contains the complete linear v2.5-v2.12 candidate stack plus the accepted
engineering-infrastructure patch.

An atomic normal push advanced `origin/main` from `40552b9` to
`cc6c331c984a6298f74fbc8ed7fb8e0681974ff2` without force or history rewrite.
The remote default HEAD is `main` at that exact commit, and local/remote equality
was verified for `main` and every v2.5-v2.12 branch ref. Those later branches had
been absent because their refs were never pushed; the normal publication created
each at its exact local tip without deleting any branch or tag.

A disposable clone from `https://github.com/nishdel/OmniSorSe.git` checked out a
clean `main` at `cc6c331`, contained `AGENTS.md` and `docs/CURRENT-STATE.md`,
passed a no-cache restore and zero-warning/zero-error Release build, and passed
the full Release suite independently: 1,861 passed, zero failed, zero not
executed. See the retained
[source-publication report](engineering/reports/2026-08-20-source-publication.md)
for the exact evidence boundary.

Initial hosted validation of the published commit was not fully green. In
[Actions run 32360140293](https://github.com/nishdel/OmniSorSe/actions/runs/32360140293),
Ubuntu completed successfully, while both `macos-15` ARM and `macos-15-intel`
failed the same three Application Debug tests: reviewed-organization
execute/Undo, separate-process Explorer companion start, and the single-use
named-pipe test whose Unix-domain socket path exceeded the 104-character host
limit. The Intel job reported 1,005 passed and three failed; Windows was still
running at the observation point.

Correction commit `d81f15482f20d674f77b0aba51ccd00896fee36e`
shortened only the opaque one-time handoff endpoint to `obh-` plus the unchanged
128-bit/32-hex launch ID, for a 36-character logical name. The reviewed-
organization execute/Undo test now injects explicit supported test
capabilities instead of inheriting the production host policy. Production
source-file mutation remains unavailable on macOS; schema 6, Explorer Protocol
1.0, and public interfaces are unchanged.

[Pull request #35's run 32373697544](https://github.com/nishdel/OmniSorSe/actions/runs/32373697544)
passed on `macos-15` ARM, `macos-15-intel`, `windows-latest`, and
`ubuntu-latest`. The pull request was merged normally as
`542e14a50885523543e80c9f593bb35a5f7ef844`, and
[exact-main run 32375495795](https://github.com/nishdel/OmniSorSe/actions/runs/32375495795)
also passed all four hosts. Each run completed the repository's Debug and
Release 1,861-test suites without failures or skips, plus formatting/analyzers,
documentation/dependency policy, advisory audit, runtime restore, and native
package-smoke gates. This closes the hosted automated follow-up; interactive,
installer, signing, notarization, tag, package-publication, and release gates
remain separate.

The later Operation History/startup reconciliation and documentation-navigation
implementation commit `1cf1910` passed zero-warning local Debug and Release
builds and 1,870/1,870 tests in each configuration, plus its focused,
documentation/policy, dependency, vulnerability, and native package-smoke
gates. [Pull request #36](https://github.com/nishdel/OmniSorSe/pull/36) passed
the complete four-host workflow at that unchanged commit and was merged
normally as `3bb3919a780afaf07901e89ecfa10f3a740016c0`.
[Exact-main run 32408658961](https://github.com/nishdel/OmniSorSe/actions/runs/32408658961)
passed the same matrix for the merge. The recorded report-only baseline
`ffc29edec23c557b1c69be5bbc5fa5d77f18c6ba` then passed Windows, Ubuntu,
macOS ARM, and macOS Intel in
[run 32410287837](https://github.com/nishdel/OmniSorSe/actions/runs/32410287837).
The PR needed two controlled Windows reruns and the report-only baseline needed one
after different unchanged timing-sensitive tests failed. Final attempts passed
without changing code, test thresholds, or workflow policy; the retained
[PR #36 report](engineering/reports/2026-08-20-operation-reconciliation-documentation-hierarchy.md)
keeps that validation-infrastructure uncertainty visible.
These later results updated the automated candidate boundary without completing
manual or stable-release readiness.

Later repository-presentation and release-engineering baseline
`d682997fcec529b49559c66634494d35af605f7e` passed the complete four-host
[cross-platform run 32490622011](https://github.com/nishdel/OmniSorSe/actions/runs/32490622011)
with 1,870 tests in Debug and Release on each host. Its exact-source
[native packaging run 32492043785](https://github.com/nishdel/OmniSorSe/actions/runs/32492043785)
produced the Windows ZIP and installer, Intel and Apple Silicon DMGs, CycloneDX
SBOM, and SHA-256 manifest. The workflow verified version/runtime/source
provenance, payload contents, hashes, Windows silent install/start/stop/uninstall
and data preservation, and native macOS mount/composition smoke. Those artifacts
belong only to `d682997` and must be regenerated after any release-preparation
merge. Interactive installer, upgrade, UX, accessibility, optional-tool, and
real-filesystem validation remain manual.

## Per-version validation ledger

| Release | Status | Validation | Scope |
| --- | --- | --- | --- |
| v0.1 Foundation | Complete | Restore, build, automated tests, and manual UI validation complete. | Read-only scan pipeline, metadata, hashing, deterministic rules, Dashboard, Settings, Diagnostics, and supporting application infrastructure. |
| v0.2 Results Exploration | Complete | Restore, build, 233 automated tests, and manual UI validation complete. | Immutable result snapshots, Results Explorer, filtering, sorting, paging, details, and exact-duplicate review. |
| v0.3 Local Suggestions and Ranked Exploration | Complete | Clean isolated restore/build and 251 automated tests passed. Existing repository `obj` folders blocked direct in-place validation. | Optional Ollama integration, validated read-only suggestions, local decision history, session tags, deterministic ranked search, and product polish. |
| v0.4 Opt-in Local Catalog | Complete | Clean isolated restore/build and 260 automated tests passed. Existing repository `obj` folders blocked direct in-place validation. | Bounded opt-in application-data JSON catalog, historical snapshot reopening, accepted-tag restoration, and catalog safety controls. |
| v0.5 Catalog Search and Maintenance | Complete | Clean isolated restore/build and 267 automated tests passed. Existing repository `obj` folders blocked direct in-place validation. | Deterministic catalog-wide metadata search, historical-hit opening, selected entry removal, and two-step clear of application-owned catalog data. |
| v0.6 User-Managed Tags | Complete | Clean isolated restore/build and 274 automated tests passed; zero build warnings/errors. | Bounded manual tag add/remove, protected deterministic tags, immediate search integration, and catalog-backed persistence. |
| v0.7 Saved Catalog Searches | Complete | Clean isolated restore/build and 283 automated tests passed; zero build warnings/errors. | Bounded atomic named queries, current-catalog rerun, selected removal, two-step corruption-recovery reset, and no persisted hits. |
| v0.8 Snapshot Identity and Scope | Complete | Clean isolated restore/build and 290 automated tests passed; zero build warnings/errors. | Catalog schema 2, schema-1 read compatibility, bounded names/source roots, and Saved Catalog/search identity context. |
| v0.9 Historical Snapshot Comparison | Complete | Post-implementation audit: clean isolated restore/build and 330 automated tests passed; zero build warnings/errors. | Bounded deterministic metadata/tag comparison, scope compatibility, cancellation, filters, historical opening, stale-state hardening, and persistence/safety hardening. |
| v0.9.1 Optional AI and Feature Controls | Implementation and corrective pass complete; manual UI verification pending | Redirected-artifact restore/build and 453 automated tests passed; zero build warnings/errors. Direct in-place generated folders remain host-write-protected. | Default-off AI/advanced controls, constrained suggestions, hardened Ollama transport/setup, bounded diagnostics, contextual Help, consistent statuses, corrected Catalog Search, and responsive Duplicate View. |
| v1.0 Integrated Local Understanding | Gold Candidate implementation and automated/package validation complete; manual GUI, OCR, Ollama, model-matrix, and workflow verification pending | Exact restore, Debug/Release builds, and 627 automated tests passed in both configurations with zero warnings/errors. NuGet vulnerability audit is clean. A fresh self-contained win-x64 publish was inspected and packaged locally; interactive GUI validation is not claimed. | Unified bounded Advanced Diagnostics, hardened structured local-AI contracts, official native branding, public README, self-contained portable ZIP, six-destination shell, Meaning Search, OCR/provenance, deterministic restructuring, and history. |
| v1.1 Safe File Operations and Robustness | Stable source implementation and automated validation complete; manual UI/platform/package verification pending | Exact restore, Debug/Release builds, and 659 automated tests passed with zero warnings/errors. | Persisted Change Plans, Review Changes, immediate preflight, non-overwriting rename/move/create, durable Operation Journal, verification, rollback, interruption recovery, conflict-aware Undo, Operation History, and report export. |
| v1.2 Watched Folders and Incremental Scanning | Source implementation and automated validation complete; manual GUI/filesystem/platform/package verification pending | Debug/Release builds and 724 automated tests passed with zero failures/skips; `git diff --check` clean. | Persistent watched roots, bounded debounced hint queue, stability deferral/retry, incremental catalogue updates, offline/full reconciliation, canonical ignores, per-item optional AI retry state, v1.1 Change Plan reuse, self-event correlation, grouped activity, and Watched Folders UI. |
| v1.3 Workflow Profiles and Recipe Library | Source implementation and automated validation complete; manual GUI/filesystem/provider/platform/package verification pending | Debug/Release builds and 761 automated tests passed with zero failures/skips; formatting and diff validation clean. | Persistent typed profiles/recipes, safe templates/previews, immutable resolution snapshots, watched/manual integration, Change Plan provenance, profile-aware AI gates, import/export/recovery, Workflows UI, and documentation. |
| v1.4 Plugin Foundation and Extension SDK | Source implementation and automated validation complete; manual GUI/filesystem/hostile-package/runtime/platform/package verification pending | Debug/Release builds and 836 automated tests passed with zero failures/skips; formatting, documentation, Mermaid-structure, dependency-policy, SDK-documentation, and diff validation clean. | Stable SDK, eight bounded extension points, strict discovery/manifests/dependencies/integrity, in-process lifecycle isolation, explicit grants, local packages, built-in references, workflow/Change Plan provenance, Plugins UI, and documentation. |
| v1.5 Cross-Platform Foundation and Linux Preview | Source implementation complete; local Windows automated validation and hosted Ubuntu execution status recorded below; manual Linux desktop/filesystem validation pending | Final exact build/test/format/documentation results are recorded in the validation baseline below. CI is defined for Windows and Ubuntu and publishes no artifacts. | Platform contracts/capabilities, Windows/Linux path and identity adapters, XDG persistence, platform-aware execution, recipe filename modes, plugin RIDs, safe external-tool discovery, desktop adapters, diagnostics, Linux instructions, and CI. |
| v1.6 Reliability, Performance, and Production Hardening | Source implementation, local and hosted automated validation, and required interactive manual smoke validation complete | Clean restore; Debug/Release builds with zero warnings/errors; 895 tests passed in each configuration with zero failures/skips; analyzer/style/format/docs/dependency/diff gates clean; local runtime-target builds and native Windows/Ubuntu/macOS CI passed. The maintainer completed the required interactive smoke testing with no release-blocking issues. See [v1.6 Validation Report](V1.6_VALIDATION_REPORT.md). | Atomic persistence, cross-instance coordination, performance/memory/cancellation hardening, watcher/task/observer lifecycle reliability, host-independent path syntax, accessibility, diagnostics/version cleanup, and 45 additional test cases. |
| v1.7 Deep Indexing Foundation | Source implementation and local automated validation complete; exact hosted evidence is not self-recorded in the repository; interactive manual validation is not claimed | Clean restore; zero-warning Debug/Release builds; 987 tests passed in each configuration with zero failures/skips; analyzer/style/format/docs/dependency/diff gates clean; advisory audit clean after pinning SQLitePCLRaw 2.1.12; four runtime-target builds passed. See [v1.7 Validation Report](V1.7_VALIDATION_REPORT.md). | Provider-independent durable indexing, embedded SQLite schema/recovery, Basic/Standard/Deep policy, progressive Search, progress/control/storage UI, privacy-safe diagnostics, naming/accessibility, and expanded recovery/concurrency/performance coverage. |
| v1.8 Search Intelligence, Quality and Privacy | Source implementation and local automated validation complete; exact hosted evidence is not self-recorded in the repository; interactive manual validation is not claimed | 1,086 tests passed in each Debug/Release configuration with no failures/skips; all recorded automated gates are in [v1.8 Validation Report](V1.8_VALIDATION_REPORT.md). | Deterministic hybrid ranking, constrained visible filters, explanations/snippets, richer progressive coverage, relevance measurement, indexed-data inspection/forgetting, selective repair, Search hardening, and AI-optional behavior. |
| v1.9 Relationships, Context & Smart Collections | Source implementation and local automated validation complete on its dedicated branch; interactive manual validation is not claimed | 1,128 tests passed in each Debug/Release configuration with zero failures/skips; all recorded local gates are in the [v1.9 Validation Report](V1.9_VALIDATION_REPORT.md), and the [manual checklist](MANUAL_TESTING_v1.9.md) remains unchecked. | Provider-neutral deterministic relationships, evidence/confidence, virtual Smart Collections/context/timeline, user corrections, contextual Search, index-only privacy/repair, SQLite schema 3, accessible UI, and bounded graph/performance controls. |
| v2.0 Knowledge Graph | Integrated into `main` by explicit history-preserving release merge after complete local and exact-tip hosted validation; broad manual/community testing begins with publication | Non-incremental zero-warning Debug/Release builds and 1,486 tests passed in each configuration with zero failures/skips; Search, Knowledge Graph, indexing, relationship, migration/recovery, concurrency/cancellation, performance, policy, vulnerability, and four-runtime cross-target gates passed. Exact-tip Windows, Ubuntu, and macOS CI passed. The Windows portable ZIP and unsigned installer passed controlled package validation. See the [v2.0 Validation Report](V2.0_VALIDATION_REPORT.md). The [release-readiness](RELEASE_READINESS_v2.0.md) and [manual](MANUAL_TESTING_v2.0.md) checklists remain fully unchecked. | Optional conservative graph projection, isolated schema-1 derived/decision sidecars, completed manifests, durable recovery/fencing, bounded browsing and Search context, privacy/repair, and accessible MVVM UI. |
| v2.1 Search & AI Quality | Released as v2.1.0 from `v2.1-search-ai-quality` through a history-preserving merge into `main` | Non-incremental zero-warning Debug/Release builds and 1,531 tests passed in each configuration with zero failures/skips. Policy, vulnerability, four-runtime, exact-main hosted, native-package, and checksum gates are release records. Broad interactive validation across arbitrary hosts/models is not claimed. | Deterministic filename relevance and typo quality, optional bounded Ollama ordering of known results, model/failure clarity, truthful scan/index progress, result actions, safe duplicate recovery, dismissible notifications, privacy wording, Related Files guidance, and contextual Help. |
| v2.2 Media Intelligence | Released as v2.2.0 from `v2.2-media-intelligence` through a history-preserving merge into `main` | Non-incremental zero-warning Debug/Release builds and 1,603 tests passed in each configuration with zero failures/skips before integration. Search/media/index/migration/duplicate/privacy/accessibility/performance and four-runtime compile gates passed. Controlled Windows native-provider evidence includes real Tesseract OCR, ffprobe/ffmpeg media processing, and schema-3-to-4 migration; broad interactive and native Linux/macOS media validation is not claimed. | First-class bounded image/audio/video evidence, EXIF/GPS, OCR, lazy thumbnails, optional ffprobe metadata, optional capped ffmpeg frames, unified Search, conservative media relationships, scan ETA, batch duplicate review, clearer navigation/privacy, and schema 4. |
| v2.3 Content Intelligence & Local Understanding | Released as v2.3.0 from `v2.3-content-intelligence` through a history-preserving merge into `main` | Non-incremental zero-warning Debug/Release builds and 1,637 tests passed in each configuration with zero failures/skips. Search/Content Intelligence/transcription/media/index/migration/privacy/accessibility/performance and four-runtime compile gates passed. Controlled Windows-native evidence includes official whisper.cpp 1.9.2 audio/video transcription, Transcript-to-Search, cancellation, ffprobe/ffmpeg, and a genuine schema-4-to-5 migration; native Tesseract was not repeated and broad interactive/native Linux/macOS validation is not claimed. | Bounded deterministic topics/textual entities/extractive summaries with provenance, schema 5, grounded Search and cross-media Related Files signals, generic-topic suppression, and an optional user-managed whisper.cpp CLI/model process adapter. No bundled model/runtime or visual-description provider. |
| v2.4 OmniSorSe Transition & Explorer Foundation | Released as v2.4.0 from `v2.4-omnisorse-transition` through a history-preserving merge into `main` | Non-incremental zero-warning Debug/Release builds and 1,671 tests passed in each configuration with zero failures/skips. Genuine Windows published-v2.3 profile reuse and installer transition, external two-process protocol lifecycle/security, four-runtime compile, exact-main, and native packaging gates passed. Broad interactive accessibility and native Linux/macOS protocol execution are not claimed. | Active OmniSorSe branding with compatibility-in-place legacy profiles/schema 5 and a dormant authenticated/source-scoped/bounded/read-only Explorer Protocol v1 for the future optional OmniExplorer. |
| v2.11 Supported Runtime & Platform Readiness | Published from GitHub `main` as unreleased candidate source; not tagged, packaged, or published as a GitHub Release | Local SDK 10.0.400 validation: no-cache restore; zero-warning non-incremental Debug/Release builds; 1,832 tests passed in each configuration with zero failures/skips; formatting/analyzer/policy gates clean; four-RID self-contained publish and Windows package smoke passed. Native Linux/macOS execution, installer lifecycle, signing/notarization, and interactive validation remain unperformed. The [manual addendum](MANUAL_TESTING_v2.11.md) remains unchecked. | Moves the complete v2.10 product baseline to net10.0, strengthens package runtime/RID/source evidence, and preserves schema 6, Protocol v1, product behavior, conservative platform mutation, and dependency boundaries. |
| v2.12 Trusted Relationships & Context | Current 2.12.0-rc source on GitHub `main`; exact v2.12 branch ref also retained; exact-source packages validated and ready for GitHub prerelease publication, not stable/GA | Exact integrated-tree validation passed forced no-cache restore; zero-warning Debug/Release builds; 1,861 tests in each configuration with zero failures/skips; focused relationship, Search, SQLite lifecycle/backup/Forget, Explorer, UI/accessibility, 100k-scale, documentation/configuration, formatting, policy, 18-project vulnerability audit, Skill validation, diff/fsck, and native local `win-x64` publish/smoke gates. A clean remote clone independently passed no-cache restore, Release build, and 1,861/1,861 Release tests at the original publication commit. After the first published-main run exposed three macOS portability/fixture failures, correction commit `d81f154` and exact-main merge `542e14a` passed the full four-host matrix. Later reconciliation/navigation commit `1cf1910` passed 1,870 Debug and Release tests locally; exact-main merge `3bb3919`, report-only commit `ffc29ed`, and exact-main baseline `d682997` each passed the complete hosted matrix. The exact-source `d682997` package workflow validated the complete six-file bundle, Windows scripted installer lifecycle/data preservation, and native macOS package smoke. The final release-preparation merge must be rerun and packaged at its own exact main commit. The [v2.12 manual addendum](MANUAL_TESTING_v2.12.md) remains fully unchecked; no interactive quality, accessibility, OmniBrille, removable-source, or broader native cross-platform identity claim is made. | Extends the existing schema-6 relationship authority with capped evidence families, reversible pair authority, graph-independent direct Related Files, bounded candidate/reanalysis work, aggregated Protocol 1.0 output, and `.oms-state` format 2 for authored Smart Collection state. |

## Release/readiness boundary for current source

[Current State](CURRENT-STATE.md) owns the concise implemented-behavior
boundary. This section records only the release and validation implications.

GitHub `main` now contains the v2.12 implementation candidate on top of the
linear v2.5-v2.11 release-candidate stack. It targets .NET 10 LTS and
strengthens the existing relationship authority without changing schema,
protocol, AI, or mutation authority. v2.4.0 remains the latest stable release.
The v2.12.0-rc package and scripted installer-lifecycle gates have passed; this
does not satisfy the remaining manual/native-interactive, signing, notarization,
or v2.12.0 GA gates.

OmniSorSe v2.4.0 is the current release, based on the safe, local-first
OpenSorSe v2.3.0 release. It continues understanding,
monitoring, searching, and organizing explicitly selected folders. The v1.6
production-hardening and cross-platform foundation remains intact; v1.7 adds
durable progressive background indexing, and v1.8 adds bounded Search
intelligence and index-only privacy/repair controls. v1.9 adds evidence-backed
relationships and virtual Smart Collections without granting any new
source-file mutation authority. Reusable workflows and plugin contributions
configure scanning and analysis but do not grant mutation authority.

v2.0 added an optional Knowledge Graph
projection over those retained facts. It is disabled by default, does not open
source files, and keeps v1.9 schema-3 data authoritative. Its isolated schema-1
graph and decision sidecars, UI, and Search context remain derived,
privacy-bounded behavior rather than source-file authority.

v2.4 changes active identity while deliberately retaining schema 5, established
OpenSorSe profile paths, internal namespaces, installer AppId/install directory,
and macOS bundle identifier. Its optional Explorer Protocol is dormant until an
explicit session is requested, exposes no writes or arbitrary paths, creates no
network listener, and does not mean OmniExplorer is implemented or released.

The current Desktop workflow does not:

- Let AI directly rename, move, delete, overwrite, create, or otherwise modify source files. Optional Ollama generates only validated rename, logical folder-structure, or bounded document-text interpretation suggestions. Accepted organization suggestions create a non-mutating Change Plan.
- Expose the historical generic executor through the Desktop, execute raw rule actions, delete duplicates, write document metadata/tags, or run autonomous organization.
- Contact Ollama when AI is disabled or merely because the global AI switch is enabled. Provider requests additionally require an enabled capability, valid context, endpoint, and model.
- Treat catalog or structure comparison as certainty. Stored snapshots and semantic similarities are bounded review aids, not live filesystem truth.

Scanning, duplicate review, extraction, OCR, tagging, indexing/search, Knowledge Graph projection, comparison, diagrams, and AI generation are non-mutating. Supported v1.1 mutations are rename file, move file, and create directory, exclusively through a user-reviewed Change Plan and dedicated execution service. Immediate revalidation rejects stale/invalid/linked/conflicting/occupied paths, overwrite is disabled, every attempt is journalled, success is verified, and safe inverse operations are recorded. AI output cannot execute.

> OmniSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

> Watched folders automate detection and analysis, not file modification.

> Workflow profiles automate configuration and analysis, not approval or file modification.

> Host-supported plugin contracts analyze or propose; they do not grant mutation authority. Third-party plugins remain trusted in-process code rather than sandboxed publishers.

Watcher APIs are treated as fallible hints. Enabled roots are reconciled on startup, resume, reconnect, overflow, at least daily while running, and on demand. Missing storage retains configuration/catalogue/history; overlapping roots are rejected.

Duplicate View may, only after an explicit user command, pass a validated current-scan path to the operating-system shell. Each action is capped at five targets, uses no constructed shell command, reports partial failures, and performs no OpenSorSe filesystem mutation.

OpenSorSe-owned bounded JSON stores may retain settings, logs, AI review decisions, optional catalog snapshots/tags, saved queries, extracted native/OCR text, deterministic search representations, structure history, plugin state/packages, Change Plans, and the Operation Journal under local application data. Released v2.3 uses the provider-isolated SQLite schema-5 index for content-hash-shared bounded media and Content Intelligence evidence, plus bounded relationship-term projection and schema-1 graph/decision sidecars. Current persistence, mutation, plugin, media, and network boundaries are detailed in [Safety and Privacy](SAFETY_AND_PRIVACY.md).

## v1.8 validation

The exact clean local automated results, independently parsed Debug and Release
TRX totals, relevance/performance gates, target builds, and advisory audit are
recorded in the [v1.8 Validation Report](V1.8_VALIDATION_REPORT.md). The report
explicitly leaves exact-tip hosted evidence to a post-commit handoff; that
hosted result is not self-recorded in the repository. No interactive manual
scenario is marked complete.

## v1.7 validation

The final clean local sequence passed restore, zero-warning Debug and Release
builds, and **987 tests in each configuration** with zero failures and zero
skips. Analyzer, style, whitespace, documentation/dependency/architecture,
vulnerability, patch, artifact, privacy, and four-runtime target-compilation
gates passed. The validation report leaves exact immutable
push/synchronization and native Windows/Ubuntu/macOS GitHub Actions evidence to
the post-commit handoff; that hosted result is not self-recorded in the
repository. Interactive manual validation is not claimed. See the
[v1.7 Validation Report](V1.7_VALIDATION_REPORT.md).

## v1.6 validation

The clean final local sequence passed restore, zero-warning Debug and Release
builds, and **895 tests in each configuration** with zero failures and zero
skips. Analyzer, style, whitespace, documentation, dependency-vulnerability,
patch, and tracked-artifact gates passed. Fresh Release target builds succeeded
for Windows x64, Linux x64, macOS x64, and macOS ARM64. Native Windows, Ubuntu,
and macOS CI also passed. The maintainer subsequently completed the required
interactive manual smoke testing and reported no release-blocking issues. Exact
automated and manual status is recorded in
[V1.6 Validation Report](V1.6_VALIDATION_REPORT.md).

## v1.5 validation baseline

The final v1.5 local Windows validation on 2026-07-27 completed a standard
solution restore; Debug and Release builds both succeeded with zero warnings
and zero errors. The complete suite passed **850 tests** in each configuration
with no failures or skips: Core 58, Scanner 61, Rules 68, Executor 61,
Application 420, and Desktop 182. This exceeds the v1.4 baseline of 836.

Five focused Windows tests passed for native identity, preserved application
paths, Windows/portable filename semantics, and safe/failing case-only rename
hops. Fifteen Linux-compatible deterministic tests passed on Windows for Linux
XDG/name/case policy, confinement, fallback identity, tool discovery,
permission/cross-filesystem preflight, native plugin RID constraints, OCR
discovery, platform diagnostics, desktop delegation, and recipe policy
persistence. A runtime-specific `linux-x64` restore and framework-dependent
publish produced 74 files (43,192,302 bytes), an ELF apphost, and a
`linux-x64` dependency target; the temporary output was then removed.

No usable local Linux distribution, container engine, or graphical session was
available, so the complete suite and desktop were **not executed on Linux**.
The GitHub Actions definition was inspected and targets `windows-latest` and
`ubuntu-latest` for restore, both builds, complete tests, formatting, and
documentation/dependency policy without publish, upload, or release steps; an
actual hosted workflow run is not claimed. Formatting, diff whitespace,
documentation links/Mermaid, project dependency policy, required documentation
entry points, SDK documentation, artifact, machine-path, and Git-state checks
were clean.

The exact standard in-place restore completed successfully. Current-source Debug and Release builds both succeeded with zero warnings and zero errors. The full suite passed 836 tests in each configuration with none skipped: Core 51, Scanner 61, Rules 68, Executor 60, Application 417, and Desktop 179. Inherited v1.1/v1.2/v1.3 coverage still validates the complete Change Plan/execution/journal/recovery/Undo boundary, watched folders, workflows, templates, import, AI gates, and presentation behavior. v1.4 adds malformed/oversized manifest and discovery coverage, dependencies/cycles/version/compatibility/integrity, hostile packages/upgrade/rollback/removal, external load-context behavior, lifecycle exception/timeout/cancellation/quarantine/conflicts, capability-gated registration, every extension point, workflow/watcher fail-closed resolution, immutable plugin provenance, template confinement, Change Plan state, Plugins ViewModel actions, repository dependency policy, case-correct documentation links, Mermaid structure, documentation entry points, and SDK XML-documentation coverage.

Source and compiled assembly metadata report product/informational version `1.5.0` and assembly/file/manifest version `1.5.0.0`; About displays `1.5`. A v1.5 package, signature, installer, updater, tag, release, or interactive Linux GUI validation is not claimed by this source implementation.

Tesseract is not installed or discoverable in this development environment, so live recognition was not claimed. Automated tests cover version/language detection, argument construction, cancellation, timeout, empty/oversized output, missing languages, mixed-page coordination, cleanup, and provider isolation through fakes. The PDF renderer itself was exercised in process against a generated real PDF.

## Documentation status

The architecture directory contains both current implementation documentation and longer-term design material. The 1.2/1.3/1.4/1.5 documents identify watched configuration/lifecycle, workflow portability, plugin runtime constraints, platform semantics/identity/capabilities, XDG persistence, template safety, immutable history/provenance, reconciliation, AI rules, Change Plans, journal, review, execution/rollback/Undo/recovery, and existing content/OCR/semantic/AI/structure components. Rich media/archive readers, relational database architecture, online plugin services/sandbox/signing authority, broad localization, cloud indexing, signed installers, and automated publishing remain design material unless a release specification explicitly marks them implemented.

## Current stable release

OmniSorSe v2.4.0 is the current stable Transition & Explorer Foundation release. It
changes the active product/executable/package identity while continuing to use
the established OpenSorSe profile, schema 5, and compatibility identities. Its
dormant Explorer Protocol v1 provides an authenticated, source-scoped, bounded,
read-only local interface for a future separate OmniExplorer without adding a
renderer, network listener, or direct database access. Automated/native package
evidence is not a claim of broad interactive validation on every host,
filesystem, display scaling, or accessibility technology. See the
[v2.4 release notes](RELEASE_NOTES_v2.4.0.md),
[transition/protocol guide](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md),
and [manual checklist](MANUAL_TESTING_v2.4.md). OpenSorSe v2.3.0 and earlier
documents, tags, releases, and validation records remain historical evidence.

## Current development

OmniSorSe v2.10 **Production Hardening & Operational Resilience** was implemented
on `v2.10-production-hardening-operational-resilience` from the committed v2.9
release candidate and is now included in the current 2.12.0-rc source on GitHub
`main`. It has no standalone v2.10 tag or package.
It adds single-writer profile ownership, fail-closed mutation/recovery stores,
bounded hostile-PDF handling, reviewed logical state export/restore, complete
Forget coordination, bounded health/lifecycle checks, and exact build
provenance. Schema remains 6, Explorer Protocol remains v1, OmniBrille is not
modified, and no production dependency or new product capability is added.
Automated results are recorded only after definitive validation; every item in
the [master manual checklist](MANUAL_TESTING_v2.10.md) remains unchecked. See
[v2.10 Production Hardening](PRODUCTION_HARDENING_v2.10.md) and the
[operational runbooks](OPERATIONAL_RUNBOOKS_v2.10.md).

Definitive Windows-host automated validation passed a forced no-cache restore,
non-incremental Debug and Release builds with zero warnings/errors, and 1,829
tests in each configuration with zero failures/skips. Focused Release reruns
passed profile/configuration/policy (34), mutation/recovery (66), parser/prompt/
health/Explorer/workflow (144), SQLite backup/indexing/performance (92),
discovery/Smart Tag/workflow (165), desktop accessibility/navigation (111),
SQLite Smart Tag authority (7), migration/recovery/Forget (31), and performance
(24). Formatting/style/analyzers, the direct/transitive vulnerability audit,
release-script syntax, and diff validation passed. Release compilation passed
for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, with injected 2.10.0
version/source metadata verified on the Windows artifact. This does not claim
native Linux/macOS runtime or interactive desktop validation.

OmniSorSe v2.9 **Reviewed Intelligent Organization** was implemented on
`v2.9-reviewed-intelligent-organization` from the committed v2.8 release
candidate and is now included in the current 2.12.0-rc source on GitHub `main`.
It has no standalone v2.9 tag or package. Existing persistent recipes
now preview an explicit bounded stable-ID selection from
Files, Search, or current Saved View results with trusted evidence, action
budgeting, collision/privacy/readiness explanations, and fresh validation before
the existing Change Plan boundary. Schema remains 6, Explorer Protocol remains
v1, and no production dependency or autonomous mutation is added. Interactive
desktop, screen-reader, DPI, permissions/filesystem, partial-failure, and native
platform execution remain manual release gates.

Definitive Windows-host automated validation passed a forced restore,
non-incremental Debug and Release builds with zero warnings/errors, and 1,795
tests in each configuration with zero failures/skips. Focused Release reruns
passed reviewed organization/workflow/Change Plan (72), Search/facet/Saved
View/Smart Tag/index/content/media (404), Explorer/companion (48),
migration/recovery/reconciliation/Undo (89), accessibility/layout (21),
performance (24), and documentation/dependency policy (8). Formatting/style/
analyzers, vulnerability audit, `git diff --check`, and `git fsck --full`
passed. Release compilation passed for `win-x64`, `linux-x64`, `osx-x64`, and
`osx-arm64`; this does not claim native Linux/macOS runtime validation. See
[v2.9 Reviewed Intelligent Organization](REVIEWED_INTELLIGENT_ORGANIZATION_v2.9.md)
and the [manual checklist](MANUAL_TESTING_v2.9.md).

OmniSorSe v2.8 **Guided Workflows & Product Coherence** was implemented on
`v2.8-guided-workflows-product-coherence` from the committed v2.7 release
candidate and is now included in the current 2.12.0-rc source on GitHub `main`.
It has no standalone v2.8 tag or package. It connects Search, Files,
Smart Tag review, durable Home readiness, Saved Views,
and reviewed organization through stable-identity navigation while preserving
schema 6, Search/facet semantics, Smart Tag authority, progressive indexing,
Explorer Protocol v1, and OmniBrille separation. Interactive desktop,
screen-reader, DPI, optional-tool, and native-platform execution remain manual
release gates. See [v2.8 Guided Workflows](GUIDED_WORKFLOWS_PRODUCT_COHERENCE_v2.8.md)
and the [manual checklist](MANUAL_TESTING_v2.8.md).

OmniSorSe v2.7 **Scalable Faceted Discovery** was implemented and locally
validated on
`v2.7-scalable-faceted-discovery` from the committed v2.6 release candidate. It
is now included in the current 2.12.0-rc source on GitHub `main`; it has no
standalone v2.7 tag or package. Complete-library SQLite candidate selection
precedes bounded hydration into the unchanged
deterministic ranker. Canonical facets/counts and dynamic Saved Views share one
query model; schema 6 and Explorer Protocol v1 remain unchanged. Debug and
Release each pass 1,753 tests with zero failures/skips, and all four supported
RID-specific Release compilations pass. Interactive desktop, DPI, screen-reader,
and native Linux/macOS execution remain manual release gates. See
[v2.7 Scalable Faceted Discovery](SCALABLE_FACETED_DISCOVERY_v2.7.md) and the
[manual checklist](MANUAL_TESTING_v2.7.md).

OmniSorSe v2.6 **Explainable Smart Tags** was committed on
`v2.6-explainable-smart-tags` from the committed v2.5 release candidate. It is
now included in the current 2.12.0-rc source on GitHub `main`; it has no
standalone v2.6 tag or package. Schema 6 becomes the durable authority for
versioned Theme/Document Type definitions, generated
assignments, explicit User Tags, and accept/reject decisions. Classification
reuses bounded local evidence and runs as a deferred stage so v2.5 base-first
Search remains usable first. Explorer Protocol remains v1 and OmniBrille is not
modified. See [v2.6 Explainable Smart Tags](EXPLAINABLE_SMART_TAGS_v2.6.md) and
[manual checklist](MANUAL_TESTING_v2.6.md).

OmniSorSe v2.5 **Workflow Completion & Indexing Quality** was implemented on
`v2.5-workflow-indexing-quality` and is now included in the current 2.12.0-rc
source on GitHub `main`. It has no standalone v2.5 tag or package. Its scope is
post-Change-Plan/Undo Files and indexing reconciliation, progressive
base-search-first scheduling, truthful indexing phases, and small organization
clarity improvements. It also includes an optional lazy, scoped desktop handoff
to the separately installed OmniBrille companion. Explorer Protocol v1's wire
contract/version, schema 5, release tags, packages, and the published v2.4.0
state are unchanged. See the
[v2.5 implementation record](WORKFLOW_AND_INDEXING_QUALITY_v2.5.md) and
[manual checklist](MANUAL_TESTING_v2.5.md), plus the
[companion handoff contract](OMNIBRILLE_COMPANION_HANDOFF_v2.5.md).

## Latest stable release identity

- Version: `v2.4.0`
- Release name: **Transition & Explorer Foundation**
- Source branch: `v2.4-omnisorse-transition`
- Status: integrated into `main` after final local and exact-tip hosted
  validation. Native packaging, tagging, and publication are release-workflow records;
  broad interactive validation is not claimed complete.

## Current prerelease identity

- Product/source version: `2.12.0-rc`
- Intended annotated tag and GitHub prerelease: `v2.12.0-rc`
- Package set: Windows x64 portable ZIP and per-user installer, macOS Intel and
  Apple Silicon DMGs, CycloneDX SBOM, and SHA-256 manifest
- Trust state: Windows unsigned; macOS unsigned and unnotarized
- Purpose: final real-world/manual validation before v2.12.0 GA
- Publication source: the exact `main` commit recorded by the tag, release, build
  manifests, SBOM, and final packaging workflow

## Default source identity

- Local and remote default branch: `main`
- Current source line: `v2.12.0-rc`
- Remote branch refs: `main` and v2.5-v2.12 exactly match their local tips
- Original publication fresh-clone evidence at `cc6c331`: clean default-branch
  checkout, no-cache restore, zero-warning Release build, and 1,861/1,861
  Release tests
- Recorded exact-main hosted evidence at `ffc29ed`: Windows, Ubuntu, macOS ARM,
  and macOS Intel passed the complete repository matrix in
  [run 32410287837](https://github.com/nishdel/OmniSorSe/actions/runs/32410287837)
- Later exact-main/package baseline at `d682997`: complete four-host
  [run 32490622011](https://github.com/nishdel/OmniSorSe/actions/runs/32490622011)
  and exact-source native
  [run 32492043785](https://github.com/nishdel/OmniSorSe/actions/runs/32492043785)
- Release boundary: v2.4.0 remains the latest stable release; v2.12.0-rc is the
  current prerelease candidate and is not GA

Release branches normally use `v<version>-<primary-feature>`, as demonstrated
by v1.2-v2.0. Historical branch names are retained as created: v1.1 used
`v1.1`, v0.1/v0.2 used `coding/v0.1` and `coding/v0.2`, and v0.4-v0.9 were
delivered together on `v0.9`. Planned roadmap entries have no branch until
implementation actually begins.
