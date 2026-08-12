# OpenSorSe v2.3 Content Intelligence

**Status:** released as OpenSorSe v2.3.0 from `v2.3-content-intelligence`
through a history-preserving merge into `main`.

**v2.4 name note:** The historical release name remains accurate. OmniSorSe
uses this same schema-5 data in place and may project bounded topics, textual
entities, summary, and evidence presence through Explorer Protocol v1. Full
OCR/transcripts and private diagnostic content are not protocol detail output.

This document is the authoritative v2.3 design and implementation boundary.
The source and automated tests remain authoritative if they disagree with this
guide.

## Goal and non-goals

Content Intelligence adds bounded, explainable clues about what an indexed file
is about. It extends the existing durable indexing, Search, Media Intelligence,
and Related Files pipelines. It does not create a second search engine, a
chatbot, an autonomous organizer, a cloud dependency, facial recognition, or
an identity-surveillance system.

The processing order remains progressive:

1. discover the file and index metadata;
2. extract deterministic document/media text where configured;
3. make names, metadata, text, and OCR searchable;
4. derive bounded topics, textual entities, keywords, and an extractive summary;
5. optionally run separately configured expensive providers such as local
   speech transcription;
6. update Search and Related Files from the evidence that actually exists.

Ordinary Search never waits for optional intelligence. Exact filenames remain
the strongest ranking tier, and optional providers cannot invent file IDs.

## Provider boundary

`IContentIntelligenceProvider` receives only caller-supplied bounded indexed
evidence. A result records:

- normalized topics and keywords;
- textual entities and their conservative category;
- an optional short source-grounded summary;
- deterministic or AI-derived origin;
- understandable evidence strength rather than fabricated percentages;
- provider identity and version;
- a processing fingerprint;
- source kind, a stable evidence key, and a bounded excerpt for provenance.

The v2.3 release registers
`DeterministicContentIntelligenceProvider`. It opens no source files, starts no
process, performs no network call, and invokes no model. It tokenizes already
bounded metadata, document text, OCR, transcript, media OCR, and optional
description fields. Common generic words are suppressed, output is
normalized and deduplicated, and deterministic ordering is used throughout.
Filename and path signals remain in their existing per-file Search fields. They
are deliberately excluded from the content-hash-owned intelligence record so
differently named duplicate files cannot inherit one another's names.

The summary is extractive: it selects and bounds one sentence from retained
evidence. It does not synthesize facts. Named entities come only from text or
metadata patterns; a textual person name is not biometric identification.

### Default bounds

| Control | Default | Supported range |
| --- | ---: | ---: |
| Combined input characters | 16,384 | 1,024–262,144 |
| Topics | 16 | 1–64 |
| Textual entities | 16 | 1–64 |
| Search keywords | 32 | 1–128 |
| Summary characters | 512 | 128–2,048 |
| Evidence excerpt characters | 160 | 64–512 |

The enclosing Deep Indexing summary/keyword stage and its privacy suppression
remain authoritative. The Content Intelligence switches then choose which
bounded categories that stage retains.

## Local speech transcription

v2.3 adds a concrete optional adapter for a **user-managed whisper.cpp CLI and
GGML model**. OpenSorSe does not bundle, download, update, or license either
file for the user. Python, PyTorch, a cloud account, a GPU, and Ollama are not
required by the adapter.

The provider:

- locates an explicitly configured absolute `whisper-cli` path or uses `PATH`;
- requires an explicitly configured absolute local model path;
- checks the runtime with a five-second bounded capability probe;
- accepts bounded WAV, MP3, FLAC, and OGG audio directly;
- uses a separately configured/user-installed `ffmpeg` to create an
  application-owned 16-kHz mono PCM WAV for video and other containers;
- invokes the executable with an argument list, never a shell command;
- requests bounded JSON and accepts at most 512 timestamped segments;
- retains at most the configured transcript-character limit;
- runs at most one transcription process per application instance;
- applies the configured duration and timeout limits;
- propagates cancellation to the existing process runner, which terminates the
  child process tree;
