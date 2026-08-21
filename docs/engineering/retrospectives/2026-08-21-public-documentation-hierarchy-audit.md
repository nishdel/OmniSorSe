# Run retrospective — public documentation hierarchy audit

**Date:** `2026-08-21`

**Task category:** `Documentation-only`

**Risk domains:** `Documentation authority, release/readiness attribution`

**Implementation/review commit:** `uncommitted on ffc29ed`

**Retrospective author:** `Lead/Orchestrator after independent review`

## Outcome

- Intended outcome: provide a clear public documentation hierarchy.
- Actual outcome: verified the hierarchy was already implemented by PR #36 and
  corrected its stale validation destination and incomplete authority table.
- Completion status: `Complete`
- Important remaining uncertainty: GitHub rendering was not published from this
  uncommitted patch; Markdown structure and links were validated locally. The
  retained PR #36 evidence also records controlled Windows reruns after
  unrelated timing-sensitive failures.

## Assumptions and rediscovery

- Incorrect assumptions: the request's flat-catalog description still matched
  the current repository.
- Architecture/authority rediscovered: README is the compact landing router;
  `docs/README.md` owns the detailed index; Current State owns current
  implementation truth; Release Status owns validation/readiness evidence.
- Missing knowledge: none after Git history and retained PR #36 records were
  inspected.
- Stale or contradictory documentation: Current State and Release Status
  stopped at the pre-PR #36 validation boundary.
- Could another competent developer understand the result without this run's
  conversation? `Yes — the authority rules and intent routes are explicit.`

## Rework and context efficiency

- Unnecessary exploration: none material; history quickly identified the prior
  implementation.
- Duplicated exploration between specialists: minor independent inspection of
  the same two routers, useful for UX and adversarial separation.
- Context that should have been transferred earlier: PR #36's retained report
  gave the reviewer the exact validation evidence.
- Substantive implementation reversals/rework: zero.
- Corrective follow-up run required: `No`

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Reconciled repository history, authority, validation, and final scope. | Appropriate |
| Product/Strategy | No | Release/product claims were explicitly unchanged. | Not needed |
| Architecture | No | No product or system architecture changed. | Not needed |
| UX | Yes | Confirmed the first-visitor intent path is readable and progressive. | Appropriate |
| AX | No | No AI-assisted product experience changed. | Not needed |
| DX/Maintainability | Yes | Confirmed another router or file move would duplicate authority. | Appropriate |
| Performance | No | Documentation-only change. | Not needed |
| Security | No | No trust boundary or security claim changed. | Not needed |
| Resource Efficiency | No | No runtime resource-cost surface changed. | Not needed |
| Implementation | No | Lead made the small documentation-only correction. | Separate agent unnecessary |
| Documentation | Yes | Verified current/release/candidate/history classification. | Appropriate |
| Adversarial Validator | Yes | Found stale living evidence behind an otherwise correct route. | Essential |

- Specialist that should have been involved earlier: none.
- Finding duplicated by multiple specialists: the existing hierarchy was already
  sufficient; the duplication provided useful independent confirmation.
- Routing change to consider at the slow-loop review: none.

## Review and validation gaps

- What implementation missed: PR #36 synchronized navigation but did not update
  the living validation-summary paragraphs after its own final validation.
- What independent review found: the validation route landed on stale evidence,
  and Current State was missing from the current-authority table.
- What automated tests failed to catch: semantic freshness of validation
  evidence and Markdown fragment anchors.
- What remains manual or platform-specific: published GitHub rendering; all
  pre-existing v2.12 manual/release gaps remain unchanged.
- Documentation/diagram synchronization completed: Current State, Release
  Status, and the authority table synchronized; no diagram change warranted.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | The navigation route was correct, but its living validation target still stopped at `542e14a` after later green runs. | Documentation failure; review validated route structure without checking destination freshness. | When changing a public route, inspect the authority at its destination for semantic freshness, not only link validity. | Documentation-review checklist or low-noise semantic check if a stable oracle emerges. | Re-evaluate across later documentation-sync runs; do not hard-code moving SHAs in tests. | Candidate |
| C2 | Existing tests validate relative target paths but not fragment anchors; current anchors were manually verified. | Test limitation, with no observed broken anchor. | Validate fragments only if a low-noise repository-native implementation proves worthwhile. | Documentation-policy test. | Trial against historical/current Markdown before promotion. | Candidate |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1 | Pending independent slow-loop evaluator | `Defer` | One clear occurrence; proportional manual review fixed it, but no stable executable oracle exists yet. | Retain only in this retrospective pending recurrence/generalization. |
| C2 | Pending independent slow-loop evaluator | `Defer` | No escaped broken fragment was found. | Avoid brittle test expansion without failure evidence. |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Documentation, combined UX/DX, Adversarial Validator |
| Major knowledge sources loaded | README, docs router, architecture router, Current State, Release Status, Git history, PR #36 report/retro, documentation-policy tests |
| Duplicate exploration | `Minor` |
| Substantive rework count | 0 |
| Independent findings | 2 material corrections, 1 advisory candidate |
| Corrective follow-up needed | `No` |
| Durable knowledge produced | `Docs` |
| Code confidence | Not applicable; no product code changed |
| Automated-validation confidence | High for documentation policy and link structure |
| Interactive/manual confidence | Markdown reviewed; unpublished GitHub rendering not exercised |
| Platform/package confidence | Unchanged and outside scope |
| Release confidence | Unchanged; v2.12 remains unreleased and v2.4.0 remains latest release |

## Handoff to slow-loop review

- Candidates awaiting evaluation: C1 and C2.
- Active knowledge that may need compaction: none.
- Historical evidence worth retaining: PR #36 report and this audit.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  fragment-anchor validation only if later evidence justifies it.
