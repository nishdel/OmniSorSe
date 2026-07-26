# AI Overview

> This document provides an overview of the Artificial Intelligence subsystem, which is responsible for enriching extracted document information through intelligent analysis and reasoning.

---

## Implementation Status

OpenSorSe 1.0 preserves and extends the v0.9.1 optional-AI boundary. AI is disabled by default, with independent rename, folder-structure, and document-text-interpretation capability switches. Application-owned feature gates reject disabled, invalid, or unconfigured calls before `IAiSuggestionProvider` can run. `AiPromptBuilder` produces capability-specific deterministic bounded prompts; rename/folder prompts remain metadata-only, while document interpretation accepts only bounded normalized extracted text after its separate gate and explicit one-document request. `AiResponseParser` and `AiSuggestionValidator` reject malformed, invented, duplicate, unsafe, or excessive output as a whole. The only results are immutable, unverified review proposals and bounded local review decisions. No AI proposal mutates a filesystem or enters restructuring.

Model discovery/selection, connection retry, readiness, and capabilities appear whenever AI is enabled. Endpoint and timeout controls plus raw request inspection are progressively disclosed by Advanced mode. Readiness distinguishes not configured, not checked, server unavailable/available, selected model missing, ready, running, failed, and cancelled. A terminal request always releases its request cancellation source and refreshes commands, so cancellation or failure cannot permanently block retry. The exact saved model is passed to the next request; Ollama loads it on demand, and the Desktop shows the model reported by the validated proposal. Ollama supports empty-request preloading and `keep_alive`-based unloading, but OpenSorSe deliberately does neither during selection: that would create extra model communication and memory/lifecycle policy outside the user’s requested suggestion. The next explicit request names and loads the selected model.

---

## Purpose

The AI subsystem transforms extracted document information into meaningful knowledge.

The implemented provider produces only the three constrained review proposals above. The broader component catalogue below is long-term architecture, not a claim that classification, summaries, AI embeddings, cloud providers, or agents ship in 1.0.

The AI subsystem operates on information extracted by the Readers subsystem. It does not read files directly.

### 1.0 concrete boundary

The delivered provider is optional and receives bounded result metadata or, only through the separate document-text capability, bounded normalized text with provenance. `OpenSorSe.Application.AI` owns gates, provider-neutral contracts, deterministic request-local identities, prompt packages, exact-model preflight, parsing, validation, typed progress, the observable memory-only diagnostics collector, and review coordination; `OpenSorSe.AI` owns normalized Ollama HTTP transport and local JSON decision history. The Desktop never calls HTTP directly. Model output remains untrusted until the complete response passes validation and becomes an application-owned preview. The local deterministic embedding provider belongs to Semantic Search and is not an AI request provider. See [final v1.0 specification 049](../../Implementation_Spec/v1.0/049_Final_Product_Completion.md) and [live diagnostics specification 052](../../Implementation_Spec/v1.0/052_Live_AI_Request_Diagnostics.md).

---

# Responsibilities

The AI subsystem is responsible for:

* Managing AI providers.
* Coordinating AI requests.
* Building prompts.
* Classifying documents.
* Generating summaries.
* Suggesting filenames.
* Suggesting folder locations.
* Generating embeddings.
* Supporting semantic search.
* Managing AI response caching.

---

# Scope

### In Scope

* Large Language Model integration
* Prompt generation
* Document understanding
* Semantic enrichment
* Embedding generation
* AI-assisted recommendations
* Response caching

### Out of Scope

The AI subsystem is **not** responsible for:

* Reading files
* Filesystem access
* Search indexing
* Database persistence
* Rule execution
* User interface rendering

These responsibilities belong to other architectural subsystems.

---

# Architectural Overview

The AI subsystem receives normalized document representations from the Readers subsystem and enriches them with intelligent analysis before forwarding the results to the Database.

```mermaid
flowchart LR

Readers["Readers"]

AIManager["AI Manager"]

PromptEngine["Prompt Engine"]

ModelProviders["Model Providers"]

AIFeatures["AI Features"]

Database["Database"]

Readers --> AIManager

AIManager --> PromptEngine

PromptEngine --> ModelProviders

ModelProviders --> AIFeatures

AIFeatures --> Database
```

---

# AI Components

The AI subsystem consists of several independent components.

| Component               | Responsibility                                         |
| ----------------------- | ------------------------------------------------------ |
| AI Manager              | Coordinates all AI operations.                         |
| Model Providers         | Provides access to local and optional cloud AI models. |
| Prompt Engine           | Constructs prompts for AI interactions.                |
| Document Classification | Categorizes and labels documents.                      |
| Summarization           | Generates document summaries.                          |
| Renaming                | Suggests meaningful filenames.                         |
| Folder Suggestions      | Recommends appropriate storage locations.              |
| Embeddings              | Generates semantic vector representations.             |
| Vector Search           | Supports semantic retrieval.                           |
| Caching                 | Stores reusable AI results.                            |

Each component is documented separately within this section.

---

# AI Processing Pipeline

A typical AI operation follows these stages:

1. Receive a normalized document representation.
2. Determine the required AI operations.
3. Build the appropriate prompt.
4. Select the configured AI provider.
5. Execute the AI request.
6. Validate and normalize the response.
7. Enrich the document representation.
8. Forward the enriched document to the Database subsystem.

---

# Design Principles

The AI subsystem follows several architectural principles:

* AI-assisted, not AI-controlled.
* Provider independent.
* Local-first.
* Explainable where practical.
* Extensible.
* Modular.
* Deterministic where possible.

The AI subsystem should support multiple providers without requiring changes to higher-level application components.

---

# AI Providers

The architecture should support multiple AI providers through a common abstraction.

Examples include:

* Local language models
* Optional cloud language models
* Local embedding models
* Cloud embedding providers

The rest of the application should remain unaware of the specific provider being used.

---

# Future Considerations

The architecture should support future enhancements, including:

* Multi-model orchestration.
* Vision-language models.
* Speech models.
* Agent-based workflows.
* Fine-tuned local models.
* Plugin-defined AI providers.

These enhancements should integrate with the existing architecture while preserving subsystem boundaries.

---

# Related Documents

* [AI Manager](01_AI_Manager.md)
* [Model Providers](02_Model_Providers.md)
* [Prompt Engine](03_Prompt_Engine.md)
* [Document Classification](04_Document_Classification.md)
* [Summarization](05_Summarization.md)
* [Renaming](06_Renaming.md)
* [Folder Suggestions](07_Folder_Suggestions.md)
* [Embeddings](08_Embeddings.md)
* [Vector Search](09_Vector_Search.md)
* [Caching](10_Caching.md)
* [Database Overview](../05_Database/00_Overview.md)
