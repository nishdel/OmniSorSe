# OmniSorSe engineering knowledge

Status: living process router. Source and tests remain the behavior authority.

This directory holds the small active system that helps humans and Codex change
OmniSorSe safely. It does not replace product or subsystem documentation.

## Load progressively

| Need | Read |
| --- | --- |
| Current version, schema, protocol, and confidence | [Current State](../CURRENT-STATE.md) |
| Who owns or consumes important state | [Architecture Authority Map](ARCHITECTURE_AUTHORITY.md) |
| How a substantial Codex task runs | [Development System](DEVELOPMENT_SYSTEM.md) |
| Which specialists and checks a change needs | [Risk and Validation Matrix](RISK_VALIDATION_MATRIX.md) |
| How observations become controlled durable knowledge | [Learning System](LEARNING_SYSTEM.md) |
| Why this system exists and how it was tested against history | [2026 archaeology](ARCHAEOLOGY_2026-08-18.md) |

Use [ADRs](../Architecture/99_Appendix/ADR.md) for durable decisions and the
[Glossary](../Architecture/99_Appendix/Glossary.md) for precise terminology.
Version/release reports remain discoverable through the main
[documentation index](../README.md), but are not routine task context.

## Templates and retained evidence

- [Project-owner report](templates/OWNER_REPORT.md)
- [Run retrospective](templates/RETROSPECTIVE.md)
- `reports/`: concise owner reports worth retaining for later audits.
- `retrospectives/`: run evidence used by periodic learning review; not loaded
  by default.

If two active documents conflict, do not copy both claims. Verify source/tests,
correct the authoritative document, and link other documents to it.
