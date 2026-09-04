# Run retrospective — Product Clarity & Workflow issue pass

**Date:** 2026-09-04

**Task category:** Cross-cutting product workflow, safety, validation, release

**Risk domains:** UX/accessibility, durable relationship authority, async AI,
Change Plan provenance, indexing metrics, documentation/release identity

**Implementation/review commit:** Uncommitted at retrospective authoring; see
the project-owner handoff and branch history.

**Retrospective author:** Lead/orchestrator

## Outcome

- Intended outcome: turn the open issue set into a coherent, safer primary
  workflow and qualify a truthful release candidate.
- Actual outcome: implemented the bounded clarity/safety fixes, retained larger
  feature requests, advanced source to 2.13.0-rc, and passed the full local
  automated matrix.
- Completion status: `Complete with follow-up`
- Important remaining uncertainty: interactive/manual quality and exact-main
  hosted/native-package evidence.

## Assumptions and rediscovery

- Incorrect assumptions: low process CPU did not prove an indexing defect;
  default concurrency is one. Smart Tags and saved searches were present but
  obscured by source level, evidence, selection, and naming.
- Architecture/authority rediscovered: relationship repair deletes retained
  authority; duplicate suggestion origin is consumed after Apply; AI proposal
  decisions need one terminal concurrency boundary.
- Missing knowledge: real-library stage timings and current interactive layout,
  focus, screen-reader, SmartScreen, and Gatekeeper behavior.
- Stale or contradictory documentation: source/package identity had to be
  separated from the already published v2.12 prerelease; visible Saved View
  wording was inconsistent.
- Could another competent developer understand the result without this run's
  conversation? `Yes` — current-state/release/manual docs, regression tests,
  owner report, and code preserve the boundaries and remaining gates.

## Rework and context efficiency

- Unnecessary exploration: some broad historical Saved View matches were
  expected compatibility/history rather than active UI defects.
- Duplicated exploration between specialists: minor overlap on Search layout,
  indexing rate, and release wording improved confidence.
- Context that should have been transferred earlier: the immutable v2.12
  publication boundary and downstream use of `SuggestionSource`.
- Substantive implementation reversals/rework: four — repair confirmation,
  single-terminal AI decisions, immutable duplicate origin, and typed deep-link
  help/settings routing.
- Corrective follow-up run required: `No` for local code; hosted/package/manual
  release gates remain part of the current run.

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Integrated issue, authority, version, validation, and release decisions. | Appropriate. |
| Product/Strategy | Via lead/UX | Kept workflow coherent and larger features out of a risky clarity patch. | Appropriate. |
| Architecture | Yes | Found repair authority and duplicate-provenance blockers. | Should be repeated after implementation, as done. |
| UX | Yes | Distinguished missing behavior from discoverability/naming and unsafe literal issue wording. | Appropriate. |
| AX | Via UX/manual matrix | Identified focus, live status, layout, and DPI gates. | Rendered review remains manual. |
| DX/Maintainability | Via lead | Preserved internal compatibility identifiers and typed navigation/focus seams. | Appropriate. |
| Performance | Yes | Diagnosed rate semantics before concurrency tuning. | Appropriate. |
| Implementation | Yes | Added bounded fixes and regressions without authority expansion. | Appropriate. |
| Documentation | Yes | Separated current source, published RC, stable release, and manual evidence. | Appropriate. |
| Adversarial Validator | Yes | Found four concrete blockers and required regression seams. | Essential after first diff. |

- Specialist that should have been involved earlier: architecture awareness of
  `SuggestionSource` consumers would have avoided one rework cycle.
- Finding duplicated by multiple specialists: indexing rate/ETA and Search
  layout; duplication was minor and corroborative.
- Routing change to consider at the slow-loop review: provide downstream
  provenance consumers and release identity to implementers before UI changes.

## Review and validation gaps

- What implementation missed: repair deletion authority, AI decision races,
  duplicate-origin consumers, and exact help/settings routing.
- What independent review found: all four above plus one whitespace failure.
- What automated tests failed to catch: the original suite had no concurrent
  Keep/Dismiss execution and treated duplicate cleanup selection as shell-open
  selection.
- What remains manual or platform-specific: visual hierarchy, resizing/DPI,
  keyboard/screen reader, real data/providers, Windows installer UI/upgrade,
  macOS Gatekeeper, and full native packages.
- Documentation/diagram synchronization completed: living/release docs are
  synchronized; diagrams were unchanged because authority topology is stable.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Repair changed non-rebuildable retained authority without confirmation. | Safety boundary classified only by source-file mutation. | Classify destructive actions by all user authority they erase. | Architecture/policy regression test. | Audit every destructive ViewModel command. | Candidate |
| C2 | Separate AI Keep/Dismiss commands could both complete. | Async commands lacked shared captured-state terminal gate. | Competing terminal decisions share one gate and immutable input snapshot. | Concurrency regression. | Apply to future proposal workflows. | Candidate |
| C3 | Editing a duplicate action replaced its origin. | Provenance conflated origin and user modification. | Preserve immutable origin and record later transformations separately. | Regression over reconciliation/Undo consumers. | Audit all source-enum rewrites. | Candidate |
| C4 | Post-publication changes initially appeared adjacent to v2.12. | Source version and immutable package identity were conflated. | New user-visible source after a published tag gets a new version line. | Release-policy check/documentation. | Re-evaluate before next RC. | Candidate |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1–C4 | Pending slow-loop evaluator | `Defer` | This run supplies strong local evidence, but the implementation author cannot self-promote permanent rules. | Regressions, report, and retrospective retained for later evaluation. |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | 5 specialist passes plus lead/orchestrator |
| Major knowledge sources loaded | Current State, docs router, authority map, ADRs, risk matrix, issue source/tests, release workflows |
| Duplicate exploration | `Minor` |
| Substantive rework count | 4 |
| Independent findings | 4 blockers plus 4 clarity inconsistencies |
| Corrective follow-up needed | `No` locally; hosted/package/manual gates remain |
| Durable knowledge produced | `Tests / Docs / Report / Retrospective` |
| Code confidence | High for bounded changed seams |
| Automated-validation confidence | High locally; hosted pending |
| Interactive/manual confidence | Low until checklist execution |
| Platform/package confidence | Windows native smoke only; complete package workflow pending |
| Release confidence | Candidate-quality, not GA |

## Handoff to slow-loop review

- Candidates awaiting evaluation: C1–C4.
- Active knowledge that may need compaction: relationship authority confirmation
  and source/edit provenance separation.
- Historical evidence worth retaining: adversarial blocker list and exact local
  1,877-test qualification.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  whether the risk matrix should explicitly name deletion of retained user
  authority and competing terminal-decision concurrency.
