# Installing OmniSorSe

**Document type:** Living installation and update guidance

## Availability

The latest published package is OmniSorSe v2.4.0 from the official
[GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.4.0) as:

- a self-contained Windows x64 portable ZIP;
- a per-user Windows x64 installer;
- an Intel macOS DMG;
- an Apple Silicon macOS DMG;
- one SHA-256 checksum file covering all four packages.

No Linux installer is published. Linux x64 remains a source-build preview.
Do not download packages from unrelated sites. Check [Release Status](RELEASE_STATUS.md)
and [v2.4.0 Release Notes](RELEASE_NOTES_v2.4.0.md) before relying on a package.

The visible rename deliberately
continues using the established OpenSorSe application-data directories, schema-5
database, Windows installer AppId/default install directory, and macOS bundle
identifier so existing profiles are not orphaned. See the
[v2.4 transition design](OMNISORSE_TRANSITION_AND_EXPLORER_PROTOCOL_v2.4.md).

## Windows x64 installer

1. Download `OmniSorSe-v2.4.0-win-x64-setup.exe` and the checksum file from the
   same official release.
2. Verify the checksum as described below.
3. Run the installer. The default is a per-user installation below Local
   AppData, with a Start Menu shortcut and uninstall entry.
4. Start OmniSorSe and select only folders you intend to analyse.

The installer and executable are unsigned for v2.4.0 unless the release page
explicitly records otherwise. Windows SmartScreen may warn that the publisher
is unrecognized. Review the source location and checksum before continuing.

## Windows x64 portable

1. Download `OmniSorSe-v2.4.0-win-x64.zip` and the checksum file from the same
   official release.
2. Verify the checksum.
3. Extract the entire ZIP into a writable directory.
4. Keep all extracted files together and start `OmniSorSe.exe`.

The portable package is self-contained and does not require a separate .NET
runtime installation.

## macOS Intel and Apple Silicon

1. Choose `OmniSorSe-v2.4.0-macos-x64.dmg` for Intel or
   `OmniSorSe-v2.4.0-macos-arm64.dmg` for Apple Silicon.
2. Verify the checksum, open the DMG, and copy `OmniSorSe.app` to Applications.
3. The v2.4.0 app is unsigned and unnotarized unless the release page explicitly
   records otherwise. Gatekeeper may require an explicit reviewed override.

The app bundle and native dependencies are built and smoke-tested on matching
native GitHub-hosted macOS runners. This is not a claim that broad interactive
macOS testing is complete. Source-file mutation remains disabled when the
platform capability service cannot prove the required identity/link/filesystem
guarantees.

## Verify SHA-256 checksums

Download `OmniSorSe-v2.4.0-SHA256SUMS.txt` from the same release.

```powershell
(Get-FileHash .\OmniSorSe-v2.4.0-win-x64-setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
```

```bash
shasum -a 256 OmniSorSe-v2.4.0-macos-arm64.dmg
```

Compare the complete value with the named line. A checksum detects changed
bytes; it does not authenticate an unsigned publisher.

## Build and run current source

Prerequisites:

- Windows 10 or later, or a Linux x64 graphical environment for the
  source-build preview;
- the .NET 10 SDK selected by [`global.json`](../global.json);
- Git when cloning the repository.

