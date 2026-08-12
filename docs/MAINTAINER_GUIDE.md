# Maintainer guide

This guide records the cross-cutting responsibilities required to keep an
OmniSorSe release compatible, safe, and understandable.

Use [Engineering Principles](../ENGINEERING_PRINCIPLES.md) for the reasoning
behind these operational requirements and
[Product Roadmap](../PRODUCT_ROADMAP.md) for version/integration status.

## Release checklist

1. Confirm the release branch, base commit, upstream, worktrees, and absence of
   an in-progress Git operation.
2. Review every working-tree file; separate implementation, documentation,
   generated output, and unrelated user work.
3. Verify version metadata and release documentation.
4. Run restore, Debug build/tests, Release build/tests, formatting, whitespace,
   documentation-link, Mermaid, machine-path, and artifact checks.
5. Complete the current manual checklist on disposable data, or record the
   explicitly approved post-publication/community-testing boundary without
   marking any unobserved scenario complete.
6. Inspect privacy-sensitive diagnostics and package contents.
7. Only after all gates pass, follow an explicitly approved commit/tag/package/
   publish workflow. Source validation alone does not claim a release exists.

The historical [v1.0 release checklist](RELEASE_CHECKLIST_v1.0.md) remains a
frozen release snapshot. Apply the current
[v2.0 manual checklist](MANUAL_TESTING_v2.0.md),
[native packaging procedure](RELEASE_PACKAGING_v2.0.md), and
[Release Status](RELEASE_STATUS.md) instead.

Record Windows, Linux, and macOS results independently. A green local Windows
run does not prove the Ubuntu workflow ran, and a successful Linux source build
does not prove every distribution, desktop, mount, watcher limit, native
dependency, or packaging format. Never turn an unobserved CI definition into a
passed-CI claim.

## Version metadata locations

| Location | Responsibility |
| --- | --- |
| `Directory.Build.props` | Product, informational, assembly, and file version defaults |
| `src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj` | Desktop package version |
| `src/OpenSorSe.Desktop/app.manifest` | Windows assembly identity |
| `src/OpenSorSe.Desktop/ViewModels/AboutViewModel.cs` | User-visible short version |
| `README.md`, `docs/CHANGELOG.md`, `docs/VERSION_NOTES_v*.md` | User/release narrative |
| `docs/RELEASE_STATUS.md` | Current integration, validation, package, tag, and publication readiness |
| `PRODUCT_ROADMAP.md`, `RELEASE_HISTORY.md` | Planned direction and concise historical branch/date/merge index |
| Workflow/plugin constants and import envelopes | Compatibility/schema identity, not marketing text |

Tests should assert compiled metadata and About presentation. Do not update only
the visible version.

## Persistence and migration ownership

The owner of a persisted model owns its bounds, validation, migration, atomic
write, corruption behavior, and tests.

| Store | Owner |
| --- | --- |
| Settings and logs | Core |
| Catalog, saved searches, content, semantic index, structure history | Application subsystem containing the store |
| Durable Search index | Application contracts and `OpenSorSe.Indexing.Sqlite` provider |
| Knowledge Graph derived sidecar | Application graph contracts and `OpenSorSe.Indexing.Sqlite.KnowledgeGraph` provider; rebuildable |
| Knowledge Graph decision sidecar | Application graph decision/privacy contracts and `OpenSorSe.Indexing.Sqlite.KnowledgeGraph` provider; non-rebuildable |
| Watched configuration/catalogue/activity | Application/Watching |
| Workflow library/import/export | Application/Workflows |
| Plugin state and controlled installed versions | Application/Plugins |
| Change Plans and Operation Journal | Executor |
| AI decision history | AI transport project |

For a schema change:

1. decide whether old data migrates, remains readable, or is explicitly
   unsupported;
2. increment the owning schema identity where required;
3. validate all records after migration;
4. write the upgraded representation atomically;
5. preserve or safely reject corrupt/unknown data;
6. add old-to-new, future-version, malformed, oversized, and cancellation
   tests;
7. update Architecture Overview, Safety and Privacy, migration notes, and
   Version Notes.

For the durable Search index, also test `PRAGMA quick_check`, unsupported newer
schemas, interrupted migration recovery copies, startup recovery of every
`running` job/stage row, WAL/backup lifecycle on Windows, connection disposal,
quota maintenance, and rebuild source preservation. PostgreSQL is not a
desktop dependency; alternate providers must preserve the provider-neutral
contract.

Schema 2 adds durable privacy rules. A v1-to-v2 migration must retain sources,
files, shared content, stages, coverage, and watched/manual ownership while
creating a pre-migration recovery copy. Validate inspection, forgetting,
selective clearing, suppression against immediate re-index loops, targeted
repair, duplicate-content impact reporting, and unchanged source files.

