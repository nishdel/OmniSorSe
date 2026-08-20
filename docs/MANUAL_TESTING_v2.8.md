# OmniSorSe v2.8 manual testing

**Status:** unreleased validation checklist

Automated coverage validates contracts and state transitions. The unchecked
desktop, screen-reader, DPI, optional-tool, and real file-operation scenarios
below are not claimed passes.

## Home

- [ ] Start with a disposable profile containing no sources; confirm Home
  explains how to begin and Find remains available.
- [ ] Restart with an existing durable index; confirm known files, source count,
  base-Search readiness, deeper-analysis state, review count, and Saved View
  count appear without a new in-session scan.
- [ ] While Fast indexing continues, confirm Home distinguishes base Search
  ready from deeper analysis/classification still running.
- [ ] Confirm recent Saved View shortcuts do not execute until opened.
- [ ] Exercise Ready, Disabled, Not configured, Unavailable, and Needs attention
  optional-capability states where practical.
- [ ] Confirm Home refresh does not contact Ollama, execute tools, launch
  OmniBrille, or noticeably block startup.

## Find and Search to Files

- [ ] Run a free-text query with multiple facets and a Saved View, select a
  result, and choose **Open in Files**.
- [ ] Confirm Files selects the same stable logical file and shows its current path.
- [ ] Choose **Return to discovery** and confirm query, facets, Saved View,
  unresolved-review mode, result context, and keyboard focus remain coherent.
- [ ] Rename/move the file through a disposable reviewed Change Plan between
  Search and Files; confirm stable identity follows the current path.
- [ ] Delete/move a disposable source file externally during the transition;
  confirm a bounded unavailable/missing warning and no stale-path launch.
- [ ] Confirm Search has only one Theme, Document Type, and User Tag filter
  surface and active chips/counts stay synchronized.

## Continuous Smart Tag review

- [ ] Open unresolved Moderate suggestions from Home or Search.
- [ ] Inspect type, label, Moderate state, and no more than the bounded evidence reasons.
- [ ] Keep one suggestion; confirm it leaves unresolved review and the next
  current item opens.
- [ ] Dismiss one suggestion; confirm rejection persists and it does not return
  after ordinary reindex.
- [ ] Use previous/next and Return to discovery with keyboard only.
- [ ] Remove or reclassify an item while review is active; confirm stale items
  are skipped gracefully and no decision is applied to another file.

## Understand and Organize

- [ ] Use Home **Understand** with and without an existing Files context; confirm
  the prerequisite is clear and no fake recent item is invented.
- [ ] Generate a rename proposal for a file with accepted and Strong
  classification evidence; confirm the allowed evidence and authority are shown.
- [ ] Confirm unresolved Moderate, Limited, and rejected evidence is not used.
- [ ] Edit/reject the proposal and confirm no source mutation occurs.
- [ ] Create and explicitly apply a disposable Change Plan, inspect reconciled
  Files/Search state, Undo it, and return to the original discovery context.

## Optional capabilities

- [ ] Validate Ollama, Tesseract, ffprobe, ffmpeg, whisper.cpp, and OmniBrille
  when installed, absent, disabled, and intentionally misconfigured where safe.
- [ ] Confirm each compact explanation describes the missing capability's effect
  without describing an optional absence as an application failure.
- [ ] Confirm settings/help links reach the existing configuration surface and
  no auto-install or network action occurs.

## Accessibility, DPI, and scrolling

- [ ] Complete Home tasks, Saved View shortcuts, Search to Files, return,
  review previous/next, Keep/Dismiss, and organization evidence using keyboard only.
- [ ] With a Windows screen reader, verify names, selected/current states,
  readiness updates, counts, capability states, and review-position changes.
- [ ] Repeat Home, Search facets, Files review, and evidence disclosure at 100%,
  125%, and 150% DPI in compact and normal windows.
- [ ] Exercise mouse wheel, scrollbar drag, Page Up/Page Down, Tab, and Shift+Tab.
- [ ] Confirm focus remains visible and stable after asynchronous Home/count updates.
- [ ] Recheck issue #29 Virtual Collections and #31 Related Files separately;
  this release does not claim those matrices complete when their code is unchanged.

## Privacy, recovery, and platforms

- [ ] Inspect ordinary/exported diagnostics for absence of raw query text, User
  Tags, Saved View contents, evidence excerpts, optional-tool secrets, and private paths.
- [ ] Verify Clear Index, Forget, recovery guidance, Operation History, and Undo
  wording distinguish index data from source-file changes.
- [ ] Perform native Windows runtime validation.
- [ ] Perform native Linux runtime validation.
- [ ] Perform native macOS x64 runtime validation.
- [ ] Perform native macOS arm64 runtime validation.
