# OmniSorSe

<p align="center">
  <img src="docs/images/opensorse-logo.png" width="144" alt="OmniSorSe logo">
</p>

<p align="center">
  <strong>Omni Sort and Search</strong><br>
  Find clarity in your files.
</p>

OmniSorSe is an open-source, local-first desktop application for finding,
understanding, and safely organizing files in folders you explicitly select.
It combines deterministic analysis, local Search, duplicate review, content
and relationship intelligence, and review-before-change organization.

OmniSorSe is not an autonomous file manager. Built-in analysis, rules,
workflows, and optional AI can propose supported changes, but file operations
require a reviewed Change Plan and separate Apply confirmation. Third-party
plugins are trusted in-process extensions, not a security sandbox.

<p align="center">
  <a href="https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0"><strong>Download v2.4.0</strong></a>
  · <a href="docs/INSTALLATION.md">Installation</a>
  · <a href="docs/CURRENT-STATE.md">Current source</a>
  · <a href="docs/README.md">Documentation</a>
  · <a href="CONTRIBUTING.md">Contribute</a>
</p>

## What OmniSorSe helps you do

- **Find files locally.** Search filenames, folders, metadata, retained text,
  OCR, tags, and other indexed evidence without requiring a cloud account.
- **Understand why a result matters.** Inspect source-labelled snippets,
  ranking explanations, Smart Tags, Related Files, and virtual collections.
- **Review duplicates and library changes.** Compare exact duplicates, monitor
  selected folders, and keep indexed state synchronized with filesystem truth.
- **Organize without surrendering control.** Turn supported suggestions into a
  Change Plan that must be reviewed, validated, and explicitly applied.
- **Use optional intelligence deliberately.** Add local OCR/media tools,
  Ollama-compatible assistance, workflows, or trusted plugins without making
  them prerequisites for ordinary scanning and Search.

## How it works

1. **Choose folders.** OmniSorSe works only with roots you explicitly select.
2. **Scan and index locally.** It builds a local catalogue of metadata,
   searchable content, relationships, and duplicate evidence.
3. **Search and review.** Explore files, explanations, related items,
   collections, and proposed organization before anything changes.
4. **Approve supported changes separately.** Supported organization actions
   have a separate preview and confirmation, recorded history, verification,
   recovery, and Undo.

## Current source and latest release

| Track | What it is | Start here |
| --- | --- | --- |
| **Current source: v2.12 candidate** | GitHub `main` contains the integrated v2.12 Trusted Relationships & Context candidate on .NET 10. It is source-only: not tagged, packaged, or published as a GitHub Release. | [Current State](docs/CURRENT-STATE.md) · [v2.12 record](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) · [Release Status](docs/RELEASE_STATUS.md) |
| **Latest downloadable release: v2.4.0** | The latest tagged and packaged OmniSorSe release for Windows x64 and macOS Intel/Apple Silicon. | [Download v2.4.0](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0) · [Installation](docs/INSTALLATION.md) · [Release Notes](docs/RELEASE_NOTES_v2.4.0.md) |

The v2.4.0 package keeps established OpenSorSe application-data, schema,
installer, and bundle identifiers where compatibility requires them. The
visible product is OmniSorSe; retained internal names prevent a branding-only
upgrade from creating an empty profile or unnecessary reindex.

## Downloads and platform availability

| Platform | Availability | Important boundary |
| --- | --- | --- |
| Windows x64 | Per-user installer or self-contained portable ZIP | Primary desktop target. Packages are unsigned, so SmartScreen may warn. |
| macOS Intel | Self-contained DMG | Unsigned and unnotarized; Gatekeeper may require a reviewed override. Source-file mutation remains disabled where equivalent filesystem safety cannot be proven. |
| macOS Apple Silicon | Self-contained DMG | Same support and safety boundary as the Intel package. |
| Linux x64 | No package; current source build only | Conservative source-build preview; no Linux installer is published. |

Download packages only from the
[official v2.4.0 release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.4.0)
and verify the accompanying SHA-256 file. Checksums detect changed bytes but do
not authenticate an unsigned publisher. See [Installation](docs/INSTALLATION.md)
for exact filenames, checksum commands, updates, application-data locations,
and Linux guidance.

## Current candidate highlights

The v2.12 source builds on the v2.5–v2.11 candidate lineage. Compared with the
latest packaged release, current `main` adds or strengthens:

- explainable Smart Tags, complete-index facets, and dynamic Saved Views;
- guided Search-to-Files workflows and reviewed organization previews;
- direct Related Files, reversible relationship decisions, and authored Smart
  Collection state;
- profile ownership, corruption-safe recovery, logical state backup/restore,
  coordinated removal of indexed/user state, and improved post-operation
  reconciliation;