- removes its owned temporary workspace after success, failure, or cancellation;
- fingerprints runtime/model path, file length, modification time, provider
  contract version, timeout, and transcript bound without repeatedly hashing a
  large model.

Replacing an executable or model in place while preserving all observed file
metadata may require an explicit re-index. This avoids repeatedly hashing very
large user-managed files.

### Transcription technology evaluation

| Candidate | License / targets | Packaging and model impact | Decision |
| --- | --- | --- | --- |
| whisper.cpp CLI | MIT; maintained native project with Windows, Linux, Intel macOS, and Apple Silicon paths | User supplies runtime and GGML model; no OpenSorSe package growth; process isolation and cancellation are explicit | Selected as an optional external provider |
| Whisper.net | MIT managed API with target-specific native runtime packages | Easier in-process API, but adds native assets and model/runtime packaging and redistribution work to every supported target | Deferred; the provider-neutral contract permits a future adapter |
| Python/PyTorch Whisper | MIT code, but requires Python plus large native/runtime dependencies | Large mandatory desktop dependency and brittle environment management | Rejected as a required OpenSorSe runtime |

The upstream whisper.cpp 1.9.2 release was rechecked on 2026-08-12. The adapter
does not pin or redistribute it: users select a compatible local build and
model, and OpenSorSe reports unavailable or invalid configuration without
affecting ordinary indexing and Search.

Model size, memory, and speed depend on the user-selected whisper.cpp model and
quantization. OpenSorSe deliberately does not label arbitrary model files as
“Fast” or “Accurate,” because that claim cannot be inferred reliably from the
path alone.

## Visual understanding evaluation

The v2.2 `IMediaVisualDescriptionProvider` contract is retained, but no concrete
visual-description provider ships in v2.3.0.

Ollama's current local API can accept images only for models that report a
vision capability. A responsible integration would additionally need explicit
per-capability consent, strict model capability discovery, bounded image/frame
preparation, remote-endpoint disclosure, malformed-output validation, and
cross-platform resource evidence. That work was deliberately deferred rather
than silently routing images through the existing text-oriented Ollama flow.
Images and video frames are never silently sent to Ollama.

Metadata, EXIF, OCR, thumbnails, and bounded representative-frame processing
continue without visual descriptions.

## Semantic similarity decision

No new model, vector database, or embedding dependency is introduced. The
existing provider-neutral `IEmbeddingProvider` and bounded local feature-hash
representation already provide a deterministic, storage-bounded related-concept
signal. It is used only as corroboration in relationships and below exact and
literal Search tiers.

v2.3 enriches that existing representation with source-grounded topics,
entities, and summaries. A heavier learned embedding model was not justified by
measured retrieval value sufficient to offset model distribution, migration,
storage, licensing, and target-runtime cost. The current fallback therefore
remains identical on every supported target and works offline.

## Search integration

Content Intelligence is projected into the existing provider-neutral Search
candidate. The hybrid ranker adds explicit components:

- `Topic match` (weight 130);
- `Entity match` (weight 140);
- `Source-grounded summary matched` (weight 85).

The existing tier system remains decisive: an exact filename is rank class 7
and weight 1,000, an exact filename stem is class 6 and weight 800, and filename
prefix/substrings remain above weak derived evidence. Identical input uses path
and stable identifiers for deterministic tie-breaking. Derived snippets are
bounded and labelled `Derived topic`, `Textual entity`, or
`Source-grounded summary`.

Transcript text remains ordinary media evidence in the same index, with its
existing source-specific explanation. Unknown IDs from optional AI reranking
remain rejected, so neither Content Intelligence nor AI can fabricate a file.

## Related Files integration

Related Files can use shared normalized topics and textual entities across
documents and media. One shared topic is insufficient. Topic evidence requires
at least two shared topics; generic terms are suppressed; a relationship still
requires the established global score/evidence threshold unless stronger
deterministic evidence independently qualifies. Device metadata remains weak
corroboration. Relationship explanations are produced from the exact shared
values.

This permits explainable cross-media connections such as a PDF and transcript
that both discuss Raspberry Pi monitoring, without relying on matching
filenames. It does not create unrestricted topic clusters.

## Persistence, migration, and recovery

