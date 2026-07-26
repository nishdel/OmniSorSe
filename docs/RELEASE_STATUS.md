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

## Current product boundary

OpenSorSe 1.1 is a safe, local-first desktop application for understanding and organizing explicitly selected folders. AI and Advanced mode are independent and disabled by default. OCR and Semantic Search Beta are also separate local opt-ins and never activate AI.

The current Desktop workflow does not:

- Let AI directly rename, move, delete, overwrite, create, or otherwise modify source files. Optional Ollama generates only validated rename, logical folder-structure, or bounded document-text interpretation suggestions. Accepted organization suggestions create a non-mutating Change Plan.
- Expose the historical generic executor through the Desktop, execute raw rule actions, delete duplicates, write document metadata/tags, or run autonomous organization.
- Contact Ollama when AI is disabled or merely because the global AI switch is enabled. Provider requests additionally require an enabled capability, valid context, endpoint, and model.
- Treat catalog or structure comparison as certainty. Stored snapshots and semantic similarities are bounded review aids, not live filesystem truth.

Scanning, duplicate review, extraction, OCR, tagging, indexing/search, comparison, diagrams, and AI generation are non-mutating. Supported v1.1 mutations are rename file, move file, and create directory, exclusively through a user-reviewed Change Plan and dedicated execution service. Immediate revalidation rejects stale/invalid/linked/conflicting/occupied paths, overwrite is disabled, every attempt is journalled, success is verified, and safe inverse operations are recorded. AI output cannot execute.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

Duplicate View may, only after an explicit user command, pass a validated current-scan path to the operating-system shell. Each action is capped at five targets, uses no constructed shell command, reports partial failures, and performs no OpenSorSe filesystem mutation.

OpenSorSe-owned bounded JSON stores may retain settings, logs, AI review decisions, optional catalog snapshots/tags, saved queries, extracted native/OCR text, deterministic semantic vectors, structure history, Change Plans, and the Operation Journal under local application data. Current persistence, mutation, and network boundaries are detailed in [Safety and Privacy](SAFETY_AND_PRIVACY.md).

## Validation baseline

The exact standard in-place restore completed successfully. Current-source Debug and Release builds both succeeded with zero warnings and zero errors. The full suite passed 659 tests with none skipped: Core 46, Scanner 61, Rules 68, Executor 60, Application 267, and Desktop 157. Added coverage includes isolated real filesystem plans, staleness/conflicts, collision refusal, Unicode/spaces, case-only rename, verification recovery, journal-write failure recovery, cancellation boundaries, permission failure, partial failure/rollback, whole/selected/partial/dependent Undo, empty/non-empty directory inverse safety, durable persistence/migration/reporting, startup interruption inspection, suggestion adapters, Review Changes Apply gating, and persistent Operation History.

Source and compiled assembly metadata report product/informational version `1.1.0` and assembly/file version `1.1.0.0`. A v1.1 portable package, signature, installer, or interactive GUI validation is not claimed by this source implementation.

Tesseract is not installed or discoverable in this development environment, so live recognition was not claimed. Automated tests cover version/language detection, argument construction, cancellation, timeout, empty/oversized output, missing languages, mixed-page coordination, cleanup, and provider isolation through fakes. The PDF renderer itself was exercised in process against a generated real PDF.

## Documentation status

The architecture directory contains both current implementation documentation and longer-term design material. The 1.1 documents identify Change Plans, the Operation Journal, Review Changes, Operation History, safe execution/rollback/Undo/recovery, and the existing content/OCR/semantic/AI/structure components. Rich media/archive readers, relational database architecture, plugins, broad localization, cloud indexing, signed installers, and automated publishing remain design material unless a release specification explicitly marks them implemented.

## Current release

OpenSorSe 1.1 source implementation and automated validation are complete on the local `v1.1` branch. Do not publish a stable binary until the inherited GUI/OCR/Ollama/platform checks and the new mutation/rollback/Undo/interruption checklist pass on disposable data. See the [user guide](USER_GUIDE_v1.1.md), [safety documentation](SAFETY_AND_PRIVACY.md), [implementation specification](Implementation_Spec/v1.1/053_Safe_File_Operations_and_Robustness.md), and [manual checklist](MANUAL_TESTING_v1.1.md).
