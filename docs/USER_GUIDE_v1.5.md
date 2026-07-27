# OpenSorSe 1.5 User Guide

OpenSorSe 1.5 is a local-first desktop application for inspecting selected
folders and applying only explicitly reviewed Change Plans. Linux support is a
preview, not a claim of uniform behavior on every filesystem or desktop.

## Start and storage

Build and launch from source using the [Linux build guide](LINUX_BUILD_AND_LAUNCH.md)
or existing Windows instructions in [Installation](INSTALLATION.md). Windows
continues to use `%LocalAppData%\OpenSorSe`. Linux uses:

- `${XDG_CONFIG_HOME:-~/.config}/opensorse` for settings;
- `${XDG_DATA_HOME:-~/.local/share}/opensorse` for durable data and plugins;
- `${XDG_STATE_HOME:-~/.local/state}/opensorse` for journals and logs;
- `${XDG_CACHE_HOME:-~/.cache}/opensorse` for reproducible caches.

OpenSorSe creates only its owned directories and never migrates or deletes an
existing Windows data directory silently.

## Platform diagnostics

Settings contains **Platform diagnostics**. It lists OS/runtime/architecture,
owned locations, OCR, identity, watcher, plugin, execution, desktop, and
packaging states. **Copy platform report** creates a human-readable bug report
without credentials or document contents. Limited and unavailable states
include their reason.

## Safe file operations

Scanning, watchers, workflows, plugins, OCR, and AI may analyze or propose.
They cannot approve or apply changes. Rename, move, and create-directory actions
still require a reviewed Change Plan, validation, separate confirmation,
journal write, execution, and resulting-state verification. Destinations are
never silently overwritten. Moves are limited to a verified same filesystem;
links and unverified mount boundaries fail closed.

## Workflows

Recipes carry one filename policy:

- `Portable` (default) uses conservative Windows/Linux interchange rules.
- `WindowsCompatible` retains Windows filename rules on every host.
- `CurrentPlatform` permits names valid on the active host, so a Linux export
  may later be invalid on Windows.

Import preserves the declared policy and does not silently rewrite it. Preview
the recipe after importing on another platform.

## Plugins and external tools

Managed plugins remain in-process and are not a security sandbox. A plugin with
native dependencies must declare supported runtime identifiers such as
`win-x64` or `linux-x64`; a mismatch prevents loading. Tesseract remains an
external installation. Configure an absolute path or leave it blank for safe
`PATH` discovery. OpenSorSe never invokes a shell command string or installs
language data.

## Watchers and links

Watcher events are hints. Debounce, stability checks, bounded queues, startup
and periodic reconciliation remain authoritative. Linux inotify descriptor
limits, overflow, mount loss, and editor replacement patterns can cause delay
or a visible reconciliation warning. Symbolic links/reparse points are not
traversed outside the approved root and are not used to mutate their targets.

See [Troubleshooting](TROUBLESHOOTING_v1.5.md) and the
[platform matrix](PLATFORM_COMPATIBILITY_MATRIX.md).
