# OpenSorSe 1.8 Manual Testing

Use disposable synthetic folders only. Record the operating system, exact
commit, dependencies, locale, data shape, and observation for every exercised
scenario. Every item intentionally remains unchecked; automated evidence is not
an interactive observation.

## Search quality and refinement

- [ ] Exact filename Search keeps the exact file above loosely related results.
- [ ] Full-text Search finds a file whose filename does not contain the query.
- [ ] OCR Search finds applicable retained OCR text and labels the evidence.
- [ ] Related-concept Search improves recall without overwhelming precise
  filename or literal text matches.
- [ ] Search remains useful with Ollama stopped or disabled.
- [ ] Search remains responsive while background indexing is active.
- [ ] Search with incomplete coverage clearly says some files may not appear.
- [ ] A natural-language absolute, relative, year, and locale month date filter
  is interpreted correctly for the recorded locale/clock.
- [ ] File-type and explicit extension filters are visible and correct.
- [ ] Remove one active filter and confirm the remaining filters/topic rerun.
- [ ] Clear all filters and confirm no hidden filter remains.
- [ ] A minor filename typo finds the intended file without unrelated flooding.
- [ ] **Why this result?** lists only evidence that actually contributed.
- [ ] A snippet is bounded, identifies its source, and highlights the matched
  term accessibly without showing a complete document.
- [ ] No-result states distinguish full coverage, incomplete indexing,
  exclusions, OCR wait, local-AI wait, failure, and maintenance/unavailability.

## Accessibility and input

- [ ] Complete Search, filter removal, explanation expansion, result opening,
  inspection, confirmation, and cancellation with keyboard only.
- [ ] Verify meaningful screen-reader names, result explanation reading order,
  snippet source, active-filter controls, live status, and focus order.
- [ ] Activate Search help, filters, explanations, inspection, and confirmation
  through mouse and hover where supported.
- [ ] Activate Search help, filters, explanations, inspection, and confirmation
  through click or touch; confirm no required information depends on hover.

## Indexed-data privacy

- [ ] Inspect indexed data for Basic, Standard, and Deep files and compare the
  displayed category/count information with the configured policy.
- [ ] Confirm raw related-concept vectors and complete extracted/OCR text are
  not shown in the ordinary inspection UI.
- [ ] Forget one indexed file after confirmation and confirm it leaves Search
  while the original source file remains byte-for-byte unchanged.
- [ ] Forget one manually managed source and confirm source registration and
  original files remain unchanged.
- [ ] Forget one watched-folder source's index data and confirm watched-folder
  ownership remains intact without an immediate re-index loop.
- [ ] Clear OCR-derived data and confirm coverage/policy update accurately.
- [ ] Clear related-concept data/chunks and confirm ordinary literal Search
  remains available.
- [ ] Downgrade a file to metadata-only and confirm deeper content is removed
  and not regenerated automatically.
- [ ] Exclude a file/folder from future deep indexing and confirm exclusion
  impact appears in coverage.
- [ ] Disable OCR, summaries/keywords, and related-concept processing in
  Settings and confirm each choice persists after restart.

## Repair, recovery, and concurrency

- [ ] Re-index a selected file and confirm only its applicable stages rerun.
- [ ] Rebuild a selected source and confirm other sources remain isolated.
- [ ] Retry a selected failed stage and confirm completed unrelated work is
  reused.
- [ ] Cancel a selected repair and confirm a safe durable boundary and accurate
  coverage.
- [ ] Terminate OpenSorSe during a selected repair, restart, and confirm durable
  recovery leaves no stale running stage.
- [ ] Search during paused indexing, cancelled indexing, restart recovery,
  cleanup, and compaction.
- [ ] Search during file deletion, rename, move, source removal, and dependency
  loss/restoration; confirm stale records behave predictably.
- [ ] Exercise rapid repeated and overlapping Search; confirm the UI remains
  responsive and stale query results do not replace newer results.
- [ ] Cancel an active Search and confirm no corrupt partial result list appears.
- [ ] Exercise Search while the index is busy or maintenance is active.

## Security and diagnostics

- [ ] Enter a very long query and confirm a bounded actionable error.
- [ ] Enter malformed/control-containing text through an appropriate test input
  method and confirm safe rejection without a crash.
- [ ] Try wildcard/SQL/FTS-like punctuation and confirm it is treated as bounded
  ordinary text rather than executable syntax.
- [ ] Search hostile but valid filenames and malformed retained snippet text
  from a disposable fixture; confirm safe rendering.
- [ ] Review Search diagnostics and confirm duration, result/filter counts,
  ranking stages, coverage, availability, cancellation/timeout/failure category
  appear where applicable.
- [ ] Confirm default diagnostics and reviewed exports omit complete queries,
  snippets, extracted/OCR paragraphs, summaries, prompts, tokens, secrets, and
  unnecessary absolute paths.

## Existing-feature regression

- [ ] Load existing saved scans/catalogs and run a saved catalog Search.
- [ ] Exercise watched folders, duplicate detection, workflows, and a compatible
  plugin without changing their persisted public behavior.
- [ ] On disposable files, create/review/apply a Change Plan, inspect Operation
  History, and Undo it successfully.
