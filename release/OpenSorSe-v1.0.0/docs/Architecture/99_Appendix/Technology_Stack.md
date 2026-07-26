# Technology Stack

> This document distinguishes technologies used by OpenSorSe 1.0 from post-1.0 design ideas.

---

## Implemented stack

| Area | Technology | Current use |
| --- | --- | --- |
| Runtime | .NET 8 target framework | All production and test projects target `net8.0`. |
| Language | C# | Primary implementation language. |
| Desktop UI | Avalonia UI | Cross-platform-capable desktop presentation framework. |
| Presentation pattern | MVVM | Separates view state and commands from application and scanner logic. |
| MVVM support | CommunityToolkit.Mvvm | Observable view models and commands. |
| Infrastructure | Microsoft.Extensions dependency injection and logging | Service composition and diagnostic logging. |
| Testing | xUnit, Microsoft.NET.Test.Sdk, coverlet collector | Automated unit and integration test coverage. |
| Documentation | Markdown and Mermaid | Repository documentation and architecture diagrams. |
| Version control | Git | Local repository history and collaboration workflow. |
| Native PDF text | PdfPig 0.1.15 | Read-only bounded page text/metadata with deterministic quality checks. |
| PDF page rendering | PDFtoImage 5.2.1 + PDFium native packages | Built-in bounded rendering of insufficient scanned/mixed PDF pages. |
| Local OCR integration | Tesseract CLI capability detection | Optional bounded PNG/JPEG/TIFF and rendered-PDF OCR Beta; the engine and `eng`/`deu` data remain externally installed. |
| Local similarity | Deterministic feature hashing | Rebuildable Semantic Search Beta vectors without a model download or network service. |
| Local persistence | `System.Text.Json` + atomic replace | Separate bounded versioned settings/catalog/content/semantic/history stores. |

The repository pins an SDK version in the root [global.json](../../../global.json). The pinned SDK may be newer than the .NET 8 target framework; the application runtime target remains .NET 8.

## Current non-adoptions

The 1.0 release does not use the following as implemented product capabilities:

- SQLite or a database-backed full-text/vector engine.
- Learned embedding models, cloud AI APIs, or GPU acceleration. Ollama remains the single optional provider for validated review-only suggestions; Semantic Search uses deterministic local feature hashing.
- Bundled Tesseract executables or language/model data.
- Plugin loading or a plugin marketplace.
- Python, PySide, or other legacy desktop stack components.

## Future technology considerations

Richer readers, additional AI providers, database indexes, learned embeddings, reports, packaging, localization, and plugins are future architectural ideas. The bounded 1.0 extractors, PDF rasterization, local OCR integration, deterministic semantic index, and JSON structure history are implemented. A technology named in a future architecture document is not a dependency until a release specification and code add it.

Future technology selection should continue to prioritize local-first privacy, user control, maintainability, and explicit safety boundaries for any feature that could affect user files.

## Related documents

- [System Overview](../00_System/00_Overview.md)
- [Release Status](../../RELEASE_STATUS.md)
- [Roadmap](../../roadmap.md)
