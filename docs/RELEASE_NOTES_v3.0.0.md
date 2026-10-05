# OmniSorSe v3.0.0-rc.1 — Progressive understanding and reviewed organization

**Release candidate for user testing, not a stable release.** The matching
[GitHub release](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
owns exact package, source commit and checksum evidence.

The milestone adds optional background local-AI enrichment to folder indexing,
per-folder policy and retained-content requeue, automatically searchable inferred
document information with provenance, editable Organize previews with three
strategies and remembered preferences, and application storage location/usage
management. Original document contents and embedded metadata remain untouched.

See [How OmniSorSe Works](HOW_OMNISORSE_WORKS.md), the complete
[38-item acceptance map](V3_ACCEPTANCE.md), and the
[manual acceptance checklist](MANUAL_TESTING_v3.0.md).

## Compatibility and recovery

- Stable OpenSorSe assembly/profile/package identifiers are retained. The
  Explorer read-only protocol remains 1.0.
- Durable index schema 7 adds per-source AI policy. Existing source libraries
  default to standard indexing until explicitly enabled. Migration uses the
  existing provider's backup and transaction path; user authority stays in the
  same durable store.
- AI inference is distinct from extracted facts and user decisions. AI does
  not gain file-operation authority and does not override accepted/rejected tags.
- Changing storage location applies on restart under the original profile lock.
  The app verifies copied registered data/cache files, then atomically switches
  its location receipt. Earlier generations remain recovery copies. Do not
  delete them until independently backed up and verified. Keep the active drive
  available; startup must fail rather than silently open an empty library.
- Configuration, operation history and profile lock retain their original
  location. The logical `.oms-state` archive has existing exclusions and is not a
  complete substitute for a closed-profile filesystem backup. In particular,
  scoped Organize preferences in `decision-history.json` are not included in
  the logical archive; preserve the complete profile and active data location.
- Older applications do not understand schema 7 or the new active-location
  receipt. Downgrade in place is unsupported. To recover an old version, close
  all instances and restore an independently preserved, complete pre-upgrade
  profile/location; do not merge old/new SQLite files or journals.

## Explicit limitations

Local AI and document interpretation must be enabled and an installed model
selected. Enrichment validates structure, not factual correctness. A metadata-only
record needs deterministic extraction before useful AI enrichment. OCR depends on
its configured local engine and applicable PDF renderer. Alternative OCR engines
and richer media understanding remain extension/future work.

Human acceptance, keyboard/accessibility, real-world profile upgrade and
subjective relevance checks remain Not run until a tester records observations.
Windows packages are expected to be unsigned; macOS packages are expected to be
publisher-unsigned and unnotarized. Native package smoke and CI evidence must be
recorded before publication and never substituted for human acceptance.
