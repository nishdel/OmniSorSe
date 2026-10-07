# ADR-007: Automatic indexed inference with separate mutation approval

| Field | Value |
| --- | --- |
| Status | Accepted for v3.0.0-rc.1 |
| Date | 2026-10-05 |
| Decision | Incorporate validated indexed inference automatically, retaining explicit mutation approval |

## Context

This extends ADR-001's narrow review-proposal scope; its provider boundary and
source-mutation prohibition remain. Progressive library understanding needs a
different approval boundary from filesystem proposals.

## Decision

Optional local AI runs after deterministic library coverage. Structurally validated
document type, category, tags, topics, entities and summary are automatically
incorporated into OmniSorSe-owned indexed records with AI provenance. Requiring a
review click for each inferred term would prevent progressive library enrichment.
Validation establishes safe structure and bounds, not factual correctness.

The existing `IIndexingEnrichmentProvider`, stage processor, durable SQLite queue,
per-file FTS/semantic refresh and relationship/graph projection remain the owners.
Schema 7 stores a source's opt-in policy. Enabling it queues retained extraction;
new discoveries inherit it. Provider requests require global/capability permission,
the exact configured local model, bounded text, timeouts and cancellation. Privacy
suppression applies to retained evidence as well as initial extraction.

No source contents or embedded metadata are written. Inferred information does
not replace accepted/rejected user authority. File moves and renames still enter
the existing proposal, review, safety validation, explicit approval, executor,
journal and History/Undo workflow. Document/model output cannot execute it.

## Consequences

Users gain inferred concepts in Search and Related Files as individual files finish.
Missing dependencies can leave enrichment waiting while deterministic coverage
remains useful. AI-derived labels prevent structural acceptance being mistaken for
source-grounded truth. Exact model semantics and subjective relevance still need
human evaluation.

## Alternatives considered

Automatic file writeback and a parallel AI metadata database were rejected because
they would violate source ownership or duplicate durable authority. Requiring
per-tag review would prevent the agreed incremental enrichment workflow.

`IndexingEnrichmentTests`, `DeepIndexingStageTests`, retained-content queue tests,
and `RetainedEnrichmentRefreshesRelationshipsAndGraphProjectionAfterRestart` cover
validation, source preservation, privacy, incremental Search and retained graph
integration. The actual-provider smoke and its limits are in
[v3 validation](../../VALIDATION_v3.0.0.md).
