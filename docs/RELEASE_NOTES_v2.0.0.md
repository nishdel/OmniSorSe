# OpenSorSe v2.0.0

OpenSorSe is a local-first desktop application for scanning, searching,
understanding, and safely organizing folders that you explicitly select. It is
not an autonomous file manager: analysis, Search, OCR, AI, relationships,
collections, and the Knowledge Graph do not authorize file changes.

## Highlights since the previous integrated `main`

- **Deep Indexing** processes large folders through durable, resumable stages
  with progress, pause, cancellation, retry, storage quotas, cleanup, and
  partial Search coverage.
- **Search** combines exact filenames, paths, metadata, retained document/OCR
  text, tags, summaries, keywords, and optional related-concept evidence. Exact
  and literal matches remain strongest, filters are visible, snippets are
  bounded, and “Why this result?” uses the ranking evidence that actually ran.
- **Indexed-data privacy controls** show what OpenSorSe retained and can forget
  or selectively rebuild generated data without deleting original files.
- **Relationships and Smart Collections** group related files virtually from
  deterministic evidence. User links, rejections, exclusions, and corrections
  persist; collections never move files.
- **Knowledge Graph** is an optional, default-off projection over stable indexed
  evidence. It uses conservative identity rules, completed manifests,
  generation fencing, bounded one-hop browsing, privacy/repair controls, and
  optional explainable Search context.
- **File safety** remains centered on persisted Change Plans, an explicit Apply
  confirmation, immediate preflight, the Operation Journal, verification,
  rollback, recovery, and conflict-aware Undo.
- **Reliability and portability** include atomic stores, cancellation and
  recovery hardening, provider-isolated SQLite, platform capability gating,
  three-host CI, and native runtime builds.

## Install

Download artifacts only from the official
[v2.0.0 GitHub Release](https://github.com/nishdel/OpenSorSe/releases/tag/v2.0.0).

### Windows x64

- `OpenSorSe-v2.0.0-win-x64-setup.exe` installs per-user, creates a Start Menu
  shortcut, registers an uninstaller, and preserves application data when the
  program is uninstalled.
- `OpenSorSe-v2.0.0-win-x64.zip` is the self-contained portable distribution.
  Extract the complete ZIP and run `OpenSorSe.exe`; keep all extracted files
  together.

The v2.0.0 Windows executable and installer are unsigned unless the GitHub
Release explicitly says otherwise. Windows SmartScreen may warn that the
publisher is unrecognized. A SHA-256 checksum verifies bytes but does not
authenticate an unsigned publisher.

### macOS

- Intel: `OpenSorSe-v2.0.0-macos-x64.dmg`
- Apple Silicon: `OpenSorSe-v2.0.0-macos-arm64.dmg`

Open the matching disk image and copy `OpenSorSe.app` to Applications. The
v2.0.0 macOS packages are unsigned and unnotarized unless the GitHub Release
explicitly says otherwise, so Gatekeeper may require an explicit reviewed
override. macOS package startup and non-mutating functionality are validated;
source-file mutation remains disabled where platform capability policy cannot
prove equivalent safety.

### Linux

No `.deb`, `.rpm`, AppImage, Flatpak, or Snap is published for v2.0.0. Linux
x64 remains available through the documented source-build preview. Follow
[Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md).

## Verify downloads

Download `OpenSorSe-v2.0.0-SHA256SUMS.txt` from the same GitHub Release.

Windows PowerShell:

```powershell
(Get-FileHash .\OpenSorSe-v2.0.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
```

macOS or Linux:

```bash
shasum -a 256 OpenSorSe-v2.0.0-macos-arm64.dmg
```

Compare the complete value with the corresponding checksum-file line.

## Optional local dependencies

- Ordinary Search, filtering, explanations, snippets, indexing, relationships,
  and Knowledge Graph browsing do not require Ollama.
- Optional AI is disabled by default. OpenSorSe does not install or start an
  Ollama-compatible service. A custom endpoint can be remote and is therefore a
  real privacy boundary.
- Tesseract 5 and language data are installed separately and are required only
  for enabled OCR recognition. Native text extraction remains available
  independently.

## Privacy and local data

The index can contain paths, metadata, retained document/OCR text, tags,
summaries, keywords, semantic representations, relationship evidence, graph
facts, user decisions, operational history, and diagnostics. Treat application
data as sensitive. It is not claimed to be encrypted by OpenSorSe.

Ordinary diagnostics avoid complete queries, document paragraphs, OCR text,
summaries, vectors, prompts, secrets, and unnecessary absolute paths. Review
any explicitly exported diagnostic before sharing it. See
[Safety and Privacy](SAFETY_AND_PRIVACY.md) and [v2.0 Security Notes](SECURITY_v2.0.md).

## Compatibility and updates

v2.0.0 preserves existing saved scans, catalogs, watched folders, workflows,
plugins, Change Plans, journal/recovery records, Undo behavior, the schema-3
deep index, and v1.9 relationship data. Knowledge Graph data uses separate
schema-1 derived and decision sidecars so rollback can ignore it. Back up
important application-owned data before upgrading and do not overwrite a
running installation.

## Known limitations

- Knowledge Graph is optional, disabled by default, conservative, bounded, and
  not a conversational assistant or general-purpose knowledge graph.
- Relationship and graph context can be incomplete while indexing, projection,
  exclusions, dependencies, repair, or quotas limit coverage.
- External plugins run in-process with current-user permissions. Integrity and
  load-context isolation are not a security sandbox or publisher identity.
- Optional AI quality depends on the separately managed provider and model.
- Windows signing and Apple signing/notarization are not available through the
  current project infrastructure unless the release page explicitly records a
  verified signature.
- Linux has no binary installer for this release, and macOS source-file mutation
  remains capability-gated.

## Validation and community testing

The release source passed the repository’s automated restore, zero-warning
Debug/Release build, complete tests with zero failures/skips, analyzers,
policies, vulnerability audit, Search/relationship/Knowledge Graph/performance
regressions, four runtime-target builds, native package inspection, and Windows,
Ubuntu, and macOS CI before publication. Exact totals and commits are recorded
in the [v2.0 Validation Report](V2.0_VALIDATION_REPORT.md) and
[Release Status](RELEASE_STATUS.md).

Broad interactive and community validation begins with this publication; it is
not claimed to have happened already. Real-world defects reported by testers
will be triaged normally and may be corrected in v2.0.x patches or later
releases.

## Report a problem

Use the [GitHub issue tracker](https://github.com/nishdel/OpenSorSe/issues).
Include the exact version, operating system, operation, expected and observed
result, and reviewed redacted diagnostics where useful. Never attach private
documents, full index databases, raw OCR/document text, secrets, tokens, or an
unreviewed diagnostics bundle.
