# OpenSorSe v2.3.0 — Content Intelligence & Local Understanding

OpenSorSe v2.3.0 adds bounded local understanding across documents and media
without replacing deterministic Search. Derived topics, keywords, textual
entities, extractive summaries, and optional local transcripts remain
explainable evidence attached to files that actually exist.

## Downloads

The official [v2.3.0 GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.3.0)
contains:

- `OpenSorSe-v2.3.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.3.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.3.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.3.0-macos-arm64.dmg` — Apple Silicon package;
- `OpenSorSe-v2.3.0-SHA256SUMS.txt` — SHA-256 checksums.

Linux x64 remains supported through the documented source-build path; no Linux
installer is published.

## Content Intelligence

- Bounded deterministic topic and keyword extraction uses already retained
  indexed evidence and suppresses generic/noisy concepts.
- Conservative textual entity extraction identifies source-grounded names,
  places, organizations, products, projects, dates, and document identifiers;
  it performs no biometric or internet identity resolution.
- One-sentence extractive summaries remain bounded and cannot invent facts that
  were absent from the indexed evidence.
- Provider, provider version, origin, confidence, processing fingerprint, and
  source evidence are retained for explainability and cache invalidation.
- Search exposes distinct Topic, Entity, Summary, Transcript, OCR, and metadata
  explanations. Exact filename and literal tiers remain authoritative.
- Related Files uses bounded shared topics/entities across file types, requires
  corroborating evidence, and suppresses generic-topic clusters.

## Optional local transcription

- v2.3.0 includes an optional process-isolated adapter for a user-managed
  whisper.cpp CLI and GGML model.
- OpenSorSe does not bundle or silently download a runtime or model.
- Transcription is local, cancellable, timeout-bounded, cacheable, and can
  retain up to 512 timestamped segments within configured transcript limits.
- Video audio preparation reuses a separately configured local `ffmpeg` and an
  application-owned temporary workspace.
- Transcripts enter the existing media index and unified Search; they do not
  create a separate transcript database.
- No cloud transcription is provided and media is not sent to Ollama for
  transcription.

## Persistence and privacy

- Embedded Search schema 5 transactionally upgrades schema 4, preserving v2.2
  document/media evidence and creating a recovery backup before migration.
- A bounded indexed relationship-term projection avoids all-pairs scans.
- Clear/forget operations remove derived Content Intelligence and dependent
  automatic relationships without changing source files.
- No telemetry, silent upload, hidden internet lookup, facial recognition,
  person identification, learned embedding model, or vector database is added.
- No concrete visual-description provider ships; images and video frames are
  not silently sent to Ollama.

## Optional dependencies

The following tools remain external, user-managed, optional, and absent from
official OpenSorSe packages:

- whisper.cpp CLI and model for speech transcription;
- `ffmpeg` for video/container audio preparation and representative frames;
- `ffprobe` for audio/video metadata;
- Tesseract 5 for OCR;
- an Ollama-compatible service for separately enabled existing AI assistance.

Missing or invalid optional-tool configuration degrades only that capability;
ordinary local indexing and Search remain available.

## Quality and validation boundary

- Debug: 1,637 passed, 0 failed, 0 skipped.
- Release: 1,637 passed, 0 failed, 0 skipped.
- Non-incremental Debug and Release builds completed with zero warnings and zero
  errors.
- Release compilation and runtime-asset checks passed for Windows x64, Linux
  x64, macOS x64, and macOS arm64.
- Controlled Windows-native validation exercised official whisper.cpp 1.9.2
  with a local tiny English model, timestamped audio and video transcription,
  Transcript-to-Search retrieval, cancellation and workspace cleanup, plus real
  ffprobe/ffmpeg metadata/frame paths.
- A genuine v2.2 schema-4 index upgraded to schema 5, retained its indexed
  document/media evidence, accepted new Content Intelligence, reopened cleanly,
  and left source data unchanged.
- Native Tesseract OCR was not repeated in the final v2.3 environment because
  the reviewed installer was cancelled by the host; deterministic OCR provider
  tests and earlier v2.2 native evidence remain, but this is not claimed as a
  fresh v2.3 native OCR pass.
- Cross-target compilation is not a claim of native Linux/macOS transcription
  or interactive behavior. Broad community testing may still uncover defects
  for later maintenance releases.

## Trust and known limitations

The Windows artifacts are unsigned. The macOS artifacts are unsigned and
unnotarized. Checksums detect changed bytes but do not authenticate an unsigned
publisher.

Transcription quality, memory use, and speed depend on the user-selected model
and hardware. There is no transcript seek UI, media playback, reverse
geocoding/map, visual-description provider, learned vector search, facial
recognition, cloud transcription, or autonomous organization in v2.3.0.

See [Content Intelligence](CONTENT_INTELLIGENCE_v2.3.md),
[Manual Testing](MANUAL_TESTING_v2.3.md), [Installation](INSTALLATION.md), and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).
