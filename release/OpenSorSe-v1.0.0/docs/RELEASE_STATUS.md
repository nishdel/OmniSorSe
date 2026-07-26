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

## Current product boundary

OpenSorSe 1.0 is a safe, local-first desktop application for understanding and organizing explicitly selected folders. AI and Advanced mode are independent and disabled by default. OCR and Semantic Search Beta are also separate local opt-ins and never activate AI.

The current Desktop workflow does not:

- Let AI rename, move, delete, overwrite, create, or otherwise modify source files. Optional Ollama can generate only capability-specific validated rename, logical folder-structure, or bounded document-text interpretation previews; document text requires a separate opt-in and explicit one-file request.
- Expose the historical generic Executor through the Desktop, execute rules, delete duplicates, write document metadata/tags, or run autonomous organization.
- Contact Ollama when AI is disabled or merely because the global AI switch is enabled. Provider requests additionally require an enabled capability, valid context, endpoint, and model.
- Treat catalog or structure comparison as certainty. Stored snapshots and semantic similarities are bounded review aids, not live filesystem truth.

Scanning, duplicate review, extraction, OCR, tagging, indexing/search, comparison, diagrams, and AI suggestions are non-mutating. The one source-location mutation is a deterministic restructuring plan that is previewed and separately confirmed against an exact preview identity. It is bounded to 500 moves under one approved root, revalidates the root before work, rejects traversal/reparse/missing/conflicting/overwrite conditions, never deletes, and records or rolls back outcomes. AI output cannot enter this service.

Duplicate View may, only after an explicit user command, pass a validated current-scan path to the operating-system shell. Each action is capped at five targets, uses no constructed shell command, reports partial failures, and performs no OpenSorSe filesystem mutation.

OpenSorSe-owned bounded JSON stores may retain settings, logs, AI review decisions, optional catalog snapshots/tags, saved queries, extracted native/OCR text, deterministic semantic vectors, and structure history under local application data. Current persistence, mutation, and network boundaries are detailed in [Safety and Privacy](SAFETY_AND_PRIVACY.md).

## Validation baseline

The exact standard in-place restore completed successfully. Current-source Debug and Release builds both succeeded with zero warnings and zero errors. The full suite passed 627 tests with none skipped in each configuration: Core 46, Scanner 61, Rules 68, Executor 36, Application 264, and Desktop 152. The suite includes generated real-PDF PDFium rendering, page-level OCR/failure/cleanup fakes, AI provider-boundary checks, unified diagnostic privacy/retention/bounds/export/correlation tests, prompt/schema snapshots, representative small-model response failures, selected-file AI context, one-pass repair policy, cancellation/failure recovery, settings/cache migration, viewer/filter contracts, scanner sampling, and the FOSS inventory. Both builds compiled every Avalonia view, and Desktop composition tests validated the production dependency graph and gated navigation.

The fresh Windows x64 self-contained publish produced `OpenSorSe.exe` with file version `1.0.0.0`, product version `1.0.0`, the official associated icon, and its private .NET runtime. Package contents, licences, and checksum were inspected. Any non-interactive process-start smoke check establishes only that the process can initialize; packaged GUI startup, responsiveness, layout, and workflows remain part of the manual release hold and are not claimed as passed.

Tesseract is not installed or discoverable in this development environment, so live recognition was not claimed. Automated tests cover version/language detection, argument construction, cancellation, timeout, empty/oversized output, missing languages, mixed-page coordination, cleanup, and provider isolation through fakes. The PDF renderer itself was exercised in process against a generated real PDF.

## Documentation status

The architecture directory contains both current implementation documentation and longer-term design material. The 1.0 overview documents identify the delivered bounded content, OCR, semantic, AI, JSON-store, GUI, structure-history, and Windows portable-distribution components. Rich media/archive readers, relational database architecture, reports, plugins, broad localization, cloud indexing, signed installers, and automated publishing remain design material unless a release specification explicitly marks them implemented.

## Current release

OpenSorSe 1.0 implementation, automated validation, public README preparation, and local portable-package creation are complete on the local `v1.0` branch. Keep this result as the Gold Candidate—or publish only as a clearly marked pre-release—until the inherited v0.9.1 and v1.0 GUI/OCR/Ollama/model-matrix/platform checks pass. See the [installation guide](INSTALLATION.md), [implementation-specification index](Implementation_Spec/README.md), and [manual checklist](MANUAL_TESTING_v1.0.md). Do not merge into `main` for a stable release until manual verification is complete.
