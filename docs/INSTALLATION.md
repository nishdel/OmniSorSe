# Installing OpenSorSe

## Current release status

OpenSorSe 1.4 is implemented and automatically validated in source, but this
repository does not claim that a v1.4 binary package has been published. The
previously produced portable package was v1.0 for Windows x64. Check the
[Release Status](RELEASE_STATUS.md) and the repository's Releases page before
expecting a newer download.

Do not download an `OpenSorSe` package from an unrelated site. A release archive
should be accompanied by version notes and a SHA-256 checksum in the same
official release.

## Build and run v1.4 from source

Prerequisites:

- Windows 10 or later;
- the .NET SDK selected by [`global.json`](../global.json);
- Git if cloning the repository.

```powershell
git clone https://github.com/nishdel/OpenSorSe.git
Set-Location .\OpenSorSe
dotnet restore .\OpenSorSe.sln
dotnet build .\OpenSorSe.sln --configuration Debug --no-restore
dotnet run --project .\src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj
```

Use disposable folders when evaluating Change Plan Apply, recovery, or Undo.

## Install an official portable release

When a release is available:

1. Download its Windows x64 ZIP and `.sha256` file from the same official
   GitHub release.
2. Verify the checksum. Replace the example name with the downloaded archive:

   ```powershell
   (Get-FileHash .\OpenSorSe-v1.0.0-win-x64.zip -Algorithm SHA256).Hash
   ```

3. Compare the complete hexadecimal result with the published checksum.
4. Extract the complete archive into a writable directory. Do not run it from
   the compressed-folder preview.
5. Keep all runtime files together and start `OpenSorSe.exe`.

The portable package is self-contained; it does not require a separate .NET
runtime installation.

## Windows security prompt

The existing portable build is not code-signed. Windows SmartScreen may call it
unrecognized. Confirm the archive origin and checksum before selecting
**More info** and **Run anyway**. A checksum detects changed bytes but does not
by itself authenticate a publisher.

## Optional Ollama support

Ollama is not bundled or started automatically.

1. Install and manage Ollama separately.
2. Install a model supported by that Ollama version.
3. In OpenSorSe Settings, enable AI, discover/select the exact model, and
   enable only the capabilities required.

AI is disabled by default. A custom Ollama-compatible endpoint can be remote;
explicitly requested metadata or separately enabled bounded document text can
then leave the machine. Review the endpoint and privacy warning first.

## Optional OCR support

Tesseract is not bundled.

1. Install a compatible Tesseract 5 command-line distribution.
2. Install `eng` and/or `deu` language data.
3. Configure the executable and languages in Settings.

Supported PDF/Open XML native-text extraction is built in. Tesseract is used
only for enabled OCR recognition of images and scanned PDF pages.

## Optional local plugins

v1.4 accepts local ZIP packages only. OpenSorSe does not search a marketplace,
download dependencies, or update plugins automatically. External plugins start
disabled and require explicit capability grants.

Plugin code runs in-process with the current user's operating-system
permissions. Install only packages whose source and publisher you trust.
Assembly-load-context isolation and SHA-256 integrity checks are not a sandbox
or publisher authentication. See [Local Plugin Packages](LOCAL_PLUGIN_PACKAGES_v1.4.md).

## Application data

OpenSorSe-owned runtime state is stored below:

`%LOCALAPPDATA%\OpenSorSe`

It can include settings, logs, saved scans, searches, extracted content,
semantic indexes, AI decision history, structure history, Workflow Profiles,
Sorting Recipes, Watched Folder configuration/catalogues/activity, plugin
state/installed versions, Change Plans, and the Operation Journal. Scanned
source files remain in their selected locations.

Protect this directory like other user data: extracted content and diagnostic
exports can contain sensitive local information. Do not copy it into the source
repository.

## Update

1. Close OpenSorSe so stores and plugin lifecycles shut down cleanly.
2. Extract the new release into a new program directory.
3. Review its Version Notes and migration information.
4. Start the new executable and verify Settings, Workflows, Plugins, Watched
   Folders, Operation History, and saved scans.
5. Keep the previous program directory until the update is verified.

Do not overwrite a running installation. User-local data remains under
`%LOCALAPPDATA%\OpenSorSe` and is handled by each store's schema rules.

## Uninstall

1. Close OpenSorSe.
2. Delete the extracted program directory.
3. Optionally delete `%LOCALAPPDATA%\OpenSorSe` to remove OpenSorSe-owned
   settings, indexes, plugins, plans, journals, history, and logs.

Deleting OpenSorSe application data does not delete scanned source files, but
it removes local recovery/Undo records and should not be done while an operation
needs review.

## Installer status

No installer is currently provided. The portable distribution avoids adding an
unsigned installer/signing surface. A future installer requires explicit
identity, signing, update, migration, and uninstall policy.

## Troubleshooting

- **The application does not start:** extract the whole archive and keep every
  runtime file beside `OpenSorSe.exe`.
- **Ollama is unavailable:** verify the endpoint, running service, selected
  model, and enabled capability.
- **OCR is unavailable:** verify the Tesseract executable and every configured
  language data file.
- **Meaning Search is unavailable:** enable it separately and build/rebuild the
  local index.
- **Settings do not persist:** verify write access to
  `%LOCALAPPDATA%\OpenSorSe`.
- **A plugin is blocked:** review compatibility, dependencies, integrity,
  requested grants, quarantine diagnostics, and restart requirements.

See the current [Troubleshooting Guide](TROUBLESHOOTING_v1.4.md).
