# v3 milestone owner report

The task is to implement the agreed 38-item milestone and publish an installable
major-version testing candidate, including learned-vector hybrid Search before
the first v3 RC. The current code adds progressive local-AI
index enrichment, existing-library enablement, automatic inferred Search and
relationship evidence, an editable Organize page and storage controls. Richer
image/audio/video understanding remains explicitly future-facing.

The existing SQLite index and proposal/executor pipeline retain authority.
Enrichment changes application-owned data only; inferred output is structurally
validated and labeled, while file moves still require reviewed approval and retain
History/Undo. Schema 8 retains per-source AI policy and adds disposable,
model-versioned vector tables inside the existing SQLite provider. Independent
keyword and semantic retrieval use reciprocal rank fusion with exact-filename
priority; Related Files presents similarity separately from retained evidence.
Storage relocation uses a locked,
verified copy and atomic receipt; old generations remain recovery material.

Verified local evidence and actual OmniLAB contributions/fallbacks are in
[Validation](VALIDATION_v3.0.0.md). Independent review corrected OCR suppression,
durable preference eviction and storage recovery gaps. Candidate lessons are in
[the retrospective](engineering/RETROSPECTIVE_v3.0.0.md); none was self-promoted.
Human acceptance remains [Not run](MANUAL_TESTING_v3.0.md).

The beginner guide, Organize/storage instructions, current architecture and system
map, contributor/agent boundaries, compatibility, release notes and acceptance map
were updated. The [release status](RELEASE_STATUS.md) owns current integration,
package and publication evidence rather than this pre-release implementation note.

The earlier enrichment/Organize/storage implementation was committed and pushed
on `codex/v3-progressive-enrichment` in
[PR #54](https://github.com/nishdel/OmniSorSe/pull/54). The learned-vector
continuation has further working-tree changes under validation; this report
does not claim those changes are committed, pushed or merged. The recorded
integration baseline is main `727ce2d09ce9870f6e6ce9e4c3baa467c7e4d5de`;
the final remote-main SHA remains unassigned. The original dirty worktree was
preserved. Public Explorer Protocol stays 1.0; assembly/profile identities stay
compatible. In-place downgrade is unsupported after schema/storage migration.

Earlier evidence: Windows hosted validation at `c6999de` passed all 1,957 tests in both
configurations and every gate; actual local-AI and native Windows smoke also
passed. Remaining uncertainty: final macOS/Linux validation, installer lifecycle,
and human acceptance. Native macOS failures and the concurrent GitHub Actions
runner incident are recorded in the validation report. OmniLAB provided useful
safety/recovery advice but mostly unusable implementation output; direct Codex
fallback produced the majority of finished code.

The 2026-10-07 continuation adds modular embedding/vector contracts, the local
Ollama embedding provider, background incremental refresh, model/privacy/freshness
gates, vector storage controls, search explanations and explicit fallback. Code
review corrected the existing 100-ID hydration bound and a legacy-path/stable-ID
duplicate in fusion; both have targeted regressions. Further independent review
corrected shell wiring, rapid-selection state, freshness after asynchronous work,
and timeout/failure fallback preserving valid lexical results. Learned RRF now
uses a lexical-only input rather than giving feature-hash similarity another vote.
The continuation's documentation includes the hybrid query diagram, SQLite/vector
authority, model controls and coverage limits. All 24 human checklist rows remain
Not run. Specific continuation OmniLAB contributions and rejected claims are
recorded with request IDs in Validation.

The central runner progressed from 215 focused tests to **2,069 passing tests in
each of Debug and Release**, zero failed/skipped, with all three formatting checks
and the vulnerability audit passing. After three later lexical-ranking regressions,
the complete rerun passed **2,072 tests in each configuration**, zero failed/skipped,
with zero-warning/error builds and all whitespace/style/analyzer checks passing.
The earlier online audit found zero vulnerabilities; dependencies were unchanged.
This supersedes the 2,069-test checkpoint while remaining working-tree evidence.

The real Ollama `qwen3-embedding:4b` benchmark initially failed its unchanged
relevance gates (Recall@5 0.857143, zero paraphrase gains). A narrow function-word
correction in the lexical fusion input then passed: Recall@5 1.0, MRR 0.928571,
one paraphrase gain, exact filename first, and 564.9 ms reported upper-median query
latency. All source-integrity, restart, one-file incremental, unavailable-model
fallback and nonempty similarity-suggestion gates passed. These measurements cover
12 synthetic files and seven judged queries, not arbitrary libraries. Both JSON
reports and the exact model digest are recorded in [Validation](VALIDATION_v3.0.0.md).

The rebuilt self-contained Windows production startup/shutdown smoke also passed
with an isolated profile. Benchmark and smoke use
`da5b313813fe47007913c6ef7bafb8ed15bcce92-working-tree-20261007`; this identifies
the working-tree base, not a final commit containing the changes. The smoke is
not installer, interactive or screenshot evidence.

Required real screenshots remain an incomplete deliverable and explicit blocker
to full task completion. Following the earlier permission wait and two native
capture timeouts, two retries with a visible rebuilt window timed out again.
The final self-contained launch through the computer-use launcher triggered a new
app-access approval that timed out; no screenshot was captured.
Provider/chunker/document-draft OmniLAB requests produced no usable
implementation, and a separate schema-review escalation was rejected by automatic
approval review. Codex did not work around that rejection. Direct fallback still
accounts for most finished code.

Status at this source checkpoint: prerelease integration pending, with final
hosted/release gates and real screenshots incomplete. Exact merged source,
tag, installer assets and checksums must be verified on the official
[v3 release page](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
and [issue #53](https://github.com/nishdel/OmniSorSe/issues/53) when available.
Begin human testing only after the merged v3 source and matching published
installer are available; historical v2.13 packages are not that test target.