The embedded Search store advances transactionally from schema 4 to **schema
5**. The migration adds one nullable `content_intelligence_json` column to the
content-hash-owned `index_content` record and an indexed
`index_relationship_feature_terms` table containing at most 64 normalized terms
per file. That projection retrieves cross-media relationship candidates by term
without an all-pairs scan and is replaced atomically with the owning relationship
feature record. Existing v2.2 files, media evidence, jobs, privacy rules, Search
data, relationships, and source registrations remain unchanged.

Migration follows the established recovery-copy and integrity process:

- a pre-migration backup is retained according to index policy;
- the column and indexed relationship-term projection are created only inside
  the migration transaction;
- schema markers advance last;
- rerunning initialization does not repeat the migration;
- unsupported newer schemas and corruption still fail closed;
- malformed or out-of-bound intelligence JSON is excluded from Search and marks
  the record as having an indexing failure rather than returning a corrupt
  partial object;
- no source file is opened or modified by migration.

The content hash remains the cache owner. Source changes, relevant extraction
settings, provider/version, and relevant model/runtime configuration invalidate
the processor fingerprint. Unrelated settings do not require expensive media or
content reprocessing.

## Privacy and clearing

All deterministic Content Intelligence runs in-process and locally. whisper.cpp
and ffmpeg, when explicitly configured, are local child processes. No telemetry,
cloud transcription, model download, web lookup, reverse geocoding, facial
recognition, or person identification is added.

The index may contain topics, textual names, identifiers, summaries, OCR, and
transcripts that reveal information not obvious from filenames. Users can
disable individual categories, inspect retained counts, clear Content
Intelligence for a selected file, forget a file/source, or clear the index.
These actions affect application-owned derived data only; source files and
user-managed model/runtime files remain untouched. Related Files evidence has
its own existing forget/rebuild controls.

A custom Ollama-compatible endpoint can be remote, but it participates only in
separately enabled existing AI flows. Content Intelligence never silently sends
content to it.

## Diagnostics and failure isolation

Index stage state retains provider/version fingerprints, completion/failure,
duration, retryability, and bounded category counts available through indexed
data inspection. Storage diagnostics include a separate Content Intelligence
byte count. Ordinary diagnostics do not retain full document text, transcript,
OCR, summary, prompt, or evidence excerpts.

A missing model is `Not configured`, a missing runtime is `Unavailable`, and an
unusable supplied path/model is `Invalid configuration`, rather than a generic
application failure. Invalid model paths, runtime start failures, malformed
JSON, timeout, cancellation, process failure, disappearing files, and corrupted
cached intelligence remain isolated to the item. Ordinary Search continues.

## Dependencies

v2.3 adds **no NuGet or bundled native production dependency**. Optional
user-managed tools are:

- whisper.cpp CLI and GGML model for speech transcription;
- ffmpeg for video/container audio preparation (and existing v2.2 frame work);
- ffprobe for existing v2.2 audio/video metadata;
- Tesseract for existing local OCR;
- Ollama-compatible service for existing separately enabled AI features.

Absence of any optional tool degrades only its capability. Core startup,
metadata indexing, deterministic Content Intelligence, Search, Related Files,
and file safety do not depend on it.

## Known limitations

- No whisper.cpp binary or model is bundled or downloaded.
- Ordinary CI uses a deterministic process boundary. Final Windows-native
  validation also exercised official whisper.cpp 1.9.2 with a controlled local
  model; native Linux/macOS transcription remains unclaimed.
- No concrete visual-description provider is included.
- No learned embedding model or vector database is added.
- Entity extraction is conservative local text patterning, not general-purpose
  language understanding or identity resolution.
- Summaries are extractive single-sentence evidence, not generative abstracts.
- Topic normalization is language-agnostic and intentionally limited; English
  and common German stop terms are included, not full stemming for every
  language.
- There is no transcript seek UI, media playback, map/reverse geocoding, face
  recognition, person identification, autonomous organization, or cloud AI.

See [v2.3 manual testing](MANUAL_TESTING_v2.3.md),
[Search architecture](Architecture/06_Search/00_Overview.md), and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).
