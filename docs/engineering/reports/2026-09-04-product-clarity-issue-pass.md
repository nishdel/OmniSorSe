# Project-owner run report — Product Clarity & Workflow

**Date:** 2026-09-04

**Candidate:** OmniSorSe 2.13.0-rc

**Base:** published `main` commit `4dd27d62fc4ecbe9916b9789c57d5e8d2336c9ac`

## 1. What this run was meant to do

Audit the open product issues against authoritative source and tests, improve
the Scan → Review → Organize path without weakening local-first or
review-before-change boundaries, validate the result, and prepare a release
candidate only if the evidence supports it.

## 2. What actually changed

- Home and navigation now lead with Scan, Review Changes, and Files/Organize;
  Search, Duplicates, Related Files, library automation, and advanced graph
  diagnostics have distinct roles.
- Review Changes identifies plan origin, purpose, warnings, and the exact
  `Approve all eligible` boundary. Duplicate cleanup remains recovery-based,
  keeps one copy per group, and no longer confuses cleanup selection with the
  five-item shell-open bound.
- Optional AI organization remains visible while disabled and deep-links to
  its Settings section without enabling or contacting a provider. Folder
  proposal Keep/Dismiss is single-terminal and concurrency-gated.
- Search gives results primary space, progressively discloses refinement,
  saved searches, and maintenance, and presents truthful Smart Tag states with
  refresh. Recent indexing throughput/ETA now uses bounded recent completions.
- Relationship-authority deletion, repair, merge/split, forget, unlink, and
  return-to-automatic operations require target-specific confirmation. Direct
  Related Files remains distinct from the derived Graph diagnostics view.
- Status presentation adds text, symbol, severity, and live-region semantics;
  compatibility names, profile paths, schema 6, Protocol 1.0, and file-mutation
  authority remain unchanged.

Issue disposition:

| Issue | Result in this pass |
| --- | --- |
| #46 product/UX umbrella | Major clarity and workflow items fixed; keep open for interactive UX and remaining feature requests. |
| #42 unified approval queue | Current-plan context and bulk eligibility fixed; multi-plan queue/filtering remains deferred. |
| #34 media search depth | Wording corrected; per-source media-depth policy remains a substantive feature. |
| #40 corrupt/unreadable files | Existing stage failures confirmed; consolidated provider-specific report remains a feature. |
| #41 protected subtrees | Confirmed absent; needs cross-layer policy/validation, not a UI-only patch. |
| #29, #31 scrolling | No unsafe speculative change; wheel/keyboard/focus/DPI reproduction remains manual. |
| #4 folder tree | Additive navigation feature retained for separate design. |
| #2 agentic process | This run followed the engineering-run evidence/review workflow; umbrella stays open. |

## 3. Important technical decisions

**Decision:** Advance source metadata to `2.13.0-rc` rather than modifying the
already published v2.12 package identity.

**Why:** The immutable v2.12 tag and public artifacts target an earlier commit;
new user-visible code cannot truthfully share that exact package identity.

**Impact:** Any publication from this work must be a new v2.13 prerelease built
from one exact merged `main` commit.

**Decision:** Treat retained relationship/index records as user authority even
when an action never changes original files.

**Why:** Repair, forget, merge/split, unlink, and automatic-result reset delete
or replace non-rebuildable user decisions.

**Impact:** A generic captured-target confirmation gate protects every such
operation and prevents selection drift or overlapping authority changes.

**Decision:** Preserve immutable suggestion origin when a user edits an
action.

**Why:** Duplicate reconciliation and recovery semantics depend on the
`DuplicateAnalysis` origin; `WasUserEdited` already records the edit.

**Impact:** UI provenance shows both facts without weakening post-apply/Undo
classification.

**Decision:** Do not increase indexing concurrency from one without a measured
real-library comparison.

**Why:** The observed low CPU is consistent with policy, while the demonstrated
defect was whole-run rate/ETA dilution.

**Impact:** Reporting is corrected; resource policy remains conservative.

No schema, protocol, production dependency, or public plugin contract changed;
no ADR was required.

## 4. Validation and confidence

**Verified locally on Windows with repository SDK 10.0.400**

- Debug and Release builds: zero warnings, zero errors.
- Debug tests: 1,877 passed, zero failed/skipped.
- Release tests: 1,877 passed, zero failed/skipped.
- Desktop regressions: 315; Search relevance: 1; performance regression: 16;
  repository documentation/dependency policy: 14.
- Whitespace, style, and analyzer verification; `git diff --check`; all active
  AXAML parsed successfully.
- Direct/transitive NuGet vulnerability audit: 18 projects, zero reported
  vulnerabilities.
- Forced no-cache `win-x64` runtime restore, self-contained publish, and native
  package smoke: exit code 0 with isolated profile creation.

**Not verified / manual validation required**

- Exact merged-main four-host CI and complete Windows/macOS packaging have not
  yet run at report authoring time.
- No interactive visual, resize/DPI, keyboard, screen-reader, real-library,
  optional-provider, normal-user installer, SmartScreen, Gatekeeper, upgrade,
  reinstall, signing, or notarization claim is made.
- Synthetic performance gates prove bounded regressions, not a real-world
  throughput increase or desktop latency target.

## 5. Problems found

- **Fixed:** repair bypassed confirmation; AI Keep/Dismiss could race; manual
  edits erased duplicate origin; graph help and AI Settings routing were
  misleading; visible saved-search terminology was split; a roadmap whitespace
  violation failed the patch gate.
- **Deferred/recorded:** multi-plan approval queue, per-source media depth,
  consolidated unreadable-file reporting, protected subtrees, folder tree, and
  unresolved interactive scrolling/layout complaints.
- **Preserved:** unsigned/unnotarized package status and all manual gates remain
  explicit rather than being inferred from automated smoke tests.

## 6. What the agents learned

UX review separated real missing behavior from discoverability problems.
Search/AI/performance review showed that default Basic indexing and one worker
explain several observations, while the rate/ETA formula was genuinely wrong.
Architecture review found three authority/concurrency/provenance release
blockers after the first implementation. Documentation/release review kept the
published v2.12 evidence immutable and forced a truthful v2.13 version line.
These are candidate lessons pending slow-loop evaluation; regression tests and
this report retain the evidence without changing the engineering Skill.

## 7. Documentation and diagrams

Updated the README, Current State, Release Status, Changelog, Product Roadmap,
platform matrix, documentation index, release defaults, and v2.13 release
notes. Added the v2.13 manual-validation addendum and this audit report. The
existing architecture maps remain accurate because authorities, schema, and
protocol did not change; no diagram update was necessary.

## 8. Repository state

**Branch:** `codex/product-clarity-issue-pass`

**HEAD at report authoring:** base commit `4dd27d62fc4ecbe9916b9789c57d5e8d2336c9ac`, changes uncommitted

**Working tree:** isolated task worktree; unrelated dirty worktree untouched

**Commits created / push performed:** recorded in the final release handoff

**Schema changed:** No

**Protocol/public interface changed:** No; Desktop-only typed focus state added

## 9. Bottom line

**Status:** Complete with follow-up

The local candidate is coherent and safe to build on. It may proceed through a
normal PR, exact-main hosted validation, and exact-source native packaging. It
must not be called stable/GA, and publication must stop if any hosted or package
gate fails. The open manual checklist remains the most important caveat.
