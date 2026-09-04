# OmniSorSe v2.13.0-rc — Product Clarity & Workflow

**Status:** Release candidate for final real-world/manual validation before any
v2.13.0 GA claim. This is not a stable release. Windows artifacts are unsigned;
macOS artifacts are not Apple Developer ID-signed and are unnotarized. A
toolchain-provided ad-hoc signature does not identify or authenticate a
publisher.

## Downloads

Use only the assets attached to the canonical
[v2.13.0-rc GitHub prerelease](https://github.com/nishdel/OmniSorSe/releases/tag/v2.13.0-rc):

- `OmniSorSe-v2.13.0-rc-win-x64-setup.exe` — per-user Windows installer;
- `OmniSorSe-v2.13.0-rc-win-x64.zip` — self-contained Windows portable package;
- `OmniSorSe-v2.13.0-rc-macos-x64.dmg` — Intel macOS package;
- `OmniSorSe-v2.13.0-rc-macos-arm64.dmg` — Apple Silicon macOS package;
- `OmniSorSe-v2.13.0-rc-sbom.cdx.json` — CycloneDX 1.6 SBOM;
- `OmniSorSe-v2.13.0-rc-SHA256SUMS.txt` — SHA-256 hashes for the other five
  release files.

Do not treat the files as published until the GitHub release exists and its
exact tagged commit, workflow provenance, metadata, and checksums have been
verified. Checksums detect changed bytes; they do not authenticate an unsigned
publisher.

## What changed

v2.13 restores the product's main path: **Scan → Review → Organize**. Home and
navigation now lead with those tasks. Search, Duplicates, Related Files, library
automation, and graph diagnostics remain available with clearer roles, while
technical controls use progressive disclosure or Settings.

Review Changes identifies the plan's origin, purpose, warnings, and eligible
action count. **Approve all eligible** now states its exact boundary: actions in
Valid or Warning state, with no blocking conflict. It never applies a plan.
Duplicate review keeps one copy per group, supports selecting more than the
five-file shell-open limit, offers a keep-first helper, and identifies its
recovery-based Change Plan explicitly.

Optional AI organization remains visible when AI is disabled or unconfigured
and links directly to AI settings. It does not enable a provider, contact one,
or bypass Change Plan review. Folder proposals have explicit Keep proposal and
Dismiss actions.

Search results receive practical vertical space. Facets, saved searches, and
index maintenance are collapsed until requested. Smart Tags explain whether a
file is unselected, still indexing, genuinely has no retained tags, or has
reviewable evidence, and they can be refreshed explicitly.

Related Files is the everyday evidence-and-correction surface. **Graph
diagnostics** is the advanced derived projection. Unlinking, forgetting,
merging, splitting, or returning a corrected pair to automatic evidence now
requires a target-specific confirmation. These actions change retained
OmniSorSe relationship state, not original files.

Index progress now reports recent throughput from terminal work completed in a
bounded trailing window. ETA appears only after enough recent samples exist, so
a long-running job no longer dilutes the displayed rate merely because of its
age. Default concurrency and resource policies are unchanged; no throughput
increase is claimed without real-library measurement.

## Preserved boundaries

- .NET 10, deep-index schema 6, Explorer Protocol 1.0, `.oms-state` format 2,
  compatibility identifiers, and the local-first architecture are unchanged.
- Reviewed Change Plans remain the only supported production source-file
  mutation path. AI, Search, Smart Tags, relationships, graph diagnostics,
  watchers, recipes, and plugins do not gain direct file-mutation authority.
- Existing saved-search storage/type names, OpenSorSe profile/assembly names,
  installer identity, and bundle identifiers remain where compatibility needs
  them.
- Optional AI stays disabled by default. A configured non-loopback
  Ollama-compatible endpoint is a remote privacy boundary.

## Automated versus manual evidence

Repository tests cover navigation composition, workflow labels, plan context
and bulk eligibility, duplicate selection bounds, disabled-AI discoverability,
Smart Tag states, relationship confirmation, and recent indexing throughput.
Local SDK 10.0.400 qualification passed zero-warning Debug/Release builds,
1,878 tests in each configuration with no skips, focused relevance/performance/
policy checks, formatting/analyzers, an 18-project vulnerability audit, and a
native Windows self-contained package smoke. Publication additionally requires
hosted four-platform and complete native packaging workflows against the exact
tagged commit; the GitHub prerelease records those immutable run and asset
details.

Automation does not validate human-scale visual hierarchy, small-window and
high-DPI layout, wheel/keyboard/focus behavior, screen readers, real-library
relationship/tag quality, optional provider integration, or normal-user
installer prompts. Those checks remain explicit in the
[v2.13 manual checklist](MANUAL_TESTING_v2.13.md).
