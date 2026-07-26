# OpenSorSe v1.0 README and Windows Distribution

| Field | Value |
| --- | --- |
| Branch | `v1.0` |
| Version | `1.0.0` |
| Distribution | Self-contained Windows x64 portable ZIP |
| Installer | Deferred; no repository signing identity or MSIX/Inno/NSIS toolchain |

## Objective

Prepare a public-facing GitHub README and a reproducible Windows x64 release artifact without changing product safety boundaries, publishing a remote release, or claiming completion of the remaining manual OCR/Ollama/workflow checklist.

## Deliverables

- A concise README with official branding, quick links, features, privacy, installation, roadmap, contribution, and licensing sections.
- Commented screenshot slots under `docs/images/`; no generated mockups or placeholder screenshots.
- `OpenSorSe.exe` with version 1.0.0 product metadata and the official embedded icon.
- A self-contained, untrimmed Windows x64 runtime directory.
- A portable ZIP and SHA-256 checksum.
- Included README, license, notices, changelog, installation guide, release notes, dependency inventory, and documentation.

## Publish contract

```powershell
dotnet publish .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\release\OpenSorSe-v1.0.0 `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -p:DebugSymbols=false
```

Trimming is disabled because Avalonia and application composition rely on reflection and generated resources. A multi-file package keeps native dependencies and failure diagnosis explicit. The generated apphost lets users run `OpenSorSe.exe` without separately installing .NET.

## Installer decision

MSIX is not produced because the repository has no selected package identity, signing certificate, or deployment policy. Inno Setup and NSIS are not installed or part of the repository toolchain. An unsigned installer would add maintenance and trust surfaces without improving the portable application’s safety. The self-contained ZIP is therefore the v1.0 candidate artifact; signed installer work remains post-v1.0.

## Privacy and safety

Packaging does not add telemetry, cloud services, update checks, elevated installation, registry changes, shell integration, or new filesystem operations. Optional Ollama and Tesseract remain external and are not bundled. User settings and owned indexes continue under `%LOCALAPPDATA%\OpenSorSe`.

## Validation

- Restore and Debug/Release build/test.
- Confirm all required package files and the self-contained runtime declaration.
- Inspect executable product/file versions and extract the associated icon.
- Inspect ZIP entries and verify the generated SHA-256 checksum.
- Launch the published executable, confirm the `OpenSorSe` window appears and responds, then close it.
- Retain the full manual workflow checklist as a release hold.
