# OmniSorSe v2.10 master manual validation matrix

**Status:** all items below are unperformed until a maintainer records the
exact host, build commit, inputs, and observed result. This checklist
deduplicates the outstanding v2.5-v2.9 gates; historical checklists remain as
release records.

## Install, profile, upgrade, and provenance

- [ ] Fresh Windows install and fresh profile.
- [ ] Reuse a published v2.4/schema-5 profile and observe schema-6 migration.
- [ ] Interrupt upgrade/migration and recover from the managed copy.
- [ ] Launch two processes against one profile; second is read-only startup failure and first remains healthy.
- [ ] Kill the owner process; next launch acquires the profile and reports abnormal prior shutdown.
- [ ] Run distinct profiles concurrently where supported.
- [ ] Install, upgrade, uninstall, and verify profile preservation.
- [ ] Verify About, executable, installer/app bundle, package name, build manifest, commit, and checksum agree.
- [ ] Confirm downgrade of newer state fails clearly and does not write.

## Indexing, discovery, and scale

- [ ] Empty library and one-file library.
- [ ] Fast/base indexing reaches Search before Deep work.
- [ ] Deep indexing, cancellation, restart, retry, and source reconciliation.
- [ ] Search ranking, facets, counts, Saved Views, and Saved View snapshot selection.
- [ ] Search to Files and return with query/facets/Saved View preserved.
- [ ] Smart Tag Strong/Moderate/Limited, accept/reject, continuous next/previous review.
- [ ] Real 20k-file library with memory, startup, Search, and health observations.
- [ ] Real 100k-file library where hardware permits; record truthful truncation/bounds.

## Reviewed organization and mutation

- [ ] Select 10 and 100 stable IDs from Files, Search, and a Saved View.
- [ ] Recipe naming-only, destination-only, combined, fallback, privacy warning, edit/re-preview.
- [ ] Missing/ambiguous evidence, Unicode, invalid characters, path limits, case/normalization collision.
- [ ] Review Changes, execute rename/move/directory actions, reconcile, return to discovery.
- [ ] Destination appears after preview; stale source moved/deleted/locked/read-only.
- [ ] Partial failure, rollback success, rollback failure reporting, restart recovery, and Undo conflict.
- [ ] Confirm watched folders and Saved Views never auto-execute a recipe.

## Backup, deletion, corruption, and storage failure

- [ ] Export state, inspect privacy warning and fixed archive contents.
- [ ] Restore into empty and existing profiles; review merge/replace conflicts.
- [ ] Restore recipes, Saved Views, sources, User Tags, accept/reject decisions, and manual relationship decisions; unresolved IDs/pairs are skipped without guessing.
- [ ] Cancel restore and inject/observe a mid-restore failure with pre-restore recovery point.
- [ ] Corrupt/truncate Change Plan and Operation Journal files; mutation blocks and evidence remains.
- [ ] Corrupt settings, Saved Views, recipes, and watched settings; verify documented authority policy.
- [ ] Forget File and Forget Source; inspect SQLite plus content/semantic/thumbnail compatibility stores for absence.
- [ ] Distinguish Clear Generated Intelligence, Forget, Clear Index/Rebuild, and Restore.
- [ ] Low application-data space, full export destination, SQLite write failure, and full Change Plan destination.
- [ ] Locked files, permission denied, read-only application data, and unavailable temp storage.
- [ ] Remove a USB/removable source during scan, preview, Search, and Change Plan; reconnect and reconcile.

## Hostile documents and optional tools

- [ ] Near/over-limit, malformed, high-page-count, and expansion-heavy PDFs.
- [ ] Cancel PDF extraction/OCR and inspect bounded cleanup.
- [ ] Record residual in-process PDFium crash/hang behavior with non-sensitive hostile fixtures.
- [ ] Tesseract installed, absent, disabled, missing language, timeout, and malformed output.
- [ ] ffprobe/ffmpeg installed, absent, disabled, timeout, crash, and oversized media.
- [ ] whisper.cpp installed/configured, absent, disabled, timeout, cancellation, and model mismatch.
- [ ] Ollama disabled/local/remote HTTPS/remote plain HTTP acknowledged/misconfigured/unavailable.
- [ ] Prompt-injection documents produce only bounded review suggestions and never execute or invent IDs.
- [ ] OmniBrille unavailable, launch, one-time handoff, expiry/replay, disconnect, and close lifecycle.

## Health, lifecycle, diagnostics, and recovery

- [ ] Health shows healthy schema/stores/ownership with bounded startup cost.
- [ ] Health shows unreachable source, failed jobs, low space, corrupt journal, and recovery backup.
- [ ] Kill during indexing, migration, Change Plan, restore, and shutdown; verify honest restart state.
- [ ] Shutdown remains responsive while optional providers are slow/uncooperative.
- [ ] Review logs/diagnostics for absence of content, OCR, transcripts, tags, queries, prompts, tokens, and private paths.
- [ ] Follow every operational runbook on a disposable profile.

## Accessibility and layout

- [ ] Keyboard-only Home, Search, Files, Smart Tag review, Organize, Review Changes, Settings health, export, and restore.
- [ ] Screen-reader names/states/live announcements for health, backup preview, conflicts, recovery, and existing workflows.
- [ ] Focus after Search-to-Files return, review decision, async refresh, recipe re-preview, and restore preview.
- [ ] Compact-window scrolling with no clipped critical action.
- [ ] Windows display scaling at 100%, 125%, and 150%, including tracked layout scenarios #29 and #31.

## Platforms and packaging

- [ ] Native Windows x64 runtime smoke and optional tools.
- [ ] Native macOS Intel runtime, case/Unicode filesystem, package, Gatekeeper/signing state, and optional tools.
- [ ] Native macOS ARM64 runtime, package, Gatekeeper/signing state, and optional tools.
- [ ] Native Linux source build/runtime, permissions, case/symlink behavior, desktop integration, and optional tools.
- [ ] Confirm cross-target compilation is not recorded as native execution.
- [ ] Validate unsigned/notarized status accurately; validate signatures when maintainer credentials become available.
