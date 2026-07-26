# Changelog

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

## v0.9.1

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

See the preserved [v0.9 release proposal](Implementation_Spec/v0.9/00_v0.9_Release_Proposal.md) and [audit corrections](Implementation_Spec/v0.9/AUDIT_CORRECTIONS.md) for the historical snapshot-comparison release.
