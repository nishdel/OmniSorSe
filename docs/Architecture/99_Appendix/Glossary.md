# OmniSorSe glossary

**Document type:** Living terminology reference

**Authority:** Current product and architecture vocabulary. Historical
documents retain the terminology used by their release.

## Product and data

### OmniSorSe

The complete local-first desktop application and its supported extension,
storage, Search, review, execution, and recovery boundaries.

### OpenSorSe compatibility identity

The former product name and the intentionally retained internal compatibility
identity. Current solution/project/namespace names, Windows and macOS profile
folders, Linux XDG subdirectories, installer identity, bundle identity, and
some persisted/public contract names still use **OpenSorSe** so the OmniSorSe
rename does not fork profiles or break compatible consumers. Do not infer the
user-facing product name from an internal identifier, and do not rename a
compatibility identity without an explicit migration and contract review.

### File

The primary source item discovered and analyzed by OmniSorSe. A file may be a
document, image, audio/video item, archive, executable, or another supported
filesystem item. Do not use **document** as a synonym when behavior applies to
all files.

### Source file

A user-controlled file in an explicitly selected or watched root. Source files
are distinct from OmniSorSe-owned settings, indexes, plans, journals, packages,
logs, and temporary workspaces.

### Document

A file format from which OmniSorSe may extract bounded structured metadata or
text, such as PDF or supported Open XML formats.

### OmniSorSe-owned data

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

Optical character recognition. OmniSorSe can use an externally installed local
Tesseract 5 command-line engine for enabled recognition of supported images and
scanned PDF pages.

### Tag

A bounded application-owned label associated with a file record. Tags have
provenance such as deterministic, extracted, user accepted/rejected, plugin
derived, or AI suggested. OmniSorSe does not currently write tags into source
file metadata.

### User Tag

An explicit application-owned label authored by the user. It is durable user
authority, not a classifier output, and is a canonical Search/facet dimension.

### Smart Tag

The schema-6 family comprising canonical **Theme**, **Document Type**, and
**User Tag** values plus assignment/decision state. Theme and Document Type
classifications are deterministic suggestions derived from retained evidence.
Explicit accept/reject decisions and User Tags outrank classifier inference.

### Theme

A canonical Smart Tag describing bounded subject matter from the built-in
taxonomy. A Theme is not proof of author intent or unrestricted semantic
understanding.

### Document Type

A canonical Smart Tag describing the document's functional/content form from
the built-in taxonomy. It is distinct from filename extension or MIME/media
type.

### Authority

State whose owner is allowed to settle a question for downstream consumers.
Documentation must name the owner and distinguish who may read, derive,
present, mutate, persist, or publish it. Two convenient copies do not become
coequal authorities.

### Derived or inferred data

Rebuildable or provisional output computed from authoritative inputs. Derived
or inferred data may aid Search and review but cannot silently replace durable
user-authored decisions or current filesystem truth.

## Discovery, analysis, and Search

### Scan

A bounded read-only traversal and analysis of explicitly selected roots.

### Watched Folder

An opt-in root whose operating-system notifications are treated as hints.
OmniSorSe reconciles hints against actual state and can update its own
catalogue or create proposals. A watcher does not authorize file mutation.

### Reconciliation

Comparison of actual discoverable filesystem state with an OmniSorSe-owned
catalogue to recover from duplicate, missing, reordered, overflowed, offline,
or unobserved watcher events.

### Post-operation reconciliation

Projection of verified Operation Journal outcomes plus current filesystem truth
back into Files, duplicate groups, selection, Search, and targeted index-refresh
inputs. It follows actual outcomes rather than Change Plan intent. Current
Desktop wiring invokes this projection after terminal Apply/Undo in Review
Changes. Operation History Undo and startup interruption recovery do not yet
publish their returned journal records to this projection, so those paths can
remain stale until a later scan/index pass.

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
provider stores schema-6 data in SQLite, including sources, runs, jobs, stages,
bounded derived content, Smart Tag authority, relationships, Smart Collections,
coverage, failures, maintenance, and privacy rules.

### Index

Rebuildable OmniSorSe-owned data that improves retrieval. An index is not the
source file and is not authoritative proof of current filesystem state.

### Progressive coverage

The reported amount and type of indexing evidence currently available.
Coverage distinguishes names/metadata, text, OCR, related-concept data,
completeness, exclusions, waits, failures, and provider unavailability so an
empty result is not overstated.

### Base-first indexing

The scheduling rule that makes discovery, filename, path, metadata, and other
base Search evidence available before more expensive deferred extraction,
media, content-intelligence, Smart Tag, and relationship work. Deferred stages
remain bounded, cancellable, restartable, and independently observable.

### Facet

A canonical filter dimension with contextual counts computed from the current
authorized candidate set. Current facets include Theme, Document Type, User
Tag, file type, and filesystem-created/modified year.

### Saved View

A bounded, application-owned dynamic Search/facet rule persisted by
`JsonSavedDiscoveryViewStore`. It is rerun against the current authorized index
and does not own or copy file membership. A Saved View is distinct from a Saved
Scan or historical catalogue snapshot.

### Stable file identity

The provider-owned identifier used to carry one indexed file across Search,
Files, Smart Tags, relationships, reviewed organization, backup/restore, and
Explorer without treating a mutable path or filename as identity. Restore and
user-authority operations must not guess a stable identity from a similar path.

### Relationship evidence family

