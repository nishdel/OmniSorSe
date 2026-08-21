# Project-owner run report — public repository cleanup

## 1. What this run was meant to do

Make the public OmniSorSe repository read as one coherent open-source project:
explain the product before release archaeology, distinguish current v2.12
source from the downloadable v2.4.0 release, repair living documentation, and
publish the validated presentation to GitHub without changing the product or a
release.

## 2. What actually changed

The root README is now a product-first landing page with immediate Download,
Installation, Current Source, Documentation, and Contribute routes. It explains
user outcomes and the review-before-change workflow before presenting a compact
v2.12-candidate versus v2.4.0-release table. Detailed capability, validation,
and historical evidence now lives behind the intent-based documentation router.

Living installation, developer, safety/privacy, compatibility-router, and
implementation-index documents were corrected where they still used the old
GitHub URL, obsolete status language, stale headings, or ambiguous package
claims. Historical/versioned records were preserved. The empty public
Screenshots explanation was removed; no fake UI was substituted. The media
guide now defines genuine release/candidate capture locations, privacy rules,
and a short usage-video storyboard.

GitHub `main` received content commit `9e0a3dd`. The public About description now
states the local-first, deterministic, optional-AI position, and ten focused
repository topics were added.

## 3. Important technical decisions

**Decision:** Keep README as the concise public landing page and
`docs/README.md` as the comprehensive documentation router.

**Why:** A new start document or broad file moves would create another
authority and break stable historical links without improving navigation.

**Impact:** A visitor can progress from product and current state to use,
development, candidate detail, validation, and history without loading the
whole documentation archive.

**Decision:** Publish no screenshot until a real, privacy-reviewed application
capture exists.

**Why:** Repository and Git-history archaeology found only the genuine current
logo, not a trustworthy application screenshot, GIF, or video.

**Impact:** The landing page has no embarrassing placeholder or fabricated
visual. The owner has an exact capture checklist below.

## 4. Validation and confidence

**Verified**

- Repository documentation-policy tests passed 14/14 under workspace .NET SDK
  10.0.400, including strict UTF-8, internal paths, Mermaid structure, current
  entry points, agents/Skill configuration, dependency policy, and artifact
  checks.
- `git diff --check` passed; `git fsck --full --strict` passed with only known
  dangling objects.
- Independent Documentation and first-time-visitor reviews returned GO after
  correcting navigation order and the in-process plugin trust statement.
- GitHub publicly renders commit `9e0a3dd` on `main`, with the revised opening,
  quick actions, and canonical links. The live About description and topics
  were independently read back after update.
- [Exact-main hosted run 32458052519](https://github.com/nishdel/OmniSorSe/actions/runs/32458052519)
  completed successfully for Windows, Ubuntu, macOS ARM, and macOS Intel at
  `9e0a3dd`. The first Windows attempt remained in the unchanged Debug test
  step for more than 30 minutes, versus about 3.5 minutes in the previous
  successful baseline. It was cancelled as stalled; one controlled rerun of
  the same Windows job passed Debug/Release tests, zero-skip, formatting,
  documentation/dependency policy, vulnerability, runtime, and native package
  smoke gates without any source, test, threshold, or workflow change.

**Not verified / manual validation required**

- No current real application screenshot or usage video was available to
  inspect or publish.
- The application UI, accessibility, installer, signing/notarization, and
  candidate manual-release gates were not exercised by this documentation run.
- No release/package/tag was created; v2.4.0 remains the latest release.

## 5. Problems found

- **Fixed:** release engineering and an unreleased feature ledger dominated the
  landing page before visitors learned the product.
- **Fixed:** living canonical URLs, installation/package language, contributor
  routing, implementation/roadmap compatibility pages, current validation
  attribution, and several obsolete headings/terms.
- **Fixed:** public prose implied stronger plugin containment than the actual
  trusted in-process boundary.
- **Fixed:** GitHub About metadata described OmniSorSe primarily as an “all in
  one” AI organizer and exposed no discovery topics.
- **Recorded:** the first exact-main Windows attempt stalled in Debug tests;
  the unchanged controlled rerun passed. This is hosted-runner/validation-
  stability evidence, not first-attempt-stability confidence or a product fix.
- **Recorded:** no real UI media exists; the capture work is listed below.
- **Deferred:** an unused legacy Avalonia icon and old redirect URLs in product
  source/package metadata are outside this documentation-only task.
- **Deferred:** a public security-reporting policy needs a real private contact
  or enabled private vulnerability reporting; neither was invented here.

## 6. What the agents learned

Product/UX review identified the chronology-first landing problem;
Documentation review separated living authorities from historical evidence;
the visitor review caught an over-broad plugin claim and verified the final
public route. Media archaeology prevented the run from mistaking the current
logo for proof of a current application UI. Candidate lessons and independent
promotion decisions are retained in the paired retrospective; no large prompt
rule or new process was added.

## 7. Documentation and diagrams

README, Installation, Developer Guide, Safety and Privacy, documentation
routers, Current State, Release Status, contributor guidance, and the public
media guide were updated and verified. Historical specifications, release
notes, checklists, reports, and frozen packages were left intact. No Mermaid
diagram changed because product and engineering architecture did not change.

Still needed from a disposable account/VM with synthetic files:

- v2.4.0: Home, Search, and Review Changes;
- v2.12 candidate: Home readiness, Search facets/explanation, Related Files,
  Review Changes, and Operation History;
- optional 60–90 second captioned walkthrough: Home → scan → Search evidence →
  Related Files → Review Changes → Apply/History/Undo → AI-off setting.

The complete resolution, privacy, version-labelling, and hosting rules are in
`docs/images/README.md`.

## 8. Repository state

**Branch:** `main`

**Published content commit:** `9e0a3dd2f6d736d803453049cb0661d6479a1776`

**Working tree:** The public cleanup is committed. Pre-existing, unrelated
Security/Resource Efficiency engineering-system work remains preserved and
uncommitted.

**Commits created:** `9e0a3dd` plus the final report-only synchronization

**Push performed:** Normal pushes to `origin/main`; no force

**Schema changed:** No

**Protocol/public interface changed:** No

**Release/tag/package created:** No

## 9. Bottom line

**Status:** Complete with follow-up

The public repository now presents the product, current source, latest release,
installation, documentation, and contribution paths coherently and truthfully.
It is safe to build on, and GitHub serves the updated `main`. The remaining
presentation gap is genuine UI media; capture the listed synthetic-data images
and video in a separate manual-media task rather than fabricating them.
