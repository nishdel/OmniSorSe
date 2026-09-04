# Changelog

**Document type:** Cumulative historical change record

Entries below preserve release-specific terminology and claims. Use
[Release History](../RELEASE_HISTORY.md) for the concise branch/date/merge
index, [Release Status](RELEASE_STATUS.md) for current readiness, and
[Product Roadmap](../PRODUCT_ROADMAP.md) for future planning.

## v2.12.0-rc prerelease — OmniSorSe Trusted Relationships & Context

Release source: GitHub `main`, including the history from
`v2.12-trusted-relationships-context`. This is an unsigned prerelease candidate
for final real-world/manual validation, not the v2.12.0 stable/GA release.

### Added and changed

- Cap independent identity, content, named-context, lexical, tag,
  structural/temporal, and semantic evidence families; semantic/AI evidence
  cannot qualify alone.
- Add reversible Related, Not Related, and Use automatic pair authority plus a
  bounded correction view.
- Aggregate multiple typed edges into one direct Related Files/Explorer target
  without discarding persisted detail.
- Use bounded indexed candidate buckets, compact batch hydration, and
  resumable relationship-only version refresh for large libraries.
- Make direct Related Files independent of the optional Knowledge Graph and add
  Search/Files entry points.
- Write `.oms-state` format 2 with authored Smart Collection authority while
  retaining exact format-1 import.

### Boundaries

- Schema remains 6 and Explorer Protocol remains 1.0.
- Knowledge Graph remains optional/derived; Smart Collections remain grouping
  authority.
- No new AI relationship inference, production dependency, clustering engine,
  Search-ranker redesign, or file-mutation path is included.

## Unreleased — OmniSorSe v2.11 Supported Runtime & Platform Readiness

Implementation branch: `v2.11-supported-runtime-platform-readiness`. This work
is not a published release and builds directly on the committed v2.10 candidate.

### Added and changed

- Move every solution project and package path to .NET 10 LTS (`net10.0`) using
  one SDK/runtime authority.
- Preserve the existing package set; remove only net8-specific transitive
  compatibility entries that no longer resolve.
- Record and validate target framework, RID, bundled runtime, self-contained
  status, semantic version, configuration, and exact source commit in packages.
- Add bounded native package-smoke validation to Windows, Ubuntu, and macOS CI
  and pin critical Actions to immutable commits.
- Make Created range filters use filesystem-created time consistently with the
  Created Year facet while keeping media capture evidence distinct.
- Preserve managed legacy `net8.0` plugin manifests while current manifests
  advertise `net10.0`.

### Boundaries

- Schema remains 6 and Explorer Protocol remains v1.
- No Search-ranker, classifier, Smart Tag, recipe, Change Plan, AI, graph,
  OmniBrille, updater, Linux-package, or production-dependency expansion is
  included.
- macOS/Linux mutation support remains conservative; cross-target compilation
  is not represented as native runtime or interactive evidence.

## Unreleased — OmniSorSe v2.10 Production Hardening & Operational Resilience

Implementation branch: `v2.10-production-hardening-operational-resilience`.
This work is not a published release and builds directly on the committed v2.9
release candidate.

### Added and changed

- Enforce one current-user writer per profile and record abnormal shutdowns.
- Preserve and fail closed on corrupt Change Plan or Operation Journal state.
- Add bounded schema/store/profile/source/job/storage health inspection.
- Bound managed PDF extraction and PDFium rasterization inputs and work.
- Add reviewed logical export/restore for difficult-to-reconstruct user state,
  including exact-pair manual relationship decisions.
- Coordinate Forget across schema-6 SQLite and rebuildable compatibility caches.
- Centralize product version/source provenance and validate packaged metadata.
- Preserve all unrelated configuration during logging/diagnostic normalization.
- Harden Ollama prompts against instructions embedded in untrusted file data.
- Add high-blast-radius corruption, rollback, deletion, parser, profile-lock,
  configuration, provenance, and backup/restore regression coverage.

### Boundaries

- Schema remains 6 and Explorer Protocol remains v1.
- No Search, classifier, organization, graph, OmniBrille, cloud, autonomous,
  mutation-engine, protocol, or production-dependency expansion is included.
- PdfPig extraction and PDFium rasterization remain in process behind strict
  bounds; parser-internal allocation and native crash/hang risk are not claimed
  eliminated and remain manual/native validation gates.

## Unreleased — OmniSorSe v2.9 Reviewed Intelligent Organization

Implementation branch: `v2.9-reviewed-intelligent-organization`. This work is
not a published release and builds directly on the committed v2.8 release
candidate.

### Added and changed

- Connect explicit bounded Files, Search, and current Saved View selections to
  the existing persistent recipe authority using stable indexed file IDs.
- Add a cancellable ephemeral Organization recipe preview with trusted evidence
  mappings, literal coverage, Reliable/Needs review/Cannot propose states,
  fallbacks, privacy warnings, and conflict-first bounded rows.
- Promote a small modern token picker for original name, accepted/uniquely
  Strong deterministic Theme and Document Type, explicit filesystem-created and
  filesystem-modified dates, and coarse file category.
- Preserve naming and destination as independent patterns and always retain the
  exact original extension in the reviewed Organization workflow.
- Budget file and deduplicated directory-creation actions together before Change
  Plan creation, with a hard 1,000-file and 1,000-total-action limit.
- Re-resolve stable IDs, evidence, targets, collisions, and bounds before
  handing the explicit request to the existing Change Plan.
- Use **Organization recipe** as the user-facing name for the compatible
  `SortingRecipe` library and add safe built-in examples with honest date
  semantics.

### Boundaries

- Schema 6, the atomic workflow-library JSON store, Search/facets/Saved Views,
  Smart Tag authority, Change Plan execution/journal/reconciliation/Undo,
  Explorer Protocol v1, and OmniBrille remain unchanged.
- No autonomous/watched-folder recipe execution, cross-root moves, metadata
  writeback, new AI behavior, cloud service, schema migration, protocol field,
  mutation engine, or production dependency is included.

## Unreleased — OmniSorSe v2.8 Guided Workflows & Product Coherence

Implementation branch: `v2.8-guided-workflows-product-coherence`. This work is
not a published release and builds directly on the committed v2.7 release
candidate.

### Added and changed

- Add a bounded discovery context that opens a Search result in Files by stable
  file ID and restores query, canonical facets, Saved View, and review mode.
- Add continuous unresolved Moderate Smart Tag review with bounded evidence,
  previous/next navigation, and automatic current-membership refresh after an
  explicit Keep or Dismiss decision.
