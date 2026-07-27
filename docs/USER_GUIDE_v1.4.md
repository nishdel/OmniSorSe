# OpenSorSe 1.4 User Guide

This guide covers the v1.4 Plugin Foundation and Extension SDK. Existing scan,
catalogue, watched-folder, workflow, Change Plan, review, Apply, Operation
History, recovery, and Undo behavior remains unchanged.

## Inspect plugins

Open **Settings > Plugins**. Each row identifies the plugin, version, publisher,
license, origin, lifecycle state, compatibility, integrity, dependencies,
contributions, requested capabilities, granted capabilities, errors,
quarantine, and whether restart is required.

Built-in reference plugins are active by default. External plugins are disabled
after discovery or installation until you review them and explicitly enable
their requested capabilities.

## Install a local package

1. Obtain a local `.zip` package from a publisher you trust.
2. In **Settings > Plugins**, enter the package path and choose **Install**.
3. Review the discovered publisher, source, version, requested capabilities,
   integrity state, dependencies, and contributions.
4. Choose **Enable and grant requested capabilities** only if you accept them.

Installation is local only. OpenSorSe does not download packages, contact a
marketplace, or automatically update plugins. Packages with invalid manifests,
unsafe archive paths, unexpected native binaries, missing entry assemblies, or
conflicting versions are rejected without replacing the active installation.

## Upgrade, disable, and remove

- **Upgrade** validates and stages the new local ZIP before switching versions;
  the previous version is preserved for rollback.
- **Disable** stops contribution use. A restart may be required to release an
  in-process assembly.
- **Remove** requires confirmation and is blocked while a workflow profile,
  recipe, or watched configuration depends on that plugin version.

OpenSorSe removes only its controlled installed-version directory. It does not
remove unrelated files, user data, workflow history, or operation history.

## Workflows and recipes

Profiles and recipes reference a plugin, exact version, and contribution ID.
Resolution snapshots preserve that identity in history. If a required plugin
is disabled, missing, incompatible, changed, quarantined, or the wrong version,
the workflow fails closed with:

> Plugin capability unavailable — review workflow profile

There is no silent fallback to a similarly named contribution. Watched folders
also stop that workflow instead of applying a different recipe.

Plugin recipe fields use the form `plugin.<plugin-id>.<field-id>`. Values remain
subject to the normal template sanitization, root confinement, collision,
preview, and Change Plan validation rules. Plugin outputs do not authorize a
file operation.

## Diagnostics and privacy

Use **Export diagnostics** to save bounded, redacted plugin lifecycle and
resolution events. Diagnostics omit file contents, credentials, and AI prompt
payloads. Review the file before sharing it.

A capability grant describes intended host-mediated access; it is not an OS
sandbox. An in-process plugin runs with the permissions of OpenSorSe. Install
only trusted code, grant the smallest needed set of capabilities, and disable a
plugin whose installed hash changes unexpectedly.

For problems, see [Troubleshooting](TROUBLESHOOTING_v1.4.md).