Schema 3 adds relationship features, evidence-backed edges, pair corrections,
virtual collections/membership, forgotten projections, diagnostics, and the
relationship-suppression privacy bit. A v2-to-v3 migration must be
transactional and recovery-copy protected. Validate manual correction
retention, collection/member bounds, privacy filtering, source ownership,
orphan/corrupt derived-row repair, exact-first Search expansion, and unchanged
source files.

v2.0 does not increment the durable Search index schema: `deep-index.db`
remains schema 3. It bootstraps independent schema-1
`knowledge-graph.db` and `knowledge-decisions.db` sidecars with distinct
application IDs, migration histories, integrity checks, and recovery behavior.
The graph store is derived and selectively rebuildable. The decision store is
authoritative for graph-native user decisions and privacy state and must never
be silently deleted, reset, or replaced by graph rebuild.

For Knowledge Graph changes, validate completed manifest ID/count/hash and
bounded paging; separate ingested/applied source, decision, and privacy
watermarks; four-axis state; atomic generation publication; fencing epoch plus
claim token; 5-second heartbeat, 30-second TTL, and 5-second shutdown grace;
legacy v1.9 decision authority; point-of-use privacy; verified backup privacy
floor; unsupported/corrupt/busy/low-resource behavior; deterministic rebuild;
and unchanged source files. Cross-store lifecycle work must take the outer
application-data lock and must not pretend to use a nested atomic transaction.

The v2.0 candidate exposes decision recovery through the provider-neutral
`IGraphDecisionRecoveryService` maintainer/integration path; there is no claimed
end-user restore button. List recovery points through that service so only
bounded identifiers, sequences, generations, times, and status codes are
shown; managed database paths and private document content are never shown.
Restore only after entering the exact confirmation `RESTORE GRAPH DECISIONS`. The provider
re-verifies integrity, checksum, application/schema identity, sequence, and the
privacy floor before journaled same-volume promotion. Corrupt or foreign
points, points below the privacy floor, and unsupported newer schemas remain
blocked. If promotion is interrupted, restart and initialize the graph storage
lifecycle so it deterministically finishes or rolls back before graph use.
Never substitute a manual database copy. Source files and `deep-index.db` are
outside this operation.

Corrupt derived-store replacement is a different candidate operation exposed
through `IGraphDerivedStoreRecoveryService`; it is also a reviewed
maintainer/integration path without an end-user button. Enter exactly
`REBUILD DERIVED GRAPH STORE`. The provider first validates the authoritative
decision sidecar, then journals a same-volume quarantine and promotion of a
validated empty graph sidecar, and validates decisions again before completing.
Reinitialize and invoke the reviewed path again after interruption so the
journal resumes deterministically. Preserve the quarantine for inspection.
The path rejects healthy and unsupported-newer graph stores and never changes
`knowledge-decisions.db`, `deep-index.db`, or source files.

The v2.0 implementation records separate automated, native-package, and
interactive evidence. Keep every box in `MANUAL_TESTING_v2.0.md` unchecked
until directly observed and reviewed. For v2.0.0, the maintainer explicitly
authorized broad interactive/community testing to begin after publication;
that decision does not convert unperformed RC or manual scenarios into passed
evidence. Use `V2.0_RC_STABILIZATION_PLAN.md` for later structured soak and
fault campaigns and triage findings through v2.0.x when appropriate.

For Search changes, run `Category=SearchRelevance` and
`Category=PerformanceRegression` in addition to the full suite. Inspect the
SQLite query plans exercised by provider tests, keep query/candidate/snippet
bounds intact, and compare exact-match preservation, top-k recall, reciprocal
rank, stability, cancellation, allocations, and increasing synthetic corpus
sizes. These are regression controls, not universal quality or latency claims.

For relationship changes, additionally inspect the deterministic evidence
matrix, false-positive/false-negative fixtures, algorithm version, feature
indexes/query plans, candidate and member caps, cancellation, user override
semantics, virtual collection tombstones, Search fallback, diagnostics
redaction, and `MANUAL_TESTING_v1.9.md`. Never replace evidence with a model's
unsupported explanation or present a rule score as a probability.

Never silently reinterpret a field in a way that could authorize broader file
operations.

## Safety invariants

Review these on every release:

- All current Desktop organization flows create Change Plans.
- `ChangePlanReviewViewModel` still requires action decisions, validation, and
  a separate Apply confirmation.
- `ChangePlanExecutionService` revalidates immediately and journals before
  mutation.
- `PhysicalFileSystemGateway` forbids overwrite.
- Rollback/Undo verify current identity and never replace occupied data.
- Watchers call proposal/review services, not execution.
- AI remains optional, bounded, validated, and suggestion-only.
- Workflow policy can narrow application safety gates but cannot broaden them.
- Plugins have no supported direct mutation/approval path; capabilities are
  checked at registration and invocation.
- Plugin integrity and load-context isolation are not described as publisher
  authentication or sandboxing.
