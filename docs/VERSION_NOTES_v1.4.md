# OpenSorSe 1.4 Version Notes

## Plugin Foundation and Extension SDK

OpenSorSe 1.4 adds a local-first plugin foundation on branch
`v1.4-plugin-foundation`. It preserves the existing preview, approval, Change
Plan, Operation Journal, recovery, and Undo boundaries.

### Highlights

- A stable `OpenSorSe.Extensions.Abstractions` SDK with immutable, asynchronous,
  cancellation-aware contracts.
- Extension points for metadata, content extraction, classification, recipe
  fields, duplicate evidence, workflow capabilities, configuration import
  proposals, and report export.
- Strict `plugin.json` parsing, controlled discovery, deterministic dependency
  resolution, compatibility checks, integrity-change lockout, and
  conflict-safe contribution registration.
- One collectible assembly load context per external plugin, bounded lifecycle
  calls, exception containment, diagnostics, repeated-failure quarantine, and
  explicit enable/capability grant.
- Local ZIP install, upgrade with rollback preservation, and dependency-aware
  removal. v1.4 has no online marketplace, remote download, or automatic
  update channel.
- Plugin references and exact resolved versions in workflow/recipe snapshots,
  recipe-field provenance in Change Plans, and fail-closed watched/manual
  workflow resolution.
- Four built-in reference plugins: filesystem metadata, extension
  classification, a recipe field, and JSON report export.
- A Plugins panel under Settings for inspection, enable/disable, package
  operations, quarantine state, and redacted diagnostics export.

### Safety boundary

Plugins analyze data or return suggestions and bounded outputs. They receive no
Change Plan execution service, dependency-injection container, settings store,
credential store, or unrestricted mutation API. A plugin cannot approve or
apply file operations. All supported mutations remain host-created proposals
that pass normal validation, review, explicit confirmation, execution,
journaling, recovery, and Undo.

External plugins execute in the OpenSorSe process with the effective operating
system permissions of that process. Assembly-load-context isolation is not a
security sandbox. SHA-256 integrity detects changed installed content but does
not authenticate a publisher or establish trust. Install only plugins whose
publisher and code you trust.

### Compatibility and limitations

- Host version: `1.4.0`; assembly/file/manifest version: `1.4.0.0`.
- Plugins must target the v1.4 abstractions and declare compatible host/runtime
  versions.
- External plugins start disabled and require an explicit capability grant.
- Disabling or upgrading a plugin can require an application restart because
  .NET cannot guarantee immediate in-process unload.
- There is no out-of-process worker, marketplace, signing authority, plugin
  script engine, UI injection API, background service, or supported direct file
  mutation extension point in v1.4.

See the [user guide](USER_GUIDE_v1.4.md), [plugin architecture](Architecture/10_Plugins/06_v1.4_Plugin_Foundation.md),
[SDK guide](EXTENSION_SDK_v1.4.md), and [manual checklist](MANUAL_TESTING_v1.4.md).
