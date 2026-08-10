# OpenSorSe 1.7 User Guide

## Search

The feature previously named **Meaning Search** is now **Search**. Existing
indexes and settings remain compatible. Search can use names, folder names,
metadata, document text, OCR, tags, summaries, and related concepts. Select or
focus the `?` help button beside the Search heading for the concise explanation;
the same action works with pointer, keyboard, touch/click, and screen readers.

Search remains usable while background indexing runs. A coverage message means
some files currently have only name/metadata coverage while others also have
document text, OCR, or related-concept data. When coverage is incomplete, no
result set should be treated as definitive.

## Indexing levels

| Level | Retained local information |
| --- | --- |
| Basic | Path, filename, extension, size, timestamps, metadata, fingerprint, and state |
| Standard | Basic plus bounded extracted text, normalized keywords, and one document-level related-concept representation |
| Deep | Standard plus applicable OCR, bounded summary, selected chunks, and richer staged processing |

Basic is the default. Executables, binary files, generated folders, and archive
contents use conservative defaults. Higher levels can consume more storage and
may depend on Tesseract or an optional local enrichment provider.

## Progress and control

The Search page shows:

- run state and active stage;
- the current filename;
- processed, discovered, and remaining counts;
- complete, skipped, failed, waiting, and retry counts;
- measured processing speed;
- an estimated remaining time only after enough samples exist;
- current and maximum index storage;
- coverage and storage-category breakdown.

Expand **Indexing failures** to inspect the bounded privacy-minimized failure
list. **Open run diagnostics** opens Advanced Diagnostics for the active run;
diagnostic export remains a separate reviewable action.

Pause stops new stages after current safe work finishes. Resume continues the
durable queue. Cancel safely asks discovery and workers to stop, retains
completed work, and records cancellation. Paused and cancelled states survive
restart: Resume is required for paused work, and Retry is required for
cancelled work. Retry failed items requeues eligible transient,
dependency-waiting, or cancelled work within the retry policy.
Prioritize changes which source is claimed first. Remove source deletes only
OpenSorSe-owned index records. Rebuild clears derived index data, keeps source
configuration, and indexes again.

## Recovery

OpenSorSe stores each stage durably. After application exit, operating-system
shutdown, power loss, or process interruption, startup requeues work that had
been running and resumes incomplete discovery in the same run. Completed
compatible stages are reused. If OCR or optional local AI is unavailable,
affected work waits and automatically becomes eligible again after the durable
retry time; other applicable work remains usable.

If the index is malformed, newer than this OpenSorSe version, or cannot be
opened, background indexing fails closed with recovery guidance while the
compatible existing Search path remains available. Pre-migration, manual, and
explicit recovery copies are kept in the managed backup directory, with at
most three retained. **Rebuild background index** preserves the unreadable
database and SQLite sidecars before creating a fresh schema; a newer schema is
never silently downgraded. Watched-folder configurations register their
sources again. A manually added source that existed only in an unreadable
database must be added again. Rebuild is safe because the index contains
derived application data, not copies of source files.

## Storage and privacy

Settings control maximum index size, text/OCR bounds, chunks, retention,
retries, concurrency, resource mode, time window, dependencies, generated
folders, binaries, and archives. Maintenance first removes stale/orphaned
derived records and expired operational history, then compacts storage. If the
quota remains exceeded, further work is blocked visibly.

The local index may contain filenames, paths, metadata, extracted text, OCR
text, tags, summaries, and related-concept data. It stays in the current user's
OpenSorSe application-data directory. No full source-file copies are stored.
Diagnostics omit document contents and minimize paths. A remotely configured
Ollama-compatible endpoint can send explicitly enabled AI input off-device;
the default background indexing path does not require it.

All organization behavior remains unchanged: suggestions enter a reviewed
Change Plan, Apply revalidates and journals, and Undo remains conflict-aware.
Indexing never grants file-mutation authority.
