# 059 — Deep Indexing Foundation

## Release identity

- Version: `1.7.0`
- Branch: `v1.7-deep-indexing-foundation`
- Base: authoritative `main` commit
  `58b8a22312f09875e93df632143d24abf6397e26`
- Scope: durable local indexing, progressive Search coverage, storage policy,
  recovery, diagnostics, and compatible user-facing naming

## Implemented decisions

1. Extend the existing MVVM/application boundaries; do not replace scanning,
   catalogs, watched folders, content extraction, or semantic Search.
2. Add provider-neutral contracts in Application and isolate SQLite in a new
   provider project.
3. Use embedded SQLite because the high-cardinality staged queue and shared
   content relationships exceed the intended scope of bounded atomic JSON.
4. Require no server. Do not add PostgreSQL; a future server client can
   implement the same store/search contracts.
5. Preserve every existing public/internal compatibility identifier unless an
   additive overload or interface is needed.
6. Rename only user-facing **Meaning Search** text to **Search**.
7. Keep Basic as the conservative default; OCR, local-AI enrichment, archive
   indexing, and higher concurrency remain opt-in.

## Acceptance behavior

- Schema creation, future-version rejection, corruption detection,
  pre-migration backup, bounded manual/recovery backup retention (including
  sidecars), explicit reset with original preservation, concurrent
  readers/writers, disposal, and quota compaction are tested.
- Stable identity, rename/move, metadata change, content invalidation,
  duplicate sharing, deletion retention, case semantics, long/unusual names,
  empty files/folders, generated exclusions, and link boundaries are tested.
- All ten durable stage boundaries have cancellation tests.
- Pause/resume across restart, cancelled-run persistence, same-run discovery
  resume, discovery cancellation, shutdown/restart, retry exhaustion,
  locked/denied input, OCR/AI dependency loss/recovery, automatic eligible
  wait recovery, watched-source ownership, corrupt-store degraded operation,
  and inaccessible roots are tested with deterministic fakes and temporary
  data.
- Search remains functional with partial coverage and clearly states that
  unseen results are possible.
- Progress counts, ETA sample gating, storage breakdown, quota status, and UI
  command/accessibility contracts are tested.
- Existing catalog, scan, watcher, Change Plan, Undo, duplicate, workflow, and
  plugin tests remain unchanged except for intentional version/naming/project
  graph expectations.
- Bounded synthetic write-throughput checks cover increasing 100, 500, and
  1,000-file discovery batches; no million-file or absolute speed claim is
  made.

## Explicit non-goals

- Conversational Search
- A final hybrid ranking model
- Learned model downloads or cloud indexing
- PostgreSQL or a required database service
- Background processing while the desktop process is closed
- Archive-content readers beyond current extraction capabilities
- Broad relationship inference
- New plugin SDK contracts
- Any new direct or automatic source-file mutation
