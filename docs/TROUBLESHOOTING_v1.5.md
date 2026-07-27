# OpenSorSe 1.5 Troubleshooting

## A platform feature is limited or disabled

Open Settings → Platform diagnostics and read the capability explanation. Copy
the report when filing an issue. An unavailable state is deliberate when
identity, permission, mount, link, desktop, or external-tool safety cannot be
established.

## Linux application data is not where expected

OpenSorSe accepts only absolute `XDG_CONFIG_HOME`, `XDG_DATA_HOME`,
`XDG_STATE_HOME`, and `XDG_CACHE_HOME` values. A missing or relative value uses
the standard home-directory fallback documented in the
[user guide](USER_GUIDE_v1.5.md). It does not merge or delete another location.

## A move or Undo is rejected

The destination may be occupied, unwritable, linked, on another filesystem, or
the source identity/metadata may have changed. Restore the mount or permissions,
remove the external collision yourself if appropriate, then revalidate. OpenSorSe
does not overwrite, elevate, change ownership, or broadly change permissions.

## A watched folder missed activity

Linux inotify and Windows watcher delivery can overflow, duplicate, coalesce, or
lose events. Use manual reconciliation and inspect grouped watcher activity.
Large Linux trees may require an administrator-managed inotify limit change;
OpenSorSe never runs `sudo` or changes system settings.

## Tesseract is unavailable

Install Tesseract and the requested language data through the operating system,
then use **Check Text Recognition**. A configured path must be absolute, exist,
resolve safely, and be executable. Blank configuration searches `PATH` without
a shell. Review the [Linux guide](LINUX_BUILD_AND_LAUNCH.md).

## Folder opening does nothing

Linux requires a graphical session and configured default opener. The desktop
adapter failure is non-fatal; copy the exact application-owned location from
Platform diagnostics and open it manually.

## A plugin is incompatible

Check `runtimeCompatibility`, OpenSorSe version range, and
`supportedRuntimeIdentifiers`. Native dependencies require at least one exact
RID matching the host. Managed `AssemblyLoadContext` isolation is not a sandbox.

For safety guarantees see [Safety and Privacy](SAFETY_AND_PRIVACY.md).
