# OpenSorSe

<p align="center">
  <img src="docs/images/opensorse-logo.png" width="144" alt="OpenSorSe logo">
</p>

<p align="center">
  <strong>Open Sort and Search</strong><br>
  Find clarity in your files.
</p>

OpenSorSe is a modern, open-source Windows desktop application for scanning, searching, understanding, and safely organizing selected folders. It combines fast local analysis, exact duplicate review, OCR, local meaning-based search, and optional Ollama-assisted suggestions without turning file management over to an autonomous agent.

> OpenSorSe 1.4.0 adds a local plugin foundation and stable Extension SDK while retaining preview-first, journalled file organization. Complete the [v1.4 manual checklist](docs/MANUAL_TESTING_v1.4.md) before publishing a binary release.

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
| **Watched Folders** | Persists selected roots, debounces filesystem hints, verifies real state, incrementally analyses changed files, reconciles offline/missed changes, and produces grouped activity without automatic file modification. |
| **Workflows** | Manages versioned scan/analysis profiles and constrained sorting recipes with deterministic previews, assignments, imports/exports, and historical revision snapshots. |
| **Local plugins** | Discovers and inspects bounded local extensions for metadata, extraction, classification, recipe fields, duplicate evidence, workflow capabilities, imports, and exports; external plugins require explicit enable/capability grants. |
| **Duplicate Detective** | Groups byte-identical files, shows potential reclaimable space, and supports review without offering automatic deletion. |
| **Meaning Search (Beta)** | Builds a bounded local index and finds related filenames, tags, metadata, native text, and OCR text with match explanations. |
| **File Assistant** | Produces validated, review-only rename and folder-structure suggestions for explicitly selected files and metadata. |
| **OCR / Text Recognition** | Extracts native PDF/Open XML text and can use an externally installed local Tesseract engine for images and scanned PDF pages. |
| **Saved scans** | Keeps optional bounded scan snapshots, accepted tags, searches, and comparisons in OpenSorSe application data. |
| **Folder plans** | Previews deterministic, root-confined organization plans before a separate explicit confirmation. |
| **Review Changes** | Converts accepted rename, folder, and deterministic organization suggestions into one editable Change Plan; each action can be approved or rejected before validation and explicit Apply. |
| **Safe file operations** | Renames files, moves files, and creates required folders without default overwrite; every attempted apply is durably recorded and verified. |
| **Operation History and Undo** | Persists action-level results, rollback and interruption details, supports a human-readable report, and offers conflict-aware Undo while resulting files remain unchanged. |
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

1. Download `OpenSorSe-v1.4.0-win-x64.zip` only from a published GitHub release after the v1.4 manual release checklist is complete. This source tree does not claim that package has been published.
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
- **Local plugins:** Optional external plugins are local ZIP packages. OpenSorSe does not download or automatically update them. They run in-process as the current user and must be trusted.

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

AI output is always untrusted and suggestion-only. Accepting a rename or folder suggestion creates a non-mutating Change Plan; it never invokes the filesystem. Only the reviewed, approved, validated, and explicitly confirmed plan can reach the dedicated execution service.

> OpenSorSe does not apply AI-generated or bulk file changes without a user-reviewed Change Plan. Supported file operations are recorded in the Operation Journal and are reversible unless later external changes make automatic restoration unsafe.

> Watched folders automate detection and analysis, not file modification.

> Workflow profiles automate configuration and analysis, not approval or file modification.

> Plugins provide analysis, suggestions, imports, and exports. They have no supported direct file-mutation or Change Plan approval path.

Watched-folder AI is separately disabled by default. Ignored files never enter AI analysis; requests contain at most 12 files, one cycle attempts at most 120 items, and per-file state makes retry select pending/failed work without repeating completed AI. Changes made while OpenSorSe is closed, paused, or disconnected are compared with the dedicated watched catalogue during startup/resume/reconnect reconciliation.

Read [Safety and Privacy](docs/SAFETY_AND_PRIVACY.md) for the complete boundary.

## Known limitations

