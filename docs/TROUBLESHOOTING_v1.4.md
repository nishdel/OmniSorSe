# OpenSorSe 1.4 Troubleshooting

## A plugin is visible but unavailable

Inspect its state in **Settings > Plugins**. Common causes are disabled state,
host/runtime incompatibility, a missing or wrong-version dependency,
contribution conflict, integrity change, repeated-failure quarantine, or a
pending restart. Resolve the displayed diagnostic; do not edit plugin state
JSON by hand.

## “Plugin capability unavailable — review workflow profile”

The profile or recipe requires an exact plugin/version/contribution that is not
active. Enable the expected version with accepted capabilities, repair its
dependency or integrity problem, or edit the profile/recipe to use an available
contribution. OpenSorSe intentionally does not choose a fallback.

## Installation is rejected

The package must be a bounded local ZIP containing exactly one root
`plugin.json`, a managed entry assembly at the declared relative path, and no
unsafe/archive-traversal content. Native libraries require the declared
`UseNativeLibraries` capability. Review the manifest reference and ask the
publisher for a corrected package.

## Integrity changed

OpenSorSe hashes controlled installed content. Any unexpected change after
acceptance locks the plugin out. Disable it, preserve diagnostics, and reinstall
a verified package from a trusted publisher. A matching hash detects content
change; it does not prove who published the package.

## Plugin is quarantined

Three repeated startup failures trigger quarantine. Export diagnostics, disable
the plugin, correct or upgrade it, and restart if requested. Do not repeatedly
re-enable an unknown or crashing binary.

## Disable or upgrade says restart required

External plugins run in collectible .NET assembly load contexts, but references
held by plugin code or runtime internals can delay unloading. Close OpenSorSe,
restart it, and inspect the resulting state. This is an in-process limitation.

## Removal is blocked

Open **Workflows** and **Watched Folders** and remove or replace references to
the exact plugin version. Historical snapshots and operation history are
preserved and do not reactivate a plugin.

## OpenSorSe fails during startup

Plugin failures should be contained and diagnosed while the application starts
without that contribution. If startup remains blocked, preserve the plugin
root and diagnostic export, then move only the suspect plugin version out of
the controlled root while OpenSorSe is closed. Do not delete workflow, Change
Plan, journal, or history stores.

## Security concern

Disable the plugin, disconnect sensitive resources if appropriate, preserve
the local ZIP/hash/diagnostics, and restart. Plugins are in-process code running
as the current user; v1.4 does not claim a sandbox, signature authority, or
publisher authentication.
