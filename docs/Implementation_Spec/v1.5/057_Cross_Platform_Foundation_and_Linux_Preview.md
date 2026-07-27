# Specification 057 — Cross-Platform Foundation and Linux Preview

- Version: `v1.5`
- Release: **Cross-Platform Foundation and Linux Preview**
- Branch: `v1.5-cross-platform-foundation`

## Objective

Make platform-sensitive behavior explicit, injectable, diagnosable, and
fail-closed while retaining OpenSorSe's reviewed Change Plan boundary.

## Implemented contracts

`IPathSemantics`, `IApplicationPathProvider`, `IFileIdentityProvider`,
`IFileSystemCapabilities`, `IExternalToolLocator`,
`IPlatformCapabilityProvider`, and Desktop `IDesktopIntegration` divide path,
storage, identity, permissions/mounts, tools, support reporting, and graphical
opening. Business services consume those contracts; OS checks remain in the
platform implementation.

Windows retains local-data and volume/file-index behavior. Linux uses XDG paths,
case-sensitive semantics, x64 device/inode identity, advisory mode-bit checks,
inotify-backed watching with reconciliation, RID-gated plugins, and graphical
desktop/tool adapters. Other Linux architectures use the explicitly weaker
metadata identity fallback. macOS is unverified.

## Mutation invariants

Links and root escapes fail closed. No occupied destination is overwritten.
Moves require a verified same filesystem. Permission checks never elevate.
Execution remains confirmation-gated, journalled, cancel-safe, verified, and
compensated by rollback/Undo where current state still matches. These are
filesystem-aware guarantees, not database transactions or universal fsync
claims.

## Verification

Focused platform tests cover filename modes, case semantics, confinement, XDG
and Windows paths, identity preservation, tool discovery, plugin RID policy,
desktop delegation, diagnostics export, and inherited execution/watcher/plugin
tests. The CI matrix builds and runs the complete suite on Windows and Ubuntu.
Exact observed results belong in [Release Status](../../RELEASE_STATUS.md).

## Non-goals

No installer/updater/package, privileged service, arbitrary shell execution,
ownership/permission rewriting, cloud provider, plugin sandbox, mobile target,
network-drive synchronization, or macOS support claim is introduced.
