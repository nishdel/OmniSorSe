# Run retrospective — operation reconciliation and documentation hierarchy

**Date:** 2026-08-20

**Task category:** Critical filesystem-mutation reconciliation plus current
documentation navigation

**Risk domains:** Undo/recovery, filesystem/projection authority, startup
bounds, current documentation hierarchy

**Implementation/review commit:** uncommitted at `0fdcf54`

**Retrospective author:** Lead/Orchestrator after independent specialist review

## Outcome

- Intended outcome: close Operation History/startup reconciliation gaps and
  make the public documentation path understandable without changing product
  policy, schema, releases, or unrelated behavior.
- Actual outcome: all in-scope paths now use shared reconciliation; startup
  sequencing and index inputs are bounded; navigation is intent-based; local
  validation is green.
- Completion status: `Partial` pending authorization for hosted CI/integration.
- Important remaining uncertainty: hosted cross-platform and interactive GUI
  evidence has not yet run for this exact tree.

## Assumptions and rediscovery

- Incorrect assumptions: existing tests equated Results logical IDs with
  filesystem identities; one batched API call was initially mistaken for a
  bounded input; initial documentation work edited an accepted ADR instead of
  leaving the historical decision immutable.
- Architecture/authority rediscovered: journal retention exceeds plan
  retention; `AffectedRootFolder` covers validator-constrained source and
  destination paths; History is durable before projection refresh.
- Missing knowledge: the earlier audit followed the two documented bypasses
  but did not enumerate the active Folder Restructuring executor consumer.
- Stale or contradictory documentation: the public routers were visually flat,
  the architecture index listed only ADR-001 through ADR-003, and current docs
  described the now-fixed reconciliation gap.
- Could another competent developer understand the result without this run's
  conversation? `Yes`; focused tests, Current State, the authority map,
  subsystem narrative, glossary, and corrected Mermaid flow preserve it.

## Rework and context efficiency

- Unnecessary exploration: none material; durable Current State/risk/authority
  docs narrowed the search quickly.
- Duplicated exploration between specialists: minor, limited to independent
  verification of the same mutation consumers and documentation claims.
- Context that should have been transferred earlier: Results ID versus physical
  identity and the full list of direct executor callers.
- Substantive implementation reversals/rework: two—removed identity-keyed row
  selection; replaced unbounded action-path union with operation-root
  coalescing. Documentation review also restored immutable ADR-004 and corrected
  the Mermaid branch.
- Corrective follow-up run required: `Yes` only for hosted CI/integration after
  explicit Git authorization; not for another local implementation pass.

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Scope, implementation, validation, synthesis | Appropriate |
| Product/Strategy | No | Product/release intent was explicit and unchanged | Unnecessary |
| Architecture | Yes | Journal authority/retention, path matching, consumers, recovery ordering | Appropriate |
| UX | No | Existing workflow presentation was preserved | Unnecessary |
| AX | No | No AI/provider behavior changed | Unnecessary |
| DX/Maintainability | Combined with Architecture | Test seams, consumer inventory, package-smoke parity | Appropriate |
| Performance | No separate agent | Reviewer found and resolved startup fan-out | Could have been useful earlier once fan-out appeared |
| Implementation | Yes | Narrow code/tests/docs changes | Appropriate |
| Documentation | Yes | Hierarchy design, ADR immutability, Mermaid truth | Appropriate |
| Adversarial Validator | Yes | Found identity collision, false bound, deferred limits, and diagram ambiguity | Essential |

- Specialist that should have been involved earlier: Performance could have
  reviewed the initial startup fan-out, but a permanent routing change is not
  justified from one case.
- Finding duplicated by multiple specialists: direct caller/recovery tracing,
  deliberately for independent confirmation.
- Routing change to consider at the slow-loop review: consider Performance when
  a critical recovery path expands retained-record work; keep as a candidate.

## Review and validation gaps

- What implementation missed: identity collision, a genuine input bound,
  package-smoke plan preflight, and active documentation for the Undo
  journal-persistence asymmetry.
- What independent review found: the four items above, accepted-ADR mutation,
  and a Mermaid graph that visually routed startup through two refresh paths.
