# Linux Build and Launch

OpenSorSe 1.5 provides a framework-dependent Linux x64 source-build foundation.
No Linux package or installer is produced by this task.

## Prerequisites

- x64 Linux with a graphical desktop and the native libraries required by
  Avalonia 12;
- the .NET SDK selected by `global.json`;
- optional externally managed Tesseract plus language data;
- optional externally running Ollama-compatible HTTP service.

Distribution package names vary. Install prerequisites through the
distribution; OpenSorSe never runs `sudo`.

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
chmod u+x /tmp/opensorse-linux-x64/OpenSorSe
/tmp/opensorse-linux-x64/OpenSorSe
```

A self-contained publish can use `--self-contained true`, but it is only build
output, not a tested installer or supported distribution package. Preserve the
main executable bit when copying. Native Avalonia/PDF renderer compatibility,
fonts, file picker, clipboard, file manager, sandbox/container restrictions,
Wayland/X11 behavior, Tesseract, and filesystem behavior must be checked on the
target distribution.

CI validates source on `ubuntu-latest` and `windows-latest`; it does not publish
artifacts. See the [manual checklist](MANUAL_TESTING_v1.5.md) before making any
broader support statement.
