# OpenSorSe 1.4 Manual Testing

Use disposable folders and disposable plugin packages. Do not use production
data. Record the application version, OS, package hashes, and results.

## Release gate

- [ ] About shows `1.4`; executable metadata reports `1.4.0.0`.
- [ ] A clean start with no plugin directory succeeds.
- [ ] Settings > Plugins lists all four built-in reference plugins.
- [ ] Plugin diagnostics can be exported and contain no file content,
      credential, token, or AI prompt payload.

## Local package lifecycle

- [ ] A valid local ZIP installs but remains disabled.
- [ ] The UI accurately shows publisher, license, source, requested
      capabilities, dependencies, contributions, compatibility, and integrity.
- [ ] Enabling requires an explicit capability grant.
- [ ] Disabling removes contributions and accurately reports restart state.
- [ ] A compatible upgrade is fully validated before activation and leaves the
      old version available for rollback.
- [ ] A corrupt/incompatible upgrade leaves the previous version usable.
- [ ] Removal requires confirmation and deletes only the controlled version
      directory.
- [ ] Removal is blocked when a profile, recipe, or watched folder depends on
      the plugin.

## Adversarial packages

- [ ] Reject missing, duplicate, oversized, deeply nested, malformed, or
      unknown-field `plugin.json` files.
- [ ] Reject path traversal, rooted paths, alternate separators, duplicate ZIP
      entries, link/reparse content, excessive entry count/size, missing entry
      assembly, and undeclared native binaries.
- [ ] Reject invalid IDs, versions, entry paths/types, dependency ranges,
      contribution conflicts, cycles, missing dependencies, and incompatible
      runtimes.
- [ ] Changing an installed file after acceptance disables the plugin and
      produces an integrity diagnostic.

## Runtime containment

- [ ] Initialization success, exception, timeout, and cancellation are reported
      without crashing startup.
- [ ] A plugin failing startup three times becomes quarantined.
- [ ] Duplicate contribution registration is rejected without replacing the
      first registration.
- [ ] Cancellation and invalid/oversized output at every extension point fails
      closed and does not leave partial state.

## Workflow and Change Plan safety

- [ ] A matching enabled plugin contribution resolves at its exact version and
      appears in the immutable workflow snapshot.
- [ ] Missing, disabled, quarantined, changed, incompatible, and wrong-version
      references show “Plugin capability unavailable — review workflow profile.”
- [ ] Watched-folder processing does not silently fall back.
- [ ] Recipe fields preserve plugin/version/contribution/value/reason/evidence
      provenance on all generated actions, including inferred directories.
- [ ] Plugin field text cannot escape the approved root, create reserved names,
      execute syntax, overwrite a destination, or bypass collision handling.
- [ ] Generated actions remain Pending until normal review and explicit Apply.
- [ ] Apply, journal verification, recovery, rollback, history, and Undo still
      use the existing v1.1 execution boundary.

## Inherited regression

- [ ] Complete the [v1.1](MANUAL_TESTING_v1.1.md),
      [v1.2](MANUAL_TESTING_v1.2.md), and
      [v1.3](MANUAL_TESTING_v1.3.md) checklists.
- [ ] Verify optional OCR/Ollama flows with plugins disabled and with one
      unrelated plugin enabled.
- [ ] Verify startup, shutdown, and restart after enable, disable, upgrade,
      quarantine, integrity change, and removal.

Do not publish a v1.4 binary until this checklist and the automated release
checks pass.