- What automated tests failed to catch: historical fixtures made the two
  identities equal and tested service behavior without every production
  caller. New tests use distinct/colliding identities and composition seams.
- What remains manual or platform-specific: hosted four-host matrix, exact
  `main`, and interactive disposable-file History/restart scenarios.
- Documentation/diagram synchronization completed: yes; 14/14 compiled policy
  tests, link/Mermaid rules, and `git diff --check` passed.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Production Results IDs differ from captured filesystem identities; old fixtures made them equal | Architecture/test failure: separate authorities collapsed in fixtures | Tests crossing authorities must use deliberately distinct identifiers and include collision cases where misrouting is harmful | Focused behavior regression | Reconciliation collision test must keep unrelated row unchanged and preserve matched Results ID | Accepted/promoted |
| C2 | Startup initially unioned up to 500×1,000 action paths while prose called one call bounded | Performance/review failure: invocation count substituted for input-work bound | A bounded-work claim must name and test the actual cardinality cap or coalescing authority | Bound helper plus scale regression and current architecture docs | Assert 500-root maximum and reject overflow independent of action count | Accepted/promoted |
| C3 | Earlier archaeology found two bypasses but missed `FolderRestructuringService.ApplyAsync` | Missing-context/review failure: documented paths were followed without enumerating interface consumers | For a critical authority handoff, enumerate production callers and make deliberate exceptions visible | Candidate low-noise architecture inventory test | Compare production `IChangePlanExecutionService` callers with reconciled/explicitly exempt routes | Candidate/deferred |
| C4 | A flat README/docs catalog obscured current source, release, candidate, and history boundaries | Documentation failure: progressive disclosure existed as policy but not public navigation | Public routers should lead by reader intent and keep one owner per fact | Existing documentation-policy test | Require the six intent routes and their authority targets without pinning prose/order | Accepted/promoted |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1 | Adversarial Validator | `Accept` | Independently reproduced the authority collision risk and rejected the equal-ID fixture | `FilesystemIdentityCollision_DoesNotSelectAnUnrelatedResultsRow` plus distinct-ID Undo/startup tests |
| C2 | Adversarial Validator | `Accept` | Verified journal/plan limits and `BackgroundIndexingService` path work; root overlap preserves affected-source coverage | `GetRecoveryRefreshRoots_CoalescesWithinJournalRetentionBound` plus composition/current architecture checks |
| C3 | Adversarial Validator | `Defer` | The extra consumer is real, but the inventory check needs an explicit allowed-caller model and should accompany its separate fix | Retained as candidate and current limitation; no brittle source-count test added |
| C4 | Documentation specialist and Adversarial Validator | `Accept` | Both verified the hierarchy problem and the low-noise authority routes | `DocumentationIndex_ContainsRequiredEntryPoints` route assertions; no new router file |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Lead, Architecture/DX, Documentation, adversarial validator |
| Major knowledge sources loaded | Current State, docs router, authority map, risk matrix, ADR-004, Change Plan source/tests/history |
| Duplicate exploration | Minor; independent consumer/claim verification only |
| Substantive rework count | 2 algorithm/bound corrections; 2 documentation corrections |
| Independent findings | 2 high, 4 medium/precision; all blockers fixed, deferred risks recorded |
| Corrective follow-up needed | Yes—hosted CI/integration authorization only |
| Durable knowledge produced | Behavior/scale/documentation tests; current architecture/docs; report/retrospective |
| Code confidence | High after independent GO |
| Automated-validation confidence | High locally: 1,870 tests per configuration, all repository gates green |
| Interactive/manual confidence | Not exercised |
| Platform/package confidence | Windows package smoke passed; hosted Linux/macOS/Windows pending |
| Release confidence | Not assessed; no release operation and v2.4.0 remains latest |

## Handoff to slow-loop review

- Candidates awaiting evaluation: C3 only; possible Performance routing signal
  remains too weak for promotion.
- Active knowledge that may need compaction: none after replacing the stale gap
  text with current truth.
- Historical evidence worth retaining: old equal-ID tests, reviewer findings,
  and eventual hosted run IDs.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  evaluate an allowed production executor-consumer architecture inventory when
  the separate Folder Restructuring handoff is addressed; do not alter the
  Skill or ADR-004 now.
