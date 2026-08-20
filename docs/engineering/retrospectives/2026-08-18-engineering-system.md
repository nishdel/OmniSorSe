# Run retrospective — engineering and Codex development system

**Date:** 2026-08-18

**Task category:** Architecture / documentation / engineering infrastructure

**Risk domains:** Repository recovery, architecture authority, documentation,
agent routing, learning, CI, validation

**Implementation/review commit:** Uncommitted working tree based on `d48bb08`

**Retrospective author:** Lead/Orchestrator; independently reviewed by the
Adversarial Reviewer

## Outcome

- Intended outcome: reconstruct current OmniSorSe and convert its development
  evidence into a durable, scoped, testable human/Codex engineering system.
- Actual outcome: current truth and authority maps, reconstructed ADRs,
  specialist routing, one substantial-run Skill, controlled learning, owner
  reporting, documentation/CI checks, and two executable historical lessons.
- Completion status: **Complete with follow-up**.
- Important uncertainty: .NET tests could not execute without SDK `10.0.400`.
  Operation History Undo and startup interruption recovery also have a verified
  pre-existing projection-reconciliation gap that was documented, not fixed.

## Assumptions and rediscovery

- Incorrect assumptions: the initial workspace root was not the current
  repository; the outer checkout had corrupt Git metadata and extensive old
  work, while `OpenSorSe-recovered-clean` was the healthy v2.12 authority.
- Architecture rediscovered: filesystem, Results, catalog, deep index, authored
  relationship state, derived graph, graph decisions, Change Plans/journal,
  AI, and Explorer projections have deliberately different owners.
- Missing/stale knowledge: active entry documents disagreed on schemas 3–6,
  v2.4/v2.12 status, repository structure, terminology, and subsystem routing.
- Independent correction: the initial archaeology/documentation inferred that
  every Undo/recovery result reached post-operation reconciliation. Source
  review showed only Review Changes publishes that completion event.
- Human comprehensibility: **Yes for the documented boundaries after this run**;
  several large ViewModels/provider classes remain incremental code-structure
  debt, and the projection gap is explicit rather than hidden.

## Rework and context efficiency

- Unnecessary exploration: the initial outer-repository baseline had to be
  replaced after locating the nested authoritative recovery checkout.
- Duplicated exploration: minor overlap between History, Architecture, and
  Documentation specialists before the corrected root and baseline were
  transferred; later work used assigned files and distilled evidence.
- Context that should have been transferred earlier: authoritative repository
  path, released-vs-candidate line, SDK gap, and active documentation hierarchy.
- Substantive rework: two reviewer-driven correction cycles—first for
  architecture/learning truth, then for Mermaid-metadata and exact agent-sandbox
  guards.
- Corrective follow-up run required: **Yes** for the product wiring/regressions
  if the owner authorizes it; **Yes** for automated validation on an SDK 10 host.

## Specialist effectiveness

| Specialist | Used? | Useful result | Assessment |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Baseline correction, integration, validation, synthesis | Necessary |
| Product/Strategy | No | Product behavior was intentionally out of scope | Correctly omitted |
| Architecture | Yes | Verified ten subsystem authorities and reconstructed ADRs | High value |
| UX | No dedicated agent | Mutation presentation considered through architecture/review | Proportionate omission |
| AX | No dedicated agent | Historical provider evidence informed the role/routing | Proportionate omission |
| DX/Maintainability | Covered by lead/history | Recovery, CI, artifact and comprehensibility findings | A dedicated pass was unnecessary |
| Performance | No dedicated agent | Historical bounds informed routing; no performance code changed | Correctly omitted |
| Implementation | Yes, focused | Deterministic queued-progress regression only | High value, bounded |
| Documentation | Yes | Current State, five diagrams, glossary, entry-point drift correction | High value and involved during implementation |
| Adversarial Reviewer | Yes, independent | Found missing mutation consumers and eight process/doc/check issues | Essential; should remain late and independent |

## Review and validation gaps

- Implementation initially missed: `.audit-validation` exclusion in recursive
  checks, Mermaid fences carrying metadata, and stronger one-Skill/sandbox
  assertions.
- Independent review found: the projection-reconciliation defect, historical
  labels, readiness authority conflict, excessive unconditional startup context,
  learning status drift, validation attribution, and terminology drift.
- Automated tests failed to catch: all current corrections are unexecuted on
  this host because SDK resolution failed before test discovery.
- Still manual/platform-specific: Mermaid rendering, interactive UX,
  accessibility, native platform/package behavior, and release readiness.
- Documentation synchronization: completed and rechecked statically.

## Candidate lessons and independent evaluation

| ID | Observation and evidence | Status / durable form |
| --- | --- | --- |
| C1 | A safe executor/service matrix does not prove every UI/startup caller forwards terminal facts to reconciliation. Verified in `UndoHistoryViewModel`, startup recovery, and `MainViewModel`. | **Candidate product fix.** Existing consumer-tracing rule is sufficient active guidance; future fix needs end-to-end regressions. |
| C2 | A validation clone can become tracked parallel authority (`c885393`/`72ba5db`). | **Accepted/promoted:** `.gitignore` plus `GeneratedValidationRoots_AreIgnoredAndUntracked`. |
| C3 | Queued progress can overwrite terminal state (`d353ce2`). | **Accepted/promoted:** `QueuedProgressAfterVerifiedExecutionDoesNotOverwriteTerminalPresentation`. |
| C4 | Historical reports can compete with current truth. | **Accepted/promoted:** authority hierarchy and explicit historical/superseded router labels. |
| C5 | Hosted evidence can remain only in an external handoff. | **Deferred candidate:** evaluate during release-process review. |

The independent reviewer, not the originating History specialist, evaluated
C2–C4. C1 remains a candidate because the product correction was outside scope.

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Architecture, Documentation, History/Learning, focused Implementation, Adversarial Reviewer |
| Major knowledge sources | Source/tests, Git history, Current State/Architecture, release and validation reports |
| Duplicate exploration | Minor, concentrated before repository-root correction |
| Substantive rework count | 2 independent-review correction cycles |
| Independent findings | 1 high and multiple medium/low findings; all engineering-system findings corrected or explicitly deferred |
| Corrective follow-up needed | Yes: SDK 10 validation and separately authorized reconciliation fix |
| Durable knowledge produced | Tests, ADRs, current docs/diagrams, agent definitions, Skill, routing/checks |
| Code confidence | No production code changed; test/config diff independently inspected |
| Automated-validation confidence | Static checks passed; .NET build/tests blocked before execution |
| Interactive/manual confidence | Not evaluated; no product workflow intentionally changed |
| Platform/package confidence | Not evaluated |
| Release confidence | Unchanged; consult Release Status |

## Handoff to slow-loop review

- Revisit the mutation-consumer candidate after a separately authorized fix.
- Confirm the substantial-run Skill and specialist roles were actually reused
  before adding more roles or Skills.
- Retire duplicate prose when executable checks prove sufficient.
- Reassess the large active architecture documents and system-map density if
  future tasks still load more context than they use.
