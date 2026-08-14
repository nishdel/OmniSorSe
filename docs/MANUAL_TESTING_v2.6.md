# OmniSorSe v2.6 manual testing

**Status:** unreleased implementation evidence tracker

Only genuinely performed scenarios may be checked. Automated coverage does not
substitute for desktop keyboard, screen-reader, large-library, provider-native,
or platform-native validation.

## Automated evidence

- [x] Fresh Debug and Release builds finish with zero warnings/errors.
- [x] Complete Debug and Release suites pass with 1,729 tests in each
  configuration, zero failures, and zero skips.
- [x] Schema-5-to-6 migration/reopen/recovery tests pass.
- [x] Taxonomy, deterministic classifier, evidence-fusion, user-authority,
  Search/filter, progressive indexing, privacy, and extraction tests pass.
- [x] Explorer Protocol v1 regression suite passes without a protocol change
  (47/47 focused tests).
- [x] Formatting, analyzers, policy, vulnerability, diff, and repository
  integrity checks pass.
- [x] Release compilation passes for win-x64, linux-x64, osx-x64, and
  osx-arm64. This is not native runtime validation.

## Schema-5 profile upgrade

- [ ] Create a disposable schema-5 profile with indexed files, accepted and
  rejected legacy tags, user tags, Content/Media Intelligence, and Search data.
- [ ] Open it with v2.6 and confirm a managed pre-migration backup is created.
- [ ] Confirm schema 6 opens, Search data remains, and source files are unchanged.
- [ ] Confirm uniquely resolvable user/accepted/rejected authority imports.
- [ ] Confirm ambiguous path identities are not guessed or destructively erased.
- [ ] Reopen and confirm migration/import are not repeated.
- [ ] Exercise controlled interrupted/corrupt/newer-schema recovery paths.

## Classification quality

- [ ] Use controlled invoice, receipt, contract, statement, report, manual,
  booking, itinerary, form, letter, meeting-notes, certificate, research-paper,
  finance, legal, travel, insurance, and technology fixtures.
- [ ] Confirm content can contradict a misleading filename.
- [ ] Confirm filename/path alone does not produce a semantic Theme.
- [ ] Confirm corroborating independent evidence can strengthen a result.
- [ ] Confirm derived topic/summary text is not visibly double-counted.
- [ ] Confirm ambiguous Document Types show an unresolved state instead of two
  Strong primary types.
- [ ] Confirm unsupported/no-evidence files are not labelled “Unknown”.
- [ ] Inspect at most a few clear evidence reasons and verify Strong/Moderate
  wording is not presented as statistical probability.

## User authority

- [ ] Add and remove a User Tag; reindex and rename/move the source file.
- [ ] Accept a Moderate suggestion and confirm it remains accepted after reindex.
- [ ] Reject/remove a generated tag and confirm it stays hidden after reindex.
- [ ] Change file content and taxonomy/classifier fingerprint; confirm decisions
  retain authority while generated evidence is refreshed.
- [ ] Reset tag decisions and confirm current generated evidence is reviewable.
- [ ] Clear Generated Smart Tags and confirm User Tags, accepted authority, and
  rejection decisions remain.
- [ ] Forget a file/source and Clear Index; confirm owned Smart Tag state is
  removed while source files remain untouched.

## Search and filtering

- [ ] Confirm an exact filename match remains above a tag-only match.
- [ ] Confirm Strong generated, accepted, and User Tag matches have accurate
  explanations.
- [ ] Confirm Moderate unaccepted and rejected tags do not affect ordinary Search.
- [ ] Select multiple Theme values and confirm OR semantics.
- [ ] Combine Theme, Document Type, and User Tag filters and confirm AND semantics.
- [ ] Clear filters and use View files with this tag.
- [ ] Confirm progressively arriving tags enrich one existing file result without
  duplicate identity or stale explanation.

## Progressive indexing and recovery

- [ ] In Fast/searchable-first mode, confirm base names/paths are searchable
  before classification completes.
- [ ] Restart, pause/resume, cancel/retry, and remove/move files while Smart Tag
  jobs are pending.
- [ ] Confirm a taxonomy-only change does not repeat OCR, transcription, media
  probing, topic/entity extraction, or summaries.
- [ ] Repeat in Deep initial analysis and confirm existing capability switches
  remain authoritative.
- [ ] Confirm missing optional media/OCR/transcription/Ollama providers do not
  block deterministic classification over available evidence.

## Text extraction

- [ ] Index disposable UTF-8/BOM `.txt`, `.md`, `.markdown`, and `.text` files.
- [ ] Confirm bounded content contributes to Content Intelligence and Smart Tags.
- [ ] Confirm malformed/oversized/unreadable files fail per item without blocking
  indexing or modifying the source.

## Desktop accessibility and privacy

- [ ] Verify compact chips/rows, grouped Classifications/Suggestions/Your tags,
  and `+N` behavior at representative window sizes and DPI settings.
- [ ] Use keyboard-only navigation for Keep, Dismiss, Add, Remove, Reset, Clear,
  and View files with this tag.
- [ ] Use a Windows screen reader to verify type, state, confidence band, and
  action labels; confirm no meaning relies on color alone.
- [ ] Inspect ordinary diagnostics and exported safe diagnostics for absence of
  raw tag values, user labels, evidence excerpts, entities, and source content.
- [ ] Confirm no source-file metadata changes and no network access occurs.

## Performance and platforms

- [ ] Exercise 10,000-file and 100,000-file controlled indexes plus hundreds of
  thousands of assignments where practical.
- [ ] Record base-search time, classification throughput, typed-filter/Search
  latency, restart reuse, taxonomy reclassification time, storage growth, and
  memory behavior.
- [ ] Perform native Windows runtime validation.
- [ ] Perform native Linux runtime validation.
- [ ] Perform native macOS x64 runtime validation.
- [ ] Perform native macOS arm64 runtime validation.
