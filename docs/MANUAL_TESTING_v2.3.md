# OpenSorSe v2.3 manual testing checklist

**Status:** v2.3.0 release evidence tracker. Unchecked items remain explicit
post-release/manual/community work rather than implied validation.

This checklist separates automated evidence from tests that require a native
provider, interactive desktop session, or another operating system. Do not
mark a scenario complete because a fake provider or cross-target build passed.

## Automated evidence

The final totals and gate results must be filled only after the fresh final
validation pass.

- [x] Clean/no-cache restore completed.
- [x] Non-incremental Debug build completed with zero warnings/errors.
- [x] Complete Debug suite passed: 1,637 passed, zero failures/skips.
- [x] Non-incremental Release build completed with zero warnings/errors.
- [x] Complete Release suite passed: 1,637 passed, zero failures/skips.
- [x] Search relevance, performance, media/indexing, SQLite migration/recovery,
  privacy, accessibility, Ollama, file-operation safety, and policy subsets
  passed.
- [x] Release cross-target compilation passed for win-x64, linux-x64, osx-x64,
  and osx-arm64 with expected application-owned native assets.

These checked items are automated Windows-host evidence from 12 August 2026.
Controlled native validation used explicit temporary paths for official
whisper.cpp 1.9.2, a local tiny English model, and ffmpeg/ffprobe 9.0; none is
bundled with OpenSorSe. Tesseract was not installed: a reviewed installer was
downloaded and verified by Windows Package Manager, but host installation was
cancelled with `0x800704c7`, so no fresh native v2.3 OCR claim is made.
Cross-target compilation does not represent native Linux or macOS execution.

## Deterministic Content Intelligence

- [x] Index a synthetic document about Raspberry Pi, Docker, Prometheus, and
  Grafana; inspect bounded topics, textual entities, provenance, and extractive
  summary.
- [x] Confirm bounded topic/entity/keyword counts and generic-term suppression
  through the controlled provider harness and automated stop-topic coverage.
- [ ] Confirm deterministic ordering is stable after repeated unchanged runs.
- [ ] Disable topics, entities, and summaries independently and confirm the
  corresponding work is absent after applicable reprocessing.
- [ ] Change one relevant bound and confirm Content Intelligence is invalidated;
  change an unrelated UI setting and confirm it is reused.
- [x] Search topic/entity/summary-only phrases and inspect their exact explanation
  and bounded snippet source.
- [x] Confirm an exact filename remains above a weak derived-only match.
- [ ] Clear Content Intelligence for one file and confirm the original file is
  byte-for-byte unchanged.
- [ ] Forget a source and clear the index; confirm no stale derived Search or
  Related Files evidence remains.

## Local whisper.cpp provider

- [x] Configure the reviewed official whisper.cpp 1.9.2 Windows x64 executable
  and a controlled local tiny English GGML model; confirm capability state is
  Available. The runtime archive's published SHA-256 was independently matched.
- [x] Transcribe a short synthetic WAV containing known words and find it through
  normal Search.
- [x] Inspect two bounded timestamp segments and a transcript-specific Search reason.
- [ ] Re-index the unchanged recording and confirm transcript cache reuse.
- [ ] Change model/runtime metadata and confirm relevant transcript invalidation.
- [x] Cancel an active transcription; confirm process-tree termination, no
  false-complete cache, retry availability, and temporary-workspace cleanup.
- [ ] Trigger the configured timeout and verify the same consistency guarantees.
- [ ] Configure a missing runtime, missing model, too-small/invalid model, and
  inaccessible path; confirm actionable unavailable state and ordinary Search.
- [x] Transcribe video audio through an owned temporary WAV and verify cleanup
  after success. Failure/cancellation cleanup remains covered deterministically,
  not claimed as a separate native video run.
- [x] Confirm the OpenSorSe adapter performs no runtime/model download and does
  not route transcription through Ollama; runtime/model acquisition for this
  test was an explicit release-engineering action.

## Native media-tool smoke

- [x] Run real ffprobe 9.0 through the OpenSorSe provider for controlled WAV and
  MP4 fixtures; verify duration, codecs, sample rate/channels, resolution, and
  frame rate.
- [x] Run real ffmpeg 9.0 through the bounded frame sampler; verify one sampled
  frame for the short fixture, byte bounds, configured cap, and workspace cleanup.
- [ ] Run real Tesseract through the final v2.3 OCR path. Installation was
  cancelled by the Windows host; automated provider tests and prior v2.2 native
  evidence are retained without being relabelled as a v2.3 native pass.

## Visual understanding and semantic fallback

- [ ] Confirm visual descriptions report unavailable/not configured and image,
  video, Search, and Related Files behavior remains usable.
- [ ] Confirm enabling the unavailable switch does not send an image/frame to
  Ollama.
- [ ] Confirm existing bounded local related-concept Search works with Ollama,
  transcription, and visual descriptions all unavailable.

## Cross-media Related Files

- [ ] Create a PDF and audio transcript sharing two specific topics; confirm an
  explainable relationship without matching filenames.
- [ ] Confirm one generic topic or same camera/device alone does not create a
  relationship.
- [ ] Inspect shared-topic and textual-entity evidence and confidence.
- [ ] Clear/forget one member's intelligence and confirm stale relationship
  evidence is removed or rebuilt through the existing relationship controls.

## Schema 4 to 5 migration

- [x] Open a controlled genuine v2.2 schema-4 index, generated by exact v2.2.0
  source in a detached worktree, through the current store.
- [x] Confirm the schema advances to 5 and `content_intelligence_json` exists.
- [x] Confirm v2.2 document/media Search data remains searchable and source
  fixtures remain untouched.
- [x] Populate Content Intelligence and relationship terms, reopen, and confirm
  the migration is not repeated and the recovery-backup count remains stable.
- [ ] Exercise interrupted/mismatched migration recovery and unsupported-newer
  schema refusal.
- [ ] Corrupt one intelligence record and confirm Search returns a valid fallback
  document with a visible failure, not a corrupt partial object.

## Windows interactive desktop

- [ ] Inspect Content Intelligence settings at normal and narrow window sizes.
- [ ] Use keyboard-only navigation and verify visible focus, meaningful labels,
  switch states, help, indexed-data inspection, clear, and Search result details.
- [ ] Confirm status wording distinguishes Disabled, Not configured,
  Unavailable, Available, Processing, Error, and cancellation where applicable.
- [ ] Confirm background indexing makes filename/text Search available before
  expensive optional transcription finishes.
- [ ] Inspect Advanced Diagnostics and confirm full document/OCR/transcript/
  summary content is absent by default.

## Linux and macOS native validation

- [ ] Linux x64 native launch, deterministic extraction, Search, and optional
  whisper.cpp capability/process validation.
- [ ] Intel macOS native launch, deterministic extraction, Search, and optional
  whisper.cpp capability/process validation.
- [ ] Apple Silicon macOS native launch, deterministic extraction, Search, and
  optional whisper.cpp capability/process validation.

Cross-target compilation alone must not check these native scenarios.

## Safety and privacy

- [ ] Verify no source document/media file changes during extraction,
  transcription, Search, relationships, clear, forget, migration, or repair.
- [ ] Verify no telemetry, hidden web lookup, silent upload, model download,
  facial recognition, person identification, or autonomous file operation.
- [ ] Configure a non-loopback Ollama-compatible endpoint and confirm its remote
  privacy warning remains separate from deterministic Content Intelligence.
- [ ] Review an exported diagnostic bundle for private content and paths before
  sharing.
