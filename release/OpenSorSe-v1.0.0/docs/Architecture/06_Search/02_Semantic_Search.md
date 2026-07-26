# Semantic Search

> This document defines the Semantic Search component, which is responsible for retrieving documents based on semantic meaning rather than exact keyword matches.

## 1.0 implementation status

Semantic Search Beta is implemented locally behind `IEmbeddingProvider`, `ISemanticIndexer`, `ISemanticIndexStore`, and `ISemanticSearchService`. The current provider uses deterministic 256-dimensional feature hashing, not a learned language model. Hybrid ranking combines exact filename, accepted tag, path, metadata, native-text, OCR, and cosine-similarity signals with user evidence weighted above low-confidence generated candidates. Results explain concrete matches and never expose vectors as certainty.

The feature is disabled by default, bounded to configured document/result limits, incremental by source/tag fingerprints, cancellable, and rebuildable after corruption or future upgrades. It does not require AI, contact Ollama, or modify source files. The later sections describe broader semantic-search direction; learned embeddings and GPU/model acceleration are not 1.0 claims.

---

## Purpose

The Semantic Search component enables users to discover documents using natural language and conceptual similarity.

By comparing semantic vector representations, the component retrieves documents that are related in meaning, even when they do not contain the same words as the search query.

Semantic Search complements Keyword Search to provide a more intelligent and flexible retrieval experience.

---

# Responsibilities

The Semantic Search component is responsible for:

* Performing semantic document retrieval.
* Comparing query embeddings with document embeddings.
* Retrieving conceptually related documents.
* Returning similarity information.
* Supporting natural language search.

---

# Scope

### In Scope

* Semantic retrieval
* Natural language search
* Similarity comparison
* Concept-based search
* Related document discovery
* Embedding comparison

### Out of Scope

The Semantic Search component is **not** responsible for:

* Embedding generation
* AI inference
* Search ranking
* Result filtering
* Document classification
* Database persistence

These responsibilities belong to other architectural components.

---

# Architectural Overview

The Semantic Search component retrieves documents by comparing the semantic meaning of a user's query with stored document embeddings.

```mermaid id="m9rq4y"
flowchart LR

UserQuery["Natural Language Query"]

EmbeddingModel["Embedding Model"]

SemanticSearch["Semantic Search"]

VectorIndex["Vector Index"]

CandidateResults["Candidate Results"]

UserQuery --> EmbeddingModel

EmbeddingModel --> SemanticSearch

SemanticSearch --> VectorIndex

VectorIndex --> CandidateResults
```

The Embedding Model generates a semantic representation of the query, while the Semantic Search component performs retrieval against the stored vector index.

---

# Search Workflow

A typical semantic search consists of the following stages:

1. Receive a user query.
2. Generate an embedding for the query.
3. Compare the query embedding with stored document embeddings.
4. Calculate semantic similarity scores.
5. Retrieve the nearest matching documents.
6. Forward candidate results for filtering and ranking.

Semantic Search should remain deterministic when using the same embedding model and indexed data.

---

# Search Sources

Semantic Search may retrieve documents based on embeddings generated from:

* Extracted document text.
* OCR text.
* AI-generated summaries.
* AI-generated classifications.
* Other semantically meaningful document information.

The embedding strategy may evolve as the application develops.

---

# Search Characteristics

Semantic Search should provide:

* Natural language understanding.
* Meaning-based retrieval.
* Robustness against different wording.
* Conceptual similarity matching.
* Discovery of related information.

Semantic Search should complement, rather than replace, traditional keyword search.

---

# Design Principles

The Semantic Search component should remain:

* Independent of embedding generation.
* Independent of AI providers.
* Extensible.
* Efficient.
* Focused on retrieval.

Its responsibility is limited to identifying semantically related documents.

---

# Error Handling

Semantic Search should handle retrieval failures gracefully.

Examples include:

* Missing embeddings.
* Unsupported embedding versions.
* Corrupted vector indexes.
* Empty search queries.
* Similarity calculation failures.

Whenever practical, failures should allow the application to continue using other search strategies.

---

# Future Considerations

The architecture should support future enhancements, including:

* Hybrid keyword and semantic search.
* Cross-language semantic retrieval.
* Personalized semantic ranking.
* Multi-modal search.
* Similar document recommendations.
* Plugin-defined semantic search providers.

These enhancements should preserve the component's primary responsibility of meaning-based document retrieval.

---

# Related Documents

* [Search Overview](00_Overview.md)
* [Keyword Search](01_Keyword_Search.md)
* [Embeddings](../04_AI/08_Embeddings.md)
* [Vector Search](../04_AI/09_Vector_Search.md)
* [Ranking](04_Ranking.md)
