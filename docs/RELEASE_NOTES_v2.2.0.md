# OpenSorSe v2.2.0 — Media Intelligence

OpenSorSe v2.2.0 extends the local-first Search experience to images, audio,
and video. Deterministic local Search remains authoritative; media evidence is
bounded, optional capabilities fail independently, and source files remain
unchanged unless a user separately approves a supported Change Plan.

## Downloads

Use the assets attached to the official
[v2.2.0 GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.2.0):

- `OpenSorSe-v2.2.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.2.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.2.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.2.0-macos-arm64.dmg` — Apple Silicon package;
- `OpenSorSe-v2.2.0-SHA256SUMS.txt` — verified SHA-256 checksums.

No Linux installer is published. Linux x64 remains source-build supported.

## Media Intelligence

- Supported images become first-class indexed content with bounded dimensions,
  orientation, capture time, camera/device, textual EXIF, and local GPS
  metadata where present.
- Existing optional local Tesseract OCR now supplies searchable image and
  representative-video-frame evidence.
- Image previews are generated lazily, orientation-corrected, bounded, cached
  only below OpenSorSe-owned storage, and never written beside the source file.
- Optional user-managed `ffprobe` supplies bounded audio/video container,
  codec, duration, resolution, frame-rate, bitrate, channel, sample-rate, and
  embedded textual metadata where available.
- Optional user-managed `ffmpeg` produces a strictly capped set of evenly
  spaced interior video frames for bounded preview/OCR evidence. Temporary
  workspaces are application-owned and removed after success, cancellation,
  timeout, or failure.
- Media metadata, OCR, future transcript evidence, and optional future visual
  descriptions use the same provider-neutral indexing and unified Search
  contracts. Search explanations identify the actual evidence source.
- Related Files can use exact OCR/transcript fingerprints and conservative
  capture-time/device corroboration without allowing weak camera identity to
  create broad clusters.
- Expensive results use file/configuration/provider fingerprints, bounded
  quotas, one-at-a-time default indexing, finite provider timeouts, and
  cooperative cancellation.
- The durable Search index migrates transactionally from schema 3 to schema 4,
  retaining a recovery backup and existing searchable content.

## Search and workflow quality

- Search is now an obvious primary navigation destination and remains one
  unified experience for files, documents, images, audio, and video.
- Scan progress can show a smoothed ETA only after sufficient comparable work;
  indeterminate and terminal states remain truthful.
- Duplicate selections can span multiple groups and enter one reviewed Change
  Plan while enforcing at least one keeper per affected group.
- Virtual Collections and Related Files now have corrected scroll ownership and
  bounded layouts.
- Related Files appears once as a visible top-level destination; the previous
  route remains compatibility-only.
- Navigation now gives common tasks greater visual priority without removing
  advanced tools.
- Settings clearly separate local file analysis from optional AI endpoints.
  Loopback endpoints are identified as local; non-loopback endpoints display a
  privacy warning.

## Privacy and optional dependencies

- Scanning, indexing, EXIF parsing, thumbnails, configured Tesseract OCR, and
  configured ffprobe/ffmpeg processing happen locally.
- OpenSorSe introduces no telemetry, mandatory cloud service, or silent media
  upload.
- `ffprobe`, `ffmpeg`, and Tesseract are optional user-managed tools and are not
  bundled in release packages. Missing or invalid tools disable only their own
  capabilities.
- The transcription architecture is present, but no concrete transcription
  runtime or model ships in v2.2.0. No model is silently downloaded and media
  is not sent to cloud transcription.
- The visual-description provider boundary is present, but no concrete model
  ships. Images and frames are not silently sent to Ollama.
- GPS remains structured local metadata. v2.2.0 performs no reverse geocoding
  and transmits no GPS information to a map service.
- Facial recognition and automatic person identification are not implemented.

## Validation boundary

The final local suite passed **1,603 tests** in Debug and Release with zero
failures and zero skips, alongside zero-warning builds, Search/media/indexing,
migration/recovery, performance, privacy, accessibility, provider failure,
formatting, analyzer, policy, vulnerability, and four-runtime cross-target
gates. Native Windows checks exercised real image decoding, Tesseract OCR,
ffprobe/ffmpeg providers, bounded frame extraction, and a genuine v2.1
schema-3 to schema-4 migration.

Cross-target builds passed for Windows x64, Linux x64, macOS x64, and macOS
ARM64. That is not a claim of native Linux/macOS media execution. Native
macOS package construction and inspection are release-workflow evidence, not
broad interactive testing.

## Signing and known limitations

Unless the GitHub release explicitly records otherwise, Windows artifacts are
unsigned and macOS artifacts are unsigned/unnotarized. SmartScreen or
Gatekeeper may warn. Verify the complete SHA-256 checksum before use.

No concrete transcription or visual-description provider, video playback,
transcript seek UI, reverse geocoding/map, facial recognition, object tracking,
full-video understanding, cloud indexing, or autonomous media organization is
included.

See [Installation](INSTALLATION.md),
[Media Intelligence](MEDIA_INTELLIGENCE_v2.2.md),
[Safety and Privacy](SAFETY_AND_PRIVACY.md), and the honest
[v2.2 manual checklist](MANUAL_TESTING_v2.2.md).
