# OpenSorSe v1.6 Manual Validation Checklist

## Completion record

The project maintainer confirmed completion of the required interactive manual
smoke testing on 2026-07-28 and reported no release-blocking issues. Detailed
host, architecture, filesystem, assistive-technology, Tesseract, Ollama, and
plugin-version observations were not supplied for this repository record. The
checkboxes below remain the reusable validation procedure rather than a
fabricated per-environment evidence log.

Use disposable folders and copies of files. Do not use irreplaceable data.
Record the operating system, architecture, filesystem, .NET runtime, external
tool versions, exact result, and any diagnostic correlation identifier.

## Windows, Linux, and macOS startup

- [ ] Build and launch the Desktop natively on the target host.
- [ ] Confirm the application icon, theme, scaling, resizing, keyboard focus,
      and text rendering.
- [ ] Confirm About displays `1.6` and assembly/product metadata displays
      `1.6.0`.
- [ ] Confirm the System check reports the correct host, application-data
      locations, path case policy, identity strength, watcher capability,
      desktop integration, packaging state, OCR tools, and limitations.
- [ ] Confirm existing v1.5 application data opens without reset or migration
      prompts.

## Accessibility

- [ ] Navigate primary, Advanced, and footer destinations using only the
      keyboard.
- [ ] With the host screen reader, verify navigation items announce their
      labels.
- [ ] Verify scan, global-operation, watched-folder, Change Plan, Operation
      History, plugin, and notification status changes are announced without
      stealing focus.
- [ ] Verify Scan cancellation and global cancellation expose distinct names.
- [ ] Verify visible focus, high contrast, 200% scaling, and resized layouts on
      critical workflows.

## Persistence and recovery

- [ ] Save Settings, workflows, watched folders, Saved scans, saved searches,
      plugin state, and AI preferences; restart and verify exact reload.
- [ ] Interrupt the process while repeatedly saving application-owned state.
      Confirm the previous or new complete JSON document loads and no partial
      document replaces it.
- [ ] Make an application-data directory temporarily unavailable or read-only.
      Confirm the operation fails visibly, unrelated workflows continue, and
      the previous document remains intact.
- [ ] Supply malformed and oversized copies of each owned store in a disposable
      profile. Confirm documented recovery behavior preserves the invalid file.
- [ ] Confirm no `*.tmp` sibling remains after successful operations.

## Scan, search, and cancellation

- [ ] Scan a large mixed tree and cancel during enumeration, metadata, hashing,
      duplicate detection, and result projection.
- [ ] Confirm cancellation returns promptly, does not publish partial results,
      and the next scan succeeds.
- [ ] Search and re-sort a large completed scan while typing rapidly. Confirm
      the UI remains responsive and stable pages contain at most the selected
      page size.
- [ ] Confirm duplicate groups, counts, ordering, and reclaimable sizes match
      v1.5 behavior.

## Watched folders

- [ ] Start with an existing watched root, then create/modify/rename/delete
      files in bursts. Confirm one normalized batch after the quiet period.
- [ ] Test case-only names on the target filesystem. Confirm behavior follows
      the reported host/filesystem case policy.
- [ ] Pause, modify offline, resume, overflow/reconnect, and run manual full
      reconciliation.
- [ ] Close the application while a debounce and reconciliation are active.
      Confirm shutdown completes without a crash or orphaned watcher.
- [ ] Confirm failures create visible activity/reconciliation state and never
      apply file changes automatically.

## Change Plans, recovery, and Undo

- [ ] Create, edit, approve, validate, and explicitly apply a disposable plan.
- [ ] Cancel before and between safe action boundaries.
- [ ] Simulate stale source, occupied destination, permission failure,
      interruption, rollback, and restart recovery.
- [ ] Confirm Operation Journal records remain complete and human-readable.
- [ ] Confirm Undo succeeds only while identity and dependency checks pass and
      blocks rather than overwriting external changes.

## AI, OCR, workflows, and plugins

- [ ] Confirm AI is disabled by default and each capability remains separately
      gated.
- [ ] Test configured local and intentionally remote Ollama endpoints, timeout,
      cancellation, missing model, malformed response, and recovery.
- [ ] Test native document extraction and optional Tesseract OCR cancellation,
      tool discovery, language availability, temporary cleanup, and bounds.
- [ ] Export/import v1.6 workflows and confirm v1.5 exports remain readable.
- [ ] Inspect, enable, disable, fail, recover, upgrade, and remove a disposable
      plugin. Confirm capability grants and dependency blocks remain enforced.

## Repository handoff

- [ ] Confirm Debug and Release automated reports contain zero failed and zero
      skipped tests.
- [ ] Confirm analyzer and formatting gates are clean.
- [ ] Confirm no generated build, test-result, package, or temporary artifact is
      tracked.
- [ ] Record native Windows, Linux, and macOS results in
      `V1.6_VALIDATION_REPORT.md` before making a release claim.
