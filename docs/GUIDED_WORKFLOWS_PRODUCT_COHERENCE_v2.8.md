# OmniSorSe v2.8 — Guided Workflows & Product Coherence

**Status:** implemented on `v2.8-guided-workflows-product-coherence` for review; not released

**Baseline:** committed v2.7 release candidate `4dca8a7adb088044576eb8d06fc5cc534deb7ad3`

v2.8 connects existing OmniSorSe capabilities around four ordinary tasks:

- **Find** uses deterministic Search, canonical facets, and dynamic Saved Views.
- **Understand** opens the richer Files/details surface for one stable file identity.
- **Review** walks unresolved Moderate Smart Tag suggestions without a separate dashboard.
- **Organize** keeps editable suggestions, Review Changes, Change Plans, reconciliation, and Undo as the only mutation path.

This is a product-coherence release. It does not add a new Search engine,
classification store, graph, protocol, server, or autonomous organizer.

## Discovery context and stable identity

Search and Files exchange a small explicit discovery context containing the
canonical query/filter rule, optional Saved View identity, selected stable file
ID, optional source, review mode, and a bounded result order. **Open in Files**
resolves the current durable record by ID and opens it in the existing Files
details surface. It does not launch the path directly or create a second details
implementation.

**Return to discovery** restores query text, canonical facets, Saved View, and
unresolved-review mode. A renamed or moved file remains the same logical item
when the durable ID remains valid. A deleted or unavailable record produces a
bounded warning and leaves Search usable; it is never guessed from an old path.

The context is an explicit navigation value, not a global mutable Search model.
Search remains the authority for query/facet/Saved View state.

## Continuous Smart Tag review

The unresolved Moderate facet starts a bounded review sequence:

1. open the current file and its bounded evidence in Files;
2. Keep or Dismiss the suggestion using schema-6 Smart Tag authority;
3. move to the next current unresolved item;
4. return to the preserved discovery rule when the sequence ends.

The review surface shows tag type, label, Strong/Moderate band, state, and at
most the existing bounded evidence reasons. It shows no fabricated percentages
or unbounded source excerpts. Accept/reject authority and reindex behavior are
unchanged from v2.6.

## Canonical facets

The older duplicate Theme/Document Type/User Tag picker state is retired from
Search. The v2.7 facet groups, active chips, counts, and Saved View rule are the
single canonical filter model. OR-within-type, AND-across-types, complete-index
candidate selection, bounded hydration, and filename-first final ranking are
unchanged.

## Durable Home

Home now projects bounded durable state instead of depending only on the last
scan in the current process. One refresh obtains:

- registered source and known-file counts;
- truthful base-Search and deeper-analysis phase;
- retained failure count;
- unresolved Moderate file count using a one-candidate complete-library count request;
- total Saved View count and at most three recently updated shortcuts, without executing them;
- compact optional-capability readiness.

Home never hydrates the indexed library, loads a Smart Tag graph, executes all
Saved Views, contacts Ollama, or launches an external tool. Capability checks
use existing local configuration/discovery for Ollama, Tesseract, ffprobe,
ffmpeg, whisper.cpp, and OmniBrille. States are **Ready**, **Disabled**, **Not
configured**, **Unavailable**, or **Needs attention**; an absent optional tool
does not make OmniSorSe unhealthy.

Home task actions route into existing pages. Understand and Organize explain
their selection prerequisite when no suitable Files context exists.

## Grounded organization evidence

Reviewed rename suggestions can receive at most four bounded classification
facts:

- accepted Theme or Document Type;
- Strong deterministic automatic Theme or Document Type;
- explicit User Tag.

Unresolved Moderate, Limited, and rejected classifications are excluded. The
review surface states the type, label, and authority used. This evidence can
inform a proposal only: the proposal stays editable and no file changes occur
without a validated, explicitly applied Change Plan.

## Terminology and advanced maintenance

- **Saved View** is a live Search/filter rule evaluated against the current index.
- **Saved scan** is an opt-in historical catalog snapshot.
- **Smart/Virtual Collection** is relationship-generated virtual membership.
- **Related Files** are evidence-backed neighbors of one selected file.

Search keeps index repair, rebuild, privacy clearing, and detailed maintenance
available behind collapsed advanced disclosure so ordinary Find work emphasizes
discovery. Critical progress, coverage, and safety facts remain visible.

## Architecture and privacy boundaries

- Schema remains **6**; there is no migration.
- Explorer Protocol remains **v1** and no wire DTO changes are made.
- OmniBrille and its repository are unchanged.
- Search ranking, candidate retrieval, facet SQL, Saved View persistence,
  progressive indexing, Smart Tag taxonomy/authority, relationships, and
  Change Plan/Undo architecture are unchanged.
- No production dependency, telemetry, cloud call, metadata writeback, or
  autonomous mutation is added.
- Home and navigation diagnostics retain only safe counts, states, timings, and
  failure categories. Raw queries, User Tags, Saved View rules, evidence
  excerpts, provider secrets, and sensitive tool paths are not logged.

See [the v2.8 manual checklist](MANUAL_TESTING_v2.8.md) for the honest manual
validation boundary.
