# Unified Advanced Diagnostics

## Purpose and boundary

Advanced Diagnostics is a process-local, UI-independent event framework for explaining feature behavior without changing it. It is separate from ordinary application logging. Feature services publish typed events through `IDiagnosticsEventSink`; the bounded `InMemoryDiagnosticsCollector` retains immutable `DiagnosticSession` snapshots; the desktop observes those snapshots and marshals viewer updates onto the Avalonia UI thread.

Diagnostics are best effort. `DiagnosticsIsolation.Protect` contains collector failures so collection, redaction, storage, export, or observer failures cannot affect scanning, extraction, OCR, or AI behavior. Closing the non-modal viewer does not own or cancel an operation.

## Settings and categories

`DiagnosticsSettings.EnableDiagnostics` is the master switch. A category switch is effective only while the master switch is on. Disabling the master switch and saving Settings clears retained history. Application exit also clears the store. Unredacted content is a separate default-off opt-in.

| Category | v1.0 state |
| --- | --- |
| AI | Fully instrumented |
| OCR and text extraction | Fully instrumented |
| Scanning | Fully instrumented |
| Duplicate detection | Setting and registry placeholder; not yet instrumented |
| Search and indexing | Setting and registry placeholder; not yet instrumented |
| Rules and organisation | Setting and registry placeholder; not yet instrumented |
| File operations | Setting and registry placeholder; not yet instrumented |
| Performance | Setting and registry placeholder; not yet a standalone producer; instrumented sessions include timing |

The registry is the extension point for truthful category descriptions. Future producers begin a category session, publish classified fields, relate parent/downstream sessions, and complete with a terminal status. Core, Application, Scanner, and AI services have no Avalonia dependency.

## Common model

- `DiagnosticCategory` identifies a feature area.
- `DiagnosticSession` is an immutable request or operation snapshot.
- `DiagnosticEvent` represents one ordered stage update.
- `DiagnosticSection` maps data into Overview, Timeline, Inputs, Intermediate results, Outputs, Warnings and errors, or Performance.
- `IDiagnosticsCollector` combines configuration, event collection, observation, and clearing.
- `IDiagnosticsRedactor` classifies and redacts values before retention.
- `IDiagnosticsExportService` creates explicit JSON or text exports; it never writes automatically.

Related session IDs preserve causality such as scan → extraction/OCR → AI. The unified viewer shows active and recent sessions in reverse chronological order with category/status filters, correlation IDs, copy/export/clear actions, auto-scroll, and word wrap.

## Privacy and lifetime

Paths, filenames, document/OCR text, metadata, tags, search terms, prompts, and responses are classified before entering the store. Redacted mode is the default. Unredacted mode may retain those values in memory, but secrets, credentials, authorization values, passwords, and API keys are removed in every mode. Detailed diagnostic content is never sent to ordinary logs.

History lasts only for the application process unless the user explicitly exports it. The current limits are:

- 50 sessions total and 20 per category;
- 750 events per session;
- 1,048,576 characters per field and per event/context field set;
- approximately 8 MiB per session;
- 100 OCR page records and 500 detailed scan-entry records per session;
- approximately 32 MiB across retained sessions;
- zero rendered-page image previews in v1.0.

Dropped, sampled, truncated, or deliberately unretained data is reported in the session. OCR page images are not retained because temporary rendering artifacts are deleted immediately; future preview support must remain explicitly byte/count bounded.

## Feature coverage

AI retains separate system prompt, user prompt, serialized request, transport response, extracted assistant content, parsed structured response, validation detail, connection tests, model discovery, timing, and retries. OCR/text extraction retains raw native text, raw OCR text, normalized text, downstream text, native-quality and fallback decisions, engine/version/language, per-page status, rendering dimensions/DPI, preprocessing, truncation, warnings, partial results, and cancellation. Scanning retains roots/options, traversal decisions, accepted and skipped entries, downstream format support, access/missing/reparse/metadata issues, progress, counts, cancellation, elapsed time, and bounded aggregation.

The v2.0 Knowledge Graph adds a separate privacy-safe operational snapshot:
run ID, projection revision, four state axes, current stage, queue and terminal
counts, completed-manifest and ingestion/applied watermarks, recovered claims,
repair-required count, node/edge/evidence/decision totals, bounded failure
category, storage size, and coverage. It does not retain source contents,
extracted/OCR text, summaries, vectors, aliases, complete Search queries, raw
suggestion payloads, secrets, or unnecessary absolute paths. Graph diagnostics
remain useful when detailed content retention is off and do not make a failed
graph operation succeed.
