# OmniSorSe v2.5 manual testing

**Status:** unreleased implementation evidence tracker

This checklist separates automated evidence from genuinely interactive work.
An unchecked item is not a failed automated test; it means that exact manual
scenario has not been performed on this branch.

## Automated evidence

- [x] Clean v2.4 baseline restored and built in Debug and Release with zero
  warnings/errors.
- [x] Baseline Debug and Release suites each passed 1,671 tests with zero
  failures/skips.
- [x] Outcome-driven reconciliation tests cover successful rename, move/folder
  restructure, rollback, partial rollback, missing results, and Undo.
- [x] Results projection test verifies path replacement and file-ID selection
  retention.
- [x] A partial execution result reaches the shell reconciliation event.
- [x] Progressive SQLite claim-order tests distinguish broad base-first from
  per-file deep-initial scheduling.
- [x] Search coverage becomes available after discovery while deeper jobs remain
  incomplete.
- [x] An affected-source refresh reuses compatible completed stages.
- [x] Scan-depth configuration default, persistence, manual-scan capture, Help,
  and accessibility labels have automated coverage.
- [x] Optional companion discovery leaves the Explorer host dormant when
  OmniBrille is absent and preserves enabled indexed-source scope when present.
- [x] A genuine separate child test process receives the established one-time
  current-user handoff, authenticates, negotiates Explorer Protocol v1, exits,
  and causes session revocation.
- [x] Bootstrap tests cover independent repeated grants, expiry/timeout,
  incompatible/rejected/invalid responses, strict bounded JSON, missing or
  misconfigured executable paths, process-start failure, and no-source denial.
- [x] Final Debug and Release suites each passed 1,702 tests with zero
  failures/skips. Focused validation passed for Explorer Protocol and companion
  handoff (47/47), workflow/file operations (77/77), Search/index/media
  (262/262), performance (15/15), and documentation/policy (8/8). The
  vulnerability audit reported no findings, and Release cross-target builds
  passed for win-x64, linux-x64, osx-x64, and osx-arm64.

## Controlled Change Plan workflow

- [ ] Rename a disposable file through Review Changes and confirm Files shows
  only the new path immediately.
- [ ] Search before/after the rename and confirm the old path stops appearing.
- [ ] Move disposable files through a folder-structure proposal and confirm
  Files, details, duplicates, and selection converge.
- [ ] Undo the operation and confirm restored Files/Search state.
- [ ] Force a safe controlled execution failure and inspect mixed-state warning
  and affected-source refresh.
- [ ] Force a controlled rollback failure; confirm the UI reports uncertainty
  rather than full success.
- [ ] Confirm Operation History and source files agree after each case.

## Progressive indexing

- [ ] Select **Fast — searchable first** for a mixed document/media source.
- [ ] Confirm names/paths become searchable while deeper media analysis is still
  reported as continuing.
- [ ] Confirm later OCR/media/content evidence updates the existing result and
  does not add a duplicate.
- [ ] Exit after base coverage but before deeper completion; restart and confirm
  base Search remains available and pending jobs resume.
- [ ] Pause/resume and cancel/retry during deeper analysis.
- [ ] Repeat with **Deep initial analysis** and confirm per-file deeper progress
  is favored without changing enabled capabilities.
- [ ] Confirm missing optional tools produce waiting/unavailable behavior without
  blocking base Search.

## Issue #29 — Virtual Collections scrolling

- [ ] Normal Windows window: mouse wheel and scrollbar.
- [ ] Small/narrow window: all collections and details reachable.
- [ ] Keyboard focus, Tab/Shift+Tab, Page Up/Page Down.
- [ ] 100% DPI.
- [ ] 125% DPI.
- [ ] 150% DPI.

No v2.5 layout change should be made unless this original scenario is genuinely
reproduced.

## Issue #31 — Related Files scrolling

- [ ] Normal Windows window: mouse wheel and scrollbar.
- [ ] Small/narrow window: all controls reachable without competing scroll.
- [ ] Keyboard focus, Tab/Shift+Tab, Page Up/Page Down.
- [ ] 100% DPI.
- [ ] 125% DPI.
- [ ] 150% DPI.

No v2.5 layout change should be made unless this original scenario is genuinely
reproduced.

## OmniBrille companion handoff

- [x] Test process: actual separate-process bootstrap and Protocol v1
  negotiation over local pipes.
- [x] Automated: host remains dormant until an explicit launch with an
  available companion and enabled indexed source.
- [x] Automated: authorized scope contains enabled indexed sources only and
  omits raw paths.
- [x] Automated: launch material is delivered through a random one-time
  current-user named pipe, not a durable file, environment value, or
  bearer-token command line.
- [x] Automated: strict 4-KiB frames, 15-second acknowledgement bound,
  independent repeated launches, and session revocation on companion exit.
- [x] Automated: missing/misconfigured executable and bootstrap/auth/version
  failures are isolated from normal OmniSorSe operation.
- [ ] Maintainer desktop: use **Open in OmniBrille** with the real installed
  OmniBrille Stage 4 client and verify Search, Related, neighborhoods, and
  details after the normal-user handoff.
- [ ] Maintainer desktop: close OmniBrille normally and confirm OmniSorSe stays
  open; then close OmniSorSe while connected and confirm OmniBrille reports the
  ended session normally.
- [ ] Maintainer desktop: invoke the action twice and confirm two independent
  companion sessions or windows according to OmniBrille's current behavior.
- [ ] Native Linux and macOS companion bootstrap/runtime checks. Cross-target
  compilation is not native execution evidence.

## Accessibility and UX

- [ ] Scan-depth options use plain language and expose an accessible name.
- [ ] Index status clearly distinguishes searchable base coverage from deeper
  analysis.
- [ ] Files organization card makes Suggest → Review Changes → execute clear.
- [ ] Folder suggestions disclose their bounded current-page scope.
- [ ] Review Changes mixed-outcome notification is visible and understandable.
- [ ] Screen-reader smoke test (not implied by keyboard/source checks).

## Platform boundary

- [ ] Native Windows interactive scenarios above.
- [ ] Native Linux interaction.
- [ ] Native macOS x64 interaction.
- [ ] Native macOS arm64 interaction.

Cross-target compilation does not mark native interaction complete.
