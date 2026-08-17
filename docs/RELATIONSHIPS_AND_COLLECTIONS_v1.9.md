# Relationships and Smart Collections in OpenSorSe v1.9

> v2.0 note: the unmerged Knowledge Graph candidate projects these existing
> relationships and Collections without replacing their schema-3 authority.
> See [Knowledge Graph v2.0](KNOWLEDGE_GRAPH_v2.0.md). This v1.9 guide remains
> authoritative for direct relationships and Smart Collections.

> v2.12 note: the current candidate keeps this schema-6 authority and adds
> capped evidence families, one pair-level Related Files projection, reversible
> Related/Not Related/Use automatic controls, compact bounded candidate
> hydration, and format-2 logical backup for authored collection state. Direct
> Related Files no longer depends on the optional Knowledge Graph. See
> [Trusted Relationships & Context](TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md).

OpenSorSe can use information already retained by background indexing to find
evidence that files belong together. It records why a relationship was made;
it does not ask you to trust an unexplained AI answer.

## What can create a relationship

Evidence can include identical content, a shared document identifier,
specific filename terms, matching retained text or OCR fingerprints, accepted
tags, generated keywords, a bounded summary fingerprint, nearby indexed
timestamps, and related-concept data that corroborates literal evidence.
Relationship processing is local and does not require Ollama.

Confidence is shown as Low, Medium, High, or Confirmed. These labels are fixed
outcomes from versioned rules, not probabilities. OpenSorSe deliberately avoids
displaying invented percentages.

Automatic analysis is conservative. A shared folder or semantic similarity by
itself is not enough. This reduces false positives, but it also means useful
relationships can be missed. You can add a manual relationship when needed.

## Collections page

The **Collections** destination contains two working areas:

- **Smart Collections** lists virtual groups, members, relationship evidence,
  and an optional timestamp timeline;
- **Related Files** selects one indexed file and lists direct related files,
  relationship type, confidence, evidence, and origin.

Collections never move original files. A timeline orders indexed timestamps; it
does not claim an event happened unless that timestamp exists in the index.

Use the controls to:

- link or unlink files;
- confirm or reject a suggestion;
- always relate or never relate a pair;
- rename, pin, merge, or split a virtual collection;
- sort or filter Related Files;
- rebuild relationship data for one file;
- repair stale or inconsistent derived records.

For one direct pair, **Related** stores positive authority, **Not Related**
stores negative authority, and **Use automatic result** clears that authority
and returns the pair to bounded deterministic analysis. Negative corrections
remain visible from the selected file so they can be reversed.

Manual changes persist. **Never relate**, collection splits, and forgotten
collections prevent immediate automatic recreation.

## Search context

Search can include direct relationship context. For example, an exact invoice
result may make its directly related warranty or payment record available even
when the second filename does not contain the query. **Why this result?** shows
the relationship evidence that contributed.

Exact and literal matches remain higher priority than relationship-only
results. Clear **Include related file context** for a query when you want only
ordinary Search ranking. Search still works when relationship analysis is
disabled, incomplete, or temporarily unavailable.

## Privacy and forgetting

The local index may retain relationship evidence, virtual memberships, and
derived context about selected files. The Collections privacy controls can:

- forget relationship data for a selected file;
- forget relationship data for its source;
- forget a virtual collection;
- suppress future relationship analysis for a file or source;
- rebuild a selected file after an intentional forget;
- disable relationship analysis globally or exclude configured file types in
  Settings.

These actions affect OpenSorSe-owned index data only. They never delete or edit
the original file, and they do not change whether a source is manually managed
or owned by Watched Folders. A suppressed file is also hidden from relationship
lists, collection membership, timelines, and contextual Search expansion.

Relationship evidence is bounded. Ordinary logs and aggregate relationship
diagnostics do not include extracted paragraphs, OCR text, summaries, full
paths, or semantic vectors. Do not assume the index is encrypted: protect the
application-data directory with operating-system account and disk controls.

## Limits

- Relationship analysis uses only retained indexed data; excluded or partially
  indexed files can have incomplete context.
- The initial algorithm does not perform face recognition, identity resolution,
  GPS clustering, or unrestricted graph traversal.
- Smart Collections are local virtual projections, not shared folders.
- The relationship layer is not a Knowledge Graph or conversational assistant.
- Future Knowledge Graph work is planned research, not v1.9 functionality.