- Replace session-only Home status with bounded durable library, readiness,
  pending-review, Saved View, and optional-capability projections.
- Add Find, Understand, Review, and Organize entry points into existing product
  surfaces without adding destinations or bypassing prerequisites.
- Retire redundant legacy Smart Tag Search selectors; v2.7 facets remain the
  sole canonical Theme, Document Type, and User Tag filter model.
- Allow accepted, explicit User Tag, and Strong deterministic classification
  evidence to inform bounded editable rename suggestions with visible authority.
- Move ordinary Search maintenance and repair actions behind collapsed advanced
  disclosure while retaining critical readiness and safety information.

### Boundaries

- Schema 6, deterministic Search ranking/candidate selection, facet SQL, Saved
  View rules, progressive indexing, Smart Tag taxonomy/authority, Change
  Plans/Undo, Explorer Protocol v1, and OmniBrille remain unchanged.
- No cloud service, new production dependency, automatic organization,
  protocol field, schema migration, classifier redesign, or metadata writeback
  is included.

## Unreleased — OmniSorSe v2.7 Scalable Faceted Discovery

Implementation branch: `v2.7-scalable-faceted-discovery`. This work is not a
published release and builds directly on the committed v2.6 release candidate.

### Added and changed

- Select complete-library Search candidate eligibility in SQLite before
  bounded hydration into the existing filename-first deterministic ranker.
- Report candidate eligibility, plausible matches, hydration count, and any
  deliberate bound separately from indexing coverage and displayed results.
- Add database-backed Theme, Document Type, User Tag, file-type,
  filesystem-created-year, and filesystem-modified-year facets with contextual
  counts, OR-within-type, and AND-across-type semantics.
- Add dynamic local Saved Views that persist canonical query/filter rules and
  reevaluate current index contents without copying result membership.
- Add unresolved Moderate Smart Tag discovery using the existing user-authority
  review actions.
- Add bounded CSV/TSV native evidence and conservative XLSX/PPTX text evidence
  without executing formulas, macros, embedded objects, or external resources.

### Boundaries

- Schema 6, Explorer Protocol v1, Smart Tag authority, filename ranking,
  progressive indexing, Change Plans, and OmniBrille separation remain intact.
- No entity facet, bulk dashboard, Ollama classifier, embeddings, metadata
  writeback, automatic organization, cloud service, or new production
  dependency is included.

## Unreleased — OmniSorSe v2.6 Explainable Smart Tags

Implementation branch: `v2.6-explainable-smart-tags`. This work is not a
published release and builds on the committed v2.5 release candidate.

### Added and changed

- Add schema-6 normalized Smart Tag definitions, file assignments, user
  decisions, classification status, indexed canonical filters, transactional
  schema-5 migration, and conservative legacy authority import.
- Add small versioned English-first Theme and Document Type taxonomies plus
  bounded freeform local User Tags.
- Add deterministic grouped-evidence classification with Strong, Moderate, and
  Limited bands; no-evidence/conflict states; bounded reasons; and protection
  against filename dominance and correlated-evidence double counting.
- Add a durable deferred Smart Tag stage that reuses retained document, OCR,
  transcript, metadata, and Content Intelligence evidence without delaying
  v2.5 Fast/searchable-first coverage.
- Add exact Smart Tag Search evidence and typed canonical filters with OR within
  one type and AND across types while preserving filename-first ranking.
- Add Files details actions for accepting/rejecting suggestions, User Tags,
  decision reset, generated-tag clearing, and View files with this tag.
- Add bounded native `.txt`, `.md`, `.markdown`, and `.text` extraction.

### Boundaries

- No source metadata writeback, embeddings/vector database, cloud classifier,
  automatic file mutation, Smart Tags dashboard, OmniBrille modification, or
  Explorer Protocol v1 change is included.

## Unreleased — OmniSorSe v2.5 Workflow Completion & Indexing Quality

Implementation branch: `v2.5-workflow-indexing-quality`. This work is not a
published release.

### Added and changed

- Reconcile Files, Search/index refresh inputs, duplicate projections, details,
  and logical selection from terminal Apply/Undo journal outcomes rather than
  assuming the reviewed Change Plan fully succeeded.
- Preserve stable file identity for successful rename/move projection updates;
  mixed rollback outcomes remain explicit and refresh only affected configured
  sources.
- Add persisted **Fast — searchable first** and **Deep initial analysis**
  scheduling choices while retaining existing capability switches and durable
  jobs.
- Distinguish discovery, usable base Search coverage, and continuing deeper
  analysis in progress presentation.
- Clarify the Suggest → Review Changes → execute journey and the bounded
  current-page scope of folder suggestions.
- Add an optional **Open in OmniBrille** action that discovers a separately
  installed companion only on demand, authorizes enabled indexed sources, and
  transfers one independent short-lived Protocol v1 session through the
  companion's established one-time current-user handoff pipe. Bearer material
  is not placed on the command line or disk.

### Boundaries

- Explorer Protocol v1 wire contracts/version and schema 5 are unchanged; the
  companion bootstrap is an additive desktop integration boundary.
- No server/cloud architecture, autonomous organization, embeddings/vector
  store, graph renderer, voice, or OmniBrille rendering/Context functionality
  is included.

## v2.4.0 — OmniSorSe Transition & Explorer Foundation

Release branch: `v2.4-omnisorse-transition`. Published after genuine Windows
profile/installer transition, external two-process protocol, full automated,
cross-target, exact-main, and native packaging validation.

### Added and changed

- Active product, desktop assembly/apphost, visible package, installer, macOS
  application, and future artifact identity change to OmniSorSe.
- Established OpenSorSe profile paths, schema 5, installer AppId/install
  directory, macOS bundle identifier, repository URL, and internal namespaces
  remain compatibility identities so the rename does not fork user state.
- Dependency-free Explorer Protocol 1.0 contracts plus an on-demand,
  current-user local named-pipe host expose authenticated, source-scoped,
  bounded, read-only Structure, unified Search, Related/context, and safe detail
  projections for a future separately distributed OmniExplorer.
- Protocol limits, stable errors, cancellation, backpressure, session expiry,
  opaque node identity, strict serialization, diagnostic redaction, security
  tests, architecture guidance, and an evidence-separated manual checklist.

### Safety and limitations

- OmniExplorer is not implemented or released. No graph renderer, layout/GPU
  dependency, voice surface, standalone scanner, external listener, cloud
  relay, or protocol mutation operation is included.
- The host is dormant until an explicit authorized session is requested;
  normal OmniSorSe startup and Search do not depend on a companion.

