# OmniSorSe v2.12 Trusted Relationships & Context

**Status:** Unreleased implementation candidate on `v2.12-trusted-relationships-context`.

v2.12 extends the existing schema-6 relationship authority. It does not add a
graph database, clustering subsystem, embedding store, AI relationship provider,
or new file-mutation path. The optional Knowledge Graph remains a derived
projection; direct Related Files works when that feature is disabled.

## Authority and scoring

Automatic relationships retain bounded evidence in independent families:

| Family | Maximum score |
| --- | ---: |
| Identity | 8 |
| Content fingerprint | 5 |
| Named context | 5 |
| Filename / lexical | 3 |
| Tag authority | 3 |
| Structural / media / temporal | 2 |
| Semantic corroboration | 3 |

Correlated content derivatives share one family cap. Semantic and AI-derived
evidence cannot qualify a pair by themselves. Ordinary automatic relationships
require a score of at least 6 from at least two independent non-structural
families. Exact full-content identity and an exact retained media-text
fingerprint remain strong single-family exceptions. Two distinct, specific,
deterministic topic matches retain the established low-confidence relationship
contract without allowing generic or AI-derived topic stacking. High confidence
requires exact content or at least score 9 with multiple independent families.
These are deterministic bands, not statistical probabilities, so the UI does
not display percentages.

Explicit pair authority always wins:

- **Related** stores positive `AlwaysRelate` authority;
- **Not Related** stores negative `NeverRelate` authority and hides automatic
  output while remaining visible in the current file's corrections;
- **Use automatic result** removes pair authority and reruns bounded analysis.

Legacy Confirmed/Rejected records retain their meaning. Multiple typed edges may
remain in SQLite, but presentation and Explorer output aggregate them into one
target with a deterministic primary type and bounded combined evidence.

## Candidate and reanalysis bounds

Candidate selection uses indexed buckets: up to 128 exact fingerprints, 192
shared tag/topic/entity/keyword candidates, 96 exact normalized stems, and 96
source-scoped structural/media/time candidates. Candidates are deduplicated and
ordered by bucket, overlap, cheap indexed rarity, then stable ID. Normal analysis
defaults to 256 candidates and never exceeds 512.

Current-version candidates are batch-loaded through compact persisted features;
large extracted/OCR/media text is not hydrated for every candidate. Stale legacy
feature rows fall back to one bounded batch so version-3 features can be created.
Relationship algorithm version `3.0.0` is independent of extraction versions.
Startup reanalysis processes restartable 64-file batches with cancellation and
does not rerun OCR, transcription, probing, or extraction merely because the
relationship policy changed.

## Product integration

Related Files is a normal provider-neutral surface. Search and Files can open it
for one stable file identity. Rows show a confidence band, primary and
contributing relationship types, a concise reason, evidence classification, and
pair authority. Literal exact filename, exact stem, and prefix ranking remain
above relationship-only Search expansion.

Explorer Protocol remains **1.0**. `GetRelated` returns bounded authorized opaque
nodes, one edge per target pair, stable ordering, concise reason, and the existing
deterministic/derived provenance class. It exposes no path beyond the existing
authorization setting and adds no mutation command. OmniBrille remains separate.

## Backup, privacy, and recovery

The `.oms-state` logical payload is format **2**; schema remains **6**. The reader
still accepts exact format-1 archives. Importing format 1 cannot clear newer
Smart Collection authority merely because that category did not exist in the
older payload. Format 2 adds pair decisions/manual links
and user-authored Smart Collection authority: manual/merged collections,
renames, pin state, manual membership, exclusions/splits, and intentional
automatic-collection tombstones. Restore uses exact stable IDs, reports
unresolved items, never guesses by filename or path, and keeps the existing
pre-restore recovery point.

Generated edges, evidence, automatic membership, graph projections, diagnostics,
and reanalysis jobs are excluded because they are rebuildable. Forget File and
Forget Source remove relationship features, edges, pair overrides, memberships,
and file-scoped collection overrides through the schema-6 deletion coordinator
and foreign-key lifecycle. Clearing generated relationship intelligence preserves
explicit pair and collection authority. Health exposes aggregate stale-version,
invalid-authority, repair-needed, and last-enrichment state without names,
paths, tags, entities, or evidence.

## Preserved boundaries

- database schema 6;
- Explorer Protocol 1.0;
- .NET 10 LTS;
- Smart Collections as the only grouping authority;
- optional, unchanged Ollama behavior;
- optional, derived Knowledge Graph;
- Change Plan review, journal, reconciliation, and Undo as the only mutation
  boundary;
- no production dependency increase.

See [v2.12 manual validation](MANUAL_TESTING_v2.12.md), [release notes](RELEASE_NOTES_v2.12.0.md),
and the inherited [v2.10 master matrix](MANUAL_TESTING_v2.10.md).
