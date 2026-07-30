# OpenSorSe engineering principles

**Document type:** Living engineering policy

**Authority:** Cross-cutting reasons and expectations for implementation,
review, validation, releases, and maintenance

**Read with:** [Contributing](CONTRIBUTING.md),
[Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md), and
[Repository Structure](docs/REPOSITORY_STRUCTURE.md)

These principles explain why OpenSorSe is engineered the way it is. Detailed
subsystem contracts remain in the architecture and implementation
documentation.

## Architecture philosophy

OpenSorSe favors explicit boundaries over clever coupling. Most code reads,
derives, stores, presents, or proposes. Only one supported production path
mutates user files. This asymmetry is intentional: analysis can usually fail
independently, while mutation requires authorization, durable evidence,
verification, and recovery.

Dependencies point toward stable contracts and domain behavior. The Desktop is
the composition root; lower projects do not reach upward for UI state or
services. Cross-platform behavior, persistence, external tools, providers, and
plugins are isolated where their failure and trust assumptions can be stated
and tested.

Abstraction is added to express ownership, policy, replaceability, or a safety
boundary—not merely to reduce the number of lines in a class.

## MVVM

Avalonia Views describe layout, binding, accessibility, and presentation.
ViewModels own observable state and commands. Application and domain services
own workflows and rules. Code-behind is acceptable for strictly
presentation-specific interaction, but not for persistence, Search ranking,
provider access, or filesystem mutation.

This separation makes command gating, cancellation, error states, and
accessibility behavior testable without launching a real desktop. It also keeps
Views from becoming alternate composition roots.

## Service boundaries

A service should have one understandable responsibility and a contract that
states bounds, cancellation, errors, and side effects. External or
privacy-sensitive work belongs behind a narrow interface.

Examples include:

- `IPathSemantics` for host-correct path policy;
- `IAiSuggestionProvider` for provider transport;
- `IDeepIndexStore` for durable index state;
- `IIndexPrivacyService` for index-only privacy and repair;
- `IChangePlanExecutionService` for approved user-file mutation.

Callers should not bypass a boundary because a concrete implementation appears
convenient. Doing so would create a second policy path that validation and
recovery can no longer reliably cover.

## Repository pattern and persistence ownership

OpenSorSe uses store/repository-style contracts where durable state has a clear
owner. A store owns:

- schema identity and compatible reading;
- validation and hard bounds;
- atomicity or transaction scope;
- corruption and newer-version behavior;
- migration, backup, and recovery;
- concurrency and cancellation;
- tests and user-facing documentation.

The repository pattern is not a license to hide arbitrary queries in generic
data access. Provider-neutral contracts should expose product operations,
while the provider owns its storage language and mechanics.

## Provider-neutral persistence

The Application layer describes durable indexing and Search in product terms.
`OpenSorSe.Indexing.Sqlite` is the current embedded provider and alone owns SQL,
connections, schema migration, WAL, integrity checks, and compaction.

Provider neutrality is valuable because it:

- prevents SQLite details from entering Views and ViewModels;
- allows deterministic service tests without a database;
- keeps an embedded desktop provider small and self-contained;
- leaves a deliberate seam for a future server provider.

Compatibility is semantic, not merely syntactic. Any future provider must
preserve cancellation, coverage, privacy, repair, ownership, migration, and
failure behavior; implementing the same interface name is not enough.

## Testing philosophy

Tests protect contracts and risk, not implementation trivia. Add the narrowest
deterministic test at the owning layer, then add integration or ViewModel
coverage where a boundary is crossed.

High-risk inputs deserve adversarial cases: traversal, links, occupied paths,
stale identities, malformed or oversized data, cancellation, concurrency,
provider failure, corruption, unsupported versions, and partial recovery.

Real filesystem tests use unique disposable temporary roots. Automated tests
must never point at a user folder, depend on order, require Ollama/Tesseract, or
silently skip. Synthetic Search corpora measure regressions; they do not claim
universal relevance or latency.

