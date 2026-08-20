# OmniSorSe risk and validation matrix

Status: living routing and completion policy. Evidence basis: source/tests and
the [2026 archaeology](ARCHAEOLOGY_2026-08-18.md). Risk labels are qualitative.

## Routing matrix

`Lead` always owns decomposition and completion. `Docs` means the Documentation
specialist participates during planning and final synchronization. Add Product
when scope/release intent is uncertain.

| Change category | Historical/current risk | Default specialists | Minimum focused validation |
| --- | --- | --- | --- |
| Documentation-only | Medium when labeled current; low for a clearly frozen report | Docs; adversarial review for authority/current-state claims | Link/path, Mermaid structure, terminology, source/test spot-check; no build claim |
| Isolated non-state bug | Low–medium | Implementation; reviewer | Observe the failure and, where a stable seam exists, capture a failing focused regression before the fix; check neighboring behavior |
| UI workflow/accessibility | Medium–high | UX, Implementation, Docs, reviewer; AX for AI/provider flows | ViewModel tests, keyboard/focus/live-region review, manual interaction named separately |
| Search/indexing/Smart Tags | High | Architecture, Performance, Implementation, Docs, reviewer | Correctness before bounds, large-library regression, cancellation/failure/privacy, legacy consumer check |
| AI/provider/media | High at UX/lifecycle boundary; deterministic core must remain optional | AX, UX, Architecture, Implementation, reviewer | Deterministic fallback, exact provider/model state, timeout/cancellation, bounded I/O, manual provider flow |
| Filesystem mutation/Undo | Critical | Architecture, UX, Implementation, Docs, reviewer | Approval/journal/identity fence; success, failure, cancellation, rollback, partial, Undo, reconciliation/restart |
| Persistence/schema/backup | Critical | Architecture, Implementation, Docs, reviewer; Performance when store lifecycle changes | Migration/legacy absence, corruption/atomicity, cancellation, user-authority round-trip, restore compensation |
| Relationships/Knowledge Graph | High | Architecture, UX, Performance, Implementation, Docs, reviewer | Authority bridge, generated-vs-authored separation, privacy, bounded reanalysis, stale/fail-closed reads |
| Explorer Protocol/OmniBrille | Critical contract/security boundary | Architecture, UX, Performance, Docs, reviewer | Version/DTO compatibility, scoped grant/revocation, read-only enforcement, bounds, native IPC where relevant |
| Plugins/extensions | High trust boundary | Architecture, DX, Implementation, Docs, reviewer | Capability/manifest/version/quarantine bounds; confirm no mutation or sandboxing claim |
| Performance/large library | High | Performance, Architecture, Implementation, reviewer | Representative scale, allocation/work bounds, cancellation/deadline, correctness outside caps |
| Packaging/platform/release | High operational risk | DX, Architecture, Docs, reviewer | local build/tests, native CI/host evidence, package smoke, artifact identity; signing/publication separate. For Git source publication, verify the intended local commit equals the remote ref after push and, where practical, verify the remote default branch from a disposable clone. |
| Cross-cutting architecture | Critical | Product if scope changes, Architecture, DX, relevant UX/AX/Performance, Docs, reviewer | dependency/authority tests, affected subsystem regressions, ADR/diagram, manual gaps |

Specialists can be omitted when the lead records why their concern is not
present. Review depth follows touched authority and failure surface, not diff
size alone.

## Conditional Definition of Done

A substantial change is complete when all applicable statements are true:

- behavior or infrastructure matches the resolved objective without unrelated
  scope expansion;
- existing user work and compatibility boundaries are preserved;
- focused tests express the behavior/invariant and relevant regression;
- every affected authority and consumer is traced through relevant success and
  non-success paths;
- bounds, cancellation, concurrency, privacy, and recovery were checked when
  those risks are present;
- UX/accessibility/AX behavior was reviewed when user or provider state changes;
- current documents, diagrams, glossary, contracts, and ADR implications match
  the final implementation;
- automated and manual/platform/package evidence are reported separately;
- independent review findings are fixed, deferred, or recorded explicitly;
- another competent developer can understand the result without the creating
  conversation;
- the retrospective distinguishes candidate observations from promoted
  knowledge; and
- the project-owner report states the exact final repository state.

Tiny isolated changes use only applicable items. Passing compilation or tests
alone does not satisfy an applicable architecture, interaction, or release gate.

## Confidence vocabulary

| Confidence | What it means |
| --- | --- |
| Code-level | The diff was inspected and reasoned against current boundaries. |
| Automated validation | Named builds/tests/checks actually ran successfully in the stated environment. |
| Interactive/manual | A human-observable flow was exercised on the named host; a checklist is not evidence. |
| Platform/package | Native execution or package smoke ran on the stated runtime/OS/artifact. Cross-target compilation is not this. |
| Release readiness | Required automated, native, manual, package, signing, and publication gates have explicit states. |

Use `Verified` only for observed evidence. Put skipped, unavailable, reasoned, or
still-required work under `Not verified / manual validation required`.

## Documentation impact check

During planning and after implementation, ask whether the diff affects Current
State, authority/subsystem docs, Mermaid diagrams, glossary, user flows, ADRs,
validation/performance/accessibility/AX guidance, public/protocol contracts, or
release evidence. Link to one owner instead of copying the same explanation.
