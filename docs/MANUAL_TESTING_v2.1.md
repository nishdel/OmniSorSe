# OpenSorSe v2.1.0 manual validation checklist

This is an evidence tracker, not a claim of completed testing. Every scenario
is intentionally unchecked. Use only synthetic/disposable files and record the
host OS, filesystem, Ollama version/model, OCR version, and application commit
when a maintainer performs a scenario.

## Search and results

- [ ] Exact complete filename ranks first over weak content or related evidence.
- [ ] Exact filename stem, prefix, substring, separator, casing, punctuation,
      diacritic, and adjacent-transposition behavior is relevant and bounded.
- [ ] **Why this result?** agrees with the actual filename/path/metadata/text/OCR
      evidence and the snippet is bounded and source-labelled.
- [ ] Hybrid Search remains responsive during active and incomplete indexing.
- [ ] Incomplete coverage is visible and no-result wording is non-definitive.
- [ ] Copy full path, Open file, Open folder, details, and Change Plan handoff are
      keyboard accessible and work on the host platform.
- [ ] Search cancellation and rapid overlapping queries leave one coherent result.

## Ollama and optional AI

- [ ] AI settings, endpoint, model discovery, runtime state, timeout, and Search
      assistance are discoverable without unrelated Advanced settings.
- [ ] Localhost/IPv4-loopback/IPv6-loopback endpoints display **Local endpoint**.
- [ ] A non-loopback endpoint displays the remote privacy warning before use.
- [ ] Installed models appear deterministically and a missing saved model falls
      back with an explicit Save instruction.
- [ ] Search works normally with Ollama stopped or not installed.
- [ ] Ollama stopped mid-request, timeout, malformed output, missing model, and
      cancellation preserve deterministic results without freezing the UI.
- [ ] AI-assisted Search cannot introduce an unknown file or promote weak evidence
      above an exact filename.

## Manual-validation fixes

- [ ] Scan elapsed time updates throughout a long scan and freezes at the truthful
      completion duration.
- [ ] Failed and cancelled scans freeze their duration; a pre-start validation
      failure does not reuse a previous scan's duration.
- [ ] Duplicate review shows every relevant path before removal planning.
- [ ] Selecting all known copies disables safe removal; leaving at least one copy
      enables a reviewable Change Plan.
- [ ] Applying duplicate safe removal moves only selected copies into
      `.opensorse/duplicate-recovery`, updates the duplicate review, and records
      Operation History.
- [ ] Undo restores a safely removed copy when conflict checks still pass.
- [ ] The notification badge opens/closes by click and keyboard; Escape closes it;
      individual dismissal and Clear all do not erase Advanced Diagnostics.
- [ ] File analysis wording states local indexing needs no AI; AI assistance and
      endpoint privacy are separate.
- [ ] Related Files explains relationships in ordinary language and every visible
      relationship still has inspectable evidence.
- [ ] Search shows Hybrid / Hybrid + AI assistance without implying Ollama performs
      the file search.

## Contextual Help and legacy workflows

- [ ] Contextual `?` opens the correct section for Search, Duplicates, Related
      Files, Watched Folders, Workflows, Settings, and AI/Ollama.
- [ ] Help covers Getting Started, Scan, Results, Organize/Change Plans,
      Diagnostics, Privacy, and Troubleshooting with current labels.
- [ ] Select a synthetic file, request an AI rename, review/edit the suggestion,
      create and validate the Change Plan, execute it, and confirm Results refresh.
- [ ] Preview a disposable folder restructure, review every move, explicitly
      confirm, execute, and verify history/repeat protection.
- [ ] Stop Ollama during an AI folder suggestion and verify actionable failure
      without any file change.

## Packaging and accessibility

- [ ] Windows installer install/start/stop/uninstall behavior matches policy.
- [ ] Windows portable package starts without a separately installed .NET runtime.
- [ ] Intel and Apple Silicon DMGs expose correct app metadata/architecture on
      their native hosts.
- [ ] Keyboard focus order, accessible names/live regions, high contrast, scaling,
      and screen-reader output are usable on a representative host.
- [ ] No screenshot, diagnostic export, or bug report includes private filenames,
      paths, content, prompts, tokens, or credentials without explicit review.
