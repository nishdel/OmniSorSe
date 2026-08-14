# OmniSorSe v2.7 — Scalable Faceted Discovery

**Status:** implemented and locally validated on `v2.7-scalable-faceted-discovery` for review; not released

**Baseline:** committed v2.6 release candidate `9d7fb98537ea4e5c9ea9b2b84a878f9e25812549`

v2.7 makes existing local intelligence discoverable across ordinary large
libraries. It does not add a second Search engine. SQLite now evaluates the
complete authorized index for candidate eligibility, returns a relevance-ordered
bounded set of file identities, and hydrates only that set for the established
deterministic ranker. Exact filename, stem, and prefix tiers remain unchanged.

## Complete eligibility and bounded hydration

The former progressive projection loaded a path-ordered prefix capped by
`MaximumDocumentCount` before ranking. At more than 10,000 files, an exact match
outside that prefix could therefore be absent from the ranker. v2.7 moves the
first candidate-selection step into the durable SQLite store:

1. enabled sources, privacy exclusions, deletion state, and typed filters define
   complete index eligibility;
2. lightweight filename/path and retained-evidence scoring selects candidate IDs;
3. only a configured bounded projection is hydrated;
4. the existing deterministic ranker produces final order and explanations.

Search reports eligible, plausible, and hydrated candidate counts. If bounded
hydration applies, the interface says so; it does not confuse indexed-file
coverage, candidate coverage, and displayed result count. Exact filename and
stem matches are selected ahead of weaker evidence anywhere in the authorized
index. Legacy providers retain their bounded fallback contract.

## One discovery query model

Free text, facets, active chips, Smart Tag entry points, and Saved Views all use
the existing canonical `SearchRequest`/`SearchFilter` model. Supported discovery
facets are:

- Theme canonical ID;
- Document Type canonical ID;
- User Tag canonical ID;
- file category;
- explicit filesystem-created year;
- explicit filesystem-modified year.

Values within one type use OR; populated types use AND. Created and modified
years remain separate and never imply embedded document, EXIF capture, or
content-inferred dates. Entity faceting is deferred because entities are not yet
normalized for an efficient interactive join; v2.7 does not parse every Content
Intelligence JSON record or introduce schema 7 for symmetry.

## Counts and progressive evidence

Facet values and counts are aggregated in SQLite. Counts for a group retain the
query and every other active group while ignoring that group's own selections,
so they describe the result if a value were added to that OR group. Each group
returns at most 30 values in the ordinary UI. File/tag association graphs are
not loaded into memory.

Fast/searchable-first indexing remains authoritative. Base filename and path
Search does not wait for Smart Tags, OCR, transcripts, or media analysis. Counts
and Saved View results can increase as durable evidence arrives. Refreshes reuse
stable facet row objects so keyboard focus is not discarded merely because a
count or selected state changed.

## Saved Views and historical Saved scans

A Saved View is a bounded local query/filter rule, not stored membership. Up to
100 rules are written atomically to application-owned
`saved-discovery-views.json`; each retains a stable ID, name, canonical query and
filters, version, and UTC timestamps. Opening a view reevaluates the current
index. Users can create, open, update/rename, and delete a view.

Saved scans remain distinct: they are opt-in historical catalog snapshots and
their saved catalog searches query that historical data. No historical catalog
is migrated, renamed, or deleted by v2.7.

## Suggestion review and extraction

The canonical unresolved-Moderate filter finds files whose generated Smart Tag
suggestions still require a decision. Existing Keep/Dismiss controls remain the
authority; accepted or rejected files leave the unresolved result naturally.
Unbounded bulk review is not included.

Bounded native CSV/TSV extraction streams representative cells with quote,
delimiter, row, column, byte, character, encoding, and cancellation safeguards.
XLSX extraction additionally observes selected shared, inline, numeric, and
formula text without evaluating formulas, macros, or external links. PPTX reads
bounded slide and speaker-note text from approved ZIP/XML parts without
rendering or executing embedded content. No production dependency was added.

## Persistence, privacy, and boundaries

The durable Search schema remains **6**. Saved Views use the established
versioned local JSON-store pattern, while candidate selection and facet counts
query existing schema-6 data and indexes. No migration is required.

Queries, User Tag values, facet labels, Saved View contents, and evidence text
remain content-sensitive. Normal diagnostics retain only counts, timings,
filter counts/types, failure categories, and safe fingerprints. Saved Views are
local and have no synchronization or telemetry path.

v2.7 does not add Ollama classification, embeddings, a vector database, metadata
writeback, automatic organization, another collection subsystem, entity
normalization, Explorer Protocol changes, OmniBrille code, graph expansion,
cloud services, or source-file mutation. Change Plans remain the only reviewed
file-mutation boundary.

See [the v2.7 manual checklist](MANUAL_TESTING_v2.7.md) for the validation
boundary.

## Automated validation boundary

Fresh non-incremental Debug and Release builds complete with zero warnings or
errors, and each full suite passes 1,753 tests with no failures or skips. The
focused performance suite passes 23 tests. A 100,000-file regression considers
the complete authorized fixture, hydrates only 512 selected candidates, and
aggregates all six facet groups in SQLite. Release compilation passes for
win-x64, linux-x64, osx-x64, and osx-arm64; this does not claim native runtime
execution on Linux or macOS. Desktop, DPI, and screen-reader scenarios remain
unchecked manual evidence.
