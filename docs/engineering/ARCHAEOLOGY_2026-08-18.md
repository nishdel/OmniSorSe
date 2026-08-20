# OmniSorSe engineering archaeology — 2026-08-18

**Knowledge level:** Level 3 — historical evidence

**Use:** Release archaeology, process review, and verification of why an active
engineering rule exists. Do not load this document for an ordinary bounded
change. Current architecture and workflow documents take precedence.

## Evidence policy

Repository source, tests, and Git objects were treated as primary evidence.
Release reports, specifications, and historical prompts were used to explain
intent or findings, but not to override implementation. A statement is marked
**derived** when it is a conclusion from several verified facts rather than a
fact recorded directly by one source.

No repository history was repaired, rewritten, merged, tagged, pushed, or
published during this audit.

## Verified baseline

The archaeology was performed in the recovered, healthy repository rather than
the damaged outer working copy.

| Item | Verified state at the start of the audit |
| --- | --- |
| Repository | `D:\Own Projects\OpenSorSe\OpenSorSe-recovered-clean` |
| Branch | `v2.12-trusted-relationships-context` |
| HEAD | `d48bb080f5125e536978e5527c77afbb4b57da7d` — `Document OmniSorSe v2.12 trusted relationships` |
| Working tree | Clean; no staged or untracked work |
| Upstream | The current local branch had no configured upstream |
| Remote | `origin` → `https://github.com/nishdel/OpenSorSe.git` |
| Published line | `main` and `origin/main` at `40552b9`, the v2.4 integration line |
| Tags | `v1.0.0`, `v2.0.0`, `v2.1.0`, `v2.2.0`, `v2.3.0`, `v2.4.0` |
| Current candidate line | Linear local v2.5–v2.12 candidate history above released v2.4 |
| Runtime authority | `Directory.Build.props` and `global.json`; current source targets .NET 10 |
| Git integrity | `git fsck --full --strict` completed successfully; only unreachable/dangling objects were reported |

The committed v2.0 recovery reports document an earlier storage failure. The
copied Git database contained a corrupt object, so it was not treated as an
authority. A clean clone from `origin` was validated, 64 tracked modifications
and 71 intended untracked implementation files were compared by path and
content, and eleven unrelated or generated changes were excluded. The damaged
copy was retained as evidence. See `docs/V2.0_IMPLEMENTATION_REPORT.md` and
`docs/V2.0_VALIDATION_REPORT.md`.

## Architectural eras

This grouping highlights changes in engineering shape rather than presenting a
release chronology.

| Era | Representative commits | What changed |
| --- | --- | --- |
| Read-only foundation | `b53f507`–`b9cb5ab` | Scanner, metadata, hashing, duplicates, rules/planning, initial MVVM and orchestration. |
| Catalog and historical exploration | `f8564e6`, `c885393` | Optional local suggestions, ranking, persistent catalog, saved queries, identity, and historical comparison. v0.4–v0.9 were delivered in one unusually large commit. |
| Corrective UX/provider work | `72ba5db`, `1adf89a` | Optional-AI controls followed by provider lifecycle, diagnostics, Help, Catalog Search, Settings, and Duplicate View correction. |
| Local understanding and reviewed mutation | v1.0 commits, `5236f13` | OCR/content/Semantic Search/structure history, then Change Plans, operation journal, verification, rollback, recovery, and Undo. |
| Continuous operation and extensibility | `944b03b`–`5e38d67` | Watched reconciliation, workflow profiles, recipes, and bounded plugins. |
| Platform and reliability | `325acb0`, `2c38472`, `63a407f` | Platform abstraction, atomic persistence, bounded lifecycle/performance, accessibility, and native-CI correction. |
| Durable intelligence | `bd67bde`, `d89a49a`, `7fbc9b4` | SQLite deep indexing, hybrid Search/privacy, relationships, corrections, and Smart Collections. These branches remained outside `main` until v2.0. |
| Knowledge Graph integration | `7212558` through `d353ce2` | Optional derived graph, graph-native decision sidecar, migration/recovery/fencing, and integration of v1.7–v1.9. |
| Released quality/content/protocol | `3854290`–`eda3752` | Search/AI quality, media, content intelligence, OmniSorSe identity, and read-only Explorer Protocol v1. |
| Unreleased consolidation | `59be07c`–`d48bb08` | Reconciliation, Smart Tags, scalable facets, guided workflows, organization, resilience, .NET 10, and trusted relationships. |

