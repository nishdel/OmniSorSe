# Platform Architecture

**Version scope:** Introduced in v1.5. This is the canonical subject document;
version-specific statements below retain that scope. Consult [Current State](../../CURRENT-STATE.md)
and source/tests for subsequent authority, schema and runtime changes.
Exact prior text: [Source at consolidation baseline](https://github.com/nishdel/OmniSorSe/blob/783bd6f4b914bb0d141d26ebff8e66a4e4aa6a3f/docs/Architecture/00_System/08_v1.5_Platform_Architecture.md).

The platform layer is owned by `OpenSorSe.Core.Platform`. It contains small
contracts and explicit Windows, Linux, conservative, and best-effort
implementations. Application/executor/scanner services depend on semantics and
capabilities, while Desktop owns graphical integration.

```mermaid
flowchart LR
    Business["Scanning, workflows, plugins, executor"] --> Contracts["Small platform contracts"]
    Contracts --> Win["Windows paths, volume/file index"]
    Contracts --> Linux["Linux XDG, x64 device/inode, mode checks"]
    Contracts --> Conservative["Unverified platform / metadata fallback"]
    Desktop["Avalonia Desktop"] --> DesktopContract["IDesktopIntegration"]
    DesktopContract --> WinDesktop["Windows association adapter"]
    DesktopContract --> LinuxDesktop["Linux graphical-session adapter"]
    Tools["OCR integration"] --> Locator["IExternalToolLocator"]
    Locator --> Configured["Absolute configured path"]
    Locator --> PathSearch["Safe PATH search"]
    Capability["Capability provider"] --> Contracts
    Capability --> Desktop
    Capability --> Tools
```

Path normalization is lexical and never treats canonicalization as permission
to follow a link. Link inspection, root confinement, native identity,
same-filesystem checks, permission diagnostics, collision checks, immediate
preflight, and result verification form separate layers. Failure to establish a
safe identity, mount, link, permission, desktop, native plugin, or executable
state produces an unavailable/limited result rather than a hidden fallback.

Application locations preserve Windows compatibility and separate Linux
configuration/data/state/cache. Packaging remains outside runtime composition;
the Windows manifest/icon are build-conditioned and Linux framework-dependent
publish is documented but not packaged.

The full [system map](../OpenSorSe_System_Map.md) keeps the analysis-to-approval-
to-mutation boundary visible. See the
[platform matrix](../../PLATFORM_COMPATIBILITY_MATRIX.md).
