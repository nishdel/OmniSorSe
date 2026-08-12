# Search and AI quality in v2.1

**Status:** OpenSorSe v2.1.0 release behavior.

**v2.4 name note:** This historical feature release keeps its OpenSorSe name.
The same deterministic-first unified Search is exposed under current OmniSorSe
branding and through the bounded read-only Explorer Protocol; ranking authority
and optional-AI privacy behavior are unchanged.

OpenSorSe Search remains a local, deterministic indexed-search feature. v2.1
improves how filename matches are ordered, makes optional Ollama behavior more
truthful, and adds an explicit local-AI ordering option. Ollama supplements
Search; it does not replace it.

## Search ranking

Search evaluates actual indexed evidence and keeps stable tie-breaking. The
main filename tiers are:

1. exact complete filename;
2. exact filename without the extension;
3. filename prefix;
4. complete topic terms in the filename or a filename substring;
5. literal path, metadata, document-text, OCR, tag, summary, keyword, and chunk
   evidence;
6. bounded typo evidence and optional related context.

A complete literal phrase in retained document text remains strong evidence.
A filename containing one word from a longer multi-word query does not
automatically outrank several matching content/path fields. Related concepts,
relationships, and Knowledge Graph context continue to supplement precise
matches rather than displace them.

Case, diacritics, punctuation, spaces, dots, underscores, hyphens, and path
separators use the existing bounded normalization. Minor filename spelling
errors now also recognize a single adjacent-letter transposition. Fuzzy work
remains candidate-bounded, deterministic, and cancellable; very short or broad
queries do not enable unbounded fuzzy scans.

Each result continues to show **Why this result?**, its matched source, and a
bounded retained-data snippet where available. v2.1 adds **Copy full path** to
the existing Open file, Open folder, and Inspect indexed data actions through
the platform-neutral clipboard service.

## Optional AI-assisted ordering

Two explicit controls are required:

- **Settings → Optional AI assistance → Enable AI-assisted Search reranking**;
- **Search → Use optional local AI to refine known results** for the request.

When both are enabled and a model is selected, OpenSorSe first completes normal
local Search. It then sends at most 12 of those known candidates to the
configured Ollama-compatible endpoint. The compact structured request contains
the query, request-local candidate IDs, filenames, actual match reasons, and a
bounded existing snippet when one exists. It does not contain absolute paths,
vectors, the whole index, or complete documents.

The response can reorder candidates only within the same deterministic
relevance tier. It cannot add an ID, create a file, promote weak evidence above
an exact filename, change a score, modify an original file, or invoke a Change
Plan. Unknown/duplicate IDs, malformed JSON, invalid status/task identities,
provider errors, a missing model, and timeouts discard the complete AI order
and preserve deterministic results. Cancelling Search cancels the AI request.

This option is off by default. A custom endpoint may be remote; when configured
that way, the bounded query/candidate text leaves the device. OpenSorSe does not
introduce or contact any cloud service itself.

## Ollama models and failures

**Retry connection** checks the configured endpoint and then discovers models.
**Refresh installed models** uses Ollama's installed-model endpoint. A short
best-effort check of Ollama's running-model endpoint marks only provider-
confirmed models as **running**. Ollama does not expose a reliable general
loading state through these endpoints, so OpenSorSe does not invent one.

If runtime-state checking fails, the valid installed-model list remains usable
and the UI says that runtime state could not be checked. If the persisted model
is no longer installed, discovery selects the first deterministic installed
model in the editable draft and asks the user to save Settings to retain that
fallback.

Unreachable endpoints, refused connections, malformed/oversized responses,
missing models, model-load failures, timeouts, and cancellation produce
actionable states. Ordinary Search remains available throughout. Requests use
finite configured timeouts and reusable asynchronous HTTP transport; no disk,
database, or model request runs synchronously on the UI thread.

## Indexing status

The existing durable progress, stage, file, outcome, throughput, estimate,
coverage, storage, pause/resume/cancel/retry, source, maintenance, failure,
privacy, and repair controls are preserved. During discovery, when the total is
not yet knowable, the progress bar is indeterminate and the count text says the
total/remaining count is unknown. It does not display a fabricated percentage.

Changed and deleted files continue to use the v1.7 incremental invalidation,
retention, cleanup, and coverage behavior. Search coverage messages continue to
identify incomplete indexing and unavailable OCR/AI stages.

Manual scan timing now starts when the scan operation actually begins, updates
from a monotonic clock while work is active, and freezes on completion,
cancellation, or failure. It does not report the duration of only the command
launch. Unknown work totals remain indeterminate rather than displaying false
precision.

## Result and troubleshooting workflows

Duplicate review can create a safe-removal Change Plan for explicitly selected
unwanted exact copies. At least one known copy must remain. After confirmation,
the existing executor moves selected files into the scan root's excluded
`.opensorse/duplicate-recovery` area, records the operation, and permits Undo
while conflict checks pass. This deliberately does not permanently delete data
or reclaim space immediately; no cross-platform recycle-bin guarantee existed
in the established executor.

Recent errors, warnings, and information use a compact dismissible notification
badge and drawer. Individual dismissal and Clear all affect transient UI only;
Advanced Diagnostics retains its separate bounded evidence. The drawer supports
keyboard focus and Escape.

Settings now distinguish local file analysis/indexing from optional AI. A
verified loopback endpoint is labelled local. Every other valid endpoint is
labelled remote with an explicit warning that information supplied to AI
requests may leave the computer. Related Files is the ordinary-language label
for the evidence-backed Knowledge Graph UI, and major pages route `?` directly
to their maintained Help topic.

## Known limits

- AI assistance reranks; it does not answer questions, search the filesystem,
  group results, or generate files.
- It does not run unless the user enables the capability and per-query option.
- Only the first 12 deterministic results are candidates for AI ordering.
- The current Ollama API can distinguish installed and running models but not a
  reliable intermediate loading state.
- Typo handling is intentionally conservative and is not language-specific
  spell checking.
- v2.1 changes no persisted Search/index schema and requires no migration.
- Safe duplicate removal is recovery staging, not permanent deletion or an
  operating-system trash integration; reclaiming its space remains explicit.

For storage, forgetting, and source-file safety, see
[Safety and Privacy](SAFETY_AND_PRIVACY.md). For contributor-level boundaries,
see [v2.1 Search and AI architecture](Architecture/06_Search/12_v2.1_Search_AI_Quality.md).
