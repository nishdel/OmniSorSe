# Installing OpenSorSe

**Document type:** Living installation and update guidance

## Availability

The current repository source identifies as an OpenSorSe v2.0 implementation
candidate on `v2.0-knowledge-graph`. That branch is not merged into `main`, and
the repository does not contain a v2.0 installer, package, tag, or published
release. Clean local automated validation is complete; exact-tip hosted, RC,
and interactive validation are separate pending gates.

The only tagged and packaged repository release is the frozen v1.0 Windows x64
portable snapshot. It does not represent current candidate behavior. Check
[Release Status](RELEASE_STATUS.md) before expecting a download.

Do not download an OpenSorSe package from an unrelated site. A future official
archive should identify its source commit/version and include release notes plus
a checksum from the same official release location.

## Build and run current source

Prerequisites:

- Windows 10 or later, or a Linux x64 graphical environment for the
  source-build preview;
- the .NET SDK selected by [`global.json`](../global.json);
- Git when cloning the repository.

```powershell
git clone https://github.com/nishdel/OpenSorSe.git
Set-Location .\OpenSorSe
git switch v2.0-knowledge-graph
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet test .\OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

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

OpenSorSe does not bundle, install, or start Ollama.

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
only for enabled recognition of supported images and scanned PDF pages.

## Optional local plugins

OpenSorSe accepts local ZIP packages; it does not search a marketplace,
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
- Linux: XDG configuration/data/state/cache locations described in
  [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md) and Platform Diagnostics.

Depending on enabled features, state can include settings, logs, saved scans,
saved searches, extracted content, the compatible semantic index, the embedded
SQLite durable Search index and managed backups, AI decisions, structure
history, optional `knowledge-graph.db` derived projection,
`knowledge-decisions.db` graph-native decisions/recovery points, Workflow
Profiles, Sorting Recipes, Watched Folder
configuration/catalogues/activity, plugin state/packages, Change Plans, and the
Operation Journal.

Source files remain in their selected locations. Extracted text, Search/graph
indexes, graph associations/aliases, paths, journals, and diagnostic exports can still be sensitive; protect
application data like the source material and do not copy it into the
repository.

## Update a source build or future portable release

1. Close OpenSorSe so stores, indexing, watchers, and plugins shut down.
2. Read Version Notes, Release Status, and migration information.
3. Keep the previous program directory and a reviewed backup of important
   application-owned state.
4. Build or extract the new version into a separate program directory.
5. Start it and verify Settings, Search/indexing, optional Knowledge Graph,
   Workflows, Plugins, Watched Folders, Operation History, and saved scans.
6. Retain the prior program/data backup until the update is verified.

Do not overwrite a running installation. Each store/provider owns its schema,
migration, newer-version rejection, corruption, and recovery behavior.

## Uninstall

1. Close OpenSorSe.
2. Delete the extracted/build program directory.
3. Optionally remove the exact OpenSorSe-owned application-data directories.

Removing application data does not delete scanned source files. It does remove
local indexes, settings, plugins, plans, journal/recovery facts, Undo evidence,
graph-native decisions, and history. Do not remove it while an interrupted
operation needs review.

## Installer status

No current installer is provided. A future installer/updater requires explicit
identity, signing, reproducibility, migration, rollback, and uninstall policy.

## Troubleshooting

- **Source does not build:** verify the SDK selected by `global.json`, restore,
  then build from a clean generated-output state.
- **The historical portable app does not start:** extract the complete archive
  and keep every runtime file beside the executable.
- **Ollama is unavailable:** verify endpoint, service, exact model, global AI,
  and the individual capability.
- **OCR is unavailable:** verify the Tesseract executable and every configured
  language data file.
- **Search is incomplete:** review Background indexing coverage, exclusions,
  dependencies, failures, quota, pause state, and index availability.
- **Settings do not persist:** verify the current platform’s application-data
  location and permissions.
- **A plugin is blocked:** review compatibility, dependencies, integrity,
  grants, quarantine, native runtime identifier, and restart requirements.

See [OpenSorSe 1.8 Troubleshooting](TROUBLESHOOTING_v1.8.md) and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).
