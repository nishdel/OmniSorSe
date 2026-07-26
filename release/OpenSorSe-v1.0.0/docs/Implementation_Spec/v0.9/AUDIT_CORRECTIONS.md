# v0.9 Senior Audit Corrections

This note supersedes only current implementation details found unsafe or misleading during the completed v0.9 audit. Historical release scope remains unchanged.

## Corrected contracts

- Diagnostic logs use `opensorse-owned-YYYY-MM-DD.log`, begin with an OpenSorSe ownership marker, and stop at 10 MiB per UTC day. Retention and append operations affect only files with both the exact daily name and marker. A colliding unowned file is preserved and disables the file sink for that process.
- `settings.json`, `decision-history.json`, `catalog.json`, and `saved-catalog-searches.json` have explicit encoded-size limits checked before deserialization and before atomic replacement. Oversized or corrupt data is preserved and reported; it is not silently rewritten.
- Catalog persistence independently validates nested records and enum/numeric/display values, enforces the twelve accepted non-deterministic tags-per-file limit and tag text bounds, and maps malformed nested nulls to `InvalidDataException`. A rejected save leaves the previous catalog byte sequence intact.
- AI decision-history load validates its 1,000-record capacity, enum values, UTC timestamps, and bounded metadata. Append validates before creating application data.
- Mutation-capable `IActionExecutor` and `IUndoEngine` implementations remain historical/internal test infrastructure, but the v0.9 Desktop no longer registers them. No current command or user flow can resolve or invoke them.
- Primary navigation uses stable user-facing labels; dense catalog action rows wrap, and critical catalog selectors and inputs expose accessible names.
- About reports exact product version `0.9.0` and replaces inert external-link buttons with labelled copyable HTTPS addresses, preserving the no-shell-launch boundary.
- Settings exposes cancellation for connection/discovery/reset work, prevents stale AI-state publication, and requires a separate confirmation before deleting local decision history.
- Application-owned catalog initialization begins after the main window opens, avoiding a synchronous UI-thread wait on asynchronous persistence I/O.
- Optional provider transport accepts at most a 128 KiB UTF-8 prompt, reads at most a 1 MiB response, and publishes at most 100 validated model identifiers.
- Malformed, invalid, inaccessible, or oversized owned settings are preserved; startup activates safe defaults and Settings presents a recovery warning instead of preventing the GUI from opening.

## Compatibility and recovery

Existing `opensorse-YYYY-MM-DD.log` files are left untouched; new log files use the ownership-aware name. Catalog schema 1 remains read-only compatible and writes forward to schema 2 only after a successful user-authorized catalog write. The JSON schema numbers are unchanged because the new checks reject invalid envelopes without changing valid serialized shapes.

## Audit acceptance coverage

Regression tests cover unowned log collisions, oversized owned stores, catalog tag capacity atomicity, invalid/over-capacity decision history, and readable navigation labels. Full solution validation and documentation checks are recorded in [Release Status](../../RELEASE_STATUS.md).
