# OpenSorSe 1.7 Manual Testing

Use disposable synthetic folders only. This checklist is not marked complete
until a maintainer records observed results in the validation report.

Record the host operating system, OpenSorSe commit, enabled dependencies, test
data shape, and the observation for each exercised item. Leave an item unchecked
when it was not exercised, was unavailable on the host, or did not pass.

## Search and accessibility

- [ ] The feature is named Search in Files navigation, page headings, controls,
  empty states, Help, accessibility output, and current user documentation.
- [ ] The Search help affordance is available by mouse hover where supported,
  keyboard focus and activation, click or touch, and a screen reader. Confirm
  its accessible name and help text do not depend on hover alone.
- [ ] During partial indexing, Search remains responsive, returns currently
  available filename and metadata matches, and states that coverage is still
  being built rather than presenting an empty result as definitive.
- [ ] Coverage reporting distinguishes filename and metadata, extracted text,
  OCR, semantic, and fully indexed coverage without claiming unavailable
  processing is complete.

## Initial indexing, progress, and persistence

- [ ] Add a disposable synthetic folder and run initial indexing. Confirm each
  applicable file progresses from discovery through its selected Basic,
  Standard, or Deep stages without indexing outside the selected source.
- [ ] During indexing, confirm the UI shows the current stage and filename,
  processed, total, remaining, completed, skipped, failed, waiting, and retry
  counts, coverage, speed, current and maximum storage, and overall progress.
  Confirm the estimated remaining time is labelled as an estimate and remains
  hidden until sufficient samples exist.
- [ ] Select and save Basic, Standard, and Deep as the default indexing level
  in turn, and confirm each setting reloads without changing an existing source
  unexpectedly.
- [ ] Restart normally during active indexing and confirm recovery resumes the
  durable run, completed stages are reused, and no item remains stale in the
  running state.
- [ ] Restart after indexing work has completed and confirm the configured
  sources, settings, indexed progress, coverage, and searchable results persist.

## Pause, cancellation, and interruption recovery

- [ ] Pause an active run and confirm no later work is claimed while paused;
  Resume and confirm processing continues from durable state.
- [ ] Exit OpenSorSe while a run is paused, relaunch it, and confirm the run
  remains paused until Resume is requested.
- [ ] Cancel active indexing and confirm cancellation is safe and prompt, its
  state persists across restart, and completed stages are not repeated when the
  run is explicitly retried or refreshed.
- [ ] Force-terminate OpenSorSe during active processing, then relaunch it.
  Confirm interrupted running work is recovered or requeued, completed work is
  reused, and no job remains permanently marked as running.
- [ ] Repeat interruption recovery at representative durable stages:
  metadata or fingerprinting, text extraction, OCR or dependency wait, and
  search-index update.

## Dependencies and resource controls

- [ ] With local AI explicitly enabled, make Ollama unavailable during
  applicable processing. Confirm a truthful waiting or failure state, then
  restore Ollama and verify retry or resume continues without repeating
  unrelated completed work.
- [ ] With OCR explicitly enabled, make the configured OCR tool unavailable
  during an applicable file. Confirm a truthful waiting, skipped, or failure
  state, then restore it and verify retry or resume continues safely.
- [ ] Select Eco, Balanced, and Fast modes in turn. Confirm each selection
  saves and reloads. With sufficient synthetic work, use run diagnostics to
  confirm observed worker concurrency follows the documented bounded mode
  policy and the UI remains responsive.
- [ ] Where the host reports support for idle, power, battery, or schedule
  controls, exercise the enabled constraint. Where support is unavailable,
  confirm the application degrades clearly and continues safely; do not infer
  platform behaviour that was not observed.

## Source lifecycle and incremental behaviour

- [ ] Complete a manual scan so its root becomes an indexing source, prioritise
  it, run the background-index rebuild, then remove it. Confirm the source
  remains configured through the rebuild and removing it does not alter the
  source files.
- [ ] Configure separate manual and watched-folder sources. Change or remove
  one watched-folder source and confirm unrelated watched and manual sources,
  their progress, and their indexed data remain isolated.
- [ ] Rename and move an unchanged indexed file within the disposable data.
  Where stable identity is supported, confirm its path metadata changes while
  reusable content analysis is not repeated.
- [ ] Modify an indexed file's content without changing its path. Confirm only
  affected stages are invalidated and rerun, and Search reflects the new
  content after indexing completes.
- [ ] Copy duplicate content, change metadata only, delete a file, and restore
  it where practical. Confirm shared work, metadata updates, and retention
  follow the documented policies without breaking watched-folder activity.

## Storage, failures, and diagnostics

- [ ] Confirm current index size, configured maximum size, and the available
  metadata, extracted text, OCR, summary or keyword, semantic, relationship,
  job-history, and diagnostics breakdowns are truthful for the exercised data.
- [ ] With a safely low quota and synthetic data, approach the limit and
  confirm maintenance cleans eligible stale, orphaned, and expired operational
  data before blocking further work with a clear message. Confirm no source
  file or important user-visible metadata is silently deleted.
- [ ] Run the storage maintenance or compaction action and confirm its reported
  cleanup actions and resulting storage values are coherent. Reopen the
  application and confirm retained indexed data remains usable.
- [ ] Create a controlled locked, inaccessible, or otherwise failing synthetic
  item. Confirm failure counts and categories, failure inspection, maximum
  retry behaviour, and Retry failed items remain accurate.
- [ ] Open diagnostics for the current indexing run and confirm run ID, stage
  durations, queue lengths, retry counts, failure categories, dependency
  availability, database size, throughput, cancellation reason, resume
  information, and cleanup actions are present where applicable.
- [ ] Review any exported diagnostics before sharing and confirm no extracted
  text, OCR text, prompt body, token, or unnecessary absolute path is present.

## Existing-feature regression smoke tests

- [ ] Load existing saved scans and exercise watched folders, duplicate
  detection, workflows, and a compatible plugin without changing their
  persisted public behaviour.
- [ ] On disposable files, create and review a Change Plan, apply it, inspect
  Operation History and recovery information, then Undo it successfully.
