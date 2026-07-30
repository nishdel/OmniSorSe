# OpenSorSe glossary

**Document type:** Living terminology reference

**Authority:** Current product and architecture vocabulary. Historical
documents retain the terminology used by their release.

## Product and data

### OpenSorSe

The complete local-first desktop application and its supported extension,
storage, Search, review, execution, and recovery boundaries.

### File

The primary source item discovered and analyzed by OpenSorSe. A file may be a
document, image, audio/video item, archive, executable, or another supported
filesystem item. Do not use **document** as a synonym when behavior applies to
all files.

### Source file

A user-controlled file in an explicitly selected or watched root. Source files
are distinct from OpenSorSe-owned settings, indexes, plans, journals, packages,
logs, and temporary workspaces.

### Document

A file format from which OpenSorSe may extract bounded structured metadata or
text, such as PDF or supported Open XML formats.

### OpenSorSe-owned data

State created in application-controlled locations: settings, logs, catalogs,
indexes, workflows, watched state, plugin state/packages, Change Plans, the
Operation Journal, histories, and managed temporary/recovery files. Removing
this data is not the same as modifying a source file.

### Metadata

Structured information about a file, such as name, path, extension, size,
timestamps, type, or safely extracted document/image fields. Metadata can be
sensitive even when it is not file content.

### Extracted text

Bounded text read from a supported document without OCR. It remains local
application data unless an explicitly enabled provider flow sends bounded text
to its configured endpoint.

### OCR

Optical character recognition. OpenSorSe can use an externally installed local
Tesseract 5 command-line engine for enabled recognition of supported images and
scanned PDF pages.

### Tag

A bounded application-owned label associated with a file record. Tags have
provenance such as deterministic, extracted, user accepted/rejected, plugin
derived, or AI suggested. OpenSorSe does not currently write tags into source
file metadata.

## Discovery, analysis, and Search

### Scan

A bounded read-only traversal and analysis of explicitly selected roots.

### Watched Folder

An opt-in root whose operating-system notifications are treated as hints.
OpenSorSe reconciles hints against actual state and can update its own
catalogue or create proposals. A watcher does not authorize file mutation.

### Reconciliation

Comparison of actual discoverable filesystem state with an OpenSorSe-owned
catalogue to recover from duplicate, missing, reordered, overflowed, offline,
or unobserved watcher events.

### Search

The user-facing retrieval feature. Write **Search** when referring to the
product feature. Current Search can use filename, folder/path, type, metadata,
tags, retained document/OCR text, summaries, keywords, selected text, and
optional related-concept evidence.

Historical UI used **Semantic Search Beta** and **Meaning Search**. Compatible
internal types and the older `semantic-index.json` retain `Semantic*` names
where renaming would risk persisted or API compatibility.

### Compatible semantic index

The established bounded JSON Search representation retained for compatibility.
It can continue supplying results independently of the durable provider.

### Durable Search index

The provider-neutral v1.7+ background-indexing system. The current embedded
provider stores schema-versioned data in SQLite, including sources, runs, jobs,
stages, bounded derived content, coverage, failures, maintenance, and privacy
rules.

### Index

Rebuildable OpenSorSe-owned data that improves retrieval. An index is not the
source file and is not authoritative proof of current filesystem state.

### Progressive coverage

The reported amount and type of indexing evidence currently available.
Coverage distinguishes names/metadata, text, OCR, related-concept data,
completeness, exclusions, waits, failures, and provider unavailability so an
empty result is not overstated.

### Related-concept data

A bounded local representation used as optional lower-priority retrieval
evidence. It is not a claim of meaning, certainty, or user intent. Exact and
literal evidence rank above related-concept-only similarity.

### Search explanation

Plain-language projection of the actual ranking components that contributed to
a result. It must not invent a reason after ranking.

### Snippet

A bounded, source-labelled excerpt built only from retained candidate/index
data. Search does not reopen a source file at query time to create a snippet.

## Configuration and extension

### Workflow Profile