```powershell
git clone https://github.com/nishdel/OpenSorSe.git
Set-Location .\OpenSorSe
git switch main
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet test .\OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

The project path remains internal/compatible, while the
desktop assembly and native apphost are named `OmniSorSe`.

Use disposable folders when evaluating Change Plan Apply, rollback, recovery,
or Undo. A successful local build is not a signed or published release.

For Linux prerequisites, XDG locations, framework-dependent publishing,
launch, and limitations, follow
[Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md).

## Historical v1.0 portable package

The repository’s v1.0 snapshot describes a self-contained Windows x64 portable
layout. If obtaining that historical release from the official project:

1. Download the ZIP and its `.sha256` file from the same official release.
2. Calculate the archive hash:

   ```powershell
   (Get-FileHash .\OpenSorSe-v1.0.0-win-x64.zip -Algorithm SHA256).Hash
   ```

3. Compare the complete hexadecimal value with the published checksum.
4. Extract the entire archive into a writable directory.
5. Keep all runtime files together and start `OpenSorSe.exe`.

A checksum detects changed bytes; it does not by itself authenticate a
publisher. The historical executable is not code-signed, so Windows
SmartScreen may report it as unrecognized.

## Optional Ollama-compatible AI

OmniSorSe does not bundle, install, or start Ollama.

1. Install and manage a compatible provider separately.
2. Install a model supported by that provider.
3. In Settings, enable AI, verify the exact endpoint/model, and enable only the
   capability required.

AI and individual capabilities are disabled by default. Ordinary scanning,
OCR, Search interpretation/ranking, snippets, explanations, Change Plan
review, and Undo do not require AI.

A custom Ollama-compatible endpoint can be remote. An explicitly requested AI
flow can then send its bounded input away from the machine. Review the endpoint
and privacy warning before enabling a capability.

## Optional OCR

Tesseract is not bundled.

1. Install a compatible Tesseract 5 command-line distribution.
2. Install `eng` and/or `deu` language data.
3. Configure the executable and languages in Settings.

Supported PDF/Open XML native-text extraction is built in. Tesseract is used
only for enabled recognition of supported images, representative video frames,
and scanned PDF pages.

## Optional media tools

OmniSorSe does not bundle `ffprobe` or `ffmpeg`. Install and manage compatible
local executables separately, then configure their absolute paths in Settings
or make them available on `PATH`. `ffprobe` supplies bounded audio/video
metadata; `ffmpeg` supplies bounded representative video frames. Missing or
invalid tools disable only those capabilities and do not disable ordinary
Search.

No concrete visual-description provider ships. OmniSorSe does not send images
or video frames to Ollama for visual analysis.

### Optional local whisper.cpp transcription

OmniSorSe can use a separately installed, user-managed `whisper-cli` executable
and local GGML model. Official packages contain neither file and never download
a model.

1. Obtain a reviewed whisper.cpp build and compatible GGML model from sources
   whose license/provenance you accept.
2. In Settings, enter absolute paths for the executable and model. If audio must
   be extracted from video or another container, also configure local `ffmpeg`.
3. Choose **Check media capabilities**. Available means the runtime capability
   probe succeeded and the local model passed bounded file validation; it is
   not a claim about model accuracy or speed.
4. Enable audio/video transcription only after reviewing duration, transcript,
   timeout, CPU/memory, and privacy limits.

The adapter is CPU-capable when the chosen whisper.cpp build is, serializes
jobs conservatively, retains bounded timestamp segments, and cleans
application-owned temporary audio. Exact disk/RAM/speed requirements depend on
the user's model/build. Python, cloud transcription, and Ollama are not used by
this speech provider. No concrete visual-description provider is included.

## Optional local plugins

OmniSorSe accepts local ZIP packages; it does not search a marketplace,
download plugin dependencies, or update plugins automatically. External
plugins start disabled and require explicit capability grants.

Plugin code runs in-process with the current user’s operating-system
permissions. Install only packages whose source and publisher you trust.
Assembly-load-context isolation and SHA-256 integrity checks are not a sandbox
or publisher authentication. See
[Local Plugin Packages](LOCAL_PLUGIN_PACKAGES_v1.4.md).

## Application data

Application-owned runtime state uses the platform path provider:

- Windows: below `%LOCALAPPDATA%\OpenSorSe`;
- macOS: below `~/Library/Application Support/OpenSorSe`, with caches and logs
  in the corresponding `~/Library/Caches` and `~/Library/Logs` locations;
- Linux: XDG configuration/data/state/cache locations described in
  [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md) and Platform Diagnostics.

Depending on enabled features, state can include settings, logs, saved scans,
saved searches, extracted content, the compatible semantic index, the embedded
SQLite durable Search index and managed backups, AI decisions, structure
history, optional `knowledge-graph.db` derived projection,
`knowledge-decisions.db` graph-native decisions/recovery points, Workflow
Profiles, bounded media metadata/OCR evidence, application-owned thumbnail
cache entries, Sorting Recipes, Watched Folder
configuration/catalogues/activity, plugin state/packages, Change Plans, and the
Operation Journal.

Source files remain in their selected locations. Extracted text, Search/graph
indexes, graph associations/aliases, paths, journals, and diagnostic exports
can still be sensitive; protect application data like the source material and
do not copy it into the repository.

## Update a source build or portable release

1. Close OmniSorSe (or an older compatible OpenSorSe build) so stores, indexing,
   watchers, journals, and plugins shut down. The supported release procedure
   never replaces binaries beneath a running process; the Windows installer
   uses Restart Manager to request closure.
2. Read Version Notes, Release Status, and migration information.
3. Keep the previous program directory and a reviewed backup of important
   application-owned state.
4. Build or extract the new version into a separate program directory.
5. Start it and verify Settings, Search/indexing, optional Knowledge Graph,
   Workflows, Plugins, Watched Folders, Operation History, and saved scans.
6. Retain the prior program/data backup until the update is verified.

Do not overwrite a running installation. Each store/provider owns its schema,
migration, newer-version rejection, corruption, and recovery behavior.

Current v2.12 candidate Windows/macOS packages are self-contained with .NET 10;
users do not install a separate runtime. OmniSorSe has no in-app updater. Obtain
a trusted package, verify its checksum/signature status and embedded source
identity, close the app, replace/install it, then review startup health.

## Uninstall

1. Close OmniSorSe.
2. For the v2.4 Windows installer, use the OmniSorSe uninstall entry. The
   retained AppId upgrades the previous OpenSorSe entry rather than creating a
   second product. For a portable
   or source build, delete only its extracted/build program directory.
3. Optionally remove the exact legacy-named OpenSorSe-owned application-data directories.

Removing application data does not delete scanned source files. It does remove
local indexes, settings, plugins, plans, journal/recovery facts, Undo evidence,
graph-native decisions, and history. Do not remove it while an interrupted
operation needs review.

The Windows uninstaller preserves application data by policy. Removing that
data is a separate explicit user decision.

## Troubleshooting

- **Source does not build:** verify the SDK selected by `global.json`, restore,
  then build from a clean generated-output state.
- **The portable app does not start:** extract the complete archive
  and keep every runtime file beside the executable.
- **Ollama is unavailable:** verify endpoint, service, exact model, global AI,
  and the individual capability.
- **OCR is unavailable:** verify the Tesseract executable and every configured
  language data file.
- **Audio/video metadata is unavailable:** verify the configured `ffprobe`
  executable. For representative video frames, also verify `ffmpeg`.
- **Transcription is unavailable:** verify both the user-managed whisper.cpp
  executable and local model paths plus `ffmpeg` for video/container
  preparation. Ordinary Search remains available.
- **Visual descriptions are unavailable:** no concrete provider ships in
  v2.4.0; metadata/OCR/Search remain available.
- **Search is incomplete:** review Background indexing coverage, exclusions,
  dependencies, failures, quota, pause state, and index availability.
- **Settings do not persist:** verify the current platform’s application-data
  location and permissions.
- **A plugin is blocked:** review compatibility, dependencies, integrity,
  grants, quarantine, native runtime identifier, and restart requirements.

See [OpenSorSe 1.8 Troubleshooting](TROUBLESHOOTING_v1.8.md) and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).
