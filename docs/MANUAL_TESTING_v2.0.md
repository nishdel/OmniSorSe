# OpenSorSe v2.0 manual validation checklist

**Status:** Design template. Every scenario is intentionally unchecked and no
manual outcome is claimed.

Record the exact commit, OS/runtime, filesystem, application-data location,
graph/index settings, dependencies, test source, observed result, diagnostics,
and cleanup for each scenario. Use synthetic/reviewed data; never infer an
Ollama, OCR, accessibility, battery, lock, low-disk, or performance result from
automation.

## Installation, enablement, and compatibility

- [ ] Upgrade a representative v1.9 profile and confirm existing Search,
      Relationships, Collections, sources, and settings before enabling graph.
- [ ] Review the graph privacy/storage explanation and decline enablement;
      confirm v1.9 behavior remains usable.
- [ ] Enable graph and observe bootstrap state, stage, progress, coverage,
      current safe item, counts, storage, and estimated time where meaningful.
- [ ] Close/reopen with graph disabled, partially built, and complete.
- [ ] Disable an already-built graph and confirm graph work/query stops while
      disclosed retained data and ordinary v1.9 features remain available.
- [ ] Run the reviewed v1.9 rollback procedure and confirm it ignores/preserves
      graph sidecars.
- [ ] While rolled back, change and remove representative v1.9 relationship and
      Smart Collection decisions; return to v2 and confirm the non-authoritative
      graph projection reconciles without resurrecting or duplicating them.

## Lifecycle, interruption, and recovery

- [ ] Pause and resume bootstrap/projection.
- [ ] Restart while paused and confirm it remains paused.
- [ ] Cancel and explicitly retry.
- [ ] Force application termination during observation capture.
- [ ] Force application termination during candidate extraction.
- [ ] Force application termination during identity resolution.
- [ ] Force application termination during edge publication.
- [ ] Force application termination during maintenance/cleanup.
- [ ] Restart after each interruption and inspect resume/recovery facts.
- [ ] Repeatedly pause/resume and cancel/retry without duplicate or stale work.
- [ ] Exit during active graph work and confirm shutdown remains responsive.

## Graph quality, explanations, and control

- [ ] Inspect File, Source, Folder, Collection, Document Set, and manual entity
      list/detail behavior where applicable; confirm accepted Tag nodes are not
      presented as stable without the separately approved tag contract.
- [ ] Confirm direct edges show relationship, evidence, confidence level,
      algorithm/version, freshness, and origin.
- [ ] Confirm an ambiguous similar-name case remains separate.
- [ ] Create and rename a manual entity.
- [ ] Link and unlink a manual edge.
- [ ] Merge compatible manual/experimental entity candidates and verify the
      explanation/control state.
- [ ] Split entities and verify they remain separate after restart/rebuild.
- [ ] Attempt to merge incompatible mechanical node kinds and confirm the action
      is unavailable or rejected without changing either identity.
- [ ] Confirm existing Same Project/Purchase/Trip/Topic relationships and
      Collection context are not presented as resolved real-world entities.
- [ ] Reject/ignore a suggestion and confirm it does not immediately return.
- [ ] Sort/filter/page related items and inspect bounded limit messages.
- [ ] Exercise a cyclic/high-degree synthetic graph and confirm navigation is
      bounded and responsive.
- [ ] Enable any experimental two-hop/suggestion feature only after reviewing
      its label and opt-in warning; confirm it can be disabled.

## Search and progressive coverage

- [ ] Search exact filenames with graph disabled, building, paused, stale,
      under maintenance, complete, and unavailable.
- [ ] Confirm exact/literal results remain above graph-only context.
- [ ] Inspect “Why this result?” for a graph-expanded result and verify the
      actual edge/evidence supports it.
- [ ] Disable graph Search expansion and confirm ordinary v1.9 results remain.
- [ ] Toggle direct v1.9 relationship context and graph context independently;
      confirm disabling graph context does not disable or alter the existing
      relationship-context behavior.
- [ ] Use a result reachable through both v1.9 relationship context and its
      graph projection; confirm one result, one contextual score contribution,
      one truthful explanation, and the combined expansion limit.
- [ ] Search during graph publication/repair/cleanup and confirm no malformed
      partial result appears.
- [ ] Inspect indexing coverage and graph-projection coverage independently for
      incomplete/stale/failed/excluded/dependency cases; confirm neither layer
      is presented as the other.
- [ ] Confirm a no-result state does not imply exhaustive graph coverage while
      projection is incomplete.

## File/index changes and selective repair