One independent category of retained deterministic support for a file pair,
such as identity, content fingerprint, named context, lexical, tag authority,
or structural/media/temporal evidence. Correlated derivatives share a family
cap. Semantic or AI-derived evidence cannot qualify an automatic relationship
by itself.

### Pair authority

An explicit reversible user decision for an exact stable file pair:
**Related** stores positive `AlwaysRelate`, **Not Related** stores negative
`NeverRelate`, and **Use automatic result** removes the override and requests
bounded reanalysis. Pair authority outranks generated edges without pretending
to change source files.

### Related Files

The provider-neutral direct projection of bounded relationship evidence and
pair authority for one stable file. It works independently of the optional
Knowledge Graph and aggregates multiple typed edges into one deterministic
target row.

### Smart Collection

A virtual grouping over indexed files. Automatic membership is derived;
manual/merged collections, names, pin state, membership, exclusions, splits,
and intentional tombstones are durable user-authored grouping authority.

### Knowledge Graph

An optional, disabled-by-default local projection of stable indexed files,
sources, folders, Smart Collections, exact-content document sets, manual
entities, typed edges, and actual retained evidence. It is bounded and
provider-neutral, does not open source files, and is not an authority for
schema-6 relationships, Smart Collections, Search, or file operations.

### Graph manifest

An immutable completed projection input identified by stable ID, canonical row
count, canonical hash, and revision. Partial or mismatched manifests never
replace the active graph input.

### Applied watermark

The latest source, decision, or privacy authority reflected in published graph
data. It is separate from the corresponding ingestion watermark. Graph reads
fail closed while applied authority lags ingested or current authority.

### Graph-native decision

An explicit user command owned by `knowledge-decisions.db`, such as a manual
entity, alias, link, never-merge rule, or graph-only exclusion. Relationship
pair decisions and Smart Collection decisions remain authoritative in the
schema-6 durable index; the graph only consumes their bounded projection.

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

### Explorer Protocol

The dependency-free, independently versioned read-only contract in
`OmniSorSe.ExplorerProtocol`. Protocol 1.0 exposes bounded operations over
authorized indexed Structure, Search, Related Files/context, and details. It
has no mutation, arbitrary-path, renderer, SQLite, or network-listener contract.

### Explorer session grant

Short-lived current-user authorization for specific indexed source roots and
bounded protocol capabilities. The on-demand local host remains dormant until
an explicit request creates a grant; node identifiers are opaque and paths are
projected only where the authorization setting permits them.

### OmniBrille

The optional separately installed and separately owned companion that can
consume an explicitly launched Explorer Protocol session. OmniSorSe may locate
and start it and transfer one scoped session over OmniBrille's current-user
handoff pipe. OmniBrille's renderer/product implementation is not in this
repository and is not required for ordinary OmniSorSe behavior.

### Profile

The compatible set of OmniSorSe-owned configuration, data, state, cache,
diagnostics, and plugin locations resolved by `IApplicationPathProvider`.
Visible OmniSorSe branding does not change the retained OpenSorSe/opensorse
storage identity.

### Profile ownership

The single-writer lease acquired by Desktop before profile services are
composed. A second writer fails explicitly rather than sharing mutable profile
state. This is process/profile coordination, not ownership of source files.

### Logical state backup

A reviewed `.oms-state` transfer package for durable user-authored application
state. Current format 2 includes settings/sources/workflows/Saved Views/Smart
Tag decisions and exact-pair/authored Smart Collection authority. It excludes
rebuildable generated index/graph data, the separately managed Knowledge Graph
decision sidecar, and active mutation history. Restore validates
bounds/digests/schema, uses stable identities, and creates a pre-restore
recovery point; the reader still accepts exact format 1.

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
OmniSorSe-owned state. Recovery does not guess ownership or bypass the ordinary
mutation boundary.

### Undo

A user-requested conflict-aware inverse based on journalled facts plus current
filesystem verification. Undo blocks rather than overwriting when the result
changed, the original path is occupied, identity is uncertain, or later work
depends on the path.

## Release and documentation status

### Implementation confidence

Confidence from inspected code, focused tests, and architecture review that a
change behaves as intended. It is narrower than release confidence.

### Automated validation confidence

Confidence supported by named automated gates on an identified commit and
environment. Passing tests do not imply interactive UX, accessibility, native
provider, packaging, or platform behavior was observed.

### Interactive or manual validation confidence

Confidence supported by an actually performed and recorded user/host scenario.
An unchecked checklist or reasoned expectation is not manual validation.

### Platform and package confidence

Confidence from executing the relevant native runtime/package/install/update/
uninstall path on the stated platform. Cross-target compilation alone does not
provide it.

### Release confidence

The combined assessment of source integration, automated evidence, required
manual/native/package evidence, version/provenance agreement, tag, artifacts,
and publication readiness. A successful implementation run is not by itself a
release.

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
- [Current State](../../CURRENT-STATE.md)
- [System Map](../OpenSorSe_System_Map.md)

For exact term semantics, inspect the owning contracts and tests rather than
inferring from display text. Important anchors include
`DeepIndexingVersion`/`DeepIndexingModels.cs`,
`Relationships/RelationshipModels.cs`, `SmartTags/SmartTagModels.cs`,
`Semantic/FacetedDiscoveryContracts.cs`,
`ExplorerProtocolContracts.cs`, `ProfileOwnership.cs`, and
`StateBackupService.cs`. Focused intent is preserved by relationship, Saved
View, Explorer, profile-ownership, Change Plan reconciliation, and state-backup
test suites.
