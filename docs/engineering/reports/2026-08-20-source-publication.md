# Project-owner report — current-source publication

## 1. What this run was meant to do

Determine why GitHub stopped at v2.4.0, identify the trustworthy local source
history, integrate it without rewriting history, and publish it so a normal
clone of `main` contains the current OmniSorSe source.

## 2. What actually changed

The discrepancy was real: `origin/main` and the remote default branch still
pointed to the v2.4.0 integration commit `40552b9`, while v2.5-v2.12 existed as
one linear local candidate chain and the accepted engineering-system patch was
initially uncommitted. No later branch ref had been pushed, so GitHub could not
display those branch names or supply that source to another clone.

The engineering-system patch was committed as `a60bbfe`, and local `main`
integrated the complete candidate branch through the history-preserving merge
`be536a0`. Living status documents now distinguish integrated source from a
release. At this report revision, the normal push and remote fresh-clone check
remain pending.

## 3. Important technical decisions

**Decision:** Integrate the validated candidate line into `main` without a
v2.5-v2.12 release tag or package.

**Why:** The owner requested cloneable trustworthy source, not a product
release. The later versions still have open manual/native/package gates.

**Impact:** Local `main` contains the complete history and current source while
v2.4.0 remains the latest downloadable GitHub Release.

**Decision:** Do not require every historical candidate branch ref to be
published.

**Why:** A fresh clone of an updated `main` receives every ancestor commit;
branch-name publication is a separate discoverability choice.

**Impact:** Remote `main` equality and fresh-clone verification are the actual
source-publication gates.

## 4. Validation and confidence

**Verified**

- Pre-integration .NET SDK 10.0.400 no-cache restore passed.
- Debug and Release builds passed with zero warnings and zero errors.
- Debug and Release each passed 1,861/1,861 tests with no failures or skips.
- Focused documentation/configuration and queued-progress regression tests
  passed.
- Whitespace, style, analyzer, dependency-policy, vulnerability, documentation,
  Mermaid-structure, link, and diff gates passed.
- Local self-contained `win-x64` publish and correctly invoked native smoke
  passed.
- Git history confirms v2.5-v2.12 is linear after v2.4.0 and is preserved by
  local merge `be536a0`.
- After publication-status synchronization, all 14 focused repository
  documentation/configuration tests passed under the workspace .NET 10 SDK,
  including strict UTF-8, internal links, Mermaid structure, current-state,
  ADR, agent/Skill, dependency, packaging-policy, and tracked-artifact checks;
  `git diff --check` also passed.

**Not verified / manual validation required**

- Push of local `main` to `origin/main`.
- Equality of the intended local commit and the remote `main` ref.
- Disposable fresh clone of the remote default branch.
- Hosted CI on the published commit.
- Native Linux/macOS, interactive UI/accessibility, installer, signing,
  notarization, package publication, and release readiness.

## 5. Problems found

- **Integrated locally:** the trustworthy candidate and engineering-system work
  was not reachable from remote `main`.
- **Explained, not cleaned up:** later branches were invisible because their
  refs had never been pushed. No branch or tag was deleted or rewritten.
- **Deferred:** remote publication and fresh-clone evidence remain the final
  source-availability gate.
- **Fixed before integration:** the Windows native package-smoke workflow passed
  a relative data root, relied on an unreliable shell exit-code path, and had
  no process timeout. A regression-first policy test now preserves the absolute
  root, structured arguments, real exit code, and bounded process-tree cleanup;
  the exact local invocation passed.
- **Unrelated and unchanged:** the recorded Operation History Undo/startup
  recovery reconciliation defect was not fixed in this run.

## 6. What the agents learned

The useful routing was Lead/Orchestrator, Git/release investigation,
Documentation, and independent validation; product UX/AX/Performance agents
were unnecessary. Existing repository knowledge prevented repeated product
archaeology.

Candidate lesson: a validated local branch is not published source. A Git
source-publication workflow should compare the intended local commit with the
post-push remote ref and, where practical, verify the remote default branch in
a disposable clone. The risk-matrix wording is subject to independent review;
this report does not promote the observation by itself.

## 7. Documentation and diagrams

Updated the repository README, Current State, Release Status, Release History,
Product Roadmap merge-state fields, and publication validation guidance. No
architecture, product behavior, protocol, schema, or Mermaid flow changed, so
no architecture diagram required modification.

## 8. Repository state

**Branch:** `main`

**HEAD:** `be536a0354e5ea2c28c826ea24547ebbcdb0432f` before this documentation patch
is committed

**Remote main:** `40552b9b2b18637313354713d66593d04cf0d92f`; update pending

**Working tree:** publication-status documentation and retained run evidence
are uncommitted at this report revision

**Commits created:** `a60bbfe` (engineering system) and `be536a0`
(history-preserving candidate-source merge)

**Push performed:** No; pending

**Schema changed:** No

**Protocol/public interface changed:** No

## 9. Bottom line

**Status:** Partial

The trustworthy source is integrated safely into local `main`, with history
preserved and no release inflation. Another machine cannot yet be said to clone
the current version because `origin/main` and a disposable clone have not been
verified. The next action is the normal non-forced push of the final validated
`main`, followed by remote-ref equality and fresh-clone verification; update
this retained report with those observed results before declaring publication
complete.