## v2.3.0 — Content Intelligence & Local Understanding

Release branch: `v2.3-content-intelligence`. Published after local,
exact-main hosted, and native packaging gates.

### Added

- Provider-neutral bounded topics, textual entities, keywords, extractive
  summaries, provenance, and processing fingerprints over already indexed
  document/media evidence.
- Optional process-isolated adapter for a user-managed whisper.cpp CLI and
  local GGML model, including bounded timestamp segments, cancellation,
  timeout, safe temporary audio, and cache invalidation.
- Schema-5 nullable Content Intelligence persistence with transactional
  migration from v2.2 schema 4, malformed-record fallback, privacy inspection,
  clearing, and byte reporting.
- Explicit topic, textual-entity, and source-grounded-summary Search signals,
  explanations, and snippets plus corroborated cross-media Related Files
  evidence.
- Content Intelligence settings, indexed-data controls, built-in Help, design
  guide, and evidence-separated manual checklist.

### Safety and limitations

- Exact filename and literal Search tiers remain authoritative. Optional AI and
  derived evidence cannot invent file membership.
- No whisper.cpp runtime/model, ffmpeg, Tesseract, visual model, learned
  embedding model, or vector database is bundled or downloaded.
- No concrete visual-description provider, cloud transcription, telemetry,
  facial recognition, person identification, or autonomous organization is
  added.

## v2.2.0 — Media Intelligence

Release branch: `v2.2-media-intelligence`. Published as the Media Intelligence
release after local, exact-main hosted, and native packaging gates.

### Added

- Provider-neutral bounded media metadata, transcription,
  representative-frame, visual-description, and thumbnail contracts integrated
  with the durable indexing pipeline.
- Deterministic JPEG/PNG/WebP/BMP/TIFF header metadata, bounded EXIF
  make/model/orientation/capture/GPS parsing, and lazy cached still-image
  thumbnails.
- Optional safely invoked `ffprobe` audio/video metadata and optional capped
  `ffmpeg` representative frames, with explicit capability/unavailable states.
- Schema-4 shared media evidence, typed Search signals/snippets/explanations,
  media privacy inspection/clearing, aggregate diagnostics, storage accounting,
  and conservative transcript/OCR/device/capture relationship signals.
- Media Intelligence Settings and Help covering independent capabilities,
  conservative size/duration/frame/text limits, optional dependencies, and
  privacy.
- Truthful smoothed scan ETA after sufficient comparable work; combined
  duplicate-recovery plans across multiple groups with an independent keeper
  invariant; and a clearer primary/secondary navigation hierarchy with Search
  and Related Files as unique primary destinations.
- Scroll-bounded Virtual Collections and Related Files layouts, clearer
  local-indexing versus optional remote-endpoint wording, and EXIF-oriented
  still-image previews.
- External media-tool deadlines are classified as isolated provider timeouts,
  while caller cancellation remains cooperative cancellation; both paths
  terminate the process tree and remove owned temporary work.

### Safety and limitations

- No transcription or visual-description implementation, model, codec pack, or
  external executable is bundled. The contracts report unavailable cleanly.
- Exact filename/literal Search remains authoritative; weak optional
  descriptions cannot identify new files or create relationships alone.
- No source file is changed, no media is silently uploaded, and no telemetry,
  facial recognition, person identification, geocoding, or cloud service is
  introduced.
- Controlled Windows ffprobe/ffmpeg, image/preview, real Tesseract OCR/Search,
  cancellation/timeout, and published-v2.1 schema-3 migration checks are
  recorded separately from still-unperformed interactive and native
  Linux/macOS checks.

## v2.1.0

Release branch: `v2.1-search-ai-quality`. Published as the Search & AI Quality
release after local and hosted release gates.

### Added

- Explicit exact filename-stem, filename-prefix, and filename-substring ranking
  signals plus bounded adjacent-letter transposition matching.
- Default-off, per-query local-AI ordering for at most 12 already-ranked known
  results, with strict schema/identity validation and deterministic tier
  fencing.
- Provider-confirmed Ollama running-model discovery with graceful installed-
  model fallback when runtime status is unavailable.
- Cross-platform **Copy full path** Search result action and truthful
  indeterminate indexing progress while discovery has no known total.
- Monotonic live scan elapsed time that freezes at terminal operation state.
- Reviewable safe duplicate-removal plans that preserve a known keeper and move
  selected copies into an excluded recovery area through the existing executor.
- Compact dismissible categorized notifications without deleting durable
  diagnostic evidence.
- Contextual Help topics for the current product, including Watched Folders,
  Workflows, Related Files, Privacy, and Troubleshooting.

### Changed

- Filename matches are stronger and more explicit without allowing one weak
  filename word to overwhelm a multi-field or exact document-text match.
- Missing persisted Ollama models fall back to a deterministic installed model
  in the editable Settings draft with a clear save requirement.
- Ollama endpoint, timeout, missing-model, malformed-response, and cancellation
  outcomes preserve ordinary deterministic Search.
- Local file-analysis/indexing settings are separate from optional AI settings;
  only verified loopback endpoints are labelled local.
- The ordinary graph-navigation label is **Related Files** and Search shows
  **Hybrid** or **Hybrid + AI assistance** without changing internal contracts.

### Safety and compatibility

- AI cannot discover or invent files, cross deterministic relevance tiers,
  modify scores, or mutate original files.
- Search assistance sends no absolute paths, vectors, complete index, or whole
  documents. A configured non-local endpoint receives the explicitly enabled
  bounded query/candidate text.
- No database/index schema or source-file mutation boundary changed.
- Duplicate recovery moves remain reviewable, journalled, and undoable; they do
  not permanently delete data or immediately reclaim disk space.

## v2.0.0

Release history: v1.7 Deep Indexing, v1.8 Search, v1.9 Relationships/Smart
Collections, the v2.0 stability design, and the v2.0 Knowledge Graph candidate
are integrated without squashing their version ancestry.

### Added

- Optional, default-off, provider-neutral Knowledge Graph projection over
  stable files, sources, folders, virtual Collections, exact-content document
  sets, and explicit manual entities.
- Isolated schema-1 SQLite derived and decision sidecars with completed
  manifests, active-generation publication, leases/fencing, watermarks,
  privacy floors, recovery points, repair, and corruption/newer-schema
  handling.
- Bounded accessible graph list/detail/evidence/privacy/repair UI and optional
  explainable Search context that remains subordinate to exact/literal ranking.