- Application stores cannot escape their controlled files/directories.
- Relationship and collection actions affect derived index data only; they do
  not acquire a source-file mutation path.
- Knowledge Graph projection, privacy, decisions, and repair affect only
  application-owned sidecars; they never open, modify, or delete source files.
- Graph reads and Search expansion fail closed when privacy/decision/source
  authority is unavailable or an applied watermark lags its authority.
- Disabling or damaging the graph cannot block ordinary Search, indexing,
  Collections, Change Plans, recovery, or Undo.

## Journal compatibility

The Operation Journal is recovery state, not expendable telemetry. Maintain:

- stable operation/action identities;
- durable pending/running state before the first corresponding mutation;
- enough original/result identity to inspect interruption and Undo;
- safe errors without raw sensitive content;
- monotonic action transitions;
- backward-compatible loading or an explicit release-blocking migration;
- bounded retention that does not remove active recovery facts.

Changing execution ordering, case-only rename handling, rollback, or Undo
requires interruption tests at every durable boundary.

## Plugin compatibility

- Keep `OpenSorSe.Extensions.Abstractions` independent of internal projects.
- Treat public SDK types and semantics as compatibility surface.
- Update XML documentation, SDK guide, author guide, manifest reference,
  package guide, host validation, and compatibility notes together.
- Do not add a capability without user-visible grant semantics and enforcement.
- Do not add an extension point without input/output bounds, cancellation,
  timeout, validation, provenance, failure containment, and adversarial tests.
- Profile/recipe dependencies use exact plugin versions; do not silently drift.
- A stronger isolation model requires a new out-of-process compatibility
  design, not a documentation-only claim.

## Documentation maintenance

Every release must keep these entry points current:

- root README;
- Product Vision, Product Roadmap, Engineering Principles, and Release History
  when their scope changes;
- `docs/README.md`;
- User Guide, Troubleshooting, Manual Testing, and Version Notes;
- Architecture Overview, Repository Structure, and System Map;
- Safety and Privacy;
- Changelog and Release Status;
- relevant implementation specification and subsystem architecture;
- Extension SDK/plugin documents when applicable.

Run documentation validation after renames. Preserve meaningful historical
documents and label them; do not rewrite old release evidence to look current.
The [documentation inventory](DOCUMENTATION_INVENTORY.md) records the authority
model.

## Generated and private data

Never commit:

- `bin`, `obj`, `.artifacts`, `TestResults`, or IDE state;
- new release binaries, ZIPs, checksums, or packages outside an approved release;
- `%LOCALAPPDATA%\OpenSorSe` settings, indexes, histories, plugins, or logs
  (the legacy name is intentionally retained by OmniSorSe v2.4);
- diagnostic exports without explicit review and redaction;
- OCR temporary pages or test workspaces;
- credentials, tokens, private endpoints, machine-specific paths, or user file
  samples.

## v2.2 media maintenance

- Treat [Media Intelligence v2.2](MEDIA_INTELLIGENCE_v2.2.md) as the exact
  candidate boundary. Check an item in [its manual checklist](MANUAL_TESTING_v2.2.md)
  only after recording the exact native or interactive observation; keep
  automated-only, unavailable-dependency, and untested-platform claims clearly
  distinguishable.
- Validate deterministic image parsing without installed tools, then validate
  `ffprobe`, `ffmpeg`, Tesseract, and any future transcription/visual provider
  separately on each actual host where a runtime claim is made.
- Confirm schema-3-to-4 migration, corruption/newer-schema rejection, cache
  reuse/invalidation, derived-data clearing, quota cleanup, cancellation, and
  retry after a provider becomes available.
- Inspect optional executable paths and arguments in diagnostics without
  exporting private media evidence. Never package a user-managed media tool or
  codec accidentally.
- Cross-target compilation is not native codec/extraction evidence. Record the
  exact executable build, platform, formats, and operations tested.

## v2.3 Content Intelligence maintenance

- Treat [Content Intelligence v2.3](CONTENT_INTELLIGENCE_v2.3.md) as the
  unmerged implementation boundary and keep
  [its manual checklist](MANUAL_TESTING_v2.3.md) honest about fake-provider,
  native-provider, interactive, and cross-target evidence.
- Preserve exact/literal Search tiers when changing topic/entity/summary
  weights. Derived signals and optional AI cannot introduce file membership.
- Changes to deterministic extraction, relevant bounds, whisper.cpp
  runtime/model metadata, or provider contract version must invalidate the
  processing fingerprint. Avoid hashing a large model on every file operation.
- Validate schema-4-to-5 migration, recovery-copy stability, malformed evidence,
  clear/forget, relationship regeneration, and source-file preservation.
- whisper.cpp, its model, ffmpeg, ffprobe, Tesseract, and Ollama remain external
  user-managed capabilities. Never add a downloaded runtime/model, private
  sample, or provider workspace to Git or release artifacts.
