# ADR-001: Optional Ollama Suggestions Behind an Application Boundary

| Field | Value |
| --- | --- |
| Status | Accepted for v0.3 |
| Date | 2026-07-17 |
| Decision | Use a narrow provider-neutral application contract with an Ollama infrastructure implementation |

## Context

OpenSorSe needs useful local AI assistance without making scanning dependent on AI, coupling ViewModels to HTTP DTOs, or allowing untrusted model output to mutate user files. Existing architecture documents anticipated a broad AI subsystem but no implementation existed.

## Decision

`OpenSorSe.Application.AI` owns a narrow suggestion-provider contract and application-owned request, preview, validation, decision, and preference models. `OpenSorSe.AI` implements the contract for Ollama using typed HTTP DTOs. The Desktop depends only on application contracts. Ollama is disabled by default, configured by the user, and receives minimal metadata only.

All model output is parsed, normalized, and validated before a preview appears. Review actions record local decisions but cannot invoke the executor or mutate files. Search remains a separate deterministic subsystem; v0.3 does not call it semantic search.

## Consequences

This adds a small project and additive JSON settings/history persistence, but keeps provider-specific transport, scan safety, and UI concerns separated. A future provider can implement the same contract without a Desktop redesign. The trade-off is intentionally narrow feature scope: no streaming, provider fallback, content readers, embeddings, or automatic execution.

## Alternatives considered

- Direct HTTP calls from ViewModels: rejected because it couples UI, transport, cancellation, and validation.
- A generic multi-provider/capability framework: rejected as premature for one supported provider.
- Executing model-suggested operations: rejected because no v0.3 mutation safety/recovery pipeline is exposed in Desktop.