- Self-contained Windows x64 portable ZIP and per-user installer plus native
  Intel and Apple Silicon macOS app/DMG packages, package startup/shutdown
  probes, and a verified SHA-256 checksum bundle.
- Process-boundary startup/shutdown exception containment, observed indexing
  task faults, strict UTF-8 documentation validation, and defensive validation
  of internal SQLite identifiers.

### Changed

- `main` now includes the preserved v1.7, v1.8, v1.9, v2.0 design, and v2.0
  implementation history developed above v1.6.
- GitHub Actions use maintained Node.js 24 action majors and validate Windows,
  Ubuntu, and macOS. Native release packaging runs separately from ordinary CI.
- Assembly, file, informational, product, installer, and bundle versions are
  consistently `2.0.0` (`2.0.0.0` where four-part metadata is required).
- User, contributor, platform, privacy/security, installation, packaging, and
  release documentation is reconciled to the current implementation.

### Safety, privacy, and limitations

- Search, indexing, OCR/AI analysis, Relationships, Smart Collections, and
  Knowledge Graph operations never modify original files. Explicit reviewed
  Change Plans remain the production mutation boundary.
- Graph facts require retained evidence; ambiguous identities remain separate;
  decisions and forgetting fences survive derived rebuilds.
- Windows artifacts are unsigned and macOS artifacts are unsigned/unnotarized
  unless the official release page explicitly records otherwise. Linux remains
  source-build only for v2.0.0.
- Broad interactive/community validation begins after publication and is not
  claimed by automated package or CI evidence.

## v1.9.0

Release branch: `v1.9-relationships-context`.

### Added

- Provider-neutral relationship engine, storage, service, Search-expansion,
  evidence, collection, context, timeline, privacy, repair, and diagnostic
  contracts.
- Deterministic bounded relationship discovery from concrete retained signals,
  with versioned algorithms and Low/Medium/High confidence instead of invented
  percentages.
- Embedded SQLite schema 3 for candidate features, relationships/evidence,
  pair corrections, Smart Collections/membership, forgotten projections, and
  aggregate diagnostics, including transactional migration from schema 2.
- Accessible Collections and Related Files surfaces with inspectors, sorting,
  filtering, timeline, manual link/unlink, confirm/reject, always/never,
  rename/pin/merge/split, privacy, rebuild, and repair controls.
- Synthetic deterministic relationship, migration, corruption, Search
  integration, accessibility, and bounded performance regression coverage.

### Changed

- The existing durable `RelationshipAnalysisCompleted` stage now produces
  incremental evidence-backed output and tracks relevant configuration in its
  processor fingerprint.
- Search may add bounded direct related-file context after ordinary v1.8
  ranking. Exact and literal matches remain above context-only results, and a
  per-query control can disable expansion.
- Index privacy inspection, storage breakdown, forgetting, cleanup, and repair
  include relationship-derived data without modifying source files.
- Product, assembly, file, manifest, and About versions are `1.9.0` /
  `1.9.0.0`.

### Preserved boundaries

- Every automatic relationship retains actual evidence and algorithm
  provenance. Semantic similarity alone cannot create a relationship.
- Smart Collections and timelines are virtual index projections; they never
  move or edit source files or invent unrecorded events.
- Ordinary Search remains useful when relationship analysis is disabled or
  unavailable. Ollama remains optional.
- v1.7/v1.8 indexing, Search, catalogs, watched folders, duplicate detection,
  workflows, plugins, Change Plans, Operation Journal, recovery, and Undo keep
  their existing contracts.
- No Knowledge Graph, conversational assistant, database server, tag, package,
  merge, or published release is added.

## v1.8.0

Release branch: `v1.8-search-intelligence-privacy`.

### Added

- Provider-neutral Search query, interpreted-filter, candidate, ranking-signal,
  explanation, snippet, coverage, privacy-inspection, forget, and repair
  contracts.
- Deterministic bounded query interpretation for common file type, extension,
  date, size, source, folder, tag, indexing, OCR, semantic, and failure filters
  with an injectable clock and visible removable filters.
- One coherent hybrid ranker with exact/literal tiers, bounded typo tolerance,
  optional semantic supplementation, explicit components, deterministic
  tie-breaking, bounded snippets, and source indicators.
- Search-quality evaluation over a synthetic corpus, relevance metrics, and
  separate bounded performance regression tests.
- User-facing indexed-data inspection, file/source forgetting, per-file
  processing policy, selective clearing, and targeted durable repair controls.
- SQLite schema 2 privacy rules with transactional migration, recovery copy,
  parameterized operations, and corrupt-ranking-data fallback.

### Changed

- Search distinguishes excluded, OCR-waiting, AI-waiting, failed-stage,
  unavailable-index, and incomplete coverage without treating partial results
  as exhaustive.
- Search diagnostics record duration, counts, filters, coverage, and ranking
  stages without complete query text, snippets, extracted content, or absolute
  paths.
- Summary and semantic processing can be disabled independently while filename,
  folder, metadata, text, OCR, filtering, ranking, snippets, and explanations
  remain local and usable without Ollama.
- Product, assembly, file, manifest, and About versions are `1.8.0` /
  `1.8.0.0`.

### Preserved boundaries

- v1.7 indexes migrate without silent loss; existing catalogs, saved scans,
  watched folders, duplicate detection, workflows, plugins, Change Plans,
  Operation Journal, recovery, and Undo remain compatible.
- Forget and repair actions alter only application-owned indexed data. Original
  user files are never deleted or modified.
- No database server, remote query service, improvised encryption,
  conversational assistant, autonomous organization, package, tag, installer,
  merge, or release publishing is added.

## v1.7.0

Release branch: `v1.7-deep-indexing-foundation`.

### Added

- Provider-neutral durable background-index contracts and a separate embedded
  SQLite provider with schema 1, migrations/backups, integrity checks,
  transactions, WAL/full synchronization, recovery, and disposal.
- Basic, Standard, and Deep indexing policies; stable file identity/content
  fingerprints; incremental invalidation; duplicate content sharing; deletion
  retention; exclusions; storage quota/maintenance; and bounded chunks/text.
- Persistent staged jobs with pause, resume, safe cancellation, retry,
  dependency waiting, prioritization, source removal, rebuild, and
  interruption recovery. Interrupted discovery resumes without resetting
  completed work; paused/cancelled state remains explicit across restart.
- Progressive Search documents/coverage and accurate indexing progress,
  counts, speed, sample-gated ETA, storage usage/breakdown, failures, and
  privacy-safe diagnostics.
- Watched-folder source ownership, automatic eligible-wait recovery, explicit
  corruption/newer-schema recovery copies, and bounded backup sidecar cleanup.
