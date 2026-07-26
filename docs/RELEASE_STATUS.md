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

## Current product boundary

OpenSorSe 1.3 is a safe, local-first desktop application for understanding, monitoring, and organizing explicitly selected folders. Reusable workflows configure scanning and analysis but do not grant mutation authority. AI and Advanced mode are independent and disabled by default. OCR and Semantic Search Beta are also separate local opt-ins and never activate AI.

The current Desktop workflow does not:

- Let AI directly rename, move, delete, overwrite, create, or otherwise modify source files. Optional Ollama generates only validated rename, logical folder-structure, or bounded document-text interpretation suggestions. Accepted organization suggestions create a non-mutating Change Plan.
- Expose the historical generic executor through the Desktop, execute raw rule actions, delete duplicates, write document metadata/tags, or run autonomous organization.
- Contact Ollama when AI is disabled or merely because the global AI switch is enabled. Provider requests additionally require an enabled capability, valid context, endpoint, and model.
- Treat catalog or structure comparison as certainty. Stored snapshots and semantic similarities are bounded review aids, not live filesystem truth.

Scanning, duplicate review, extraction, OCR, tagging, indexing/search, comparison, diagrams, and AI generation are non-mutating. Supported v1.1 mutations are rename file, move file, and create directory, exclusively through a user-reviewed Change Plan and dedicated execution service. Immediate revalidation rejects stale/invalid/linked/conflicting/occupied paths, overwrite is disabled, every attempt is journalled, success is verified, and safe inverse operations are recorded. AI output cannot execute.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

> Watched folders automate detection and analysis, not file modification.

> Workflow profiles automate configuration and analysis, not approval or file modification.

Watcher APIs are treated as fallible hints. Enabled roots are reconciled on startup, resume, reconnect, overflow, at least daily while running, and on demand. Missing storage retains configuration/catalogue/history; overlapping roots are rejected.

Duplicate View may, only after an explicit user command, pass a validated current-scan path to the operating-system shell. Each action is capped at five targets, uses no constructed shell command, reports partial failures, and performs no OpenSorSe filesystem mutation.

OpenSorSe-owned bounded JSON stores may retain settings, logs, AI review decisions, optional catalog snapshots/tags, saved queries, extracted native/OCR text, deterministic semantic vectors, structure history, Change Plans, and the Operation Journal under local application data. Current persistence, mutation, and network boundaries are detailed in [Safety and Privacy](SAFETY_AND_PRIVACY.md).

## Validation baseline

The exact standard in-place restore completed successfully. Current-source Debug and Release builds both succeeded with zero warnings and zero errors. The full suite passed 761 tests in each configuration with none skipped: Core 46, Scanner 61, Rules 68, Executor 60, Application 354, and Desktop 172. Inherited v1.1/v1.2 coverage still validates the complete Change Plan/execution/journal/recovery/Undo boundary and watched-folder configuration, event, reconciliation, stability, AI, correlation, and presentation behavior. v1.3 adds lifecycle/migration/corruption/dependency tests, template traversal/root/reserved/collision/Unicode/AI-data safety, precedence and immutable history, review-only provenance, import hardening, profile-aware AI, manual selection, and management ViewModels.

Source and compiled assembly metadata report product/informational version `1.3.0` and assembly/file version `1.3.0.0`; About displays `1.3`. A v1.3 portable package, signature, installer, or interactive GUI validation is not claimed by this source implementation.

Tesseract is not installed or discoverable in this development environment, so live recognition was not claimed. Automated tests cover version/language detection, argument construction, cancellation, timeout, empty/oversized output, missing languages, mixed-page coordination, cleanup, and provider isolation through fakes. The PDF renderer itself was exercised in process against a generated real PDF.

## Documentation status

The architecture directory contains both current implementation documentation and longer-term design material. The 1.2/1.3 documents identify watched configuration/lifecycle, workflow profile/recipe persistence and resolution, template safety, immutable history, watcher hints/debounce/stability, incremental processing, reconciliation, ignore/backpressure/AI rules, self-event correlation, Change Plans, the Operation Journal, Review Changes, Operation History, safe execution/rollback/Undo/recovery, and the existing content/OCR/semantic/AI/structure components. Rich media/archive readers, relational database architecture, plugins, broad localization, cloud indexing, signed installers, and automated publishing remain design material unless a release specification explicitly marks them implemented.

## Current release

OpenSorSe 1.3 source implementation and automated validation are complete on `v1.3-workflow-profiles`. Release branches follow `v<version>-<primary-feature>`. Do not publish a stable binary until the inherited GUI/OCR/Ollama/platform, v1.1 execution/recovery, v1.2 watcher/filesystem, and v1.3 workflow/template/import checklists pass on disposable data. See the [user guide](USER_GUIDE_v1.3.md), [safety documentation](SAFETY_AND_PRIVACY.md), [implementation specification](Implementation_Spec/v1.3/055_Workflow_Profiles_and_Recipe_Library.md), and [manual checklist](MANUAL_TESTING_v1.3.md).

## Release identity

- Version: `v1.3`
- Release name: **Workflow Profiles and Recipe Library**
- Git branch: `v1.3-workflow-profiles`
- Status: source implementation and automated validation complete; manual validation pending.

The branch convention is `v<version>-<primary-feature>`, for example `v1.1-safe-file-operations`, `v1.2-watched-folders`, and `v1.3-workflow-profiles`.
