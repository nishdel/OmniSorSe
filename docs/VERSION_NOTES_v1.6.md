# OpenSorSe v1.6 Version Notes

OpenSorSe 1.6.0 is a reliability and performance release. It preserves every
v1.5 feature and persisted format while strengthening production behavior.

## Highlights

- One durable, bounded atomic JSON writer across all application-owned stores.
- Process-local cross-instance coordination for complete persistence
  transactions, including safety-critical Change Plans and Operation Journals.
- Faster, lower-allocation exact-duplicate analysis and Results search.
- Responsive cancellation during large result projection and query work.
- Bounded processing-session memory and terminal-safe task progress.
- Idempotent watched-folder initialization/disposal and isolated observer
  failures.
- Host-correct case semantics for watched hints and action planning.
- Accessibility names and live status announcements on critical workflows.
- Windows, Ubuntu, and macOS CI with Debug/Release, zero-skip, analyzer, style,
  formatting, and documentation gates.
- A single runtime product-version source to prevent provenance drift.

## Compatibility and safety

No JSON schema is bumped. Existing settings, scans, tags, searches, AI
decisions, indexes, workflows, watched folders, plugin state, plans, journals,
and history remain compatible. AI remains optional and suggestion-only. File
changes still require a reviewed, approved, validated, explicitly confirmed
Change Plan and remain journalled, recoverable, and conflict-aware for Undo.

## Release boundary

This source implementation does not itself claim a package, installer,
signature, tag, or published release. See the
[validation report](V1.6_VALIDATION_REPORT.md) and complete the
[manual checklist](MANUAL_TESTING_v1.6.md) before a binary release claim.
