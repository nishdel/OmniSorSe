# OpenSorSe v2.1.0 — Search & AI Quality

OpenSorSe v2.1.0 is a focused quality release over the v2.0 local-first
architecture. Deterministic indexed Search remains authoritative and fully
usable without Ollama. No Search/index database schema or migration changes are
introduced.

## Downloads

Use the assets on the official
[v2.1.0 GitHub Release](https://github.com/nishdel/OmniSorSe/releases/tag/v2.1.0):

- `OpenSorSe-v2.1.0-win-x64-setup.exe` — per-user Windows installer;
- `OpenSorSe-v2.1.0-win-x64.zip` — self-contained Windows portable package;
- `OpenSorSe-v2.1.0-macos-x64.dmg` — Intel macOS package;
- `OpenSorSe-v2.1.0-macos-arm64.dmg` — Apple Silicon macOS package;
- `OpenSorSe-v2.1.0-SHA256SUMS.txt` — verified SHA-256 checksums.

No Linux installer is published. Linux x64 remains validated as a source-build
target; see [Linux Build and Launch](LINUX_BUILD_AND_LAUNCH.md).

## Search & AI Quality

- Exact complete filenames and exact filename stems rank above prefix,
  substring, ordinary field, fuzzy, relationship, and semantic-only evidence.
- Case, punctuation, diacritics, spaces, underscores, hyphens, dots, path
  separators, partial filenames, and one adjacent-letter transposition use
  bounded deterministic normalization and matching.
- **Why this result?**, source-labelled bounded snippets, and **Copy full path**
  make matches and common next actions clearer.
- Optional AI assistance reranks at most 12 files already returned by Search,
  cannot add file identities, cannot cross deterministic relevance tiers, and
  falls back to the original order on any invalid output or provider failure.
- Ollama installed-model discovery is deterministic. Provider-confirmed running
  models are distinguished where `/api/ps` is available; OpenSorSe does not
  invent a loading state.
- Missing models, refused connections, malformed responses, model-load errors,
  timeout, and cancellation are actionable states. Search remains available.
- Prompts are compact and suitable for small local models. Absolute paths,
  complete documents, the whole index, and raw vectors are not sent by Search
  assistance.

## Manual-validation corrections

- Scan elapsed time now measures the actual operation with a monotonic clock,
  updates while scanning, and freezes truthfully at completion, failure, or
  cancellation.
- Duplicate review can create a safe-removal Change Plan for selected unwanted
  copies while requiring a known keeper. Confirmed moves go to the excluded
  `.opensorse/duplicate-recovery` area, are journalled, and can be undone while
  conflict checks pass. They are not permanent deletion and do not immediately
  reclaim disk space.
- Errors, warnings, and information use a compact dismissible badge/drawer.
  Dismissal never deletes Advanced Diagnostics evidence.
- Settings distinguish local file analysis/indexing from optional AI. Verified
  loopback endpoints are labelled local; other valid endpoints show an explicit
  remote privacy warning.
- The ordinary UI calls the evidence-backed Knowledge Graph experience
  **Related Files** and explains it without requiring graph terminology.
- Search explicitly reports **Hybrid** or **Hybrid + AI assistance** and states
  that Ollama does not perform the underlying file search.
- Help now covers Getting Started, Scan, Results, Duplicates, Search, Related
  Files, Change Plans, Watched Folders, Workflows, AI/Ollama, Settings,
  Diagnostics, Privacy, and Troubleshooting, with contextual `?` routing.
- The selected-file AI rename and folder-restructure workflows remain behind
  review, confirmation, validation, execution, history, and recovery tests.

## Privacy and safety

Scanning, indexing, OCR, Search, relationships, Related Files, Smart
Collections, and AI suggestions do not modify original files. Only a reviewed,
approved, validated, separately confirmed Change Plan reaches the executor.

Ollama is optional. `localhost`, IPv4 loopback, and IPv6 loopback endpoints are
identified as local. Any other endpoint is treated as remote: bounded data
supplied to an explicit AI request may leave the computer. OpenSorSe adds no
cloud service, account, telemetry, or remote database.

## Quality evidence

The complete Debug and Release automated suites each pass **1,531 tests with
zero failures and zero skips**. Analyzers, formatting,
documentation/dependency/architecture policies, vulnerability audit, Search
relevance and performance regressions, and win-x64/linux-x64/osx-x64/osx-arm64
builds are release gates. Hosted validation runs on Windows, Ubuntu, and macOS.
Native package workflows inspect and smoke-test their platform artifacts.

Automated and packaged smoke validation is not a claim of broad interactive
testing across every computer, filesystem, Ollama model, OCR installation, or
accessibility technology. Real-world findings are handled as normal maintenance
work.

## Trust and known limitations

- Windows and macOS packages are unsigned, and macOS packages are unnotarized,
  unless the GitHub Release explicitly records a verified signature change.
  SmartScreen or Gatekeeper may warn.
- Checksums detect changed bytes; they do not authenticate an unsigned publisher.
- AI reranking does not answer questions, discover files, or create results.
- Ollama does not expose a reliable general loading state through the endpoints
  used, so only installed and provider-confirmed running states are shown.
- Typo matching is conservative and is not language-specific spell checking.
- Safe duplicate removal uses recovery staging rather than OS trash or permanent
  deletion; disk space is not reclaimed immediately.

See [Installation](INSTALLATION.md),
[Search and AI Quality](SEARCH_AND_AI_QUALITY_v2.1.md), and
[Safety and Privacy](SAFETY_AND_PRIVACY.md).
