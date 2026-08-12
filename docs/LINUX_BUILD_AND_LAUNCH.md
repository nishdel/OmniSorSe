# Linux Build and Launch

OmniSorSe provides a framework-dependent Linux x64 source-build preview. The
v2.4 source selects Linux native SQLite/Skia assets when
cross-targeted and compiles the Unix-domain-backed named-pipe Explorer Protocol
transport, but native Linux protocol/UI execution is not recorded. No Linux
package or installer is published by this repository.

Read the living [Platform Compatibility Matrix](PLATFORM_COMPATIBILITY_MATRIX.md)
before making a broader support claim.

## Prerequisites

- x64 Linux with a graphical desktop and the native libraries required by
  Avalonia 12;
- the .NET SDK selected by `global.json`;
- optional externally managed Tesseract plus language data;
- optional externally running Ollama-compatible HTTP service.

Distribution package names vary. Install prerequisites through the
distribution; OmniSorSe never runs `sudo`.

## Validate source

From the repository root:

```bash
dotnet restore OpenSorSe.sln
dotnet build OpenSorSe.sln --configuration Debug --no-restore
dotnet test OpenSorSe.sln --configuration Debug --no-build --no-restore
dotnet build OpenSorSe.sln --configuration Release --no-restore
dotnet test OpenSorSe.sln --configuration Release --no-build --no-restore
dotnet format OpenSorSe.sln --verify-no-changes --no-restore
```

## Run

```bash
dotnet run --project src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj \
  --configuration Debug --no-restore
```

## Framework-dependent Linux x64 publish

Use an output directory outside the tracked source tree:

```bash
dotnet publish src/OpenSorSe.Desktop/OpenSorSe.Desktop.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  --output /tmp/opensorse-linux-x64
chmod u+x /tmp/opensorse-linux-x64/OmniSorSe
/tmp/opensorse-linux-x64/OmniSorSe
```

A self-contained publish can use `--self-contained true`, but it is only build
output, not a tested installer or supported distribution package. Preserve the
main executable bit when copying. Native Avalonia/PDF renderer compatibility,
SQLite native loading, fonts, file picker, clipboard, file manager,
sandbox/container restrictions, Wayland/X11 behavior, Tesseract, and filesystem
behavior must be checked on the target distribution.

Repository CI is configured for `windows-latest`, `ubuntu-latest`, and
`macos-latest`; it does not publish artifacts. The immutable v1.6 report proves
a successful three-host run, while the current v2.4 implementation task records
Windows execution and cross-target compilation only. Linux continues using the
legacy-compatible `opensorse` XDG subdirectories so the product rename cannot
orphan a prior profile. Use the current
[v2.4 manual checklist](MANUAL_TESTING_v2.4.md) before making a broader support
statement.
