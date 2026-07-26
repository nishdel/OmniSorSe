# v0.8 Implementation Decisions

## Scope selection

The roadmap does not assign v0.8 and places databases, content readers, semantic search, reports/export, execution, and plugins in future ideas. The implemented catalog instead has an immediate usability and provenance gap: timestamp-only entries do not explain their purpose or selected scan roots. v0.8 closes that bounded gap and supplies honest scope metadata needed by the next historical-review workflow.

## Labels are application metadata

A display name belongs to a catalog entry, not a file or folder. Clearing or replacing it rewrites only the bounded OpenSorSe catalog file. Source roots are captured from the validated scan request and are immutable afterwards so the user cannot accidentally falsify provenance.

## Additive model compatibility

New values are init-only properties on the existing records. Existing production and test constructors remain valid, while JSON version 1 naturally supplies their empty defaults. This avoids a broad call-site rewrite and keeps the migration explicit at the envelope boundary.

## Read-compatible, write-forward migration

Schema 1 is read without automatic mutation. Any later successful catalog save emits schema 2 atomically. This avoids surprising writes at startup and preserves malformed or unsupported data for explicit maintenance.

## Scope bounds and path handling

The store accepts at most 32 roots of 2,048 characters. It never resolves those strings against the current machine. Separator-neutral, case-insensitive identity is used for drive-letter and UNC forms; Unix-style paths remain case-sensitive. The stored first spelling is retained for presentation.

## Deferred alternatives

Arbitrary notes, snapshot grouping, exports, reports, live verification, database migration, and background monitoring are deliberately deferred. None is required to make the bounded existing catalog understandable and ready for comparison.
