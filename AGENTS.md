# OmniSorSe Codex orientation

This file is a router, not a complete project specification. Keep it compact.

## Orient proportionately

For a repository-changing or substantial task:

1. Confirm the repository root, branch, HEAD, remotes, worktree, untracked/staged
   files, active Git operations, and relevant local SDKs. Preserve user work.
2. Read [current state](docs/CURRENT-STATE.md), then search only the relevant
   row/section of the [documentation router](docs/README.md) for the affected
   subsystem. Do not load the complete documentation inventory by default.
3. Use the repository skill at
   [`.agents/skills/omnisorse-engineering-run/SKILL.md`](.agents/skills/omnisorse-engineering-run/SKILL.md)
   and the [risk/validation matrix](docs/engineering/RISK_VALIDATION_MATRIX.md).

For a tiny read-only question, inspect only the relevant source/test/document
and verify any current fact the answer depends on. A full Git/toolchain baseline
and substantial-run workflow are unnecessary unless the answer changes state or
makes a readiness/architecture claim.

When sources disagree, current source and tests own implemented behavior;
`docs/CURRENT-STATE.md` owns volatile version/runtime/schema/protocol/current-
boundary facts; [Release Status](docs/RELEASE_STATUS.md) owns validation,
integration, packaging, publication, and readiness evidence; current architecture
documents explain boundaries; ADRs record durable decisions; versioned reports
are historical evidence. Planned work is not implementation.

## Non-negotiable boundaries

- The live filesystem owns current source-file truth.
- Only the shared production Change Plan executor may mutate user files.
  Persisted intent, the operation journal, filesystem verification, and
  post-operation reconciliation are one safety workflow.
- `deep-index.db` owns durable indexed state and current Smart Tag,
  relationship, and Smart Collection authority. Preserve user-authored state.
- `knowledge-graph.db` is rebuildable projection state;
  `knowledge-decisions.db` is non-rebuildable graph-native authority.
- AI and derived evidence are optional proposals. They do not override user
  authority or bypass Change Plan review.
- Explorer Protocol and OmniBrille access is local, scoped, bounded, on-demand,
  and read-only. Plugins are capability-filtered, not OS-sandboxed.
- Treat legacy JSON Search/content indexes as compatibility state, not a reason
  to create a new authority. Investigate their interaction before changing it.
- Preserve OpenSorSe assembly, namespace, profile, and package identifiers
  unless a separately approved compatibility migration says otherwise.

See [architecture authorities](docs/engineering/ARCHITECTURE_AUTHORITY.md) for
evidence and source/test locations.

## Working rules

- Do not commit, merge, rebase, tag, push, publish, delete branches, rewrite
  history, or discard existing work unless explicitly authorized.
- Do not infer product-behavior changes from an infrastructure, documentation,
  audit, or diagnosis request.
- Extend an existing owner before proposing a store, coordinator, ledger,
  cache, index, or mutation path.
- Trace all consumers and success, failure, cancellation, retry, rollback,
  partial, Undo, and restart behavior that are relevant to the change.
- Apply bounds after correctness-preserving eligibility, not before it.
- Cross-target compilation is not native runtime evidence.
- Prefer focused regression/architecture tests over prose-only rules when a
  lesson can be made executable.
- Comments explain non-obvious reasons and invariants, not obvious statements.

## Specialists and context

The orchestrator selects specialists from [`.codex/agents/`](.codex/agents/)
by risk. Do not invoke every specialist for every task. Give specialists a
distilled baseline and relevant paths so they do not repeat archaeology.
Implementation follows resolved product/architecture decisions; contradictory
source evidence returns to the orchestrator. Independent review must not rely
only on the implementation agent's summary.

## Optional OmniLAB development aid

Delegate suitable substantive development work to OmniLAB / LocalAgentBridge
by default when available, including repository discovery,
bounded analysis, test-case drafting, documentation and first-pass review. It is
separately installed; optionality applies to installation, not the default routing
policy. It is not an OmniSorSe runtime dependency or mandatory contributor
installation. Codex defines scope, supervises, independently verifies source
evidence and results, and owns final decisions. Local output is contribution and
evidence for review, never proof of correctness or a human test observation.