## Recurring engineering patterns

### Extend the existing authority

Later work repeatedly found that safe integration depended on preserving an
existing owner rather than adding another writable representation.

- v1.7 separated watched-folder-owned indexing sources from manual sources so
  watcher configuration could not remove or requeue unrelated sources.
- v2.0's `RelationshipGraphAuthorityBridge` sends projected relationship and
  collection commands back to the schema-3 relationship authority. It does not
  append a duplicate graph-native decision.
- v2.9 reused the persistent `SortingRecipe` and Change Plan authorities.
- v2.12 kept schema-6 relationships and Smart Collections authoritative while
  retaining the Knowledge Graph as an optional derived projection.

Relevant executable evidence includes
`BackgroundIndexingServiceTests.WatchedSourceSynchronizationRemovesOnlyDisabledManagedSources`
and `RelationshipGraphAuthorityBridgeTests`.

### A successful local path is not the complete workflow

Several corrections involved a consumer that was correct after ordinary
success but stale after another lifecycle path:

- v1.0 Files selection did not update File Assistant until a later query
  refresh.
- a queued `Progress<T>` callback could overwrite a verified terminal Change
  Plan state;
- v1.9 review found that full rebuild could leave stale automatic relationship
  data and that privacy-suppressed files could remain relationship/Search seeds;
- v2.5 added one reconciliation contract for success, failure, rollback,
  partial rollback, and Undo. Independent review in this audit found that the
  Desktop publishes only Review Changes terminal results to that contract;
  Operation History Undo and startup recovery bypass projection reconciliation;
- v2.7 corrected Search eligibility that silently excluded indexed files before
  ranking.

The general obligation is to inventory consumers and validate success,
failure, cancellation, rollback, partial failure, retry, restart, and Undo
where those states exist.

### Bounds must not silently redefine correctness

v2.7 records the clearest example. The former progressive projection loaded a
path-ordered prefix capped by `MaximumDocumentCount` before ranking. Above
10,000 files, an exact match outside the prefix could not reach the ranker.
The corrected design performs complete lightweight eligibility and relevance
selection in SQLite, then bounds expensive hydration.

Current regression tests include:

- `BackgroundIndexingServiceTests.ExactFilenameBeyondDefaultProjectionIsSearchable`;
- `BackgroundIndexingServiceTests.CandidateSelectionConsidersOneHundredThousandFilesWithoutHydratingTheLibrary`.

Wall-clock regex timeouts produced the same category of mistake twice: a 50 ms
relationship-identifier timeout in v2.0 and a 100 ms Search parser timeout
exposed during v2.11 could classify scheduler contention as invalid input even
though input length and regex complexity were already bounded.

### Optional features require first-class failure paths

No verified release made Ollama, OCR, media tools, OmniBrille, or the Knowledge
Graph mandatory to core deterministic Search or the file-mutation boundary.
That optionality is a consistent architectural strength.

Optional-provider UX was nevertheless a recurring source of rework.
`1adf89a` corrected competing client/request timeouts, doubled Ollama API
suffixes, silent model substitution, stale provider state, essential controls
hidden by Advanced mode, and inconsistent failure presentation. Optionality
therefore requires AX, UX, cancellation, and degraded-mode validation; a
deterministic fallback alone is insufficient evidence of a usable feature.

### Native execution is different evidence from cross-target compilation

- v1.6 hosted runs exposed timezone assumptions, Windows-only path fixtures,
  host-dependent parsing of persisted paths, macOS mutation-capability test
  assumptions, and a filename fixture invalid only on Windows. Fixes landed in
  `63a407f` and `325b3aa`.