- Expanded unit, integration, persistence, migration, corruption, concurrency,
  cancellation, recovery, ViewModel/accessibility, and bounded synthetic
  performance-regression coverage.

### Changed

- User-facing **Meaning Search** is now **Search**. Stable internal types,
  schemas, APIs, and migration identifiers retain compatible names.
- Search includes an accessible pointer/keyboard/touch/screen-reader help
  affordance and remains usable with an explicit partial-coverage warning.
- Product, assembly, file, manifest, and About versions are `1.7.0` /
  `1.7.0.0`.
- Search/index diagnostics are now an instrumented category.
- Search exposes a bounded failure inspector and direct current-run diagnostics
  navigation; compatible existing Search stays available if the derived store
  requires recovery.

### Preserved boundaries

- Existing JSON settings/catalog/content/Search/history/watch/workflow/plugin/
  Change Plan/Operation Journal data remains compatible.
- No PostgreSQL or database server, conversational Search, final hybrid ranker,
  cloud indexing, autonomous file organization, new mutation path, package,
  tag, installer, or release publishing is added.

## v1.6.0

Release name: **Reliability, Performance, and Production Hardening**<br>
Release branch: `v1.6-reliability-performance`.

- Consolidated all application-owned JSON stores onto one bounded, flushed,
  cancellation-safe atomic replacement primitive.
- Added normalized process-local transaction coordination across independent
  store instances without changing persisted schemas.
- Reduced duplicate-detection and Results-query transient allocations and made
  large projection/query paths cancellation-responsive.
- Bounded processing-session history and hardened background-task progress,
  cancellation classification, and observer isolation.
- Made watched-folder initialization/disposal concurrent-safe, awaited owned
  loops, observed background failures, and applied host path semantics.
- Added critical-workflow automation names and polite live status regions.
- Expanded concurrency, recovery, stress, cancellation, persistence,
  lifecycle, accessibility, and repository-policy tests.
- Expanded source CI to Windows, Ubuntu, and macOS with Debug/Release,
  zero-skip, analyzer, style, whitespace, documentation, and patch gates.
- Product/informational version is `1.6.0`; assembly/file/manifest version is
  `1.6.0.0`; About displays `1.6`.
- No tag, installer, package, updater, or published release is created by the
  source integration.

## v1.5.0

Release name: **Cross-Platform Foundation and Linux Preview**<br>
Release branch: `v1.5-cross-platform-foundation`.

- Added focused platform contracts and capability reporting for path semantics,
  application locations, file identity, filesystem checks, external tools, and
  desktop integration.
- Preserved the Windows local-data layout; added XDG configuration/data/state/
  cache separation on Linux.
- Added Windows volume/file-index and Linux device/inode identity with explicit
  metadata fallback limitations.
- Made Change Plan validation/execution, case handling, confinement, permission,
  same-filesystem moves, rollback, recovery, and Undo platform-aware without
  permitting overwrite or unreviewed mutation.
- Added portable, Windows-compatible, and current-platform recipe filename
  policies; existing recipes retain conservative portable behavior.
- Added exact plugin runtime-identifier constraints for native dependencies.
- Added configured-path and safe `PATH` discovery for Tesseract and explicit
  Windows/Linux desktop-opening adapters.
- Added Settings platform diagnostics, human-readable report export, platform
  matrix, Linux build/manual/troubleshooting guidance, architecture maps, and a
  Windows/Ubuntu CI matrix that publishes no artifacts.
- Product/informational version is `1.5.0`; assembly/file/manifest version is
  `1.5.0.0`; About displays `1.5`.
- No v1.5 tag, installer, package, updater, or published release is created by
  this implementation task.

## v1.4.0

Plugin Foundation and Extension SDK.

Release branch: `v1.4-plugin-foundation`.

### Added

- Standalone immutable, asynchronous, cancellation-aware Extension SDK with
  eight bounded analysis/proposal/import/export extension points.
- Strict manifest parsing, controlled manifest-first discovery, runtime and
  host compatibility, deterministic dependency graphs, integrity-change
  lockout, diagnostics, quarantine, and conflict-safe contribution registry.
- Dedicated collectible assembly-load contexts for external plugins with
  bounded initialization/stop, exception containment, and restart reporting.
- Explicit external enable/capability grants and four built-in reference
  plugins for metadata, classification, recipe fields, and JSON export.
- Transactional local ZIP install, fully validated upgrade with previous
  version preservation, dependency-aware confirmed removal, and archive
  traversal/link/native/bounds defenses.
- Exact plugin/version/contribution references in profiles and recipes,
  immutable resolution snapshots, recipe value/action provenance, watched
  fail-closed behavior, and import/export host boundaries.
- Settings > Plugins management, redacted diagnostics export, SDK/author/
  manifest/package documentation, and adversarial/runtime/workflow/UI tests.
- Audience-oriented documentation index, repository/developer/maintainer guides,
  authoritative architecture overview, and four-part Mermaid system map.
- Deterministic repository tests for case-correct relative documentation links,
  Mermaid structure, documentation entry points, production dependency policy,
  and public Extension SDK XML documentation.

### Changed

- Product/informational version is `1.4.0`; assembly/file/manifest version is
  `1.4.0.0`; About displays `1.4`.
- Workflow import/export schema identity advances to 1.4 and preserves exact
  plugin contribution references.
- Current installation/safety guidance, SDK call contracts, subsystem
  lifecycle/invariant comments, and legacy architecture link casing were
  corrected without changing product behavior.

### Safety

- Plugins can analyze, suggest, parse import proposals, or return export bytes;
  they cannot directly mutate user files, approve/apply Change Plans, write the
  journal, or receive the host service container.
- External plugins are disabled until explicit enable and capability grant.
- Missing, incompatible, changed, conflicting, failed, or quarantined
  capabilities fail closed with no silent workflow fallback.
- External code remains in-process with the current user's OS permissions.
  Assembly-load-context isolation is not a sandbox and SHA-256 integrity does
  not authenticate publishers.

## v1.3.0

Workflow Profiles and Recipe Library.

Release branch: `v1.3-workflow-profiles`.

### Added

