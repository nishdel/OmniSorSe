# OpenSorSe 1.7 Version Notes

OpenSorSe 1.7 introduces a durable background-indexing foundation.

- Meaning Search is now named **Search** in the user interface.
- Accessible concise Search help explains names, metadata, text, OCR, tags, and
  related concepts without implementation jargon.
- Basic, Standard, and Deep indexing levels provide conservative progressive
  coverage.
- A provider-independent application boundary uses embedded SQLite locally;
  no database server or PostgreSQL installation is required.
- Durable stage state supports pause, resume, safe cancellation, dependency
  waiting, bounded retry, process-interruption recovery, and completed-work
  reuse.
- Interrupted discovery resumes in the same run; explicit paused/cancelled
  state survives restart; dependency and resource waits resume when eligible.
- Stable identity, content fingerprints, shared content, retention, quota
  maintenance, and compaction bound long-term storage.
- Search shows exact coverage and remains available while deeper processing is
  incomplete.
- Progress exposes active stage/file, counts, speed, gated ETA, and storage.
- Search exposes an inspectable privacy-minimized failure list and direct
  current-run diagnostics.
- Corrupt or newer derived storage does not disable compatible existing Search;
  explicit rebuild preserves a bounded recovery copy before starting fresh.
- Existing JSON stores, APIs, workflows, plugins, watched folders, duplicate
  detection, Change Plans, Operation History, and Undo are preserved.

This release does not deliver conversational Search, a final intelligent
ranking engine, a server database, cloud indexing, or autonomous file changes.
