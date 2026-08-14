# OmniSorSe v2.7 manual testing

**Status:** unreleased validation checklist

This document separates automated evidence from scenarios that still require a
real desktop, large disposable library, accessibility technology, or native
platform. An unchecked item is not a claimed pass.

## Automated release evidence

- [x] Fresh Debug and Release non-incremental builds finish with zero warnings/errors.
- [x] Complete Debug and Release suites pass: 1,753 passed, 0 failed, 0 skipped
  in each configuration (24 tests above the committed v2.6 baseline).
- [x] A 20,000-file regression retrieves exact filename, stem, and prefix
  matches beyond the former 10,000-row projection, including canonical Smart
  Tag and explicit filesystem-date filters.
- [x] A controlled 100,000-file regression considers the complete library while
  hydrating 512 relevance-selected candidates. On the validation host, candidate
  selection took 2.840 seconds and six-group facet aggregation took 3.357 seconds.
- [x] SQLite facet counts, OR-within/AND-across semantics, unresolved Moderate
  review, Saved View persistence, extraction, privacy, and accessibility tests pass.
- [x] Explorer Protocol v1, workflow safety, performance (23/23), policy (8/8),
  format, analyzer, vulnerability, diff, and repository-integrity gates pass.
- [x] Release compilation passes for win-x64, linux-x64, osx-x64, and osx-arm64.
  Compilation is not native runtime validation.

## Complete-library retrieval

- [ ] Index a disposable library above 10,000 files and search for an exact
  filename deliberately sorted outside the first 10,000 paths.
- [ ] Repeat with the exact filename stem, a file-type facet, and a Smart Tag facet.
- [ ] Confirm exact filename/stem/prefix order remains above weaker content/tag matches.
- [ ] Inspect the separate indexed, candidate-coverage, and displayed-result facts.
- [ ] Cancel a large query and replace an in-flight query; confirm no stale result appears.

## Faceted discovery

- [ ] Combine two Themes and confirm OR within Theme.
- [ ] Add Document Type, User Tag, file type, created year, and modified year;
  confirm AND across populated groups.
- [ ] Confirm created and modified years are labelled as filesystem dates.
- [ ] Confirm counts respect query text and filters in every other group.
- [ ] Remove individual chips and Clear all; confirm one coherent query state.
- [ ] Let classification finish in the background and confirm counts/results
  increase without duplicate file identity or a false Complete state.

## Saved Views and Smart Tag review

- [ ] Save a query/filter rule, reopen it, rename/update it, and delete it.
- [ ] Change the indexed library and confirm the view reevaluates current data;
  no result membership should be copied into the Saved View store.
- [ ] Confirm Saved scans remain historical catalog snapshots with distinct wording.
- [ ] Open unresolved Moderate suggestions, Keep one, Dismiss one, and confirm
  each leaves the unresolved result without weakening reindex authority.

## Native extraction

- [ ] Index controlled UTF-8/BOM CSV and TSV files with quoted cells, embedded
  delimiters, malformed rows, and size/row bounds.
- [ ] Index controlled XLSX files with shared strings, inline strings, numeric
  values, dates, and formulas; verify formulas are never executed.
- [ ] Index controlled PPTX slides and speaker notes; verify embedded objects,
  macros, relationships, and external resources are not opened.
- [ ] Confirm extracted evidence can feed Search, Content Intelligence, and Smart
  Tags without modifying the source files.

## Windows desktop and accessibility

- [ ] Exercise facet disclosure, scrolling, search-with-filters, and Saved View
  actions using mouse and keyboard at 100%, 125%, and 150% DPI.
- [ ] Repeat in compact and normal windows; verify focus survives asynchronous
  count refresh when the focused value remains present.
- [ ] Use a Windows screen reader to verify group, value/count, selected state,
  removable chip, Clear all, Saved View, and live coverage announcements.
- [ ] Confirm no state relies on colour alone.

## Privacy and platforms

- [ ] Inspect ordinary and exported diagnostics for absence of raw query text,
  User Tags, facet labels, Saved View rules, and evidence excerpts.
- [ ] Confirm Saved Views remain local and no network request is introduced.
- [ ] Perform native Windows runtime validation.
- [ ] Perform native Linux runtime validation.
- [ ] Perform native macOS x64 runtime validation.
- [ ] Perform native macOS arm64 runtime validation.
