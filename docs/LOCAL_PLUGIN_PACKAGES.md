# Local Plugin Packages

**Version scope:** Introduced in v1.4. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/LOCAL_PLUGIN_PACKAGES_v1.4.md).

v1.4 accepts local ZIP files only. It has no marketplace, online search,
package download, automatic update, dependency download, or package script
execution.

## Package layout

The ZIP contains exactly one `plugin.json` at its root, the declared managed
entry assembly, and only required dependencies/resources at normalized relative
paths. Limits are 128 MiB/1,024 entries for a package and 256 MiB/4,096 files
after installation.

## Installation transaction

OpenSorSe validates archive paths and bounds, parses the manifest, verifies the
entry assembly is managed, checks native declarations and optional integrity,
extracts to a staging directory inside the controlled plugin root, reparses the
staged manifest, and finally moves the complete staged directory into its exact
plugin/version location. It never overwrites an existing version.

The installed plugin remains disabled until explicit enable and capability
grant. Discovery does not execute an entry assembly.

## Upgrade and rollback

Upgrade uses the same complete validation and staging flow. The previous
version remains installed and is restored as the selected version if activation
of the new package fails. Workflows reference exact versions; they do not
silently drift to another installed version.

## Removal

Removal requires confirmation. It is blocked by active profile, recipe, watched
folder, or import/export dependencies. Only the resolved controlled directory
for that plugin/version is removed; unrelated files, package sources, user
configuration, immutable snapshots, Change Plans, journal records, diagnostics,
and operation history remain.

## Trust

SHA-256 checks detect content changes and optional manifest mismatch. They do
not authenticate a publisher, validate a certificate, review code, or sandbox
execution. Verify package origin out of band and install only trusted binaries.
