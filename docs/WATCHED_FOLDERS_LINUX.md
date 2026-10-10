# Watched Folders on Linux in

**Version scope:** Introduced in v1.5. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/WATCHED_FOLDERS_LINUX_v1.5.md).

`FileSystemWatcher` uses the .NET inotify backend on Linux. Notifications are
untrusted hints: they can be duplicated, reordered, coalesced, lost, or dropped
on queue overflow. Recursive trees consume watch descriptors; mounts can
disconnect; editors often save by temporary-file replacement.

OpenSorSe retains bounded queues, debounce, stability checks, startup/manual/
periodic reconciliation, overflow visibility, unavailable-root status, and
catalogue comparison. Reconciliation reads actual root-confined state and skips
symbolic links; it cannot authorize file mutation. OpenSorSe does not change
inotify limits, elevate, or run a background service after the desktop exits.