- The previously produced v1.0 portable package targets Windows x64. A v1.4 package is not claimed until the v1.4 manual release checklist and packaging workflow are completed.
- Tesseract and its language data are external and required for image/scanned-page OCR. Rendered OCR page previews are not retained in diagnostics.
- Real approximately 2B, 4B, and 7B/8B Ollama compatibility remains pending the documented manual matrix.
- Folder-structure AI requests accept at most 12 selected files. Larger selections are rejected as a whole with the exact count shown; no file is silently omitted and no partial plan is generated.
- Watcher APIs can duplicate, reorder, omit, or overflow events. OpenSorSe treats them as hints and reconciles on startup, resume, reconnect, overflow, at least daily while running, and on demand.
- Watching runs only while OpenSorSe is open. A root renamed externally is shown as unavailable; v1.4 does not guess its new location.
- Permanent deletion, automatic unattended organization, cloud AI, and cloud synchronization remain outside v1.4.
- The legacy recipe ID `current` is not silently persisted; affected watched folders require a deliberate persistent replacement.
- Workflow transfer uses versioned JSON in the Workflows area. There is no cloud library, synchronization, marketplace, or arbitrary scripting.
- v1.4 plugins are in-process. Assembly-load-context isolation and SHA-256 integrity checking are not an OS sandbox, code review, signature authority, or publisher authentication.
- Filesystem work is transaction-like, not a true filesystem transaction. OpenSorSe revalidates, journals, verifies, and attempts reverse-order rollback, but hardware, permission, or external-process failures can require manual recovery.
- Undo is blocked rather than overwriting when a result was moved, replaced, materially modified, used by a later OpenSorSe operation, or when the original path became occupied.
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
  --output .\release\OpenSorSe-v1.4.0
```

## Documentation

- [Documentation index](docs/README.md)
- [Installation](docs/INSTALLATION.md)
- [Release status](docs/RELEASE_STATUS.md)
- [v1.4 user guide](docs/USER_GUIDE_v1.4.md)
- [v1.4 manual testing](docs/MANUAL_TESTING_v1.4.md)
- [v1.4 version notes](docs/VERSION_NOTES_v1.4.md)
- [v1.4 troubleshooting](docs/TROUBLESHOOTING_v1.4.md)
- [Extension SDK](docs/EXTENSION_SDK_v1.4.md)
- [Plugin author guide](docs/PLUGIN_AUTHOR_GUIDE_v1.4.md)
- [Plugin manifest reference](docs/PLUGIN_MANIFEST_REFERENCE_v1.4.md)
- [Local plugin packages](docs/LOCAL_PLUGIN_PACKAGES_v1.4.md)
- [Plugin architecture](docs/Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md)
- [Workflow architecture](docs/Architecture/07-Rules/08_v1.3_Workflow_Profiles_and_Recipes.md)
- [Safety and privacy](docs/SAFETY_AND_PRIVACY.md)
- [Advanced diagnostics architecture](docs/Architecture/01_Core/10_Advanced_Diagnostics.md)
- [Small-model AI prompt contracts](docs/Architecture/04_AI/11_Small_Model_Prompt_Contracts.md)
- [Architecture overview](docs/ARCHITECTURE_OVERVIEW.md)
- [System map](docs/Architecture/OpenSorSe_System_Map.md)
- [Repository structure](docs/REPOSITORY_STRUCTURE.md)
- [Developer guide](docs/DEVELOPER_GUIDE.md)
- [Maintainer guide](docs/MAINTAINER_GUIDE.md)
- [Contributing](CONTRIBUTING.md)
- [Implementation specifications](docs/Implementation_Spec/README.md)
- [Changelog](docs/CHANGELOG.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## Roadmap

**v1.4 — Plugin Foundation and Extension SDK** is implemented in source on `v1.4-plugin-foundation`; automated and manual completion status is tracked in the release documentation. Autonomous AI file management and unrestricted filesystem control are not roadmap goals.

See the detailed [roadmap](docs/roadmap.md).

## Contributing

Contributions are welcome in focused, reviewable changes. Useful areas include:

- Reproducing and documenting bugs with safe disposable test data. Prefer an explicitly exported redacted Advanced Diagnostic; inspect it before attaching it to an issue.
- Improving accessibility, keyboard workflows, and high-DPI behavior.
- Adding defensive parser, migration, and provider-failure tests.
- Improving documentation and platform verification.
- Proposing bounded local-first features that preserve the safety model.

Before submitting a change, run the complete Debug and Release test suites, avoid committing generated `bin`/`obj` output, and explain any effect on privacy or source-file mutation boundaries.

Read [CONTRIBUTING.md](CONTRIBUTING.md) for the dependency, safety,
documentation, test, and pull-request requirements.

## License

OpenSorSe is available under the [MIT License](LICENSE). Bundled and referenced dependencies retain their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the [dependency policy](docs/FOSS_DEPENDENCY_POLICY.md), and the [machine-readable dependency inventory](docs/dependency-licenses.json).
