# Free and Open-Source Dependency Policy

OpenSorSe 1.0 accepts only dependencies with a documented free/open-source license and a redistribution path compatible with the repository's MIT distribution model.

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
| SkiaSharp | Transitive image surface/encoding | MIT | Already present through Avalonia; version unified by restore. |
| Tesseract | External OCR executable | Apache-2.0 | Optional and never downloaded or bundled by OpenSorSe. |
| Tesseract language data | External runtime data | Apache-2.0 project distributions; individual sources must be reviewed by distributors | Optional and user installed. |

Poppler and Ghostscript are not selected or bundled. A broken Poppler command shim in one development environment is not considered a runtime capability.

## Engineering-not-legal disclaimer

The inventory and checks are engineering controls intended to prevent accidental use of unknown or forbidden software. They are not legal advice and do not replace a distributor's final license/security review.
