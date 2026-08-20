# Run retrospective — macOS hosted-test correction

**Date:** 2026-08-20

**Task category:** Cross-platform regression correction

**Risk domains:** Explorer/OmniBrille local IPC; Change Plan test capability
isolation; hosted platform validation

**Implementation/review commit:** correction `d81f154`; normal merge `542e14a`

**Retrospective author:** Documentation specialist, for independent review

## Outcome

- Intended outcome: correct the three published-main macOS test failures
  without enabling macOS production mutation or changing product contracts.
- Actual outcome: the opaque handoff endpoint was shortened with entropy
  preserved, and the execute/Undo test received explicit supported test
  capabilities. Local, pull-request, and exact-main matrices passed.
- Completion status: `Complete`
- Important remaining uncertainty: interactive/native product scenarios and
  release gates remain outside this corrective run.

## Assumptions and rediscovery

- Incorrect assumptions: the logical named-pipe length alone was not the
  portable bound; .NET's complete Unix socket path includes additional host
  path components.
- Architecture/authority rediscovered: production platform capabilities, not
  the test host, own whether source-file mutation is supported.
- Missing knowledge: the affected domain test did not make its required
  capability fixture explicit.
- Stale or contradictory documentation: living status documents still
  reported the initial hosted failures; historical publication records were
  correct for their observation point.
- Could another competent developer understand the result without this run's
  conversation? `Yes`; regression assertions, platform-policy tests, Release
  Status, and the owner report preserve the behavior and evidence boundary.

## Rework and context efficiency

- Unnecessary exploration: none material; the prior report named all three
  failures and the affected subsystem boundaries.
- Duplicated exploration between specialists: none material.
- Context that should have been transferred earlier: the distinction between a
  domain test's supported fixture and production macOS capability policy.
- Substantive implementation reversals/rework: none.
- Corrective follow-up run required: `No`; both the pull-request and exact-main
  hosted matrices passed.

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Kept the correction narrow and protected release claims | Appropriate |
| Product/Strategy | No | Product scope was unchanged and explicit | Unnecessary |
| Architecture | Yes | Confirmed entropy, protocol, and platform-authority boundaries | Appropriate |
| UX | No | No user workflow changed | Unnecessary |
| AX | No | No AI/provider behavior changed | Unnecessary |
| DX/Maintainability | No separate agent | Portable test seams were covered by implementation and review | Separate routing unnecessary |
| Performance | No | No runtime work/allocation bound changed | Unnecessary |
| Implementation | Yes | Applied the two minimal corrections | Appropriate |
| Documentation | Yes | Separated living status from historical evidence | Appropriate |
| Adversarial Validator | Yes | Challenged scope and required exact hosted evidence | Appropriate |

- Specialist that should have been involved earlier: none.
- Finding duplicated by multiple specialists: none material.
- Routing change to consider at the slow-loop review: none yet; evaluate the
  candidates below before changing active routing or rules.

## Review and validation gaps

- What implementation missed: no remaining material issue after review.
- What independent review found: explicit entropy and production-capability
  non-regression checks were necessary to keep the small fix truthful.
- What automated tests failed to catch: the previous Windows-only/local run did
  not exercise the complete macOS Unix socket path or production host policy.
- What remains manual or platform-specific: interactive OmniBrille, desktop,
  accessibility, installer, signing, notarization, and release scenarios.
- Documentation/diagram synchronization completed: four living status/index
  documents updated; the compiled 14/14 documentation-policy suite, whitespace,
  and independent strict-UTF-8/link checks passed. No architecture or Mermaid
  change was warranted.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | The prior handoff name made the complete macOS Unix socket path exceed the host limit; both macOS runners failed | Implementation/test failure: the logical endpoint was considered without the transport's complete path budget | Account for the complete Unix socket path, retain established random entropy, keep the logical identifier compact, and validate on native supported hosts | Focused endpoint length/entropy and native handoff regression tests | Run single-use and separate-process handoff tests on both macOS architectures | Accepted and promoted as scoped executable memory |
| C2 | Execute/Undo expected supported mutation while its fixture inherited production macOS policy | Test/missing-context failure: domain behavior and platform support policy were coupled implicitly | When platform capability is not the behavior under test, inject an explicit supported fixture and separately preserve production-policy tests | Explicit test capability fixture plus neighboring platform-policy regressions | Run domain test on all hosts and confirm macOS production mutation remains unavailable | Merged and promoted into existing H3 cross-platform fixture/policy lesson |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1 | Adversarial Validator | `Accept` | Independently corroborated by the v2.4 compact endpoint correction and this second native macOS failure; existing length/entropy, single-use, separate-process, and two-macOS-host checks are sufficient | Promoted in focused tests and the production why-comment; no global rule added |
| C2 | Adversarial Validator | `Merge` | The existing H3 lesson already covers native capability assumptions and host-independent fixtures; the explicit fixture plus production-policy tests supply executable memory | Merged into [H3 cross-platform fixture/policy knowledge](../ARCHAEOLOGY_2026-08-18.md#h3--cross-platform-validation-encodes-the-development-host); no duplicate rule added |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Lead, Architecture, Implementation, Documentation, adversarial validator |
| Major knowledge sources loaded | Prior owner report/failures, Current State, Release Status, risk matrix, companion handoff contract, platform capability source/tests |
| Duplicate exploration | None |
| Substantive rework count | 0 |
| Independent findings | Preserve explicit entropy and production capability boundary; require exact-main hosted evidence |
| Corrective follow-up needed | No |
| Durable knowledge produced | Regression tests; current status docs; report/retrospective |
| Code confidence | High for the two narrow corrections |
| Automated-validation confidence | High: local, PR, and exact-main matrices passed |
| Interactive/manual confidence | Not exercised |
| Platform/package confidence | Hosted Windows/Ubuntu/macOS ARM/macOS Intel tests and native package smoke passed; interactive/install lifecycle not exercised |
| Release confidence | Automated correction complete; v2.12 remains unreleased with manual/release gates open |

## Handoff to slow-loop review

- Candidates awaiting evaluation: none.
- Active knowledge that may need compaction: none.
- Historical evidence worth retaining: initial run `32360140293`, green PR run
  `32373697544`, and green exact-main run `32375495795`.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  keep the promoted tests low-noise and retire any duplicate prose; no Skill,
  ADR, routing, or global prompt change is justified by this run.
