# Autonomous v0.1 Decisions

## Specification 011 — Logging Service

### Missing details found

The original draft required log files and statistics but did not define their destination, format, retention, public models, configuration schema, failure semantics, or tests.

### Decisions

- Retain `Microsoft.Extensions.Logging`, Debug output, and the existing minimum-level configuration.
- Add an optional local UTF-8 daily text sink, enabled by default, under the platform local-application-data `OpenSorSe/Logs` directory unless configured otherwise.
- Use UTC daily names (`opensorse-YYYY-MM-DD.log`), seven-file retention, serialized writes, and a flush after each entry.
- Count process-lifetime accepted entries and file-sink failures; do not retain or query log entries.
- Omit supplied exceptions from file text and require callers not to log contents, hashes, credentials, or raw sensitive diagnostics.
- Swallow local file-sink failures and retain Debug output.

### Rationale and deferred alternatives

The design is local-only, bounded, deterministic, and does not add dependencies. JSON, querying, remote output, compression, size rotation, buffering, and telemetry are deferred.

### Safety and compatibility

No user files are modified outside the configured application log directory. Existing `Initialize(LogLevel)` callers remain supported.

### Tests and verification

Added deterministic tests for daily output, minimum-level filtering, file-sink disablement, retention, isolated file-sink failure, option validation, disposal, configuration preservation, and Core DI resolution. Verification is currently blocked before compilation because NuGet repository-signature retrieval is unavailable in the sandbox. An elevated restore reached NuGet but could not write the existing Executor generated restore artifact. No package or project dependency was added.

## Specification 012 — Configuration Manager

### Decisions

- Retain the existing Core configuration interface and JSON implementation; a UI-facing settings mutation API is deferred to Specification 019.
- Require an absolute configured settings path, load defaults then persisted JSON then the existing logging environment override, and map malformed JSON to a configuration-domain exception.
- Permit controlled atomic replacement only of the configured application settings file. Temporary files are cleaned up on all save outcomes.
- Observe cancellation before filesystem access and during async serialization/deserialization.

### Safety and deferred behavior

Configuration never scans user files or accesses databases. Profiles, encryption, live reload, import/export, migration, and cloud synchronization are deferred. No dependencies are added.

## Specification 013 — Main Window

### Decisions

- Keep the existing Avalonia Desktop project and DI-created `MainWindow`; no view service locator or global provider is added.
- Provide a stable shell layout and deterministic enum-based navigation state, defaulting to Dashboard.
- Host only selected-destination presentation until later page specifications implement concrete page views.

### Safety and deferred behavior

The shell has no filesystem or business-service access. Multiple windows, docking, themes, concrete feature pages, and interactive global menu actions remain deferred.

## Specification 014 — Dashboard

### Decisions

- Use read-only zero-valued current-session totals until scan history and report data have their own specifications.
- Implement quick actions as navigation-only commands; they do not start scans or modify settings.
- Host the Dashboard view only at the Dashboard destination in the existing application shell.

### Safety and deferred behavior

Dashboard has no filesystem, persistence, or executor dependency. History, notifications, charts, recommendations, and real-time statistics are deferred.

## Specification 015 — Folder Selection

### Decisions

- Implement a portable manual absolute-path input rather than an undefined native picker contract.
- Validate only selected root availability with `Directory.Exists`; folder contents are never enumerated or scanned.
- Normalize roots, reject duplicates using the platform path comparer, retain input order, and retain the five most recently added roots for the current process only.
- Emit an immutable `ScanRequest` event without invoking scanner, task, database, or navigation services.

### Safety and deferred behavior

The page does not read or modify user files. Native pickers, persisted history, scan profiles, exclusions, drag and drop, and scan orchestration remain deferred.

## Specification 016 — Scan Progress

### Decisions

- Consume existing immutable Scanner `ScanProgress` snapshots and show an indeterminate indicator because no total-work estimate exists.
- Keep a passive presentation model with `Start`, `ApplyProgress`, terminal completion, and a cancellation event; it does not call scanner or task services.
- Leave view closing and scan-lifetime ownership to the later orchestrator/application controller.

### Safety and deferred behavior

Progress presentation performs no filesystem access or cancellation itself. Estimates, throughput, pause/resume, error lists, previews, and concurrent scans are deferred.

## Specification 017 — Results View

### Decisions

- Replace the undefined `MovePlan` with the implemented `ConflictResolutionResult` and accept an explicit ordered `FileEntry` collection for review.
- Show accepted operations and conflict warnings without attempting resolution, rule evaluation, scanning, or execution.
- Emit an approval event with a read-only operation snapshot; cancellation and back are UI decisions only.

### Safety and deferred behavior

The review page cannot call the executor or alter filesystem state. Sorting, filtering, previews, manual edits, AI explanations, search, and controller navigation policy remain deferred.

## Specification 018 — Rule Editor

### Decisions

