# 060 — Search Intelligence, Quality and Privacy

## Release identity

- Version: `1.8.0`
- Branch: `v1.8-search-intelligence-privacy`
- Base: validated v1.7 commit
  `cce0d8a2e01ecba679f05c2baa02191c034c8365`
- Scope: local hybrid ranking, conservative query interpretation, visible
  refinement, result evidence, progressive coverage, indexed-data privacy,
  selective repair, security limits, and relevance measurement

## Gap analysis and decisions

| Existing v1.7 behavior | Current limitation | Intended v1.8 improvement | Compatibility risk | Migration impact | Required tests |
| --- | --- | --- | --- | --- | --- |
| Durable SQLite indexing and provider-neutral progressive documents | Stored summary, keyword, chunk, OCR, metadata, and semantic signals were not coherently ranked | Reuse the store and introduce one provider-neutral tiered ranker with explicit components | Candidate mapping must preserve v1.7 filename/metadata fallback and ordering safety | None; existing indexed fields are reused | Ranking signals, exact-versus-semantic conflict, stable order, storage fallback |
| Filename/path/text/OCR/tag matching and deterministic local vectors | Scores and explanations were coupled and filters were not user-visible | Separate query interpretation, candidates, ranking components, snippets, and explanations | Additive contracts must not force existing providers or UI callers to change | None | Interpreter, filter composition/removal, explanation provenance, snippet bounds/accessibility |
| Aggregate partial-index coverage | Dependency, exclusion, failure, and unavailable-index causes were not distinguished | Extend coverage through additive named properties and explicit excluded-path projection | Existing positional construction and legacy progressive providers must continue to work | None | Full/partial/excluded/waiting/failed coverage, provider failure isolation |
| Per-source lifecycle, rebuild, retry, and maintenance | No per-file inspection, forgetting, processing policy, or selective repair | Add application contracts and SQLite schema 2 privacy rules; preserve source ownership | Forget/repair must not remove source registration, watched ownership, or original files | Schema 1 to 2 adds one privacy-rule table transactionally | Inspection, forget, source ownership, shared content, repair, cancellation/recovery |
| Search worked without Ollama | Ordinary natural-language filters were absent | Use a deterministic clock/locale-aware grammar; AI remains optional and is not called for ordinary Search | Uncertain phrases must remain topic text instead of silently narrowing old searches | None | AI unavailable, clock/locale/date/type/size/source/tag filters, ambiguity |
| Schema 1 transactional SQLite storage | No durable anti-regeneration rule after a user forgets derived data | Retain explicit exclusion/suppression rules and apply them to queueing, Search, and coverage | A malformed or newer schema must fail safely without silent data loss | Transactional migration with managed pre-migration backup | v1.7 migration, interrupted migration behavior, newer/corrupt schema, anti-regeneration |

## Ranking policy

Ranking uses ordered evidence tiers rather than allowing one blended semantic
score to decide everything:

1. exact normalized filename;
2. exact literal phrase;
3. complete filename-token coverage;
4. literal filename, folder, path, extension, type, tag, metadata, extracted
   text, OCR, summary, keyword, or selected-chunk evidence;
5. bounded filename typo evidence;
6. related-concept similarity only.

Optional related-concept similarity supplements literal evidence. It cannot
promote a weak semantic-only result above an exact filename or stronger literal
tier. Within a tier, bounded evidence contributions are validated by the
synthetic relevance suite. Source priority, indexing completeness, modification
date, and ordinal full path are deterministic tie-breakers, not hidden semantic
boosts.

## Query interpretation

The local interpreter accepts at most 512 UTF-16 characters, 32 topic tokens,
and 16 filters. It rejects control characters, embedded nulls, malformed
surrogates, excessive tokens, and invalid explicit filters. It recognizes
conservative file types, explicit extensions/tags/sources/folders, bounded
sizes, ISO dates, years, current/previous month or year, locale month names,
indexing completeness/level, OCR availability, related-concept availability,
and failure state. Anything uncertain remains a visible topic phrase.

