# OpenSorSe product vision

**Document type:** Living product direction

**Authority:** Product purpose, values, and the boundary between current
behavior and future intent

**Read with:** [Product Roadmap](PRODUCT_ROADMAP.md),
[Release Status](docs/RELEASE_STATUS.md), and
[Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md)

## Why OpenSorSe exists

People accumulate files faster than they can understand, retrieve, and safely
organize them. Existing tools often make one of two poor trade-offs: they are
limited to filenames and folders, or they ask users to hand control and content
to an opaque service.

OpenSorSe exists to provide a third option: a local-first, inspectable desktop
tool that helps people understand and organize the information they already
own while keeping consequential decisions under their control.

The product is not an autonomous file manager. It is an analysis, Search, and
review system with a deliberately narrow execution boundary.

## Who it is for

OpenSorSe is intended for:

- people with large personal, research, creative, administrative, or small-team
  file collections;
- privacy-conscious users who prefer local processing and explicit data
  retention;
- users who need evidence and recovery around organization changes;
- contributors and plugin authors who want an open, bounded extension model;
- future self-hosters who may need a provider-backed index without surrendering
  the desktop safety model.

It is not currently a multi-user document-management server, cloud drive,
backup product, full office-content platform, or unattended automation engine.

## Product goals

OpenSorSe aims to:

1. make selected files easier to discover, understand, compare, and organize;
2. remain useful without AI, a cloud account, or a database server;
3. explain why a Search result or suggestion appeared;
4. separate analysis and proposals from authorization to change files;
5. make supported changes previewable, journalled, verifiable, and recoverable;
6. protect user privacy through local defaults, explicit bounds, and visible
   controls;
7. keep storage, AI, OCR, and plugin integrations replaceable at clear
   boundaries;
8. evolve without rewriting or disguising earlier release history.

## Current behavior

The current source line is OpenSorSe 1.9 on
`v1.9-relationships-context`. It is not merged into `main`, and its
interactive manual checklist remains open. Exact readiness belongs in
[Release Status](docs/RELEASE_STATUS.md), not in this vision.

Today, the source implements:

- read-only scanning, metadata analysis, classification, hashing, and exact
  duplicate review for explicitly selected folders;
- opt-in Watched Folders whose operating-system events are treated as hints and
  reconciled against actual filesystem state;
- bounded local extraction of supported document metadata and text, plus
  optional local Tesseract OCR;
- local Search over filenames, folders, metadata, tags, retained document/OCR
  text, summaries, keywords, selected text, and optional related-concept data;
- visible filters, deterministic hybrid ranking, evidence-backed explanations,
  bounded snippets, and progressive coverage reporting;
- deterministic evidence-backed file relationships, virtual Smart Collections
  and timelines, persistent user corrections, relationship-aware Search, and
  index-only relationship privacy/repair controls;
- provider-neutral durable indexing contracts with an embedded SQLite
  implementation;
- local Workflow Profiles, constrained Sorting Recipes, and a bounded
  in-process plugin SDK;
- optional Ollama-compatible suggestions that are validated, untrusted, and
  review-only;
- persisted Change Plans for supported rename, move, and create-directory
  actions;
- explicit review, immediate preflight, non-overwriting execution, a durable
  Operation Journal, result verification, rollback attempts, recovery, and
  conflict-aware Undo.

Current source does **not** implement cloud synchronization, collaboration,
OpenSorSe Server, a knowledge graph, a conversational assistant, unrestricted
media understanding, autonomous organization, permanent deletion, or a
published v1.9 package.

## Future vision

The long-term direction is a trustworthy personal and shared knowledge layer
over user-controlled files. Possible future capabilities beyond the current
bounded relationships and collections include knowledge graphs, adaptive assistance, richer media analysis,
conversation, NAS/cloud providers, a server deployment, and collaboration.

Those are concepts, not current capabilities or delivery commitments. They
must earn their place through explicit scope, privacy, migration, security,
performance, accessibility, and recovery designs. See the
[Product Roadmap](PRODUCT_ROADMAP.md) for the distinction between completed,
in-progress, planned, research, and backlog work.

## Product philosophy

### Local first, not local only

Scanning, indexing, OCR, Search, settings, plans, and history use local
application resources by default. No cloud account is required.

“Local first” is a default and an ownership principle, not a claim that data
can never leave the machine. A user may configure an Ollama-compatible endpoint
that is remote. In that case, only an explicitly enabled and requested AI flow
may send its bounded input to that endpoint, and the product must make the
boundary visible.

Future NAS, cloud, or server providers may be possible, but they must be
optional, explicit, provider-bounded, and honest about where data is processed
and retained.

### Human control over consequential change

Analysis can be automatic; authorization is not. Watchers, rules, recipes,
plugins, OCR, Search, and AI can produce facts or proposals. They do not gain
permission to alter source files.

The product should never make “smart” behavior a synonym for hidden behavior.
Controls, status, provenance, failure, and limits should be visible in language
appropriate to the user.

### Review before change

