# Runtime dependency licences

This directory accompanies the OpenSorSe 1.0 Windows x64 portable build. The
versioned component inventory and source links are in
`../dependency-licenses.json`; concise attribution and bundling notes are in
`../THIRD_PARTY_NOTICES.md`.

The following copied upstream files apply directly to shipped runtime
components:

- `ANGLE-LICENSE.txt`: ANGLE native binaries used by Avalonia.
- `CommunityToolkit.Mvvm-LICENSE.md` and
  `CommunityToolkit.Mvvm-ThirdPartyNotices.txt`.
- `DOTNET-LICENSE.txt` and `DOTNET-ThirdPartyNotices.txt`: the self-contained
  Microsoft .NET runtime.
- `HarfBuzzSharp-LICENSE.txt`.
- `Microsoft.Extensions-LICENSE.txt`.
- `Newtonsoft.Json-LICENSE.md`.
- `SkiaSharp-LICENSE.txt`.

`MIT-LICENSE-TEXT.txt` is the common licence text declared by the versioned
Avalonia, PDFtoImage, Tmds.DBus.Protocol, MicroCom.Runtime, and other MIT
packages listed in the inventory. Their applicable authors and copyright
holders are identified by the package metadata and upstream links in the
inventory and third-party notice.

`Apache-2.0.txt` is the canonical licence text declared by the shipped PDFium
binary packages and PdfPig. PDFium incorporates additional permissively
licensed components; see the PDFium source link and redistribution note in the
inventory before redistributing a modified package.

Tesseract and its language data are not bundled. If a distributor adds them,
that distributor must include their applicable licences and trained-data
notices.
