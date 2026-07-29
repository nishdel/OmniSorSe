# OpenSorSe 1.8 Troubleshooting

## Search says coverage is incomplete

Open Background indexing and review names/metadata, text, OCR, related-concept,
and fully indexed coverage. The message identifies exclusions, OCR/local-AI
waits, failed stages, or unavailable storage. Search still returns currently
known filename/metadata results. Resume paused work, restore an optional
dependency, or retry failed items as appropriate.

## A natural-language filter is wrong

Every interpreted filter is visible. Remove the individual chip or use **Clear
all filters**. Uncertain language should remain a topic term; report a
reproducible phrase if OpenSorSe applies an uncertain filter. Changing the query
clears old filters.

## A precise match ranks too low

Open **Why this result?** and compare actual evidence. Exact filename and
literal tiers should remain above related-concept-only results. Record a small
synthetic corpus and query for a relevance regression report; do not share
private document snippets.

## A result has no snippet

Snippets are optional. Search uses only bounded already-indexed content and will
not extract a file at query time. A metadata-only, excluded, malformed, or
partially indexed file may have no safe snippet.

## Search is rejected

Queries are limited to 512 characters, 32 topic terms, and 16 filters. Embedded
null/control characters and malformed Unicode are rejected. Short typo queries
are intentionally not expanded into many weak matches.

## The deep index is unavailable or busy

Compatible filename/metadata Search remains available where possible. Wait for
maintenance or another short transaction to finish, then refresh. Inspect
redacted diagnostics for a failure category. For corruption or an unsupported
newer schema, preserve the recovery copy and use the explicit rebuild action;
never edit a live database manually.

## Forget or clear data reappears

v1.8 stores a durable source-relative suppression/exclusion policy. Ensure the
action completed and inspect the file policy. A later explicit re-index/repair
can intentionally clear the suppression. Watched-folder configuration may own
the source but does not override a file exclusion.

## Selective repair is waiting

The repair uses the durable v1.7 pipeline. Check pause state, resource mode,
quota, file accessibility, OCR/local-AI availability, retry count, and
processing window. Cancellation and restart recovery apply normally.

## Diagnostics and privacy

Default Search diagnostics contain timing, counts, stages, coverage,
availability, and failure categories—not full queries, snippets, extracted/OCR
paragraphs, summaries, prompts, tokens, or unnecessary paths. Review every
explicit export before sharing.
