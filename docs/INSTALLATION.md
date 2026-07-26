# Installing OpenSorSe 1.0 on Windows

## Supported package

The initial public distribution is a portable, self-contained Windows x64 package:

`OpenSorSe-v1.0.0-win-x64.zip`

It includes the native Windows apphost, the .NET runtime required by OpenSorSe, application libraries, licensing material, release notes, and this installation guide.

## Install

1. Download the ZIP and its `.sha256` checksum from the same GitHub release.
2. Verify the checksum:

   ```powershell
   (Get-FileHash .\OpenSorSe-v1.0.0-win-x64.zip -Algorithm SHA256).Hash
   ```

3. Extract the complete archive. Do not run the executable from inside the compressed-folder preview.
4. Open the extracted `OpenSorSe-v1.0.0` directory.
5. Run `OpenSorSe.exe`.

The package is self-contained. A separate .NET runtime installation is not required.

## Windows security prompt

The initial portable build is not code-signed. Windows SmartScreen may identify it as an unrecognized application. Confirm that the archive came from the official repository and that its SHA-256 checksum matches the release before selecting **More info** and **Run anyway**.

## Optional Ollama support

Ollama is not bundled or started automatically.

1. Install Ollama separately from its official distribution.
2. Install a model supported by your local Ollama version.
3. In OpenSorSe Settings, enable AI, select an installed model, and enable only the capabilities you want.

AI remains disabled by default. Enabling it alone does not send a request. A custom remote endpoint changes the privacy boundary and should be used only when explicitly intended.

## Optional OCR support

Tesseract is not bundled.

1. Install a compatible Tesseract 5 command-line distribution.
2. Install the required `eng` and/or `deu` language data.
3. Configure or verify the executable and language selection in OpenSorSe Settings.

PDF rendering and supported native-text extraction are built in. Tesseract is required only for OCR recognition of images and scanned PDF pages.

## Application data

OpenSorSe-owned settings, diagnostics, saved scans, content cache, semantic index, AI decisions, and structure history are stored below the current user’s local application-data directory:

`%LOCALAPPDATA%\OpenSorSe`

Scanned source files remain in their selected locations.

## Update

1. Close OpenSorSe.
2. Extract the new portable release into a new program directory.
3. Start the new `OpenSorSe.exe`.

Do not copy a new release over a running installation. Existing user settings remain in local application data and are migrated through backward-compatible defaults.

## Uninstall

1. Close OpenSorSe.
2. Delete the extracted program directory.
3. Optionally delete `%LOCALAPPDATA%\OpenSorSe` to remove OpenSorSe-owned settings, indexes, saved scans, history, and logs.

Deleting OpenSorSe application data does not delete or modify scanned source files.

## Installer status

No installer is included in the initial candidate. MSIX requires package identity and signing decisions, while Inno Setup and NSIS are not part of the repository toolchain. The portable self-contained package is therefore the reproducible release artifact for v1.0.

## Troubleshooting

- **The application does not start:** extract the entire ZIP and keep every runtime file beside `OpenSorSe.exe`.
- **Ollama is unavailable:** confirm Ollama is running, retry the connection, and verify the exact configured model exists.
- **OCR is unavailable:** verify the Tesseract executable and every configured language data file.
- **Meaning Search is unavailable:** enable it separately in Settings and build or rebuild its local index.
- **Settings do not persist:** confirm the current account can write to `%LOCALAPPDATA%\OpenSorSe`.
