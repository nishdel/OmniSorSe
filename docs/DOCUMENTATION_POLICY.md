# Documentation policy

One subject has one canonical living document. Update that document for each
release; add a version heading, ledger row or collapsible historical section
instead of a version-suffixed sibling. Keep independent subjects separate.

## Authority and evidence

- Source and tests define implemented behavior. [Current State](CURRENT-STATE.md)
  owns volatile source/runtime/schema facts.
- [Changelog](CHANGELOG.md) is the cumulative user-visible release record.
  [Release History](../RELEASE_HISTORY.md) separately records branch, integration
  and publication chronology. [Release Status](RELEASE_STATUS.md) owns readiness.
- [Manual testing](MANUAL_TESTING.md) owns procedures and historical observations;
  the release-testing issue owns live execution. Preserve planned, observed,
  automated, native, interactive, failed, blocked and unrecorded states separately.
- Git history, tags, GitHub releases and CI artifacts are the immutable sources
  for exact snapshots. Published releases own exact assets and checksums.
- Name living files for their subject without a version number. Prefer a stable
  explicit anchor when a section is a public reference. Update links and tooling
  before deleting a replaced path; verify content parity and fragments first.

## Explicit archival exceptions

Versioned documentation requires an explicit documented exception, reviewed
with its content, authority boundary, canonical destination and retention reason.
No active manual-testing or release-note sibling family is exempt.

| Scope | Exact reason and authority boundary |
| --- | --- |
| `release/OpenSorSe-v1.0.0/` | Frozen distributed package, including its documentation and legal notices; byte-preserved redistribution evidence. It does not compete with current guidance. |
| `docs/Implementation_Spec/` | Numbered accepted specifications, proposal/decision packages and original coding prompts retain their identifiers and version directories. They preserve distinct architectural intent and acceptance context, including rejected/planned behavior; the architecture index owns living guidance. |
| Dated records below `docs/engineering/reports/` and `docs/engineering/retrospectives/`, plus `docs/engineering/RETROSPECTIVE_v3.0.0.md` | Individual engineering events retain exact failures, interventions and agent provenance. The v3 retrospective is an archived event record, not reusable guidance. |
| `docs/release-testing/` package-bound prepared issue bodies | Each is a distinct release/package-specific execution contract, with pinned definitions and historical unknowns. These are preparation records, never a second current results ledger or a template for new versioned guide files. |

The [migration register](engineering/documentation-migration.tsv) lists every
original Markdown path, destination/anchor and disposition, including individual
archival records. The [consolidation report](engineering/reports/2026-10-11-documentation-consolidation.md)
records review, parity and external-link decisions. There are **no compatibility
stubs** for this migration: existing pinned links survive in Git, and relative
published release links are corrected to canonical destinations at a pinned
consolidation commit.

## Validation

Use the existing `RepositoryDocumentationTests` filter in the Core test project
and `git diff --check`. The documentation filename guard rejects new repetitive
manual-testing/release-note families and version-suffixed living guides outside
the explicit archival scopes. A new exception needs both this policy and the
reviewed guard updated; a filename alone is never a retention reason.

Documentation-only changes use the documentation row of the
[risk and validation matrix](engineering/RISK_VALIDATION_MATRIX.md); do not claim
product tests, native execution, or human acceptance from documentation checks.
