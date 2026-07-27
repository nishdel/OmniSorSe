# Platform compatibility matrix

This matrix records the audit baseline and verified support level for
OpenSorSe 1.5, **Cross-Platform Foundation and Linux Preview**. A capability is
not described as supported merely because .NET exposes a similarly named API.
The status reflects OpenSorSe's own validation, safety policy, automated tests,
and available manual evidence.

## Status vocabulary

| State | Meaning |
| --- | --- |
| Supported | Implemented and covered by repeatable automated validation on the named platform. |
| Supported with limitations | Implemented and tested, with documented filesystem, desktop, or external-tool limitations. |
| Unverified | Implemented behind an explicit abstraction but not executed on the named platform in this release environment. |
| Unavailable | Disabled because the platform cannot provide the required safety guarantee. |

## Pre-implementation audit

The clean v1.4 baseline was a Windows-first .NET 8/Avalonia application:

- every project targeted portable `net8.0`, but the Desktop project used
  `WinExe`, a Windows application manifest, an ICO application icon, and only
  documented `win-x64` publishing;
- 23 production files selected path behavior with direct
  `OperatingSystem.IsWindows()` checks;
- stable watcher identity called `GetFileInformationByHandle` from
  `kernel32.dll`, with a creation-time/length fallback;
- application configuration, data, execution state, caches, plugins, and logs
  were all rooted below the Windows local-application-data directory;
- Change Plan filename validation always enforced the Windows reserved-name and
  trailing-dot/space rules, even on other platforms;
- path comparison was usually Windows-insensitive and otherwise ordinal, but
  responsibility was distributed between Scanner, Rules, Executor,
  Application, plugins, workflows, and ViewModels;
- link safety used `.NET`'s `FileAttributes.ReparsePoint`. This detects managed
  symbolic-link/reparse entries but did not expose a platform capability or a
  single policy service;
- the watcher backend was `FileSystemWatcher`; reconciliation already treated
  notifications as lossy hints, but inotify limits were not reported;
- safe execution used non-overwriting `File.Move`, journal-before-mutation,
  verification, compensating rollback, recovery, and Undo. Cross-filesystem
  moves and filesystem-specific durability were not modelled explicitly;
- Tesseract used an exact configured path or the bare `tesseract` command. PATH
  discovery and executable-permission diagnostics were implicit in process
  startup failures;
- Ollama already behaved as an external HTTP service and did not require a
  Windows process launcher;
- desktop opening used `ProcessStartInfo` with `UseShellExecute=true`, with no
  explicit capability report;
- package extraction normalized ZIP separators and rejected traversal and
  reparse entries, but manifests had no runtime-identifier/native-dependency
  constraint;
- 35 test files used Windows path strings. Most are pure model fixtures; tests
  that touch the real filesystem already use `Path.GetTempPath`;
- there was no GitHub Actions workflow;
- there was no Registry, `WindowsIdentity`, elevation, `sudo`, shell-command
  construction, `File.Replace`, installer project, or automatic updater in
  production.

## v1.5 support matrix

The table is updated only when the implementation and corresponding validation
exist. “Linux preview” means the core safety model and build/test path are
available; it does not promise identical behavior on every filesystem or
desktop environment.

| Capability | Windows | Linux preview | macOS | Notes |
| --- | --- | --- | --- | --- |
| Core build and deterministic analysis | Supported | Unverified | Unverified | CI is defined for Windows and Ubuntu, but an Ubuntu run was not observable in this implementation environment. |
| Avalonia desktop build/startup composition | Supported | Unverified | Unverified | A framework-dependent linux-x64 ELF output was generated and inspected on Windows; it was not launched in a Linux graphical session. |
| Path normalization and confinement | Supported | Unverified | Unverified | Linux semantics are covered by deterministic tests, but the complete suite has not yet executed on Linux. |
| Portable filename policy | Supported | Unverified | Unverified | Recipes can request current-platform, portable, or Windows-compatible output; simulated Linux semantics are tested. |
| Stable file identity | Supported | Unverified | Unverified | Windows volume/file-index behavior is locally tested. Linux x64 device/inode code awaits an actual Ubuntu run; other Linux architectures use the explicit metadata fallback. |
| Read-only scanning and metadata | Supported | Unverified | Unverified | Links are skipped; permission and mount failures remain visible per item. |
| Duplicate detection | Supported | Unverified | Unverified | Content hashes are platform-neutral, but Linux execution remains unobserved. |
| Change Plan rename/move/create | Supported | Unverified | Unavailable | Linux code permits only operations that pass current-platform capability, permission, link, root, and filesystem checks. macOS remains disabled pending validation. |
| Cross-filesystem move | Unavailable | Unavailable | Unavailable | `File.Move` is not treated as an atomic cross-filesystem transaction. Plans fail closed instead of copying/deleting silently. |
| Durable journal, rollback, recovery, Undo | Supported | Unverified | Unavailable | Compensating operations are verified on Windows; no database-style transaction or universal `fsync` guarantee is claimed. |
| Watched folders | Supported with limitations | Unverified | Unverified | Notifications are hints. Linux inotify handling and limits are implemented/documented but not executed here. |
| Workflows and recipes | Supported | Unverified | Unverified | Import preserves policy; it does not silently rewrite names for another platform. |
| Managed plugins | Supported | Unverified | Unverified | Manifest runtime identifiers are enforced. In-process loading is not sandboxing. |
| Native plugin dependencies | Supported with limitations | Unverified | Unavailable | A plugin must explicitly list a matching runtime identifier. Binary portability is never inferred. |
| OCR through external Tesseract | Supported with limitations | Unverified | Unverified | Configured path and PATH discovery verify an executable before bounded invocation. Linux Tesseract execution was not observed. |
| Ollama-compatible local AI | Supported with limitations | Unverified | Unverified | Uses a configured HTTP endpoint; OpenSorSe does not require or auto-launch a platform executable. |
| Open/reveal with file manager | Supported | Unverified | Unavailable | Uses an exact path through the desktop association API. Linux graphical-session behavior remains manual. |
| Configuration/data/state/cache locations | Supported | Unverified | Unverified | Windows preserves existing local application data. Linux XDG resolution is tested deterministically but not in a Linux process. |
| Automatic update/package installation | Unavailable | Unavailable | Unavailable | No updater, installer, or release package is produced by v1.5. |

## Filesystem limitations

- Device/inode and volume/file-index values identify an object on one mounted
  filesystem at one point in time. They do not survive copies, filesystem
  migration, all snapshot/restore operations, or guaranteed inode reuse.
- Network filesystems, removable media, FUSE providers, bind mounts,
  containers, and copy-on-write filesystems can expose weaker identity,
  locking, timestamp, watcher, free-space, or durability behavior.
- Linux permissions can include ACL, capability, namespace, and mount rules
  that are not completely described by Unix mode bits. OpenSorSe treats an
  actual access failure as authoritative and never attempts elevation,
  ownership changes, or broad permission changes.
- Symbolic links, Windows reparse points, junctions, and mount escapes are not
  followed during scanning, reconciliation, plugin traversal, or Change Plan
  validation.
- Watcher delivery can be duplicated, reordered, coalesced, lost, or stopped.
  Startup, resume, reconnect, overflow, daily, and manual reconciliation remain
  the source of truth.

## Validation evidence

The final v1.5 validation section in `docs/RELEASE_STATUS.md` records exact
Windows test totals, Linux-compatible CI coverage, Linux publish/build checks,
formatting, documentation tests, and any manual checks that were actually
performed. Until that evidence exists, this matrix must be read conservatively
and no stronger platform claim may be made.