- Use immutable `FileRule` values and whole-rule add-or-replace operations instead of an undefined field-draft contract.
- Validate supported Rule Engine condition/action shapes locally before mutating the in-memory collection.
- Treat Save as a read-only snapshot event because v0.1 has no rule repository or configuration persistence contract.

### Safety and deferred behavior

The editor does not evaluate, plan, execute, persist, or inspect files. A full form builder, templates, import/export, priority visualisation, and persistence are deferred.

## Specification 019 — Settings

### Decisions

- Expose only implemented logging configuration rather than inventing settings for unimplemented scanner, AI, plugin, or update subsystems.
- Add an explicit configuration-service replacement-save overload; the UI never writes files itself.
- Treat all saved logging changes as restart-required because startup owns active logger construction.

### Safety and deferred behavior

Only the configured application settings file may be persisted. Startup, update, theme, language, scanner, conflict-strategy, AI, plugin, profile, and live-reload settings are deferred.

## Specification 020 — Log Viewer

### Decisions

- Preserve the Specification 011 privacy boundary: logging retains counters, not log-entry payloads.
- Implement an aggregate logging-health page with a severity counter filter, refresh, and display-only clear action.
- Add a Logs shell destination without adding log-file reads, export, deletion, or operation-history dependencies.

### Safety and deferred behavior

The page cannot expose raw messages or exceptions and cannot mutate stored logs. Entry viewing, exporting, file deletion, operation history, streaming, search, and structured logs are deferred.

## Specification 021 — Undo History

### Decisions

- Introduce an explicit caller-supplied `UndoHistorySession` model because persistent history is not implemented.
- Preserve caller session and record order; the view does not infer a reverse order.
- Require a two-step confirmation before emitting an undo request to a later controller and allow display of externally returned undo results.

### Safety and deferred behavior

The page never calls `IUndoEngine` or modifies files/history. Persistence, session discovery, selective undo, redo, previews, and automatic reverse ordering are deferred.

## Specification 022 — About View

### Decisions

- Declare Desktop project version `0.1.0` and present the matching static v0.1 metadata.
- Use vetted HTTPS URI values and emit external-link requests rather than launching a process or requiring network access.
- Display the MIT license as declared by the implementation specification.

### Safety and deferred behavior

The page performs no filesystem or network access. Packaged license text, contributors, changelog, release notes, and host link-launching policy are deferred.

## Specification 023 — Notification Center

### Decisions

- Use an in-memory immutable notification queue with process-local IDs, manual dismissal, and timer-based expiry.
- Automatically expire information/success messages after five seconds; warnings/errors remain until dismissed.
- Do not subscribe to unfinished Event Bus or error-notification contracts; callers publish explicit notification requests.

### Safety and deferred behavior

Notifications do not execute operations or access files. Persistence, history, action buttons, desktop integration, progress, grouping, and event-bus wiring are deferred.

## Specification 024 — Scan Orchestrator

### Decisions

- Create the documented Application project and coordinate only already implemented Scanner/Rules services.
- Require explicit ordered rules with each processing request because no persisted rule repository exists; an empty collection is valid.
- Return all reached stage outputs, report stage snapshots without fabricated percentages, and stop before executor invocation.

### Safety and deferred behavior

The orchestrator is sequential and non-destructive: it never calls the executor, touches files itself, persists results, or publishes events. Rules storage, Task Manager wrapping, UI ownership, and later pipeline stages remain deferred.

## Specification 025 — Session Manager

### Decisions

- Keep unique processing sessions in process memory only and expose immutable snapshots in creation order.
- Treat unexpected orchestrator failure as a terminal tracked failed session with a user-safe message.
- Allow explicit closure only of terminal sessions; sessions are neither persisted nor resumed.

### Safety and deferred behavior

Sessions do not execute stages or modify files directly. Persistence, database history, resume, comparison, ownership, and Task Manager integration are deferred.

## Specification 026 — Application Controller

### Decisions

- Implement a narrow UI-agnostic controller that routes explicit processing requests to the session manager.
- Leave UI navigation and all destructive execution authority outside this controller until an explicit confirmation contract exists.

### Safety and deferred behavior

The controller cannot call executor or undo services and does not reference Desktop views. Navigation, cancellation ownership, events, dialogs, persistence, and execution approval wiring are deferred.

## Specification 027 — Event Bus

### Decisions

- Reuse the existing Phase 2 Core `IEventBus`/`EventBus` implementation rather than introduce duplicate contracts.
- Document sequential in-memory typed delivery, disposable subscriptions, cancellation between handlers, and isolated subscriber failure logging.

### Safety and deferred behavior

Events are not persistent, remote, retried, replayed, or used for direct destructive-operation authorization.

## Specification 028 — Error Handler

### Decisions

- Reuse the Phase 2 `IErrorHandler`/`ErrorHandler` and its user-safe `ApplicationError` severity model.
- Keep UI notification adaptation outside Core so error handling does not depend on Desktop services.

### Safety and deferred behavior

The handler logs and broadcasts error metadata but performs no retry, persistence, recovery decision, or destructive action.
