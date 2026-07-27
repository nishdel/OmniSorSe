# Watched Folders on Linux in v1.5

`FileSystemWatcher` uses the .NET inotify backend on Linux. Notifications are
untrusted hints: they can be duplicated, reordered, coalesced, lost, or dropped
on queue overflow. Recursive trees consume watch descriptors; mounts can
disconnect; editors often save by temporary-file replacement.

OpenSorSe retains bounded queues, debounce, stability checks, startup/manual/
periodic reconciliation, overflow visibility, unavailable-root status, and
catalogue comparison. Reconciliation reads actual root-confined state and skips
symbolic links; it cannot authorize file mutation. OpenSorSe does not change
inotify limits, elevate, or run a background service after the desktop exits.