Deleting or weakening an existing test to make a change pass requires a
documented contract change, not convenience.

## Validation philosophy

Compilation is necessary but not sufficient. Validation should match the risk
introduced:

- build Debug and Release with warnings treated as errors;
- run the complete automated suite without skips;
- run focused relevance/performance/provider tests for affected subsystems;
- verify formatting, analyzers, documentation links, Mermaid structure,
  dependency policy, and patch whitespace;
- inspect generated artifacts, migration output, diagnostics, and packages
  where relevant;
- perform manual interaction for desktop, accessibility, platform, OCR, AI,
  watcher, and recovery behavior that automation does not prove.

Evidence must say where and how it was observed. A CI definition is not a
passing run, target compilation is not native execution, and an unchecked
manual checklist is not completed validation.

## Branching strategy

`main` is the integrated line. Work happens on focused branches and is merged
only after its intended validation and review. A branch name should state its
purpose without imposing a prefix that adds no information.

Release branches use the repository’s established
`v<version>-<primary-feature>` form when a maintainer actually begins that
release. Historical exceptions such as `v1.1`, `coding/v0.1`, and
`coding/v0.2` remain part of history and are not renamed. Planned roadmap
entries do not receive invented branch names.

Do not commit directly to `main`, reuse an earlier release branch for unrelated
work, rewrite shared release history, or force-push without explicit maintainer
coordination.

## Release strategy

Implementation, integration, validation, packaging, tagging, and publication
are separate facts. Documentation must not collapse them into the word
“released.”

A release may be:

- implemented in source;
- locally validated;
- validated on hosted/native runners;
- manually validated;
- merged to `main`;
- tagged;
- packaged;
- published.

Record only completed facts. A release branch can remain in progress even when
its source implementation and automated tests are complete. Tags and packages
require explicit maintainer authorization and must be reproducible from an
identified commit.

## CI expectations

CI should repeat deterministic repository gates on every supported validation
host without publishing by default. A healthy pipeline restores, builds and
tests Debug/Release, rejects skipped tests, runs analyzer/format checks, checks
documentation and dependencies, and reports each platform independently.

CI must fail visibly. It should not mask platform differences, mutate release
state, upload user data, or turn ordinary pull requests into releases.

## Performance philosophy

OpenSorSe is interactive desktop software, so long work must be asynchronous,
cancellable, bounded, and visible. Prefer paging, streaming, incremental
invalidation, bounded channels, short transactions, and reuse of compatible
work.

Measure before optimizing. Preserve correctness, determinism, and safety while
reducing allocation or latency. Synthetic regression thresholds are guardrails,
not promises about every machine or million-file collection.

Resource policy should degrade honestly when a platform signal is unavailable.
Never trade away journal durability, path validation, output bounds, or
privacy for speed.

## Security philosophy

Treat every external boundary as untrusted:

- filesystem observations can become stale;
- watcher notifications are hints;
- AI and plugin outputs require validation;
- plugin packages and imports require path/count/size checks;
- Search text is data, not SQL or FTS syntax;
- diagnostics and exports may contain sensitive material.

Use parameterized storage operations, root confinement, explicit capabilities,
least privilege, strict parsing, output validation, timeouts, and failure
containment. OpenSorSe does not elevate privileges or silently relax a safety
check.

In-process plugin load contexts are not an operating-system sandbox. SHA-256
integrity is not publisher authentication. Documentation must retain those
distinctions.

## Privacy philosophy

Local-first design minimizes unnecessary disclosure, but locally retained
indexes and extracted text are still sensitive. Store only bounded data a
feature needs, keep ordinary logs content-free, make AI/diagnostics/export
boundaries explicit, and provide inspection and deletion for OpenSorSe-owned
derived data.

If a custom provider endpoint may be remote, say so before data is sent.
Privacy statements must describe actual behavior and exceptions rather than
relying on the word “local.”

## Accessibility philosophy