- Typed, versioned workflow profiles and declarative sorting recipes with stable IDs, revisions, origins, capability/file/extraction/analysis/AI/plan/notification/scan policies, applicability, templates, fallbacks, normalization, collision/uncertainty policy, and preview examples.
- Five immutable duplicable profile defaults and four immutable duplicable recipe defaults.
- Bounded atomic `workflow-library.json` persistence with schema migration, corruption preservation/diagnostic copy, safe built-in recovery, lifecycle operations, dependency protection, usage inspection, and diagnostic export.
- Constrained field/date template parser and deterministic evaluator with Unicode/case/whitespace policy, portable invalid-character sanitization, reserved-device/length/root/traversal/collision checks, and full preview explanations.
- Immutable effective configuration resolution and historical workflow snapshots for manual scans, saved scans, watched cycles, and watched catalogues.
- Persistent watched-folder profile resolution, multiple permitted recipe selection, constrained overrides, configuration-change reconciliation, and explicit profile-unavailable state.
- Manual scan profile selection, capability/intensity summary, one-time narrowing, and save-adjusted-as-new-profile flow.
- Workflow/recipe provenance on Change Plan actions, including inferred directories, with profile/recipe revisions, values, evidence, deterministic/AI state, warnings, and unresolved fields.
- Versioned human-inspectable profile/recipe import/export with explicit conflict policy and size/depth/schema/dependency/template/capability validation.
- Dedicated Workflows profile/recipe/editor/preview/import/export UI with search, file/origin/capability/archive filters, usage, lifecycle actions, scan/watch routing, and diagnostics export.
- Comprehensive workflow persistence, migration, recovery, template safety, precedence, historical snapshot, Change Plan, AI-gate, transfer, and ViewModel tests.

### Changed

- Watched configuration/catalogue schemas advance to 3/2 for recipe lists, overrides, and workflow revision snapshots.
- v1.2 `default` maps explicitly to General Documents; session-only `current` recipes require deliberate replacement.
- Processing stages honor effective extraction, classification, duplicate, rule, AI, file-selection, and Change Plan settings.
- Product/informational version is `1.3.0`; assembly/file/manifest version is `1.3.0.0`; About displays `1.3`.

### Safety

- Workflow profiles automate configuration and analysis, not approval or file modification.
- Imported recipes cannot execute code, contain destructive recipe rules, escape an approved root, or overwrite a destination.
- Profiles cannot bypass global OCR/AI gates; item-level AI policy is checked before provider requests.
- All mutations continue through v1.1 review, approval, preflight, explicit Apply, journaling, verification, recovery, rollback, history, and Undo.

## v1.2.0

Watched Folders and Incremental Scanning.

Release branch: `v1.2-watched-folders`.

### Added

- Persistent watched-folder configurations with stable IDs, availability/status, subfolder scope, exact/pattern ignores, scan profile, sorting recipe, deterministic/AI switches, notification preferences, quiet period, size/hidden policy, timestamps, queue state, summaries, pending plans, and associated catalogue identity.
- Versioned atomic `watched-folders.json`, `watched-catalogues.json`, and grouped `watched-activity.json` stores with bounds, corruption preservation, schema migration, and missing-store compatibility.
- Replaceable `FileSystemWatcher` event source, canonical event/root validation, per-folder quiet-period debounce, duplicate burst grouping, directory/overflow escalation, and a bounded 256-batch single-reader queue with backpressure.
- Stable Windows file identity and portable best-effort identity, real-filesystem probes, file-stability observations, deferral/retry, and root-confined reparse-safe enumeration.
- Targeted incremental processing that preserves unchanged analysis and selectively reuses metadata, content/OCR cache, SHA-256, classification, duplicate, and rule infrastructure.
- Startup, pause/resume, reconnect, overflow, daily, user-triggered incremental, and full reconciliation workflows.
- Canonical ignore policy for exact paths, directories, filename/extension patterns, hidden/linked/internal/oversized items, and visible built-in temporary/incomplete-download patterns.
- Optional per-folder AI with global/capability/model gates, 12-item requests, a 120-item per-cycle backlog bound, cancellation, unchanged-content avoidance, persisted pending/completed/failed item state, provenance, independent failure, and pending/failed-only retry.
- Operation Journal path/identity correlation and verified post-operation reconciliation to suppress recursive suggestions without disabling watching for a fixed duration.
- **Watched Folders** desktop management, status, actions, grouped activity, precise notifications, explicit configuration-removal confirmation, and review routing.
- Automated configuration, store, ignore, event/debounce, processor, reconciliation, AI, correlation, stability, and ViewModel tests.

### Changed

- Product, assembly, informational, file, manifest, and About versions report `1.2.0` / `1.2.0.0`.
- Dedicated watched catalogues update in place without consuming or evicting entries from the separate opt-in Saved scans catalogue.
- Existing v1.1 deterministic and optional AI suggestions are reused to create reviewable Change Plans.
- Release branches follow `v<version>-<primary-feature>`.
- Late v1.1 Review Changes progress callbacks no longer overwrite the verified terminal execution status.

### Safety

- Watched folders automate detection and analysis, not file modification.
- Watcher events are hints and are reconciled with actual canonical in-root state.
- Overlapping roots are rejected to prevent duplicate ownership and processing.
- Missing/disconnected folders retain configuration, catalogue, and history.
- Ignored files never enter optional AI.
- Watched-folder processing never invokes `IChangePlanExecutionService`; every mutation remains behind existing v1.1 manual review, approval, validation, and explicit Apply.
- Journal-correlated OpenSorSe changes update catalogue state without repeated plans or AI analysis.

## v1.1.0

Safe File Operations and Robustness stable release.

### Added

- Persisted Change Plans with stable plan/action identities, source file identity snapshots, suggestion provenance, approval/validation state, warnings, conflicts, edit state, scan freshness, and forward-compatible action types.
- Review Changes UI with approve-all-safe, deselect-all, per-action approve/reject, editable filename/destination, action/issue filters, counts, validation, final confirmation summary, explicit Apply, progress, result summary, and Undo.
- Dedicated non-overwriting filesystem gateway and execution service for rename, move, and create-directory actions.
- Durable versioned Operation Journal with pending/running/action/terminal writes, pre/post identities, safe error categories, rollback and Undo facts, AI correlation metadata, and bounded human-readable report export.
- Immediate pre-execution revalidation, deterministic ordering, safe-boundary cancellation, result verification, reverse-order rollback, case-only rename handling, and startup Interrupted Operation inspection.
- Conflict-aware whole-operation and individual-operation Undo, including external modification, occupied original, later-operation dependency, and non-empty created-directory protection.
- `change-plans.json` and `operation-journal.json` atomic local application-data stores, legacy journal-array compatibility, and graceful corrupt-entry recovery.
- Automated safety tests using isolated temporary directories for planning, stale state, collisions, execution, rollback failure, verification failure, cancellation, Unicode/spaces, case-only rename, persistence, migration, restart recovery, partial Undo, history, and ViewModel apply gating.
- v1.1 user, safety, architecture, troubleshooting, manual-testing, and implementation documentation.

