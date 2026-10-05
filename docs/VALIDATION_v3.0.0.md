# v3.0.0-rc.1 validation evidence

This report separates executed checks from release work and human acceptance.
Implementation baseline: `727ce2d09ce9870f6e6ce9e4c3baa467c7e4d5de` on remote
main. Implementation branch: `codex/v3-progressive-enrichment`. The existing
dirty older checkout was preserved; work used a separate worktree.

## Local automated evidence — Windows, 2026-10-05

.NET SDK 10.0.400; Avalonia telemetry disabled, matching CI. Both configurations
use the repository's pinned dependency graph. The complete Debug suite passed
1,946 tests with zero failures or skips and a zero-warning/error build. Release
also passed all 1,946 with no skips and a zero-warning/error build. Final added
integration regressions passed in both configurations: retained relationship and
graph projection after restart; inference summary/entity labels; and persistent
restart notices for storage/logging/workers. The complete Settings test class
(37 tests), enrichment class (14), relationship integration (1) and documentation
gates (14) passed. These additions bring the source suite to 1,952 tests; complete
hosted suites remain the final source-bound gate. The online direct/transitive
NuGet vulnerability audit returned no vulnerable packages.
Whitespace, code-style and analyzer verification passed. A self-contained native
Windows publish of implementation commit `6463344` passed the isolated production
`--package-smoke-test` entry point. This was not an installer or interactive test.
Final hosted source-bound evidence supersedes these pre-commit local counts.

Coverage includes existing extraction/OCR contracts, stage ordering and queue
lifecycle, schema migration, retained-library requeue, inherited source policy,
hostile model output, privacy suppression, per-file FTS updates, user decision
preservation, relationship provenance, editable Organize safety/Apply/Undo,
storage migration and durable-state retention under reclamation. Automated
ViewModel tests do not establish visual usability or human acceptance.

Independent source reviews crossed implementation ownership:

- Indexing/desktop review caught suppressed OCR reaching enrichment consumers;
  processor and semantic/classification consumers now honor suppression.
- Organize review caught FIFO history trimming learned folder preferences;
  scoped records are protected and bounded writes fail rather than erase them.
- Storage review caught loss of an active-location receipt reopening stale
  defaults; a permanent guard and incomplete-state checks now fail closed.
- Source/bootstrap and current documentation received separate read-only review.

## Actual local-AI execution

An isolated smoke used the installed `qwen3:4b` model and the production
`OllamaIndexingEnrichmentProvider`, strict validator, staged SQLite store and
Search. It retained an inferred electricity-bill/finance record for synthetic
account text, found inferred concepts, reopened the store successfully, and
verified unchanged source bytes and modification time. This did not change the
personal application profile or development-router configuration.

Evidence was captured in the ignored run directory
`.artifacts/real-ai-smoke/bin/Debug/net10.0/evidence/20261005-191714/`:
`validated-enrichment.json`, `statement.txt`, and `deep-index.db`. This proves
one actual configured model path, not arbitrary-model factual accuracy, native
OCR extraction, installed GUI interaction, or broad relationship relevance.
Relationship/graph integration has separate deterministic regression coverage.

## OmniLAB development contribution and fallbacks

The existing interpreter imported an editable development checkout. A hashed,
frozen module snapshot was used outside that checkout; the runtime stayed fixed.
An actual stdio MCP connection enumerated seven baseline tools. Existing routing
selected `gpt-oss:20b` with its Large cap. No personal config or model was replaced.
The configured source root excluded this repository, so bounded inline excerpts
were supplied. No OmniLAB dependency was added to the product.

| Request | Actual request ID | Observed result and decision |
| --- | --- | --- |
| Enrichment parser implementation | `2f63bb0c-07a9-4059-8786-0a29d15218a7` | Timed out after 209.7 seconds; handoff, no usable contribution. |
| Retained-content queue implementation | `c68457e2-47f1-40ed-b5ad-62e120a69bd1` | Truncated generic description, no requested implementation/tests; rejected. |
| Organize service implementation | `c1e42c8e-6f6c-412f-a6c7-509d35e81e8f` | Useful filename/root safety and strategy-test advice verified and corrected; unchanged executable rows suggestion rejected. |
| Editable tree implementation | `1d6f0d57-812e-4abc-931d-e24c5e4f5e12` | No actionable tree code; router handoff `no_enabled_capable_model`; rejected. |
| Retention SQL and regressions | `2894b651-8dcb-49fd-b9ce-f1bddbf1d456` | Cascade-loss diagnosis verified; no usable SQL/tests; direct implementation fallback. |
| Storage migration and tests | `aaae19f9-7127-444e-a26f-a945a9812877` | Hash/reparse/partial-recovery suggestions useful; invented migration cap rejected. |
| Scoped preferences implementation | `09e341a8-94ae-437e-aed3-20bedfa7c1ae` | No usable output and routing handoff; rejected. |
| Desktop enrichment wiring | `3a29a9dc-d396-443d-85e4-a57ea72c4013` | No requested implementation, only unsupported no-defect assertion; rejected. |
| Indexing regression implementation | `586bf6b1-aa19-43b4-8197-163b6162f892` | Truncated fragments and spurious strings, no usable test; rejected. |

These were substantive implementation requests, not token reviews. Their actual
availability/output failures required direct Codex implementation. A majority of
finished code was **not** produced by local models. Eight completed request
outcomes were recorded through feedback-only triage (`model_called=false`);
the timeout was excluded. Original request/result and runtime-manifest files
remain under ignored `.artifacts/omnilab/` for local audit.

## Remaining release and human evidence

Hosted four-platform validation, exact-main native packaging, asset identity,
tag/publication and checksums remain release gates until their successful runs
are linked in [Release Status](RELEASE_STATUS.md) and the release page. A passing
cross-target build is not native execution. Signing/notarization are disclosed
separately and never inferred from a package smoke.

All eighteen rows in [the v3 manual checklist](MANUAL_TESTING_v3.0.md) remain
**Not run**. Real-world upgrades, keyboard/screen-reader use, subjective relevance,
and long-running personal libraries require testing after publication.
