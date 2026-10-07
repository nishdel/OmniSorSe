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

## Learned-vector continuation — 2026-10-07

The added objective is dedicated-model hybrid Search before the first candidate,
while retaining SQLite, source integrity, explicit relationship decisions and the
reviewed filesystem boundary. The implementation adds schema-8 derived vectors,
independent semantic retrieval, RRF, Related Files suggestions and status/storage
controls. Integration and publication are still separate orchestrator gates.

Search initially attempted to hydrate up to 1,000 semantic IDs in one call,
exceeding the existing SQLite lookup maximum of 100. Independent transport-agent
review and an independent source read found the same mismatch. Batching now uses
the existing bound; a 150-result regression verifies semantic retrieval survives.
The frozen local review also identified duplicate fusion when legacy path-only
hits and catalog stable IDs described the same file. Hydrated catalog identity
now reconciles them, with a one-result/two-rank and snippet-preservation test.

OmniLAB input initially exceeded the installed 10,000-character cap; the request
was reduced to 9,000 source characters with unchanged configuration/routing.
Its review was mixed: the duplicate-identity finding was useful, but a proposed
reset of RRF rank for every file was mathematically wrong and was rejected.
Other claims contradicted visible enabled guards or snippet fallback. A bounded
benchmark review incorrectly treated omitted/truncated sections as absent work;
those claims were rejected against the full source. Request IDs and accept/reject
decisions are retained in [Validation](../VALIDATION_v3.0.0.md). Local prose is a
review contribution, never an automated pass or proof of correctness.

The opt-in benchmark exercises production transport/store/coordinator/Search on
synthetic content with predeclared relevance gates and source hash/time checks.
An out-of-corpus query is explicitly diagnostic rather than a fabricated relevant
answer. Timing is measured but is not a broad performance promise. The central
runner owns builds, tests and real-model execution; package/native/human claims
must use its recorded evidence. Documentation now separates the older
deterministic “semantic available” filter from learned-vector status.

| ID | Observation and evidence | Root cause | Proposed durable form | Evaluation |
| --- | --- | --- | --- | --- |
| C6 | Vector-only retrieval above 100 IDs could lose every semantic result | New caller exceeded established hydration bound | 150-result integration regression | Pending independent generalization review |
| C7 | Legacy path and stable catalog ID could appear twice in RRF | Compatibility identity was not reconciled across channels | One-hit/two-rank/snippet regression | Pending independent generalization review |
| C8 | Local review asserted omitted benchmark steps were missing and proposed incorrect RRF ranks | Bounded evidence plus unverified model claims | Preserve full-source independent review and actual request outcomes | Pending independent generalization review |

These are candidate lessons only; no skill, policy or durable lesson was
self-promoted. Final automated, native, release and human confidence remain
governed by the exact-source validation/publication evidence.

### Continuation evidence audit

Additional substantive OmniLAB implementation requests also needed direct
fallback. Provider helper/tests request `8dc7e349-a235-415a-8e3e-aa5897fbcb5b`
returned only a restatement/incomplete uncertainty; chunker request
`0363f401-6634-4705-9194-7c13cb785896` failed while Ollama was unavailable.
Their same-task retries had no enabled capable model and made no new model
attempt. Guide/Mermaid request `88f8840e-b669-4776-a038-a7fc101ae75c` returned
no draft after its oversized first input was reduced. Feedback-only triage
recorded those three outcomes as rejected. These failures are not delegated
implementation completions or successful validations.

The separate schema-review call first failed on normal bridge telemetry outside
the write sandbox. Automatic approval review then rejected escalation because
the architecture/schema payload's configured destination and handling were not
established. That request has no model ID/result, and no workaround followed.
Actual bounded reviews elsewhere retain their own outcomes; most code still
required direct Codex implementation and independent source review.

