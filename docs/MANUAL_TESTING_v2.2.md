# OpenSorSe v2.2 Media Intelligence manual checklist

**Status:** controlled Windows native-provider, real local Tesseract, and
published-v2.1 migration checks completed on 2026-08-11 are checked below.
Interactive desktop and native Linux/macOS scenarios remain unchecked. A
checked item is not a claim that a broader UI or platform scenario was
exercised.

Use only synthetic or intentionally shareable test media. Before and after the
session, verify that no source media file was changed.

## Completed controlled native-host evidence

These checks ran OpenSorSe provider code from a disposable ignored harness;
they were not mocked and did not inspect personal media.

- [x] Published v2.1.0 binaries created a genuine schema-3 index with one
  searchable record; the current store migrated it to schema 4, created one
  recovery backup and the media table, retained Search data, reopened cleanly,
  did not rerun migration, and preserved the source fixture hash.
- [x] A generated valid JPEG APP1/EXIF fixture returned dimensions,
  orientation, make/model, capture text, and intentionally embedded GPS through
  the real image metadata provider. Missing/corrupt EXIF isolation remains
  covered by the automated suite.
- [x] A real decodable generated PNG produced a lazy application-owned Skia
  preview; a 4×2 orientation-6 source produced a 2×4 preview, reused the same
  cache entry, cleared the cache, and preserved the source hash.
- [x] Gyan FFmpeg/ffprobe 9.0 (temporary unbundled validation copy) supplied
  real metadata for generated MP3, WAV, FLAC, M4A, and MP4 fixtures through
  `FfprobeMediaMetadataProvider`.
- [x] The real 4-second 640×360, 25-fps H.264/AAC MP4 produced one bounded
  interior frame at 2 seconds through `FfmpegVideoFrameSampler`; the encoded
  frame was 82,943 bytes and the owned workspace was removed.
- [x] The real process runner cancelled and timed out controlled long-running
  ffmpeg operations, requested process-tree termination, and left no completed
  provider result.
- [x] Invalid ffprobe/ffmpeg executable paths returned `Unavailable` without a
  crash or false success.
