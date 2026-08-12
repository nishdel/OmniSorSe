# Third-Party Notices

OpenSorSe is MIT licensed and uses free/open-source dependencies. The exact package/version inventory used for the current validation is in [`docs/dependency-licenses.json`](docs/dependency-licenses.json). This engineering notice is not legal advice. The portable binary distribution includes this notice, the OpenSorSe license, and the dependency license/notice files applicable to its shipped runtime files.

## Avalonia

Avalonia UI and its managed/platform packages are MIT licensed. Copyright and license details are available from the [Avalonia repository](https://github.com/AvaloniaUI/Avalonia).

## ANGLE

`Avalonia.Angle.Windows.Natives` includes ANGLE binaries under a BSD 3-Clause-style license. Its copyright, redistribution conditions, and disclaimer are embedded as `LICENSE` in the NuGet package and must accompany redistributed binaries.

## Microsoft .NET

Microsoft.Extensions, .NET compatibility/runtime libraries, and the Microsoft test platform packages listed in the inventory use the MIT license. Test-only packages are not part of the application runtime output.

## Embedded SQLite indexing

Microsoft.Data.Sqlite 8.0.28 is MIT licensed. SQLitePCLRaw 2.1.12 and its bundled native SQLite packages use Apache-2.0. OpenSorSe pins the patched native bundle rather than accepting the older transitive minimum. These components are used only inside the embedded, provider-isolated indexing store; users do not need to install a database server. Retain the applicable MIT and Apache-2.0 notices when redistributing application binaries.

## PDFtoImage

PDFtoImage is MIT licensed. OpenSorSe uses version 5.2.1 as the managed wrapper for page rendering. See the [PDFtoImage repository](https://github.com/sungaila/PDFtoImage).

## PDFium

The `bblanchon.PDFium.*` runtime packages are declared Apache-2.0 and contain PDFium native binaries. PDFium incorporates separately licensed permissive components; a binary distributor must retain the package and upstream third-party notices. See the [PDFium license](https://pdfium.googlesource.com/pdfium/+/HEAD/LICENSE).

## PdfPig

PdfPig 0.1.15 is Apache-2.0 licensed and is used for read-only, page-aware native PDF text and metadata extraction. See the [PdfPig repository](https://github.com/UglyToad/PdfPig).

## Tesseract OCR

Tesseract is Apache-2.0 licensed. It is an optional, externally managed executable: OpenSorSe neither downloads nor bundles Tesseract or its language data. Users or distributors who install or package Tesseract are responsible for retaining its license and reviewing the source/license of the chosen trained-data files. See the [Tesseract repository](https://github.com/tesseract-ocr/tesseract).

## Optional external media tools

The v2.2 source can use separately installed `ffprobe` and `ffmpeg` executables.
They are not downloaded or redistributed by OpenSorSe. FFmpeg build licensing
depends on how that external build was configured; users and downstream
distributors must review the license and codec terms of the build they choose.

The unmerged v2.3 candidate can also invoke a separately installed
MIT-licensed whisper.cpp CLI with a user-supplied local GGML model. OpenSorSe
does not download, bundle, or redistribute that executable or model. Users and
downstream distributors must review and retain the license and provenance of
the exact runtime and model they choose. The optional adapter does not make
whisper.cpp part of the OpenSorSe binary distribution.

## Other MIT components

CommunityToolkit.Mvvm, Newtonsoft.Json, PDFtoImage, Tmds.DBus.Protocol, HarfBuzzSharp, MicroCom.Runtime, SkiaSharp, and coverlet packages in the inventory are MIT licensed. Retain each package's copyright and permission notice when its files are redistributed.

## Apache-2.0 components

NuGet.Frameworks and xUnit.net packages in the inventory are Apache-2.0 licensed. Test-only components are not part of normal application runtime output. Retain the Apache-2.0 license and any component NOTICE file when redistributing them.