Read the selected installation's `README.md`, `CODEX_POLICY.md` and integration
docs. The [upstream project](https://github.com/nishdel/LocalAgentBridge) documents
the stdio MCP bridge; newer companion development also documents durable jobs
in `docs/QUICKSTART.md`, `docs/CONFIGURATION.md` and
`docs/COMPANION-WORKFLOWS.md`. Development features are not proof of the installed
version's capabilities. Verify the interpreter, imported module location/version,
configuration and available CLI/tools. An installed runtime is not necessarily
an active controller or a bridge connected to this Codex session.

Keep the coordinating runtime/environment fixed while it owns jobs. Use a
non-editable installation or explicitly hashed source snapshot; edit a separate
worktree and do not upgrade an active worker/controller. Invoke from outside the
development checkout so Python cannot accidentally import that checkout.
For the owner's **existing** installation/configuration (configurable examples):

```powershell
$labPython = 'C:\Tools\LocalAgentBridge\.venv\Scripts\python.exe'
$labConfig = 'C:\Tools\LocalAgentBridge\config.toml'
& $labPython -m localagentbridge --help
& $labPython -m localagentbridge --config $labConfig codex-config
codex mcp list
```

`codex-config` prints registration without editing settings. The documented
MCP invocation is `python -m localagentbridge --config PATH server`, using that
verified interpreter; do not invent an `omnilab` executable or assume a checkout
is the active runtime. For file-path access, use an already authorized `source_root`
containing this repository. If it is outside `source_root`, supply bounded inline
excerpts to tools that support them; this grants no repository filesystem access
and needs no configuration change. Do not silently change personal configuration,
download models or substitute for existing selected models. Report a concrete limitation and
continue with Codex when integration, scope, models or usable output is missing.
Availability as an optional aid does not depend on participation in every task.

The baseline tools are `find_relevant_context`, `summarize_context`, `analyze_code`,
`analyze_test_output`, `review_diff`, `triage_task` and `second_opinion`. Use only
what the installed runtime exposes; lexical `use_model=false` may suffice.
When enabled, use existing skills/advisory roles, model-routing profiles, project
profiles and workflow policy from `docs/WORKFLOWS.md`, `docs/PROFILES.md` and
`docs/ADAPTIVE-DELEGATION.md`. Honor shared attempt/time/token budgets, bounded
source/diff/test evidence handoffs, source freshness and explicit Codex checkpoints.
Preserve handoff packets and spent budgets across resumes. Workflow checkpoints
record actual accept/reject/revise/abort decisions, not automatic approval.

Adaptive direct/auto/workflow selection should keep small, scoped work direct
unless specialization or project-required review justifies a workflow. Do not
force multiple agents for every small task. The legacy seven tools do not enforce
project workflow policy; use the workflow task interface when that policy applies.
Supported workflow CLI actions are `catalog`, `plan`, `start`, `step`, `checkpoint`
and `status`, with request shapes from the matching installed guide.

Where the installed companion supports it, `companion workflow-submit`,
`workflow-next`, `workflow-checkpoint` and `workflow-status` reuse that same engine,
profiles, budgets and evidence. Companion review events and workflow checkpoints
remain separate; local supervising review uses documented `companion decide --local`
without an authenticated callback. A single exact-model job does not imply workflow
policy was enforced. Use existing selected models and documented request schemas.
Record actual request/model/routing outcomes and what Codex accepted/corrected;
a gate-only handoff is not a local-model contribution.

For release preparation use the [manual-testing guide](docs/MANUAL_RELEASE_TESTING.md)
and [release index](docs/release-testing/README.md). Prepare the release issue
before human execution, update affected definitions when behavior changes,
and preserve prior package identities/evidence before selecting retests.

## Completion

Use the conditional Definition of Done in the risk matrix. Substantial runs
must synchronize affected current documentation and Mermaid diagrams, record
an evidence-based retrospective, keep candidate lessons separate from promoted
knowledge, and finish with the owner report template. Never describe reasoned,
cross-compiled, or planned checks as verified execution.
