# Release Status

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
| v1.7 Deep Indexing Foundation | Source implementation and local automated validation complete; post-push hosted validation is a handoff gate; interactive manual validation is not claimed | Clean restore; zero-warning Debug/Release builds; 987 tests passed in each configuration with zero failures/skips; analyzer/style/format/docs/dependency/diff gates clean; advisory audit clean after pinning SQLitePCLRaw 2.1.12; four runtime-target builds passed. See [v1.7 Validation Report](V1.7_VALIDATION_REPORT.md). | Provider-independent durable indexing, embedded SQLite schema/recovery, Basic/Standard/Deep policy, progressive Search, progress/control/storage UI, privacy-safe diagnostics, naming/accessibility, and expanded recovery/concurrency/performance coverage. |

## Current product boundary

OpenSorSe 1.7 is a safe, local-first desktop application for understanding,
monitoring, searching, and organizing explicitly selected folders. The v1.6
production-hardening and cross-platform foundation remains intact; v1.7 adds
durable progressive background indexing. Reusable workflows and plugin contributions
configure scanning and analysis but do not grant mutation authority.

The current Desktop workflow does not:

- Let AI directly rename, move, delete, overwrite, create, or otherwise modify source files. Optional Ollama generates only validated rename, logical folder-structure, or bounded document-text interpretation suggestions. Accepted organization suggestions create a non-mutating Change Plan.
- Expose the historical generic executor through the Desktop, execute raw rule actions, delete duplicates, write document metadata/tags, or run autonomous organization.
- Contact Ollama when AI is disabled or merely because the global AI switch is enabled. Provider requests additionally require an enabled capability, valid context, endpoint, and model.
- Treat catalog or structure comparison as certainty. Stored snapshots and semantic similarities are bounded review aids, not live filesystem truth.

Scanning, duplicate review, extraction, OCR, tagging, indexing/search, comparison, diagrams, and AI generation are non-mutating. Supported v1.1 mutations are rename file, move file, and create directory, exclusively through a user-reviewed Change Plan and dedicated execution service. Immediate revalidation rejects stale/invalid/linked/conflicting/occupied paths, overwrite is disabled, every attempt is journalled, success is verified, and safe inverse operations are recorded. AI output cannot execute.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

> Watched folders automate detection and analysis, not file modification.

> Workflow profiles automate configuration and analysis, not approval or file modification.

> Plugins analyze or propose; they do not grant mutation authority.

Watcher APIs are treated as fallible hints. Enabled roots are reconciled on startup, resume, reconnect, overflow, at least daily while running, and on demand. Missing storage retains configuration/catalogue/history; overlapping roots are rejected.

Duplicate View may, only after an explicit user command, pass a validated current-scan path to the operating-system shell. Each action is capped at five targets, uses no constructed shell command, reports partial failures, and performs no OpenSorSe filesystem mutation.

OpenSorSe-owned bounded JSON stores may retain settings, logs, AI review decisions, optional catalog snapshots/tags, saved queries, extracted native/OCR text, deterministic search representations, structure history, plugin state/packages, Change Plans, and the Operation Journal under local application data. The provider-isolated embedded SQLite index additionally retains durable sources, runs, stages, bounded shared content, coverage, and maintenance history. Current persistence, mutation, plugin, and network boundaries are detailed in [Safety and Privacy](SAFETY_AND_PRIVACY.md).

## v1.7 validation

The final clean local sequence passed restore, zero-warning Debug and Release
builds, and **987 tests in each configuration** with zero failures and zero
skips. Analyzer, style, whitespace, documentation/dependency/architecture,
vulnerability, patch, artifact, privacy, and four-runtime target-compilation
gates passed. The exact immutable commit, push, synchronization, and native
Windows/Ubuntu/macOS GitHub Actions result are post-commit evidence reported in
the final handoff. Interactive manual validation is not claimed. See the
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

## Current release

OpenSorSe 1.7 source implementation is on
`v1.7-deep-indexing-foundation`. Final local automated validation is complete;
commit, push, and exact GitHub Actions evidence are post-commit handoff gates
tracked separately from the uncompleted manual checklist.
Packaging, signing, tagging, and publishing remain separate release activities.
See the [user guide](USER_GUIDE_v1.7.md), [implementation
specification](Implementation_Spec/v1.7/059_Deep_Indexing_Foundation.md),
[implementation report](V1.7_IMPLEMENTATION_REPORT.md), and
[manual checklist](MANUAL_TESTING_v1.7.md).

## Release identity

- Version: `v1.7`
- Release name: **Deep Indexing Foundation**
- Git branch: `v1.7-deep-indexing-foundation`
- Status: source implementation and local automated validation complete; post-push hosted validation remains a handoff gate; no interactive manual result, package, tag, or published release is claimed.

The branch convention is `v<version>-<primary-feature>`, for example `v1.1-safe-file-operations`, `v1.2-watched-folders`, `v1.3-workflow-profiles`, `v1.4-plugin-foundation`, and `v1.5-cross-platform-foundation`.
