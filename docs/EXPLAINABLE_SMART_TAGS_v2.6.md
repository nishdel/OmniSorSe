# OmniSorSe v2.6 — Explainable Smart Tags

**Status:** unreleased implementation design and behavior record

**Branch:** `v2.6-explainable-smart-tags`

**Baseline:** committed v2.5 release candidate `59be07c6cebff12072cbf18701fb16cb11801287`

v2.6 extends the existing local indexing and Content/Media Intelligence pipeline
with durable, explainable classification. It does not create a parallel content
store, re-run OCR or transcription, write metadata into source files, or require
Ollama. Schema-6 SQLite is the authoritative store for classification and user
tag decisions.

## Product model

Smart Tags have three types:

- **Theme** is a bounded multi-label semantic classification. The English-first
  taxonomy v1 contains Administration, Education, Finance, Health, Housing,
  Insurance, Legal, Research, Technology, and Travel. At most three generated
  Theme assignments are active for one file.
- **Document Type** is normally one primary classification. Taxonomy v1 contains
  Booking, Budget, Certificate, Contract, Form, Invoice, Itinerary, Letter,
  Manual, Meeting Notes, Notes, Presentation, Receipt, Report, Research Paper,
  Resume/CV, and Statement.
- **User Tag** is an explicit bounded local label owned by the user.

Entities, dates/years, file/media type, author, device, location, EXIF, and
filesystem timestamps remain typed evidence/facets. They are not duplicated as
Smart Tag strings.

Canonical IDs such as `theme.finance` and `document-type.invoice` are stable and
language-neutral. Display labels and bounded aliases come from the validated,
embedded `smart-tags.en.v1.json` resource. Theme and Document Type are separate
taxonomies; neither is placed underneath the other. There is no online taxonomy
service.

## Confidence and authority

The deterministic classifier exposes **Strong**, **Moderate**, and **Limited**
evidence bands. These are not probabilities. Limited evidence is persisted only
as a classification outcome/fingerprint when useful and is not shown as a tag.
Moderate assignments are suggestions and do not contribute to ordinary Search
until accepted. Strong deterministic assignments are automatic Search evidence.
No AI-only assignment is auto-applied.

Authority is ordered as follows:

1. user-created tags and accepted Smart Tags;
2. explicit rejection;
3. Strong deterministic generated assignments;
4. Moderate generated suggestions;
5. hidden Limited/no-result evidence.

Accepted and rejected decisions are stored separately from generated evidence.
Reindexing, taxonomy reclassification, and path changes do not erase those
decisions. Removing a generated assignment records rejection; removing a User
Tag removes that explicit association. **Reset tag decisions** is the only
ordinary action that clears accept/reject overrides.

**Clear Generated Smart Tags** removes recomputable assignments and status while
preserving User Tags, accepted authority, and rejection decisions. Forgetting a
file/source and Clear Index follow existing index ownership semantics and remove
corresponding schema-6 rows without modifying source files.

## Evidence fusion and classification

Classification consumes already retained bounded evidence grouped into
independent families:

- native document content;
- OCR and sampled-frame OCR;
- transcript;
- structural/embedded and media metadata;
- filename and path as weak supporting evidence;
- derived topics, keywords, entities, and extractive summary.

Derived text is not counted again when its underlying direct content family has
already matched. Filename/path evidence alone cannot establish a semantic
Theme. Strong document structure or corroboration across independent evidence
can produce Strong classification. A close conflict between mutually exclusive
Document Types produces an explicit conflicting-evidence outcome rather than
two Strong types. No evidence, insufficient evidence, conflicting evidence, and
classified are distinct durable outcomes.

The classifier is local and deterministic. Optional Ollama ambiguity resolution
is not part of this implementation; deterministic classification remains useful
with AI disabled or unavailable.

## Durable indexing

`SmartTagsClassified` is a deferred durable stage after Content Intelligence and
before the final Search refresh. Fast/searchable-first mode can publish base
Search coverage before this stage. Deep initial analysis keeps its existing
per-file scheduling preference, but individual OCR, transcription, media, and
Content Intelligence switches remain authoritative.

The classification fingerprint includes source/content and retained-evidence
fingerprints, classifier version, and taxonomy version. A classifier/taxonomy
change prepares only Smart Tag reclassification; it does not invalidate or
repeat compatible OCR, transcription, media probing, topic extraction, entity
extraction, or summaries. Durable jobs retain the existing restart,
pause/resume, bounded retry, cancellation, and per-file failure behavior.

Bounded native text extraction now includes `.txt`, `.md`, `.markdown`, and
`.text`, with BOM-aware common Unicode handling, strict UTF-8 fallback,
character/file-size bounds, cancellation, and isolated failure. No new Office
parsing subsystem was added; CSV/XLSX/PPTX improvements are deferred.

## Schema 6

Schema 6 adds normalized tables for:

- Smart Tag definitions and taxonomy identity;
- file assignments with confidence, origin, provider/classifier identity,
  versions, input fingerprint, bounded evidence, state, and timestamps;
- user accept/reject decisions;
- per-file classification status, including no-evidence/conflict results.

Foreign keys attach assignments and decisions to stable file IDs and remove
orphaned state with file/source forgetting. Indexed joins support file-to-tag,
tag-to-file, tag-type filtering, active-state review, and bounded Search
projection without loading the full association graph into memory.

The schema-5-to-6 migration uses the existing recovery-copy and transactional
integrity conventions. Built-in definitions are seeded from the validated local
resource. Legacy path-keyed User Tags and accepted/rejected decisions are
imported only when a path resolves to exactly one active durable file identity.
Ambiguous legacy identities are not guessed; the legacy store remains available
until successful import is recorded. Regenerable deterministic date/file-type
labels are not blindly imported.

## Search and filters

Smart Tag evidence is additive to the existing deterministic ranker. Exact
filename/stem/prefix behavior stays protected. Search explanations distinguish
`Theme: Finance — Strong`, `Document Type: Invoice — Accepted`, and
`User Tag: Review`. Rejected assignments never contribute. Moderate suggestions
are excluded unless accepted (or an explicit future include-suggestions mode is
used).

Typed filters use canonical IDs. Values within one type are OR; populated types
are AND. Thus Theme Finance or Legal plus Document Type Invoice returns invoice
files classified as either Theme. Current entity/date/file-type filters remain
separate; v2.6 does not introduce an ambiguous generic Year Smart Tag.

Files displays compact grouped rows for Classifications, Suggestions, and Your
tags, with accessible state/confidence descriptions and Keep, Dismiss, Remove,
Reset, Clear Generated, and View files with this tag actions. The feature does
not add a Smart Tags dashboard or another collections subsystem.

## Privacy, diagnostics, and boundaries

Tags, user labels, evidence, and entities are content-sensitive local data.
Ordinary diagnostics may record counts, timings, classifier identity/version,
taxonomy version, confidence bands, fingerprints, and failure categories. They
must not record tag values, evidence excerpts, user labels, extracted content,
or AI prompt/response bodies. No telemetry or hidden network call is added.

Smart Tags never mutate files. They may be grounded evidence for a future
human-reviewed rename/folder suggestion, but Change Plans remain the only
mutation boundary. v2.6 does not write PDF/Office/image/audio/video metadata,
add embeddings/vector storage, modify Explorer Protocol v1 or OmniBrille, add a
cloud classifier, or perform facial/person recognition.
