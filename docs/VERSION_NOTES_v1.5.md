# OpenSorSe 1.5 Version Notes

Version: `v1.5`<br>
Release name: **Cross-Platform Foundation and Linux Preview**<br>
Branch: `v1.5-cross-platform-foundation`

OpenSorSe 1.5 makes operating-system behavior explicit and replaceable while
preserving the analysis → review → approval → mutation boundary. Product and
informational version are `1.5.0`; assembly, file, and Windows manifest version
are `1.5.0.0`; About displays `1.5`.

## Included

- Small platform contracts for path semantics, application locations, file
  identity, filesystem inspection, tool discovery, capability reporting, and
  desktop integration.
- Existing Windows local-data layout is preserved. Linux separates
  configuration, data, state, cache, diagnostics, and plugins using XDG
  locations.
- Windows volume/file-index identity and Linux x64 device/inode identity, with
  an explicit metadata fallback for other architectures and documented
  lifetime limits.
- Platform-aware, root-confined Change Plan validation; non-overwriting,
  same-filesystem execution; journalled verification; compensating Undo.
- Portable, Windows-compatible, or current-platform recipe filename policy.
  Existing recipes default to the conservative portable policy.
- Plugin runtime-identifier constraints. Native payloads without a declared
  supported runtime identifier fail manifest validation.
- Explicit configured-path and safe `PATH` discovery for external Tesseract.
- Windows and Linux desktop-opening adapters with non-fatal unavailable states.
- Settings platform diagnostics and a copyable, secret-free support report.
- Windows/Ubuntu CI source validation. CI builds and tests source; it does not
  publish packages.

## Support statement

Windows is the locally verified development platform. Linux has implemented
source, XDG, filesystem, watcher, plugin, OCR-discovery, and desktop foundations
and is exercised by the Ubuntu CI definition; it remains a preview until that
workflow and the manual Linux checklist have run successfully in the target
environment. macOS paths are implemented conservatively but the platform is
unverified and is not a supported v1.5 target.

There is no v1.5 installer, updater, tag, package, or published release in this
source task. See the [capability matrix](PLATFORM_COMPATIBILITY_MATRIX.md),
[Linux build guide](LINUX_BUILD_AND_LAUNCH.md), [user guide](USER_GUIDE_v1.5.md),
and [manual checklist](MANUAL_TESTING_v1.5.md).