A typed, versioned set of scan/analysis policy. Profiles configure processing
and can narrow global capabilities; they do not approve or apply file changes.

### Sorting Recipe

A constrained declarative organization template that produces a deterministic
preview/proposal. It cannot execute code or authorize mutation.

### Plugin

An extension loaded through the supported
`OpenSorSe.Extensions.Abstractions` contract. External plugins are local
packages, disabled until explicit enable/grants, and run in-process as the
current user.

### Plugin capability

A declared and user-granted category of plugin intent. A grant is enforced by
the host but is not an operating-system sandbox or proof that the plugin is
safe.

### Provider

A concrete implementation behind an application-owned contract, such as the
Ollama-compatible AI transport or embedded SQLite index store. A provider does
not define the product-facing contract.

### AI

Optional provider-assisted inference used only by explicitly enabled bounded
flows. AI output is untrusted, validated, provenance-bearing, and
suggestion-only. The term does not include deterministic Search filters,
ranking, OCR, hashing, or rules.

## Proposals, file changes, and recovery

### Rule

A deterministic condition/action definition evaluated as data. Current Rules
produce decisions and planned proposals. A rule does not itself execute a
source-file operation.

### Proposal or suggestion

A non-mutating candidate organization or interpretation produced by a rule,
recipe, plugin-influenced value, optional AI, or user edit.

### Change Plan

A persisted, versioned set of proposed supported file actions with stable
identities, source observations, provenance, review decisions, validation, and
conflicts. Creating or editing a Change Plan does not modify source files.

### Review Changes

The user workflow for approving, rejecting, editing, filtering, validating,
and separately confirming Change Plan actions.

### Apply

The explicit command, separately confirmed after review/validation, that asks
the execution service to perform immediate preflight and execute only approved
safe actions.

### Supported file mutation

Current production source-file changes are rename file, verified
same-filesystem move file, and create directory. Permanent deletion and silent
overwrite are not supported.

### Operation Journal

Durable action-level facts written before and during supported mutation. The
journal supports result verification, Operation History, rollback, restart
recovery, and Undo. It is recovery state, not expendable telemetry.

### Rollback

A reverse-order attempt to undo verified reversible actions after a blocking
failure in the current operation. Rollback is verified and may be partial when
the filesystem no longer permits a safe inverse.

### Recovery

Inspection and controlled continuation/rebuild/repair of interrupted or invalid
OpenSorSe-owned state. Recovery does not guess ownership or bypass the ordinary
mutation boundary.

### Undo

A user-requested conflict-aware inverse based on journalled facts plus current
filesystem verification. Undo blocks rather than overwriting when the result
changed, the original path is occupied, identity is uncertain, or later work
depends on the path.

## Release and documentation status

### Implemented in source

Code and tests exist on an identified commit/branch. This does not mean the work
is integrated, manually validated, packaged, tagged, or published.

### Integrated

The implementation commit is an ancestor of `main`.

### Automated validation complete

The documented automated gates passed in an identified environment. It does not
claim interactive desktop, accessibility, provider, or platform behavior that
was not observed.

### Manual validation complete

Required interactive scenarios were actually performed and recorded. An
unchecked checklist is not complete.

### Packaged, tagged, or published

Separate release facts. A build directory is not a package; a version string is
not a tag; a tag is not a published release.

### Living document

Guidance intended to track current behavior or policy.

### Historical/version snapshot

Evidence preserved for a particular release, implementation, validation, or
decision context. It should not be rewritten to match current behavior.

### Planned concept

A named possible future direction without an implementation claim or delivery
commitment.

### Research

Evidence-gathering work that may result in a decision, prototype, measurement,
or rejection, but is not a product capability.

## Related documents

- [Product Vision](../../../PRODUCT_VISION.md)
- [Engineering Principles](../../../ENGINEERING_PRINCIPLES.md)
- [Architecture Overview](../../ARCHITECTURE_OVERVIEW.md)
- [Safety and Privacy](../../SAFETY_AND_PRIVACY.md)
- [Documentation Index](../../README.md)
