# OpenSorSe 1.4 System Goals

## Goals

1. Analyze selected folders safely with read-only traversal, metadata, hashes, classification, exact duplicates, progress, and cancellation.
2. Keep Results usable through fixed filters, bounded paging, independent scrolling, explanations, provenance tags, and a responsive duplicate drawer.
3. Add local understanding through defensive metadata extraction, optional OCR Beta, and independently enabled local Semantic Search Beta.
4. Preserve user control: AI remains optional and suggestion-only; restructuring remains deterministic, preview-first, separately confirmed, root-confined, bounded, and auditable.
5. Avoid repeated organization by activating protection only after a successful apply, while allowing incremental new-file proposals, material-change detection, and explicit override.
6. Keep data local, stores bounded/atomic/versioned, logs privacy-aware, and v0.9.1 settings/catalog/tags/searches backward compatible.
7. Maintain responsive asynchronous MVVM workflows with cancellation, bounded memory, lazy/bounded presentation, and failure isolation.
8. Monitor explicitly registered roots only while OpenSorSe runs, using bounded watcher hints, stability checks, incremental catalogue updates, and conservative reconciliation.
9. Keep every watched-folder suggestion behind the existing reviewable Change Plan, explicit approval, live revalidation, journalled execution, and conflict-aware Undo boundary.
10. Make scan/analysis policy reusable, persistent, inspectable, versioned, and historically truthful through workflow profiles and immutable resolution snapshots.
11. Make deterministic naming/destination composition approachable without adding an executable language, root escape, overwrite, or hidden ordering.
12. Support bounded local extensions through a stable SDK, strict manifests, explicit capability grants, deterministic dependency resolution, integrity checks, lifecycle containment, and inspectable diagnostics.
13. Preserve exact plugin/version/contribution provenance in workflows and plans, and fail closed when a referenced capability is unavailable.

## Non-goals

OpenSorSe 1.4 does not provide:

- Autonomous or AI-driven filesystem control.
- Duplicate deletion or automatic cleanup.
- Generic rule execution/undo from the Desktop.
- Bundled Tesseract executables/language data, GPU acceleration, or externally learned embeddings.
- An online plugin marketplace, downloads, automatic plugin updates, an OS sandbox, publisher authentication/signing authority, broad localization, packaging overhaul, monitoring while the application is closed, cloud indexing, or automated publishing.
- Claims of cross-platform packaging validation beyond portable architecture and current Windows build/test validation.
- Arbitrary recipe scripts or expressions, unattended workflow execution, cloud workflow synchronization, or a recipe marketplace.
- Plugin direct file mutation, Change Plan approval, unrestricted DI/storage/credential access, script execution, or safety-policy bypass.

## Safety invariant

No operation may infer mutation authority from scanning, watching, reconciliation, indexing, an AI response, a suggestion, a preview, or a history record. Only an explicitly reviewed, approved, revalidated, and confirmed Change Plan grants bounded authority for that one operation.

Workflow profiles automate configuration and analysis, not approval or file modification.

Plugins analyze or propose; they do not grant mutation authority.
