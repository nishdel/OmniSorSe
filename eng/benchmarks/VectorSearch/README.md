# Learned vector Search benchmark

This opt-in harness uses the real SQLite catalog, Ollama embedding transport,
vector coordinator and hybrid Search service. It creates twelve small synthetic
source files in a unique temporary directory, writes extracted fixture text into
the catalog through normal stage APIs, and never opens a user profile. It is
outside the solution so the automated suite does not require Ollama.

With the repository SDK and an already installed local embedding model:

```powershell
dotnet run --project eng/benchmarks/VectorSearch/VectorSearch.csproj -c Release -p:SourceRevisionId=<commit> -- <absolute-report.json> qwen3-embedding:4b
```

The harness does not download models or change personal settings. A missing
model is a failed run. It retains its synthetic workspace and writes JSON with
the exact provider/model/digest/dimensions, source revision, per-query keyword
and hybrid results, explanations, and wall-clock latency. Its source fixtures
are intentionally small; the latency observations are not large-library or
hardware-independent performance promises.

Seven judged queries cover an exact invoice filename, literal sourdough wording,
and five paraphrases about camping, household budgets, car maintenance, piano
practice and gardening. Other documents are distractors. Each query has one
ground-truth relevant file. The gates require Recall@5 of at least 0.9, mean
reciprocal rank of at least 0.7, exact filename first, and at least one paraphrase
whose relevant file enters the top five only with learned retrieval. Gates are
declared in source before execution and must not be loosened to make a model pass.

An eighth, out-of-corpus query reports diagnostic results without claiming a
known relevant file. Similarity retrieval may suggest weak matches for such a
query; its results are not factual relationships. Inspect those explanations
separately from the judged relevance aggregate.

The run also gates source SHA-256/creation/write-time integrity, reopen reuse
without redundant embedding, a single added file's incremental embedding,
keyword fallback with an unavailable model, and separate semantic-related
suggestion wording. The incremental step creates one additional synthetic file;
the integrity check covers the unchanged original corpus. Native installer,
interactive UX/accessibility, broad real-library relevance and other supported
platforms require their own verification.
