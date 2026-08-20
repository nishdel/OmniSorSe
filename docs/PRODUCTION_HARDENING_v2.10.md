# OmniSorSe v2.10 Production Hardening & Operational Resilience

**Status:** unreleased implementation candidate on
`v2.10-production-hardening-operational-resilience`; maintainer manual validation pending.

v2.10 changes the engineering substrate, not Search, classification,
organization, graph, Explorer Protocol, or OmniBrille behavior. Schema remains
6 and Explorer Protocol remains v1.

## Profile and recovery authority

The desktop acquires one current-user, profile-specific operating-system mutex
before opening profile state. A dedicated owner thread holds the thread-affine
mutex so any orderly shutdown continuation can release it. Process death lets
the operating system abandon and release ownership. A second process receives
an explicit startup failure and cannot construct profile services. Isolated
tests/helpers use an explicit no-ownership state.

Change Plans and the Operation Journal are mutation/recovery-authoritative.
Malformed, truncated, unsupported, or invalid state is copied for forensic
recovery, left intact, surfaced, and blocks new execution and Undo. Read-only
discovery may continue. A valid empty store remains valid empty state.

JSON stores follow three policies:

- rebuildable derived stores may be cleared and rebuilt;
- user-authored stores preserve invalid input and require an explicit reviewed
  save/restore before replacement;
- mutation/recovery stores fail closed and block mutation.

Settings may start with safe defaults after a load warning, but the invalid
file is not replaced until an explicit Settings save or state restore.

## Logical state backup and restore

**Export OmniSorSe State** creates one bounded `.oms-state` ZIP with exactly
`manifest.json` and `state.json`. The current writer records format 2, schema 6,
application version, source revision, build configuration, creation time, and
the SHA-256 payload digest. Creation uses a same-directory temporary file and
atomic replacement. On Unix, the resulting file is owner-read/write.

The logical payload contains settings, registered/watched source definitions,
user workflow profiles and organization recipes, Saved Views, explicit User
Tags, accepted/rejected Smart Tag decisions, and manual relationship/pair
decisions. v2.12 format 2 also includes exact-pair authority and user-authored
Smart Collection titles, pins, manual/merged identity, manual membership,
exclusions/splits, and intentional tombstones. The reader continues to accept
format 1. Relationship authority uses exact stable file pairs and never path
guessing. It intentionally excludes
extracted text, OCR, transcripts, thumbnails, generated classifications,
Search/index data, background jobs, Change Plans, and journals. The archive can
contain private source paths and authored labels and must be protected as
sensitive user data; it is not encrypted. The independently journalled
Knowledge Graph decision sidecar is not part of format 2 and remains a separate
recovery/export limitation.

Restore never extracts entries. It rejects extra/duplicate/traversal entries,
oversized or malformed payloads, unsupported format/schema versions, and digest
mismatch. The user previews category counts and stable-ID conflicts, then
explicitly chooses restore. A pre-restore logical recovery archive is written
before application. Libraries can merge or replace according to the reviewed
mode; source definitions only merge so restore cannot silently delete source
registrations. Smart Tag authority is restored only for the exact active file
ID; unresolved identities are reported and never guessed from paths.
If application fails partway through restore, targeted compensation removes
only newly introduced source and tag authority records, then restores the
captured settings and reviewed libraries. The pre-restore archive remains the
forensic recovery point if compensation itself cannot complete.

## Forget and compatibility caches

Schema-6 SQLite remains authoritative. `IndexPrivacyDeletionCoordinator`
performs its transactional Forget operation, then clears rebuildable legacy
path-keyed content, semantic, and thumbnail caches. Whole-cache clearing is
intentional: those compatibility stores cannot prove complete stable-ID
ownership after external moves. Cleanup is retried even when SQLite reports the
row was already removed, making recovery from an interrupted cleanup complete.

- **Clear Generated Intelligence** removes selected derived evidence while
  retaining documented user authority.
- **Forget File** erases the file and its derived/user authority from all
  application persistence and records the existing exclusion behavior.
- **Forget Source** erases all indexed file state for that source but does not
  delete the source folder.
- **Clear Index/Rebuild** clears rebuildable derived state while preserving
  source configuration.
- **Restore State** restores only reviewed logical user state, never source
  file contents.

## Hostile PDF boundary

Managed PDF extraction and in-process PDFium rasterization now impose an
independent 64 MiB hard input ceiling. Native text processing is capped to 500
pages and the shared bounded text limits, with cancellation between pages. The
damaged-PDF compatibility reader only reads files at or below 4 MiB, limits
matched values, and never creates multiple unbounded full-file
representations. Raster dimensions are capped at 8192 pixels.

PdfPig text extraction and PDFium rasterization remain in-process parser
boundaries. Full helper-process isolation was judged disproportionate for this
focused release; input/work reduction and optional OCR substantially reduce
exposure but cannot prevent every parser-internal decompression allocation,
native hang, or access violation. This residual risk requires hostile-fixture
and real-platform validation and remains documented rather than
misrepresented as absolute isolation.

## Health and lifecycle

The bounded Data & Index Health service checks profile ownership, recovery
fencing, prior abnormal shutdown, configuration warnings, authoritative JSON
parse/version state, SQLite quick/schema/required-object health, source
reachability, failed-job totals, application-data writability/free-space,
recovery-backup presence, recipes, and Saved Views. It reads bounded metadata;
it does not inspect documents, repair, rebuild, or mutate files. Results are
Healthy, Attention, Recovery required, Unavailable, or Unknown and are exposed
as a compact Settings card.

An atomic run marker identifies a previous run that did not cross the clean
shutdown boundary. Shutdown applies bounded 20-second waits to application and
service-provider teardown after the graph's existing bounded stop. A clean
marker is written only after critical shutdown succeeds; otherwise startup
recovery retains an honest abnormal state.

## AI, version, and release provenance

All Ollama prompt families now explicitly label filenames, metadata, extracted
text, OCR, transcripts, candidate descriptions, validation errors, and prior
responses as untrusted data. Embedded instructions must not be followed.
Existing strict schema, known-ID, known-taxonomy, portable-path, review, and
Change Plan validation remains authoritative; AI still cannot execute.

`Directory.Build.props` is the product-version authority. Source builds report
`2.10.0-rc` and `unversioned`; release builds inject the exact three-part
version and 40-character commit. About, diagnostics, state manifests, assembly
metadata, package metadata, package names, and a package build manifest expose
the same provenance. Release validation rejects disagreement.

The app still targets .NET 8. A framework migration is deliberately excluded
from this hardening RC; maintainers must validate and complete a supported-LTS
migration before .NET 8 support expires rather than combining it with these
recovery changes.

## Explicit boundaries

No new production package, schema, network listener, protocol DTO/version,
cloud provider, mutation engine, recipe, token, ranker, taxonomy, graph
capability, or autonomous behavior is introduced. Native package signing and
macOS notarization still require maintainer credentials; release scripts keep
unsigned artifacts explicit and continue checksum validation.

## Automated validation record

The final Windows-host source validation passed non-incremental Debug and
Release builds and 1,829 tests in each configuration with no failures or skips.
Focused profile, recovery, backup/restore, PDF, Forget, prompt, Explorer,
discovery, Smart Tag, organization, accessibility/layout, migration, and
performance reruns passed. Formatting, analyzers, vulnerability, release-script
syntax, and Git integrity gates passed. Release compilation passed for
`win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`; only compilation and
Windows-host metadata inspection are claimed. The master manual matrix remains
entirely unchecked.
