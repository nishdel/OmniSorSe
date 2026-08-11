# Free and Open-Source Dependency Policy

OpenSorSe accepts only dependencies with a documented free/open-source license
and a redistribution path compatible with the repository's MIT distribution
model. The machine-readable inventory tracks the current restored source graph;
the frozen v1.0 package retains its own historical notices.

## Rules

- Every resolved NuGet package must appear in `docs/dependency-licenses.json`.
- Inventory records must include name, version, purpose, upstream project, SPDX-style license identifier, notice location, bundled/external status, optional/mandatory status, transitive concerns, and redistribution obligations.
- Missing or `UNKNOWN` licenses fail automated compliance validation.
- Proprietary, non-commercial, source-available-only, paid-runtime, and AGPL dependencies are forbidden.
- GPL/LGPL/MPL and other reciprocal licenses require an explicit reviewed integration/redistribution analysis before use.
- Optional executables must be capability-detected and documented; OpenSorSe must start and retain non-dependent functionality when they are absent.
- Package metadata is checked against the committed inventory after restore.

## Selected OCR components

| Component | Integration | License | Distribution decision |
| --- | --- | --- | --- |
| PDFtoImage 5.2.1 | Managed renderer wrapper | MIT | Mandatory NuGet dependency. |
| PDFium native packages | Transitive rendering runtime | Apache-2.0 package metadata; upstream permissive notices retained | Bundled transitively by NuGet for supported desktop runtimes. |
| SkiaSharp | Managed image surface/encoding | MIT | Existing resolved dependency is now referenced directly by Application for bounded lazy media thumbnails; no additional resolved package or native family was introduced. |
| Tesseract | External OCR executable | Apache-2.0 | Optional and never downloaded or bundled by OpenSorSe. |
| Tesseract language data | External runtime data | Apache-2.0 project distributions; individual sources must be reviewed by distributors | Optional and user installed. |

Poppler and Ghostscript are not selected or bundled. A broken Poppler command shim in one development environment is not considered a runtime capability.

## Optional v2.2 media tools

v2.2 can invoke user-managed `ffprobe` for audio/video
metadata and `ffmpeg` for bounded representative frames. Neither executable,
codec, model, nor media runtime is downloaded or bundled by OpenSorSe. FFmpeg
build licensing varies with the distributor's configuration (commonly LGPL or
GPL); users and distributors are responsible for choosing and licensing their
external build. Absence, timeout, cancellation, malformed output, or unsupported
codecs leave ordinary Search and deterministic image metadata usable.

No Whisper-compatible transcription engine or visual-description model is
selected, downloaded, or bundled. v2.2 provides provider contracts and explicit
unavailable capability states only. Any future concrete provider requires a
separate dependency, license, size, platform, security, and redistribution
review under this policy.

The final v2.2 review considered MIT-licensed `whisper.cpp` 1.8.1,
MIT-licensed Whisper.net 1.9.1, and the MIT OpenAI Whisper Python reference.
`whisper.cpp` is the preferred future process-isolated direction but still
needs a versioned CLI/timestamp adapter, user-supplied model validation, safe
audio conversion, and native testing on every target. Whisper.net would add
per-RID native runtime assets and material package/native-validation scope.
The Python reference would make Python/PyTorch a large mandatory or
externally-managed runtime. None is added in v2.2, and no model is silently
downloaded.

## Embedded SQLite components

| Component | Integration | License | Distribution decision |
| --- | --- | --- | --- |
| Microsoft.Data.Sqlite 8.0.28 | Managed embedded provider | MIT | Mandatory only for the current durable-index provider project. |
| SQLitePCLRaw 2.1.12 and native bundle | Managed interop and per-runtime SQLite native library | Apache-2.0 | Pinned patched bundle; redistributed notices are required. |

These dependencies remain isolated in `OpenSorSe.Indexing.Sqlite`. They do not
add a database server requirement or adopt PostgreSQL. Target-specific package
contents must include only the expected native SQLite library for that runtime.

## Engineering-not-legal disclaimer

The inventory and checks are engineering controls intended to prevent accidental use of unknown or forbidden software. They are not legal advice and do not replace a distributor's final license/security review.
