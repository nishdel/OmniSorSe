# Project-owner report — macOS hosted-test correction

## 1. What this run was meant to do

Diagnose and correct the three tests that failed on both macOS hosts after the
v2.12 candidate source was published, without expanding product behavior or
the supported macOS filesystem-mutation boundary.

## 2. What actually changed

The optional OmniBrille bootstrap handoff now uses the compact opaque name
`obh-` plus the existing 32-hex launch ID. Its random identity remains 128 bits,
while the 36-character logical name stays within the stricter Unix-domain
socket path budget used by .NET named pipes on macOS.

The reviewed-organization execute/Undo test now supplies explicit supported
test capabilities. It therefore tests the Change Plan behavior consistently on
every host without pretending that production file mutation is supported on
macOS. Production capability policy, user workflows, and product behavior did
not otherwise change.

## 3. Important technical decisions

**Decision:** Shorten only the opaque handoff prefix and retain the full random
launch ID.

**Why:** The prior logical pipe name made the complete Unix socket path exceed
the host limit. Reducing entropy or changing the handoff contract was
unnecessary.

**Impact:** The same one-time scoped handoff works within the portable path
budget while retaining its 128-bit rendezvous identity.

**Decision:** Make the domain test's supported capabilities explicit rather
than changing production platform detection.

**Why:** The test promises execute/Undo behavior under supported capabilities;
the macOS product policy deliberately fails closed for source-file mutation.

**Impact:** Cross-platform tests are deterministic, and macOS production
mutation remains unavailable.

## 4. Validation and confidence

**Verified**

- Correction commit `d81f15482f20d674f77b0aba51ccd00896fee36e`
  passed a local .NET 10 no-cache restore, zero-warning Debug and Release
  builds, and 1,861/1,861 tests in each configuration with zero failed or not
  executed.
- Focused affected tests passed 30/30; neighboring Change Plan/platform tests
  passed 45/45.
- Fourteen documentation tests, formatting/analyzers, repository Skill
  validation, the 18-project dependency-vulnerability audit, `git diff
  --check`, and `git fsck` passed locally.
- [Pull request #35's run 32373697544](https://github.com/nishdel/OmniSorSe/actions/runs/32373697544)
  passed on macOS ARM, macOS Intel, Windows, and Ubuntu. Each host passed Debug
  and Release with 1,861 tests and no skips, plus formatting/analyzers,
  documentation/dependency policy, advisory audit, runtime restore, and native
  package smoke.
- Pull request #35 merged normally as
  `542e14a50885523543e80c9f593bb35a5f7ef844`. The
  [exact-main run 32375495795](https://github.com/nishdel/OmniSorSe/actions/runs/32375495795)
  passed the same complete matrix on all four hosts.
- The final diff preserves the 128-bit launch identity, keeps production macOS
  mutation unavailable, and changes no schema, Explorer Protocol contract, or
  public interface.
- The final documentation synchronization passed the compiled 14/14
  repository documentation/policy tests and whitespace validation. An
  independent read-only scan checked 366 active Markdown files as strict UTF-8
  and resolved their relative links with zero issues; no Mermaid source changed.

**Not verified / manual validation required**

- Interactive OmniBrille launch/handoff on macOS, broader desktop and
  accessibility behavior, and real-filesystem mutation behavior outside the
  deliberately unavailable macOS production boundary.
- Installer lifecycle, signing, notarization, release artifacts, tagging, and
  GitHub Release publication. v2.4.0 remains the latest release.

## 5. Problems found

- **Fixed:** the long opaque handoff name could exceed macOS's complete Unix
  socket path limit, failing both the single-use and separate-process tests.
- **Fixed:** the execute/Undo domain test inherited the production host's
  capabilities even though its assertion required a supported mutation host.
- **Unchanged:** the separately recorded Operation History Undo/startup-
  recovery reconciliation defect was outside this task.

## 6. What the agents learned

Lead, Implementation, Documentation, and independent validation were the useful
roles. Product, UX, AX, and Performance specialists were unnecessary because
the product contract and scope did not change.

Independent review accepted the scoped Unix-endpoint lesson: account for the
complete socket path, keep the logical identifier compact, retain established
random entropy, and validate on native supported hosts. Its promoted form is
the focused length/entropy, single-use, separate-process, and native-host test
coverage. The capability-fixture observation was merged into the existing H3
cross-platform fixture/policy lesson, backed by the explicit fixture and
separate production-policy tests. Neither required a new global prompt rule.

## 7. Documentation and diagrams

Current State, Release Status, Product Roadmap, and Release History now record
the successful correction and exact hosted evidence. The earlier source-
publication report and retrospective remain unchanged historical snapshots.
No authority, topology, protocol, terminology, or user flow changed, so no ADR,
glossary entry, architecture document, or Mermaid diagram required an update.

## 8. Repository state

**Branch:** `main`

**Validated implementation HEAD:**
`542e14a50885523543e80c9f593bb35a5f7ef844`; the evidence-only documentation
commit containing this report is the later final `main` HEAD

**Working tree:** Clean after the evidence-only documentation commit; local
validation outputs remain ignored

**Commits created:** `d81f154` (correction), `542e14a` (normal PR merge), and the
evidence-only documentation commit containing this report

**Push performed:** Yes for the correction branch, normal PR merge, and final
documentation synchronization; no force, history rewrite, tag, package, or
GitHub Release

**Schema changed:** No

**Protocol/public interface changed:** No

## 9. Bottom line

**Status:** Complete

The hosted macOS regression objective is complete and the correction is safe to
build on: local validation, the pull-request matrix, and an independent exact-
main matrix all passed. The changes preserve handoff entropy and the
conservative macOS mutation boundary. This closes the automated follow-up but
does not make v2.12 a release. The recommended next separate task is the
recorded Operation History Undo/startup-recovery reconciliation investigation
and fix; it was not started in this run.
