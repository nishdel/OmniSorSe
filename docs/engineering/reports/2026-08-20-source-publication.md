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
release. Publication record commit `cc6c331` was pushed normally and atomically
to `origin/main`; every v2.5-v2.12 branch ref was also created at its exact local
tip. A disposable clone independently confirmed the remote default source.

## 3. Important technical decisions

**Decision:** Integrate the validated candidate line into `main` without a
v2.5-v2.12 release tag or package.

**Why:** The owner requested cloneable trustworthy source, not a product
release. The later versions still have open manual/native/package gates.

**Impact:** GitHub `main` contains the complete history and current source while
v2.4.0 remains the latest downloadable GitHub Release.

**Decision:** Publish the exact candidate refs for discoverability while keeping
`main` as the source-clone authority.

**Why:** A fresh clone of an updated `main` receives every ancestor commit;
branch-name publication is useful but remains a separate discoverability choice.

**Impact:** Local/remote ref equality and fresh-clone verification proved the
publication; no branch name was substituted for release readiness.

## 4. Validation and confidence

**Verified**

- The exact integrated tree at `cc6c331` passed .NET SDK 10.0.400 no-cache
  restore and zero-warning/zero-error Debug and Release builds.
- Debug and Release each passed 1,861/1,861 tests with no failures or skips at
  that exact tree.
- Focused documentation/configuration and queued-progress regression tests
  passed.
- Whitespace, style, analyzer, dependency-policy, vulnerability, documentation,
  Mermaid-structure, link, and diff gates passed.
- Local self-contained `win-x64` publish and correctly invoked native smoke
  passed.
- Git history confirms v2.5-v2.12 is linear after v2.4.0 and is preserved by
  local merge `be536a0`.
- A normal atomic push advanced `origin/main` from `40552b9` to `cc6c331` with
  no force, deletion, tag, package, GitHub Release, or history rewrite. Remote
  default HEAD is `main`; local and remote refs match exactly for `main` and all
  v2.5-v2.12 branches.
- A disposable clone from `https://github.com/nishdel/OmniSorSe.git` checked out
  clean `main` at `cc6c331`, contained the engineering entry points, passed
  no-cache restore and a zero-warning Release build, and independently passed
  1,861/1,861 Release tests with zero failed and zero not executed.
- After publication-status synchronization, all 14 focused repository
  documentation/configuration tests passed under the workspace .NET 10 SDK,
  including strict UTF-8, internal links, Mermaid structure, current-state,
  ADR, agent/Skill, dependency, packaging-policy, and tracked-artifact checks;
  `git diff --check` also passed.

**Not verified / follow-up required**

- Hosted exact-main CI is not green. Ubuntu passed, but both `macos-15` ARM and
  `macos-15-intel` in
  [Actions run 32360140293](https://github.com/nishdel/OmniSorSe/actions/runs/32360140293)
  failed the same three Application Debug tests: reviewed-organization
  execute/Undo, separate-process Explorer companion start, and a single-use
  named-pipe Unix socket path exceeding the 104-character host limit. The Intel
  job reported 1,005 passed and three failed; Windows was still running at the
  observation point.
- Interactive Linux/macOS desktop and accessibility validation, resolution of
  the macOS automated failures, installer, signing, notarization, package
  publication, and release readiness.

## 5. Problems found

- **Fixed:** the trustworthy candidate and engineering-system work was not
  reachable from remote `main`; exact ref publication and a clean clone now
  prove availability.
- **Explained and resolved:** later branches were invisible because their refs
  had never been pushed. Exact remote refs now exist; no branch or tag was
  deleted or rewritten.
- **Follow-up required:** both exact-main hosted macOS Debug jobs failed the
  same three tests.
  Diagnose those regressions separately; do not describe cross-platform CI or
  release readiness as green.
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

Accepted lesson: a validated local branch is not published source. This run's
remote-ref equality and disposable-clone evidence exercised the proposed rule:
a Git source-publication workflow should compare the intended local commit with
the post-push remote ref and, where practical, verify the remote default branch
in a disposable clone. An independent adversarial reviewer accepted the rule as
proportionate and low-noise; it is promoted in the Risk/Validation Matrix.

## 7. Documentation and diagrams

Updated the repository README, Current State, Release Status, Release History,
Product Roadmap merge-state fields, and publication validation guidance. No
architecture, product behavior, protocol, schema, or Mermaid flow changed, so
no architecture diagram required modification.

## 8. Repository state

**Branch:** `main`

**Published source HEAD:** `cc6c331c984a6298f74fbc8ed7fb8e0681974ff2`

**Remote main:** `cc6c331c984a6298f74fbc8ed7fb8e0681974ff2`; exact equality verified

**Working tree:** publication-status documentation and retained run evidence
are uncommitted at this report revision

**Commits created:** `a60bbfe` (engineering system), `be536a0`
(history-preserving candidate-source merge), and `cc6c331` (source-publication
record)

**Push performed:** Yes. Normal atomic push of `main` and exact v2.5-v2.12
branch refs; no force, deletion, tag, package, GitHub Release, or rewrite.

**Schema changed:** No

**Protocol/public interface changed:** No

## 9. Bottom line

**Status:** Complete with follow-up

The source-publication objective is complete: GitHub `main` contains the
history-preserved v2.12 candidate source, and another machine can clone, restore,
build, and test it normally. This is not a product release; v2.4.0 remains the
latest release. The next separate task should diagnose and fix the three hosted
macOS Debug test failures, then re-run exact-main hosted validation before
any cross-platform or release-readiness claim.