The focused continuation suite first reached 215 passing cases across Application,
SQLite and Desktop. The central runner later passed 2,069 tests in each of Debug
and Release, with zero failures/skips, all three formatting checks and the
vulnerability audit. Three final lexical-ranking regressions were added afterward;
the complete rerun passed 2,072 tests in each configuration with zero failures/skips,
zero-warning/error builds and all whitespace/style/analyzer checks passing. The
earlier online audit found zero vulnerabilities with no subsequent dependency changes.
This supersedes the 2,069-test checkpoint. A rebuilt self-contained Windows
production startup/shutdown smoke passed with an isolated profile. This is native
process evidence, not installer or interactive acceptance.

Independent review fixed the shell's vector-controls wiring, stale Related Files
state during rapid selection, and learned-result freshness after hydration and
later asynchronous work. A further review caught optional hydration/validation
exceptions and timeouts incorrectly removing valid keyword results. Successful
validation now controls known invalidation; optional failure preserves unconfirmed
lexical results. Learned fusion uses lexical-only ranking, with the existing
ranking path restored if learned matches vanish, so feature-hash similarity cannot
silently become a second semantic vote.

Actual production-path benchmarking on 12 synthetic files retained the failed
report rather than redefining success. Initial Recall@5 was 0.857143, MRR 0.809524
and paraphrase gains zero. Common function words were admitting weak lexical
candidates into learned RRF. Filtering them when useful topic terms exist improved
Recall@5 to 1.0, MRR to 0.928571 and paraphrase gains to one, under the same ground
truth and pass criteria. Exact filename, phrase, function-word-only and fallback
behavior received focused regressions. Source integrity, restart reuse, exactly
one incremental embedding call, unavailable-model fallback and nonempty separately
labeled Related suggestions passed in both reports. The successful reported upper-median
query latency was 564.9 ms; this is a small-corpus measurement, not a general limit.

Reports identify `qwen3-embedding:4b` by its exact digest in
[Validation](../VALIDATION_v3.0.0.md) and record source as
`da5b313813fe47007913c6ef7bafb8ed15bcce92-working-tree-20261007`.
That identity describes the working-tree base and must not be cited as a final
commit containing the implementation. Both initial failure and subsequent pass
remain in `.artifacts/v3-vector-benchmark*.json`.

Screenshot collection remains an explicit blocker and incomplete deliverable.
After the permission wait timed out and the user confirmed readiness, two native
captures timed out; two more retries with the visible rebuilt window also timed
out. A final self-contained launch through the computer-use launcher triggered a
new app-access approval that timed out. No real screenshot or manual acceptance is inferred from attempts, automated
tests, native smoke or source inspection. Source integration/publication remains
pending at this checkpoint, and all 24 human checklist rows remain Not run.

| ID | Observation and evidence | Root cause | Proposed durable form | Evaluation |
| --- | --- | --- | --- | --- |
| C9 | Derived freshness changed during hydration or later asynchronous work | A single pre-read check did not span the query lifecycle | Post-hydration/final validation and known-revocation regressions | Candidate; pending independent generalization review |
| C10 | Optional vector failure could erase a valid exact keyword hit | Unconfirmed lookup failure was treated as confirmed revocation | Hydration/validation exception and timeout fallback regressions | Candidate; pending independent generalization review |
| C11 | Hash similarity and function-word matches weakened learned RRF | The keyword input was not sufficiently independent or selective | Lexical-only input, preserved fallback and real-model relevance gates | Candidate; pending independent generalization review |
| C12 | Component tests missed shell wiring and rapid-selection state | View-model behavior was tested outside its production composition/lifecycle | Shell composition and selection-race regressions | Candidate; pending independent generalization review |

These additions are candidate lessons only. No skill or durable policy was
promoted from this execution; final source, native platform, release and human
confidence require their own evidence.

Documentation gates require exact relative-link casing, strict UTF-8, balanced
supported Mermaid fences, the system map's existing five diagrams, required
documentation-router entries and ADR structure, and no tracked generated evidence.
The new benchmark is opt-in; its small-corpus results must stay separate from
full-suite, large-library, native installer and human confidence.
