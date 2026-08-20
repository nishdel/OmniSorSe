# Project-owner report — engineering-system validation

## 1. What this run was meant to do

Close the missing .NET 10 validation gap for the uncommitted OmniSorSe
engineering-infrastructure patch, challenge the patch under its own new
workflow, and incorporate only external AI-development practices that improve
this project without adding bureaucracy.

## 2. What actually changed

The patch now has executed .NET 10 evidence rather than static-review confidence
alone. SDK 10.0.400 restored and built the complete solution in Debug and
Release, and all 1,861 tests passed in each configuration with no skips. The two
new executable-memory areas—the repository documentation/configuration policy
and the terminal progress-ordering regression—passed focused tests.

No production source or product behavior changed. Three active workflow files
received narrow clarifications: behavior work names observable test seams and
non-goals; regression-first is conditional on an independent oracle; review
checks both task fidelity and repository standards; and specialist handoffs
carry only durable references plus the live delta.

## 3. Important technical decisions

**Decision:** Accept the external methodology selectively, not as a new
framework.

**Why:** Matt Pocock's current public guidance on
[discovery](https://github.com/mattpocock/skills/blob/main/docs/engineering/grill-with-docs.md),
[specification](https://github.com/mattpocock/skills/blob/main/docs/engineering/to-spec.md),
[observable-seam TDD](https://github.com/mattpocock/skills/blob/main/docs/engineering/tdd.md),
[implementation](https://github.com/mattpocock/skills/blob/main/docs/engineering/implement.md),
[two-axis review](https://github.com/mattpocock/skills/blob/main/skills/engineering/code-review/SKILL.md?plain=1),
and [handoffs](https://github.com/mattpocock/skills/blob/main/docs/productivity/handoff.md)
reinforces several historical OmniSorSe lessons, while OmniSorSe already has
stronger project-specific authority, risk, documentation, and learning controls.

**Impact:** The accepted practices live in the substantial-run Skill,
Development System, and Risk/Validation Matrix. The full external Skill chain,
mandatory grilling/specs/TDD, automatic commits, fixed review cadences, and a
universal fresh-context rule were rejected as duplicative, disproportionate, or
in conflict with owner authority. Skill A/B evaluation remains only a candidate
until OmniSorSe has local evidence.

**Decision:** Preserve the prior SDK-blocked report as historical truth and add
this separate validation record.

**Why:** Rewriting an old run report would erase the distinction between what
was known then and what was executed now.

**Impact:** Future audits can compare the original confidence gap with the
later evidence that closed it.

## 4. Validation and confidence

**Verified**

- Repository: Windows 10, SDK 10.0.400, runtime 10.0.11.
- Forced no-cache solution restore passed.
- Debug and Release builds passed with zero warnings and zero errors.
- Debug: 1,861/1,861 tests passed; Release: 1,861/1,861 passed. TRX counters
  report zero failed and zero not executed in both configurations.
- Focused gates: 14/14 repository documentation/configuration tests and the
  queued-terminal-progress regression passed.
- Whitespace, style, and analyzer verification passed with no changes.
- NuGet audit covered 18 projects and found zero direct/transitive
  vulnerabilities.
- Windows `win-x64` self-contained publish succeeded. With an absolute isolated
  data root and an explicitly waited process, the native host smoke exited 0.
- The project Skill passed the official quick validator; link, Mermaid
  structure, ADR, agent/configuration, UTF-8, path, runtime/dependency, no-skip,
  generated-root, and documentation checks passed through the 14-test policy
  suite. `git diff --check` passed.
- Independent adversarial review found no acceptance blocker and confirmed the
  remaining native-smoke and product defects are separate follow-up work.

**Not verified / manual validation required**

- Hosted CI, native Linux/macOS execution, interactive UI/accessibility,
  installer lifecycle, signing/notarization, publication, and rendered Mermaid
  appearance were not run. This work does not establish release readiness.
- The separately recorded Operation History Undo/startup-recovery
  reconciliation defect was intentionally not fixed or retested as a product
  change.

Confidence: high code-level and Windows automated confidence for this patch;
Windows package-smoke confidence for the corrected local invocation; no new
interactive, non-Windows-native, or release confidence.

## 5. Problems found

- **Fixed in execution:** sandboxed builds could not overwrite existing ignored
  outputs. The same commands passed with the approved build escalation; this
  was not a code failure.
- **Deferred and recorded:** the existing cross-platform workflow supplies a
  relative smoke data root although `PackageSmokeTest` requires an absolute
  one, and local Windows GUI-process invocation showed that `$LASTEXITCODE` is
  not trustworthy without an explicit wait. This predates the patch and was not
  changed under the run's scope restriction.
- **Fixed in the patch:** this validation initially lacked a new retained run
  record. A separate report and retrospective now preserve the evidence without
  rewriting the earlier SDK-blocked history.

## 6. What the agents learned

The scoped Documentation/Product researcher was valuable and avoided general
repository archaeology. The independent reviewer caught the evidence-record
gap and challenged native-smoke attribution. No UX, AX, Performance, or separate
Implementation agent was needed. There was no duplicated specialist search.

Candidate lessons remain controlled: repair/test the native-smoke invocation;
consider local-SDK discovery only if it recurs; and evaluate material Skill
changes behaviorally only after a useful local harness exists. None became a
new permanent invariant merely because this run observed it. The four accepted
methodology refinements were independently compared with source evidence and
OmniSorSe history before promotion.

## 7. Documentation and diagrams

Updated: `DEVELOPMENT_SYSTEM.md`, `RISK_VALIDATION_MATRIX.md`, and the
substantial-run `SKILL.md`. Created: this validation report and its controlled
retrospective. Existing current architecture documents, ADRs, glossary, and all
Mermaid structures were revalidated; no architecture changed, so no diagram
needed alteration. No stale current document was identified.

The system is plausibly more token-efficient: normal orientation is about
15 KB, the substantial-run Skill plus risk matrix adds about 10 KB, the large
router is searched by section, and archaeology stays historical-only. This run
used one scoped researcher and one reviewer with distilled handoffs. Actual
savings across future runs remain a derived expectation, not measured history.

## 8. Repository state

**Branch:** `v2.12-trusted-relationships-context`

**HEAD:** `d48bb080f5125e536978e5527c77afbb4b57da7d`

**Working tree:** uncommitted engineering-infrastructure patch; 16 modified
tracked files and 27 untracked files after this report/retrospective; none staged.
Ignored build, test, SDK, and native-smoke artifacts remain untracked.

**Commits created:** None.

**Push performed:** No.

**Schema changed:** No.

**Protocol/public interface changed:** No.

## 9. Bottom line

**Status:** Complete with follow-up

**Recommendation: ACCEPT WITH FOLLOW-UP.** The engineering-infrastructure patch
is safe to accept and build on: it has clean .NET 10 Debug/Release execution,
focused regression evidence, documentation/configuration validation, and no
product-source change. The important caveat is that the existing CI native-smoke
step can report misleading Windows evidence. The next separate task should
repair that invocation and add a regression that proves the workflow supplies
an absolute isolated root and waits for the real host exit. Do not begin major
feature work by silently relying on the current smoke result.
