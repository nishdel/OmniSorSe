# Project-owner report — operation reconciliation and documentation hierarchy

## 1. What this run was meant to do

Close the stale Results/index handoff after Operation History Undo and startup
interruption recovery, while making the public documentation path clearly
separate current source, the v2.4.0 release, candidate documentation,
validation evidence, and history.

## 2. What actually changed

Operation History now forwards every returned terminal Undo record—including
blocked outcomes—to the same shell reconciliation used by Review Changes.
Startup now validates the authoritative plan and journal stores early, waits
until the background index and shell are ready, then reconciles the exact
recovery records. Package smoke follows the same lifecycle.

Reconciliation no longer assumes that a filesystem identity is a Results row
ID. It locates the affected row by recorded path and preserves that row's
logical ID. Startup indexing work is coalesced to at most one root per retained
journal operation, a hard maximum of 500, rather than expanding to as many as
500,000 action paths.

The GitHub README is now a small intent-based router. It leads a first-time
visitor from current state to release use, development, architecture, the
unreleased v2.12 candidate, validation/readiness, or released/history detail.
The deeper documentation and architecture indexes use the same hierarchy.
No files were moved and no historical record was rewritten.

## 3. Important technical decisions

**Decision:** Reuse the Operation Journal and existing reconciliation service;
do not add another recovery ledger or event bus.

**Why:** The journal owns actual execution/Undo/recovery facts and retains 500
operations, while source plans retain only 100.

**Impact:** History and restart paths remain correct after a source plan is
pruned, with no schema or persistence-authority change.

**Decision:** Match Results rows by recorded path, never by filesystem
identity, and preserve the matched Results ID.

**Why:** Production Results IDs and filesystem safety identities are separate
concepts; older tests incorrectly made them equal.

**Impact:** Undo and recovery cannot update an unrelated Results row merely
because its logical ID collides with a recorded filesystem identity.

**Decision:** Coalesce startup index submission to affected operation roots.

**Why:** Source and destination paths are validated inside each operation root,
and indexing maps those roots to affected configured sources. This preserves
coverage with a 500-root bound independent of action count.

**Impact:** Startup performs one bounded index submission and one bounded user
notification rather than potentially expanding work per recovered action.

## 4. Validation and confidence

**Verified**

- Local Windows, workspace .NET SDK 10.0.400/runtime 10.0.11: no-cache restore;
  Debug and Release builds completed with zero warnings/errors.
- Debug 1,870/1,870 and Release 1,870/1,870 tests passed, with zero failed or
  not executed. Focused final reconciliation tests passed 9/9 and focused
  Desktop recovery/Undo/composition tests passed 41/41.
- Formatter/code-style/analyzers, 14/14 repository documentation/policy tests,
  internal link/Mermaid/agent/Skill policy, `git diff --check`, and the
  18-project direct/transitive vulnerability audit passed.
- A fresh self-contained `win-x64` publish passed the isolated package-smoke
  lifecycle, including plan/journal preflight and recovery reconciliation.
- Independent Architecture/Documentation/adversarial review gave GO after its
  identity, startup-bound, ADR-immutability, diagram, and limitation findings
  were corrected.

**Not verified / manual validation required**

- Hosted Windows, Ubuntu, macOS Intel, and macOS ARM validation is pending
  explicit authorization to commit/push a validation branch. Exact-`main`
  validation therefore remains open.
- Interactive Operation History Undo and forced interrupted-startup recovery
  were not exercised through the GUI on disposable real files.
- Signing, tagging, installers, GitHub Release, and package publication are out
  of scope. v2.4.0 remains the latest release and v2.12 remains a candidate.

Code-level and local automated confidence are high. Windows native-package
confidence is high for the smoke boundary. Cross-platform and interactive
confidence remain open until the named evidence exists.

## 5. Problems found

- **Fixed:** Operation History Undo refreshed only its journal ViewModel; its
  exact terminal record did not reach Results/index reconciliation.
- **Fixed:** startup and package smoke discarded `RecoverInterruptedAsync`
  results.
- **Fixed:** reconciliation could mistake filesystem identity for a Results
  logical ID; realistic collision coverage now prevents recurrence.
- **Fixed:** startup index submission was described as bounded without a real
  path-count bound.
- **Fixed:** public documentation was a flat catalog and the architecture index
  had a stale ADR range.
- **Recorded/deferred:** Folder Restructuring Apply is another direct executor
  consumer without shell handoff; duplicate-recovery Undo cannot recreate a
  removed Results logical ID from journal schema 1; recovery/reconciliation is
  adjacent but not crash-atomic; and an Undo journal-write failure after the
  inverse filesystem action can leave disk restored without a terminal
  projection handoff.

## 6. What the agents learned

The strongest incorrect assumption was that a Change Plan's captured file
identity was the Results row ID. Independent review also found that one call is
not evidence of bounded work, and that following documented consumers missed
the additional Folder Restructuring caller.

Architecture, Documentation, Implementation, and the adversarial reviewer were
useful. Product, UX, AX, and a separate Performance agent were omitted because
scope, interaction, AI, and product policy did not change; the reviewer still
caught the startup scale issue. The identity and startup-bound lessons were
independently accepted and promoted as focused executable regressions. A
production-executor-consumer inventory check remains a candidate, not a rule.

## 7. Documentation and diagrams

README, the documentation index, architecture index, Current State, Developer
Guide, Architecture Overview/Authority, Change Plan subsystem record, glossary,
Roadmap, and Release Status were synchronized. The five-view System Map keeps
its existing count; its Change Plan Mermaid view now distinguishes durable
History, interactive reconciliation, and bounded startup reconciliation.
Accepted ADR-004 remains unchanged as a historical decision record.

## 8. Repository state

**Branch:** `main`

**HEAD:** `0fdcf54e2173b30073914d9502a9ce358d2df2b6`

**Working tree:** 23 intended modified files plus this report and its
retrospective; no staged files or unrelated pre-existing work

**Commits created:** None; explicit authorization is required

**Push performed:** No

**Schema changed:** No

**Protocol/public interface changed:** No supported external, Explorer,
extension, serialization, or schema contract changed. The Application assembly
grants the Desktop assembly internal access to the journal-only reconciliation
seam; the existing public reconciliation interface is unchanged.

## 9. Bottom line

**Status:** Partial — pending hosted-validation authorization

Both requested changes are implemented, independently reviewed, and fully
green in local automated and Windows native-smoke validation. The tree is safe
for a normal validation branch, but this report does not yet claim hosted
cross-platform or exact-`main` confidence. The next action is to authorize a
normal feature-branch commit/push/PR; if all four hosted jobs pass, merge
normally, run exact-`main`, and update this evidence record without creating a
release.
