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

## Completion

Use the conditional Definition of Done in the risk matrix. Substantial runs
must synchronize affected current documentation and Mermaid diagrams, record
an evidence-based retrospective, keep candidate lessons separate from promoted
knowledge, and finish with the owner report template. Never describe reasoned,
cross-compiled, or planned checks as verified execution.
