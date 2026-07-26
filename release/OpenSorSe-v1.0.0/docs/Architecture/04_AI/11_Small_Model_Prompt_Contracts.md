# Small-model Prompt Contracts

## Status and intent

These contracts are designed for local instruction-following models in the approximate 2B-8B parameter range. This is a design target, not a compatibility claim. Compatibility remains unverified until the manual matrix in `docs/MANUAL_TESTING_v1.0.md` is completed with exact Ollama model IDs and retained results.

Approved templates live in `AiPromptTemplates.cs`; exact Ollama JSON Schemas and ordered C# wire DTOs live in `AiStructuredOutputContracts.cs`. `AiPromptSnapshotTests` hashes the approved system prompts, representative final user prompts, repair prompt, and schemas. An intentional prompt or schema change requires explicit review and a snapshot update.

## Common rules

- One narrowly defined task is sent per generation request.
- System prompts are short and deterministic. User prompts are compact JSON with labelled `promptVersion`, `taskId`, `task`, `input`, numbered `rules`, `responseSchema`, and `output` sections.
- Ollama receives the exact task schema as its structured-output `format` value, with `stream: false`, `temperature: 0.0`, and `keep_alive: 5m`.
- Output is one JSON object: no Markdown, prose outside JSON, alternatives, commands, filesystem actions, absolute paths, or hidden-reasoning request.
- `reason` is a short user-facing explanation of at most 160 characters. It is not chain-of-thought.
- The model is not asked to perform deterministic filesystem safety checks. Application validators make every identity, name, extension, assignment, relationship, and path-safety decision.
- Exact property ordering is fixed by prompts and `JsonPropertyOrder` on the wire DTOs.
- `additionalProperties` is false. Unknown or wrong-case properties reject the complete suggestion.

## File rename

| Item | Contract |
| --- | --- |
| Prompt version | `2.0` |
| Task ID | `file-rename-v2` |
| Input | `item-001`, current stem, preserved extension, deterministic document type, up to 8 nearby stems, up to 6 rejected stems |
| Output | `taskId`, `status`, `sourceFileId`, `suggestedStem`, `reason`, optional numeric/null `confidence` |
| Stem bound | 120 characters; final application-owned name remains at most 255 characters |
| Naming | letters/numbers joined by single `-`; no leading/trailing/doubled separator |
| Preferred order | explicit date, subject, document type; explicit dates use `yyyy-MM-dd` |
| No suggestion | ambiguous evidence, already-clear stem, or no grounded improvement |

The model never returns an extension or path. OpenSorSe appends the known exact extension after validation. Reserved Windows device names, control characters, portable invalid characters, collisions, unchanged suggestions, path/traversal syntax, and tokens not grounded in the supplied filename/document type reject the whole response. OpenSorSe cannot infer a date or fact that was not supplied, translate names, or accept a plausible but invented subject.

## Folder structure

| Item | Contract |
| --- | --- |
| Prompt version | `2.0` |
| Task ID | `folder-structure-v2` |
| File input | at most 12 deterministically ordered records with `item-NNN`, current stem, extension, and bounded document type |
| Folder choices | at most 16 prevalidated components derived from supplied deterministic classifications, existing/preferred components, plus `Other` |
| Output | at most 8 folders, at most 12 assignments, one reason |
| Folder IDs | `folder-NNN` |
| Depth/component bounds | maximum depth 3; maximum component length 64 |
| Fallback | `Other` for uncertain classification |

Every included `item-NNN` must appear exactly once. Every assignment and parent must reference a declared folder ID. OpenSorSe rejects the entire plan for unknown IDs, missing or duplicate assignments, undeclared folder names, duplicate paths, unknown parents, self-reference, cycles, traversal, absolute paths, unsafe/reserved components, excessive depth, or count violations. The model cannot select source paths or prescribe file operations.

## Document text interpretation

| Item | Contract |
| --- | --- |
| Prompt version | `1.1` |
| Task ID | `document-text-interpretation-v1` |
| Gate | separate default-off extracted-text capability and explicit user request |
| Input | one opaque ID; at most 12 provenance-labelled pages and 16,384 extracted characters |
| Output | bounded nullable document type/title/issuer/folder, up to 12 tags, up to 8 explicit ISO dates, reason, optional confidence |

This task exists only for the separately enabled review proposal. Rename and folder requests do not receive document text. Values must be directly supported by supplied filename/text. Dates must be explicit `yyyy-MM-dd` values. The suggested folder is one safe component, not a path. The output remains unverified and cannot trigger a file operation.

## Optional repair

Repair prompt version `1.0` is permitted once after a provider-success response fails JSON or exact schema-shape validation. It contains:

1. The original task ID.
2. The same exact structured-output schema.
3. The bounded prior response, at most 32,768 characters.
4. The exact concise validation error.
5. A JSON-only correction instruction.

There is no repair for timeouts, cancellation, transport/provider failures, oversized or empty responses, unsafe/unknown identities, absolute/traversal paths, or output showing invented evidence or task misuse. A failed repair ends the operation; a third generation request is impossible. Advanced Diagnostics retains the original and repair attempts as separate related session-only records.

## Known small-model limitations

- Some small models still add fences/prose, copy example-like values, change property casing, coerce numbers to strings, or truncate JSON despite structured output.
- The rename grounding rule intentionally favors rejection over a creative but unsupported name.
- A folder request containing more than 12 selected files is rejected before provider discovery or generation. The UI reports the exact selection count and states that none were sent; OpenSorSe does not present a partial plan or silently omit files.
- Folder choices are intentionally restrictive. A useful new category may be unavailable until deterministic application metadata supplies it.
- OCR noise and multilingual text may reduce usefulness. Validators do not turn plausibility into evidence.
- The single repair can correct shape, not unsafe identity, invented facts, or poor classification.
- Passing automated contract fixtures does not establish compatibility with any real model build, quantization, context configuration, or Ollama version.