- [x] A checksum-verified Tesseract 5.5.3 validation copy and the official
  English `tessdata_fast` model were extracted into temporary storage without
  system registration. OpenSorSe's real CLI engine recognized `QUARTZ NEBULA
  DOCKER VALIDATION 74291` from a generated `capture-2026.png`; the filename
  and path contained none of the query words. Unified Search returned the
  image with a `media OCR` component and `MediaOcr` snippet, the source hash
  was unchanged, an invalid executable path stayed unavailable, and disabled
  OCR stopped before provider execution. All temporary runtime/model/image
  material was removed after validation.
- [ ] Real speech transcription. No concrete provider/runtime/model is shipped
  or configured, so transcript Search is not claimed.
- [ ] Interactive window-size, keyboard, screen-reader, wheel, and Windows
  125%/150% scaling checks. XAML/accessibility regression tests pass, but they
  are not a substitute for maintainer interaction.

## Issue-readiness recommendation (issues remain open)

This table separates implementation/automated evidence from still-unperformed
interactive checks. It is a release-review recommendation, not an issue-closure
record.

| Issue | Recommendation | Evidence and remaining interaction |
| --- | --- | --- |
| #27 Scan ETA | Fixed and automated | A monotonic, stage-aware smoothed estimate appears only after enough homogeneous work, resets when the workload changes, and terminates on success, failure, or cancellation. Visual observation during a long heterogeneous scan remains unchecked. |
| #28 Multi-group duplicate removal | Fixed and automated | Selections persist across groups, every group must retain a keeper, one reviewable Change Plan is created, cancellation emits no plan, and the duplicate projection changes only after completely successful execution. Interactive Change Plan execution and partial-failure recovery remain unchecked. |
| #29 Virtual Collections scrolling | Fixed and automated | The collections list owns a bounded grid row and vertical scroll surface; adjacent detail content scrolls independently. Mouse-wheel, high-DPI, resize, and focus-following observation remain unchecked. |
| #30 Related Files destination | Fixed and automated | Related Files is one primary destination. Collections retains a hidden compatibility tab/route only, so old internal navigation does not break. Interactive navigation remains unchecked. |
| #31 Related Files scrolling | Fixed and automated | The status region is bounded and the tab pages own vertical scroll surfaces with stretched content. The requested window-size/scaling matrix remains unchecked interactively. |
| #32 local/privacy wording | Fixed and automated | File analysis is described as local indexing; local and non-loopback AI endpoints are distinguished, and remote endpoints carry an explicit disclosure. Interactive screen-reader review remains unchecked. |
| #33 Search discoverability | Fixed and automated | Search is a primary destination with a unified media-aware search box and Hybrid/Hybrid + AI explanation. Interactive first-use and keyboard-navigation observation remains unchecked. |

No issue was closed during this implementation-only pass.

## Settings and capability states

- [ ] Open Settings with a fresh profile and confirm metadata is enabled while
  image OCR, transcription, frame analysis, and visual descriptions are off.
- [ ] Use keyboard-only navigation through every Media Intelligence switch,
  bound, path field, capability button, status, and Help route.
- [ ] Confirm screen-reader names describe each switch and capability state.
- [ ] Check capabilities with no ffprobe, ffmpeg, transcription provider, or
  visual provider configured; unavailable features are explained as optional.
- [ ] Configure a valid local ffprobe/ffmpeg path and recheck capability.
- [ ] Enter invalid/relative executable paths and confirm Save fails safely.
- [ ] Disable Media Intelligence and confirm ordinary document/filename Search
  remains available.

## Images

- [ ] Index synthetic JPEG, PNG, WebP, BMP, and TIFF images.
- [ ] Verify dimensions and available EXIF make/model/orientation/capture date.
- [ ] Verify missing EXIF is normal and malformed EXIF fails only that evidence.
- [ ] Index a synthetic GPS-tagged image; inspect local stored categories and
  confirm no external geocoding/network request occurs.
- [ ] Enable image OCR with a configured local engine and find a screenshot by
  visible command text.
- [ ] Stop/remove OCR and confirm the item waits or reports capability without
  blocking filename Search.
- [ ] Inspect an indexed image and confirm its bounded preview loads lazily.
- [ ] Clear media-derived data and confirm the preview/index data is removed but
  the original image is byte-for-byte unchanged.
- [ ] Test a corrupt, truncated, oversized, permission-denied, and disappearing
  image; the indexing run continues.

## Audio

- [x] With local ffprobe available, index synthetic MP3, WAV, FLAC, and M4A and
  inspect duration/codec/sample/channel/title metadata where present.
- [ ] Test an unsupported codec in a recognized container and inspect the
  actionable per-file failure.
- [ ] Enable transcription with no provider and confirm the truthful
  not-configured/waiting state.
- [ ] If a reviewed local transcription provider is later configured, verify a
  known phrase and timestamped segments through normal Search.
- [ ] Cancel transcription and verify prompt cooperative cancellation.
- [ ] Re-index an unchanged recording and verify completed compatible work is
  reused.
- [ ] Exceed audio duration and file-size limits and verify intentional skips.

## Video

- [ ] With local ffprobe available, index synthetic MP4, MOV, MKV, and AVI and
  inspect available duration/resolution/frame-rate/codec/device metadata.
- [x] Enable frame analysis with local ffmpeg and verify at most the configured
  representative-frame count is produced.
- [x] Verify a short clip uses one interior frame.
- [ ] Verify with a real long clip that sampling uses one frame per started five
  minutes up to the configured cap. The deterministic algorithm is automated,
  but no large native fixture was generated for this pass.
- [ ] Enable frame OCR and find a synthetic screen recording by sampled text.
- [ ] Confirm at most the configured OCR frame count is processed.
- [ ] Remove/stop ffmpeg during processing and confirm the file fails/waits
  safely without aborting the run.
- [ ] Cancel frame extraction and confirm the managed temporary workspace is
  removed.
- [ ] Exceed the video duration/file-size limits and verify no unbounded work.
- [ ] Confirm video playback, editing, whole-frame analysis, and video previews
  are not presented as implemented.

## Search and Related Files

- [ ] Find media by exact filename and confirm it outranks weak derived evidence.
- [ ] Find an image by camera/device metadata.
- [ ] Find an image/video by OCR and inspect the source-labelled snippet.
- [ ] If transcription is configured, find audio/video by transcript and inspect
  the source-labelled snippet.
- [ ] Confirm optional visual descriptions are unavailable unless a compatible
  explicitly configured local provider exists.
- [ ] Confirm explanations distinguish media metadata, transcript, media OCR,
  and optional visual description.
- [ ] Search while media indexing is running, paused, cancelled, and waiting for
  a dependency; incomplete coverage remains clear.
- [ ] Confirm a same-camera match alone does not create a Related Files link.
- [ ] Confirm exact matching transcript/OCR evidence produces an explainable
  relationship without overwhelming deterministic filename matches.

## Privacy, diagnostics, and recovery

- [ ] Inspect media-derived data categories for an image, audio file, and video.
- [ ] Clear OCR-derived data, media-derived data, and all generated Search data;
  verify original files remain unchanged.
- [ ] Forget a media file and a media source; verify watched/manual source
  ownership and immediate re-index suppression remain correct.
- [ ] Restart during media processing and verify no stale running job remains.
- [ ] Retry a requested dependency after it becomes available.
- [ ] Review Advanced Diagnostics for provider, status, duration, cache, frame
  count, evidence sizes, warnings, failure, and cancellation.
- [ ] Confirm ordinary diagnostic summaries do not expose media bytes, complete
  transcripts/OCR, descriptions, or precise GPS.
- [ ] Review an exported diagnostics bundle before sharing.
- [ ] Monitor CPU, memory, temporary disk, and index growth using a deliberately
  bounded mixed-media folder under Eco, Balanced, and Fast modes.
- [ ] Verify pause, resume, cancel, quota cleanup, compaction, shutdown, and
  restart recovery with mixed document/media indexing.

## Cross-platform native follow-up

- [x] Windows x64: run image metadata/preview, a contained local Tesseract
  validation copy, and locally configured ffprobe/ffmpeg tools. Transcription
  was not available because no concrete provider ships.
- [ ] Linux x64: build/run from source and test only installed local tools.
- [ ] macOS Intel: run image metadata/preview and any locally configured tools.
- [ ] macOS Apple Silicon: run image metadata/preview and any locally configured
  tools.
- [ ] Record exact external-tool build, license, version, architecture, and codec
  availability for every native claim.