### Changed

- Accepted AI rename and folder-structure suggestions now create a Change Plan instead of ending at a decision record.
- Deterministic folder restructuring now routes applied moves through the same Change Plan validator, journal, execution, rollback, and Undo boundary.
- The advanced history destination is named **Operation History** and loads persistent journal records across restarts.
- Product, assembly, informational, file, manifest, and About versions report `1.1.0` / `1.1.0.0`.

### Safety

- No AI generation, parsing, retry, acceptance, or diagnostic path performs a filesystem mutation.
- Destinations are never overwritten and no automatic numeric suffix or implicit conflict resolution is used.
- Approved actions are revalidated immediately before mutation and the executed action list is immutable for that operation.
- Every attempted supported apply is journalled before mutation; successful actions carry verified inverse information.
- Rollback and Undo are reported successful only after verification. Unsafe inverse actions are blocked and journalled instead of overwriting newer data.
- Permanent deletion remains outside v1.1.

## v1.0.0

Integrated local-understanding and structure-history release candidate.

### Added

- A self-contained Windows x64 portable release layout with native `OpenSorSe.exe`, official embedded icon, version/product metadata, legal notices, installation guidance, ZIP archive, and SHA-256 checksum.
- A public-facing GitHub README with official branding and commented real-screenshot slots under `docs/images/`; no generated screenshot placeholders are shipped.
- Local, bounded metadata extraction for filesystem, PDF, Open XML, and image metadata with source provenance and per-file failure isolation.
- Optional OCR Beta through capability-detected local Tesseract CLI execution for images and rendered PDF pages, with PdfPig native page text, built-in PDFtoImage/PDFium rasterization, mixed-document page decisions, English/German language checks, and deterministic bounds.
- Page-level OCR provenance, engine/rasterizer-aware cache fingerprints, owned temporary-workspace cleanup, and stale-compatible cache migration.
- A separate default-off AI document-text interpretation capability with bounded page context, strict JSON validation, non-local endpoint warning, and an unverified review-only preview.
- A unified, default-off Advanced Diagnostics framework with one bounded process-memory store, category/status filtering, seven shared viewer tabs, correlation, redaction, copy/export/clear actions, and fully instrumented AI, OCR/text-extraction, and scanning sessions.
- Versioned small-model prompt contracts for rename, folder structure, and document interpretation, with exact Ollama JSON Schemas, deterministic DTO/property ordering, fail-closed grounding and identity validation, and snapshot tests.
- Machine-readable resolved dependency/license inventory, third-party notices, and automated unknown/forbidden-license protection.
- Provenance-aware confirmed, suggested, accepted, and rejected tags sourced from users, deterministic rules, file type/date/folder context, embedded metadata, local OCR, preferences, semantic inference, and optional AI review.
- Default-off local Semantic Search Beta with deterministic feature-hashing vectors, hybrid exact/tag/metadata/native-text/OCR ranking, match explanations, incremental refresh, cancellation, stale-file removal, and clear/rebuild controls.
- Versioned atomic `content-index.json`, `semantic-index.json`, and `structure-history.json` stores with explicit bounds and controlled corrupt optional-index recovery.
- Advanced Structure history page with root/status filters, source/proposed/applied/current snapshots, bounded tree projection, accessible text, and Added/Removed/Moved/Renamed/Unchanged comparison labels.
- Deterministic preview-first root-level folder proposals, separately confirmed bounded apply, current-root revalidation, traversal/reparse/conflict/overwrite protection, rollback attempts, and per-item outcomes.
- Successful-apply repeat protection, incremental proposals for new files, material-change detection, and an explicit **Propose restructuring again** override.
- Contextual Help for Semantic Search Beta and Structure history.

### Changed

- The Desktop output assembly is named `OpenSorSe`, so public builds expose `OpenSorSe.exe` rather than an implementation-oriented executable name.
- Replaced the page-heavy shell with six everyday destinations: Home, Scan, Files, Duplicates, Saved scans, and Settings; advanced tools are grouped separately and Help/About are in the footer.
- Consolidated the saved scan library, saved-scan search, and advanced scan comparison under one Saved scans workspace.
- Exposed local Semantic Search as **Meaning Search (Beta)** from the Files search area rather than as an unrelated top-level destination.
- Redesigned Files around one primary search, an on-demand filter drawer, a bounded file list, and a selection-only details/File Assistant panel.
- Added a persistent bottom status bar with active-operation details and shared cancellation for scans, Meaning Search, and AI requests.
- Added a warmer theme-resource system, semantic feature colors, layered cards, selected navigation state, compact brand mark, friendly empty states, and a metric-tile Home layout.
- Replaced the placeholder shell/window icon with the official compact OpenSorSe mark and added the expanded product name and tagline to the roomier sidebar brand block.
- Made the Files table/details boundary draggable and keyboard adjustable, with 450/320 device-independent-pixel minimums and a validated, persisted 20–50% details-width preference.
- Added subtle alternating Files rows, clearer hover/selection feedback, improved row spacing, and keyboard-resizable table columns.
- Replaced technical user-facing terms such as Results, Saved catalog, Compare snapshots, Semantic Search, Diagnostics, and Operation history with plain-language labels while retaining stable internal type names.
- Results search/filter/status controls remain fixed while the virtualized result list scrolls independently.
- Duplicate View keeps its group list visible and opens selected details in a responsive right-side drawer with Escape/close support.
- Global **Enable AI** and **Advanced features** controls remain visible in the navigation shell and synchronize with Settings.
- Assembly, package, informational, file, and About versions report `1.0.0`.
- Advanced navigation now includes Structure history; Semantic Search Beta remains independently enabled and does not require AI or Advanced mode.
- Existing v0.9.1 settings, catalog schemas, accepted tags, saved searches, and AI decisions remain readable with safe defaults for new settings.
- English/German search normalization now folds diacritics, splits punctuation/extensions, retains ISO dates, and adds conservative suffix variants without a model.
- Folder-structure suggestions now reject selections above the 12-file contract bound before contacting Ollama and state the exact count; no file is silently omitted and no partial plan is shown.
- Updated test-only xUnit packages to remove the obsolete .NET Standard dependency chain flagged by the NuGet vulnerability audit; production dependencies and packaged runtime files are unchanged.

### Safety

