# Project-owner run report — public documentation hierarchy audit

## 1. What this run was meant to do

Make the public documentation path obvious to a first-time GitHub visitor while
preserving the distinction between released v2.4.0 and the unreleased v2.12
candidate on `main`.

## 2. What actually changed

Repository history showed that PR #36 had already replaced the former flat
documentation catalog with an intent-first hierarchy. This run preserved that
accepted structure. It corrected two residual navigation defects: Current State
is now listed in the current-authority table, and the living Current State and
Release Status documents now point to the latest recorded validation boundary
instead of stopping at the earlier macOS-correction merge.

## 3. Important technical decisions

**Decision:** Keep README as the compact GitHub landing-page router and
`docs/README.md` as the authoritative deeper index.

**Why:** A new `START-HERE.md`, file moves, or another index would duplicate an
authority that already works and increase drift risk.

**Impact:** Visitors get progressive disclosure without losing access to
versioned release, candidate, validation, or historical evidence.

## 4. Validation and confidence

**Verified**

- Current `main` and `origin/main` both resolve to `ffc29ed`.
- Git history and blame verify that PR #36 implemented the intent-first public
  hierarchy.
- GitHub Actions runs 32408658961 (`3bb3919`) and 32410287837 (`ffc29ed`) are
  completed successfully; the latter has green Windows, Ubuntu, macOS ARM, and
  macOS Intel jobs. The retained PR #36 record also shows that Windows reached
  green after two controlled reruns at the implementation commit and one at the
  report-only tip, following different unchanged timing-sensitive failures; no
  code, threshold, or workflow change was used to clear them.
- Repository documentation-policy tests, link/path checks, strict UTF-8 checks,
  Mermaid structural checks, and `git diff --check` passed after the final edit.

**Not verified / manual validation required**

- No product build or full product suite was rerun because only Markdown
  navigation/current-evidence documents changed.
- Rendered GitHub presentation was reviewed from Markdown structure rather than
  by publishing this uncommitted patch.
- Existing v2.12 interactive, accessibility, installer, signing, notarization,
  and release gates remain open and unchanged.

## 5. Problems found

- **Already fixed by PR #36:** the root README's flat catalog and mixed
  release/candidate presentation.
- **Fixed here:** Current State was absent from the table labelled current
  project authorities.
- **Fixed here:** Current State and Release Status still presented `542e14a`
  and 1,861 tests as the latest validation boundary after PR #36 had recorded
  1,870 tests and later green exact-main runs.
- **Recorded:** route-test coverage does not validate Markdown fragment anchors;
  inspected anchors are valid, so this remains a candidate rather than a new
  brittle rule.
- **Recorded:** terminal hosted matrices are green, but the controlled Windows
  reruns remain validation-stability evidence rather than first-attempt-stability
  confidence.

## 6. What the agents learned

The prompt described the repository before PR #36, so repository evidence
prevented a duplicate redesign. Documentation and UX/DX specialists confirmed
the existing progressive-disclosure structure; independent review found the
stale destination evidence that a structure-only inspection missed. No lesson
was promoted: checking the truth at a navigation target is retained as a
candidate for slow-loop evaluation.

## 7. Documentation and diagrams

Updated Current State, Release Status, and the documentation authority table.
README, the intent router, architecture index, release history, and historical
records were verified and left unchanged. No Mermaid diagram changed because
the product and engineering architecture did not change.

## 8. Repository state

**Branch:** `main`

**HEAD:** `ffc29edec23c557b1c69be5bbc5fa5d77f18c6ba`

**Working tree:** Dirty with this uncommitted documentation audit and the
preserved pre-existing Security/Resource Efficiency engineering-system patch.

**Commits created:** None

**Push performed:** No

**Schema changed:** No

**Protocol/public interface changed:** No

## 9. Bottom line

**Status:** Complete

The requested hierarchy is present and its living validation destination is
now current. The result is safe to build on without changing release readiness:
v2.4.0 remains the latest release, while v2.12 remains the integrated,
unreleased candidate. The next action is to review and commit this documentation
patch separately from the preserved pre-existing engineering-system work.
