# OpenSorSe

<p align="center">
  <img src="docs/images/opensorse-logo.png" width="144" alt="OpenSorSe logo">
</p>

<p align="center">
  <strong>Open Sort and Search</strong><br>
  Find clarity in your files.
</p>

OpenSorSe is a modern, open-source Windows desktop application for scanning, searching, understanding, and safely organizing selected folders. It combines fast local analysis, exact duplicate review, OCR, local meaning-based search, and optional Ollama-assisted suggestions without turning file management over to an autonomous agent.

> OpenSorSe 1.0.0 is a final manual-validation candidate. Complete the [release checklist](docs/RELEASE_CHECKLIST_v1.0.md) before treating it as a production release.

## Quick links

- [Installation](#installation)
- [Features](#features)
- [Known limitations](#known-limitations)
- [Screenshots](#screenshots)
- [Documentation](#documentation)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

## Why OpenSorSe?

- **Local-first:** Scanning, OCR, indexes, tags, saved scans, and history remain on your computer.
- **Open source:** The application and its safety boundaries can be inspected and improved publicly.
- **AI-assisted, not AI-controlled:** Optional local AI produces bounded, validated suggestions for review.
- **Privacy-focused:** No cloud account is required, and AI communication is disabled by default.
- **Fast:** Results are paged, indexed, bounded, cancellable, and designed for responsive desktop use.
- **Lightweight:** Everyday workflows remain simple while technical tools stay behind Advanced mode.
- **Modern desktop experience:** Avalonia provides a native-feeling resizable interface, keyboard access, dark/light resources, and high-DPI support.

## Features

| Feature | What it provides |
| --- | --- |
| **Fast folder scanning** | Recursively scans selected folders with progress, cancellation, recoverable error isolation, metadata classification, and exact duplicate detection. |
| **Duplicate Detective** | Groups byte-identical files, shows potential reclaimable space, and supports review without offering automatic deletion. |
| **Meaning Search (Beta)** | Builds a bounded local index and finds related filenames, tags, metadata, native text, and OCR text with match explanations. |
| **File Assistant** | Produces validated, review-only rename and folder-structure suggestions for explicitly selected files and metadata. |
| **OCR / Text Recognition** | Extracts native PDF/Open XML text and can use an externally installed local Tesseract engine for images and scanned PDF pages. |
| **Saved scans** | Keeps optional bounded scan snapshots, accepted tags, searches, and comparisons in OpenSorSe application data. |
| **Folder plans** | Previews deterministic, root-confined organization plans before a separate explicit confirmation. |
| **Local AI support** | Uses compact versioned prompts, exact structured-output schemas, strict application validation, and one bounded shape-repair pass with an explicitly configured Ollama-compatible endpoint. |
| **Dashboard** | Summarizes the latest scan and routes directly to common workflows. |
| **Smart organization** | Combines tags, classifications, metadata, safe proposals, conflict checks, and structure history without silent changes. |

## Screenshots

Screenshot files belong in [`docs/images/`](docs/images/README.md). The comments below already contain the intended relative Markdown links; uncomment each line after adding the corresponding real application capture.

### Home

<!-- Home Screenshot: ![OpenSorSe Home dashboard](docs/images/home.png) -->

The Home dashboard provides a clear first-run state, latest-scan summary, and direct routes to scanning and settings.

### Files

<!-- Files Screenshot: ![OpenSorSe Files workspace](docs/images/files.png) -->

The Files workspace combines fixed search controls, a resizable explorer table, tags, metadata, and selection-only contextual tools.

### Duplicate Detective

<!-- Duplicate Detective Screenshot: ![OpenSorSe Duplicate Detective](docs/images/duplicate-detective.png) -->

Duplicate Detective keeps exact-copy groups visible while showing selected locations and potential space savings.

### File Assistant

<!-- File Assistant Screenshot: ![OpenSorSe File Assistant](docs/images/file-assistant.png) -->

File Assistant clearly reports local-model readiness and presents unverified suggestions for review without applying them automatically.

### Meaning Search

<!-- Meaning Search Screenshot: ![OpenSorSe Meaning Search](docs/images/meaning-search.png) -->

Meaning Search searches the local deterministic index and explains why each result matched.

### Settings

<!-- Settings Screenshot: ![OpenSorSe Settings](docs/images/settings.png) -->

Settings keeps AI, Advanced mode, OCR, local indexing, provider configuration, and privacy-sensitive controls explicit.

## Installation

### Portable ZIP

1. Download `OpenSorSe-v1.0.0-win-x64.zip` from the GitHub release.
2. Extract the entire archive to a writable folder.
3. Run `OpenSorSe.exe`.

The Windows x64 package is self-contained; users do not need to install the .NET runtime separately. Keep all extracted runtime files beside the executable.

### Windows executable

`OpenSorSe.exe` is the packaged native Windows apphost. Windows may show a SmartScreen warning until public releases are code-signed. Review the release checksum and publisher repository before choosing **Run anyway**.

### Installer

An installer is not currently provided. The portable package avoids unsigned MSIX identity/signing requirements and can be removed by deleting its extracted program folder. OpenSorSe-owned settings and indexes remain under the current user’s local application-data folder until removed separately.

### Optional components

- **Ollama:** Required only for explicitly enabled File Assistant capabilities. Install and manage it separately, then select an installed model in OpenSorSe Settings.
- **Advanced diagnostics:** A default-off, non-modal viewer unifies live AI, OCR/text-extraction, and scanning sessions with correlation, filters, copy/export, explicit bounds, and redacted display by default. Duplicate, search/indexing, rules/organisation, file-operation, and performance categories are visible as not yet instrumented. Records remain memory-only unless manually exported.
- **Tesseract 5:** Required only for local OCR recognition. Native metadata and supported document text extraction work without it. English (`eng`) and/or German (`deu`) language data must match the configured languages.
- **Developing from source:** Requires the .NET SDK version selected by [`global.json`](global.json).

See the complete [Windows installation guide](docs/INSTALLATION.md).

## Privacy

OpenSorSe is local-first:

- Selected files are not uploaded by scanning, OCR, saved scans, or Meaning Search.
- OCR runs through local libraries and an optional local Tesseract installation.
- Meaning Search uses a local, rebuildable deterministic index.
- AI is optional, disabled by default, and contacted only for explicit enabled requests.
- File Assistant prompts target small local instruction models, but no model-size compatibility is claimed until the documented manual matrix is completed.
- No cloud account is required.
- Ordinary logs exclude raw document/OCR text, vectors, credentials, and raw model payloads.
- Advanced diagnostics are independently gated, redacted by default, cleared when disabled or on exit, and never retain credentials even in unredacted mode.

A custom Ollama-compatible endpoint can be remote. When configured that way, explicitly requested AI metadata—or separately enabled bounded document text—can leave the computer. OpenSorSe displays this distinction in Settings.

AI output is always untrusted and suggestion-only. The only source-file mutation in v1.0 is a deterministic folder plan that is previewed, separately confirmed, root-confined, conflict checked, non-overwriting, and unrelated to AI output.

Read [Safety and Privacy](docs/SAFETY_AND_PRIVACY.md) for the complete boundary.

## Known limitations

- The provided v1.0 portable package targets Windows x64; other platforms are not release-validated.
- Tesseract and its language data are external and required for image/scanned-page OCR. Rendered OCR page previews are not retained in diagnostics.
- Real approximately 2B, 4B, and 7B/8B Ollama compatibility remains pending the documented manual matrix.
- Folder-structure AI requests accept at most 12 selected files. Larger selections are rejected as a whole with the exact count shown; no file is silently omitted and no partial plan is generated.
- Duplicate detection, search/indexing, rules/organisation, file-operation, and standalone performance diagnostic categories are registered extension points but are not instrumented in v1.0.
- The portable executable is not code-signed, so Windows SmartScreen may warn.

## Build from source

```powershell
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet test .\OpenSorSe.sln --configuration Debug --no-build
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Create the self-contained Windows release:

```powershell
dotnet publish .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\release\OpenSorSe-v1.0.0
```

## Documentation

- [Installation](docs/INSTALLATION.md)
- [Release status](docs/RELEASE_STATUS.md)
- [v1.0 manual testing](docs/MANUAL_TESTING_v1.0.md)
- [Safety and privacy](docs/SAFETY_AND_PRIVACY.md)
- [Advanced diagnostics architecture](docs/Architecture/01_Core/10_Advanced_Diagnostics.md)
- [Small-model AI prompt contracts](docs/Architecture/04_AI/11_Small_Model_Prompt_Contracts.md)
- [Architecture](docs/Architecture/00_System/00_Overview.md)
- [Implementation specifications](docs/Implementation_Spec/README.md)
- [Changelog](docs/CHANGELOG.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## Roadmap

After v1.0, likely areas include signed Windows distribution, packaging automation, broader platform verification, localization, accessibility refinement, index-quality improvements, export/report workflows, and carefully scoped extension points. Autonomous AI file management and unrestricted filesystem control are not roadmap goals.

See the detailed [roadmap](docs/roadmap.md).

## Contributing

Contributions are welcome in focused, reviewable changes. Useful areas include:

- Reproducing and documenting bugs with safe disposable test data. Prefer an explicitly exported redacted Advanced Diagnostic; inspect it before attaching it to an issue.
- Improving accessibility, keyboard workflows, and high-DPI behavior.
- Adding defensive parser, migration, and provider-failure tests.
- Improving documentation and platform verification.
- Proposing bounded local-first features that preserve the safety model.

Before submitting a change, run the complete Debug and Release test suites, avoid committing generated `bin`/`obj` output, and explain any effect on privacy or source-file mutation boundaries.

## License

OpenSorSe is available under the [MIT License](LICENSE). Bundled and referenced dependencies retain their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the [dependency policy](docs/FOSS_DEPENDENCY_POLICY.md), and the [machine-readable dependency inventory](docs/dependency-licenses.json).