- .NET 10 runtime and package provenance validation.

These are implemented candidate capabilities, not a v2.12 release claim. Read
[Current State](docs/CURRENT-STATE.md) for the concise implemented boundary and
[Release Status](docs/RELEASE_STATUS.md) for verified versus still-manual
evidence.

## Safety and privacy by design

- Built-in scanning, Search, OCR, relationships, collections, workflows, and AI
  do not directly modify selected files.
- Supported file operations require a reviewed Change Plan, separate Apply
  confirmation, immediate preflight, durable journalling, and result
  verification. Destinations are not silently overwritten.
- Recovery and Undo verify current filesystem facts and report ambiguity rather
  than guessing; they are compensating operations, not universal transactions.
- Ordinary scanning and Search stay local. AI is optional and disabled by
  default. A custom Ollama-compatible endpoint can be remote, so explicitly
  requested AI input may then cross that privacy boundary.
- Plugins run in-process with the current user's permissions. Capability grants
  and integrity checks are not an operating-system sandbox or publisher
  authentication.

Read [Safety and Privacy](docs/SAFETY_AND_PRIVACY.md) for the complete current
contract and [Platform Compatibility](docs/PLATFORM_COMPATIBILITY_MATRIX.md)
before making a native-platform support claim.

## Optional tools

OmniSorSe remains useful without these externally managed components:

- **Ollama-compatible service:** optional bounded assistance and Search
  reranking over already selected candidates.
- **Tesseract 5:** optional local OCR recognition.
- **ffprobe/ffmpeg:** optional bounded audio/video metadata and representative
  frame processing.
- **whisper.cpp CLI and model:** optional user-managed local transcription.
- **Plugins:** optional trusted local packages running in-process.

Nothing above is downloaded or enabled silently by OmniSorSe.

## Documentation

Start at the [Documentation Index](docs/README.md) for the comprehensive,
intent-based map. The shortest routes are:

| I want to… | Read first | Then continue with… |
| --- | --- | --- |
| Understand the project and current state | [Current State](docs/CURRENT-STATE.md) | [Product Vision](PRODUCT_VISION.md) |
| Install or use the latest release | [Installation](docs/INSTALLATION.md) | [v2.4.0 Release Notes](docs/RELEASE_NOTES_v2.4.0.md) |
| Build or contribute | [Developer Guide](docs/DEVELOPER_GUIDE.md) | [Contributing](CONTRIBUTING.md) and [Engineering Principles](ENGINEERING_PRINCIPLES.md) |
| Understand the architecture | [Architecture Overview](docs/ARCHITECTURE_OVERVIEW.md) | [System Map](docs/Architecture/OpenSorSe_System_Map.md) and [Architecture Library](docs/Architecture/README.md) |
| Review the current candidate | [v2.12 Trusted Relationships & Context](docs/TRUSTED_RELATIONSHIPS_CONTEXT_v2.12.md) | [Candidate lineage](docs/README.md#current-unreleased-candidate-lineage) |
| Check validation or readiness | [Release Status](docs/RELEASE_STATUS.md) | [Platform Compatibility](docs/PLATFORM_COMPATIBILITY_MATRIX.md) and versioned manual gates |
| Research released or historical work | [Release History](RELEASE_HISTORY.md) | [Changelog](docs/CHANGELOG.md) and [historical records](docs/README.md#release-and-implementation-records) |

Versioned specifications, reports, and checklists remain available as
historical evidence without competing with living current documentation.

## Build from source

The current source requires the .NET 10 SDK selected by [`global.json`](global.json).

```powershell
git clone https://github.com/nishdel/OmniSorSe.git
Set-Location .\OmniSorSe
dotnet restore .\OpenSorSe.sln
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Use disposable folders when testing Change Plan Apply, rollback, recovery, or
Undo. See the [Developer Guide](docs/DEVELOPER_GUIDE.md) for architecture and
validation, or [Linux Build and Launch](docs/LINUX_BUILD_AND_LAUNCH.md) for the
Linux preview.

## Contributing

Contributions are welcome when they are focused, reviewable, tested at the
owning layer, and explicit about privacy, persistence, platform, and
source-file safety. Start with [CONTRIBUTING.md](CONTRIBUTING.md); cross-cutting
work should also read [Engineering Principles](ENGINEERING_PRINCIPLES.md).

## License

OmniSorSe is available under the [MIT License](LICENSE). Dependencies retain
their own licenses; see [Third-Party Notices](THIRD_PARTY_NOTICES.md), the
[FOSS Dependency Policy](docs/FOSS_DEPENDENCY_POLICY.md), and the
[machine-readable dependency inventory](docs/dependency-licenses.json).
