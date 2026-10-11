# Maintainer guide

This guide records the cross-cutting responsibilities required to keep an
OmniSorSe release compatible, safe, and understandable.

Use [Engineering Principles](../ENGINEERING_PRINCIPLES.md) for the reasoning
behind these operational requirements and
[Product Roadmap](../PRODUCT_ROADMAP.md) for version/integration status.

For current failure response use the
[v2.10 operational runbooks](OPERATIONAL_RUNBOOKS.md). Prepare one release
manual-testing issue before execution using the [short guide](MANUAL_TESTING.md#release-testing-procedure),
[template](../.github/ISSUE_TEMPLATE/release-manual-testing.md) and
[verified release index/drafts](release-testing/README.md). The issue owns live
results; repository documents own test definitions. The
[coverage map](release-testing/coverage-map.md) preserves the v2.10 master matrix,
v2.11–v2.13 addenda and earlier requirements without creating another status ledger.
Update tests when behavior/platform risks change, identify the exact package/hash,
and retain older candidate/failed evidence before resetting rows for retest.
Unselected inherited requirements remain unresolved, not implicitly accepted.

Use OmniLAB / LocalAgentBridge by default for suitable substantive development
work when available. Installation remains optional; the routing policy applies
whenever it is available and the work is suitable. Follow the
[OmniLAB / LocalAgentBridge policy](../AGENTS.md#optional-omnilab-development-aid).
Use the existing installed runtime, routing/profiles, budgets, evidence handoffs
and review checkpoints when helpful; keep its coordinating environment fixed and
separate from development edits. Small work need not become a multi-agent run.
Codex defines scope and owns supervision, independent verification and final
decisions. Record actual local contributions and limitations; neither local
output nor runtime availability is a manual-test result or release approval.

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

The historical [v1.0 release checklist](MAINTAINER_GUIDE.md#historical-v1-0-release-checklist) and
[v2.0 manual checklist](MANUAL_TESTING.md#manual-v2-0) remain evidence for those
versions. Apply the current master matrix/addenda above, the
[native packaging procedure](RELEASE_PACKAGING.md), and
[Release Status](RELEASE_STATUS.md) for a current release decision.

Record Windows, Linux, and macOS results independently. A green local Windows
run does not prove the Ubuntu workflow ran, and a successful Linux source build
does not prove every distribution, desktop, mount, watcher limit, native
dependency, or packaging format. Never turn an unobserved CI definition into a
passed-CI claim.

## Version metadata locations

| Location | Responsibility |
| --- | --- |
| `Directory.Build.props` | Authoritative product, informational, assembly, file-version, source-revision, and build-configuration defaults |
| `src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj` | Desktop package version |
| `src/OpenSorSe.Desktop/app.manifest` | Windows assembly identity |
| `src/OpenSorSe.Desktop/ViewModels/AboutViewModel.cs` | User-visible version and source provenance projection |
| `eng/release/Build-*`, `Validate-*`, package build manifest | Inject and verify exact release version/commit/package agreement |
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
| Saved discovery views | Application/Semantic; dynamic query rules, not file membership |
| Durable Search index | Application contracts and `OpenSorSe.Indexing.Sqlite` provider |
| Knowledge Graph derived sidecar | Application graph contracts and `OpenSorSe.Indexing.Sqlite.KnowledgeGraph` provider; rebuildable |
| Knowledge Graph decision sidecar | Application graph decision/privacy contracts and `OpenSorSe.Indexing.Sqlite.KnowledgeGraph` provider; non-rebuildable |
| Watched configuration/catalogue/activity | Application/Watching |
| Workflow library/import/export | Application/Workflows |
| Plugin state and controlled installed versions | Application/Plugins |
| Change Plans and Operation Journal | Executor |
| AI decision history | AI transport project |
| Profile lock and run marker | Core platform/lifecycle; Desktop acquires and completes them around the profile-service lifetime |
| Logical state backup/restore | Application/Resilience coordinates the owning stores; it does not become a duplicate authority |

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

At v2.0, the release did not increment the durable Search index schema:
`deep-index.db`
remains schema 3. It bootstraps independent schema-1
`knowledge-graph.db` and `knowledge-decisions.db` sidecars with distinct
application IDs, migration histories, integrity checks, and recovery behavior.
The graph store is derived and selectively rebuildable. The decision store is
authoritative for graph-native user decisions and privacy state and must never
be silently deleted, reset, or replaced by graph rebuild.

Subsequent released/candidate migrations advance the current durable index:
schema 4 adds shared bounded media evidence, schema 5 adds bounded Content
Intelligence, and schema 6 adds normalized Smart Tag authority and canonical
facet joins while retaining relationships, Smart Collections, pair/collection
authority, and privacy. The current schema identity is
`DeepIndexingVersion.SchemaVersion`; do not infer it from a versioned graph or
relationship document. Validate each intervening migration/recovery-copy path,
future-schema rejection, and preservation of user-authored authority.

For Knowledge Graph changes, validate completed manifest ID/count/hash and
bounded paging; separate ingested/applied source, decision, and privacy
watermarks; four-axis state; atomic generation publication; fencing epoch plus
claim token; 5-second heartbeat, 30-second TTL, and 5-second shutdown grace;
current schema-6 relationship/Smart Collection authority; point-of-use privacy;
verified backup privacy
floor; unsupported/corrupt/busy/low-resource behavior; deterministic rebuild;
and unchanged source files. Cross-store lifecycle work must take the outer
application-data lock and must not pretend to use a nested atomic transaction.

The v2.0 implementation exposes decision recovery through the provider-neutral
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
interactive evidence. Keep every box in `MANUAL_TESTING.md#manual-v2-0` unchecked
until directly observed and reviewed. For v2.0.0, the maintainer explicitly
authorized broad interactive/community testing to begin after publication;
that decision does not convert unperformed RC or manual scenarios into passed
evidence. Use `MAINTAINER_GUIDE.md#historical-v2-0-stabilization` for later structured soak and
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
redaction, format-2 logical backup/format-1 read compatibility, and the current
[v2.12 manual addendum](MANUAL_TESTING.md#manual-v2-12). Use
`MANUAL_TESTING.md#manual-v1-9` only for inherited historical scenarios. Never replace
evidence with a model's unsupported explanation or present a rule score as a
probability.

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
- AI remains optional, bounded and structurally validated. Indexed inferences
  are automatic with provenance; filesystem proposals require review and approval.
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
- Current State and the glossary when volatile facts or terminology change;
- User Guide, Troubleshooting, Manual Testing, and Version Notes;
- Architecture Overview, Repository Structure, and System Map;
- relevant ADRs and Mermaid diagrams when ownership, boundaries, or durable
  reasoning change;
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

- Treat [Media Intelligence v2.2](MEDIA_INTELLIGENCE.md) as the released
  subsystem boundary. Check an item in [its manual checklist](MANUAL_TESTING.md#manual-v2-2)
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

- Treat [Content Intelligence v2.3](CONTENT_INTELLIGENCE.md) as the
  released subsystem boundary and keep
  [its manual checklist](MANUAL_TESTING.md#manual-v2-3) honest about fake-provider,
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


<a id="historical-v1-0-release-checklist"></a>
## v1.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/RELEASE_CHECKLIST_v1.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This checklist covers preparation of the local v1.0 release candidate. It does not authorize a merge, remote push, GitHub release publication, or installation on another user’s system.

<a id="historical-v1-0-release-checklist-repository"></a>
#### Repository

- [x] Current branch is `v1.0`.
- [x] The v1.0 branch preserves completed v0.9.1 history.
- [x] `main` has not been merged into or changed by this work.
- [x] Source and documentation release-preparation changes are captured in reviewed local commits.
- [x] No `bin`, `obj`, IDE cache, test result, secret, or local model file is tracked.
- [x] Nothing was pushed without explicit authorization.

<a id="historical-v1-0-release-checklist-product-behavior"></a>
#### Product behavior

- [x] Results controls remain fixed while result rows scroll.
- [x] Files table/details resizing is bounded, keyboard accessible, and persisted.
- [x] Duplicate details keep groups visible and expose no deletion command.
- [x] AI and Advanced controls remain independent and centrally enforced.
- [x] Enabling AI alone never invokes a provider.
- [x] OCR is local, page-aware, bounded, cancellable, and honestly reports unavailable states.
- [x] Tesseract version and configured English/German language data are validated before recognition.
- [x] AI extracted-text interpretation has a separate default-off gate and remains review-only.
- [x] Metadata readers are defensive and preserve provenance.
- [x] Generated tags remain distinguishable and require review where applicable.
- [x] Meaning Search Beta is local, bounded, cancellable, and explains matches.
- [x] Restructuring remains deterministic, preview-first, root-confined, conflict checked, and separately confirmed.
- [x] AI workflows remain suggestion-only and cannot enter file operations.
- [x] Rename/folder prompts are compact single-task contracts with exact schemas, application-owned extension preservation, opaque folder IDs, and fail-closed deterministic validation.
- [x] A folder suggestion with more than 12 selected files is visibly rejected as a whole before provider access; the exact count is shown and no file is silently omitted.
- [x] Structured-output repair is limited to one related attempt and excludes provider failure, timeout, cancellation, unsafe identity, path, hard-bound, and model-misuse failures.
- [x] Advanced Diagnostics has one master switch, category switches, one common memory store, and one non-modal viewer.
- [x] AI, OCR/text extraction, and scanning publish through the common UI-independent diagnostics framework.
- [x] Unsupported diagnostic categories are visibly marked not yet instrumented.

<a id="historical-v1-0-release-checklist-persistence-privacy-and-safety"></a>
#### Persistence, privacy, and safety

- [x] Existing v0.9.1 settings load with safe v1.0 defaults.
- [x] New stores are versioned, bounded, atomic, and recover safely from malformed data.
- [x] Existing catalogs, snapshots, tags, and saved searches remain readable.
- [x] Settings and stores contain no credentials or model data.
- [x] OCR, metadata extraction, tagging, and meaning indexing are local.
- [x] Raw OCR/extracted text and vectors are excluded from ordinary logs.
- [x] Rename/folder AI requests exclude file contents.
- [x] Extracted text can be sent only through its separate opt-in and explicit request.
- [x] Source files are opened read-only by scanners and extractors.
- [x] Apply operations reject overwrite, traversal, reparse, changed-preview, and missing-source conditions.
- [x] Duplicate actions do not delete or mutate files.
- [x] Advanced diagnostic content is redacted before retention by default, bounded in memory, session-only unless explicitly exported, and cleared on disable/exit.
- [x] Secrets and authorization values are removed even when unredacted diagnostics are explicitly enabled.
- [x] Detailed diagnostic paths, text, metadata, prompts, and responses never enter ordinary logs.

<a id="historical-v1-0-release-checklist-accessibility-and-usability"></a>
#### Accessibility and usability

- [ ] Keyboard focus order is manually verified.
- [ ] Drawer close and Escape behavior are manually verified.
- [ ] Toggle labels and help text are manually verified with accessibility tooling.
- [ ] Status is manually confirmed not to rely only on color.
- [ ] Long names and paths are manually checked at common window sizes.
- [ ] High-DPI behavior is manually checked at supported Windows scaling levels.
- [x] Large lists, page results, prompts, responses, and diagrams have deterministic bounds.
- [x] Empty, unavailable, cancelled, partial, and failed states have explicit presentation.

<a id="historical-v1-0-release-checklist-documentation"></a>
#### Documentation

- [x] Release proposals, specifications, decisions, migration, architecture, and version notes are current.
- [x] Public README contains official branding, quick links, features, privacy, installation, roadmap, contribution, and license guidance.
- [x] README screenshot slots point to `docs/images/` and contain no generated screenshots or placeholder images.
- [x] Windows install, update, uninstall, checksum, Ollama, and Tesseract guidance is documented.
- [x] OCR/Tesseract and AI limitations are stated accurately.
- [x] FOSS policy, dependency inventory, license, and third-party notices are current.
- [x] No documentation claims autonomous AI filesystem control.

<a id="historical-v1-0-release-checklist-windows-distribution"></a>
#### Windows distribution

- [x] Windows x64 publish is self-contained and untrimmed.
- [x] Public apphost is named `OpenSorSe.exe`.
- [x] File version is `1.0.0.0`; product version is `1.0.0`.
- [x] Official icon and product metadata are embedded.
- [x] README, license, notices, changelog, installation guide, release notes, dependency inventory, and documentation are included.
- [x] Portable ZIP and SHA-256 checksum are generated locally.
- [ ] Packaged GUI startup, visible window responsiveness, navigation, and layout are manually verified.
- [ ] Signed MSIX or conventional installer is available; deferred because identity/signing policy and installer toolchains are absent.

<a id="historical-v1-0-release-checklist-automated-validation"></a>
#### Automated validation

- [x] `dotnet restore .\OpenSorSe.sln`
- [x] Current-source Debug build succeeds with zero warnings/errors.
- [x] Current-source Debug tests pass.
- [x] Current-source Release build succeeds with zero warnings/errors.
- [x] Current-source Release tests pass.
- [x] Self-contained Windows x64 publish succeeds.
- [x] Package metadata, required files, icon, self-contained runtime, archive, and checksum are inspected.
- [x] Tests run against current source rather than stale binaries.
- [x] `git diff --check`
- [x] Static inspection covers XAML bindings, dependency registration, cancellation, serialization, navigation gates, and file-operation safety.
- [x] Prompt/schema snapshots and representative small-model response fixtures cover exact JSON, common shape errors, no-suggestion, identity, assignment, grounding, and path failures.
- [x] NuGet vulnerability audit reports no known vulnerable direct or transitive packages; test-only xUnit was updated to remove the flagged legacy dependency chain.
- [x] Formatting verification passes for every C# file changed in this release-preparation work; older unrelated repository formatting debt remains documented.

<a id="historical-v1-0-release-checklist-manual-release-hold"></a>
#### Manual release hold

- [ ] All checks in `docs/MANUAL_TESTING.md#manual-v0-9-1` are complete.
- [ ] All checks in `docs/MANUAL_TESTING.md#manual-v1-0` are complete.
- [ ] Scanning is exercised from the packaged executable.
- [ ] Live OCR is exercised on the intended release machine.
- [ ] Live Ollama/File Assistant behavior is exercised with an installed model.
- [ ] The 13-file folder-suggestion boundary is exercised and visibly rejects the whole request without provider access or a partial plan.
- [ ] The manual model matrix is complete for one approximately 2B, one approximately 4B, and one approximately 7B/8B model; exact IDs/builds and results are recorded before any compatibility claim.
- [ ] Unified Advanced Diagnostics is exercised for AI, OCR/text extraction, and scanning in redacted and explicitly unredacted modes, including filters, correlation, copy/export, failure detail, close-without-cancel, bounds, and clear-on-disable/exit.
- [ ] Meaning Search, Saved scans, folder plans, and settings persistence are exercised from the package.
- [ ] Supported non-Windows behavior is tested or its limitation remains documented.
- [ ] Manual testing is complete before considering merge into `main` or remote release publication.

</details>


<a id="historical-v2-0-stabilization"></a>
## v2.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V2.0_RC_STABILIZATION_PLAN.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Status:** retained follow-up/community validation plan; no item is marked complete

Automated implementation validation is not evidence that interactive or
real-world behavior has been exercised. This plan was drafted for a dedicated
pre-release stabilization phase. The maintainer subsequently selected a public
v2.0.0 release followed immediately by broad manual/community testing, so the
unchecked scenarios remain the structured follow-up campaign for v2.0.x and
later work. Results belong in separate evidence; this plan contains no
completed claims.

<a id="historical-v2-0-stabilization-entry-criteria"></a>
#### Entry criteria

- [ ] The v2.0 implementation commit and exact-tip hosted CI are successful.
- [ ] Debug/Release test totals are independently parsed with zero failures and
  zero skips and do not regress the valid 1,128-test baseline.
- [ ] All analyzer, policy, vulnerability, target-build, native-asset,
  documentation, repository-integrity, and artifact audits pass.
- [ ] Schema-3 compatibility and both sidecar schema-1 recovery paths are
  demonstrated using synthetic application data.
- [ ] No unresolved migration placeholder, swallowed background exception,
  stale running claim, generated database, or private test corpus is present.

<a id="historical-v2-0-stabilization-soak-and-failure-injection-campaign"></a>
#### Soak and failure-injection campaign

- [ ] Run sustained projection during indexing, Search, graph browsing, privacy
  inspection, and diagnostics while observing bounded memory, handles, queues,
  database size, and responsiveness.
- [ ] Exercise repeated pause/resume/cancel/retry/restart cycles and forced
  termination at manifest capture, claim, validation, publication, checkpoint,
  backup, forget, repair, and compaction boundaries.
- [ ] Exercise database busy/locked, low disk, read-only application data,
  corrupt graph rows, corrupt decision rows, unsupported newer schemas,
  unavailable backup, invalid privacy floor, and interrupted restore.
- [ ] Confirm stale epochs/tokens cannot publish, expired claims recover once,
  and late workers do not overwrite newer generations.
- [ ] Confirm graph failure never blocks ordinary Search, indexing, Collections,
  Change Plans, Operation Journal, recovery, or Undo.

<a id="historical-v2-0-stabilization-upgrade-and-rollback-matrix"></a>
#### Upgrade and rollback matrix

- [ ] Upgrade a copy of valid v1.9 schema-3 data with no sidecars.
- [ ] Restart repeatedly during first sidecar initialization and initial
  projection.
- [ ] Disable graph processing and verify v1.9 behavior remains available.
- [ ] Roll back to v1.9 with sidecars present and confirm schema 3 remains
  readable and authoritative.
- [ ] Reopen v2.0 after rollback and reconcile without losing user decisions.
- [ ] Restore verified decision recovery points at the privacy floor and above;
  reject stale, partial, tampered, and unsupported recovery data.

<a id="historical-v2-0-stabilization-privacy-and-authority-campaign"></a>
#### Privacy and authority campaign

- [ ] Race forget/exclude/include/clear operations with queries, Search,
  projection, repair, backup, and shutdown.
- [ ] Confirm no read or Search expansion exposes data while source, decision,
  or privacy applied watermarks trail authoritative state.
- [ ] Confirm source files remain byte-for-byte unchanged during every graph,
  privacy, repair, backup, restore, and clear action.
- [ ] Review normal and detailed diagnostics plus exports for content, queries,
  model data, secrets, tokens, and unnecessary absolute paths.
- [ ] Confirm manual entities, aliases, links, never-merge decisions, and
  exclusions survive rebuild and recovery where their retention policy says
  they should.

<a id="historical-v2-0-stabilization-platform-and-accessibility-campaign"></a>
#### Platform and accessibility campaign

- [ ] Complete the entire [v2.0 manual checklist](MANUAL_TESTING.md#manual-v2-0) on the
  claimed platforms and record host/dependency facts without inference.
- [ ] Validate keyboard-only navigation, focus order, screen-reader names,
  live-region behavior, zoom/scaling, high contrast, and touch/click controls.
- [ ] Validate filesystems with case-sensitive and case-insensitive identity,
  long/unusual valid paths, symlinks, removable/offline sources, and watched
  source ownership.
- [ ] Validate graph-disabled, Ollama-unavailable, OCR-unavailable, and
  dependency-restored operation.

<a id="historical-v2-0-stabilization-exit-criteria"></a>
#### Exit criteria

- [ ] All release-readiness and manual checklist evidence is reviewed.
- [ ] No unresolved P0/P1 defect or unclassified data-loss/privacy risk remains.
- [ ] Performance and capacity statements are limited to measured datasets and
  environments.
- [ ] Known limitations and deferred scope match the implementation.
- [ ] The candidate commit is unchanged after validation, or every change
  produces a new candidate and restarts affected gates.
- [ ] Maintainer explicitly approves merge/tag/package/publication as separate
  operations.

Publication does not mark these scenarios complete. Any finding is triaged as
real-world evidence and, where required, corrected through v2.0.x or a later
release with proportionate automated regression coverage.

</details>
