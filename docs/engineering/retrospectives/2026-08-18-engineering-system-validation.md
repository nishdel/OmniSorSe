# Run retrospective — engineering-system validation

**Date:** 2026-08-18

**Task category:** engineering-infrastructure validation and process review

**Risk domains:** cross-cutting documentation/configuration, executable memory,
build/test evidence, native package validation

**Implementation/review commit:** uncommitted patch based on
`d48bb080f5125e536978e5527c77afbb4b57da7d`

**Retrospective author:** Lead/Orchestrator; independently reviewed by the
Adversarial Validator

This record follows the earlier
[engineering-system retrospective](2026-08-18-engineering-system.md). It does
not rewrite that run's truthful record that SDK 10.0.400 was unavailable on the
system `PATH`; it records the later validation that closed the gap with the
ignored repository-local SDK.

## Outcome

- Intended outcome: close the .NET 10 validation gap, challenge the patch under
  its own workflow, and adopt only external methodology that fits OmniSorSe.
- Actual outcome: SDK 10.0.400 restore, zero-warning Debug/Release builds,
  1,861 tests in each configuration, formatting, policy, vulnerability, Skill,
  and corrected Windows-native smoke validation passed. Four small workflow
  clarifications were added; no product source changed.
- Completion status: `Complete with follow-up`.
- Important remaining uncertainty: native Linux/macOS execution, interactive
  UI/accessibility, hosted-CI execution, signing, and publication were not
  performed. The existing CI package-smoke invocation needs a separate repair
  before its exit status is treated as native evidence.

## Assumptions and rediscovery

- Incorrect assumptions: checking installed SDKs alone missed the ignored
  `.artifacts/dotnet10` SDK. The first build failures were sandbox write denials,
  not compiler failures. An initial smoke wrapper incorrectly treated
  `$LASTEXITCODE` from the Windows GUI host as reliable and passed a relative
  data root although the probe requires an absolute one.
- Architecture/authority rediscovered: `PackageSmokeTest` owns the probe
  contract and requires an isolated, fully qualified data root; the workflow
  currently supplies a relative root.
- Missing knowledge: the repository had no active note that the ignored local
  SDK existed. This remains run-local evidence rather than a permanent toolchain
  assumption.
- Stale or contradictory documentation: none found after the methodology edits;
  the previous run report is historical evidence, not current validation state.
- Could another competent developer understand the result without this run's
  conversation? `Yes` — current workflow changes are in the development system,
  risk matrix, and substantial-run Skill; evidence and caveats are retained
  here and in the owner report.

## Rework and context efficiency

- Unnecessary exploration: minor. Two failed sandboxed build attempts preceded
  the required escalated build; the smoke wrapper required correction after
  source inspection.
- Duplicated exploration between specialists: none. One Documentation/Product
  specialist handled only public-methodology research; one reviewer received a
  distilled baseline, diff, evidence, and non-goal.
- Context that should have been transferred earlier: the local SDK path and the
  smoke probe's absolute-path contract.
- Substantive implementation reversals/rework: none; only narrow active-process
  documentation and Skill refinements were added.
- Corrective follow-up run required: `Yes` — repair and regression-test the
  existing native smoke workflow separately; do not combine it with this patch.

## Specialist effectiveness

| Specialist | Used? | Useful finding or decision | Too early/late/unnecessary? |
| --- | --- | --- | --- |
| Lead/Orchestrator | Yes | Centralized all builds/tests, separated sandbox failures from code failures, and synthesized acceptance | Appropriate |
| Product/Strategy | Scoped with Documentation | Rejected importing an external workflow mechanically | Appropriate |
| Architecture | No new agent | Current authority map and prior independent architecture review were sufficient; no product boundary changed | Omission appropriate |
| UX | No | No user workflow changed | Omission appropriate |
| AX | No | No AI behavior changed | Omission appropriate |
| DX/Maintainability | Covered by lead | Verified SDK/build/configuration and context cost | Separate agent unnecessary |
| Performance | No | No runtime/performance behavior changed | Omission appropriate |
| Implementation | Lead only | Applied three narrow process-document edits | Separate agent unnecessary |
| Documentation | Yes | Compared current public methodology with active OmniSorSe knowledge and proposed four bounded refinements | High value |
| Adversarial Validator | Yes | Challenged evidence attribution, retained-run records, native smoke semantics, and acceptance | High value and correctly late |

- Specialist that should have been involved earlier: none.
- Finding duplicated by multiple specialists: none materially.
- Routing change to consider at the slow-loop review: none; this run supports
  the existing small, risk-matched team.

## Review and validation gaps

- What implementation missed: no patch failure was found. The prior run could
  not execute the new tests because its system SDK inventory stopped at 9.0.
- What independent review found: this run needed a separate retained report
  rather than leaving the SDK-blocked record as the apparent latest evidence.
  It also corroborated that the native-smoke concern is separate from the patch.
- What automated tests failed to catch: the workflow supplies a relative smoke
  data root even though the production probe requires an absolute root, and the
  Windows GUI-host invocation does not provide trustworthy `$LASTEXITCODE`
  evidence in this local shell. This predates the patch and was not fixed here.
