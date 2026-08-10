# OpenSorSe v1.9 manual validation checklist

This checklist is intentionally an unexecuted evidence template. Every scenario
must remain unchecked until a maintainer performs and records the observation
on the intended host. Do not infer outcomes from automated tests.

Record before testing: branch and commit; operating system/version; filesystem;
desktop environment; power state; indexing policy; OCR executable/version;
Ollama endpoint/model; accessibility technology; synthetic test root; and
application-data backup location.

## Installation, migration, and recovery

- [ ] Start from a backed-up v1.8 schema-2 index and verify transactional v1.9 migration preserves Search, sources, files, jobs, privacy rules, and retained content.
- [ ] Interrupt a disposable migration attempt and verify the original/recovery copy remains actionable.
- [ ] Present an unsupported newer schema and verify OpenSorSe fails safely without rewriting it.
- [ ] Present a deliberately corrupt disposable relationship record and verify inspection/repair is actionable and source files remain unchanged.
- [ ] Force application termination during relationship analysis, restart, and verify no stage remains permanently Running.
- [ ] Restart after completed relationship work and verify unchanged files are not unnecessarily reanalysed.

## Relationship discovery and explanations

- [ ] Index a synthetic same-project set and verify only evidence-backed relationships appear.
- [ ] Index a synthetic trip set containing booking, ticket, photo metadata, GPX, expense, and packing records; verify conservative context and no invented events.
- [ ] Index a synthetic purchase set containing invoice, receipt, payment, warranty, and manual records; verify retained evidence matches the source signal.
- [ ] Verify identical content produces a Same Document Set relationship without duplicate source-file storage.
- [ ] Rename and move an unchanged file and verify identity reuse avoids unnecessary relationship analysis.
- [ ] Modify content and verify only affected relationship work is invalidated.
- [ ] Add unrelated distractors and inspect false positives.
- [ ] Add deliberately related but weak-evidence files and inspect false negatives.
- [ ] Repeat identical analysis and verify deterministic relationship type, confidence, ordering, and explanation.
- [ ] Verify malformed OCR, summary, metadata, Unicode, timestamps, and semantic data fail safely without an invented relationship.
- [ ] Verify confidence uses Low, Medium, High, or Confirmed and never presents an unexplained percentage.

## Smart Collections, context, and timeline

- [ ] Open Collections and verify title, description, relationship summary, confidence, creation source, update time, and member count.
- [ ] Verify automatic membership updates incrementally when a synthetic member is added, changed, moved, or deleted.
- [ ] Inspect collection context and verify every displayed claim is supported by stored evidence.
- [ ] Inspect the timeline and verify it orders only available indexed timestamps and identifies each timestamp source.
- [ ] Rename and pin a collection; restart and verify both choices persist.
- [ ] Merge two collections and verify virtual membership changes without moving source files.
- [ ] Split a member, rerun analysis, and verify the member is not immediately re-added.
- [ ] Forget a collection, rerun analysis, and verify its automatic context is not immediately recreated.
- [ ] Exercise a cyclic set of manual relationships and verify the UI remains responsive and bounded.
- [ ] Exercise the configured maximum collection membership and verify excess derived membership is bounded and reported safely.

## Related Files and manual control

- [ ] Select an indexed file and verify Related Files shows relationship, evidence, confidence, origin, and validation time.
- [ ] Sort Related Files by confidence, relationship, filename, and last validation.
- [ ] Filter by relationship type and minimum confidence; clear filters and verify all eligible direct relationships return.
- [ ] Link two files manually using a standard category and verify the relationship persists across restart.
- [ ] Create a bounded Custom relationship and verify its label and evidence are inspectable.
- [ ] Unlink a relationship and verify the original files remain unchanged.
- [ ] Confirm and reject automatic relationships; restart and verify the decisions persist.
- [ ] Apply Always relate and Never relate, rerun analysis, and verify each override is respected.
- [ ] Verify relationship deletion and duplicate analysis do not create duplicate edges or memberships.

## Search integration

- [ ] Search an exact filename and verify it remains above a loosely related contextual result.
- [ ] Search for a synthetic invoice and verify a directly related warranty/receipt can appear with actual relationship evidence.
- [ ] Open Why this result? and verify relationship context appears only when it contributed to ranking.
- [ ] Disable Include related file context and verify contextual-only results disappear while ordinary Search remains functional.
- [ ] Search during active indexing, paused indexing, cleanup, and compaction; verify responsiveness and honest coverage.
- [ ] Search after relationship analysis is disabled or temporarily unavailable; verify filename, metadata, text, OCR, filters, and v1.8 explanations still work.
- [ ] Cancel and rapidly repeat contextual searches; verify prompt cancellation, deterministic results, and no partial/corrupt result objects.

## Privacy, forgetting, and repair

- [ ] Disable relationship analysis globally and verify existing ordinary Search remains available.
- [ ] Exclude a configured file type and verify it is not analysed for relationships.
- [ ] Forget a selected file's relationship data and verify the source file bytes/timestamps remain unchanged.
- [ ] Forget and exclude a selected file; verify it is absent from Related Files, collection members/timeline, and contextual Search.
- [ ] Forget a manually managed source and verify source ownership/configuration remains intact.
- [ ] Forget a watched-folder source and verify watched-folder ownership remains intact.
- [ ] Rebuild a selected file and verify manual links/corrections remain while automatic data refreshes.
- [ ] Run derived-data repair and verify stale memberships, orphan evidence, and corrupt derived rows are handled without a full index rebuild.
- [ ] Inspect storage and privacy information and verify relationship counts are accurate after forget/rebuild/repair.
- [ ] Review relationship diagnostics and verify counts, timing, algorithm version, and repair operations do not expose document text, OCR text, summaries, vectors, secrets, or unnecessary paths.

## Dependency, performance, and resource behavior

- [ ] Remove Ollama availability and verify relationship analysis and ordinary Search remain useful.
- [ ] Restore Ollama and verify optional v1.8 enrichment can resume without duplicating unchanged relationship work.
- [ ] Remove OCR availability and verify metadata/text-based relationships continue and coverage remains honest.
- [ ] Restore OCR and verify affected durable stages can retry before relationship refresh.
- [ ] Exercise Eco, Balanced, and Fast indexing modes and verify the Collections UI stays responsive.
- [ ] Use increasing synthetic datasets and observe candidate bounds, incremental update cost, Search latency, memory, and cancellation without claiming unmeasured scale.
- [ ] Trigger database busy/maintenance behavior and verify a recoverable message plus ordinary Search fallback.

## Accessibility and regression

- [ ] Navigate Collections, Smart Collections, Related Files, inspectors, filters, and actions using keyboard only with logical focus order.
- [ ] Activate relationship explanation and privacy/help controls with mouse, keyboard, and touch/click where supported.
- [ ] Verify meaningful screen-reader names, selected states, evidence text, confidence labels, and live status announcements with the recorded accessibility technology.
- [ ] Verify high contrast, focus visibility, text scaling, narrow layout, and long bounded filenames/titles.
- [ ] Smoke-test Scan, Watched Folders, Search filters/snippets, duplicate review, workflows/plugins, Change Plan, Apply/recovery, and Undo without changing their established behavior.

## Completion record

Leave this section empty until every applicable observation has been performed.
Record failures and environment limitations rather than marking an unobserved
scenario complete. Interactive validation is required separately from the
automated v1.9 validation report.
