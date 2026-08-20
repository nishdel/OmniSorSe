# Run retrospective — current-source publication

**Date:** 2026-08-20

**Task category:** Git integration and source publication

**Risk domains:** Git history, remote refs, release/status documentation,
fresh-clone reproducibility

**Implementation/review commit:** published source record `cc6c331`, containing
history-preserving merge `be536a0`

**Retrospective author:** Documentation specialist, for orchestrator review

## Outcome

- Intended outcome: make the latest trustworthy source cloneable from GitHub
  `main` without creating a release or rewriting history.
- Actual outcome: the candidate line and engineering system are published from
  GitHub `main`; exact local/remote equality and a clean-clone restore,
  zero-warning Release build, and 1,861/1,861 Release tests were observed.
- Completion status: `Complete with follow-up`
- Important remaining uncertainty: the root cause of the same three exact-main
  hosted Debug test failures on both macOS ARM and Intel; Windows was still
  running at the observation point.

## Assumptions and rediscovery

- Incorrect assumptions: GitHub did not omit existing remote candidate
  branches; those refs had never been pushed.
- Architecture/authority rediscovered: Release Status owns source integration,
  publication, and release-readiness evidence; versioned reports remain frozen.
- Missing knowledge: the active publication gate did not explicitly require
  local/remote ref equality or a fresh-clone check.
- Stale or contradictory documentation: living documents stopped at v2.4 or
  described v2.5-v2.12 as unmerged even after local integration.
- Could another competent developer understand the result without this run's
  conversation? `Yes`; final remote, clone, validation, and release-boundary
  evidence is recorded in Release Status and the owner report.

## Rework and context efficiency

- Unnecessary exploration: none material; the current-state and release-status
  authorities bounded the documentation review.
- Duplicated exploration between specialists: none material.
- Context that should have been transferred earlier: the intended distinction
  between source publication and product release was the key handoff.
- Substantive implementation reversals/rework: none.
- Corrective follow-up run required: `Yes`, as a separate hosted macOS
  regression diagnosis/fix; source publication itself is complete.

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Protected history and separated integration from publication | Appropriate |
| Product/Strategy | No | Scope was explicit: source publication, not release | Unnecessary |
| Architecture | No separate agent | No architecture or authority change | Unnecessary |
| UX | No | No user workflow change | Unnecessary |
| AX | No | No AI/provider change | Unnecessary |
| DX/Maintainability | Yes | Validation and clone reproducibility matter to another developer | Appropriate |
| Performance | No | No runtime-performance change | Unnecessary |
| Implementation | Yes | Created the engineering commit and history-preserving merge | Appropriate |
| Documentation | Yes | Found and corrected living source/release contradictions | Appropriate |
| Adversarial Validator | Yes | Challenged remote/clone claims, identified hosted macOS failures, and independently accepted C1 | Appropriate |

- Specialist that should have been involved earlier: none found.
- Finding duplicated by multiple specialists: none material.
- Routing change to consider at the slow-loop review: none beyond the candidate
  publication gate below.

## Review and validation gaps

- What implementation missed: local validation did not by itself publish any
  Git ref.
- What independent review found: C1 was accepted as a scoped, low-noise
  publication rule. Exact hosted execution independently exposed the same three
  Debug test failures on macOS ARM and Intel.
- What automated tests failed to catch: remote branch/ref absence is outside
  ordinary repository tests; local Windows and clean-clone Release validation
  did not expose the hosted macOS failures.
- What remains manual or platform-specific: macOS regression diagnosis,
  completion of the observed Windows jobs, and optional native/manual/package/
  release gates.
- Documentation/diagram synchronization completed: living status documents
  updated; no diagram was affected.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | `origin/main` remained at v2.4 while validated v2.5-v2.12 work existed only locally | Process/validation failure: local correctness was conflated with remote availability | For Git source publication, verify intended local SHA equals the remote ref and, where practical, clone the remote default branch | Low-noise validation rule in the risk matrix | Compare local SHA, `ls-remote`, remote default HEAD, and disposable-clone HEAD after push | Promoted after independent review |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1 | Independent adversarial reviewer | `Accept` | The rule directly detects the observed local/remote gap, applies only to Git publication, makes exact SHA comparison cheap, and keeps the clone conditional on practicality | `docs/engineering/RISK_VALIDATION_MATRIX.md` publication gate |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Lead, DX/validation, Documentation, adversarial reviewer |
| Major knowledge sources loaded | AGENTS, substantial-run Skill, Current State, Release Status, Release History, roadmap, risk matrix, Git refs/history |
| Duplicate exploration | None |
| Substantive rework count | 0 |
| Independent findings | The same three hosted Debug tests failed on macOS ARM and Intel; C1 accepted and promoted |
| Corrective follow-up needed | Yes, separate hosted macOS regression task |
| Durable knowledge produced | Status docs; candidate validation rule; report/retrospective |
| Code confidence | No product-code change |
| Automated-validation confidence | High local/exact-clone confidence; hosted cross-platform confidence is mixed because both macOS Debug jobs are red |
| Interactive/manual confidence | Not exercised |
| Platform/package confidence | Local Windows publish/smoke only |
| Release confidence | Source publication complete; product release not attempted and not ready |

## Handoff to slow-loop review

- Candidates awaiting evaluation: none from this run.
- Active knowledge that may need compaction: none.
- Historical evidence worth retaining: before/after main SHAs, original absence
  and final equality of remote refs, clean-clone result, and hosted macOS failure
  run.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  retain the publication rule only if it remains low-noise and useful.
