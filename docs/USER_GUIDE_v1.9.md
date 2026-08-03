# OpenSorSe v1.9 user guide

This guide adds v1.9 relationship and collection behavior to the v1.8 Search
and v1.7 background-indexing guides. Existing Scan, Watched Folder, duplicate,
workflow, Change Plan, recovery, and Undo instructions remain unchanged.

## Background relationship analysis

Relationship analysis runs as a durable background-indexing stage. It uses
metadata and derived data already retained at the selected indexing level, so
available evidence improves as indexing progresses. Completed unchanged work
is reused across pause, restart, dependency loss, and ordinary rescans.

In **Settings → Background indexing**, you can enable or disable relationship
analysis, set bounded candidate/relationship/collection limits, and exclude
file extensions. Disabling it does not disable ordinary Search or remove source
files. Use index privacy controls when you also want derived relationship data
forgotten.

## Browse Smart Collections

Open **Collections** from the main navigation. The **Smart Collections** tab
shows evidence-backed virtual groups. Select a collection to inspect:

- title, description, confidence, creation source, and update time;
- indexed member files;
- relationships and their retained evidence;
- a deterministic timeline from available creation/modification timestamps.

Rename or pin a collection to keep your preferred presentation. Merge combines
virtual membership; Split removes the selected member and records that choice.
None of these operations moves or changes a source file.

## Inspect Related Files

Open **Related Files**, choose an indexed file, and filter by relationship type
or minimum confidence. Sort by confidence, relationship, filename, or last
validation. Select an edge to inspect the evidence, algorithm/version, and user
decision.

Use **Confirm**, **Reject**, **Always relate**, **Never relate**, or **Unlink**
to correct suggestions. Use the two file selectors and relationship category to
create a manual link. A Custom relationship requires a short valid name.

## Use relationship-aware Search

The Search option **Include related file context** is enabled by default. Search
first performs its ordinary exact/literal-first ranking, then may add bounded
direct neighbors from already-ranked results. Explanations identify relationship
context when it actually contributed. Disable the option per query when you do
not want context expansion.

Search does not require Ollama. If the relationship index is unavailable,
filename, folder, metadata, text, OCR, filters, snippets, explanations, and the
other v1.8 ranking signals continue through their existing fallback behavior.

## Forget or rebuild derived data

The Collections privacy and repair area offers index-only actions:

- **Forget file relationships** removes automatic relationship data for the
  selected file;
- **Forget and exclude** also suppresses future relationship analysis;
- **Forget source relationships** applies the same boundary to the selected
  source without removing its indexing ownership;
- **Forget collection** removes only the virtual collection and records a
  tombstone to prevent immediate recreation;
- **Rebuild selected file** clears automatic edges, preserves manual choices,
  and queues the existing durable relationship stage for refresh;
- **Repair derived data** removes orphan/corrupt relationship records and stale
  memberships.

Every action states that original files remain unchanged. Review the selected
file or collection before applying an operation.

## Diagnostics

The Collections status includes relationship, collection, evidence, correction,
exclusion, candidate, timing, algorithm-version, and repair counts. It avoids
document text and unnecessary paths. Advanced Diagnostics keeps the existing
Search/index privacy boundary; exports remain explicit and reviewable.

## Accessibility

Collections, Related Files, inspectors, filters, explanations, privacy actions,
and maintenance controls provide meaningful accessible names. Tabs, lists,
selectors, and buttons support keyboard focus and activation as well as pointer
and touch/click use. Status text is exposed as a live region. Perform the
unchecked v1.9 manual checklist with the maintainer's target screen reader and
desktop platform before a release claim.

## Known limitations

Relationships are conservative and evidence-based, not exhaustive. Some
categories are primarily available for manual links until a future algorithm
has trustworthy evidence. Timelines expose indexed timestamps rather than
invented event narratives. Collections are local virtual data. v1.9 does not
implement a conversational assistant or the future Knowledge Graph.
