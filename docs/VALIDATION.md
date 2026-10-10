# Validation methodology and evidence

Follow the [risk and validation matrix](engineering/RISK_VALIDATION_MATRIX.md): run focused documentation checks for documentation changes, and the required build/test, native, manual and packaging gates for changes that affect them. Record exact source, command, host, result, failures/retries and exclusions. Automated, native, interactive and publication evidence are separate. [Manual testing](MANUAL_TESTING.md) owns human procedures; [Release Status](RELEASE_STATUS.md) owns current readiness. Historical counts below are not current suite totals.

## Chapter index

- [v3.0.0](#validation-v3-0-0): Local automated evidence — Windows, 2026-10-05; Actual local-AI execution; OmniLAB development contribution and fallbacks; Remaining release and human evidence; Learned-vector continuation assistance — 2026-10-07; Continuation checkpoint — source integration pending.
- [v2.0](#validation-v2-0): Base and scope; Clean local validation; Crash investigation; Runtime-target compilation; Stability evidence added during recovery; Repository and privacy audit.
- [v1.9](#validation-v1-9): Scope and evidence policy; Automated evidence; Manual validation.
- [v1.8](#validation-v1-8): Evidence policy; Repository basis; Automated evidence; Manual validation.
- [v1.7](#validation-v1-7): Evidence policy; Repository basis; Final local evidence; Hosted native validation; Manual validation.
- [v1.6](#validation-v1-6): Evidence policy; Baseline evidence; Power-loss recovery audit; Final local evidence; Hosted native validation; Manual validation.

## Version history


<a id="validation-v3-0-0"></a>
## v3.0.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/VALIDATION_v3.0.0.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

This report separates executed checks from release work and human acceptance.
Implementation baseline: `727ce2d09ce9870f6e6ce9e4c3baa467c7e4d5de` on remote
main. Implementation branch: `codex/v3-progressive-enrichment`. The existing
dirty older checkout was preserved; work used a separate worktree.

<a id="validation-v3-0-0-local-automated-evidence--windows-2026-10-05"></a>
#### Local automated evidence — Windows, 2026-10-05

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

<a id="validation-v3-0-0-actual-local-ai-execution"></a>
#### Actual local-AI execution

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

<a id="validation-v3-0-0-omnilab-development-contribution-and-fallbacks"></a>
#### OmniLAB development contribution and fallbacks

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

<a id="validation-v3-0-0-remaining-release-and-human-evidence"></a>
#### Remaining release and human evidence

Hosted four-platform validation, exact-main native packaging, asset identity,
tag/publication and checksums remain release gates until their successful runs
are linked in [Release Status](RELEASE_STATUS.md) and the release page. A passing
cross-target build is not native execution. Signing/notarization are disclosed
separately and never inferred from a package smoke.

All 24 rows in [the v3 manual checklist](MANUAL_TESTING.md#manual-v3-0) remain
**Not run**. Real-world upgrades, keyboard/screen-reader use, subjective relevance,
and long-running personal libraries require testing after publication.

<a id="validation-v3-0-0-learned-vector-continuation-assistance--2026-10-07"></a>
#### Learned-vector continuation assistance — 2026-10-07

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

<a id="validation-v3-0-0-continuation-checkpoint--source-integration-pending"></a>
#### Continuation checkpoint — source integration pending

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

<a id="validation-v3-0-0-real-model-relevance-and-recovery-execution"></a>
##### Real-model relevance and recovery execution

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

<a id="validation-v3-0-0-native-smoke-and-incomplete-deliverables"></a>
##### Native smoke and incomplete deliverables

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

</details>

<a id="validation-v2-0"></a>
## v2.0

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V2.0_VALIDATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

**Release:** Knowledge Graph

**Branch:** `v2.0-knowledge-graph`

**Report state:** final local/native Windows validation and exact source-tip
Windows, Ubuntu, and macOS validation complete; integrated into `main` by an
explicit history-preserving release merge on 2026-08-11

This report records automated evidence for the implementation source reviewed
before and during main integration. It does not mark an interactive scenario
complete. Native release artifacts, checksums, tag, publication, signing, and
notarization are verified through their GitHub release records rather than
being inferred by this source document. Broad manual/community testing begins
with publication.

<a id="validation-v2-0-base-and-scope"></a>
#### Base and scope

- Exact design base: `a2a9a071600de74759937f05a7be61f85e9d5d93`
- Verified v1.9 source commit:
  `7fbc9b47ebf2cd1209c177388aceffe1cb4cec16`
- v1.9 automated baseline: 1,128 passing tests in both configurations
- Required branch: `v2.0-knowledge-graph`
- Required compatibility: `deep-index.db` schema 3 plus sidecar schema 1
- Manual validation: pending; no scenario is marked complete
- RC stabilization: pending; no RC or release gate is marked complete

The copied development repository had a corrupt Git object database. A full
`git fsck` identified object
`c3832dd5d22f4d9c5f4b49eb4095cd0a9a34eb01` as corrupt, so its Git metadata was
not trusted. A fresh clone from the authoritative remote passed `git fsck
--full`; the previously inventoried 64 tracked modifications and 71 intended
untracked implementation files were compared and recovered onto the exact
design commit. Eleven unrelated or generated copied changes were excluded. The
damaged copy remains preserved as recovery evidence.

<a id="validation-v2-0-clean-local-validation"></a>
#### Clean local validation

All commands below ran against the recovered clean clone on Windows 11 with
.NET SDK 9.0.316 (the repository permits latest-feature roll-forward from
9.0.315). Generated results, target output, and package staging used the
repository's ignored `.artifacts/` boundary and were not added to source
history.

| Gate | Result |
| --- | --- |
| Forced restore | Passed with `--force --no-cache` for the complete solution |
| Debug build | Passed, non-incremental, 0 warnings, 0 errors |
| Debug tests | 1,486 passed, 0 failed, 0 skipped |
| Release build | Passed, non-incremental, 0 warnings, 0 errors |
| Release tests | 1,486 passed, 0 failed, 0 skipped |
| Search relevance regression | 1 passed, 0 failed, 0 skipped |
| Application Knowledge Graph regression | 225 passed, 0 failed, 0 skipped |
| SQLite Knowledge Graph regression | 94 passed, 0 failed, 0 skipped |
| Desktop Knowledge Graph regression | 27 passed, 0 failed, 0 skipped |
| Relationship/collection regressions | 60 passed, 0 failed, 0 skipped across Application, SQLite, and Desktop |
| Indexing regressions | 106 passed, 0 failed, 0 skipped across Application and SQLite |
| Recovery/migration/concurrency/cancellation regressions | 71 passed, 0 failed, 0 skipped across Application and SQLite |
| Application performance regressions | 9 passed, 0 failed, 0 skipped |
| SQLite performance regressions | 5 passed, 0 failed, 0 skipped |
| Whitespace formatting | Passed |
| Code-style formatting | Passed |
| Analyzer formatting/static analyzers | Passed |
| Documentation/dependency/architecture/XML-doc policy | 8 passed |
| Vulnerability audit | No vulnerable direct or transitive packages found |
| Windows portable ZIP | Built self-contained; version and payload audit passed |
| Windows installer | Native install/start/stop/uninstall passed; shortcut and uninstall entry removed; test user data preserved |
| Package trust | Windows output verified unsigned; no repository/GitHub signing credentials or signing automation exists |
| `git diff --check` | Passed |

Debug and Release totals were independently parsed from one TRX file for each
of the seven test projects. The final 1,486-test total exceeds the verified
1,128-test v1.9 baseline; no test was deleted, ignored, or skipped.

<a id="validation-v2-0-crash-investigation"></a>
#### Crash investigation

The reported Windows dialog identified `dotnet.exe` and managed exception code
`0xe0434352`, but no timestamp, process ID, command line, managed exception,
stack trace, or faulting OpenSorSe operation was available. Windows Application
events, .NET Runtime/Application Error events, Windows Error Reporting and
crash-dump locations, reliability history, OpenSorSe logs, and retained
diagnostics contained no correlating OpenSorSe event. Unrelated native Ollama
worker faults were present but do not establish an OpenSorSe process failure.
The observed dialog therefore cannot responsibly be attributed to OpenSorSe.

The review still hardened startup rollback, bounded shutdown, graph/runtime
cleanup, owned indexing tasks, native picker/export/navigation event
boundaries, and fallback diagnostics. A clean full-suite run then exposed and
fixed a real cancellation/disposal race: shutdown could retain a concurrent
snapshot of a stage cancellation source immediately after its worker disposed
it. Cancellation requests now treat disposal winning that narrow race as
completed shutdown while the owned task completion remains the synchronization
boundary. The exact paused-run restart regression and both complete suites pass.

Exact-tip hosted validation then exposed two test-harness portability and
scheduling assumptions before integration. The package composition test now
checks the platform-specific application-owned directories instead of a
Windows-only directory name. The fast-build heartbeat regression now
coordinates its synthetic builder with the test scheduler, preserving the
exact 24-wait assertion while proving every wait is cancelled and observed and
none remains active. No retry or arbitrary delay was introduced.

The following hosted run also exposed a SQLite lock-budget defect on macOS.
Ordinary graph writers were reissuing the persistent WAL journal-mode pragma,
so a competing writer could consume one five-second busy deadline during
connection setup and a second during the real transaction. WAL mode is now
configured once at schema initialization. A subsequent exact-tip run proved
that separate setup and transaction commands could still consume more than one
native wait on macOS. Ordinary writers therefore use short native busy slices
inside one managed five-second operation budget spanning connection setup,
`BEGIN`, statements, and `COMMIT`; cancellation also interrupts the active
slice. Short-lived writers retain foreign-key enforcement and
`synchronous=FULL` without repeating the persistent mode transition. The
existing locked-writer classification, prompt-cancellation, and recovery tests
are the regression gates.

The ensuing complete Release suite found the same ownership smell in the
inherited schema-3 deep-index provider: a relationship-repair fixture could
finish assertions while a process-wide SQLite pool still retained its database
file. Deep-index operations are already serialized by a store-owned gate, so
they now use short-lived unpooled connections; initialization and index-only
reset establish WAL once, and disposal/reset no longer clear unrelated global
pools. The synthetic fixture is likewise unpooled. Schema, WAL, repair,
performance, and deterministic file-release coverage validate the boundary.

<a id="validation-v2-0-runtime-target-compilation"></a>
#### Runtime-target compilation

Fresh runtime-specific restores and non-incremental framework-dependent Release
builds completed for the Desktop project:

| Runtime | Build | Warnings | Errors | Native SQLite asset |
| --- | --- | ---: | ---: | --- |
| `win-x64` | Passed | 0 | 0 | one `e_sqlite3.dll` |
| `linux-x64` | Passed | 0 | 0 | one `libe_sqlite3.so` |
| `osx-x64` | Passed | 0 | 0 | one `libe_sqlite3.dylib` |
| `osx-arm64` | Passed | 0 | 0 | one `libe_sqlite3.dylib` |

This is cross-target compilation and asset inspection from Windows. Native
host execution belongs to exact-tip hosted CI and the later manual/RC gates.

<a id="validation-v2-0-stability-evidence-added-during-recovery"></a>
#### Stability evidence added during recovery

The recovered candidate now explicitly covers deterministic database-handle
release without global pool clearing; bounded independent WAL readers;
disposal fencing; finite SQLite busy deadlines and prompt locked-database
cancellation; cancellation precedence over pause/resume; independent
coordinator/job heartbeats; monotonic fencing epochs across clock jumps;
completed-manifest retention; exact accessible page-range presentation; and
SQLite-safe cross-platform source replacement detection without raw database
header reads that can conflict with Unix locking. It also covers deterministic,
linear-time relationship identifier scanning under scheduler contention without
using a false-positive wall-clock timeout as an input-validity boundary.

The detailed direct/partial/deferred mapping is maintained in
[Automated Test Matrix](Implementation_Spec/v2.0/AUTOMATED_TEST_MATRIX.md).
Residual true multi-process, exhaustive crash-boundary, low-disk/`SQLITE_FULL`,
long-soak, native-platform, and assistive-technology exercises remain explicit
RC or manual work; they are not represented as completed by this run.

<a id="validation-v2-0-repository-and-privacy-audit"></a>
#### Repository and privacy audit

The complete candidate diff was reviewed by path and scope. Source conflict
markers, zero-byte recovered source files, machine-specific paths, private
keys, credentials, generated SQLite databases/indexes, TRX files, logs,
diagnostics exports, and generated binaries are excluded from the candidate
commit. Synthetic secret-like strings occur only in redaction and hostile-input
tests. Historical v1.0 release binaries remain unchanged.

<a id="validation-v2-0-exact-source-tip-hosted-evidence"></a>
#### Exact source-tip hosted evidence

The final implementation source tip is
`52f07b20ea3e78774826af54090aff97983e032f`. GitHub Actions run
`31440412936` validated that exact commit successfully on `windows-latest`,
`ubuntu-latest`, and `macos-latest`: both builds, both complete test suites,
zero-skip enforcement, whitespace/style/analyzer formatting, patch checks, and
documentation/dependency policy all passed. Local and remote source branches
were synchronized before integration.

<a id="validation-v2-0-remaining-release-sequence-and-evidence-boundary"></a>
#### Remaining release sequence and evidence boundary

Post-merge validation, exact-main hosted CI, native package
construction/inspection, checksum verification, tag/publication, and safe
branch cleanup occur after this integrated source revision. Their immutable
identifiers belong in Git/GitHub and the final maintainer handoff rather than
being guessed or backfilled into historical source evidence.

The [manual checklist](MANUAL_TESTING.md#manual-v2-0),
[release-readiness checklist](RELEASE_STATUS.md#historical-v2-0-readiness), and
[stabilization plan](MAINTAINER_GUIDE.md#historical-v2-0-stabilization) remain evidence trackers;
no unchecked interactive, accessibility, OCR, Ollama, battery, watcher, soak,
or community observation is represented as complete. Under the maintainer's
release policy, broad manual/community testing begins after publication and
findings may be addressed in v2.0.x patches or later releases.

</details>

<a id="validation-v1-9"></a>
## v1.9

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.9_VALIDATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="validation-v1-9-scope-and-evidence-policy"></a>
#### Scope and evidence policy

This record covers automated release validation for
`v1.9-relationships-context`, based on exact validated v1.8 commit
`01899f9701f58d3bf2e5c0eaadc5c87efe68ea2d`. Initial fetch and Git inspection
confirmed the v1.8 branch matched its remote, its tree was clean, no Git
operation was active, and the commit was not contained in `origin/main`.
Neither v1.7, v1.8, nor `main` was modified or merged.

This report does not claim a tag, package, published release, or interactive
manual outcome. Exact-tip GitHub Actions evidence is a post-commit gate and is
reported in the final handoff rather than self-referenced by this commit.

<a id="validation-v1-9-automated-evidence"></a>
#### Automated evidence

The final local sequence ran on Windows 10 `10.0.19045`, x64, with .NET SDK
`9.0.315`, targeting .NET 8.

| Gate | Result |
| --- | --- |
| Safe generated-output cleanup | Removed 32 repository-owned `bin` and `obj` directories after resolving and checking every target remained beneath the repository root. The restricted first attempt changed no source and left every target present; the verified elevated retry removed all 32. |
| Forced restore | `dotnet restore .\OpenSorSe.sln --force --no-cache` passed for all 16 projects. |
| Non-incremental Debug build | Passed with zero warnings and zero errors. |
| Complete Debug tests/TRX parse | 1,128 passed; zero failed, skipped/not executed, errors, timeouts, or aborts. |
| Non-incremental Release build | Passed with zero warnings and zero errors. |
| Complete Release tests/TRX parse | 1,128 passed; zero failed, skipped/not executed, errors, timeouts, or aborts. |
| Analyzers, style, and whitespace | Compiler analyzers ran with warnings as errors; whitespace, style, analyzer-format, and `git diff --check` gates passed. |
| Architecture, dependency, and documentation policy | Six repository-policy tests passed: links, Mermaid structure, documentation index, acyclic project dependencies, SDK XML documentation, and no skipped tests. |
| Vulnerability audit | Live NuGet advisory evaluation checked all 16 projects, including transitive packages, against `https://api.nuget.org/v3/index.json`; zero vulnerable packages were reported. |
| Search relevance regression | The deterministic synthetic relevance corpus passed its top-result, top-k recall, reciprocal-rank, exact-preservation, and stable-order floors. |
| Relationship regression | 42 Release tests passed across Application, SQLite provider, and Desktop projects; zero failed or skipped. |
| Performance regression | Six Release tests passed. Relationship discovery took about 51 ms for 100 candidates and 50 ms for the defensive maximum of 512. Hybrid ranking took about 265 ms, 658 ms, and 1 second for 100, 1,000, and 5,000 synthetic candidates. Pre-cancelled fuzzy ranking completed in 18 ms. These are host observations, not universal guarantees. |
| Runtime-target compilation | Fresh Release restores/builds passed with zero warnings/errors for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`. |
| Native SQLite selection | Every target output contained `OpenSorSe.dll`, its expected app host, and exactly one native SQLite library for that runtime. |
| Diff and artifact/private-data audit | The complete v1.8-to-v1.9 diff and every untracked intended file were reviewed. No generated database/index, TRX, log, diagnostic export, binary, credential-like value, developer-machine path, conflict marker, or incomplete implementation remains in the commit. The manual checklist contains zero checked items. |

The runtime-specific restore first encountered the sandbox's expected blocked
NuGet socket and generated-file access. The same explicit restore/build matrix
was rerun with approved network/filesystem access and passed. No product failure
was hidden or retried without diagnosis.

<a id="validation-v1-9-automated-test-totals"></a>
##### Automated test totals

| Project | Debug | Release |
| --- | ---: | ---: |
| Core | 83 | 83 |
| Scanner | 63 | 63 |
| Rules | 69 | 69 |
| Executor | 62 | 62 |
| Application | 539 | 539 |
| Desktop | 203 | 203 |
| SQLite indexing provider | 109 | 109 |
| **Total** | **1,128** | **1,128** |

Each configuration produced seven TRX files. Independent XML parsing returned
`total=1128`, `executed=1128`, `passed=1128`, `failed=0`, and
`notExecuted=0`, with zero error, timeout, or aborted counters.

<a id="validation-v1-9-cross-target-assets"></a>
##### Cross-target assets

| Runtime | App host | SQLite native library |
| --- | --- | --- |
| `win-x64` | `OpenSorSe.exe` | `e_sqlite3.dll` |
| `linux-x64` | `OpenSorSe` | `libe_sqlite3.so` |
| `osx-x64` | `OpenSorSe` | `libe_sqlite3.dylib` |
| `osx-arm64` | `OpenSorSe` | `libe_sqlite3.dylib` |

These Windows-hosted builds prove target compilation and target-asset
selection. They do not claim interactive Linux or macOS execution.

<a id="validation-v1-9-manual-validation"></a>
#### Manual validation

No interactive scenario is marked complete. The fully unchecked
[`MANUAL_TESTING.md#manual-v1-9`](MANUAL_TESTING.md#manual-v1-9) remains the required
maintainer evidence template. No host, OCR, Ollama, accessibility, battery,
watcher, filesystem, dependency, or performance observation is inferred from
automated tests.

</details>

<a id="validation-v1-8"></a>
## v1.8

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.8_VALIDATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="validation-v1-8-evidence-policy"></a>
#### Evidence policy

This report records automated evidence only. No interactive host, OCR, Ollama,
watcher, battery, accessibility, or subjective relevance observation is
claimed. Every scenario in
[OpenSorSe 1.8 Manual Testing](MANUAL_TESTING.md#manual-v1-8) remains unchecked.

The immutable commit, push, exact-tip workflow URL, and final synchronization
are post-commit evidence and are reported in the final handoff rather than
self-referenced in this source commit.

<a id="validation-v1-8-repository-basis"></a>
#### Repository basis

The branch `v1.8-search-intelligence-privacy` was created from exact v1.7 commit
`cce0d8a2e01ecba679f05c2baa02191c034c8365` after fetch, clean-tree,
upstream/divergence, active-operation, and v1.7-not-in-main checks. Neither
v1.7 nor `main` was changed or merged.

<a id="validation-v1-8-automated-evidence"></a>
#### Automated evidence

The final local sequence ran on Windows 10 `10.0.19045`, x64, with .NET SDK
`9.0.315`, targeting .NET 8. Hosted exact-tip evidence remains a post-commit
gate and is reported in the final handoff.

| Gate | Result |
| --- | --- |
| Safe generated-output cleanup | Removed 33 repository-owned `bin`, `obj`, `.artifacts`, and test-result directories after resolving and checking every path remained beneath the repository root. |
| `dotnet restore .\OpenSorSe.sln --force --no-cache` | Passed for all 16 projects. |
| Non-incremental Debug build | Passed with zero warnings and zero errors. |
| Complete Debug tests/TRX parse | 1,086 passed; zero failed, skipped/not executed, errors, timeouts, or aborts. |
| Non-incremental Release build | Passed with zero warnings and zero errors. |
| Complete Release tests/TRX parse | 1,086 passed; zero failed, skipped/not executed, errors, timeouts, or aborts. |
| Analyzers, style, whitespace | Compiler analyzers ran with warnings as errors; whitespace, style, and analyzer `dotnet format --verify-no-changes` gates passed. |
| Architecture, dependency, documentation policy | Six repository-policy tests passed: links, Mermaid structure, documentation index, acyclic project dependencies, SDK XML documentation, and no skipped tests. |
| Vulnerability audit | All 16 restored graphs (67 unique direct/transitive packages) were evaluated with NuGet's `VersionRange` implementation against the official NuGet advisory snapshot dated 2026-07-29 05:43:51Z; zero findings. |
| Search relevance regression | The deterministic synthetic corpus passed top-result, top-k recall, reciprocal-rank, exact-preservation, and stable-order floors. |
| Search performance regression | Four separate Release tests passed. Cold/warm ranking/filter/snippet and allocation bounds passed at 100, 1,000, and 5,000 candidates; the detailed runner reported 289 ms, 791 ms, and 1 s per parameterized case. Pre-cancelled fuzzy ranking completed in 14 ms. These are regression observations on this host, not universal guarantees. |
| Runtime-target compilation | Fresh Release restores/builds passed with zero warnings/errors for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`. |
| Native SQLite selection | Every target output contained `OpenSorSe.dll`, its target app host, and exactly one expected native SQLite library. |
| `git diff --check` and artifact/private-data audit | Passed after the evidence update. All 62 intended paths were reviewed; no generated database/index, TRX, log, diagnostic export, binary, credential-like value, developer-machine path, conflict marker, or implementation placeholder is present. The v1.8 manual checklist contains zero checked items. |

The standard live
`dotnet list .\OpenSorSe.sln package --vulnerable --include-transitive`
presentation command could not open a socket in the sandbox, and external
execution was not authorized because it would disclose the dependency graph.
The offline audit used the official NuGet cache already refreshed on this host
that day and the SDK's own version-range evaluator; no clean result was inferred
from the blocked command.

<a id="validation-v1-8-automated-test-counts"></a>
##### Automated test counts

| Project | Debug | Release |
| --- | ---: | ---: |
| Core | 83 | 83 |
| Scanner | 63 | 63 |
| Rules | 69 | 69 |
| Executor | 62 | 62 |
| Application | 522 | 522 |
| Desktop | 197 | 197 |
| SQLite indexing provider | 90 | 90 |
| **Total** | **1,086** | **1,086** |

Each configuration produced seven TRX files. Independent XML parsing returned
`total=1086`, `executed=1086`, `passed=1086`, `failed=0`, and
`notExecuted=0`, with zero error, timeout, or aborted counters.

<a id="validation-v1-8-cross-target-compilation"></a>
##### Cross-target compilation

| Runtime | App host | SQLite native library |
| --- | --- | --- |
| `win-x64` | `OpenSorSe.exe` | `e_sqlite3.dll` |
| `linux-x64` | `OpenSorSe` | `libe_sqlite3.so` |
| `osx-x64` | `OpenSorSe` | `libe_sqlite3.dylib` |
| `osx-arm64` | `OpenSorSe` | `libe_sqlite3.dylib` |

These local Windows builds prove target compilation and target-asset selection.
They do not claim native Linux or macOS execution.

<a id="validation-v1-8-manual-validation"></a>
#### Manual validation

Interactive validation has not been performed or marked complete. The branch is
intended for maintainer testing after automated and exact-tip hosted validation.

</details>

<a id="validation-v1-7"></a>
## v1.7

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.7_VALIDATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="validation-v1-7-evidence-policy"></a>
#### Evidence policy

This report separates locally executed evidence from hosted-runner evidence.
An immutable commit hash, push result, and exact branch-tip GitHub Actions run
exist only after this source report is committed, so they are recorded in the
final handoff rather than self-referenced inside the commit.

No interactive manual completion is claimed. The unmarked checklist remains
[OpenSorSe 1.7 Manual Testing](MANUAL_TESTING.md#manual-v1-7).

<a id="validation-v1-7-repository-basis"></a>
#### Repository basis

Validation began from branch `v1.7-deep-indexing-foundation`, created from
clean authoritative `main` commit
`58b8a22312f09875e93df632143d24abf6397e26`. `main` and `origin/main`
matched, and no merge, rebase, cherry-pick, revert, or bisect operation was
active. Only intended v1.7 source, tests, configuration, workflow, and
documentation changed.

<a id="validation-v1-7-final-local-evidence"></a>
#### Final local evidence

<a id="shared-fe4eb5b3480a"></a>

The final sequence ran on Windows 10 `10.0.19045`, x64, with .NET SDK
`9.0.315`, targeting .NET 8.

| Gate | Result |
| --- | --- |
| Safe generated-output cleanup | Debug and Release solution outputs and local TRX results were cleaned through verified repository-owned paths. Four historical tracked v1.0 publish files referenced by an old clean manifest were restored byte-for-byte from `HEAD` and are absent from the v1.7 diff. |
| `dotnet restore .\OpenSorSe.sln --force --no-cache` | Passed for all 16 projects. |
| Non-incremental Debug build | Passed with zero warnings and zero errors. |
| Complete Debug tests | 987 passed; zero failed; zero skipped/not executed. |
| Non-incremental Release build | Passed with zero warnings and zero errors. |
| Complete Release tests | 987 passed; zero failed; zero skipped/not executed. |
| Static analyzers | Compiler analyzers ran with warnings as errors; `dotnet format analyzers --severity warn --verify-no-changes` passed. |
| Formatting | Whitespace and style verification passed with no changes. |
| Documentation/dependency/architecture policy | Six repository-policy tests passed. |
| Vulnerability audit | The restored graph was checked against the official NuGet advisory base dated 2026-07-24 and update dated 2026-07-28: 67 unique direct/transitive packages, zero findings. The audit first found `GHSA-2m69-gcr7-jv3q` in the original SQLitePCLRaw transitive minimum; v1.7 pins fixed version 2.1.12 and the complete graph was restored and re-audited. |
| Runtime-target compilation | Fresh Release builds passed with zero warnings/errors for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`. |
| Patch/private/generated-data audit | `git diff --check` passed. Added content contains no developer-machine paths, credential-like values, generated build/test output, runtime databases/indexes, logs, or diagnostic exports. |

The standard live `dotnet list package --vulnerable --include-transitive`
presentation command could not open a socket under the execution sandbox. The
solution restore had already refreshed NuGet's official configured-source
vulnerability cache. The offline audit parsed all 16 restored
`project.assets.json` graphs with NuGet's own `VersionRange` implementation
against that cache; this avoids treating a blocked presentation command as a
clean result.

<a id="validation-v1-7-automated-test-counts"></a>
##### Automated test counts

| Project | Debug | Release |
| --- | ---: | ---: |
| Core | 83 | 83 |
| Scanner | 63 | 63 |
| Rules | 69 | 69 |
| Executor | 62 | 62 |
| Application | 449 | 449 |
| Desktop | 190 | 190 |
| SQLite indexing provider | 71 | 71 |
| **Total** | **987** | **987** |

TRX counters were parsed independently. Each configuration produced seven
result files with `total=987`, `executed=987`, `passed=987`, `failed=0`, and
`notExecuted=0`.

<a id="validation-v1-7-cross-target-compilation"></a>
##### Cross-target compilation

The Desktop dependency graph was restored and built separately in Release for
each runtime identifier. Every output contained `OpenSorSe.dll`, the expected
native app host, and the SQLite native library:

| Runtime | App host | SQLite native library |
| --- | --- | --- |
| `win-x64` | `OpenSorSe.exe` | `e_sqlite3.dll` |
| `linux-x64` | `OpenSorSe` | `libe_sqlite3.so` |
| `osx-x64` | `OpenSorSe` | `libe_sqlite3.dylib` |
| `osx-arm64` | `OpenSorSe` | `libe_sqlite3.dylib` |

These local Windows runs prove target compilation and app-host/native-library
selection. They do not claim native Linux or macOS execution.

<a id="validation-v1-7-hosted-native-validation"></a>
#### Hosted native validation

`.github/workflows/cross-platform-validation.yml` targets
`windows-latest`, `ubuntu-latest`, and `macos-latest` for this branch. Each job
restores, builds and tests Debug and Release, parses TRX counters to reject
skips, verifies analyzers/style/whitespace, runs `git diff --check`, and runs
repository policy tests.

The exact immutable branch-tip run is a required post-push gate. Its URL, run
identity, commit identity, job conclusions, and final branch synchronization
are reported in the final handoff after GitHub has completed the run; merely
defining the workflow is not presented as hosted evidence here.

<a id="validation-v1-7-manual-validation"></a>
#### Manual validation

Interactive Search, assistive-technology, Tesseract, Ollama, battery/idle
adapter, filesystem interruption, and platform-desktop checks were not
executed in this automated implementation session. No manual result is
inferred from automated coverage.

</details>

<a id="validation-v1-6"></a>
## v1.6

[Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/V1.6_VALIDATION_REPORT.md). Historical wording and evidence apply only to this version.

<details>
<summary>Version-specific scenarios, decisions and evidence</summary>

<a id="validation-v1-6-evidence-policy"></a>
#### Evidence policy

This report separates locally executed evidence from hosted-runner evidence.
A workflow definition alone is not presented as proof that GitHub executed it.

<a id="validation-v1-6-baseline-evidence"></a>
#### Baseline evidence

On the Windows development host, before v1.6 changes:

- `dotnet restore OpenSorSe.sln`: passed.
- Debug solution build: passed with zero warnings and zero errors.
- Debug automated suite: 850 passed, zero failed, zero skipped.
- `main` and `origin/main` both resolved to `3cafe92`.

<a id="validation-v1-6-power-loss-recovery-audit"></a>
#### Power-loss recovery audit

Validation was interrupted by a host power loss. On recovery:

- the active branch was `v1.6-reliability-performance` at
  `3cafe920de40a84980d6799e582d273f358631de`;
- `main` and `origin/main` still resolved to that same commit;
- the v1.6 branch had no upstream yet;
- 68 tracked files were modified, 13 intended files were untracked, and no
  files were staged;
- no merge, rebase, cherry-pick, revert, bisect, sequencer operation, conflict,
  zero-length changed file, or OpenSorSe test process remained;
- six Debug TRX files and only five Release TRX files existed in ignored
  output, so neither set was accepted as final evidence; and
- no conflict marker, introduced TODO/FIXME, skipped-test attribute, or
  placeholder implementation was present in the recovered diff.

All `.artifacts`, `bin`, `obj`, and `TestResults` directories under `src` and
`tests` were removed after validating their resolved paths stayed inside the
repository. The complete sequence below was then executed from regenerated
outputs.

<a id="validation-v1-6-final-local-evidence"></a>
#### Final local evidence

Shared definition: [same retained text](#shared-fe4eb5b3480a).

| Gate | Result |
| --- | --- |
| `dotnet restore .\OpenSorSe.sln` | Passed for all 14 projects. |
| Debug build with `--no-restore` | Passed; zero warnings and zero errors. |
| Debug tests with `--no-build --no-restore` | 895 passed; zero failed; zero skipped/not executed. |
| Release build with `--no-restore` | Passed; zero warnings and zero errors. |
| Release tests with `--no-build --no-restore` | 895 passed; zero failed; zero skipped/not executed. |
| Static analyzers | Compiler analyzers ran with warnings as errors; `dotnet format analyzers --severity warn --verify-no-changes` passed. |
| Formatting | Whitespace and style verification passed with no changes. |
| Documentation and zero-skip policy | Six repository-policy tests passed; the full suite's source-wide no-skip policy passed. |
| Dependency audit | All 14 projects reported no known vulnerable direct or transitive package from the configured NuGet source. |
| Runtime-target compilation | Fresh Release builds passed with zero warnings/errors for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`. |
| Patch/generated-artifact audit | `git diff --check` passed; no v1.6 generated output, local setting, log, diagnostic, or temporary file is tracked. |

<a id="validation-v1-6-automated-test-counts"></a>
##### Automated test counts

| Project | Debug | Release |
| --- | ---: | ---: |
| Core | 83 | 83 |
| Scanner | 63 | 63 |
| Rules | 69 | 69 |
| Executor | 62 | 62 |
| Application | 433 | 433 |
| Desktop | 185 | 185 |
| **Total** | **895** | **895** |

TRX counters were parsed independently after each run. Each configuration
produced six result files with `total=895`, `executed=895`, `passed=895`,
`failed=0`, and `notExecuted=0`.

<a id="validation-v1-6-cross-target-compilation"></a>
##### Cross-target compilation

The Desktop dependency graph was restored and built separately in Release for
each runtime identifier. Each output contained `OpenSorSe.dll` and the expected
native app host:

| Runtime | App host |
| --- | --- |
| `win-x64` | `OpenSorSe.exe` |
| `linux-x64` | `OpenSorSe` |
| `osx-x64` | `OpenSorSe` |
| `osx-arm64` | `OpenSorSe` |

These runs prove target compilation and app-host generation on the local
Windows machine. They do not claim native Linux or macOS execution.

<a id="validation-v1-6-hosted-native-validation"></a>
#### Hosted native validation

`.github/workflows/cross-platform-validation.yml` defines native
`windows-latest`, `ubuntu-latest`, and `macos-latest` jobs. Each job restores,
builds and tests Debug and Release, parses TRX counters to reject skipped tests,
checks analyzers/style/whitespace, checks patch formatting, and runs repository
documentation/dependency policy.

The validated branch commit
`5ac2138b71318c2f795a0ad65187cddd36dc66f4` completed
[GitHub Actions run 30357675937](https://github.com/nishdel/OpenSorSe/actions/runs/30357675937)
successfully on 2026-07-28. All three native jobs passed every defined step.
Each runner executed all 895 tests in both Debug and Release with zero failures
and zero skipped/not-executed tests.

Two earlier branch runs failed before the final matrix. They exposed a
timezone-dependent UTC test value, Windows-only path fixtures on POSIX hosts,
host-dependent parsing of persisted path syntax, macOS tests that accidentally
used production fail-closed mutation capabilities, and one filename fixture
that was valid under POSIX rules. Those findings were corrected and are not
reported as passing evidence; run 30357675937 is the immutable
validated-branch evidence.

<a id="validation-v1-6-manual-validation"></a>
#### Manual validation

After automated validation, the project maintainer completed the required
interactive manual smoke testing governed by
[the v1.6 manual checklist](MANUAL_TESTING.md#manual-v1-6) and reported no
release-blocking issues. This is maintainer-attested manual evidence; detailed
host, architecture, filesystem, assistive-technology, Tesseract, Ollama, and
plugin-version observations were not added to the repository.

</details>