- What remains manual or platform-specific: native Linux/macOS execution,
  hosted CI, interactive UI/accessibility, installer lifecycle, signing, and
  publication.
- Documentation/diagram synchronization completed: yes. No architecture or
  diagram changed in this validation run; existing link and Mermaid-structure
  gates were rerun after edits.

## External methodology evaluation

Primary evidence was Matt Pocock's current public material on
[discovery with repository docs](https://github.com/mattpocock/skills/blob/main/docs/engineering/grill-with-docs.md),
[specification](https://github.com/mattpocock/skills/blob/main/docs/engineering/to-spec.md),
[TDD](https://github.com/mattpocock/skills/blob/main/docs/engineering/tdd.md),
[implementation](https://github.com/mattpocock/skills/blob/main/docs/engineering/implement.md),
[two-axis review](https://github.com/mattpocock/skills/blob/main/skills/engineering/code-review/SKILL.md?plain=1),
and [handoffs](https://github.com/mattpocock/skills/blob/main/docs/productivity/handoff.md),
inspected 2026-08-18.

Accepted and promoted after repository comparison and independent review:

- name observable test seams and material non-goals before behavior changes;
- use a failing focused regression first only when a stable, independent
  behavioral oracle exists;
- review objective/plan fidelity and repository authority/standards as distinct
  axes; and
- hand off durable references plus only the live task delta, labeling inference
  and avoiding overlapping recursive delegation.

These now live in `DEVELOPMENT_SYSTEM.md`, `RISK_VALIDATION_MATRIX.md`, and the
substantial-run `SKILL.md`. Rejected: importing the full external Skill chain,
mandatory grilling/specification/TDD, automatic commits, fixed-cadence
architecture surveys, and mandatory fresh context per ticket. They duplicate
existing authorities, conflict with owner control, or add cost without local
evidence. Empirical before/after evaluation for material Skill changes remains
a candidate, not a rule.

## Candidate lessons

| ID | Observation and evidence | Failure class/root cause | Proposed general rule | Strongest proposed form | Future validation | Status |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | Native smoke passed only after supplying the production-required absolute root and explicitly waiting for the Windows GUI process; the current workflow uses a relative root and `$LASTEXITCODE` | Validation failure: the wrapper does not match the probe contract | Native gates must exercise contract-valid inputs and wait for the actual host exit | Workflow regression test plus corrected CI script | Reproduce on Windows CI and inspect profile creation/exit status | Candidate; independently corroborated, not promoted in this run |
| C2 | The ignored repository-local SDK closed a gap that installed-SDK inventory could not | Missing-context/toolchain discovery | Consider documented local SDK discovery only if this recurs | Development guidance, not a global rule | Observe another run | Candidate; defer |
| C3 | Public methodology suggests behavioral evaluation of Skill changes, but OmniSorSe has no repeated local evidence or harness | Process/agent-routing uncertainty | Evaluate material Skills against representative tasks before promotion | Future process evaluation | Compare baseline and candidate behavior on a later material Skill change | Candidate; defer |

## Independent lesson evaluation

| Candidate | Evaluator | Decision | Evidence/generalization check | Durable artifact or rejection reason |
| --- | --- | --- | --- | --- |
| C1 | Adversarial Validator | Defer | Source/workflow mismatch and local execution are real, but the repair is outside this patch | Separate native-smoke correction task |
| C2 | Adversarial Validator | Defer | One ignored local installation is not a stable repository contract | Keep only in this retrospective |
| C3 | Documentation specialist and Lead | Defer | Third-party practice has not yet produced OmniSorSe outcome evidence | Revisit only for a material Skill change |

## Compact run metrics

| Metric | Result |
| --- | --- |
| Specialists used | Lead, scoped Documentation/Product methodology research, Adversarial Validator |
| Major knowledge sources loaded | `AGENTS.md`, Current State, project Skill, development/risk/learning guidance, CI workflow, focused source/tests, external primary methodology pages |
| Duplicate exploration | None |
| Substantive rework count | 0 implementation reversals; 2 validation-wrapper corrections |
| Independent findings | 1 run-record completion issue fixed; 1 pre-existing native-smoke defect recorded |
| Corrective follow-up needed | Yes — native-smoke workflow, separate task |
| Durable knowledge produced | Skill and active process documentation; this retained evidence; no product test/code change |
| Code confidence | High for the uncommitted patch; no production source delta |
| Automated-validation confidence | High on Windows/.NET 10 for named gates |
| Interactive/manual confidence | Not established |
| Platform/package confidence | Windows self-contained host smoke verified with corrected invocation; Linux/macOS not established |
| Release confidence | Not established; this is not a release run |

## Handoff to slow-loop review

- Candidates awaiting evaluation: C1-C3 above.
- Active knowledge that may need compaction: none now.
- Historical evidence worth retaining: this record paired with the prior
  SDK-blocked retrospective.
- Skill, routing, ADR, test, or documentation item to reconsider periodically:
  whether a lightweight Skill forward-evaluation harness earns its cost after
  real repeated Skill changes.