- Scanning, OCR, extraction, indexing, duplicate review, diagrams, and AI suggestions never modify source files.
- AI remains default-off, capability-specific, untrusted, and suggestion-only; bounded extracted text can leave the process only through its separate opt-in and explicit one-file request, and no AI result enters a filesystem operation.
- The only new source-file mutation is a deterministic restructuring plan applied after a separate exact-preview confirmation. It moves only listed files under one explicit root and never overwrites or deletes.
- Raw OCR/document text and semantic vectors are excluded from ordinary logs.
- Advanced diagnostic content is retained only in bounded process memory unless explicitly exported, is redacted by default, is cleared on disable/exit, and removes credential-like values even in unredacted mode.
- The portable package now includes copied runtime dependency licence/notice files alongside the reviewed machine-readable dependency inventory.

### Fixed

- Selecting a visible Files row now immediately updates File Assistant context, so rename suggestions no longer remain incorrectly disabled until a later query refresh.
- File Assistant now explains every common disabled state and distinguishes not configured, unchecked, unavailable server, available server, missing model, ready, running, failed, and cancelled readiness.
- Cancelled, failed, unavailable, timed-out, and invalid AI results return to idle and remain retryable with a fresh cancellation source.
- Added explicit connection retry, exact selected-model validation, and display of the actual model used by the latest validated suggestion.
- Switching models after a failed request now causes the next request to use the newly configured exact model rather than retaining stale presentation state.
- Generated content tags no longer trigger a re-entrant Results refresh, and loading them no longer replaces the deterministic extension tag.
- Hiding or clearing the selected-file details panel now returns all available width to the Files table instead of leaving an empty reserved column.
- Navigation falls back safely when Advanced mode hides Structure history or any other selected advanced page.
- Changed roots are rejected between restructuring preview and apply, preventing stale proposals from moving files.
- Failed or preview-only restructuring records cannot activate repeat protection.
- Mixed PDFs no longer skip scanned pages merely because another page contains enough native text.
- Content reprocessing preserves accepted/user tags and same-source rejection decisions instead of replacing them with regenerated candidates.
- Successful OCR capability detection is refreshable and validates every configured Tesseract language before recognition.
- `AvaloniaUI.DiagnosticsSupport` was removed because its resolved package metadata did not declare a license; built-in OpenSorSe diagnostics remain available.

## v0.9.1

Focused optional-AI and interface-complexity refinement; this is not the v1.0 milestone.

### Added

- Default-off global **Enable AI features** and **Show advanced features** settings.
- Independent default-off file-rename and folder-structure suggestion capabilities.
- Central feature requirements shared by navigation, views, commands, Settings, and application services.
- Capability-specific deterministic metadata-only prompt builders with explicit size bounds.
- Strict JSON response contracts, parsing, identity/graph/count/confidence checks, and portable filename/path validation.
- Review, edit, accept, and reject proposal workflow that records local decisions without executing them.
- Typed Ollama missing-model, timeout, cancellation, unsupported-response, malformed/empty/oversized-response, and connection failure handling.
- A bounded, newest-first 500-event process-session diagnostic viewer with severity/category filters, safe details, and copy support.
- Optional live AI request diagnostics in a separate non-modal window, bounded to 20 memory-only records and available only when AI, advanced mode, and the explicit diagnostic switch are all enabled.
- Separate default-off unredacted diagnostic-content opt-in; redacted display retention remains the default and disabling diagnostics clears history.
- Ollama generation now sends a capability-specific JSON Schema aligned with prompt and C# validation contracts, while retaining raw HTTP envelopes separately from extracted assistant content.
- Precise structured-response diagnostics now report actual JSON types, including the former generic invalid-`reason` failure.
- Contextual Help from every major page, with topic-specific workflow, safety, error, and related-topic guidance.
- A responsive **Duplicate View** with per-file details and explicitly requested, capped opening of known files or containing folders through a testable launcher abstraction.
- Reusable severity-labelled status presentation for Settings, AI, Diagnostics, Catalog Search, and Duplicate View.

### Changed

- Raw provider/request diagnostics, detailed logging, historical comparison, detailed diagnostics, and operation-history internals are classified as advanced.
- Essential Ollama endpoint, connection check, model discovery/selection, timeout, and capability controls are visible whenever AI is enabled; only raw request inspection and other technical detail require advanced mode.
- Ollama endpoint normalization accepts safe HTTP(S) base paths and strips known `/api`, `/api/tags`, and `/api/generate` suffixes before building request URIs.
- Provider operations use one request-scoped timeout from 5 through 300 seconds instead of competing `HttpClient` and request timeouts.
- Model discovery preserves the configured exact model and reports it unavailable instead of silently selecting another model.
- AI requests report typed progress stages and preflight the selected exact model before generation.
- Folder prompts use deterministic request-local `item-NNN` identities, report included/omitted counts, and require every included item exactly once.
- Catalog Search now prioritizes search, has one result/status surface, supports clear and rename workflows, and separates saved-search maintenance.
- Settings preserves the current scroll offset across visibility-driven layout changes.
- The earlier mixed AI organization proposal is narrowed to rename only; AI no longer proposes tags, deterministic categories, or file destinations in v0.9.1.
- About and assembly versions report `0.9.1`.
- Generated validation directories are ignored and removed from source control.

### Safety

- AI is disabled by default, and disabled or invalid requests are rejected before provider invocation.
- Ollama remains optional, local-first, and externally managed; a custom endpoint may be remote.
- Requests exclude file content and absolute paths, and model output is always treated as untrusted.
- No AI result renames, moves, creates, deletes, overwrites, or edits a file or folder.

### Fixed

- Hidden-page navigation now rejects stale/direct access and falls back safely when the selected page becomes unavailable.
- Changing Results context now cancels in-flight AI work and clears stale proposals before they can be reviewed against another file.
- A rename edited back to the current filename is treated as no change and is not saved as an accepted decision.
- Provider-diagnostic transport failures are normalized instead of escaping the application boundary.
- Folder validation rejects reserved system-directory names and duplicate logical paths rather than silently normalizing them.
- Undefined internal suggestion kinds and invalid provider timeouts are blocked before network transport.
- Quoted JSON authorization values are redacted from opt-in AI diagnostics.
- Raw AI diagnostic capture can no longer bypass the advanced-mode requirement through an application-service call.
- Diagnostic event capture and clipboard failures remain isolated from scanning and other primary workflows.

## v0.9

See the preserved [v0.9 release proposal](Implementation_Spec/v0.9/00_v0.9_Release_Proposal.md) and [audit corrections](Implementation_Spec/v0.9/AUDIT_CORRECTIONS.md) for the historical snapshot-comparison release.
