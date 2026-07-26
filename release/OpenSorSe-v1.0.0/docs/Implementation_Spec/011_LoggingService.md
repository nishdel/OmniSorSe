# Implementation Specification

> **Current v0.9 supersession:** daily files now use `opensorse-owned-YYYY-MM-DD.log` plus an ownership marker and a 10 MiB per-day bound. Append and retention apply only to marked OpenSorSe files; colliding unowned files are preserved and the file sink fails closed. The original filename below records the historical v0.1 contract. See [v0.9 audit corrections](v0.9/AUDIT_CORRECTIONS.md).

| Property | Value |
| --- | --- |
| Spec ID | 011 |
| Component | Logging Service |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Approved v0.1 contract |

## Purpose

Provide centralized, local-only diagnostic logging for all OpenSorSe components without changing application behavior or exposing sensitive file data.

## Responsibilities

- Create categorized `Microsoft.Extensions.Logging.ILogger` instances.
- Apply the configured minimum level to Debug and optional local-file output.
- Write one UTF-8 text entry per line to a UTC daily file when file logging is enabled.
- Retain at most a configured number of `opensorse-YYYY-MM-DD.log` files.
- Serialize file writes, flush each complete entry, and isolate logging-output failures.
- Expose process-lifetime emitted-entry and file-write-failure counters.

## Non-responsibilities

The service does not execute business actions, change filesystem plans, expose a log-query API, rotate files by size, compress logs, publish events, collect telemetry, transmit data, persist operation history, or serialize exceptions, file contents, hashes, credentials, or API keys.

## Placement and dependencies

The service remains in `OpenSorSe.Core.Logging`. It uses the existing `Microsoft.Extensions.Logging` packages only. `ApplicationHost` maps validated configuration to logging options; the logging service does not read configuration files itself.

## Public contract

`ILoggingService` retains `Initialize(LogLevel)` and adds `Initialize(LoggingOptions)` and `GetStatistics()`.

`LoggingOptions` contains `MinimumLevel`, `FileLoggingEnabled`, optional `LogDirectoryPath`, and `RetainedFileCount`. The default directory is `<LocalApplicationData>/OpenSorSe/Logs`; the default retention is seven files. `RetainedFileCount` must be at least one. A caller may disable the file sink.

`LoggingStatistics` contains process-lifetime counts for Trace, Debug, Information, Warning, Error, Critical, and file-write failures. Counts include entries accepted by the logging factory's configured minimum level, exactly once per emitted entry. No entry payload is retained.

## Configuration

`LoggingSettings` adds `FileLoggingEnabled` (default `true`), optional `LogDirectoryPath`, and `RetainedFileCount` (default `7`). `MinimumLevel` remains the existing configuration-controlled severity. Invalid retention or a whitespace directory path is rejected during configuration validation.

## Processing behavior

1. `ApplicationHost` loads and validates settings, then initializes logging with mapped options.
2. Initialization replaces any previous logging factory after the new factory is ready.
3. Debug output is always configured. When enabled, the local provider writes `timestamp [level] [category] message` as one line using a UTC round-trip timestamp and UTF-8 without a BOM.
4. The provider uses `opensorse-YYYY-MM-DD.log` according to the current UTC date and removes only older matching daily files beyond retention.
5. File sink and retention failures are swallowed, increment `FileWriteFailures`, and leave Debug output available. They never invoke the global error handler or throw from an `ILogger.Log` call.
6. Disposing the service disposes its logging factory. Creating a logger or reinitializing after disposal throws `ObjectDisposedException`.

## Ordering, concurrency, and performance

Each provider serializes writing with one lock. Entries are written in lock acquisition order. The implementation allocates only the formatted line and writes synchronously so every accepted entry is flushed before `Log` returns. File retention scans only the configured directory during initialization.

## Error and privacy behavior

Invalid options throw `ArgumentException` before replacing a configured factory. File I/O failures are recoverable and not surfaced to application callers. Exceptions supplied through the standard logging API are deliberately omitted from file output. Callers must provide non-sensitive messages; components must not log file contents, hashes, credentials, or raw exception details.

## Acceptance criteria

- Information, Warning, and Error entries are written when enabled and permitted by the configured level.
- Disabled levels do not produce file entries or statistics.
- File sink failure does not throw from application logging.
- Daily retention removes only matching old log files and honors the configured limit.
- Statistics are accurate for emitted entries and sink failures.
- Existing `Initialize(LogLevel)` callers remain compatible.
- Public APIs have XML documentation and all deterministic tests pass.

## Tests

Unit tests cover level filtering, categorized output, daily UTF-8 text output, disabled file logging, sink-failure isolation, retention, statistics, validation, disposal, and Core DI resolution. Tests use isolated temporary directories and do not assert a physical Debug output implementation.

## Deferred behavior

Structured JSON, live log viewing, querying, remote logging, telemetry, size rotation, compression, asynchronous buffering, and metrics are deferred.

## Autonomous v0.1 Decisions

- A local file sink is enabled by default because the original specification explicitly requires log files, while Debug output remains the non-file fallback.
- Seven daily files are retained to bound local disk use without implementing rotation or compression.
- Statistics are process-lifetime counters because the specification explicitly requires statistics; no entry store or query API is introduced.
- Standard logging exceptions are not written to disk to avoid raw sensitive diagnostics. This deliberately favors privacy over complete exception persistence.