Accessibility is part of correctness. Important workflows must work with
keyboard input, meaningful names, predictable focus, visible state, sufficient
contrast, and non-hover activation. Progress, failure, validation, and
completion should have appropriate live announcements without flooding assistive
technology.

ViewModel tests protect accessible state where possible; real assistive
technology and platform behavior still require manual validation.

## Documentation philosophy

Documentation is part of the product and must distinguish:

- living current guidance;
- version-specific current contracts;
- historical snapshots and evidence;
- planned concepts;
- research and unassigned ideas.

Update the smallest authoritative document and link to detail rather than
copying it. Preserve release reports, validation reports, version notes,
implementation specifications, and manual checklists as historical evidence.
If a living document is superseded, keep a clear navigation page when inbound
links or historical value justify it.

Planned work must not be written in the present tense as an implemented
capability.

## Dependency philosophy

Every dependency adds update, security, licensing, packaging, and platform
cost. Add one only when its maintained capability is meaningfully better than a
small owned implementation and fits the architecture.

Dependencies require:

- a compatible free/open-source license and inventory entry;
- vulnerability and maintenance review;
- explicit mandatory/optional status;
- platform and packaging consideration;
- isolation behind a boundary where appropriate;
- graceful behavior when an optional component is absent.

Avoid framework duplication and speculative dependencies for planned features.

## Code review expectations

Review should establish:

- the change has one clear purpose;
- ownership and dependency direction remain correct;
- trust boundaries, bounds, and cancellation are explicit;
- persistence and migration behavior are complete;
- no second mutation or provider path was introduced;
- tests cover success, failure, and adversarial behavior;
- user-visible, safety, roadmap, and release documentation remains accurate;
- the diff excludes generated output and unrelated churn.

Review comments should explain the risk or principle involved. Small,
evidence-backed changes are easier to verify and recover than broad rewrites.

## Error handling

Expected failures should become typed or otherwise controlled outcomes at the
owning boundary. Preserve enough context for action without exposing source
content, credentials, or internals unnecessarily.

Cancellation is not success. Corruption is not an empty state unless the
specific optional store contract deliberately defines a safe rebuildable
fallback. Unsafe ambiguity should block mutation and tell the user what remains
unknown.

Do not catch broadly and continue with partially valid data across a trust or
authorization boundary.

## Logging and diagnostics

Ordinary logs are bounded operational records and exclude source content,
complete queries, prompts, vectors, credentials, and raw provider payloads.
Detailed Advanced Diagnostics is separately gated, redacted by default,
bounded in memory, and exported only through an explicit user action.

Logs should help locate a failure without becoming a second content store.
Correlation IDs, stages, categories, counts, duration, and safe error types are
usually more useful than raw payloads.

## Recovery

Design recovery before the happy path ships. Durable operations must identify
their state transitions, what can be retried, what can be rebuilt, what must be
preserved, and what requires user review.

Application-owned stores use atomic replacement or provider transactions.
Safety-critical operation facts are journalled before mutation. Interrupted
state is inspected rather than guessed. Recovery never authorizes overwrite or
source-file change outside the normal boundary.

## Backward compatibility

Persisted data, public SDK contracts, user workflows, and safety semantics are
compatibility surfaces. Prefer additive changes and stable internal names where
renaming would break stored or plugin data.

Compatibility does not require retaining misleading UI terminology forever;
for example, the product can display **Search** while preserving compatible
internal `Semantic*` identifiers. Document such distinctions.

When compatibility cannot be retained, fail explicitly and provide a migration
or recovery path.

## Migration principles

The owning subsystem must decide whether older data is migrated, remains
readable, or is rejected. A migration should:

1. validate the source version and bounds;
2. preserve a recovery copy where loss would matter;
3. execute atomically or transactionally;
4. retain ownership and safety facts;
5. reject unsupported newer versions;
6. verify the result;
7. cover old, malformed, oversized, interrupted, and cancellation cases;
8. update architecture, privacy, release, and user guidance.

Never reinterpret an old field in a way that silently broadens file operations,
provider access, plugin authority, or data disclosure.
