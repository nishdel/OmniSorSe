# Third-Party Notices

OpenSorSe is MIT licensed and uses free/open-source dependencies. The exact package/version inventory used for the final 1.0 validation is in [`docs/dependency-licenses.json`](docs/dependency-licenses.json). This engineering notice is not legal advice. The portable binary distribution includes this notice, the OpenSorSe license, and the dependency license/notice files applicable to its shipped runtime files.

## Avalonia

Avalonia UI and its managed/platform packages are MIT licensed. Copyright and license details are available from the [Avalonia repository](https://github.com/AvaloniaUI/Avalonia).

## ANGLE

`Avalonia.Angle.Windows.Natives` includes ANGLE binaries under a BSD 3-Clause-style license. Its copyright, redistribution conditions, and disclaimer are embedded as `LICENSE` in the NuGet package and must accompany redistributed binaries.

## Microsoft .NET

Microsoft.Extensions, .NET compatibility/runtime libraries, and the Microsoft test platform packages listed in the inventory use the MIT license. Test-only packages are not part of the application runtime output.

## PDFtoImage

PDFtoImage is MIT licensed. OpenSorSe uses version 5.2.1 as the managed wrapper for page rendering. See the [PDFtoImage repository](https://github.com/sungaila/PDFtoImage).

## PDFium

The `bblanchon.PDFium.*` runtime packages are declared Apache-2.0 and contain PDFium native binaries. PDFium incorporates separately licensed permissive components; a binary distributor must retain the package and upstream third-party notices. See the [PDFium license](https://pdfium.googlesource.com/pdfium/+/HEAD/LICENSE).

## PdfPig

PdfPig 0.1.15 is Apache-2.0 licensed and is used for read-only, page-aware native PDF text and metadata extraction. See the [PdfPig repository](https://github.com/UglyToad/PdfPig).

## Tesseract OCR

Tesseract is Apache-2.0 licensed. It is an optional, externally managed executable: OpenSorSe neither downloads nor bundles Tesseract or its language data. Users or distributors who install or package Tesseract are responsible for retaining its license and reviewing the source/license of the chosen trained-data files. See the [Tesseract repository](https://github.com/tesseract-ocr/tesseract).

## Other MIT components

CommunityToolkit.Mvvm, Newtonsoft.Json, PDFtoImage, Tmds.DBus.Protocol, HarfBuzzSharp, MicroCom.Runtime, SkiaSharp, and coverlet packages in the inventory are MIT licensed. Retain each package's copyright and permission notice when its files are redistributed.

## Apache-2.0 components

NuGet.Frameworks and xUnit.net packages in the inventory are Apache-2.0 licensed. Test-only components are not part of normal application runtime output. Retain the Apache-2.0 license and any component NOTICE file when redistributing them.