A suggestion is not an instruction. Supported organization work becomes a
Change Plan because a durable plan creates a stable boundary between:

- what a rule, recipe, plugin-influenced value, AI provider, or user proposed;
- what the user selected or edited;
- what was validated;
- what was actually executed.

Change Plans exist so the user can inspect actions together, reject unsafe or
unwanted items, see conflicts, and give separate confirmation only after the
proposal has become concrete.

### Recovery is part of correctness

Filesystem operations are not perfectly transactional. Power loss, permission
changes, storage failures, and other processes can interrupt even a careful
operation. The Operation Journal is therefore written before mutation and
updated through execution.

Undo exists because a successful change should retain enough verified inverse
information to be reversible when the current filesystem still makes reversal
safe. Undo blocks rather than overwrites when later external changes make the
inverse ambiguous. It is a safety mechanism, not a promise that time can always
be reversed.

## AI principles

AI is an optional assistant, never the authority.

- Non-AI scanning, duplicate review, filters, literal Search, OCR Search,
  ranking, snippets, explanations, Change Plan review, execution, and Undo
  remain available without an AI provider.
- AI capabilities start disabled and require an explicit supported request.
- Inputs and outputs are bounded.
- Provider output is parsed and validated as untrusted data.
- Provenance and limitations accompany AI-influenced suggestions.
- Invalid, incomplete, unsafe, or ungrounded output fails closed.
- AI output cannot directly call the executor or mutate a source file.
- A future AI feature must preserve a useful deterministic path where the task
  reasonably allows one.

### Explainable AI

“Explainable” means OpenSorSe should expose the evidence, provenance, and
decision boundary it actually possesses. It must not invent a rationale for a
model or present confidence as truth.

For Search, explanations come from the ranking components that determined the
result. For suggestions, OpenSorSe records supplied reasons and evidence only
after validating their shape and grounding. For generated data, the interface
must distinguish deterministic facts, extracted evidence, user decisions, and
unverified inference.

For relationships, explanations come only from stored evidence rows. Semantic
similarity cannot create an automatic relationship by itself, confidence uses
deterministic levels rather than fabricated percentages, and user corrections
remain distinguishable from automatic output.

## Why Search is designed this way

Search is a retrieval tool, not a certainty engine.

Exact filenames and literal evidence rank above optional related-concept
similarity because precise intent should not be displaced by a weaker inferred
association. Natural-language filters are interpreted locally and shown as
editable chips so no hidden constraint silently changes a query. Snippets come
only from bounded retained index data; Search does not reopen files at query
time to manufacture an explanation.

Progressive coverage is visible because an empty result during indexing is not
proof that nothing matches. Search should degrade to available filename and
metadata evidence when deeper indexing is disabled, incomplete, or
unavailable.

## Privacy principles

Privacy is an architectural constraint, not a slogan.

- Collect only what a feature needs and apply explicit size/count/retention
  bounds.
- Keep ordinary logs free of source content, complete queries, vectors,
  credentials, and raw provider payloads.
- Treat extracted text and indexes as sensitive application data.
- Make diagnostic collection, unredacted detail, exports, AI, OCR, and deep
  indexing separately understandable and controllable.
- Make clearing, forgetting, and repair operate only on OpenSorSe-owned data
  unless a reviewed Change Plan explicitly authorizes a supported source-file
  action.
- Do not claim custom at-rest encryption that the product does not provide.
  Operating-system account, disk, and backup protections remain relevant.

## Why SQLite is used today

The durable Search index needs transactions, migrations, integrity checks,
relational state, recovery, and efficient bounded queries without requiring a
separate service. Embedded SQLite fits that current desktop requirement:

- it is local and requires no database server;
- it supports transactional schema migration and recovery copies;
- it provides mature indexes and bounded query plans;
- it can be packaged for the current runtime targets;
- its provider mechanics can remain outside product and presentation logic.

SQLite is an implementation choice for the current embedded provider, not the
definition of the product model.

## Why persistence is provider neutral

Application contracts define sources, jobs, stages, coverage, Search
candidates, relationship evidence/collections, privacy operations, and repair.
The SQLite project implements those contracts and owns SQL, migrations, WAL,
backups, and compaction. Views,
ViewModels, ranking, and application orchestration do not depend on SQLite
types.

This separation:

- keeps storage mechanics out of the user experience and domain rules;
- makes the current embedded implementation testable in isolation;
- prevents a future server design from leaking into the desktop prematurely;
- leaves room for a reviewed server or NAS provider that preserves the same
  safety, privacy, cancellation, compatibility, and failure semantics.

A future provider is possible, not promised. PostgreSQL or any server database
would belong behind a server/API or provider boundary; it is not a current
desktop dependency.

## Decision test

When a product choice is unclear, prefer the option that:

1. improves user understanding or control;
2. preserves source-file safety and recovery;
3. minimizes unnecessary data movement and retention;
4. keeps deterministic functionality useful;
5. exposes uncertainty and limitations;
6. fits an existing service boundary or justifies a deliberate new one;
7. can be tested, documented, migrated, and supported without pretending future
   work is already complete.