Interpreted filters are chips that can be removed individually or cleared
together. Editing a query clears old filters so a hidden constraint cannot leak
into the next Search.

## Privacy and repair

The UI can inspect category presence and bounded counts for metadata, extracted
text, OCR, summaries/keywords, related-concept data, selected chunks, failures,
history, level, source ownership, and update time. It never shows raw numeric
vectors and does not load a source file to create an inspection or snippet.

Index-only actions include forgetting a file/source, metadata-only policy,
deep-index exclusion, clearing OCR or related-concept data, retrying a failed
stage, rebuilding a file, and rebuilding a selected source. Forget actions
require confirmation. They preserve original files, manual source registration,
and watched-folder ownership. Durable suppression/exclusion rules prevent an
immediate regeneration loop. Shared-content clearing reports its complete
impact.

Selective repairs invalidate only the chosen stage and its dependants, persist
the requested restart stage, participate in ordinary progress/cancellation,
and resume through the v1.7 recovery pipeline.

## Storage and migration

SQLite remains an embedded implementation behind `IDeepIndexStore`,
`IProgressiveSearchSource`, and `IIndexPrivacyStore`. A bounded excluded-path
projection suppresses compatible legacy results without turning privacy
tombstones into Search candidates. Views and ViewModels use
application contracts and never create SQL. Schema 2 adds
`index_privacy_rules`, keyed by source and normalized relative path. Migration
from schema 1 is atomic, creates a bounded pre-migration backup, rejects newer
schemas, and retains existing sources/files/content/jobs unchanged.

PostgreSQL is neither required nor referenced. A future server provider can map
the same query, candidate, coverage, privacy, and repair contracts without
changing Views or ViewModels.

## Security boundaries

- All SQLite values remain parameters; ordinary Search never accepts SQLite
  FTS syntax.
- Candidate count, query length/tokens, result count, fuzzy candidates,
  Levenshtein input, snippets, retained text, chunks, and generated model output
  are bounded.
- Fuzzy work is cooperative-cancellable and only considers sufficiently long,
  small token sets.
- Malformed retained ranking JSON degrades to filename/metadata data and marks
  the result incomplete instead of constructing a corrupt result.
- Snippets replace malformed Unicode/control characters, have a hard 240
  character limit, identify their retained source, and never extract a file at
  query time.
- The four-query application gate bounds overlapping expensive Search work.
- Default diagnostics retain query length, token/filter/result counts, stages,
  timing, coverage, availability, and failure category—not complete queries,
  snippets, extracted/OCR text, summaries, prompts, secrets, or paths.
- Archive contents are not recursively expanded by the v1.8 Search path;
  existing bounded extractors remain authoritative. No new decompression or
  archive-traversal surface is introduced.

## Quality and performance evidence

`SearchQualityEvaluator` reports top-result correctness, top-k recall, mean
reciprocal rank, exact-match preservation, and stable ordering for supplied
synthetic corpora. The repository corpus covers vehicle repair, tax, travel,
climbing, battery research, employment, Raspberry Pi monitoring, recipes,
household records, unrelated technical documents, distractors, and exact
filenames. These are regression indicators, not claims of human understanding
or universal relevance.

Separate `SearchRelevance` and `PerformanceRegression` traits exercise corpus
quality, 100/1,000/5,000 candidate ranking, allocations, cancellation latency,
and SQLite query plans. Ordinary Search still uses bounded local candidate
selection; no million-file or universal latency claim is made.

## Explicit non-goals

- Conversational or autonomous file assistance
- Remote query interpretation or cloud Search
- Learned ranking or telemetry-based personalization
- PostgreSQL or another server dependency
- Raw embedding/vector presentation
- Improvised SQLite encryption
- Recursive archive indexing
- Any source-file mutation outside the existing reviewed Change Plan boundary