- v2.0 hosted runs exposed Unix SQLite source-stamp locking and a macOS writer
  contention budget defect.
- `0a62418` made a schema migration fixture portable.
- `eda3752` shortened the Explorer Protocol endpoint after the original name
  exceeded macOS's Unix-domain-socket path limit.

Cross-RID builds prove compilation and target asset selection. They do not
prove native filesystem, IPC, lifecycle, accessibility, or optional-tool
behavior.

### Large commits increased corrective and archaeology cost

Examples include:

- `c885393`: 663 files changed and 59,497 insertions;
- `7212558`: 135 files changed and 38,923 insertions;
- v2.2–v2.10 feature commits: 33–147 files each.

The v2.0 candidate was followed by separate source-replacement, fixture,
identifier-scanning, package-smoke, SQLite-ownership, contention-budget, CI,
and terminal-progress corrections. **Derived conclusion:** the review process
was valuable, but discovery, implementation, review, correction,
documentation, and validation were too often compressed into one broad unit.
Phased decisions and independent review should make future evidence easier to
isolate without forcing artificial commit boundaries.

## Classified lessons

| Lesson | Failure class | Evidence | Strongest durable form |
| --- | --- | --- | --- |
| Never commit a validation clone or parallel repository authority. | Process/agent-routing/review | `c885393` tracked 510 files under `.audit-validation`; `72ba5db` removed them and ignored `.audit-validation/` and `.artifacts/`. | Existing ignore rules plus a tracked-forbidden-root policy test. |
| Passing unit tests do not validate ordinary provider setup or GUI comprehension. | Test/review/UX | v0.1 manual findings in `docs/Implementation_Spec/v0.2/00_v0.2_Release_Proposal.md`; v0.9.1 correction in `1adf89a`. | Risk-routed UX/AX review, focused tests, and bounded manual validation. |
| A local host and cross-target build are not native-platform evidence. | Test/review | `63a407f`, `325b3aa`, `0a62418`, `eda3752`. | Native CI and portable fixture tests. |
| Corrupt Git metadata is not an authority to repair in place. | Operational/process | v2.0 recovery reports. | Repository-audit/recovery procedure and integrity gate. |
| One operation needs one contention/cancellation budget and explicit resource ownership. | Architecture/performance | `d88605c`, `52f07b2`; v2.0 validation report. | Store lifecycle invariants and contention/fault-injection tests. |
| Verified terminal state must dominate late progress; mutation outcomes need symmetric reconciliation. | Implementation/test/architecture | weak guard in `944b03b`; serialized fence in `d353ce2`; v2.5 reconciliation in `59be07c`. | Deterministic late-callback regression plus the existing reconciliation matrix. |
| Bound expensive hydration after complete lightweight eligibility. | Architecture/performance/test | v2.7 design and 10k/100k regressions. | Provider contract, architecture documentation, and scale tests. |
| Every user-authored authority needs explicit backup/restore and legacy-absence semantics. | Architecture/persistence/docs | v2.10 `.oms-state` format 1; v2.12 format 2 and `StateBackupServiceTests`. | Versioned compatibility contract and round-trip/failure tests. |

## Representative development-process regression scenarios

These scenarios are inputs to periodic evaluation of the engineering workflow.
They test general detection mechanisms, not memorization of the historical fix.

### H1 — A validation workspace becomes a second repository

- **What went wrong:** `c885393` committed a 510-file `.audit-validation`
  repository copy. `72ba5db` later removed 51,134 lines and added ignore rules.
- **Signal available then:** top-level Git status, diff statistics, a duplicate
  solution, and repeated source/document paths.
- **Expected detection:** Lead baseline plus DX/adversarial tracked-artifact
  review.
- **Automation:** reject known build/test/validation roots if tracked.
- **Knowledge:** active executable rule; file count and commit remain history.

### H2 — An optional AI feature is safe but operationally misleading

- **What went wrong:** competing timeouts, invalid composed endpoints, silent
  model substitution, stale state, and hidden essential setup controls.
