# OpenSorSe 1.7 Troubleshooting

## Indexing says waiting

Check the active stage and dependency. OCR work waits when Tesseract or required
language data is unavailable. Optional local enrichment waits when its provider
is unavailable. OpenSorSe automatically makes the run eligible again at its
durable retry time after the dependency returns. Choose **Retry failed items**
to request an immediate retry, or **Resume** if the run was explicitly paused.
Name/metadata Search remains available.

## Indexing is paused or never starts

Check Background indexing settings: enabled state, Eco/Balanced/Fast mode,
processing time window, and source availability. Idle/power/battery policies
degrade gracefully when the current platform cannot report those signals.

## Storage limit reached

Choose **Maintain storage**. OpenSorSe removes expired deleted/history records,
orphans, and low-value selected chunks, then compacts the database. If the
limit remains reached, raise the explicit quota, lower the indexing level or
retention, remove an indexing source, or rebuild. Source files are not deleted.

## A file is failed or skipped

Open indexing failures/diagnostics and review the category. Locked or transient
I/O failures can be retried. Permission denied, missing files, links, or
unsupported input may require changing access/source policy. Skipped items are
counted and are not presented as fully indexed.

## The application or computer stopped unexpectedly

Restart OpenSorSe. Running stages are requeued and compatible completed stages
are reused; interrupted discovery resumes the same run without resetting
completed jobs. If a run remains paused, choose Resume. Cancellation remains
cancelled until explicitly retried.

## The index is corrupt or from a newer version

Do not delete unrelated application data. Review the actionable error and
provider-managed `deep-index-*.db` backups in the OpenSorSe index directory.
Existing compatible Search remains available while background indexing is
degraded. Prefer opening a newer-schema index with a compatible newer
OpenSorSe version. Choosing **Rebuild background index** explicitly preserves
the original database and sidecars as a bounded recovery copy before creating
a fresh derived index; a newer schema is never silently downgraded.

## Search appears incomplete

Read the coverage line. Some files may only be searchable by name and metadata
until text, OCR, or related-concept stages complete. The UI states:
“Search coverage is still being built. Some files may not appear yet.”
