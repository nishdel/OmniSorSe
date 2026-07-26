# Implementation Specification

| Property | Value |
|----------|-------|
| Spec ID | 020 |
| Component | Diagnostics |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

Diagnostics displays the health of OpenSorSe's own application logging in a user-friendly interface.

It provides aggregate troubleshooting information without exposing log-entry text or operation history.

---

# Why

Users need an easy way to understand whether OpenSorSe's own diagnostic logging is healthy without exposing internal payloads or affecting user files.

---

# Responsibilities

Diagnostics shall:

- Display logging status.
- Display the aggregate number of recorded events.
- Display the aggregate number of log write failures.
- Explain each metric in plain language.
- Refresh the aggregate counters.

---

# Does NOT

The Log Viewer must NOT:

- Write log entries.
- Execute business logic.
- Scan folders.
- Move files.
- Modify the filesystem.
- Execute rules.

---

# Inputs

- Aggregate logging statistics.
- User interaction.

---

# Outputs

The component provides:

- Refresh request.

---

# Workflow

1. Read aggregate counters.
2. Display logging health and explanations.
3. Refresh the counters when requested.

---

# Assumptions

- Logging Service is available.
- Individual log storage does not need to be accessible because payloads are not read.

---

# Acceptance Criteria

The implementation is complete when:

- Aggregate logging health is displayed correctly.
- The no-events state is understandable.
- Refresh updates the aggregate counters.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| Log Viewer                                                 |
+------------------------------------------------------------+

Filter:

[ All ▼ ]

------------------------------------------------------------

Time        Level      Message

09:15:01    INFO       Scan started

09:15:12    INFO       2,154 files discovered

09:15:15    WARNING    Folder inaccessible

09:15:18    ERROR      File locked

------------------------------------------------------------

[ Refresh ]

[ Export ]

[ Clear ]

------------------------------------------------------------

Status

154 Log Entries

---

## Corrected v0.1 layout

The implemented Diagnostics page replaces the illustrative draft log-viewer layout above. It shows a plain-language Logging status, Recorded events, and Log write failures with explanatory text, a no-events empty state, and one Refresh diagnostics button. It has no severity selector, raw log rows, export control, or clear-display control.

# Future

Not part of v0.1:

- Live log streaming.
- Advanced filtering.
- Search.
- Structured JSON logs.
- Remote logs.

---

# Dependencies

Depends on:

- 011 - Logging Service

Required by:

- None

---

# Autonomous v0.1 Decisions

Specification 011 intentionally does not retain or expose individual log entry payloads, and the draft does not define a safe entry-query, export, or deletion contract. v0.1 therefore presents this destination as **Diagnostics**: a privacy-safe aggregate view over `ILoggingService.GetStatistics()`. It uses plain-language logging status, recorded-event count, and log-write-failure count, with an explanation for each. Refresh reads the current counters and has a meaningful effect.

There is no severity selector, raw payload display, export, or clear-display action because those controls would not provide a useful v0.1 diagnostic workflow. An empty state explains that no diagnostic events have been recorded in the current application session. The view cannot read log files, display log text, export logs, delete logs, access operation history, or mutate logging configuration. Those operations are deferred until a reviewed privacy, retention, access-control, and export contract exists.