- **Signal available then:** provider lifecycle, settings visibility, exact
  model contract, and manual setup flow.
- **Expected detection:** AX and UX before implementation closure; adversarial
  provider-state review afterward.
- **Automation:** endpoint, timeout, exact-model, stale-context, composition,
  and fallback tests. Visual workflow remains manual.
- **Knowledge:** active provider/AX validation matrix; details remain history.

### H3 — “Cross-platform” validation encodes the development host

- **What went wrong:** Windows path syntax, invalid-name rules, timezone values,
  and capability assumptions passed locally but failed on native hosts.
- **Signal available then:** host-sensitive APIs and fixtures, plus the declared
  supported-host matrix.
- **Expected detection:** Platform/Architecture and native adversarial
  validation.
- **Automation:** native Windows/Linux/macOS jobs and host-independent fixtures.
- **Knowledge:** active platform evidence policy.

### H4 — Git storage becomes corrupt during a major change

- **What went wrong:** the copied object database failed integrity validation.
- **Signal available then:** failed object reads and `git fsck`.
- **Expected detection:** Lead stops implementation, inventories work, treats a
  clean remote clone as authority, and requests independent recovery review.
- **Automation:** baseline/final `git fsck`; content selection still requires
  human judgment.
- **Knowledge:** active recovery procedure; object-level incident is history.

### H5 — SQLite work passes locally but violates native contention bounds

- **What went wrong:** raw database reads conflicted with SQLite on Unix;
  repeated WAL setup and separate busy waits exceeded one intended deadline;
  process-wide pools outlived store ownership.
- **Signal available then:** connection lifecycle, global pool operations,
  multiple timeout sites, and native CI failures.
- **Expected detection:** Architecture and Performance review followed by
  adversarial locked-store/native validation.
- **Automation:** finite locked-writer deadlines, prompt cancellation,
  deterministic file release, and source-replacement tests.
- **Knowledge:** active storage lifecycle invariant.

### H6 — Late progress or partial mutation leaves stale presentation/index state

- **What went wrong:** `Progress<T>` could run after terminal publication. The
  first `LastExecution is not null` check was racy. Until v2.5, downstream
  convergence was not one explicit contract for rollback, partial rollback,
  failure, and Undo. This audit then found a separate integration gap: Operation
  History Undo and startup recovery do not send their results to that contract.
- **Signal available then:** async callback semantics, journal status matrix,
  current filesystem state, and the consumer list.
- **Expected detection:** Architecture, UX, and adversarial state-transition
  review.
- **Automation:** `ChangePlanReconciliationServiceTests` cover the reconciliation
  algorithm for success, rollback, partial rollback, missing result, and Undo,
  but not every Desktop caller. This run added a deliberately queued
  post-terminal progress regression. Operation History/startup integration
  tests remain a future product-fix requirement.
- **Knowledge:** terminal-progress test promoted; missing-consumer wiring remains
  a recorded candidate/product defect; commit sequence remains history.

### H7 — A performance cap removes the correct Search result

- **What went wrong:** a path-ordered 10,000-document cap ran before relevance,
  so an exact filename outside the prefix was absent.
- **Signal available then:** placement of `MaximumDocumentCount`, path ordering,
  and the large-library requirement.
- **Expected detection:** Search Architecture plus Performance and adversarial
  correctness review.
- **Automation:** exact-match-beyond-cap and 100,000-file
  complete-eligibility/bounded-hydration regressions.
- **Knowledge:** active Search contract and executable tests.

### H8 — Backup coverage lags expanding user authority

- **What went wrong:** `.oms-state` format 1 did not preserve later authored
  Smart Collection authority; older payload absence also needed explicit
  non-clearing semantics.
- **Signal available then:** authority tables, payload categories, restore mode,
  and compensation behavior.
- **Expected detection:** Persistence/Architecture and Documentation during
  planning, then adversarial compatibility review.
- **Automation:** exact format-1 compatibility without clearing newer
  categories, format-2 authority round-trip, and generated-edge exclusion.
