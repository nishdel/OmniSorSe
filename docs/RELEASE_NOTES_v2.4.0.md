# OmniSorSe v2.4.0 — Transition & Explorer Foundation

OmniSorSe v2.4.0 changes the active product identity from OpenSorSe to
OmniSorSe without moving or rebuilding existing user data. It also introduces
Explorer Protocol v1, a small authenticated read-only local IPC boundary for a
future separately distributed OmniExplorer companion.

## Downloads

The official [v2.4.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0)
contains:

- `OmniSorSe-v2.4.0-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.4.0-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.4.0-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.4.0-macos-arm64.dmg` — Apple Silicon package;
- `OmniSorSe-v2.4.0-SHA256SUMS.txt` — SHA-256 checksums for all packages.

Linux remains source-build supported. The Windows and macOS packages are
unsigned, and the macOS packages are not notarized. Verify the checksum and
review the source/release origin before overriding operating-system warnings.

## OmniSorSe transition

- The active desktop, About, Help, diagnostics, package, executable, shortcut,
  and visible macOS application identity is OmniSorSe.
- Existing profiles stay in their established OpenSorSe locations. Settings,
  indexed sources, schema-5 Search data, watched folders, Media and Content
  Intelligence, external-tool paths, plans, journals, and recovery state remain
  compatible in place.
- Windows retains the existing installer AppId and default installation
  directory, allowing an installed OpenSorSe v2.3 release to upgrade to
  OmniSorSe v2.4 without creating a second product entry.
- The macOS bundle identifier remains unchanged for continuity.
- Internal `OpenSorSe.*` namespaces/projects and branding-neutral processing
  fingerprints remain unchanged. Branding alone does not require reindexing.
- Historical releases remain named OpenSorSe. Repository URLs also remain at
  the current OpenSorSe repository until a separately reviewed repository
  rename.

## Explorer Protocol v1

- Protocol v1 is read-only, bounded, authenticated, and dormant until an
  explicitly authorized local session is created.
- It uses a current-user-only local named pipe on Windows (Unix-domain-backed
  by .NET on Unix hosts). It creates no TCP, HTTP, LAN, cloud, or discovery
  listener.
- Sessions use random 128-bit identifiers, 256-bit bearer tokens retained only
  as hashes, absolute expiry, immediate revocation, and session-bound HMAC
  opaque node identifiers.
- An authorized client can negotiate capabilities and limits, list only
  approved indexed roots, inspect bounded structural children/neighborhoods,
  run unified deterministic Search, read existing Related Files evidence, and
  request bounded node details.
- Raw paths are omitted unless separately granted. Full documents, complete
  OCR/transcripts, precise GPS, binary media, diagnostics, credentials, and
  arbitrary filesystem paths are not exposed.
- Strict JSON, frame/query/node/edge/depth/result limits, four concurrent
  requests, a bounded queue, hard timeouts, disconnect cancellation, and safe
  saturation recovery limit resource use.
- Search membership remains grounded in indexed file IDs and authorized scope;
  protocol requests never enable AI assistance.

## OmniExplorer status

OmniExplorer is a future optional companion and is not included in v2.4.0.
There is no graph renderer, Explorer UI, GPU dependency, voice dependency,
companion installer, or direct SQLite access in this release. OmniSorSe remains
fully functional by itself.

## Privacy and security

- No telemetry, cloud relay, external listener, silent upload, or new cloud
  dependency was introduced.
- Explorer Protocol v1 is local, current-user-only, explicitly scoped, and
  read-only. It cannot rename, move, delete, organize, or otherwise mutate
  source files.
- Diagnostics retain bounded operation/state/count/timing facts without raw
  tokens, queries, paths, snippets, OCR, transcripts, or request payloads.
- Existing local-first Search, Media Intelligence, Content Intelligence, AI
  endpoint warnings, and privacy clearing behavior remain unchanged.

## Compatibility and optional dependencies

Schema remains version 5. No data migration or reindex is required for the
rename. OmniSorSe continues to treat Ollama-compatible services, ffmpeg,
ffprobe, Tesseract, whisper.cpp, and Whisper models as optional user-managed
capabilities; none is bundled in official packages.

## Quality boundary

- Fresh Debug and Release suites each pass 1,671 tests with zero failures and
  zero skipped tests, including two native named-pipe regressions added during
  final external-process validation.
- Release builds pass for Windows x64, Linux x64, macOS x64, and macOS arm64.
- A genuine published-v2.3 profile was opened repeatedly by the v2.4 candidate
  with schema 5, Search, watched folders, representative settings, and source
  bytes preserved.
- Windows-native two-process protocol validation covered negotiation,
  authorized roots, Structure, Search, Related Files, bounded details, invalid
  authentication, strict JSON, oversized frames, expiry, revocation, forced
  disconnect, application shutdown, concurrency, backpressure, Unicode, and no
  TCP listener.
- Native Linux/macOS protocol execution and comprehensive screen-reader, DPI,
  UNC, and broad interactive desktop testing are not claimed. Native macOS
  package validation is performed by the established release workflow.

See [Installation](INSTALLATION.md), the
[transition and protocol design](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md),
and [manual validation evidence](MANUAL_TESTING_v2.4.md) for the exact
compatibility, security, packaging, and validation boundaries.
