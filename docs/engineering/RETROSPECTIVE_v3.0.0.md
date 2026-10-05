# Run retrospective — progressive enrichment and reviewed organization

Date: 2026-10-05. Category: implementation and release. Risk: cross-cutting,
durable state, filesystem proposals, AI privacy, desktop and packaging.
Author: coordinating Codex agent. Implementation branch:
`codex/v3-progressive-enrichment`; release integration is still pending.

## Outcome

The intended result is a complete next-major testing candidate. Implementation
extends the existing indexing, relationship, proposal/executor and storage owners.
Local automated and actual-model evidence is recorded in
[Validation](../VALIDATION_v3.0.0.md). Publication remains a separate gate;
human acceptance starts after publication. Status: partial until release gates.

## Assumptions and rediscovery

The original checkout was an older dirty branch, not current release source.
Remote main and published releases determined the isolated baseline. An installed
Python distribution did not establish an immutable OmniLAB runtime; import-path
inspection required a frozen snapshot. Legacy JSON Search data can contain user
authority, so treating it as wholly disposable cache would lose decisions.
Current guidance now distinguishes automatic indexed inference from reviewed
filesystem proposals. Linked source owners, workflow and technology tables let
another developer follow the result without this conversation.

## Rework and specialist effectiveness

Three specialists implemented indexing, Organize and storage, then reviewed other
owners' actual source. The orchestrator handled desktop composition, release,
integration and actual-provider validation. UX/AX/performance concerns were
reviewed through those roles rather than adding overlapping agents; interactive
accessibility remains unverified. Documentation review covered the beginner guide,
storage/recovery and architecture diagrams.

Independent review found three substantive gaps: suppressed OCR consumers,
preference eviction, and missing-location-receipt recovery. All received code
fixes and regressions. A fourth gap was verification depth: feature-level AI
relationship checks were expanded to retained relationship and graph projection
after restart. Initial stale version/navigation assertions were updated for the
intentional v3 UI. One overlapping restore caused transient dependency/build
contention; central validation then owned the toolchain exclusively.

Native macOS CI subsequently caught rejection of the operating system's standard
`/var` alias by the storage guard. The fix admits only two exact macOS system
aliases, verifies their immediate targets/ancestry, and tests arbitrary/dangling
link refusal. Local Windows success could not establish that platform behavior.
The final living-document sweep also found stale schema and review-only AI wording;
authority/glossary/technology guides and ADR-007 now state the implemented boundary.
A subsequent native ARM run passed the production alias checks but exposed Unix
dangling-link cleanup in the new regression fixture; it now unlinks the entry
without resolving its absent target. GitHub runner acquisition failures were
recorded separately from this actionable test failure.

OmniLAB's bounded implementation requests mostly returned unavailable or unusable
results. Helpful safety/recovery diagnoses were verified, and direct fallback was
recorded rather than presenting local output as completed implementation.

## Candidate lessons

These observations are candidates, not newly promoted project rules.

| ID | Observation and evidence | Root cause | Proposed durable form | Evaluation |
| --- | --- | --- | --- | --- |
| C1 | Lost storage receipt could select stale default data | Recovery marker treated as optional lookup | Missing/corrupt receipt regression and recovery documentation | Pending independent generalization review |
| C2 | Legacy cache and decision history contain durable authority | Classification by filename/store age | Reclamation and history-capacity regression tests | Pending independent generalization review |
| C3 | OCR suppression must apply to every derived consumer | Privacy gate only at extraction boundary | Suppressed-retained-text consumer regressions | Pending independent generalization review |
| C4 | Enrichment features alone did not prove Related Files/graph behavior | Test stopped before durable integration consumer | Incremental relationship/reopen/graph test | Pending independent generalization review |
| C5 | macOS native tests rejected trusted system temporary paths | All ancestor links treated alike | Exact native alias and untrusted-link regressions | Pending independent generalization review |

## Confidence and handoff

Code-level: independently reviewed, blocking findings fixed. Automated confidence:
executed local suites plus the linked final CI evidence. Manual confidence:
unestablished; checklist explicitly Not run. Platform/package and release confidence
depend on exact-main native workflow results and published artifact identity.
No skill self-modification or candidate-lesson promotion occurred. Retain request
outcomes, source-bound CI, release hashes and these regressions for future audit.