- **Knowledge:** active versioned state contract; format evolution is history.

## Documentation and validation evidence gaps

### Verified

- `docs/V2.0_IMPLEMENTATION_REPORT.md` still describes an unmerged candidate
  and 1,468 tests, while `docs/V2.0_VALIDATION_REPORT.md`, release status, and
  history record integration and 1,486 final tests. It is useful as a historical
  snapshot but can be mistaken for current truth.
- v1.7–v1.9 validation reports defer immutable hosted-run URLs and final
  synchronization to a post-commit handoff. `docs/RELEASE_STATUS.md` explicitly
  says that evidence is not self-recorded.
- The v2.5–v2.7 manual checklists contain partial recorded evidence but are not
  complete. The v2.8–v2.12 manual checklists remain fully unchecked.
- Historical reports usually distinguish local build, cross-target compile,
  native CI, manual validation, packaging, signing, and publication. Broad
  report inflation was not the dominant problem; durable evidence placement
  was.

### Derived conclusions

- Historical implementation reports need explicit snapshot/superseded status
  so they cannot compete with current-state documentation.
- Exact-tip hosted evidence should be committed in a later evidence update when
  it cannot exist inside the implementation commit.
- The most valuable future specialist routing is concentrated around
  persistence/authority, native-platform contracts, mutation reconciliation,
  optional-provider AX, and large-library Search.

### Remaining uncertainties

- Exact hosted-run URLs for v1.7–v1.9 may have existed only in handoffs or
  conversations unavailable to the repository audit.
- Some defects were discovered and corrected inside the same broad commit, so
  the original failing source snapshot cannot always be isolated.
- Automated evidence does not establish interactive accessibility, arbitrary
  local-model quality, OmniBrille behavior, removable-source identity, or
  native optional-provider behavior for the unreleased candidate chain.

## Evidence index

Primary historical documents:

- `RELEASE_HISTORY.md`
- `docs/RELEASE_STATUS.md`
- `docs/Implementation_Spec/v0.9/AUDIT_CORRECTIONS.md`
- `docs/Implementation_Spec/v0.9.1/047_Correction_Reliability_and_Usability_Pass.md`
- `docs/V1.6_VALIDATION_REPORT.md`
- `docs/V1.7_IMPLEMENTATION_REPORT.md`
- `docs/V1.9_IMPLEMENTATION_REPORT.md`
- `docs/V2.0_IMPLEMENTATION_REPORT.md`
- `docs/V2.0_VALIDATION_REPORT.md`
- `docs/WORKFLOW_AND_INDEXING_QUALITY_v2.5.md`
- `docs/SCALABLE_FACETED_DISCOVERY_v2.7.md`
- `docs/PRODUCTION_HARDENING_v2.10.md`
- `docs/SUPPORTED_RUNTIME_PLATFORM_READINESS_v2.11.md`
- `docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md`

High-value executable evidence:

- `tests/OpenSorSe.Application.Tests/ChangePlanReconciliationServiceTests.cs`
- `tests/OpenSorSe.Application.Tests/KnowledgeGraph/GraphProjectionCoordinatorTests.cs`
- `tests/OpenSorSe.Application.Tests/KnowledgeGraph/RelationshipGraphAuthorityBridgeTests.cs`
- `tests/OpenSorSe.Application.Tests/SearchIntelligenceTests.cs`
- `tests/OpenSorSe.Core.Tests/PlatformFoundationTests.cs`
- `tests/OpenSorSe.Core.Tests/RepositoryDocumentationTests.cs`
- `tests/OpenSorSe.Indexing.Sqlite.Tests/BackgroundIndexingServiceTests.cs`
- `tests/OpenSorSe.Indexing.Sqlite.Tests/KnowledgeGraph/SqliteGraphReaderConcurrencyTests.cs`
- `tests/OpenSorSe.Indexing.Sqlite.Tests/SqliteGraphProjectionSourceTests.cs`
- `tests/OpenSorSe.Indexing.Sqlite.Tests/StateBackupServiceTests.cs`
