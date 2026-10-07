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
The subsequent complete Debug rerun passed 1,952/1,952. Release passed all product
tests but caught a missing required ADR section while documentation was being
edited; corrected documentation gates passed. The first macOS Intel hosted run
at `6224649` then caught rejection of its standard `/var` alias. The narrow guard
fix and five new cases passed 17 focused Core storage tests on Windows; native
alias assertions still require macOS CI. The source suite now has 1,957 tests.
Final hosted source-bound evidence supersedes these intermediate local counts.

Hosted run [37366894165](https://github.com/nishdel/OmniSorSe/actions/runs/37366894165)
at `c6999de5cd87833cf33eec39b1a0c8d26b981094` passed all 1,957 tests in both
configurations on Windows, with zero skips and all formatting, policy, audit and
native package-smoke gates green. The first Ubuntu/macOS ARM jobs could not acquire
hosted runners during the GitHub Actions incident. Intel stopped reporting progress
and had no retrievable log after cancellation; its cause was not established.
The retry's macOS ARM Debug suite passed 1,956 tests and exposed one test-fixture
cleanup error: `Directory.Delete` could not remove the deliberately dangling Unix
symlink. The production refusal assertions and native system-alias cases passed.
Cleanup now uses `File.Delete` to unlink that Unix entry and retains directory-link
deletion on Windows. Final four-platform validation is still required.

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

All 24 rows in [the v3 manual checklist](MANUAL_TESTING_v3.0.md) remain
**Not run**. Real-world upgrades, keyboard/screen-reader use, subjective relevance,
and long-running personal libraries require testing after publication.

## Learned-vector continuation assistance — 2026-10-07

The same frozen runtime and unchanged routing/configuration were used for bounded
inline source review; local `gpt-oss:20b` ran through the supported `analyze_code`
tool. The installed total-input bound is 10,000 characters: an initial oversized
request was refused before inference and retried with 9,000 source characters.
Normal bridge telemetry required an approved sandbox escalation; source-root
permissions and personal configuration were not broadened.
One separate architecture/schema review escalation was rejected by automatic
approval review; it is recorded below and was not worked around. Authorization
of other bounded calls does not turn that rejected request into a contribution.

| Request | Actual request ID | Reviewed contribution and decision |
| --- | --- | --- |
| Embedding provider parser/test implementation | `8dc7e349-a235-415a-8e3e-aa5897fbcb5b` | Returned a short restatement and incomplete uncertainty text, with no requested helper or tests. Rejected. Its same-task retry returned `no_enabled_capable_model` with no attempt/new request ID; direct implementation fallback. |
| Vector chunker implementation/test proposal | `0363f401-6634-4705-9194-7c13cb785896` | Model attempt failed because Ollama was unavailable. Same-task retry returned `no_enabled_capable_model` with no attempt/new request ID. No usable implementation contribution; direct fallback. |
| Hybrid guide/Mermaid draft | `88f8840e-b669-4776-a038-a7fc101ae75c` | Initial input was rejected above 10,000 characters before inference. Bounded retry returned only a task restatement and no requested draft/diagram; rejected. |
| Vector architecture/schema review | No model request ID | Initial call failed before MCP initialization on the configured bridge-log write outside the sandbox. Automatic approval review rejected the same invocation's escalation because the configured runtime's actual destination/data handling were not established for the architecture/schema payload. No workaround, model result or contribution followed; Codex performed source analysis directly. |
| Independent-vector integration/test review | `60627b33-7609-4d47-8705-f45beaf24606` | Accepted disabled-provider and privacy-before-ranking test advice. Rejected claimed missing master-enabled early return and graph-disabled leak: actual code already guarded both. No generated patch was used. |
| Implemented RRF review | `c08555ed-1698-4399-a60a-9a2f56d8d324` | Accepted legacy path-only/stable-ID duplication finding and focused test; fixed reconciliation. Accepted snippet-preservation test advice. Rejected resetting rank per file (incorrect RRF) and claimed snippet overwrite (existing fallback preserved it). |
| Real-model benchmark review | `e877e71f-b516-43e6-8c80-b4672e64049d` | Suggested source-integrity, one-file incremental and unavailable-fallback checks were already present. Claims that those steps were absent contradicted full source/omitted excerpt sections; no novel fix was accepted. |
| Hybrid user/release documentation review | `de6c2381-9b0d-4416-86ff-449822976036` | Returned incomplete suggestions and incorrectly asserted that lack of a native vector extension prevents exact cosine scanning. Rejected against the provider's managed full-eligible-vector scan; retained the explicit sixteen-chunk coverage caveat. No usable new draft/fix was supplied. |

These are substantive local-model review contributions, not local-model-authored
implementation. Successful review advice above does not alter the failed
implementation attempts: most completed code used direct Codex fallback. The
provider request and chunker/draft requests were separately recorded as rejected
through feedback-only `triage_task` calls (`model_called=false`, one and two
feedback entries respectively). Codex authored the integration/tests/harness and independently
reviewed every recommendation. The [benchmark definition](../eng/benchmarks/VectorSearch/README.md)
declares Recall@5/MRR/exact-filename/paraphrase-gain gates and records exact model
digest, per-query latency, source integrity, restart reuse and incremental work.
Executing and passing it is a separate recorded fact; its existence alone does
not establish real-model quality, installer readiness or human acceptance.

## Continuation checkpoint — source integration pending

The earlier focused pass was **215 tests**: 63 Application, 106 SQLite and
46 Desktop. The central Windows runner subsequently passed **2,069 tests in each
of Debug and Release**, with zero failures or skips, all three formatting checks
(whitespace, style and analyzers), and the vulnerability audit. That execution
preceded three final lexical-ranking regressions. The expanded full suite then
passed **2,072 tests in each of Debug and Release**, with zero failures or skips:
1,136 Application, 332 Desktop, 289 SQLite, 116 Core, 69 Rules, 66 Executor and
64 Scanner. Both builds finished with zero warnings/errors; whitespace, style
and analyzer checks all passed. The earlier online audit found zero vulnerabilities
and dependencies were unchanged. This supersedes the 2,069-test checkpoint.
These are working-tree results, not final remote-main or release-commit evidence.

Independent review corrected production shell wiring for vector indexing controls,
stale Related Files state during rapid selection, and vector freshness after
hydration and later asynchronous work. Successful revalidation now removes known
stale IDs and path aliases; optional lookup/validation failure or timeout preserves
otherwise valid lexical results. Learned fusion uses lexical-only ranking so a
feature-hash score cannot contribute a second semantic vote. When learned results
become unavailable, the existing ranking path remains available. Targeted tests
cover these boundaries and passed within both complete local suites.

### Real-model relevance and recovery execution

The synthetic production-path benchmark retained both reports:
`.artifacts/v3-vector-benchmark-initial-failed.json` and
`.artifacts/v3-vector-benchmark.json`. Both identify the working tree as
`da5b313813fe47007913c6ef7bafb8ed15bcce92-working-tree-20261007`.
This is a base revision plus a working-tree label, not a commit containing the
tested changes. Both use Ollama `qwen3-embedding:4b`, 2,560 dimensions, digest
`df5bd2e3c74cd8d069d21dc038f1b359fcdc9458fce1c99bd43c9eb1518ff907`.

The corpus contains 12 synthetic files, seven judged queries (one exact filename,
one keyword query and five paraphrases), and one unjudged out-of-corpus diagnostic.
The predeclared criteria stayed unchanged: Recall@5 at least 0.9, MRR at least
0.7, exact filename at rank 1, at least one paraphrase gain over keyword top five,
and all integrity/recovery/fallback gates true.

| Metric | Initial execution | Execution after lexical correction |
| --- | --- | --- |
| Overall gate | Failed | Passed |
| Recall@5 | 0.857143 | 1.000000 |
| Mean reciprocal rank | 0.809524 | 0.928571 |
| Paraphrase gains over keyword top five | 0 | 1 |
| Exact filename rank | 1 | 1 |
| Reported upper-median query latency | 652.6 ms | 564.9 ms |
| Initial indexing time | 9,733.2 ms | 8,235.8 ms |
| Total harness time | 23,039.9 ms | 20,368.4 ms |

The initial failure exposed function-word matches weakening the keyword input to
learned fusion. The narrow correction filters common function words when useful
topic terms exist, retaining exact filenames, full-phrase matching, function-word-only
queries and the compatibility fallback. No corpus, ground truth or pass criterion
was relaxed. For `fix problems with my car`, the judged file moved from hybrid
rank 6 to rank 2 and remained absent from keyword top five.

| Judged query | Final relevant rank | Query latency |
| --- | --- | --- |
| `invoice-2026-104.txt` | 1 | 615.4 ms |
| `sourdough bread` | 1 | 575.4 ms |
| `sleep outside away from home` | 1 | 636.8 ms |
| `manage the money our family spends` | 1 | 536.3 ms |
| `fix problems with my car` | 2 | 521.1 ms |
| `learn to play a keyboard instrument` | 1 | 525.6 ms |
| `raise edible plants in the backyard` | 1 | 564.9 ms |

Both reports passed unchanged source hashes/creation/write times, reopening with
vector reuse, one-file incremental refresh with exactly one embedding call,
unavailable-model lexical fallback, and nonempty Related Files suggestions labeled
as similarity rather than verified relationships. The out-of-corpus query took
538.3 ms in the passing run and has no assigned relevance judgment. Timing covers
one measured query per case after indexing, including local HTTP, embeddings, SQL
and fusion. The reported latency selects the upper middle value across all eight
queries. This small synthetic result is not a large-library performance claim
or proof of subjective relevance on personal files.

### Native smoke and incomplete deliverables

The rebuilt self-contained Windows application passed its production
`--package-smoke-test` startup/shutdown with an isolated profile; the runner retained
`.artifacts/v3-native-smoke.log`. It uses the working-tree identity above, including
the final lexical correction. This proves native startup/shutdown, not installer
lifecycle, interactive behavior, screenshot capture or final commit identity.

Required real v3 screenshots remain an **incomplete deliverable and explicit
blocker to completing the requested evidence**. The initial computer-use permission
wait timed out, then the user confirmed readiness. Two native captures timed out;
two further retries with a visible rebuilt window hit the same timeout. A final
self-contained launch through the computer-use launcher triggered a new app-access
approval that timed out. No real screenshot has been captured. Older images, mockups and synthetic UI renderings
cannot fill this gap. All 24 human acceptance rows remain Not run.

Exact-source hosted validation, native platform/package checks, remote-main
identity, tag resolution, checksums and non-draft prerelease publication still
require their own observed evidence. The official
[v3 release page](https://github.com/nishdel/OmniSorSe/releases/tag/v3.0.0-rc.1)
and [issue #53](https://github.com/nishdel/OmniSorSe/issues/53) own final identity
and availability when the release is published. This checkpoint does not assign
a final release commit or direct testing of an unavailable installer.