- [ ] Rename a synthetic file and confirm compatible identity/decisions follow.
- [ ] Move a synthetic file within a source.
- [ ] Move a synthetic file across sources and inspect ambiguity/ownership.
- [ ] Modify metadata only and inspect targeted invalidation.
- [ ] Modify content and inspect targeted graph invalidation/reprojection.
- [ ] Delete a file and confirm stale graph/Search visibility is removed.
- [ ] Remove a source and confirm completed reconciliation cleans its graph.
- [ ] Verify/repair one edge.
- [ ] Verify/repair one file/entity/collection/source.
- [ ] Rebuild a selected component while prior valid graph remains usable.
- [ ] Perform the reviewed full derived-graph rebuild last-resort scenario and
      confirm all manual decisions survive.

## Privacy and original-file safety

- [ ] Inspect stored graph data, evidence references, decisions, history, and
      storage use for a selected item/source.
- [ ] Forget graph data for a file and confirm the original remains unchanged.
- [ ] Forget graph data for a source and confirm original files/source
      registration remain unchanged.
- [ ] Exclude a folder/file type and confirm it cannot leak through stale graph
      Search while cleanup is pending.
- [ ] Review decision/graph database locations, retention, backup, and recovery
      explanations, including that sidecars, decisions, aliases, labels,
      evidence references, backups, and quarantine copies are sensitive local
      metadata and are not claimed to be encrypted.
- [ ] Clear derived graph data while retaining graph-native decisions, then
      rebuild and confirm those decisions are reapplied.
- [ ] Clear graph-native decisions and their backups through the separate
      irreversible confirmation; confirm original files and v1.9 decisions are
      unchanged and graph processing does not restart until explicitly enabled.
- [ ] Complete a forget action, exercise the reviewed backup-restore path, and
      confirm an older backup cannot resurrect forgotten active graph data;
      inspect the disclosed minimum tombstone retention.
- [ ] On a disposable profile, make the decision store unavailable/corrupt and
      confirm graph reads and Search expansion fail closed while ordinary v1.9
      Search remains usable.
- [ ] Review a diagnostics export before sharing and confirm private content,
      aliases, full paths, queries, OCR, summaries, prompts, vectors, and secrets
      are absent by default.
- [ ] Compare checksums/timestamps of source fixtures before/after all graph,
      forget, merge/split, repair, and rebuild actions.

## Dependency and resource failure

- [ ] Use stable graph/Search with Ollama unavailable.
- [ ] Restore Ollama and confirm only eligible optional work resumes.
- [ ] Use stable graph/Search with OCR unavailable.
- [ ] Restore OCR and inspect partial coverage recovery.
- [ ] Hold the graph database with a second process and inspect bounded
      busy/locked behavior and fallback.
- [ ] Exercise the reviewed low-disk/quota scenario and inspect
      Waiting-for-resources behavior without silent deletion.
- [ ] Restore disk/resources and resume safely.
- [ ] Run Search while graph maintenance/compaction is active.

## Accessibility and responsiveness

- [ ] Navigate Graph overview/list/detail/evidence/privacy/repair using keyboard
      only with logical focus and visible focus state.
- [ ] Activate every explanation/help/control without hover.
- [ ] Verify meaningful screen-reader names, roles, states, progress, errors,
      stale/partial coverage, and confirmation dialogs on a supported host.
- [ ] Verify mouse and touch/click targets where supported.
- [ ] Verify progress/live announcements are useful and not repetitive.
- [ ] After paging, async refresh, merge, split, forget, repair, and removal,
      confirm selection/focus remains on the valid item or moves to the
      documented logical target without an older response replacing it.
- [ ] Verify high-contrast themes and supported text scaling preserve all
      content, controls, focus indicators, and bounded layouts.
- [ ] Confirm confidence, freshness, failures, and limits remain understandable
      without color, and every destructive index-only confirmation states that
      original files are unaffected.
- [ ] Page/filter a large synthetic graph without UI/accessibility-tree hang.
- [ ] Cancel long work and repeated Search promptly while the UI remains usable.

## Existing-feature smoke tests

- [ ] Existing indexing pause/resume/cancel/recovery still works.
- [ ] Existing Search and v1.9 contextual expansion still work.
- [ ] Existing Relationships/Collections/manual overrides still work.
- [ ] Watched-folder ownership/isolation still works.
- [ ] Catalogs, saved scans, and duplicate detection still work.
- [ ] Workflows and plugins still operate within existing authority.
- [ ] Create/review a Change Plan without graph side effects.
- [ ] Execute the approved synthetic Change Plan through the journal and Undo;
      confirm graph only observes the committed result.

## Completion record

- [ ] Every completed item includes exact host/dependency evidence.
- [ ] All failures are classified and release blockers are resolved/retested.
- [ ] Test data, graph/index copies, diagnostics, and temporary backups are
      reviewed and safely cleaned up.
- [ ] No scenario is marked complete based solely on automated validation.
